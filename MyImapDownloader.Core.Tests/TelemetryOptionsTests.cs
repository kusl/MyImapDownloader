using Microsoft.Extensions.Configuration;

using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests;

public class TelemetryOptionsTests
{
    [Test]
    public async Task From_UsesDefaultsWhenSectionIsMissing()
    {
        var options = TelemetryOptions.From(new ConfigurationBuilder().Build(), "Service", "2.0.0");

        await Assert.That(options.ServiceName).IsEqualTo("Service");
        await Assert.That(options.ServiceVersion).IsEqualTo("2.0.0");
        await Assert.That(options.MaxFileSizeMB).IsEqualTo(25);
        await Assert.That(options.MaxFileSizeBytes).IsEqualTo(25L * 1024 * 1024);
        await Assert.That(options.EnableTracing).IsTrue();
        await Assert.That(options.EnableMetrics).IsTrue();
        await Assert.That(options.EnableLogging).IsTrue();
        await Assert.That(options.FlushIntervalSeconds).IsEqualTo(5);
        await Assert.That(options.MetricsExportIntervalSeconds).IsEqualTo(15);
    }

    [Test]
    public async Task From_BindsTelemetrySection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telemetry:ServiceName"] = "Custom",
                ["Telemetry:MaxFileSizeMB"] = "3",
                ["Telemetry:EnableMetrics"] = "false",
                ["Telemetry:FlushIntervalSeconds"] = "9"
            })
            .Build();

        var options = TelemetryOptions.From(configuration, "Service", "2.0.0");

        await Assert.That(options.ServiceName).IsEqualTo("Custom");
        await Assert.That(options.ServiceVersion).IsEqualTo("2.0.0");
        await Assert.That(options.MaxFileSizeMB).IsEqualTo(3);
        await Assert.That(options.EnableMetrics).IsFalse();
        await Assert.That(options.FlushIntervalSeconds).IsEqualTo(9);
    }
}
