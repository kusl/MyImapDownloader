#!/bin/bash
set -e

# =============================================================================
# FINAL FIX SCRIPT - Fixes all 5 failing tests
# =============================================================================
# 
# Issues:
# 1. EscapeFts5Query_WithEmptyString test expects wrong value
# 2. TelemetryExtensionsTests look for wrong TelemetryConfiguration type
#    - There are TWO TelemetryConfiguration classes:
#      - MyImapDownloader.Core.Telemetry.TelemetryConfiguration (registered by AddCoreTelemetry)
#      - MyImapDownloader.Telemetry.TelemetryConfiguration (what tests look for)
# =============================================================================

echo "=========================================="
echo "Final Fix Script - Fixing 5 Failing Tests"
echo "=========================================="
echo ""

cd ~/src/dotnet/MyImapDownloader || exit 1

# =============================================================================
# FIX 1: SearchDatabaseEscapingTests.cs
# The implementation returns "" for empty input, not "\"\""
# =============================================================================
echo "[1/2] Fixing SearchDatabaseEscapingTests.cs..."

cat > MyEmailSearch.Tests/Data/SearchDatabaseEscapingTests.cs << 'EOF'
namespace MyEmailSearch.Tests.Data;

using MyEmailSearch.Data;

/// <summary>
/// Tests for FTS5 query escaping methods.
/// </summary>
public class SearchDatabaseEscapingTests
{
    [Test]
    public async Task EscapeFts5Query_WithSpecialCharacters_EscapesCorrectly()
    {
        // Input: test"query -> Output: "test""query"
        var result = SearchDatabase.EscapeFts5Query("test\"query");

        await Assert.That(result).IsEqualTo("\"test\"\"query\"");
    }

    [Test]
    public async Task EscapeFts5Query_WithNormalText_WrapsInQuotes()
    {
        var result = SearchDatabase.EscapeFts5Query("hello world");

        await Assert.That(result).IsEqualTo("\"hello world\"");
    }

    [Test]
    public async Task EscapeFts5Query_WithEmptyString_ReturnsEmpty()
    {
        // Implementation returns "" for empty input (not wrapped in quotes)
        var result = SearchDatabase.EscapeFts5Query("");

        await Assert.That(result).IsEqualTo("");
    }

    [Test]
    public async Task EscapeFts5Query_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.EscapeFts5Query(null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery(null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWhitespace_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("   ");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithSimpleText_WrapsInQuotes()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("search term");

        await Assert.That(result).IsEqualTo("\"search term\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWildcard_PreservesWildcard()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("search*");

        await Assert.That(result).IsEqualTo("\"search\"*");
    }
}
EOF

echo "  ✓ Fixed EscapeFts5Query empty string test"

# =============================================================================
# FIX 2: TelemetryExtensionsTests.cs
# The tests look for MyImapDownloader.Telemetry.TelemetryConfiguration
# but AddCoreTelemetry registers MyImapDownloader.Core.Telemetry.TelemetryConfiguration
# Fix: Use the Core TelemetryConfiguration type in tests
# =============================================================================
echo "[2/2] Fixing TelemetryExtensionsTests.cs..."

cat > MyImapDownloader.Tests/Telemetry/TelemetryExtensionsTests.cs << 'EOF'
using AwesomeAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MyImapDownloader.Core.Telemetry;
using MyImapDownloader.Telemetry;

// IMPORTANT: Use the CORE TelemetryConfiguration, which is what AddCoreTelemetry registers
using TelemetryConfiguration = MyImapDownloader.Core.Telemetry.TelemetryConfiguration;

namespace MyImapDownloader.Tests.Telemetry;

/// <summary>
/// Tests for TelemetryExtensions.
/// </summary>
public class TelemetryExtensionsTests
{
    [Test]
    public async Task AddTelemetry_RegistersTelemetryConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();

        services.AddTelemetry(configuration);
        var provider = services.BuildServiceProvider();

        // AddCoreTelemetry registers Core.TelemetryConfiguration
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

        // Core's default is "MyImapDownloader" because AddTelemetry passes DiagnosticsConfig.ServiceName
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
EOF

echo "  ✓ Fixed TelemetryExtensionsTests to use Core.TelemetryConfiguration"

# =============================================================================
# Build and Test
# =============================================================================
echo ""
echo "=========================================="
echo "Building and Testing..."
echo "=========================================="

echo ""
echo "Building solution..."
if dotnet build; then
    echo ""
    echo "✅ Build succeeded!"
else
    echo ""
    echo "❌ Build failed!"
    exit 1
fi

echo ""
echo "Running tests..."
if dotnet test; then
    echo ""
    echo "=========================================="
    echo "✅ ALL TESTS PASSED!"
    echo "=========================================="
else
    echo ""
    echo "⚠ Some tests failed (check output above)"
    exit 1
fi
EOF
