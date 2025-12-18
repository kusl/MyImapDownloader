using FluentAssertions;
using MyImapDownloader.Telemetry;

namespace MyImapDownloader.Tests.Telemetry;

public class TelemetryWriterProviderTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private readonly List<JsonTelemetryFileWriter> _writers = new();

    public TelemetryWriterProviderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"provider_test_{Guid.NewGuid():N}");
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

    private JsonTelemetryFileWriter CreateWriter(string prefix)
    {
        var writer = new JsonTelemetryFileWriter(
            _testDirectory, prefix, 1024 * 1024, TimeSpan.FromSeconds(30));
        _writers.Add(writer);
        return writer;
    }

    [Test]
    public async Task Constructor_AcceptsAllWriters()
    {
        var traceWriter = CreateWriter("traces");
        var metricsWriter = CreateWriter("metrics");
        var logsWriter = CreateWriter("logs");

        var provider = new TelemetryWriterProvider(traceWriter, metricsWriter, logsWriter);

        await Assert.That(provider.TraceWriter).IsEqualTo(traceWriter);
        await Assert.That(provider.MetricsWriter).IsEqualTo(metricsWriter);
        await Assert.That(provider.LogsWriter).IsEqualTo(logsWriter);
    }

    [Test]
    public async Task Constructor_AcceptsNullWriters()
    {
        var provider = new TelemetryWriterProvider(null, null, null);

        await Assert.That(provider.TraceWriter).IsNull();
        await Assert.That(provider.MetricsWriter).IsNull();
        await Assert.That(provider.LogsWriter).IsNull();
    }

    [Test]
    public async Task Constructor_AcceptsMixedNullAndNonNullWriters()
    {
        var traceWriter = CreateWriter("traces");

        var provider = new TelemetryWriterProvider(traceWriter, null, null);

        await Assert.That(provider.TraceWriter).IsEqualTo(traceWriter);
        await Assert.That(provider.MetricsWriter).IsNull();
        await Assert.That(provider.LogsWriter).IsNull();
    }

    [Test]
    public async Task ImplementsInterface()
    {
        var provider = new TelemetryWriterProvider(null, null, null);

        provider.Should().BeAssignableTo<ITelemetryWriterProvider>();
    }

    [Test]
    public async Task NullProvider_ReturnsAllNull()
    {
        var provider = new NullTelemetryWriterProvider();

        await Assert.That(provider.TraceWriter).IsNull();
        await Assert.That(provider.MetricsWriter).IsNull();
        await Assert.That(provider.LogsWriter).IsNull();
    }

    [Test]
    public async Task NullProvider_ImplementsInterface()
    {
        var provider = new NullTelemetryWriterProvider();

        provider.Should().BeAssignableTo<ITelemetryWriterProvider>();
    }

    [Test]
    public async Task ProvidersAreInterchangeable()
    {
        ITelemetryWriterProvider provider1 = new NullTelemetryWriterProvider();
        ITelemetryWriterProvider provider2 = new TelemetryWriterProvider(null, null, null);

        // Both should work identically through the interface
        await Assert.That(provider1.TraceWriter).IsNull();
        await Assert.That(provider2.TraceWriter).IsNull();
    }
}
