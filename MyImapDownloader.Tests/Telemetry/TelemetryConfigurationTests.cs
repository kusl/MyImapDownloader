using FluentAssertions;
using MyImapDownloader.Telemetry;

namespace MyImapDownloader.Tests.Telemetry;

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
        await Assert.That(config.MetricsExportIntervalSeconds).IsEqualTo(15);
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
    [Arguments(1024)]
    public async Task MaxFileSizeBytes_ScalesWithMB(int megabytes)
    {
        var config = new TelemetryConfiguration { MaxFileSizeMB = megabytes };
        var expected = (long)megabytes * 1024L * 1024L;

        await Assert.That(config.MaxFileSizeBytes).IsEqualTo(expected);
    }

    [Test]
    public async Task SectionName_IsCorrect()
    {
        await Assert.That(TelemetryConfiguration.SectionName).IsEqualTo("Telemetry");
    }

    [Test]
    public async Task AllPropertiesAreMutable()
    {
        var config = new TelemetryConfiguration
        {
            ServiceName = "CustomService",
            ServiceVersion = "2.0.0",
            OutputDirectory = "/custom/path",
            MaxFileSizeMB = 50,
            EnableTracing = false,
            EnableMetrics = false,
            EnableLogging = false,
            FlushIntervalSeconds = 10,
            MetricsExportIntervalSeconds = 30
        };

        await Assert.That(config.ServiceName).IsEqualTo("CustomService");
        await Assert.That(config.ServiceVersion).IsEqualTo("2.0.0");
        await Assert.That(config.OutputDirectory).IsEqualTo("/custom/path");
        await Assert.That(config.MaxFileSizeMB).IsEqualTo(50);
        await Assert.That(config.EnableTracing).IsFalse();
        await Assert.That(config.EnableMetrics).IsFalse();
        await Assert.That(config.EnableLogging).IsFalse();
        await Assert.That(config.FlushIntervalSeconds).IsEqualTo(10);
        await Assert.That(config.MetricsExportIntervalSeconds).IsEqualTo(30);
    }
}
