using System.Globalization;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonLinesFileWriter
{
    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly string _prefix;
    private readonly long _maxFileSizeBytes;
    private readonly TimeProvider _time;
    private string _date = "";
    private int _sequence;

    public JsonLinesFileWriter(string directory, string prefix, long maxFileSizeBytes, TimeProvider time)
    {
        Directory.CreateDirectory(directory);
        _directory = directory;
        _prefix = prefix;
        _maxFileSizeBytes = maxFileSizeBytes;
        _time = time;
    }

    public void Append(ReadOnlySpan<byte> lines)
    {
        if (lines.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            using var stream = new FileStream(
                SelectFile(lines.Length), FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(lines);
        }
    }

    private string SelectFile(int incomingBytes)
    {
        var today = _time.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (today != _date)
        {
            _date = today;
            _sequence = 0;
        }

        while (true)
        {
            var path = Path.Combine(
                _directory, string.Create(CultureInfo.InvariantCulture, $"{_prefix}_{_date}_{_sequence:D4}.jsonl"));
            var file = new FileInfo(path);
            var size = file.Exists ? file.Length : 0;
            if (size == 0 || size + incomingBytes <= _maxFileSizeBytes)
            {
                return path;
            }

            _sequence++;
        }
    }
}
