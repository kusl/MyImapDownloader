using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Thread-safe JSON Lines file writer with size-based rotation and periodic flushing.
/// </summary>
public sealed class JsonTelemetryFileWriter : IDisposable
{
    private readonly string _directory;
    private readonly string _prefix;
    private readonly long _maxFileSize;
    private readonly TimeSpan _flushInterval;
    private readonly ConcurrentQueue<object> _queue = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Timer _flushTimer;
    private readonly CancellationTokenSource _cts = new();

    private string _currentFilePath;
    private long _currentFileSize;
    private int _fileSequence;
    private bool _disposed;

    public JsonTelemetryFileWriter(
        string directory,
        string prefix,
        long maxFileSizeBytes,
        TimeSpan flushInterval)
    {
        _directory = directory;
        _prefix = prefix;
        _maxFileSize = maxFileSizeBytes;
        _flushInterval = flushInterval;

        Directory.CreateDirectory(directory);
        _currentFilePath = GenerateFilePath();
        InitializeFileSize();

        _flushTimer = new Timer(
            _ => _ = FlushAsync(),
            null,
            flushInterval,
            flushInterval);
    }

    public void Enqueue(object record)
    {
        if (_disposed) return;
        _queue.Enqueue(record);
    }

    public async Task FlushAsync()
    {
        if (_disposed || _queue.IsEmpty) return;

        var records = new List<object>();
        while (_queue.TryDequeue(out var record))
        {
            records.Add(record);
        }

        if (records.Count == 0) return;

        await _writeLock.WaitAsync(_cts.Token);
        try
        {
            await WriteRecordsAsync(records);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteRecordsAsync(List<object> records)
    {
        var sb = new StringBuilder();
        foreach (var record in records)
        {
            var json = JsonSerializer.Serialize(record, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            sb.AppendLine(json);
        }

        var content = sb.ToString();
        var bytes = Encoding.UTF8.GetBytes(content);

        if (_currentFileSize + bytes.Length > _maxFileSize)
        {
            RotateFile();
        }

        await File.AppendAllTextAsync(_currentFilePath, content, _cts.Token);
        _currentFileSize += bytes.Length;
    }

    private void RotateFile()
    {
        _fileSequence++;
        _currentFilePath = GenerateFilePath();
        _currentFileSize = 0;
    }

    private string GenerateFilePath()
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        return Path.Combine(_directory, $"{_prefix}_{date}_{_fileSequence:D4}.jsonl");
    }

    private void InitializeFileSize()
    {
        try
        {
            _currentFileSize = File.Exists(_currentFilePath)
                ? new FileInfo(_currentFilePath).Length
                : 0;
        }
        catch
        {
            _currentFileSize = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _flushTimer.Dispose();

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore flush errors during disposal
        }

        _writeLock.Dispose();
        _cts.Dispose();
    }
}
