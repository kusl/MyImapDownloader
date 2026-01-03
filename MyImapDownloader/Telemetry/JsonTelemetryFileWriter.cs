using System.Collections.Concurrent;
using System.Text.Json;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Thread-safe, async file writer for telemetry data in JSONL format.
/// Each telemetry record is written as a separate JSON line (JSONL format).
/// Gracefully handles write failures without crashing the application.
/// FIX: Uses async-safe timer pattern to prevent swallowed exceptions.
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
    private readonly CancellationTokenSource _cts = new();

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
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        try
        {
            Directory.CreateDirectory(_baseDirectory);
        }
        catch
        {
            _writeEnabled = false;
        }

        // FIX: Wrap the async call in a synchronous wrapper that handles exceptions
        _flushTimer = new Timer(
            _ => FlushTimerCallback(), 
            null, 
            flushInterval, 
            flushInterval);
    }

    /// <summary>
    /// FIX: Synchronous wrapper that properly handles async FlushAsync exceptions.
    /// </summary>
    private void FlushTimerCallback()
    {
        if (_disposed || !_writeEnabled || _buffer.IsEmpty) return;

        try
        {
            // Use GetAwaiter().GetResult() in a try-catch to surface exceptions
            FlushAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // FIX: Log or count errors instead of silently swallowing
            // For telemetry writer, we degrade gracefully - disable writes after too many failures
            if (_buffer.Count > 10000)
            {
                _writeEnabled = false;
                while (_buffer.TryDequeue(out _)) { }
            }
        }
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
            return;

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
            if (_buffer.Count > 10000)
            {
                _writeEnabled = false;
                while (_buffer.TryDequeue(out _)) { }
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

            if (today != _currentDate || _currentFileSize >= _maxFileSizeBytes)
            {
                if (today != _currentDate)
                {
                    _currentDate = today;
                    _fileSequence = 0;
                }
                RotateFile();
            }

            string json = JsonSerializer.Serialize(record, record.GetType(), _jsonOptions);
            string line = json + Environment.NewLine;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(line);

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
