using System.Collections.Concurrent;
using System.Text.Json;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Thread-safe, async file writer for telemetry data in JSONL format.
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

        _flushTimer = new Timer(
            _ => FlushTimerCallback(),
            null,
            flushInterval,
            flushInterval);
    }

    private void FlushTimerCallback()
    {
        if (_disposed || !_writeEnabled || _buffer.IsEmpty) return;

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Degrade gracefully - disable writes after buffer grows too large
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
        // Note: We check _buffer.IsEmpty but NOT _disposed here.
        // This allows the final flush during disposal to complete.
        if (!_writeEnabled || _buffer.IsEmpty) return;

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

        // Stop the timer first to prevent new timer-driven flushes
        _flushTimer.Dispose();

        // CRITICAL: Flush BEFORE setting _disposed = true.
        // FlushAsync() no longer checks _disposed, so the final flush completes.
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
