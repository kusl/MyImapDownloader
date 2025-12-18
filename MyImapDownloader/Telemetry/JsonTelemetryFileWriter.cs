using System.Collections.Concurrent;
using System.Text.Json;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Thread-safe JSON file writer that manages daily files with size limits.
/// Each telemetry record is written as a separate, valid JSON file.
/// </summary>
public sealed class JsonTelemetryFileWriter : IDisposable
{
    private readonly string _baseDirectory;
    private readonly string _prefix;
    private readonly long _maxFileSizeBytes;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentQueue<object> _buffer = new();
    private readonly Timer _flushTimer;
    private readonly JsonSerializerOptions _jsonOptions;

    private string _currentDate = "";
    private string _currentFilePath = "";
    private int _fileSequence;
    private long _currentFileSize;
    private bool _disposed;

    public JsonTelemetryFileWriter(
        string baseDirectory,
        string prefix,
        long maxFileSizeBytes,
        TimeSpan flushInterval)
    {
        _baseDirectory = baseDirectory;
        _prefix = prefix;
        _maxFileSizeBytes = maxFileSizeBytes;

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        Directory.CreateDirectory(_baseDirectory);
        _flushTimer = new Timer(_ => FlushAsync().Wait(), null, flushInterval, flushInterval);
    }

    public void Enqueue(object record)
    {
        if (_disposed) return;
        _buffer.Enqueue(record);
    }

    public async Task FlushAsync()
    {
        if (_disposed || _buffer.IsEmpty) return;

        await _writeLock.WaitAsync();
        try
        {
            var records = new List<object>();
            while (_buffer.TryDequeue(out var record))
            {
                records.Add(record);
            }

            foreach (var record in records)
            {
                await WriteRecordAsync(record);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteRecordAsync(object record)
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Check if we need a new file (new day or size exceeded)
        if (today != _currentDate || _currentFileSize >= _maxFileSizeBytes)
        {
            if (today != _currentDate)
            {
                _currentDate = today;
                _fileSequence = 0;
            }
            await RotateFileAsync();
        }

        // Each record is a complete, valid JSON document
        // We write to a JSON Lines format where each line is valid JSON
        string json = JsonSerializer.Serialize(record, _jsonOptions);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json + Environment.NewLine);

        // Check if adding this record would exceed size limit
        if (_currentFileSize + bytes.Length > _maxFileSizeBytes && _currentFileSize > 0)
        {
            await RotateFileAsync();
        }

        await File.AppendAllTextAsync(_currentFilePath, json + Environment.NewLine);
        _currentFileSize += bytes.Length;
    }

    private Task RotateFileAsync()
    {
        _fileSequence++;
        _currentFilePath = Path.Combine(
            _baseDirectory,
            $"{_prefix}_{_currentDate}_{_fileSequence:D4}.jsonl");
        _currentFileSize = File.Exists(_currentFilePath) ? new FileInfo(_currentFilePath).Length : 0;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer.Dispose();
        FlushAsync().Wait();
        _writeLock.Dispose();
    }
}
