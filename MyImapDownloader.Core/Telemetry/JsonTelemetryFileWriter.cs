using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonTelemetryFileWriter : IDisposable
{
    private const int MaxBufferedLines = 10000;

    private readonly string _directory;
    private readonly string _prefix;
    private readonly long _maxFileSize;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Timer _flushTimer;

    private string _currentDate;
    private string _currentFilePath;
    private long _currentFileSize;
    private int _fileSequence;
    private volatile bool _disposed;
    private volatile bool _writeEnabled = true;

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

        _currentDate = CurrentDate();
        _currentFilePath = GenerateFilePath();
        _currentFileSize = GetExistingFileSize(_currentFilePath);

        _flushTimer = new Timer(_ => FlushTimerCallback(), null, flushInterval, flushInterval);
    }

    public void Enqueue<T>(T record, JsonTypeInfo<T> typeInfo)
    {
        if (_disposed || !_writeEnabled) return;

        string line;
        try
        {
            line = JsonSerializer.Serialize(record, typeInfo);
        }
        catch
        {
            return;
        }

        _queue.Enqueue(line);
    }

    public async Task FlushAsync()
    {
        if (!_writeEnabled || _queue.IsEmpty) return;

        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            return;

        try
        {
            var sb = new StringBuilder();
            while (_queue.TryDequeue(out var line))
            {
                sb.Append(line).Append('\n');
            }

            if (sb.Length > 0)
            {
                await WriteAsync(sb.ToString()).ConfigureAwait(false);
            }
        }
        catch
        {
            DisableIfOverflowing();
        }
        finally
        {
            _writeLock.Release();
        }
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
            DisableIfOverflowing();
        }
    }

    private void DisableIfOverflowing()
    {
        if (_queue.Count <= MaxBufferedLines) return;
        _writeEnabled = false;
        _queue.Clear();
    }

    private async Task WriteAsync(string content)
    {
        var byteCount = Encoding.UTF8.GetByteCount(content);

        var today = CurrentDate();
        if (today != _currentDate)
        {
            _currentDate = today;
            _fileSequence = 0;
            _currentFilePath = GenerateFilePath();
            _currentFileSize = GetExistingFileSize(_currentFilePath);
        }

        if (_currentFileSize > 0 && _currentFileSize + byteCount > _maxFileSize)
        {
            _fileSequence++;
            _currentFilePath = GenerateFilePath();
            _currentFileSize = GetExistingFileSize(_currentFilePath);
        }

        try
        {
            await File.AppendAllTextAsync(_currentFilePath, content).ConfigureAwait(false);
            _currentFileSize += byteCount;
        }
        catch
        {
        }
    }

    private static string CurrentDate() =>
        DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private string GenerateFilePath() =>
        Path.Combine(_directory, $"{_prefix}_{_currentDate}_{_fileSequence:D4}.jsonl");

    private static long GetExistingFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _flushTimer.Dispose();

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _disposed = true;
        _writeLock.Dispose();
    }
}
