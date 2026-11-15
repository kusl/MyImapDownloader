using System.Diagnostics;
using System.Diagnostics.Metrics;

using MyImapDownloader.Core.Telemetry;

using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace MyImapDownloader.Core.Tests;

public class JsonLinesExporterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private JsonLinesFileWriter CreateWriter(string kind) =>
        new(Path.Combine(_temp.Path, kind), kind, 1024 * 1024, TimeProvider.System);

    private async Task<string> ReadAllAsync(string kind)
    {
        var parts = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(_temp.Path, kind), "*.jsonl"))
        {
            parts.Add(await File.ReadAllTextAsync(file));
        }

        return string.Concat(parts);
    }

    [Test]
    public async Task TraceExporter_EmptyBatchWritesNothing()
    {
        var exporter = new JsonLinesTraceExporter(CreateWriter("traces"));

        var result = exporter.Export(new Batch<Activity>([], 0));

        await Assert.That(result).IsEqualTo(ExportResult.Success);
        await Assert.That(await ReadAllAsync("traces")).IsEqualTo("");
    }

    [Test]
    public async Task TraceExporter_WritesOneJsonLinePerSpanWithStringTags()
    {
        var exporter = new JsonLinesTraceExporter(CreateWriter("traces"));
        var sourceName = $"trace.test.{Guid.NewGuid():N}";
        using var source = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var activity = source.StartActivity("ExportTest");
        await Assert.That(activity).IsNotNull();
        activity!.SetTag("text.key", "text.value");
        activity.SetTag("number.key", 42);
        activity.Stop();

        var result = exporter.Export(new Batch<Activity>([activity], 1));
        activity.Dispose();
        var content = await ReadAllAsync("traces");

        await Assert.That(result).IsEqualTo(ExportResult.Success);
        await Assert.That(content.EndsWith('\n')).IsTrue();
        await Assert.That(content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(1);
        await Assert.That(content).Contains("\"Type\":\"trace\"");
        await Assert.That(content).Contains("\"OperationName\":\"ExportTest\"");
        await Assert.That(content).Contains("\"text.key\":\"text.value\"");
        await Assert.That(content).Contains("\"number.key\":\"42\"");
    }

    [Test]
    public async Task MetricExporter_WritesCountersAndHistograms()
    {
        var meterName = $"metrics.test.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        var histogram = meter.CreateHistogram<double>("test.latency");
        var counter = meter.CreateCounter<long>("test.count");

        using (var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(meterName)
            .AddReader(new BaseExportingMetricReader(new JsonLinesMetricExporter(CreateWriter("metrics"))))
            .Build())
        {
            histogram.Record(5);
            histogram.Record(15);
            counter.Add(3);
            provider.ForceFlush();
        }

        var content = await ReadAllAsync("metrics");

        await Assert.That(content).Contains("\"Type\":\"metric\"");
        await Assert.That(content).Contains("\"MetricName\":\"test.latency\"");
        await Assert.That(content).Contains("\"Count\":2");
        await Assert.That(content).Contains("\"Min\":5");
        await Assert.That(content).Contains("\"Max\":15");
        await Assert.That(content).Contains("\"MetricName\":\"test.count\"");
        await Assert.That(content).Contains("\"Value\":3");
    }
}
