using System.Diagnostics;

using MyEmailSearch.Indexing;

namespace MyEmailSearch.Commands;

public sealed class ConsoleProgress(TextWriter output) : IProgress<IndexingProgress>
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private const int LineWidth = 100;

    private long _lastReport;

    public void Report(IndexingProgress value)
    {
        if (value.Processed < value.Total && Stopwatch.GetElapsedTime(_lastReport) < Interval)
        {
            return;
        }

        _lastReport = Stopwatch.GetTimestamp();
        var line = $"\rProcessing: {value.Processed:N0}/{value.Total:N0} ({value.Percentage:F1}%) - {value.CurrentFile}";
        output.Write(line.PadRight(LineWidth)[..LineWidth]);
    }
}
