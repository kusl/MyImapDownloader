using AwesomeAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MyImapDownloader.Telemetry;

namespace MyImapDownloader.Tests.Telemetry;

public class TelemetryExtensionsTests
{
    [Test]
    public async Task AddTelemetry_RegistersTelemetryConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        services.AddTelemetry(configuration);
        var provider = services.BuildServiceProvider();

        var config = provider.GetService<TelemetryConfiguration>();
        config.Should().NotBeNull();
    }

    [Test]
    public async Task AddTelemetry_RegistersWriterProvider()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        services.AddTelemetry(configuration);
        var provider = services.BuildServiceProvider();

        var writerProvider = provider.GetService<ITelemetryWriterProvider>();
        writerProvider.Should().NotBeNull();
    }

    [Test]
    public async Task AddTelemetry_BindsConfigurationValues()
    {
        var configData = new Dictionary<string, string?>
        {
            ["Telemetry:ServiceName"] = "CustomService",
            ["Telemetry:ServiceVersion"] = "2.0.0",
            ["Telemetry:MaxFileSizeMB"] = "50",
            ["Telemetry:EnableTracing"] = "true",
            ["Telemetry:EnableMetrics"] = "false"
        };

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        services.AddTelemetry(configuration);
        var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<TelemetryConfiguration>();

        await Assert.That(config.ServiceName).IsEqualTo("CustomService");
        await Assert.That(config.ServiceVersion).IsEqualTo("2.0.0");
        await Assert.That(config.MaxFileSizeMB).IsEqualTo(50);
        await Assert.That(config.EnableMetrics).IsFalse();
    }

    [Test]
    public async Task AddTelemetry_WithDisabledTelemetry_RegistersNullProvider()
    {
        var configData = new Dictionary<string, string?>
        {
            ["Telemetry:EnableTracing"] = "false",
            ["Telemetry:EnableMetrics"] = "false",
            ["Telemetry:EnableLogging"] = "false"
        };

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        services.AddTelemetry(configuration);
        var provider = services.BuildServiceProvider();

        var writerProvider = provider.GetService<ITelemetryWriterProvider>();
        writerProvider.Should().NotBeNull();
    }

    [Test]
    public async Task AddTelemetry_CanBeCalledMultipleTimes_WithoutError()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        // Should not throw on multiple calls
        services.AddTelemetry(configuration);
        services.AddTelemetry(configuration);

        var provider = services.BuildServiceProvider();
        var config = provider.GetService<TelemetryConfiguration>();

        config.Should().NotBeNull();
    }

    [Test]
    public async Task AddTelemetry_WithEmptyConfiguration_UsesDefaults()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddTelemetry(configuration);
        var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<TelemetryConfiguration>();

        await Assert.That(config.ServiceName).IsEqualTo("MyImapDownloader");
        await Assert.That(config.EnableTracing).IsTrue();
    }

    [Test]
    public async Task AddTelemetry_ReturnsServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        var result = services.AddTelemetry(configuration);

        result.Should().BeSameAs(services);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telemetry:ServiceName"] = "TestService"
            })
            .Build();
    }
}
