using System.Diagnostics;

using AwesomeAssertions;

using MyImapDownloader.Telemetry;

using OpenTelemetry;

namespace MyImapDownloader.Tests.Telemetry;

public class JsonFileTraceExporterTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private readonly List<JsonTelemetryFileWriter> _writers = [];

    public JsonFileTraceExporterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"trace_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var writer in _writers)
        {
            writer.Dispose();
        }

        await Task.Delay(100);

        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch { }
    }

    private JsonTelemetryFileWriter CreateWriter(string prefix = "traces")
    {
        var writer = new JsonTelemetryFileWriter(_testDirectory, prefix, 1024 * 1024, TimeSpan.FromSeconds(30));
        _writers.Add(writer);
        return writer;
    }

    [Test]
    public async Task Export_WithNullWriter_ReturnsSuccess()
    {
        var exporter = new JsonFileTraceExporter(null);

        var batch = new Batch<Activity>([], 0);
        var result = exporter.Export(batch);

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }

    [Test]
    public async Task Export_WithWriter_EnqueuesRecords()
    {
        var writer = CreateWriter();
        var exporter = new JsonFileTraceExporter(writer);

        using var activitySource = new ActivitySource("Test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("ExportTest");
        activity?.SetTag("test.key", "test.value");
        activity?.Stop();

        var batch = new Batch<Activity>([activity!], 1);
        var result = exporter.Export(batch);

        await Assert.That(result).IsEqualTo(ExportResult.Success);

        // Flush and verify
        await writer.FlushAsync();

        var files = Directory.GetFiles(_testDirectory, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);

        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("ExportTest");
    }

    [Test]
    public async Task Export_ReturnsSuccess_EvenOnError()
    {
        var writer = CreateWriter();
        var exporter = new JsonFileTraceExporter(writer);

        // Empty batch should still return success
        var batch = new Batch<Activity>([], 0);
        var result = exporter.Export(batch);

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }
}

public class JsonFileLogExporterTests : IAsyncDisposable
{
    private readonly string _testDirectory;

    public JsonFileLogExporterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"log_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);

        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch { }
    }

    [Test]
    public async Task Export_WithNullWriter_ReturnsSuccess()
    {
        var exporter = new JsonFileLogExporter(null);

        // Test with empty batch
        var batch = new Batch<OpenTelemetry.Logs.LogRecord>([], 0);
        var result = exporter.Export(batch);

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }
}

public class JsonFileMetricsExporterTests : IAsyncDisposable
{
    private readonly string _testDirectory;

    public JsonFileMetricsExporterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"metrics_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);

        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch { }
    }

    [Test]
    public async Task Export_WithNullWriter_ReturnsSuccess()
    {
        var exporter = new JsonFileMetricsExporter(null);

        var batch = new Batch<OpenTelemetry.Metrics.Metric>([], 0);
        var result = exporter.Export(batch);

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }
}
