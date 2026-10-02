using System.Diagnostics;
using System.Diagnostics.Metrics;

using MyImapDownloader.Core.Infrastructure;
using MyImapDownloader.Core.Telemetry;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class JsonExporterTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("exporter_test");
    private readonly List<JsonTelemetryFileWriter> _writers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var writer in _writers)
        {
            writer.Dispose();
        }
        await Task.Delay(100);
        _temp.Dispose();
    }

    private JsonTelemetryFileWriter CreateWriter(string prefix)
    {
        var writer = new JsonTelemetryFileWriter(
            Path.Combine(_temp.Path, prefix), prefix, 1024 * 1024, TimeSpan.FromSeconds(30));
        _writers.Add(writer);
        return writer;
    }

    private async Task<string> ReadAllAsync(string prefix)
    {
        var files = Directory.GetFiles(Path.Combine(_temp.Path, prefix), "*.jsonl");
        var parts = new List<string>();
        foreach (var file in files)
        {
            parts.Add(await File.ReadAllTextAsync(file));
        }
        return string.Concat(parts);
    }

    [Test]
    public async Task TraceExporter_WithNullWriter_ReturnsSuccess()
    {
        var exporter = new JsonFileTraceExporter(null);

        var result = exporter.Export(new Batch<Activity>([], 0));

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }

    [Test]
    public async Task LogExporter_WithNullWriter_ReturnsSuccess()
    {
        var exporter = new JsonFileLogExporter(null);

        var result = exporter.Export(new Batch<LogRecord>([], 0));

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }

    [Test]
    public async Task MetricsExporter_WithNullWriter_ReturnsSuccess()
    {
        var exporter = new JsonFileMetricsExporter(null);

        var result = exporter.Export(new Batch<Metric>([], 0));

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }

    [Test]
    public async Task TraceExporter_WritesSpanWithNonStringTags()
    {
        var writer = CreateWriter("traces");
        var exporter = new JsonFileTraceExporter(writer);

        var sourceName = $"trace.test.{Guid.NewGuid():N}";
        using var activitySource = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var activity = activitySource.StartActivity("ExportTest");
        await Assert.That(activity).IsNotNull();
        activity!.SetTag("text.key", "text.value");
        activity.SetTag("number.key", 42);
        activity.Stop();

        var result = exporter.Export(new Batch<Activity>([activity], 1));
        await writer.FlushAsync();
        activity.Dispose();

        var content = await ReadAllAsync("traces");

        await Assert.That(result).IsEqualTo(ExportResult.Success);
        await Assert.That(content).Contains("ExportTest");
        await Assert.That(content).Contains("\"text.key\":\"text.value\"");
        await Assert.That(content).Contains("\"number.key\":\"42\"");
    }

    [Test]
    public async Task MetricsExporter_WritesHistogramMinAndMax()
    {
        var writer = CreateWriter("metrics");
        var meterName = $"metrics.test.{Guid.NewGuid():N}";

        using var meter = new Meter(meterName);
        var histogram = meter.CreateHistogram<double>("test.latency");
        var counter = meter.CreateCounter<long>("test.count");

        using (var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(meterName)
            .AddReader(new BaseExportingMetricReader(new JsonFileMetricsExporter(writer)))
            .Build())
        {
            histogram.Record(5);
            histogram.Record(15);
            counter.Add(3);
            provider.ForceFlush();
        }

        await writer.FlushAsync();
        var content = await ReadAllAsync("metrics");

        await Assert.That(content).Contains("\"MetricName\":\"test.latency\"");
        await Assert.That(content).Contains("\"Count\":2");
        await Assert.That(content).Contains("\"Min\":5");
        await Assert.That(content).Contains("\"Max\":15");
        await Assert.That(content).Contains("\"MetricName\":\"test.count\"");
        await Assert.That(content).Contains("\"Value\":3");
    }
}
