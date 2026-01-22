using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MyImapDownloader.Core.Telemetry;

/// <summary>
/// Thread-safe JSON Lines file writer with size-based rotation and periodic flushing.
/// Each telemetry record is written as a separate JSON line (JSONL format).
/// Gracefully handles write failures without crashing the application.
/// </summary>
public sealed class JsonTelemetryFileWriter : IDisposable
{
    private readonly string _directory;
    private readonly string _prefix;
    private readonly long _maxFileSize;
    private readonly ConcurrentQueue<object> _queue = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Timer _flushTimer;
    private readonly CancellationTokenSource _cts = new();

    private string _currentFilePath;
    private long _currentFileSize;
    private int _fileSequence;
    private bool _disposed;
    private bool _writeEnabled = true;

    public JsonTelemetryFileWriter(
        string directory,
        string prefix,
        long maxFileSizeBytes,
        TimeSpan flushInterval)
    {
        _directory = directory;
        _prefix = prefix;
        _maxFileSize = maxFileSizeBytes;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch
        {
            _writeEnabled = false;
        }

        _currentFilePath = GenerateFilePath();
        InitializeFileSize();

        // Timer callback with proper exception handling
        _flushTimer = new Timer(
            _ => FlushTimerCallback(),
            null,
            flushInterval,
            flushInterval);
    }

    private void FlushTimerCallback()
    {
        if (_disposed || !_writeEnabled || _queue.IsEmpty) return;

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Degrade gracefully - disable writes after buffer grows too large
            if (_queue.Count > 10000)
            {
                _writeEnabled = false;
                while (_queue.TryDequeue(out _)) { }
            }
        }
    }

    public void Enqueue(object record)
    {
        if (_disposed || !_writeEnabled) return;
        _queue.Enqueue(record);
    }

    public async Task FlushAsync()
    {
        // Note: We check _queue.IsEmpty but NOT _disposed here
        // This allows final flush during disposal
        if (!_writeEnabled || _queue.IsEmpty) return;

        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            return;

        try
        {
            var records = new List<object>();
            while (_queue.TryDequeue(out var record))
            {
                records.Add(record);
            }

            if (records.Count > 0)
            {
                await WriteRecordsAsync(records).ConfigureAwait(false);
            }
        }
        catch
        {
            if (_queue.Count > 10000)
            {
                _writeEnabled = false;
                while (_queue.TryDequeue(out _)) { }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteRecordsAsync(List<object> records)
    {
        if (!_writeEnabled) return;

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

        if (_currentFileSize + bytes.Length > _maxFileSize && _currentFileSize > 0)
        {
            RotateFile();
        }

        try
        {
            await File.AppendAllTextAsync(_currentFilePath, content).ConfigureAwait(false);
            _currentFileSize += bytes.Length;
        }
        catch
        {
            // Individual write failures are silently ignored
        }
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
        
        // Stop the timer first to prevent new flushes
        _flushTimer.Dispose();
        
        // CRITICAL: Flush BEFORE setting _disposed = true
        // This ensures FlushAsync() doesn't return early
        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore flush errors during disposal
        }
        
        // NOW mark as disposed
        _disposed = true;
        
        _cts.Cancel();
        _writeLock.Dispose();
        _cts.Dispose();
    }
}
