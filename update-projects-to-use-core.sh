#!/bin/bash
# =============================================================================
# Update Existing Projects to Use MyImapDownloader.Core
# =============================================================================
# This script updates MyImapDownloader and MyEmailSearch to reference the
# new shared Core library and removes duplicated code.
# =============================================================================

set -euo pipefail

PROJECT_ROOT="${1:-$(pwd)}"

echo "=========================================="
echo "Updating projects to use Core library"
echo "=========================================="

# =============================================================================
# 1. Update MyImapDownloader.csproj
# =============================================================================
echo ""
echo "[1/6] Updating MyImapDownloader.csproj..."

cat > "$PROJECT_ROOT/MyImapDownloader/MyImapDownloader.csproj" << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <!--
    MyImapDownloader - Email Archive Downloader
    
    Downloads emails from IMAP servers and archives them locally.
    Uses MyImapDownloader.Core for shared infrastructure.
  -->
  
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MyImapDownloader</RootNamespace>
    <AssemblyName>MyImapDownloader</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core library reference -->
    <ProjectReference Include="..\MyImapDownloader.Core\MyImapDownloader.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- CLI -->
    <PackageReference Include="CommandLineParser" />
    
    <!-- IMAP -->
    <PackageReference Include="MailKit" />
    
    <!-- Resilience -->
    <PackageReference Include="Polly" />
    
    <!-- Configuration & Hosting -->
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
CSPROJ

# =============================================================================
# 2. Update MyImapDownloader to use Core telemetry
# =============================================================================
echo "[2/6] Updating MyImapDownloader/Telemetry/DiagnosticsConfig.cs..."

cat > "$PROJECT_ROOT/MyImapDownloader/Telemetry/DiagnosticsConfig.cs" << 'CSHARP'
using System.Diagnostics.Metrics;
using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Application-specific diagnostics configuration for MyImapDownloader.
/// Extends the core telemetry infrastructure with email-specific metrics.
/// </summary>
public static class DiagnosticsConfig
{
    public const string ServiceName = "MyImapDownloader";
    public const string ServiceVersion = "1.0.0";

    private static readonly DiagnosticsConfigBase _base = new(ServiceName, ServiceVersion);

    public static System.Diagnostics.ActivitySource ActivitySource => _base.ActivitySource;
    public static Meter Meter => _base.Meter;

    // Email download metrics
    public static readonly Counter<long> EmailsDownloaded = _base.CreateCounter<long>(
        "emails.downloaded", "emails", "Total emails downloaded");

    public static readonly Counter<long> BytesDownloaded = _base.CreateCounter<long>(
        "bytes.downloaded", "bytes", "Total bytes downloaded");

    public static readonly Histogram<double> DownloadLatency = _base.CreateHistogram<double>(
        "download.latency", "ms", "Email download latency");

    public static readonly Counter<long> RetryAttempts = _base.CreateCounter<long>(
        "retry.attempts", "attempts", "Number of retry attempts");

    // Storage metrics
    public static readonly Counter<long> FilesWritten = _base.CreateCounter<long>(
        "storage.files.written", "files", "Number of email files written");

    public static readonly Counter<long> BytesWritten = _base.CreateCounter<long>(
        "storage.bytes.written", "bytes", "Total bytes written to disk");

    public static readonly Histogram<double> WriteLatency = _base.CreateHistogram<double>(
        "storage.write.latency", "ms", "Disk write latency");

    // Connection metrics
    private static int _activeConnections;
    private static int _queuedEmails;
    private static long _totalEmailsInSession;

    public static readonly ObservableGauge<int> ActiveConnections = _base.Meter.CreateObservableGauge(
        "connections.active", () => _activeConnections, "connections", "Active IMAP connections");

    public static readonly ObservableGauge<int> QueuedEmails = _base.Meter.CreateObservableGauge(
        "emails.queued", () => _queuedEmails, "emails", "Emails queued for processing");

