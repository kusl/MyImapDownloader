#!/bin/bash
# =============================================================================
# Setup Script for MyEmailSearch
# =============================================================================
# This script creates the MyEmailSearch project structure within the existing
# MyImapDownloader repository. It demonstrates that NO restructuring is needed.
#
# Prerequisites:
#   - .NET 10 SDK installed
#   - Run from repository root: ~/src/dotnet/MyImapDownloader/
#
# What this script does:
#   1. Creates MyEmailSearch/ and MyEmailSearch.Tests/ directories
#   2. Generates all necessary source files
#   3. Updates Directory.Packages.props with new packages
#   4. Adds projects to the solution
#   5. Builds and tests to verify everything works
#
# Usage:
#   chmod +x setup-myemailsearch.sh
#   ./setup-myemailsearch.sh
# =============================================================================

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

log_info() { echo -e "${CYAN}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
log_warning() { echo -e "${YELLOW}[WARNING]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# =============================================================================
# Verify Prerequisites
# =============================================================================
log_info "Checking prerequisites..."

# Check we're in the right directory
if [ ! -f "MyImapDownloader.sln" ]; then
    log_error "MyImapDownloader.sln not found. Please run from repository root."
    exit 1
fi

# Check .NET SDK
if ! command -v dotnet &> /dev/null; then
    log_error ".NET SDK not found. Please install .NET 10 SDK."
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
log_info "Using .NET SDK version: $DOTNET_VERSION"

# =============================================================================
# Create Directory Structure
# =============================================================================
log_info "Creating directory structure..."

mkdir -p MyEmailSearch/{Commands,Search,Indexing,Data/{Migrations,Repositories},Telemetry,Configuration,Infrastructure}
mkdir -p MyEmailSearch.Tests/{Search,Indexing,Data,Telemetry,Integration,TestFixtures/SampleEmails}

log_success "Directory structure created"

# =============================================================================
# Generate MyEmailSearch.csproj
# =============================================================================
log_info "Creating MyEmailSearch.csproj..."

cat > MyEmailSearch/MyEmailSearch.csproj << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MyEmailSearch</RootNamespace>
    <AssemblyName>MyEmailSearch</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" />
    <PackageReference Include="Microsoft.Data.Sqlite" />
    <PackageReference Include="MimeKit" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
    <PackageReference Include="OpenTelemetry" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="Polly" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
CSPROJ

# =============================================================================
# Generate MyEmailSearch.Tests.csproj
# =============================================================================
log_info "Creating MyEmailSearch.Tests.csproj..."

cat > MyEmailSearch.Tests/MyEmailSearch.Tests.csproj << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="TUnit" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyEmailSearch\MyEmailSearch.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="TestFixtures\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
CSPROJ

# =============================================================================
# Generate appsettings.json
# =============================================================================
log_info "Creating appsettings.json..."

cat > MyEmailSearch/appsettings.json << 'JSON'
{
  "Search": {
    "DefaultResultLimit": 100,
    "MaxResultLimit": 1000,
    "SnippetLength": 200,
    "EnableContentSearch": true
  },
  "Indexing": {
    "BatchSize": 500,
    "ContentIndexingEnabled": true,
    "ParallelismDegree": 4
  },
  "Archive": {
    "BasePath": "",
    "AutoDetect": true
  },
  "Database": {
    "CacheSizeMB": 64,
    "MmapSizeMB": 256,
    "WalEnabled": true
  },
  "Telemetry": {
    "ServiceName": "MyEmailSearch",
    "ServiceVersion": "1.0.0",
    "EnableTracing": true,
    "EnableMetrics": true,
    "EnableLogging": true,
    "MaxFileSizeMB": 25,
    "FlushIntervalSeconds": 5,
    "ExportToSqlite": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  }
}
JSON

# =============================================================================
# Generate Program.cs (Entry Point)
# =============================================================================
log_info "Creating Program.cs..."

cat > MyEmailSearch/Program.cs << 'CSHARP'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Commands;

namespace MyEmailSearch;

/// <summary>
/// MyEmailSearch - Email Archive Search Utility
/// 
/// A companion tool for MyImapDownloader that enables fast searching
/// across archived emails using SQLite FTS5 full-text search.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Build the root command with subcommands
        var rootCommand = new RootCommand("MyEmailSearch - Search your email archive")
        {
            SearchCommand.Create(),
            IndexCommand.Create(),
            StatusCommand.Create(),
            RebuildCommand.Create()
        };

        // Add global options
        var archiveOption = new Option<string?>(
            aliases: ["--archive", "-a"],
            description: "Path to the email archive directory");
        
        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output");

        rootCommand.AddGlobalOption(archiveOption);
        rootCommand.AddGlobalOption(verboseOption);

        return await rootCommand.InvokeAsync(args);
    }
}
CSHARP

