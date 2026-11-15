using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.Logging;

using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests;

public class JsonFileTelemetryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _sourceName = $"telemetry.test.{Guid.NewGuid():N}";

    public void Dispose() => _temp.Dispose();

    private string ReadAll(string kind)
    {
        var directory = Path.Combine(_temp.Path, kind);
        return Directory.Exists(directory)
            ? string.Concat(Directory.GetFiles(directory, "*.jsonl").Select(File.ReadAllText))
            : "";
    }

    [Test]
    public async Task Start_WithoutDirectoryDisablesEverything()
    {
        using var telemetry = JsonFileTelemetry.Start(new TelemetryOptions(), _sourceName, directory: null);
        using var loggers = LoggerFactory.Create(telemetry.ConfigureLogging);

        loggers.CreateLogger("test").LogInformation("ignored");

        await Assert.That(telemetry.OutputDirectory).IsNull();
        await Assert.That(Directory.GetFileSystemEntries(_temp.Path).Length).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_FlushesSpansMetricsAndLogsToJsonLines()
    {
        using var source = new ActivitySource(_sourceName);
        using var meter = new Meter(_sourceName);
        var counter = meter.CreateCounter<long>("test.events");

        using (var telemetry = JsonFileTelemetry.Start(new TelemetryOptions(), _sourceName, _temp.Path))
        {
            using (var loggers = LoggerFactory.Create(telemetry.ConfigureLogging))
            {
                using (var activity = source.StartActivity("FlushTest"))
                {
                    counter.Add(7);
                    loggers.CreateLogger("test.category").LogInformation("hello {Name}", "telemetry");
                }
            }
        }

        await Assert.That(ReadAll("traces")).Contains("\"OperationName\":\"FlushTest\"");
        await Assert.That(ReadAll("metrics")).Contains("\"MetricName\":\"test.events\"");
        await Assert.That(ReadAll("logs")).Contains("\"FormattedMessage\":\"hello telemetry\"");
        await Assert.That(ReadAll("logs")).Contains("\"CategoryName\":\"test.category\"");
    }

    [Test]
    public async Task Start_HonoursDisabledSignals()
    {
        var options = new TelemetryOptions { EnableTracing = false, EnableMetrics = false, EnableLogging = false };
        using var source = new ActivitySource(_sourceName);

        using (var telemetry = JsonFileTelemetry.Start(options, _sourceName, _temp.Path))
        {
            using var loggers = LoggerFactory.Create(telemetry.ConfigureLogging);
            using var activity = source.StartActivity("Ignored");
            loggers.CreateLogger("test").LogInformation("ignored");
        }

        await Assert.That(ReadAll("traces")).IsEqualTo("");
        await Assert.That(ReadAll("metrics")).IsEqualTo("");
        await Assert.That(ReadAll("logs")).IsEqualTo("");
    }
}
