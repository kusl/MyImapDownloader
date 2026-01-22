using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class TelemetryConfigurationTests
{
    [Test]
    public async Task DefaultValues_AreReasonable()
    {
        var config = new TelemetryConfiguration();

        await Assert.That(config.ServiceName).IsEqualTo("MyImapDownloader");
        await Assert.That(config.ServiceVersion).IsEqualTo("1.0.0");
        await Assert.That(config.EnableTracing).IsTrue();
        await Assert.That(config.EnableMetrics).IsTrue();
        await Assert.That(config.EnableLogging).IsTrue();
        await Assert.That(config.MaxFileSizeMB).IsEqualTo(25);
        await Assert.That(config.FlushIntervalSeconds).IsEqualTo(5);
    }

    [Test]
    public async Task MaxFileSizeBytes_CalculatesCorrectly()
    {
        var config = new TelemetryConfiguration { MaxFileSizeMB = 10 };
        await Assert.That(config.MaxFileSizeBytes).IsEqualTo(10L * 1024L * 1024L);
    }

    [Test]
    [Arguments(1)]
    [Arguments(25)]
    [Arguments(100)]
    public async Task MaxFileSizeBytes_ScalesWithMB(int megabytes)
    {
        var config = new TelemetryConfiguration { MaxFileSizeMB = megabytes };
        var expected = (long)megabytes * 1024L * 1024L;
        await Assert.That(config.MaxFileSizeBytes).IsEqualTo(expected);
    }
}