# =============================================================================
# Generate Command Files
# =============================================================================
log_info "Creating command handlers..."

# SearchCommand.cs
cat > MyEmailSearch/Commands/SearchCommand.cs << 'CSHARP'
using System.CommandLine;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'search' command for querying the email index.
/// </summary>
public static class SearchCommand
{
    public static Command Create()
    {
        var queryArgument = new Argument<string>(
            name: "query",
            description: "Search query (e.g., 'from:alice@example.com subject:report kafka')");

        var limitOption = new Option<int>(
            aliases: ["--limit", "-l"],
            getDefaultValue: () => 100,
            description: "Maximum number of results to return");

        var formatOption = new Option<string>(
            aliases: ["--format", "-f"],
            getDefaultValue: () => "table",
            description: "Output format: table, json, or csv");

        var command = new Command("search", "Search emails in the archive")
        {
            queryArgument,
            limitOption,
            formatOption
        };

        command.SetHandler(async (query, limit, format, ct) =>
        {
            await ExecuteAsync(query, limit, format, ct);
        }, queryArgument, limitOption, formatOption, CancellationToken.None);

        return command;
    }

    private static async Task ExecuteAsync(
        string query,
        int limit,
        string format,
        CancellationToken ct)
    {
        Console.WriteLine($"Searching for: {query}");
        Console.WriteLine($"Limit: {limit}, Format: {format}");
        
        // TODO: Implement search logic
        await Task.CompletedTask;
    }
}
CSHARP

# IndexCommand.cs
cat > MyEmailSearch/Commands/IndexCommand.cs << 'CSHARP'
using System.CommandLine;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'index' command for building/updating the search index.
/// </summary>
public static class IndexCommand
{
    public static Command Create()
    {
        var fullOption = new Option<bool>(
            aliases: ["--full"],
            description: "Perform full reindex instead of incremental update");

        var contentOption = new Option<bool>(
            aliases: ["--content"],
            description: "Also index email body content (slower but enables full-text search)");

        var command = new Command("index", "Build or update the search index")
        {
            fullOption,
            contentOption
        };

        command.SetHandler(async (full, content, ct) =>
        {
            await ExecuteAsync(full, content, ct);
        }, fullOption, contentOption, CancellationToken.None);

        return command;
    }

    private static async Task ExecuteAsync(
        bool full,
        bool content,
        CancellationToken ct)
    {
        Console.WriteLine($"Indexing... Full: {full}, Content: {content}");
        
        // TODO: Implement indexing logic
        await Task.CompletedTask;
    }
}
CSHARP

# StatusCommand.cs
cat > MyEmailSearch/Commands/StatusCommand.cs << 'CSHARP'
using System.CommandLine;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'status' command to show index statistics.
/// </summary>
public static class StatusCommand
{
    public static Command Create()
    {
        var command = new Command("status", "Show index status and statistics");

        command.SetHandler(async (ct) =>
        {
            await ExecuteAsync(ct);
        }, CancellationToken.None);

        return command;
    }

    private static async Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine("Index Status:");
        Console.WriteLine("=============");
        Console.WriteLine("  Total emails indexed: (not yet implemented)");
        Console.WriteLine("  Index size: (not yet implemented)");
        Console.WriteLine("  Last updated: (not yet implemented)");
        
        await Task.CompletedTask;
    }
}
CSHARP

# RebuildCommand.cs
cat > MyEmailSearch/Commands/RebuildCommand.cs << 'CSHARP'
using System.CommandLine;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'rebuild' command to rebuild the index from scratch.
/// </summary>
public static class RebuildCommand
{
    public static Command Create()
    {
        var confirmOption = new Option<bool>(
            aliases: ["--yes", "-y"],
            description: "Skip confirmation prompt");

        var command = new Command("rebuild", "Rebuild the entire search index from scratch")
        {
            confirmOption
        };

        command.SetHandler(async (confirm, ct) =>
        {
            await ExecuteAsync(confirm, ct);
        }, confirmOption, CancellationToken.None);

        return command;
    }

    private static async Task ExecuteAsync(bool confirm, CancellationToken ct)
    {
        if (!confirm)
        {
            Console.Write("This will delete and rebuild the entire index. Continue? [y/N]: ");
            var response = Console.ReadLine();
            if (!string.Equals(response, "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Cancelled.");
                return;
            }
        }

        Console.WriteLine("Rebuilding index...");
        
        // TODO: Implement rebuild logic
        await Task.CompletedTask;
    }
}
CSHARP

# =============================================================================
# Generate Basic Test File
# =============================================================================
log_info "Creating initial test file..."

cat > MyEmailSearch.Tests/SmokeTests.cs << 'CSHARP'
namespace MyEmailSearch.Tests;

