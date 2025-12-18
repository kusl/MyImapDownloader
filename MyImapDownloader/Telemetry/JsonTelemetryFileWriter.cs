using System.Collections.Concurrent;
using System.Text.Json;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Thread-safe JSON file writer that manages daily files with size limits.
/// Each telemetry record is written as a separate JSON line (JSONL format).
/// Gracefully handles write failures without crashing the application.
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
    private bool _writeEnabled = true;

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
            WriteIndented = false, // JSONL format - single line per record
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Try to create base directory - if it fails, disable writes
        try
        {
            Directory.CreateDirectory(_baseDirectory);
        }
        catch
        {
            _writeEnabled = false;
        }

        _flushTimer = new Timer(_ => FlushAsync().ConfigureAwait(false), null, flushInterval, flushInterval);
    }

    public void Enqueue(object record)
    {
        if (_disposed || !_writeEnabled) return;
        _buffer.Enqueue(record);
    }

    public async Task FlushAsync()
    {
        if (_disposed || !_writeEnabled || _buffer.IsEmpty) return;

        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)))
            return; // Skip this flush cycle if we can't get the lock

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
        catch
        {
            // If we consistently fail to write, disable future writes
            // to avoid accumulating memory
            if (_buffer.Count > 10000)
            {
                _writeEnabled = false;
                while (_buffer.TryDequeue(out _)) { } // Clear buffer
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task WriteRecordAsync(object record)
    {
        if (!_writeEnabled) return;

        try
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
                RotateFile();
            }

            // Each record is a complete JSON object on a single line (JSONL format)
            string json = JsonSerializer.Serialize(record, record.GetType(), _jsonOptions);
            string line = json + Environment.NewLine;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(line);

            // Check if adding this record would exceed size limit
            if (_currentFileSize + bytes.Length > _maxFileSizeBytes && _currentFileSize > 0)
            {
                RotateFile();
            }

            await File.AppendAllTextAsync(_currentFilePath, line);
            _currentFileSize += bytes.Length;
        }
        catch
        {
            // Individual write failures are silently ignored
            // The application continues normally
        }
    }

    private void RotateFile()
    {
        _fileSequence++;
        _currentFilePath = Path.Combine(
            _baseDirectory,
            $"{_prefix}_{_currentDate}_{_fileSequence:D4}.jsonl");
        
        try
        {
            _currentFileSize = File.Exists(_currentFilePath) ? new FileInfo(_currentFilePath).Length : 0;
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
    }
}