    public static readonly ObservableGauge<long> TotalEmailsInSession = _base.Meter.CreateObservableGauge(
        "emails.total.session", () => _totalEmailsInSession, "emails", "Total emails in session");

    public static void SetActiveConnections(int count) => _activeConnections = count;
    public static void IncrementActiveConnections() => Interlocked.Increment(ref _activeConnections);
    public static void DecrementActiveConnections() => Interlocked.Decrement(ref _activeConnections);
    public static void SetQueuedEmails(int count) => _queuedEmails = count;
    public static void IncrementTotalEmails() => Interlocked.Increment(ref _totalEmailsInSession);
}
CSHARP

# =============================================================================
# 3. Create MyImapDownloader TelemetryExtensions that uses Core
# =============================================================================
echo "[3/6] Updating MyImapDownloader/Telemetry/TelemetryExtensions.cs..."

cat > "$PROJECT_ROOT/MyImapDownloader/Telemetry/TelemetryExtensions.cs" << 'CSHARP'
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyImapDownloader.Core.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace MyImapDownloader.Telemetry;

/// <summary>
/// Extension methods for configuring telemetry in MyImapDownloader.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Adds telemetry services using the Core infrastructure.
    /// </summary>
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddCoreTelemetry(
            configuration,
            DiagnosticsConfig.ServiceName,
            DiagnosticsConfig.ServiceVersion);
    }

    /// <summary>
    /// Adds telemetry logging.
    /// </summary>
    public static ILoggingBuilder AddTelemetryLogging(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        var config = new TelemetryConfiguration();
        configuration.GetSection(TelemetryConfiguration.SectionName).Bind(config);

        if (!config.EnableLogging)
            return builder;

        var telemetryBaseDir = TelemetryDirectoryResolver.ResolveTelemetryDirectory(config.ServiceName);
        if (telemetryBaseDir == null)
            return builder;

        var logsDir = Path.Combine(telemetryBaseDir, "logs");
        Directory.CreateDirectory(logsDir);

        var flushInterval = TimeSpan.FromSeconds(config.FlushIntervalSeconds);

        try
        {
            var logsWriter = new JsonTelemetryFileWriter(
                logsDir, "logs", config.MaxFileSizeBytes, flushInterval);

            builder.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                options.AddProcessor(new BatchLogRecordExportProcessor(
                    new JsonFileLogExporter(logsWriter),
                    maxQueueSize: 2048,
                    scheduledDelayMilliseconds: (int)flushInterval.TotalMilliseconds,
                    exporterTimeoutMilliseconds: 30000,
                    maxExportBatchSize: 512));
            });
        }
        catch
        {
            // Continue without log telemetry
        }

        return builder;
    }
}
CSHARP

# =============================================================================
# 4. Update MyEmailSearch.csproj
# =============================================================================
echo "[4/6] Updating MyEmailSearch.csproj..."

cat > "$PROJECT_ROOT/MyEmailSearch/MyEmailSearch.csproj" << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <!--
    MyEmailSearch - Email Archive Search Utility
    
    Provides search capabilities over archived emails.
    Uses MyImapDownloader.Core for shared infrastructure.
  -->
  
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MyEmailSearch</RootNamespace>
    <AssemblyName>MyEmailSearch</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- Core library reference -->
    <ProjectReference Include="..\MyImapDownloader.Core\MyImapDownloader.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- CLI Framework -->
    <PackageReference Include="System.CommandLine" />
    
    <!-- Email Parsing -->
    <PackageReference Include="MimeKit" />
    
    <!-- SQLite (additional to Core for FTS5) -->
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
CSPROJ

# =============================================================================
# 5. Create MyEmailSearch DiagnosticsConfig
# =============================================================================
echo "[5/6] Creating MyEmailSearch/Telemetry/DiagnosticsConfig.cs..."

mkdir -p "$PROJECT_ROOT/MyEmailSearch/Telemetry"