/// <summary>
/// Basic smoke tests to verify the project builds and runs.
/// </summary>
public class SmokeTests
{
    [Test]
    public async Task Application_ShouldCompileAndRun()
    {
        // This test simply verifies the project compiles
        // More comprehensive tests will be added as features are implemented
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task SearchCommand_ShouldExist()
    {
        // Verify the search command can be created
        var command = Commands.SearchCommand.Create();
        await Assert.That(command).IsNotNull();
        await Assert.That(command.Name).IsEqualTo("search");
    }

    [Test]
    public async Task IndexCommand_ShouldExist()
    {
        var command = Commands.IndexCommand.Create();
        await Assert.That(command).IsNotNull();
        await Assert.That(command.Name).IsEqualTo("index");
    }

    [Test]
    public async Task StatusCommand_ShouldExist()
    {
        var command = Commands.StatusCommand.Create();
        await Assert.That(command).IsNotNull();
        await Assert.That(command.Name).IsEqualTo("status");
    }

    [Test]
    public async Task RebuildCommand_ShouldExist()
    {
        var command = Commands.RebuildCommand.Create();
        await Assert.That(command).IsNotNull();
        await Assert.That(command.Name).IsEqualTo("rebuild");
    }
}
CSHARP

# =============================================================================
# Update Directory.Packages.props
# =============================================================================
log_info "Updating Directory.Packages.props..."

# Backup existing file
cp Directory.Packages.props Directory.Packages.props.bak

cat > Directory.Packages.props << 'XML'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- =========================================================================
         SHARED PACKAGES (used by multiple projects)
         ========================================================================= -->
    
    <!-- Microsoft.Extensions.* - Core infrastructure -->
    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.UserSecrets" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Console" Version="10.0.1" />
    
    <!-- Database -->
    <PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.1" />
    
    <!-- OpenTelemetry -->
    <PackageVersion Include="OpenTelemetry" Version="1.14.0" />
    <PackageVersion Include="OpenTelemetry.Exporter.Console" Version="1.14.0" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.14.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.14.0" />
    
    <!-- Resilience -->
    <PackageVersion Include="Polly" Version="8.6.0" />
    
    <!-- CLI -->
    <PackageVersion Include="CommandLineParser" Version="2.9.1" />
    <PackageVersion Include="System.CommandLine" Version="2.0.0-beta5.25306.1" />
    
    <!-- =========================================================================
         MyImapDownloader SPECIFIC PACKAGES
         ========================================================================= -->
    <PackageVersion Include="MailKit" Version="4.14.1" />
    <PackageVersion Include="Dapper" Version="2.1.66" />
    <PackageVersion Include="Microsoft.Data.SqlClient" Version="6.0.1" />
    
    <!-- =========================================================================
         MyEmailSearch SPECIFIC PACKAGES
         ========================================================================= -->
    <PackageVersion Include="MimeKit" Version="4.14.1" />
    
    <!-- =========================================================================
         TEST PACKAGES (shared by all test projects)
         ========================================================================= -->
    <PackageVersion Include="TUnit" Version="1.7.7" />
    <PackageVersion Include="NSubstitute" Version="5.3.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
  </ItemGroup>
</Project>
XML

# =============================================================================
# Add Projects to Solution
# =============================================================================
log_info "Adding projects to solution..."

dotnet sln MyImapDownloader.sln add MyEmailSearch/MyEmailSearch.csproj
dotnet sln MyImapDownloader.sln add MyEmailSearch.Tests/MyEmailSearch.Tests.csproj

log_success "Projects added to solution"

# =============================================================================
# Build and Test
# =============================================================================
log_info "Restoring packages..."
dotnet restore

log_info "Building solution..."
dotnet build --configuration Release

log_info "Running tests..."
dotnet test --configuration Release --verbosity normal

# =============================================================================
# Summary
# =============================================================================
echo ""
log_success "=============================================="
log_success "MyEmailSearch setup complete!"
log_success "=============================================="
echo ""
echo "Project structure:"
echo "  MyEmailSearch/              - Main search application"
echo "  MyEmailSearch.Tests/        - Unit tests"
echo ""
echo "Next steps:"
echo "  1. Implement search engine in MyEmailSearch/Search/"
echo "  2. Implement indexing in MyEmailSearch/Indexing/"
echo "  3. Add database layer in MyEmailSearch/Data/"
echo "  4. Write comprehensive tests"
echo ""
echo "Commands:"
echo "  dotnet run --project MyEmailSearch -- search 'from:alice'"
echo "  dotnet run --project MyEmailSearch -- index"
echo "  dotnet run --project MyEmailSearch -- status"
echo "  dotnet run --project MyEmailSearch -- rebuild"
echo ""