cat > "$PROJECT_ROOT/MyEmailSearch/Telemetry/DiagnosticsConfig.cs" << 'CSHARP'
using System.Diagnostics.Metrics;
using MyImapDownloader.Core.Telemetry;

namespace MyEmailSearch.Telemetry;

/// <summary>
/// Application-specific diagnostics configuration for MyEmailSearch.
/// </summary>
public static class DiagnosticsConfig
{
    public const string ServiceName = "MyEmailSearch";
    public const string ServiceVersion = "1.0.0";

    private static readonly DiagnosticsConfigBase _base = new(ServiceName, ServiceVersion);

    public static System.Diagnostics.ActivitySource ActivitySource => _base.ActivitySource;
    public static Meter Meter => _base.Meter;

    // Search metrics
    public static readonly Counter<long> SearchesExecuted = _base.CreateCounter<long>(
        "searches.executed", "queries", "Total search queries executed");

    public static readonly Counter<long> SearchErrors = _base.CreateCounter<long>(
        "searches.errors", "errors", "Search query errors");

    public static readonly Histogram<double> SearchDuration = _base.CreateHistogram<double>(
        "search.duration", "ms", "Search query execution time");

    public static readonly Histogram<long> SearchResultCount = _base.CreateHistogram<long>(
        "search.results", "emails", "Number of results per search");

    // Indexing metrics
    public static readonly Counter<long> EmailsIndexed = _base.CreateCounter<long>(
        "indexing.emails", "emails", "Emails indexed");

    public static readonly Counter<long> IndexingErrors = _base.CreateCounter<long>(
        "indexing.errors", "errors", "Indexing errors");

    public static readonly Histogram<double> IndexingDuration = _base.CreateHistogram<double>(
        "indexing.duration", "ms", "Indexing operation duration");
}
CSHARP

# =============================================================================
# 6. Update test projects
# =============================================================================
echo "[6/6] Updating test projects..."

# Update MyImapDownloader.Tests.csproj
cat > "$PROJECT_ROOT/MyImapDownloader.Tests/MyImapDownloader.Tests.csproj" << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyImapDownloader\MyImapDownloader.csproj" />
    <ProjectReference Include="..\MyImapDownloader.Core\MyImapDownloader.Core.csproj" />
  </ItemGroup>
</Project>
CSPROJ

# Update MyEmailSearch.Tests.csproj
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/MyEmailSearch.Tests.csproj" << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyEmailSearch\MyEmailSearch.csproj" />
    <ProjectReference Include="..\MyImapDownloader.Core\MyImapDownloader.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="TestFixtures\SampleEmails\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
CSPROJ

echo ""
echo "=========================================="
echo "✅ Projects updated to use Core library!"
echo ""
echo "Changes made:"
echo "  - MyImapDownloader now references MyImapDownloader.Core"
echo "  - MyEmailSearch now references MyImapDownloader.Core"
echo "  - DiagnosticsConfig classes use Core's DiagnosticsConfigBase"
echo "  - Test projects reference both app and Core projects"
echo ""
echo "Files you can now delete (duplicates):"
echo "  - MyImapDownloader/Telemetry/TelemetryConfiguration.cs"
echo "  - MyImapDownloader/Telemetry/JsonTelemetryFileWriter.cs"
echo "  - MyImapDownloader/Telemetry/JsonFileLogExporter.cs"
echo "  - MyImapDownloader/Telemetry/JsonFileTraceExporter.cs"
echo "  - MyImapDownloader/Telemetry/JsonFileMetricsExporter.cs"
echo "  - MyImapDownloader/Telemetry/TelemetryDirectoryResolver.cs"
echo "  - MyImapDownloader/Telemetry/ActivityExtension.cs"
echo "  - MyEmailSearch/Configuration/PathResolver.cs (use Core's)"
echo ""
echo "Next steps:"
echo "  1. Delete the duplicate files listed above"
echo "  2. Update 'using' statements in remaining files"
echo "  3. Run: dotnet build"
echo "  4. Run: dotnet test"
echo "=========================================="
