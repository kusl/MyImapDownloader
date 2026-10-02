#!/usr/bin/env bash
set -euo pipefail
cd "${1:-$HOME/src/dotnet/MyImapDownloader}"

rm -f "MyEmailSearch/Telemetry/DiagnosticsConfig.cs"
rm -f "MyEmailSearch/appsettings.json"
rm -f "MyImapDownloader.Core.Tests/Data/EmailMetadataTests.cs"
rm -f "MyImapDownloader.Core/Data/EmailMetadata.cs"
rm -f "MyImapDownloader.Core/Infrastructure/TestLogger.cs"
rm -f "MyImapDownloader.Tests/Telemetry/JsonExporterTests.cs"
rm -f "MyImapDownloader.Tests/Telemetry/JsonTelemetryFileWriterTests.cs"
rm -f "MyImapDownloader.Tests/Telemetry/TelemetryDirectoryResolverTests.cs"
rm -f "MyImapDownloader/MyImapDownloader/DownloadCommand.cs"
rm -f "MyImapDownloader/Telemetry/JsonFileLogExporter.cs"
rm -f "MyImapDownloader/Telemetry/JsonFileMetricsExporter.cs"
rm -f "MyImapDownloader/Telemetry/JsonFileTraceExporter.cs"
rm -f "MyImapDownloader/Telemetry/JsonTelemetryFileWriter.cs"
rm -f "MyImapDownloader/Telemetry/TelemetryDirectoryResolver.cs"
rm -f "fix.sh"
rmdir MyImapDownloader/MyImapDownloader MyImapDownloader.Core/Data MyImapDownloader.Core.Tests/Data MyEmailSearch/Telemetry 2>/dev/null || true

mkdir -p "$(dirname ".github/workflows/release.yml")"
cat > ".github/workflows/release.yml" <<'MYIMAP_EOF'
name: Rolling Release

on:
  push:
    branches: [main, master, develop]

jobs:
  build-and-test:
    name: ${{ matrix.os }}
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]

    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
          dotnet-quality: 'ga'

      - name: Display .NET info
        run: dotnet --info

      - name: Restore dependencies
        run: dotnet restore

      - name: Build solution
        run: dotnet build --no-restore --configuration Release

      - name: Run tests
        run: dotnet test --no-build --configuration Release --verbosity normal

      - name: Upload test results
        uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results-${{ matrix.os }}
          path: '**/TestResults/**'
          retention-days: 7

  publish:
    name: Generate Rolling Release
    needs: build-and-test
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Checkout repository
        uses: actions/checkout@v4

      - name: Set Version String
        id: version
        run: echo "REL_VERSION=$(date +'%Y.%m.%d').${{ github.run_number }}" >> "$GITHUB_OUTPUT"

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Publish Full-Fat Binaries
        env:
          REL_VERSION: ${{ steps.version.outputs.REL_VERSION }}
        run: |
          set -euo pipefail
          for runtime in linux-x64 win-x64 osx-arm64 osx-x64; do
            echo "--- Processing $runtime ---"

            dotnet publish MyImapDownloader/MyImapDownloader.csproj -c Release -r "$runtime" \
              --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false \
              -p:OutputExtensionsInBundle=true \
              -p:IncludeNativeLibrariesForSelfExtract=true \
              -p:Version="$REL_VERSION" \
              -o "./dist/downloader-$runtime"

            dotnet publish MyEmailSearch/MyEmailSearch.csproj -c Release -r "$runtime" \
              --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false \
              -p:OutputExtensionsInBundle=true \
              -p:IncludeNativeLibrariesForSelfExtract=true \
              -p:Version="$REL_VERSION" \
              -o "./dist/search-$runtime"

            (cd "./dist/downloader-$runtime" && zip -r "../../MyImapDownloader-$runtime-$REL_VERSION.zip" .)
            (cd "./dist/search-$runtime" && zip -r "../../MyEmailSearch-$runtime-$REL_VERSION.zip" .)
          done

      - name: Create Rolling Release
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
          REL_VERSION: ${{ steps.version.outputs.REL_VERSION }}
        run: |
          set -euo pipefail
          {
            echo "### Automated Rolling Release"
            echo "- **Version:** \`$REL_VERSION\`"
            echo "- **Commit:** \`$GITHUB_SHA\`"
            echo "- **Build Date:** $(date -u +'%Y-%m-%d %H:%M:%S') UTC"
            echo ""
            echo "Self-contained build. No .NET runtime installation is required on the target machine."
          } > release-notes.md
          gh release create "rolling-build-$REL_VERSION" ./*.zip \
            --title "Rolling Release: $REL_VERSION" \
            --notes-file release-notes.md \
            --target "$GITHUB_SHA" \
            --latest
MYIMAP_EOF

mkdir -p "$(dirname "Directory.Build.targets")"
cat > "Directory.Build.targets" <<'MYIMAP_EOF'
<Project>
  <Target Name="DisplayBuildInfo" BeforeTargets="Build">
    <Message Importance="high" Text="Building $(MSBuildProjectName) - $(Configuration) - $(TargetFramework)" />
  </Target>

  <Target Name="CleanArtifacts" BeforeTargets="Clean">
    <RemoveDir Directories="$(BaseOutputPath)" Condition="Exists('$(BaseOutputPath)')" />
    <RemoveDir Directories="$(BaseIntermediateOutputPath)" Condition="Exists('$(BaseIntermediateOutputPath)')" />
  </Target>
</Project>
MYIMAP_EOF

mkdir -p "$(dirname "Directory.Packages.props")"
cat > "Directory.Packages.props" <<'MYIMAP_EOF'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <MicrosoftExtensionsVersion>10.0.12</MicrosoftExtensionsVersion>
    <OpenTelemetryVersion>1.19.1</OpenTelemetryVersion>
    <OpenTelemetryRuntimeVersion>1.19.0</OpenTelemetryRuntimeVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="$(MicrosoftExtensionsVersion)" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Binder" Version="$(MicrosoftExtensionsVersion)" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="$(MicrosoftExtensionsVersion)" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="$(MicrosoftExtensionsVersion)" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="$(MicrosoftExtensionsVersion)" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="$(MicrosoftExtensionsVersion)" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Console" Version="$(MicrosoftExtensionsVersion)" />
    <PackageVersion Include="Microsoft.Data.Sqlite" Version="$(MicrosoftExtensionsVersion)" />

    <PackageVersion Include="OpenTelemetry" Version="$(OpenTelemetryVersion)" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="$(OpenTelemetryVersion)" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="$(OpenTelemetryRuntimeVersion)" />

    <PackageVersion Include="Polly" Version="8.8.0" />
    <PackageVersion Include="System.CommandLine" Version="2.0.12" />
    <PackageVersion Include="MailKit" Version="4.18.1" />
    <PackageVersion Include="MimeKit" Version="4.18.1" />

    <PackageVersion Include="TUnit" Version="1.72.16" />
    <PackageVersion Include="NSubstitute" Version="6.2.0" />
    <PackageVersion Include="AwesomeAssertions" Version="9.6.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
  </ItemGroup>
</Project>
MYIMAP_EOF

mkdir -p "$(dirname "MyEmailSearch.Tests/Data/Fts5HelperTests.cs")"
cat > "MyEmailSearch.Tests/Data/Fts5HelperTests.cs" <<'MYIMAP_EOF'
using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Data;

public class Fts5HelperTests
{
    [Test]
    public async Task PrepareFts5MatchQuery_WithNull_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery(null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithEmptyString_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWhitespace_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("   ");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithOnlyWildcard_ReturnsNull()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("*");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithWildcard_PreservesWildcard()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test*");

        await Assert.That(result).IsEqualTo("\"test\"*");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithoutWildcard_WrapsInQuotes()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test query");

        await Assert.That(result).IsEqualTo("\"test query\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithFts5Operators_EscapesThem()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test OR hack");

        await Assert.That(result).IsEqualTo("\"test OR hack\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithParentheses_EscapesThem()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("(test)");

        await Assert.That(result).IsEqualTo("\"(test)\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithEmbeddedQuote_DoublesIt()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("test\"query");

        await Assert.That(result).IsEqualTo("\"test\"\"query\"");
    }

    [Test]
    public async Task PrepareFts5MatchQuery_WithQuotedPhraseAndWildcard_EscapesAndKeepsWildcard()
    {
        var result = SearchDatabase.PrepareFts5MatchQuery("\"exact phrase\"*");

        await Assert.That(result).IsEqualTo("\"\"\"exact phrase\"\"\"*");
    }

    [Test]
    public async Task QueryAsync_WithUnbalancedQuote_DoesNotThrowAndFindsMatch()
    {
        using var temp = new TempDirectory("fts_quote_test");
        await using var db = new SearchDatabase(
            Path.Combine(temp.Path, "search.db"),
            NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "quote@example.com",
            FilePath = "/test/quote.eml",
            Subject = "Quarterly report",
            BodyText = "the quarterly report is attached",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var results = await db.QueryAsync(new SearchQuery { ContentTerms = "quarterly\" report" });

        await Assert.That(results.Count).IsEqualTo(1);
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyEmailSearch/Commands/SearchCommand.cs")"
cat > "MyEmailSearch/Commands/SearchCommand.cs" <<'MYIMAP_EOF'
using System.CommandLine;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Commands;

public static class SearchCommand
{
    public static Command Create(
        Option<string?> archiveOption,
        Option<string?> databaseOption,
        Option<bool> verboseOption)
    {
        var queryArgument = new Argument<string>("query")
        {
            Description = "Search query (e.g., 'from:alice@example.com subject:report kafka')"
        };

        var limitOption = new Option<int>("--limit", "-l")
        {
            Description = "Maximum number of results to return",
            DefaultValueFactory = _ => 100
        };

        var formatOption = new Option<string>("--format", "-f")
        {
            Description = "Output format: table, json, or csv",
            DefaultValueFactory = _ => "table"
        };

        var openOption = new Option<bool>("--open", "-o")
        {
            Description = "Interactively select and open an email in your default application",
            DefaultValueFactory = _ => false
        };

        var command = new Command("search", "Search emails in the archive");
        command.Arguments.Add(queryArgument);
        command.Options.Add(limitOption);
        command.Options.Add(formatOption);
        command.Options.Add(openOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(queryArgument)!;
            var limit = parseResult.GetValue(limitOption);
            var format = parseResult.GetValue(formatOption)!;
            var openInteractive = parseResult.GetValue(openOption);
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            return await ExecuteAsync(query, limit, format, openInteractive, archivePath, databasePath, verbose, ct)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string query,
        int limit,
        string format,
        bool openInteractive,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("Error: Search query cannot be empty");
            return 1;
        }

        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"Error: No index exists at {databasePath}");
            Console.Error.WriteLine("Run 'myemailsearch index' first to create the index.");
            return 1;
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();
        var searchEngine = sp.GetRequiredService<SearchEngine>();

        await database.InitializeAsync(ct).ConfigureAwait(false);

        var results = await searchEngine.SearchAsync(query, limit, 0, ct).ConfigureAwait(false);

        try
        {
            if (openInteractive && results.Results.Count > 0)
            {
                await HandleInteractiveOpenAsync(results, ct).ConfigureAwait(false);
            }
            else
            {
                switch (format.ToLowerInvariant())
                {
                    case "json":
                        OutputJson(results);
                        break;
                    case "csv":
                        OutputCsv(results);
                        break;
                    default:
                        OutputTable(results);
                        break;
                }
            }
        }
        catch (IOException ex)
        {
            if (verbose)
            {
                Console.Error.WriteLine($"Output error: {ex.Message}");
            }
        }

        return 0;
    }

    private static async Task HandleInteractiveOpenAsync(SearchResultSet results, CancellationToken ct)
    {
        Console.WriteLine($"Found {results.TotalCount} results ({results.QueryTime.TotalMilliseconds:F0}ms):");
        Console.WriteLine();

        var displayCount = Math.Min(results.Results.Count, 20);
        for (var i = 0; i < displayCount; i++)
        {
            var result = results.Results[i];
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd") ?? "Unknown";
            var from = TruncateString(result.Email.FromAddress ?? "Unknown", 25);
            var subject = TruncateString(result.Email.Subject ?? "(no subject)", 45);

            Console.WriteLine($"[{i + 1,2}] {date}  {from,-25}  {subject}");
        }

        if (results.TotalCount > displayCount)
        {
            Console.WriteLine($"... and {results.TotalCount - displayCount} more (use --limit to see more)");
        }

        Console.WriteLine();
        Console.Write($"Open which result? (1-{displayCount}, or q to quit): ");

        var input = await ReadLineAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(input) || input.Trim().ToLowerInvariant() == "q")
        {
            Console.WriteLine("Cancelled.");
            return;
        }

        if (!int.TryParse(input.Trim(), out var selection) || selection < 1 || selection > displayCount)
        {
            Console.Error.WriteLine($"Invalid selection. Please enter a number between 1 and {displayCount}.");
            return;
        }

        var selectedResult = results.Results[selection - 1];
        var filePath = selectedResult.Email.FilePath;

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: Email file not found: {filePath}");
            return;
        }

        Console.WriteLine($"Opening: {filePath}");
        OpenFileWithDefaultApplication(filePath);
    }

    private static async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                return Console.ReadLine();
            }
            catch (IOException)
            {
                return null;
            }
        }, ct).ConfigureAwait(false);
    }

    private static void OpenFileWithDefaultApplication(string filePath)
    {
        try
        {
            ProcessStartInfo psi;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "cmd",
                    Arguments = $"/c start \"\" \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                Console.Error.WriteLine("Unsupported platform for opening files.");
                return;
            }

            using var process = Process.Start(psi);
            process?.WaitForExit(1000);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error opening file: {ex.Message}");
        }
    }

    private static void OutputTable(SearchResultSet results)
    {
        if (results.TotalCount == 0)
        {
            Console.WriteLine("No results found.");
            return;
        }

        Console.WriteLine($"Found {results.TotalCount} results ({results.QueryTime.TotalMilliseconds:F0}ms):");
        Console.WriteLine();
        Console.WriteLine($"{"Date",-12} {"From",-30} {"Subject",-50}");
        Console.WriteLine(new string('-', 94));

        foreach (var result in results.Results)
        {
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd") ?? "Unknown";
            var from = TruncateString(result.Email.FromAddress ?? "Unknown", 28);
            var subject = TruncateString(result.Email.Subject ?? "(no subject)", 48);

            Console.WriteLine($"{date,-12} {from,-30} {subject,-50}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                var snippet = TruncateString(result.Snippet.Replace("\n", " ").Replace("\r", ""), 80);
                Console.WriteLine($"             {snippet}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Showing {results.Results.Count} of {results.TotalCount} results");
    }

    private static void OutputJson(SearchResultSet results)
    {
        Console.WriteLine(JsonSerializer.Serialize(results, SearchOutputJsonContext.Default.SearchResultSet));
    }

    private static void OutputCsv(SearchResultSet results)
    {
        Console.WriteLine("MessageId,From,Subject,Date,Folder,Account,FilePath");
        foreach (var result in results.Results)
        {
            var messageId = EscapeCsvField(result.Email.MessageId ?? "");
            var from = EscapeCsvField(result.Email.FromAddress ?? "");
            var subject = EscapeCsvField(result.Email.Subject ?? "");
            var date = result.Email.DateSent?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
            var folder = EscapeCsvField(result.Email.Folder ?? "");
            var account = EscapeCsvField(result.Email.Account ?? "");
            var filePath = EscapeCsvField(result.Email.FilePath);

            Console.WriteLine($"{messageId},{from},{subject},\"{date}\",{folder},{account},{filePath}");
        }
    }

    private static string TruncateString(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }

    private static string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyEmailSearch/Data/EmailDocument.cs")"
cat > "MyEmailSearch/Data/EmailDocument.cs" <<'MYIMAP_EOF'
using System.Text.Json;

namespace MyEmailSearch.Data;

public sealed class EmailDocument
{
    public long Id { get; set; }
    public required string MessageId { get; init; }
    public required string FilePath { get; init; }
    public string? FromAddress { get; init; }
    public string? FromName { get; init; }
    public string? ToAddressesJson { get; init; }
    public string? CcAddressesJson { get; init; }
    public string? BccAddressesJson { get; init; }
    public string? Subject { get; init; }
    public long? DateSentUnix { get; init; }
    public long? DateReceivedUnix { get; init; }
    public string? Folder { get; init; }
    public string? Account { get; init; }
    public bool HasAttachments { get; init; }
    public string? AttachmentNamesJson { get; init; }
    public string? BodyPreview { get; init; }
    public string? BodyText { get; init; }
    public long IndexedAtUnix { get; init; }
    public long LastModifiedTicks { get; init; }

    public DateTimeOffset? DateSent => DateSentUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateSentUnix.Value)
        : null;

    public DateTimeOffset? DateReceived => DateReceivedUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateReceivedUnix.Value)
        : null;

    public IReadOnlyList<string> ToAddresses => ParseJsonArray(ToAddressesJson);
    public IReadOnlyList<string> CcAddresses => ParseJsonArray(CcAddressesJson);
    public IReadOnlyList<string> BccAddresses => ParseJsonArray(BccAddressesJson);
    public IReadOnlyList<string> AttachmentNames => ParseJsonArray(AttachmentNamesJson);

    private static IReadOnlyList<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize(json, EmailDocumentJsonContext.Default.ListString) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string ToJsonArray(IEnumerable<string?> items)
    {
        var list = items.OfType<string>().ToList();
        return list.Count > 0 ? JsonSerializer.Serialize(list, EmailDocumentJsonContext.Default.ListString) : "";
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyEmailSearch/Data/SearchDatabase.cs")"
cat > "MyEmailSearch/Data/SearchDatabase.cs" <<'MYIMAP_EOF'
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyEmailSearch.Data;

public sealed partial class SearchDatabase(string databasePath, ILogger<SearchDatabase> logger) : IAsyncDisposable
{
    private readonly string _connectionString = $"Data Source={databasePath}";
    private readonly ILogger<SearchDatabase> _logger = logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    private string DatabasePath { get; } = databasePath;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        const string schema = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS emails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id TEXT NOT NULL,
                file_path TEXT NOT NULL UNIQUE,
                from_address TEXT,
                from_name TEXT,
                to_addresses TEXT,
                cc_addresses TEXT,
                bcc_addresses TEXT,
                subject TEXT,
                date_sent_unix INTEGER,
                date_received_unix INTEGER,
                folder TEXT,
                account TEXT,
                has_attachments INTEGER DEFAULT 0,
                attachment_names TEXT,
                body_preview TEXT,
                body_text TEXT,
                indexed_at_unix INTEGER NOT NULL,
                last_modified_ticks INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_emails_from ON emails(from_address);
            CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date_sent_unix);
            CREATE INDEX IF NOT EXISTS idx_emails_folder ON emails(folder);
            CREATE INDEX IF NOT EXISTS idx_emails_account ON emails(account);
            CREATE INDEX IF NOT EXISTS idx_emails_message_id ON emails(message_id);

            CREATE VIRTUAL TABLE IF NOT EXISTS emails_fts USING fts5(
                subject,
                body_text,
                from_address,
                to_addresses,
                content='emails',
                content_rowid='id',
                tokenize='porter unicode61'
            );

            CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON emails BEGIN
                INSERT INTO emails_fts(rowid, subject, body_text, from_address, to_addresses)
                VALUES (new.id, new.subject, new.body_text, new.from_address, new.to_addresses);
            END;

            CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, subject, body_text, from_address, to_addresses)
                VALUES ('delete', old.id, old.subject, old.body_text, old.from_address, old.to_addresses);
            END;

            CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, subject, body_text, from_address, to_addresses)
                VALUES ('delete', old.id, old.subject, old.body_text, old.from_address, old.to_addresses);
                INSERT INTO emails_fts(rowid, subject, body_text, from_address, to_addresses)
                VALUES (new.id, new.subject, new.body_text, new.from_address, new.to_addresses);
            END;

            CREATE TABLE IF NOT EXISTS index_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;

        await ExecuteNonQueryAsync(schema, ct).ConfigureAwait(false);
    }

    public async Task<List<EmailDocument>> QueryAsync(SearchQuery query, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        AddQueryConditions(query, conditions, parameters);

        string sql;
        var ftsQuery = PrepareFts5MatchQuery(query.ContentTerms);

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            var whereClause = conditions.Count > 0
                ? $"AND {string.Join(" AND ", conditions)}" : "";

            sql = $"""
                SELECT emails.*
                FROM emails
                INNER JOIN emails_fts ON emails.id = emails_fts.rowid
                WHERE emails_fts MATCH @ftsQuery {whereClause}
                ORDER BY bm25(emails_fts)
                LIMIT @limit OFFSET @offset;
                """;
            parameters["@ftsQuery"] = ftsQuery;
        }
        else
        {
            var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";

            sql = $"""
                SELECT * FROM emails
                {whereClause}
                ORDER BY date_sent_unix DESC
                LIMIT @limit OFFSET @offset;
                """;
        }

        parameters["@limit"] = query.Take;
        parameters["@offset"] = query.Skip;

        var results = new List<EmailDocument>();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        foreach (var (key, value) in parameters)
        {
            cmd.Parameters.AddWithValue(key, value);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapToEmailDocument(reader));
        }

        return results;
    }

    public async Task<int> GetTotalCountForQueryAsync(SearchQuery query, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object>();

        AddQueryConditions(query, conditions, parameters);

        string sql;
        var ftsQuery = PrepareFts5MatchQuery(query.ContentTerms);

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            var whereClause = conditions.Count > 0
                ? $"AND {string.Join(" AND ", conditions)}" : "";

            sql = $"""
                SELECT COUNT(*)
                FROM emails
                INNER JOIN emails_fts ON emails.id = emails_fts.rowid
                WHERE emails_fts MATCH @ftsQuery {whereClause};
                """;
            parameters["@ftsQuery"] = ftsQuery;
        }
        else
        {
            var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";

            sql = $"""
                SELECT COUNT(*) FROM emails
                {whereClause};
                """;
        }

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        foreach (var (key, value) in parameters)
        {
            cmd.Parameters.AddWithValue(key, value);
        }

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    private static void AddQueryConditions(SearchQuery query, List<string> conditions, Dictionary<string, object> parameters)
    {
        if (!string.IsNullOrWhiteSpace(query.FromAddress))
        {
            if (query.FromAddress.Contains('*'))
            {
                conditions.Add("emails.from_address LIKE @fromAddress");
                parameters["@fromAddress"] = query.FromAddress.Replace('*', '%');
            }
            else
            {
                conditions.Add("emails.from_address = @fromAddress");
                parameters["@fromAddress"] = query.FromAddress;
            }
        }

        if (!string.IsNullOrWhiteSpace(query.ToAddress))
        {
            conditions.Add("emails.to_addresses LIKE @toAddress");
            parameters["@toAddress"] = $"%{query.ToAddress}%";
        }

        if (!string.IsNullOrWhiteSpace(query.Subject))
        {
            conditions.Add("emails.subject LIKE @subject");
            parameters["@subject"] = $"%{query.Subject}%";
        }

        if (query.DateFrom.HasValue)
        {
            conditions.Add("emails.date_sent_unix >= @dateFrom");
            parameters["@dateFrom"] = query.DateFrom.Value.ToUnixTimeSeconds();
        }

        if (query.DateTo.HasValue)
        {
            conditions.Add("emails.date_sent_unix <= @dateTo");
            parameters["@dateTo"] = query.DateTo.Value.ToUnixTimeSeconds();
        }

        if (!string.IsNullOrWhiteSpace(query.Account))
        {
            conditions.Add("emails.account = @account");
            parameters["@account"] = query.Account;
        }

        if (!string.IsNullOrWhiteSpace(query.Folder))
        {
            conditions.Add("emails.folder = @folder");
            parameters["@folder"] = query.Folder;
        }
    }

    public static string? PrepareFts5MatchQuery(string? searchTerms)
    {
        if (string.IsNullOrWhiteSpace(searchTerms)) return null;
        var trimmed = searchTerms.Trim();
        var hasWildcard = trimmed.EndsWith('*');
        if (hasWildcard) trimmed = trimmed[..^1].TrimEnd();
        if (trimmed.Length == 0) return null;
        var escaped = EscapeFts5Query(trimmed);
        return hasWildcard ? escaped + "*" : escaped;
    }

    public static string? EscapeFts5Query(string? input)
    {
        if (input == null) return null;
        if (string.IsNullOrEmpty(input)) return "";
        var escaped = input.Replace("\"", "\"\"");
        return "\"" + escaped + "\"";
    }

    public async Task<long> GetEmailCountAsync(CancellationToken ct = default)
    {
        return await ExecuteScalarLongAsync("SELECT COUNT(*) FROM emails;", ct).ConfigureAwait(false);
    }

    public async Task<long> GetTotalCountAsync(CancellationToken ct = default)
    {
        return await GetEmailCountAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            await ExecuteScalarLongAsync("SELECT 1;", ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetMetadataAsync(string key, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        const string sql = "SELECT value FROM index_metadata WHERE key = @key;";
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result?.ToString();
    }

    public async Task SetMetadataAsync(string key, string value, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        const string sql = """
            INSERT INTO index_metadata (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = @value;
            """;
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertEmailAsync(EmailDocument email, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        const string sql = """
            INSERT INTO emails (
                message_id, file_path, from_address, from_name, to_addresses, cc_addresses, bcc_addresses,
                subject, date_sent_unix, date_received_unix, folder, account, has_attachments,
                attachment_names, body_preview, body_text, indexed_at_unix, last_modified_ticks
            ) VALUES (
                @messageId, @filePath, @fromAddress, @fromName, @toAddresses, @ccAddresses, @bccAddresses,
                @subject, @dateSentUnix, @dateReceivedUnix, @folder, @account, @hasAttachments,
                @attachmentNames, @bodyPreview, @bodyText, @indexedAtUnix, @lastModifiedTicks
            )
            ON CONFLICT(file_path) DO UPDATE SET
                message_id = @messageId,
                from_address = @fromAddress,
                from_name = @fromName,
                to_addresses = @toAddresses,
                cc_addresses = @ccAddresses,
                bcc_addresses = @bccAddresses,
                subject = @subject,
                date_sent_unix = @dateSentUnix,
                date_received_unix = @dateReceivedUnix,
                folder = @folder,
                account = @account,
                has_attachments = @hasAttachments,
                attachment_names = @attachmentNames,
                body_preview = @bodyPreview,
                body_text = @bodyText,
                indexed_at_unix = @indexedAtUnix,
                last_modified_ticks = @lastModifiedTicks;
            """;

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@messageId", email.MessageId);
        cmd.Parameters.AddWithValue("@filePath", email.FilePath);
        cmd.Parameters.AddWithValue("@fromAddress", (object?)email.FromAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fromName", (object?)email.FromName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@toAddresses", (object?)email.ToAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ccAddresses", (object?)email.CcAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bccAddresses", (object?)email.BccAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@subject", (object?)email.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dateSentUnix", (object?)email.DateSent?.ToUnixTimeSeconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dateReceivedUnix", (object?)email.DateReceived?.ToUnixTimeSeconds() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@folder", (object?)email.Folder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@account", (object?)email.Account ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hasAttachments", email.HasAttachments ? 1 : 0);
        cmd.Parameters.AddWithValue("@attachmentNames", (object?)email.AttachmentNamesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bodyPreview", (object?)email.BodyPreview ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bodyText", (object?)email.BodyText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@indexedAtUnix", email.IndexedAtUnix);
        cmd.Parameters.AddWithValue("@lastModifiedTicks", email.LastModifiedTicks);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task UpsertEmailsAsync(IEnumerable<EmailDocument> emails, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var transaction = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var email in emails)
            {
                await UpsertEmailAsync(email, ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<DatabaseStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var totalCount = await ExecuteScalarLongAsync("SELECT COUNT(*) FROM emails;", ct).ConfigureAwait(false);
        var headerCount = totalCount;
        var contentCount = await ExecuteScalarLongAsync(
            "SELECT COUNT(*) FROM emails WHERE body_text IS NOT NULL AND body_text != '';", ct).ConfigureAwait(false);

        long ftsSize = 0;
        try
        {
            var pageCount = await ExecuteScalarLongAsync(
                "SELECT COUNT(*) FROM emails_fts_data;", ct).ConfigureAwait(false);
            ftsSize = pageCount * 4096;
        }
        catch
        {
        }

        var accountCounts = new Dictionary<string, long>();
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT account, COUNT(*) as cnt FROM emails WHERE account IS NOT NULL GROUP BY account;";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var account = reader.GetString(0);
                var count = reader.GetInt64(1);
                accountCounts[account] = count;
            }
        }

        var folderCounts = new Dictionary<string, long>();
        await using (var cmd = _connection!.CreateCommand())
        {
            cmd.CommandText = "SELECT folder, COUNT(*) as cnt FROM emails WHERE folder IS NOT NULL GROUP BY folder ORDER BY cnt DESC LIMIT 20;";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var folder = reader.GetString(0);
                var count = reader.GetInt64(1);
                folderCounts[folder] = count;
            }
        }

        return new DatabaseStatistics
        {
            TotalEmails = totalCount,
            HeaderIndexed = headerCount,
            ContentIndexed = contentCount,
            FtsIndexSize = ftsSize,
            AccountCounts = accountCounts,
            FolderCounts = folderCounts
        };
    }

    public async Task<Dictionary<string, long>> GetKnownFilesAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var result = new Dictionary<string, long>();
        const string sql = "SELECT file_path, last_modified_ticks FROM emails;";

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var filePath = reader.GetString(0);
            var ticks = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
            result[filePath] = ticks;
        }

        return result;
    }

    public async Task ClearAllDataAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        const string sql = """
            DELETE FROM emails;
            DELETE FROM emails_fts;
            DELETE FROM index_metadata;
            """;

        await ExecuteNonQueryAsync(sql, ct).ConfigureAwait(false);
    }

    private async Task EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connection != null) return;

        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection(_connectionString);
        await _connection.OpenAsync(ct).ConfigureAwait(false);
    }

    private async Task ExecuteNonQueryAsync(string sql, CancellationToken ct)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<long> ExecuteScalarLongAsync(string sql, CancellationToken ct)
    {
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    private static EmailDocument MapToEmailDocument(SqliteDataReader reader)
    {
        return new EmailDocument
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            MessageId = reader.GetString(reader.GetOrdinal("message_id")),
            FilePath = reader.GetString(reader.GetOrdinal("file_path")),
            FromAddress = reader.IsDBNull(reader.GetOrdinal("from_address")) ? null : reader.GetString(reader.GetOrdinal("from_address")),
            FromName = reader.IsDBNull(reader.GetOrdinal("from_name")) ? null : reader.GetString(reader.GetOrdinal("from_name")),
            ToAddressesJson = reader.IsDBNull(reader.GetOrdinal("to_addresses")) ? null : reader.GetString(reader.GetOrdinal("to_addresses")),
            CcAddressesJson = reader.IsDBNull(reader.GetOrdinal("cc_addresses")) ? null : reader.GetString(reader.GetOrdinal("cc_addresses")),
            BccAddressesJson = reader.IsDBNull(reader.GetOrdinal("bcc_addresses")) ? null : reader.GetString(reader.GetOrdinal("bcc_addresses")),
            Subject = reader.IsDBNull(reader.GetOrdinal("subject")) ? null : reader.GetString(reader.GetOrdinal("subject")),
            DateSentUnix = reader.IsDBNull(reader.GetOrdinal("date_sent_unix")) ? null : reader.GetInt64(reader.GetOrdinal("date_sent_unix")),
            DateReceivedUnix = reader.IsDBNull(reader.GetOrdinal("date_received_unix")) ? null : reader.GetInt64(reader.GetOrdinal("date_received_unix")),
            Folder = reader.IsDBNull(reader.GetOrdinal("folder")) ? null : reader.GetString(reader.GetOrdinal("folder")),
            Account = reader.IsDBNull(reader.GetOrdinal("account")) ? null : reader.GetString(reader.GetOrdinal("account")),
            HasAttachments = !reader.IsDBNull(reader.GetOrdinal("has_attachments")) && reader.GetInt32(reader.GetOrdinal("has_attachments")) == 1,
            AttachmentNamesJson = reader.IsDBNull(reader.GetOrdinal("attachment_names")) ? null : reader.GetString(reader.GetOrdinal("attachment_names")),
            BodyPreview = reader.IsDBNull(reader.GetOrdinal("body_preview")) ? null : reader.GetString(reader.GetOrdinal("body_preview")),
            BodyText = reader.IsDBNull(reader.GetOrdinal("body_text")) ? null : reader.GetString(reader.GetOrdinal("body_text")),
            IndexedAtUnix = reader.GetInt64(reader.GetOrdinal("indexed_at_unix")),
            LastModifiedTicks = reader.IsDBNull(reader.GetOrdinal("last_modified_ticks")) ? 0 : reader.GetInt64(reader.GetOrdinal("last_modified_ticks"))
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    public long GetDatabaseSize()
    {
        if (!File.Exists(DatabasePath)) return 0;
        return new FileInfo(DatabasePath).Length;
    }

    public async Task BatchUpsertEmailsAsync(IReadOnlyList<EmailDocument> documents, CancellationToken ct = default)
    {
        if (documents.Count == 0) return;

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var transaction = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var doc in documents)
            {
                ct.ThrowIfCancellationRequested();
                await UpsertEmailAsync(doc, ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

}
MYIMAP_EOF

mkdir -p "$(dirname "MyEmailSearch/Indexing/EmailParser.cs")"
cat > "MyEmailSearch/Indexing/EmailParser.cs" <<'MYIMAP_EOF'
using Microsoft.Extensions.Logging;

using MimeKit;

using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

public sealed class EmailParser(string archivePath, ILogger<EmailParser> logger)
{
    private const int BodyPreviewLength = 500;

    public async Task<EmailDocument?> ParseAsync(
        string filePath,
        bool includeFullBody,
        CancellationToken ct = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var message = await MimeMessage.LoadAsync(filePath, ct).ConfigureAwait(false);
            var bodyText = GetBodyText(message);
            var bodyPreview = bodyText != null
                ? Truncate(bodyText, BodyPreviewLength)
                : null;
            var attachmentNames = message.Attachments
                .Select(a => a is MimePart mp ? mp.FileName : null)
                .Where(n => n != null)
                .Cast<string>()
                .ToList();

            return new EmailDocument
            {
                MessageId = message.MessageId ?? Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                FromAddress = message.From.Mailboxes.FirstOrDefault()?.Address,
                FromName = message.From.Mailboxes.FirstOrDefault()?.Name,
                ToAddressesJson = EmailDocument.ToJsonArray(message.To.Mailboxes.Select(m => m.Address)),
                CcAddressesJson = EmailDocument.ToJsonArray(message.Cc.Mailboxes.Select(m => m.Address)),
                BccAddressesJson = EmailDocument.ToJsonArray(message.Bcc.Mailboxes.Select(m => m.Address)),
                Subject = message.Subject,
                DateSentUnix = message.Date != DateTimeOffset.MinValue
                    ? message.Date.ToUnixTimeSeconds()
                    : null,
                Folder = ArchiveScanner.ExtractFolderName(filePath, archivePath),
                Account = ArchiveScanner.ExtractAccountName(filePath, archivePath),
                HasAttachments = attachmentNames.Count > 0,
                AttachmentNamesJson = attachmentNames.Count > 0
                    ? EmailDocument.ToJsonArray(attachmentNames)
                    : null,
                BodyPreview = bodyPreview,
                BodyText = includeFullBody ? bodyText : null,
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastModifiedTicks = fileInfo.LastWriteTimeUtc.Ticks
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse email: {Path}", filePath);
            return null;
        }
    }

    private static string? GetBodyText(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return NormalizeWhitespace(message.TextBody);
        }

        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            return NormalizeWhitespace(StripHtml(message.HtmlBody));
        }

        return null;
    }

    private static string StripHtml(string html)
    {
        var result = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&nbsp;", " ");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&amp;", "&");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&lt;", "<");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&gt;", ">");
        result = System.Text.RegularExpressions.Regex.Replace(result, "&quot;", "\"");
        return result;
    }

    private static string NormalizeWhitespace(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyEmailSearch/MyEmailSearch.csproj")"
cat > "MyEmailSearch/MyEmailSearch.csproj" <<'MYIMAP_EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MyEmailSearch</RootNamespace>
    <AssemblyName>MyEmailSearch</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyImapDownloader.Core\MyImapDownloader.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" />
    <PackageReference Include="MimeKit" />
  </ItemGroup>
</Project>
MYIMAP_EOF

mkdir -p "$(dirname "MyEmailSearch/Program.cs")"
cat > "MyEmailSearch/Program.cs" <<'MYIMAP_EOF'
using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using MyEmailSearch.Commands;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyEmailSearch.Search;

namespace MyEmailSearch;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("MyEmailSearch - Search your email archive");

        var archiveOption = new Option<string?>("--archive", "-a")
        {
            Description = "Path to the email archive directory",
            Recursive = true
        };

        var databaseOption = new Option<string?>("--database", "-d")
        {
            Description = "Path to the search database file",
            Recursive = true
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output",
            Recursive = true
        };

        rootCommand.Options.Add(archiveOption);
        rootCommand.Options.Add(databaseOption);
        rootCommand.Options.Add(verboseOption);

        rootCommand.Subcommands.Add(SearchCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(IndexCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(StatusCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(RebuildCommand.Create(archiveOption, databaseOption, verboseOption));

        return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    public static ServiceProvider CreateServiceProvider(
        string archivePath,
        string databasePath,
        bool verbose)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        services.AddSingleton(sp =>
            new SearchDatabase(databasePath, sp.GetRequiredService<ILogger<SearchDatabase>>()));

        services.AddSingleton<QueryParser>();
        services.AddSingleton(sp => new SearchEngine(
            sp.GetRequiredService<SearchDatabase>(),
            sp.GetRequiredService<QueryParser>(),
            sp.GetRequiredService<ILogger<SearchEngine>>()));

        services.AddSingleton(sp =>
            new ArchiveScanner(sp.GetRequiredService<ILogger<ArchiveScanner>>()));

        services.AddSingleton(sp =>
            new EmailParser(archivePath, sp.GetRequiredService<ILogger<EmailParser>>()));

        services.AddSingleton(sp => new IndexManager(
            sp.GetRequiredService<SearchDatabase>(),
            sp.GetRequiredService<ArchiveScanner>(),
            sp.GetRequiredService<EmailParser>(),
            sp.GetRequiredService<ILogger<IndexManager>>()));

        return services.BuildServiceProvider();
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core.Tests/Telemetry/JsonTelemetryFileWriterTests.cs")"
cat > "MyImapDownloader.Core.Tests/Telemetry/JsonTelemetryFileWriterTests.cs" <<'MYIMAP_EOF'
using System.Text.Json;
using System.Text.Json.Serialization;

using MyImapDownloader.Core.Infrastructure;
using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class JsonTelemetryFileWriterTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("writer_test");
    private readonly List<JsonTelemetryFileWriter> _writers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var writer in _writers)
        {
            writer.Dispose();
        }
        await Task.Delay(100);
        _temp.Dispose();
    }

    private JsonTelemetryFileWriter CreateWriter(
        string? subDir = null,
        string prefix = "test",
        long maxSize = 1024 * 1024)
    {
        var dir = subDir != null
            ? Path.Combine(_temp.Path, subDir)
            : _temp.Path;
        Directory.CreateDirectory(dir);

        var writer = new JsonTelemetryFileWriter(dir, prefix, maxSize, TimeSpan.FromSeconds(30));
        _writers.Add(writer);
        return writer;
    }

    private static WriterTestRecord Record(int id, string name) => new() { Id = id, Name = name };

    [Test]
    public async Task Constructor_CreatesDirectory_WhenItDoesNotExist()
    {
        var newDir = Path.Combine(_temp.Path, "new_subdir");

        var writer = new JsonTelemetryFileWriter(newDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        _writers.Add(writer);

        await Assert.That(Directory.Exists(newDir)).IsTrue();
    }

    [Test]
    public async Task Enqueue_CreatesFile_AfterFlush()
    {
        var writer = CreateWriter("enqueue_test");

        writer.Enqueue(Record(1, "test"), WriterTestJsonContext.Default.WriterTestRecord);
        await writer.FlushAsync();

        var files = Directory.GetFiles(_temp.Path, "*.jsonl", SearchOption.AllDirectories);
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Enqueue_WritesJsonLines()
    {
        var writer = CreateWriter("jsonl_test");

        writer.Enqueue(Record(1, "First"), WriterTestJsonContext.Default.WriterTestRecord);
        writer.Enqueue(Record(2, "Second"), WriterTestJsonContext.Default.WriterTestRecord);
        await writer.FlushAsync();

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "jsonl_test"), "*.jsonl");
        await Assert.That(files.Length).IsEqualTo(1);

        var lines = await File.ReadAllLinesAsync(files[0]);
        await Assert.That(lines.Length).IsEqualTo(2);
        await Assert.That(lines[0]).Contains("\"Id\":1");
        await Assert.That(lines[1]).Contains("\"Id\":2");
    }

    [Test]
    public async Task FlushAsync_WritesValidJsonPerLine()
    {
        var writer = CreateWriter("multi_test");

        writer.Enqueue(Record(1, "First"), WriterTestJsonContext.Default.WriterTestRecord);
        writer.Enqueue(Record(2, "Second"), WriterTestJsonContext.Default.WriterTestRecord);
        writer.Enqueue(Record(3, "Third"), WriterTestJsonContext.Default.WriterTestRecord);

        await writer.FlushAsync();

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "multi_test"), "*.jsonl");
        var lines = await File.ReadAllLinesAsync(files[0]);

        await Assert.That(lines.Length).IsEqualTo(3);

        foreach (var line in lines)
        {
            var parsed = JsonSerializer.Deserialize(line, WriterTestJsonContext.Default.WriterTestRecord);
            await Assert.That(parsed).IsNotNull();
        }
    }

    [Test]
    public async Task Writer_RotatesFile_WhenSizeExceeded()
    {
        var writer = CreateWriter("rotate_test", maxSize: 100);

        for (var i = 0; i < 10; i++)
        {
            writer.Enqueue(Record(i, new string('x', 50)), WriterTestJsonContext.Default.WriterTestRecord);
            await writer.FlushAsync();
        }

        var files = Directory.GetFiles(Path.Combine(_temp.Path, "rotate_test"), "*.jsonl");
        await Assert.That(files.Length).IsGreaterThan(1);
    }

    [Test]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        var writer = new JsonTelemetryFileWriter(
            Path.Combine(_temp.Path, "dispose_multi"),
            "test",
            1024 * 1024,
            TimeSpan.FromSeconds(30));

        writer.Dispose();
        writer.Dispose();
        writer.Dispose();

        await Assert.That(writer).IsNotNull();
    }

    [Test]
    public async Task Dispose_FlushesRemainingRecords()
    {
        var subDir = Path.Combine(_temp.Path, "dispose_flush");
        Directory.CreateDirectory(subDir);

        var writer = new JsonTelemetryFileWriter(subDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        writer.Enqueue(Record(42, "FinalRecord"), WriterTestJsonContext.Default.WriterTestRecord);
        writer.Dispose();

        var files = Directory.GetFiles(subDir, "*.jsonl");
        await Assert.That(files.Length).IsGreaterThanOrEqualTo(1);

        var content = await File.ReadAllTextAsync(files[0]);
        await Assert.That(content).Contains("FinalRecord");
    }

    [Test]
    public async Task Enqueue_AfterDispose_IsIgnored()
    {
        var subDir = Path.Combine(_temp.Path, "after_dispose");
        var writer = new JsonTelemetryFileWriter(subDir, "test", 1024 * 1024, TimeSpan.FromSeconds(30));
        writer.Dispose();

        writer.Enqueue(Record(1, "late"), WriterTestJsonContext.Default.WriterTestRecord);

        await Assert.That(Directory.GetFiles(subDir, "*.jsonl").Length).IsEqualTo(0);
    }

    [Test]
    public async Task FlushAsync_WithEmptyBuffer_DoesNotThrow()
    {
        var writer = CreateWriter("empty_flush");

        await writer.FlushAsync();

        await Assert.That(writer).IsNotNull();
    }
}

public sealed record WriterTestRecord
{
    public int Id { get; init; }
    public string? Name { get; init; }
}

[JsonSerializable(typeof(WriterTestRecord))]
internal partial class WriterTestJsonContext : JsonSerializerContext
{
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core/MyImapDownloader.Core.csproj")"
cat > "MyImapDownloader.Core/MyImapDownloader.Core.csproj" <<'MYIMAP_EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>MyImapDownloader.Core</RootNamespace>
    <AssemblyName>MyImapDownloader.Core</AssemblyName>
    <Description>Shared infrastructure for MyImapDownloader and MyEmailSearch</Description>
    <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" />
    <PackageReference Include="OpenTelemetry" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
  </ItemGroup>
</Project>
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core/Telemetry/JsonFileLogExporter.cs")"
cat > "MyImapDownloader.Core/Telemetry/JsonFileLogExporter.cs" <<'MYIMAP_EOF'
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonFileLogExporter(JsonTelemetryFileWriter? writer) : BaseExporter<LogRecord>
{
    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        if (writer == null) return ExportResult.Success;

        try
        {
            foreach (var log in batch)
            {
                var record = new LogRecordData
                {
                    Timestamp = log.Timestamp != default ? log.Timestamp : DateTime.UtcNow,
                    TraceId = log.TraceId != default ? log.TraceId.ToString() : null,
                    SpanId = log.SpanId != default ? log.SpanId.ToString() : null,
                    LogLevel = log.LogLevel.ToString(),
                    CategoryName = log.CategoryName,
                    EventId = log.EventId.Id != 0 ? log.EventId.Id : null,
                    EventName = log.EventId.Name,
                    FormattedMessage = log.FormattedMessage,
                    Body = log.Body,
                    Attributes = ExtractAttributes(log),
                    Exception = ExtractException(log.Exception)
                };

                writer.Enqueue(record, TelemetryJsonContext.Default.LogRecordData);
            }
        }
        catch
        {
        }

        return ExportResult.Success;
    }

    private static Dictionary<string, string?>? ExtractAttributes(LogRecord log)
    {
        if (log.Attributes == null || log.Attributes.Count == 0) return null;
        return TelemetryTagFormatter.ToStringDictionary(log.Attributes);
    }

    private static ExceptionInfo? ExtractException(Exception? ex)
    {
        if (ex == null) return null;

        return new ExceptionInfo
        {
            Type = ex.GetType().FullName,
            Message = ex.Message,
            StackTrace = ex.StackTrace,
            InnerException = ExtractException(ex.InnerException)
        };
    }
}

public record LogRecordData
{
    public string Type => "log";
    public DateTime Timestamp { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? LogLevel { get; init; }
    public string? CategoryName { get; init; }
    public int? EventId { get; init; }
    public string? EventName { get; init; }
    public string? FormattedMessage { get; init; }
    public string? Body { get; init; }
    public Dictionary<string, string?>? Attributes { get; init; }
    public ExceptionInfo? Exception { get; init; }
}

public record ExceptionInfo
{
    public string? Type { get; init; }
    public string? Message { get; init; }
    public string? StackTrace { get; init; }
    public ExceptionInfo? InnerException { get; init; }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core/Telemetry/JsonFileMetricsExporter.cs")"
cat > "MyImapDownloader.Core/Telemetry/JsonFileMetricsExporter.cs" <<'MYIMAP_EOF'
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonFileMetricsExporter(JsonTelemetryFileWriter? writer) : BaseExporter<Metric>
{
    public override ExportResult Export(in Batch<Metric> batch)
    {
        if (writer == null) return ExportResult.Success;

        try
        {
            foreach (var metric in batch)
            {
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    var record = new MetricRecord
                    {
                        Timestamp = point.EndTime.UtcDateTime,
                        MetricName = metric.Name,
                        MetricDescription = metric.Description,
                        MetricUnit = metric.Unit,
                        MetricType = metric.MetricType.ToString(),
                        MeterName = metric.MeterName,
                        StartTime = point.StartTime.UtcDateTime,
                        EndTime = point.EndTime.UtcDateTime,
                        Tags = ExtractTags(in point),
                        Value = ExtractValue(metric.MetricType, in point)
                    };

                    writer.Enqueue(record, TelemetryJsonContext.Default.MetricRecord);
                }
            }
        }
        catch
        {
        }

        return ExportResult.Success;
    }

    private static Dictionary<string, string?>? ExtractTags(in MetricPoint point)
    {
        var tags = new Dictionary<string, string?>();
        foreach (var tag in point.Tags)
        {
            tags[tag.Key] = tag.Value?.ToString();
        }
        return tags.Count > 0 ? tags : null;
    }

    private static object? ExtractValue(MetricType metricType, in MetricPoint point) => metricType switch
    {
        MetricType.LongSum or MetricType.LongSumNonMonotonic => point.GetSumLong(),
        MetricType.DoubleSum or MetricType.DoubleSumNonMonotonic => point.GetSumDouble(),
        MetricType.LongGauge => point.GetGaugeLastValueLong(),
        MetricType.DoubleGauge => point.GetGaugeLastValueDouble(),
        MetricType.Histogram or MetricType.ExponentialHistogram => ExtractHistogram(in point),
        _ => null
    };

    private static HistogramValue ExtractHistogram(in MetricPoint point)
    {
        var hasMinMax = point.TryGetHistogramMinMaxValues(out var min, out var max);
        return new HistogramValue
        {
            Count = point.GetHistogramCount(),
            Sum = point.GetHistogramSum(),
            Min = hasMinMax ? min : null,
            Max = hasMinMax ? max : null
        };
    }
}

public record MetricRecord
{
    public string Type => "metric";
    public DateTime Timestamp { get; init; }
    public string? MetricName { get; init; }
    public string? MetricDescription { get; init; }
    public string? MetricUnit { get; init; }
    public string? MetricType { get; init; }
    public string? MeterName { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public Dictionary<string, string?>? Tags { get; init; }
    public object? Value { get; init; }
}

public record HistogramValue
{
    public long Count { get; init; }
    public double Sum { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core/Telemetry/JsonFileTraceExporter.cs")"
cat > "MyImapDownloader.Core/Telemetry/JsonFileTraceExporter.cs" <<'MYIMAP_EOF'
using System.Diagnostics;

using OpenTelemetry;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonFileTraceExporter(JsonTelemetryFileWriter? writer) : BaseExporter<Activity>
{
    public override ExportResult Export(in Batch<Activity> batch)
    {
        if (writer == null) return ExportResult.Success;

        try
        {
            foreach (var activity in batch)
            {
                var record = new TraceRecord
                {
                    Timestamp = activity.StartTimeUtc,
                    TraceId = activity.TraceId.ToString(),
                    SpanId = activity.SpanId.ToString(),
                    ParentSpanId = activity.ParentSpanId.ToString(),
                    OperationName = activity.OperationName,
                    DisplayName = activity.DisplayName,
                    Kind = activity.Kind.ToString(),
                    Status = activity.Status.ToString(),
                    StatusDescription = activity.StatusDescription,
                    Duration = activity.Duration,
                    DurationMs = activity.Duration.TotalMilliseconds,
                    Source = new SourceInfo
                    {
                        Name = activity.Source.Name,
                        Version = activity.Source.Version
                    },
                    Tags = TelemetryTagFormatter.ToStringDictionary(activity.TagObjects),
                    Events = activity.Events.Select(e => new SpanEvent
                    {
                        Name = e.Name,
                        Timestamp = e.Timestamp.UtcDateTime,
                        Attributes = TelemetryTagFormatter.ToStringDictionary(e.Tags)
                    }).ToList()
                };

                writer.Enqueue(record, TelemetryJsonContext.Default.TraceRecord);
            }
        }
        catch
        {
        }

        return ExportResult.Success;
    }
}

public record TraceRecord
{
    public string Type => "trace";
    public DateTime Timestamp { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public string? OperationName { get; init; }
    public string? DisplayName { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public string? StatusDescription { get; init; }
    public TimeSpan Duration { get; init; }
    public double DurationMs { get; init; }
    public SourceInfo? Source { get; init; }
    public Dictionary<string, string?>? Tags { get; init; }
    public List<SpanEvent>? Events { get; init; }
}

public record SourceInfo
{
    public string? Name { get; init; }
    public string? Version { get; init; }
}

public record SpanEvent
{
    public string? Name { get; init; }
    public DateTime Timestamp { get; init; }
    public Dictionary<string, string?>? Attributes { get; init; }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core/Telemetry/JsonTelemetryFileWriter.cs")"
cat > "MyImapDownloader.Core/Telemetry/JsonTelemetryFileWriter.cs" <<'MYIMAP_EOF'
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MyImapDownloader.Core.Telemetry;

public sealed class JsonTelemetryFileWriter : IDisposable
{
    private const int MaxBufferedLines = 10000;

    private readonly string _directory;
    private readonly string _prefix;
    private readonly long _maxFileSize;
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Timer _flushTimer;

    private string _currentDate;
    private string _currentFilePath;
    private long _currentFileSize;
    private int _fileSequence;
    private volatile bool _disposed;
    private volatile bool _writeEnabled = true;

    public JsonTelemetryFileWriter(
        string directory,
        string prefix,
        long maxFileSizeBytes,
        TimeSpan flushInterval)
    {
        _directory = directory;
        _prefix = prefix;
        _maxFileSize = maxFileSizeBytes;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch
        {
            _writeEnabled = false;
        }

        _currentDate = CurrentDate();
        _currentFilePath = GenerateFilePath();
        _currentFileSize = GetExistingFileSize(_currentFilePath);

        _flushTimer = new Timer(_ => FlushTimerCallback(), null, flushInterval, flushInterval);
    }

    public void Enqueue<T>(T record, JsonTypeInfo<T> typeInfo)
    {
        if (_disposed || !_writeEnabled) return;

        string line;
        try
        {
            line = JsonSerializer.Serialize(record, typeInfo);
        }
        catch
        {
            return;
        }

        _queue.Enqueue(line);
    }

    public async Task FlushAsync()
    {
        if (!_writeEnabled || _queue.IsEmpty) return;

        if (!await _writeLock.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            return;

        try
        {
            var sb = new StringBuilder();
            while (_queue.TryDequeue(out var line))
            {
                sb.Append(line).Append('\n');
            }

            if (sb.Length > 0)
            {
                await WriteAsync(sb.ToString()).ConfigureAwait(false);
            }
        }
        catch
        {
            DisableIfOverflowing();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void FlushTimerCallback()
    {
        if (_disposed || !_writeEnabled || _queue.IsEmpty) return;

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            DisableIfOverflowing();
        }
    }

    private void DisableIfOverflowing()
    {
        if (_queue.Count <= MaxBufferedLines) return;
        _writeEnabled = false;
        _queue.Clear();
    }

    private async Task WriteAsync(string content)
    {
        var byteCount = Encoding.UTF8.GetByteCount(content);

        var today = CurrentDate();
        if (today != _currentDate)
        {
            _currentDate = today;
            _fileSequence = 0;
            _currentFilePath = GenerateFilePath();
            _currentFileSize = GetExistingFileSize(_currentFilePath);
        }

        if (_currentFileSize > 0 && _currentFileSize + byteCount > _maxFileSize)
        {
            _fileSequence++;
            _currentFilePath = GenerateFilePath();
            _currentFileSize = GetExistingFileSize(_currentFilePath);
        }

        try
        {
            await File.AppendAllTextAsync(_currentFilePath, content).ConfigureAwait(false);
            _currentFileSize += byteCount;
        }
        catch
        {
        }
    }

    private static string CurrentDate() =>
        DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private string GenerateFilePath() =>
        Path.Combine(_directory, $"{_prefix}_{_currentDate}_{_fileSequence:D4}.jsonl");

    private static long GetExistingFileSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _flushTimer.Dispose();

        try
        {
            FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _disposed = true;
        _writeLock.Dispose();
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core/Telemetry/TelemetryExtensions.cs")"
cat > "MyImapDownloader.Core/Telemetry/TelemetryExtensions.cs" <<'MYIMAP_EOF'
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyImapDownloader.Core.Telemetry;

public static class TelemetryExtensions
{
    public static IServiceCollection AddCoreTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion)
    {
        var config = new TelemetryConfiguration
        {
            ServiceName = serviceName,
            ServiceVersion = serviceVersion
        };
        configuration.GetSection(TelemetryConfiguration.SectionName).Bind(config);
        services.AddSingleton(config);

        var telemetryBaseDir = TelemetryDirectoryResolver.ResolveTelemetryDirectory(config.ServiceName);

        if (telemetryBaseDir == null)
        {
            services.AddSingleton<ITelemetryWriterProvider>(new NullTelemetryWriterProvider());
            return services;
        }

        config.OutputDirectory = telemetryBaseDir;

        var flushInterval = TimeSpan.FromSeconds(config.FlushIntervalSeconds);

        var traceWriter = config.EnableTracing
            ? TryCreateWriter(telemetryBaseDir, "traces", config.MaxFileSizeBytes, flushInterval)
            : null;
        var metricsWriter = config.EnableMetrics
            ? TryCreateWriter(telemetryBaseDir, "metrics", config.MaxFileSizeBytes, flushInterval)
            : null;
        var logsWriter = config.EnableLogging
            ? TryCreateWriter(telemetryBaseDir, "logs", config.MaxFileSizeBytes, flushInterval)
            : null;

        services.AddSingleton<ITelemetryWriterProvider>(
            new TelemetryWriterProvider(traceWriter, metricsWriter, logsWriter));

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(serviceName: config.ServiceName, serviceVersion: config.ServiceVersion);

        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                if (traceWriter != null)
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddSource(serviceName)
                        .AddProcessor(new BatchActivityExportProcessor(
                            new JsonFileTraceExporter(traceWriter),
                            maxQueueSize: 2048,
                            scheduledDelayMilliseconds: (int)flushInterval.TotalMilliseconds));
                }
            })
            .WithMetrics(builder =>
            {
                if (metricsWriter != null)
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddMeter(serviceName)
                        .AddRuntimeInstrumentation()
                        .AddReader(new PeriodicExportingMetricReader(
                            new JsonFileMetricsExporter(metricsWriter),
                            exportIntervalMilliseconds: config.MetricsExportIntervalSeconds * 1000));
                }
            });

        if (logsWriter != null)
        {
            services.AddLogging(logging => logging.AddOpenTelemetry(options =>
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
            }));
        }

        return services;
    }

    public static void InitializeCoreTelemetry(this IServiceProvider services)
    {
        _ = services.GetService<TracerProvider>();
        _ = services.GetService<MeterProvider>();
    }

    private static JsonTelemetryFileWriter? TryCreateWriter(
        string baseDirectory,
        string kind,
        long maxFileSizeBytes,
        TimeSpan flushInterval)
    {
        try
        {
            return new JsonTelemetryFileWriter(
                Path.Combine(baseDirectory, kind), kind, maxFileSizeBytes, flushInterval);
        }
        catch
        {
            return null;
        }
    }
}

public interface ITelemetryWriterProvider : IDisposable
{
    JsonTelemetryFileWriter? TraceWriter { get; }
    JsonTelemetryFileWriter? MetricsWriter { get; }
    JsonTelemetryFileWriter? LogsWriter { get; }
}

public sealed class TelemetryWriterProvider(
    JsonTelemetryFileWriter? traceWriter,
    JsonTelemetryFileWriter? metricsWriter,
    JsonTelemetryFileWriter? logsWriter) : ITelemetryWriterProvider
{
    public JsonTelemetryFileWriter? TraceWriter => traceWriter;
    public JsonTelemetryFileWriter? MetricsWriter => metricsWriter;
    public JsonTelemetryFileWriter? LogsWriter => logsWriter;

    public void Dispose()
    {
        traceWriter?.Dispose();
        metricsWriter?.Dispose();
        logsWriter?.Dispose();
    }
}

public sealed class NullTelemetryWriterProvider : ITelemetryWriterProvider
{
    public JsonTelemetryFileWriter? TraceWriter => null;
    public JsonTelemetryFileWriter? MetricsWriter => null;
    public JsonTelemetryFileWriter? LogsWriter => null;

    public void Dispose()
    {
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader/EmailDownloadService.cs")"
cat > "MyImapDownloader/EmailDownloadService.cs" <<'MYIMAP_EOF'
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

using Microsoft.Extensions.Logging;

using MyImapDownloader.Telemetry;

using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace MyImapDownloader;

public class EmailDownloadService
{
    private const int BatchSize = 50;

    private readonly ILogger<EmailDownloadService> _logger;
    private readonly ImapConfiguration _config;
    private readonly EmailStorageService _storage;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;

    public EmailDownloadService(
        ILogger<EmailDownloadService> logger,
        ImapConfiguration config,
        EmailStorageService storage)
    {
        _logger = logger;
        _config = config;
        _storage = storage;

        _retryPolicy = Policy
            .Handle<Exception>(IsRetryable)
            .WaitAndRetryForeverAsync(
                retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 300)),
                (exception, retryCount, timeSpan) =>
                {
                    DiagnosticsConfig.RetryAttempts.Add(1);
                    _logger.LogWarning(exception, "Retry {Count} in {Delay}: {Message}", retryCount, timeSpan, exception.Message);
                });

        _circuitBreakerPolicy = Policy
            .Handle<Exception>(IsRetryable)
            .CircuitBreakerAsync(5, TimeSpan.FromMinutes(2));
    }

    private static bool IsRetryable(Exception ex) =>
        ex is not AuthenticationException and not OperationCanceledException;

    public async Task DownloadEmailsAsync(DownloadOptions options, CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("DownloadEmails");

        await _storage.InitializeAsync(ct);

        var policy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);

        await policy.ExecuteAsync(async token =>
        {
            using var client = new ImapClient { Timeout = 180_000 };
            try
            {
                await ConnectAndAuthenticateAsync(client, token);

                var inbox = client.Inbox
                    ?? throw new InvalidOperationException(
                        "IMAP client returned a null Inbox after authentication.");

                var folders = options.AllFolders
                    ? await GetAllFoldersAsync(client, inbox, token)
                    : new List<IMailFolder> { inbox };

                foreach (var folder in folders)
                {
                    await ProcessFolderAsync(folder, options, token);
                }
            }
            finally
            {
                await DisconnectQuietlyAsync(client);
            }
        }, ct);
    }

    private async Task ProcessFolderAsync(IMailFolder folder, DownloadOptions options, CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessFolder");
        activity?.SetTag("folder", folder.FullName);

        try
        {
            await folder.OpenAsync(FolderAccess.ReadOnly, ct);

            var lastUid = await _storage.GetLastUidAsync(folder.FullName, folder.UidValidity, ct);

            _logger.LogInformation("Syncing {Folder}. Last UID: {Uid}", folder.FullName, lastUid);

            var query = SearchQuery.All;
            if (lastUid > 0)
            {
                var range = new UniqueIdRange(new UniqueId((uint)lastUid + 1), UniqueId.MaxValue);
                query = SearchQuery.Uids(range);
            }
            if (options.StartDate.HasValue) query = query.And(SearchQuery.DeliveredAfter(options.StartDate.Value));
            if (options.EndDate.HasValue) query = query.And(SearchQuery.DeliveredBefore(options.EndDate.Value));

            var uids = await folder.SearchAsync(query, ct);
            _logger.LogInformation("Found {Count} new messages in {Folder}", uids.Count, folder.FullName);

            var checkpoint = new FolderCheckpointTracker();

            for (var i = 0; i < uids.Count; i += BatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = uids.Skip(i).Take(BatchSize).ToList();
                var failedUids = await DownloadBatchAsync(folder, batch, checkpoint, ct);

                if (checkpoint.SafeCheckpoint > 0)
                {
                    await _storage.UpdateLastUidAsync(folder.FullName, checkpoint.SafeCheckpoint, folder.UidValidity, ct);
                }

                if (failedUids.Count > 0)
                {
                    _logger.LogWarning("Failed to download {Count} emails in {Folder}: UIDs {Uids}",
                        failedUids.Count, folder.FullName, string.Join(", ", failedUids));
                }
            }

            if (checkpoint.LowestFailedUid.HasValue)
            {
                _logger.LogWarning(
                    "Checkpoint for {Folder} held at UID {Checkpoint} so UID {Failed} is retried next run",
                    folder.FullName, checkpoint.SafeCheckpoint, checkpoint.LowestFailedUid.Value);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error processing folder {Folder}", folder.FullName);
            throw;
        }
    }

    private async Task<List<uint>> DownloadBatchAsync(
        IMailFolder folder,
        IList<UniqueId> uids,
        FolderCheckpointTracker checkpoint,
        CancellationToken ct)
    {
        var failedUids = new List<uint>();

        var items = await folder.FetchAsync(uids, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, ct);

        foreach (var item in items)
        {
            using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessEmail");

            var uid = item.UniqueId.Id;
            var envelope = item.Envelope;
            var envelopeMessageId = envelope?.MessageId;

            if (!string.IsNullOrWhiteSpace(envelopeMessageId))
            {
                var normalizedId = EmailStorageService.NormalizeMessageId(envelopeMessageId);
                if (await _storage.ExistsAsyncNormalized(normalizedId, ct))
                {
                    _logger.LogDebug("Skipping duplicate {Id}", normalizedId);
                    checkpoint.MarkProcessed(uid);
                    continue;
                }
            }

            try
            {
                using var stream = await folder.GetStreamAsync(item.UniqueId, ct);
                var isNew = await _storage.SaveStreamAsync(
                    stream,
                    envelopeMessageId ?? string.Empty,
                    item.InternalDate ?? DateTimeOffset.UtcNow,
                    folder.FullName,
                    ct);

                if (isNew)
                {
                    DiagnosticsConfig.EmailsDownloaded.Add(1);
                    _logger.LogInformation("Downloaded: {Subject}", envelope?.Subject);
                }

                checkpoint.MarkProcessed(uid);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to download UID {Uid}", item.UniqueId);
                failedUids.Add(uid);
                checkpoint.MarkFailed(uid);
            }
        }

        return failedUids;
    }

    private async Task ConnectAndAuthenticateAsync(ImapClient client, CancellationToken ct)
    {
        _logger.LogInformation("Connecting to {Server}:{Port}", _config.Server, _config.Port);
        await client.ConnectAsync(_config.Server, _config.Port, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(_config.Username, _config.Password, ct);
    }

    private async Task DisconnectQuietlyAsync(ImapClient client)
    {
        if (!client.IsConnected) return;

        try
        {
            await client.DisconnectAsync(true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IMAP disconnect failed");
        }
    }

    private static async Task<List<IMailFolder>> GetAllFoldersAsync(ImapClient client, IMailFolder inbox, CancellationToken ct)
    {
        var folders = new List<IMailFolder>();

        if (client.PersonalNamespaces.Count > 0)
        {
            var personal = client.GetFolder(client.PersonalNamespaces[0]);
            await CollectFoldersRecursiveAsync(personal, folders, ct);
        }

        if (!folders.Contains(inbox))
        {
            folders.Insert(0, inbox);
        }

        return folders;
    }

    private static async Task CollectFoldersRecursiveAsync(IMailFolder parent, List<IMailFolder> folders, CancellationToken ct)
    {
        foreach (var folder in await parent.GetSubfoldersAsync(false, ct))
        {
            if (IsSelectable(folder))
            {
                folders.Add(folder);
            }

            await CollectFoldersRecursiveAsync(folder, folders, ct);
        }
    }

    private static bool IsSelectable(IMailFolder folder) =>
        (folder.Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) == 0;
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader/EmailStorageService.cs")"
cat > "MyImapDownloader/EmailStorageService.cs" <<'MYIMAP_EOF'
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using MimeKit;

using MyImapDownloader.Telemetry;

namespace MyImapDownloader;

public class EmailStorageService(ILogger<EmailStorageService> logger, string baseDirectory) : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(baseDirectory, "index.v1.db");
    private SqliteConnection? _connection;

    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(baseDirectory);

        try
        {
            await OpenAndMigrateAsync(ct);
        }
        catch (SqliteException ex)
        {
            logger.LogError(ex, "Database corruption detected. Initiating recovery...");
            await RecoverDatabaseAsync(ct);
        }
    }

    private async Task OpenAndMigrateAsync(CancellationToken ct)
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync(ct);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS Messages (
                MessageId TEXT PRIMARY KEY,
                Folder TEXT NOT NULL,
                ImportedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SyncState (
                Folder TEXT PRIMARY KEY,
                LastUid INTEGER NOT NULL,
                UidValidity INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Messages_Folder ON Messages(Folder);
            """;

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task RecoverDatabaseAsync(CancellationToken ct)
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        SqliteConnection.ClearAllPools();

        var suffix = $".corrupt.{DateTime.UtcNow.Ticks}";
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Move(path, path + suffix);
                logger.LogWarning("Moved corrupt database file to {Path}", path + suffix);
            }
        }

        await OpenAndMigrateAsync(ct);

        logger.LogInformation("Rebuilding index from disk...");
        var count = 0;

        foreach (var metaFile in Directory.EnumerateFiles(baseDirectory, "*.meta.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = await File.ReadAllTextAsync(metaFile, ct);
                var meta = JsonSerializer.Deserialize(json, EmailMetadataJsonContext.Default.EmailMetadata);
                if (!string.IsNullOrWhiteSpace(meta?.MessageId) &&
                    !string.IsNullOrWhiteSpace(meta.Folder))
                {
                    await InsertMessageRecordAsync(meta.MessageId, meta.Folder, ct);
                    count++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Skipping malformed meta file {File}: {Error}", metaFile, ex.Message);
            }
        }

        logger.LogInformation("Recovery complete. Re-indexed {Count} emails.", count);
    }

    public async Task<long> GetLastUidAsync(string folderName, long currentValidity, CancellationToken ct)
    {
        if (_connection == null) await InitializeAsync(ct);

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT LastUid, UidValidity FROM SyncState WHERE Folder = @folder";
        cmd.Parameters.AddWithValue("@folder", folderName);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var storedValidity = reader.GetInt64(1);
            if (storedValidity == currentValidity)
                return reader.GetInt64(0);

            logger.LogWarning("UIDVALIDITY changed for {Folder}. Resetting cursor.", folderName);
        }

        return 0;
    }

    public async Task UpdateLastUidAsync(string folderName, long lastUid, long validity, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SyncState (Folder, LastUid, UidValidity)
            VALUES (@folder, @uid, @validity)
            ON CONFLICT(Folder) DO UPDATE SET
                LastUid = @uid,
                UidValidity = @validity
            WHERE LastUid < @uid OR UidValidity != @validity;
            """;

        cmd.Parameters.AddWithValue("@folder", folderName);
        cmd.Parameters.AddWithValue("@uid", lastUid);
        cmd.Parameters.AddWithValue("@validity", validity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> SaveStreamAsync(
        Stream networkStream,
        string messageId,
        DateTimeOffset internalDate,
        string folderName,
        CancellationToken ct)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("SaveStream");
        var sw = Stopwatch.StartNew();

        var normalizedId = string.IsNullOrWhiteSpace(messageId) ? null : NormalizeMessageId(messageId);

        if (normalizedId != null && await ExistsAsyncNormalized(normalizedId, ct))
            return false;

        var folderPath = GetFolderPath(folderName);
        EnsureMaildirStructure(folderPath);

        var tempPath = Path.Combine(
            folderPath,
            "tmp",
            $"{internalDate.ToUnixTimeSeconds()}.{Guid.NewGuid()}.tmp");

        try
        {
            long bytesWritten;
            using (var fs = File.Create(tempPath))
            {
                await networkStream.CopyToAsync(fs, ct);
                bytesWritten = fs.Length;
            }

            HeaderList headers;
            using (var fs = File.OpenRead(tempPath))
            {
                var parser = new MimeParser(fs, MimeFormat.Entity);
                headers = await parser.ParseHeadersAsync(ct);
            }

            if (normalizedId == null)
            {
                var parsedId = headers[HeaderId.MessageId];
                normalizedId = !string.IsNullOrWhiteSpace(parsedId)
                    ? NormalizeMessageId(parsedId)
                    : await ComputeFileHashAsync(tempPath, ct);

                if (await ExistsAsyncNormalized(normalizedId, ct))
                {
                    File.Delete(tempPath);
                    return false;
                }
            }

            var metadata = new EmailMetadata
            {
                MessageId = normalizedId,
                Subject = headers[HeaderId.Subject],
                From = headers[HeaderId.From],
                To = headers[HeaderId.To],
                Date = DateTimeOffset.TryParse(headers[HeaderId.Date], out var d)
                    ? d.UtcDateTime
                    : internalDate.UtcDateTime,
                Folder = folderName,
                ArchivedAt = DateTime.UtcNow,
                HasAttachments = false
            };

            var safeFileId = SanitizeFilename(normalizedId);
            var finalName = GenerateFilename(internalDate, safeFileId);
            var finalPath = Path.Combine(folderPath, "cur", finalName);

            var attempt = 0;
            while (File.Exists(finalPath) && attempt < 10)
            {
                attempt++;
                finalName = GenerateFilename(internalDate, $"{safeFileId}_{attempt}");
                finalPath = Path.Combine(folderPath, "cur", finalName);
            }

            if (File.Exists(finalPath))
            {
                File.Delete(tempPath);
                await InsertMessageRecordAsync(normalizedId, folderName, ct);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            File.Move(tempPath, finalPath);

            await File.WriteAllTextAsync(
                finalPath + ".meta.json",
                JsonSerializer.Serialize(metadata, EmailMetadataJsonContext.Default.EmailMetadata),
                ct);

            await InsertMessageRecordAsync(normalizedId, folderName, ct);

            DiagnosticsConfig.FilesWritten.Add(1);
            DiagnosticsConfig.BytesWritten.Add(bytesWritten);
            DiagnosticsConfig.WriteLatency.Record(sw.Elapsed.TotalMilliseconds);

            return true;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private async Task InsertMessageRecordAsync(string messageId, string folder, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO Messages (MessageId, Folder, ImportedAt) VALUES (@id, @folder, @date)";
        cmd.Parameters.AddWithValue("@id", messageId);
        cmd.Parameters.AddWithValue("@folder", folder);
        cmd.Parameters.AddWithValue("@date", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private string GetFolderPath(string folderName) =>
        Path.Combine(baseDirectory, SanitizeForFilename(folderName, 100));

    private static void EnsureMaildirStructure(string folderPath)
    {
        Directory.CreateDirectory(Path.Combine(folderPath, "cur"));
        Directory.CreateDirectory(Path.Combine(folderPath, "new"));
        Directory.CreateDirectory(Path.Combine(folderPath, "tmp"));
    }

    public static string GenerateFilename(DateTimeOffset date, string safeId)
    {
        var host = SanitizeForFilename(Environment.MachineName, 20);
        return $"{date.ToUnixTimeSeconds()}.{safeId}.{host}.eml";
    }

    public static string NormalizeMessageId(string messageId)
    {
        var cleaned = Regex.Replace(messageId, @"[<>:""/\\|?*\x00-\x1F]", "_")
            .Trim('<', '>')
            .ToLowerInvariant();

        if (cleaned.Length > 100)
        {
            var hash = ComputeHash(cleaned)[..8];
            cleaned = cleaned[..91] + "_" + hash;
        }

        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    private static string SanitizeFilename(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);

        foreach (var c in input)
            sb.Append(invalid.Contains(c) ? '_' : c);

        return sb.ToString().TrimEnd('.', ' ');
    }

    public async Task<bool> ExistsAsyncNormalized(string id, CancellationToken ct)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Messages WHERE MessageId = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", id);
        return (await cmd.ExecuteScalarAsync(ct)) != null;
    }

    public static string SanitizeForFilename(string input, int maxLength)
    {
        var sb = new StringBuilder(maxLength);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');

            if (sb.Length >= maxLength) break;
        }
        return sb.ToString().Trim('_');
    }

    public static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken ct)
    {
        using var fs = File.OpenRead(path);
        var bytes = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
            await _connection.DisposeAsync();
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader/Program.cs")"
cat > "MyImapDownloader/Program.cs" <<'MYIMAP_EOF'
using System.CommandLine;
using System.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using MyImapDownloader;
using MyImapDownloader.Core.Telemetry;
using MyImapDownloader.Telemetry;

var invocation = new InvocationConfiguration { ProcessTerminationTimeout = TimeSpan.FromSeconds(30) };
return await DownloadCommand.Parse(DownloadCommand.Create(RunAsync), args).InvokeAsync(invocation);

static async Task<int> RunAsync(DownloadOptions options, CancellationToken ct)
{
    var host = BuildHost(options);
    var telemetryWriters = host.Services.GetRequiredService<ITelemetryWriterProvider>();

    try
    {
        host.Services.InitializeCoreTelemetry();
        return await RunSessionAsync(host.Services, options, ct);
    }
    finally
    {
        if (host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            host.Dispose();
        }

        telemetryWriters.Dispose();
    }
}

static IHost BuildHost(DownloadOptions options) =>
    Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration((_, config) =>
        {
            config.SetBasePath(AppContext.BaseDirectory);
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            config.AddEnvironmentVariables();
        })
        .ConfigureLogging((_, logging) =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information);
            if (options.Verbose)
            {
                logging.AddFilter(null, LogLevel.Debug);
            }
        })
        .ConfigureServices((context, services) =>
        {
            services.AddTelemetry(context.Configuration);
            services.AddSingleton(new ImapConfiguration
            {
                Server = options.Server,
                Username = options.Username,
                Password = options.Password,
                Port = options.Port
            });
            services.AddSingleton(sp => new EmailStorageService(
                sp.GetRequiredService<ILogger<EmailStorageService>>(),
                options.OutputDirectory));
            services.AddTransient<EmailDownloadService>();
        })
        .Build();

static async Task<int> RunSessionAsync(IServiceProvider services, DownloadOptions options, CancellationToken ct)
{
    var downloadService = services.GetRequiredService<EmailDownloadService>();
    var logger = services.GetRequiredService<ILogger<Program>>();
    var telemetryConfig = services.GetRequiredService<TelemetryConfiguration>();

    using var rootActivity = DiagnosticsConfig.ActivitySource.StartActivity(
        "EmailArchiveSession", ActivityKind.Server);

    rootActivity?.SetTag("service.name", telemetryConfig.ServiceName);
    rootActivity?.SetTag("service.version", telemetryConfig.ServiceVersion);
    rootActivity?.SetTag("host.name", Environment.MachineName);
    rootActivity?.SetTag("process.pid", Environment.ProcessId);
    rootActivity?.SetTag("telemetry.directory", telemetryConfig.OutputDirectory);

    var sessionStopwatch = Stopwatch.StartNew();

    try
    {
        logger.LogInformation("Starting email archive download...");
        logger.LogInformation("Output: {Output}", Path.GetFullPath(options.OutputDirectory));
        logger.LogInformation("Telemetry output: {TelemetryOutput}",
            Path.GetFullPath(telemetryConfig.OutputDirectory));

        rootActivity?.AddEvent(new ActivityEvent("DownloadStarted"));

        await downloadService.DownloadEmailsAsync(options, ct);

        rootActivity?.SetTag("session_duration_ms", sessionStopwatch.ElapsedMilliseconds);
        rootActivity?.SetStatus(ActivityStatusCode.Ok);
        rootActivity?.AddEvent(new ActivityEvent("DownloadCompleted"));

        logger.LogInformation("Archive complete! Session duration: {Duration}ms",
            sessionStopwatch.ElapsedMilliseconds);

        return 0;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        rootActivity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
        rootActivity?.AddEvent(new ActivityEvent("DownloadCancelled"));
        logger.LogWarning("Download cancelled after {Duration}ms", sessionStopwatch.ElapsedMilliseconds);
        return 130;
    }
    catch (Exception ex)
    {
        rootActivity.SetErrorStatus(ex);
        rootActivity?.AddEvent(new ActivityEvent("DownloadFailed", tags: new ActivityTagsCollection
        {
            ["exception.type"] = ex.GetType().FullName,
            ["exception.message"] = ex.Message
        }));

        logger.LogCritical(ex, "Fatal error during download");
        return 1;
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader/Telemetry/DiagnosticsConfig.cs")"
cat > "MyImapDownloader/Telemetry/DiagnosticsConfig.cs" <<'MYIMAP_EOF'
using System.Diagnostics;
using System.Diagnostics.Metrics;

using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Telemetry;

public static class DiagnosticsConfig
{
    public const string ServiceName = "MyImapDownloader";
    public const string ServiceVersion = "1.0.0";

    private static readonly DiagnosticsConfigBase Base = new(ServiceName, ServiceVersion);

    public static ActivitySource ActivitySource => Base.ActivitySource;
    public static Meter Meter => Base.Meter;

    public static readonly Counter<long> EmailsDownloaded = Base.CreateCounter<long>(
        "emails.downloaded", "emails", "Total emails downloaded");

    public static readonly Counter<long> RetryAttempts = Base.CreateCounter<long>(
        "retry.attempts", "attempts", "Number of retry attempts");

    public static readonly Counter<long> FilesWritten = Base.CreateCounter<long>(
        "storage.files.written", "files", "Number of email files written");

    public static readonly Counter<long> BytesWritten = Base.CreateCounter<long>(
        "storage.bytes.written", "bytes", "Total bytes written to disk");

    public static readonly Histogram<double> WriteLatency = Base.CreateHistogram<double>(
        "storage.write.latency", "ms", "Disk write latency");
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader/Telemetry/TelemetryExtensions.cs")"
cat > "MyImapDownloader/Telemetry/TelemetryExtensions.cs" <<'MYIMAP_EOF'
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Telemetry;

public static class TelemetryExtensions
{
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration) =>
        services.AddCoreTelemetry(
            configuration,
            DiagnosticsConfig.ServiceName,
            DiagnosticsConfig.ServiceVersion);
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader/appsettings.json")"
cat > "MyImapDownloader/appsettings.json" <<'MYIMAP_EOF'
{
  "Telemetry": {
    "ServiceName": "MyImapDownloader",
    "ServiceVersion": "1.0.0",
    "MaxFileSizeMB": 25,
    "EnableTracing": true,
    "EnableMetrics": true,
    "EnableLogging": true,
    "FlushIntervalSeconds": 5,
    "MetricsExportIntervalSeconds": 15
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  }
}
MYIMAP_EOF

mkdir -p "$(dirname "README.md")"
cat > "README.md" <<'MYIMAP_EOF'
# MyImapDownloader

[![Build and Test](https://github.com/kusl/MyImapDownloader/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/kusl/MyImapDownloader/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)

Two .NET 10 CLI tools:

- **MyImapDownloader** archives IMAP mailboxes to local `.eml` files with delta sync.
- **MyEmailSearch** indexes the archive into SQLite FTS5 and searches it.

> This project contains code generated by LLMs. Treat it as experimental.

## Safety

- IMAP folders are opened with `FolderAccess.ReadOnly`. No delete, move or flag commands exist.
- Archived `.eml` files are never overwritten or deleted. The only local deletion is cleanup of failed writes in `tmp/`.
- The search index is derived data and can be rebuilt at any time.

## Install

Build from source:

```bash
git clone https://github.com/kusl/MyImapDownloader.git
cd MyImapDownloader
dotnet build -c Release
```

Or install the latest self-contained Linux release to `/opt` with wrappers in `/usr/local/bin`:

```bash
bash install.sh
```

`install.sh` preserves an existing `appsettings.json`.

## MyImapDownloader

```bash
myimapdownloader -s imap.example.com -u you@example.com -p "app-password" -o ~/Documents/mail/you_example_com -a
```

| Option | Alias | Default | Description |
|---|---|---|---|
| `--server` | `-s` | required | IMAP server |
| `--username` | `-u` | required | Username |
| `--password` | `-p` | required | Password or app password |
| `--port` | `-r` | `993` | Port (implicit TLS) |
| `--output` | `-o` | `EmailArchive` | Archive directory |
| `--all-folders` | `-a` | `false` | All selectable folders instead of INBOX only |
| `--start-date` | | | Only messages delivered after this date (`yyyy-MM-dd`) |
| `--end-date` | | | Only messages delivered before this date (`yyyy-MM-dd`) |
| `--verbose` | `-v` | `false` | Debug logging |
| `--help`, `--version` | `-h` | | |

Arguments starting with `@` are passed through literally; response files are disabled.

Exit codes: `0` success, `1` failure or invalid arguments, `130` cancelled (Ctrl+C or SIGTERM; up to 30 s is allowed to flush state).

### Archive layout

```
<output>/
├── index.v1.db
└── INBOX/
    ├── cur/
    │   ├── 1702900000.abc123@mail.example.com.myhost.eml
    │   └── 1702900000.abc123@mail.example.com.myhost.eml.meta.json
    ├── new/
    └── tmp/
```

File names are `{internal-date-unix}.{normalized-message-id}.{host}.eml`. Messages are written to `tmp/` and moved to `cur/`.

Messages without a `Message-ID` header are identified by the SHA-256 of their content.

`index.v1.db` (SQLite, WAL):

```sql
CREATE TABLE Messages (MessageId TEXT PRIMARY KEY, Folder TEXT NOT NULL, ImportedAt TEXT NOT NULL);
CREATE TABLE SyncState (Folder TEXT PRIMARY KEY, LastUid INTEGER NOT NULL, UidValidity INTEGER NOT NULL);
```

Sidecar `.meta.json`:

```json
{
  "MessageId": "abc123@mail.example.com",
  "Subject": "Project Update",
  "From": "alice@example.com",
  "To": "bob@example.com",
  "Date": "2025-12-24T10:30:00Z",
  "Folder": "INBOX",
  "ArchivedAt": "2025-12-24T15:45:32Z",
  "HasAttachments": false
}
```

`HasAttachments` is currently always `false`.

### Delta sync

1. Read `LastUid`/`UidValidity` for the folder. A changed `UIDVALIDITY` resets the cursor to 0.
2. Search `UID > LastUid`, plus optional date filters.
3. Fetch envelopes in batches of 50 and skip Message-IDs already in `Messages`.
4. Stream each new message to disk.
5. After each batch, advance `LastUid` to the highest UID below the lowest failed UID in this folder run. Failed messages are retried on the next run.

Network errors are retried with exponential backoff capped at 5 minutes behind a circuit breaker. Authentication errors and cancellation are not retried.

### Recovery

If `index.v1.db` cannot be opened, it and its `-wal`/`-shm` files are renamed to `*.corrupt.<ticks>`, a new database is created and `Messages` is rebuilt from all `.meta.json` files. `SyncState` restarts at 0; existing messages are skipped by Message-ID.

### Configuration

`appsettings.json` next to the binary; environment variables override it (`Telemetry__EnableMetrics=false`).

```json
{
  "Telemetry": {
    "ServiceName": "MyImapDownloader",
    "ServiceVersion": "1.0.0",
    "MaxFileSizeMB": 25,
    "EnableTracing": true,
    "EnableMetrics": true,
    "EnableLogging": true,
    "FlushIntervalSeconds": 5,
    "MetricsExportIntervalSeconds": 15
  },
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft": "Warning", "System": "Warning" }
  }
}
```

### Telemetry

OpenTelemetry traces, metrics and logs are written as JSONL to the first writable directory of:

1. `$XDG_STATE_HOME/myimapdownloader/telemetry`
2. `$XDG_DATA_HOME/myimapdownloader/telemetry`
3. `~/.local/state/myimapdownloader/telemetry`
4. `~/.local/share/myimapdownloader/telemetry`
5. `./telemetry`

If none is writable, telemetry is disabled.

Files: `{traces|metrics|logs}/{kind}_{yyyyMMdd}_{seq:D4}.jsonl`, rotated daily and at `MaxFileSizeMB`. Properties are PascalCase and null values are omitted; tag and attribute values are strings.

Spans: `EmailArchiveSession`, `DownloadEmails`, `ProcessFolder`, `ProcessEmail`, `SaveStream`.

| Metric | Type | Unit |
|---|---|---|
| `emails.downloaded` | Counter | emails |
| `retry.attempts` | Counter | attempts |
| `storage.files.written` | Counter | files |
| `storage.bytes.written` | Counter | bytes |
| `storage.write.latency` | Histogram | ms |

.NET runtime metrics are also exported.

## MyEmailSearch

The archive root is expected to contain one directory per account, each holding MyImapDownloader output:

```
~/Documents/mail/<account>/<folder>/cur/*.eml
```

```bash
myemailsearch index --content
myemailsearch search "from:alice@example.com subject:report kafka"
myemailsearch search "date:2025-01-01..2025-06-30 invoice" --format json --limit 50
myemailsearch status
myemailsearch rebuild --yes --content
```

Global options, valid before or after the subcommand:

| Option | Alias | Description |
|---|---|---|
| `--archive` | `-a` | Archive root |
| `--database` | `-d` | Index database file |
| `--verbose` | `-v` | Debug logging |

| Command | Options |
|---|---|
| `search <query>` | `--limit/-l` (100), `--format/-f` `table\|json\|csv`, `--open/-o` |
| `index` | `--full/-f`, `--content` |
| `status` | |
| `rebuild` | `--yes/-y`, `--content` |

Without `--content`, only subject, sender and recipients are full-text indexed; a 500-character preview is always stored.

Query syntax:

| Term | Meaning |
|---|---|
| `from:addr` | Exact sender; `*` is a wildcard |
| `to:addr` | Recipient contains |
| `subject:text` or `subject:"two words"` | Subject contains |
| `date:YYYY-MM-DD` or `date:YYYY-MM-DD..YYYY-MM-DD` | Sent date range, end inclusive |
| `after:YYYY-MM-DD`, `before:YYYY-MM-DD` | Bounds, `before` inclusive |
| `account:name`, `folder:name` | Exact account or folder directory |
| remaining text | FTS5 phrase; trailing `*` for prefix match |

Defaults:

- Archive: `$MYIMAPDOWNLOADER_ARCHIVE`, then `$XDG_DATA_HOME/myimapdownloader`, `~/.local/share/myimapdownloader`, `~/Documents/mail`, `~/mail`.
- Database: `$XDG_DATA_HOME/myemailsearch/search.db`, else `~/.local/share/myemailsearch/search.db`.

Indexing is incremental by file path and modification time. Logs go to stderr, so `--format json` and `csv` output on stdout is clean. `search` exits `1` when no index exists.

## Development

```
MyImapDownloader.Core/         Shared telemetry, XDG paths, SQLite helpers
MyImapDownloader/              Downloader
MyEmailSearch/                 Search tool
*.Tests/                       TUnit tests
Directory.Packages.props       Central package versions
```

```bash
dotnet build
dotnet test
```

| Package | Use |
|---|---|
| MailKit / MimeKit | IMAP and MIME parsing |
| Microsoft.Data.Sqlite | SQLite and FTS5 |
| System.CommandLine | CLI parsing |
| Polly | Retry and circuit breaker |
| OpenTelemetry | Traces, metrics, logs |
| TUnit, NSubstitute, AwesomeAssertions | Tests |

JSON serialization uses source-generated `JsonSerializerContext`s and configuration binding uses the binder source generator.

## License

[AGPL-3.0](LICENSE).
MYIMAP_EOF

mkdir -p "$(dirname "MyEmailSearch/Data/SearchJsonContext.cs")"
cat > "MyEmailSearch/Data/SearchJsonContext.cs" <<'MYIMAP_EOF'
using System.Text.Json.Serialization;

namespace MyEmailSearch.Data;

[JsonSerializable(typeof(List<string>))]
internal partial class EmailDocumentJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SearchResultSet))]
internal partial class SearchOutputJsonContext : JsonSerializerContext
{
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core.Tests/Telemetry/JsonExporterTests.cs")"
cat > "MyImapDownloader.Core.Tests/Telemetry/JsonExporterTests.cs" <<'MYIMAP_EOF'
using System.Diagnostics;
using System.Diagnostics.Metrics;

using MyImapDownloader.Core.Infrastructure;
using MyImapDownloader.Core.Telemetry;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class JsonExporterTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("exporter_test");
    private readonly List<JsonTelemetryFileWriter> _writers = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var writer in _writers)
        {
            writer.Dispose();
        }
        await Task.Delay(100);
        _temp.Dispose();
    }

    private JsonTelemetryFileWriter CreateWriter(string prefix)
    {
        var writer = new JsonTelemetryFileWriter(
            Path.Combine(_temp.Path, prefix), prefix, 1024 * 1024, TimeSpan.FromSeconds(30));
        _writers.Add(writer);
        return writer;
    }

    private async Task<string> ReadAllAsync(string prefix)
    {
        var files = Directory.GetFiles(Path.Combine(_temp.Path, prefix), "*.jsonl");
        var parts = new List<string>();
        foreach (var file in files)
        {
            parts.Add(await File.ReadAllTextAsync(file));
        }
        return string.Concat(parts);
    }

    [Test]
    public async Task TraceExporter_WithNullWriter_ReturnsSuccess()
    {
        var exporter = new JsonFileTraceExporter(null);

        var result = exporter.Export(new Batch<Activity>([], 0));

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }

    [Test]
    public async Task LogExporter_WithNullWriter_ReturnsSuccess()
    {
        var exporter = new JsonFileLogExporter(null);

        var result = exporter.Export(new Batch<LogRecord>([], 0));

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }

    [Test]
    public async Task MetricsExporter_WithNullWriter_ReturnsSuccess()
    {
        var exporter = new JsonFileMetricsExporter(null);

        var result = exporter.Export(new Batch<Metric>([], 0));

        await Assert.That(result).IsEqualTo(ExportResult.Success);
    }

    [Test]
    public async Task TraceExporter_WritesSpanWithNonStringTags()
    {
        var writer = CreateWriter("traces");
        var exporter = new JsonFileTraceExporter(writer);

        var sourceName = $"trace.test.{Guid.NewGuid():N}";
        using var activitySource = new ActivitySource(sourceName);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var activity = activitySource.StartActivity("ExportTest");
        await Assert.That(activity).IsNotNull();
        activity!.SetTag("text.key", "text.value");
        activity.SetTag("number.key", 42);
        activity.Stop();

        var result = exporter.Export(new Batch<Activity>([activity], 1));
        await writer.FlushAsync();
        activity.Dispose();

        var content = await ReadAllAsync("traces");

        await Assert.That(result).IsEqualTo(ExportResult.Success);
        await Assert.That(content).Contains("ExportTest");
        await Assert.That(content).Contains("\"text.key\":\"text.value\"");
        await Assert.That(content).Contains("\"number.key\":\"42\"");
    }

    [Test]
    public async Task MetricsExporter_WritesHistogramMinAndMax()
    {
        var writer = CreateWriter("metrics");
        var meterName = $"metrics.test.{Guid.NewGuid():N}";

        using var meter = new Meter(meterName);
        var histogram = meter.CreateHistogram<double>("test.latency");
        var counter = meter.CreateCounter<long>("test.count");

        using (var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(meterName)
            .AddReader(new BaseExportingMetricReader(new JsonFileMetricsExporter(writer)))
            .Build())
        {
            histogram.Record(5);
            histogram.Record(15);
            counter.Add(3);
            provider.ForceFlush();
        }

        await writer.FlushAsync();
        var content = await ReadAllAsync("metrics");

        await Assert.That(content).Contains("\"MetricName\":\"test.latency\"");
        await Assert.That(content).Contains("\"Count\":2");
        await Assert.That(content).Contains("\"Min\":5");
        await Assert.That(content).Contains("\"Max\":15");
        await Assert.That(content).Contains("\"MetricName\":\"test.count\"");
        await Assert.That(content).Contains("\"Value\":3");
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core.Tests/Telemetry/TelemetryDirectoryResolverTests.cs")"
cat > "MyImapDownloader.Core.Tests/Telemetry/TelemetryDirectoryResolverTests.cs" <<'MYIMAP_EOF'
using MyImapDownloader.Core.Telemetry;

namespace MyImapDownloader.Core.Tests.Telemetry;

public class TelemetryDirectoryResolverTests
{
    [Test]
    public async Task ResolveTelemetryDirectory_ReturnsExistingDirectory()
    {
        var appName = $"ResolverTest_{Guid.NewGuid():N}";
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory(appName);

        try
        {
            await Assert.That(result).IsNotNull();
            await Assert.That(Directory.Exists(result!)).IsTrue();
        }
        finally
        {
            DeleteAppDirectory(result, appName);
        }
    }

    [Test]
    public async Task ResolveTelemetryDirectory_UsesLowercaseAppNameAndTelemetryLeaf()
    {
        var appName = $"ResolverCase_{Guid.NewGuid():N}";
        var result = TelemetryDirectoryResolver.ResolveTelemetryDirectory(appName);

        try
        {
            await Assert.That(result).IsNotNull();
            await Assert.That(Path.GetFileName(result!)).IsEqualTo("telemetry");

            if (!OperatingSystem.IsWindows())
            {
                await Assert.That(result!).Contains(appName.ToLowerInvariant());
            }
        }
        finally
        {
            DeleteAppDirectory(result, appName);
        }
    }

    private static void DeleteAppDirectory(string? telemetryDirectory, string appName)
    {
        if (telemetryDirectory == null) return;

        try
        {
            var appDirectory = Path.GetDirectoryName(telemetryDirectory);
            if (appDirectory != null &&
                string.Equals(Path.GetFileName(appDirectory), appName, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(appDirectory))
            {
                Directory.Delete(appDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core/Telemetry/TelemetryJsonContext.cs")"
cat > "MyImapDownloader.Core/Telemetry/TelemetryJsonContext.cs" <<'MYIMAP_EOF'
using System.Text.Json.Serialization;

namespace MyImapDownloader.Core.Telemetry;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TraceRecord))]
[JsonSerializable(typeof(MetricRecord))]
[JsonSerializable(typeof(LogRecordData))]
[JsonSerializable(typeof(HistogramValue))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
internal partial class TelemetryJsonContext : JsonSerializerContext
{
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Core/Telemetry/TelemetryTagFormatter.cs")"
cat > "MyImapDownloader.Core/Telemetry/TelemetryTagFormatter.cs" <<'MYIMAP_EOF'
using System.Globalization;

namespace MyImapDownloader.Core.Telemetry;

internal static class TelemetryTagFormatter
{
    public static Dictionary<string, string?> ToStringDictionary(IEnumerable<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, string?>();
        foreach (var tag in tags)
        {
            result[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
        }
        return result;
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Tests/EmailStorageServiceNoMessageIdTests.cs")"
cat > "MyImapDownloader.Tests/EmailStorageServiceNoMessageIdTests.cs" <<'MYIMAP_EOF'
using Microsoft.Extensions.Logging.Abstractions;

namespace MyImapDownloader.Tests;

public class EmailStorageServiceNoMessageIdTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new();

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    private static MemoryStream CreateRawEmailWithoutMessageId(string body)
    {
        var raw = $"""
            From: sender@test.com
            To: receiver@test.com
            Subject: No identifier
            Date: Wed, 01 May 2024 12:00:00 +0000

            {body}
            """;

        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(raw));
    }

    [Test]
    public async Task DistinctMessagesWithSameInternalDate_AreBothSaved()
    {
        await using var service = new EmailStorageService(NullLogger<EmailStorageService>.Instance, _temp.Path);
        await service.InitializeAsync(CancellationToken.None);

        var internalDate = new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero);

        using var first = CreateRawEmailWithoutMessageId("First body");
        using var second = CreateRawEmailWithoutMessageId("Second body");

        var savedFirst = await service.SaveStreamAsync(first, "", internalDate, "INBOX", CancellationToken.None);
        var savedSecond = await service.SaveStreamAsync(second, "", internalDate, "INBOX", CancellationToken.None);

        await Assert.That(savedFirst).IsTrue();
        await Assert.That(savedSecond).IsTrue();

        var emlFiles = Directory.GetFiles(Path.Combine(_temp.Path, "INBOX", "cur"), "*.eml");
        await Assert.That(emlFiles.Length).IsEqualTo(2);
    }

    [Test]
    public async Task IdenticalMessagesWithoutMessageId_AreDeduplicated()
    {
        await using var service = new EmailStorageService(NullLogger<EmailStorageService>.Instance, _temp.Path);
        await service.InitializeAsync(CancellationToken.None);

        var internalDate = new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero);

        using var first = CreateRawEmailWithoutMessageId("Same body");
        using var second = CreateRawEmailWithoutMessageId("Same body");

        var savedFirst = await service.SaveStreamAsync(first, "", internalDate, "INBOX", CancellationToken.None);
        var savedSecond = await service.SaveStreamAsync(second, "", internalDate.AddMinutes(5), "Archive", CancellationToken.None);

        await Assert.That(savedFirst).IsTrue();
        await Assert.That(savedSecond).IsFalse();
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader.Tests/FolderCheckpointTrackerTests.cs")"
cat > "MyImapDownloader.Tests/FolderCheckpointTrackerTests.cs" <<'MYIMAP_EOF'
namespace MyImapDownloader.Tests;

public class FolderCheckpointTrackerTests
{
    [Test]
    public async Task NoFailures_AdvancesToHighestProcessedUid()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkProcessed(1);
        tracker.MarkProcessed(2);
        tracker.MarkProcessed(5);

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(5L);
        await Assert.That(tracker.LowestFailedUid).IsNull();
    }

    [Test]
    public async Task FailureInMiddle_HoldsCheckpointBeforeFailure()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkProcessed(1);
        tracker.MarkProcessed(2);
        tracker.MarkFailed(3);
        tracker.MarkProcessed(4);

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(2L);
        await Assert.That(tracker.LowestFailedUid).IsEqualTo(3L);
    }

    [Test]
    public async Task FailureInEarlierBatch_IsNotSkippedByLaterBatches()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkProcessed(10);
        tracker.MarkFailed(11);

        for (uint uid = 12; uid <= 200; uid++)
        {
            tracker.MarkProcessed(uid);
        }

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(10L);
    }

    [Test]
    public async Task FirstUidFails_CheckpointStaysAtZero()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkFailed(7);
        tracker.MarkProcessed(8);

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(0L);
    }

    [Test]
    public async Task LowerFailureAfterHigherFailure_LowersCheckpoint()
    {
        var tracker = new FolderCheckpointTracker();

        tracker.MarkProcessed(1);
        tracker.MarkProcessed(2);
        tracker.MarkProcessed(3);
        tracker.MarkFailed(5);
        tracker.MarkFailed(2);

        await Assert.That(tracker.SafeCheckpoint).IsEqualTo(1L);
        await Assert.That(tracker.LowestFailedUid).IsEqualTo(2L);
    }
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader/DownloadCommand.cs")"
cat > "MyImapDownloader/DownloadCommand.cs" <<'MYIMAP_EOF'
using System.CommandLine;

namespace MyImapDownloader;

public static class DownloadCommand
{
    public static RootCommand Create(Func<DownloadOptions, CancellationToken, Task<int>> run)
    {
        var serverOption = new Option<string>("--server", "-s")
        {
            Description = "IMAP server address",
            Required = true
        };

        var usernameOption = new Option<string>("--username", "-u")
        {
            Description = "Email username",
            Required = true
        };

        var passwordOption = new Option<string>("--password", "-p")
        {
            Description = "Email password",
            Required = true
        };

        var portOption = new Option<int>("--port", "-r")
        {
            Description = "IMAP port",
            DefaultValueFactory = _ => 993
        };

        var outputOption = new Option<string>("--output", "-o")
        {
            Description = "Output directory for archived emails",
            DefaultValueFactory = _ => "EmailArchive"
        };

        var startDateOption = new Option<DateTime?>("--start-date")
        {
            Description = "Download emails from this date (yyyy-MM-dd)"
        };

        var endDateOption = new Option<DateTime?>("--end-date")
        {
            Description = "Download emails until this date (yyyy-MM-dd)"
        };

        var allFoldersOption = new Option<bool>("--all-folders", "-a")
        {
            Description = "Download from all folders, not just INBOX"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose logging"
        };

        var rootCommand = new RootCommand("Archive emails from an IMAP server to local .eml files");
        rootCommand.Options.Add(serverOption);
        rootCommand.Options.Add(usernameOption);
        rootCommand.Options.Add(passwordOption);
        rootCommand.Options.Add(portOption);
        rootCommand.Options.Add(outputOption);
        rootCommand.Options.Add(startDateOption);
        rootCommand.Options.Add(endDateOption);
        rootCommand.Options.Add(allFoldersOption);
        rootCommand.Options.Add(verboseOption);

        rootCommand.SetAction((parseResult, ct) => run(
            new DownloadOptions
            {
                Server = parseResult.GetValue(serverOption)!,
                Username = parseResult.GetValue(usernameOption)!,
                Password = parseResult.GetValue(passwordOption)!,
                Port = parseResult.GetValue(portOption),
                OutputDirectory = parseResult.GetValue(outputOption)!,
                StartDate = parseResult.GetValue(startDateOption),
                EndDate = parseResult.GetValue(endDateOption),
                AllFolders = parseResult.GetValue(allFoldersOption),
                Verbose = parseResult.GetValue(verboseOption)
            },
            ct));

        return rootCommand;
    }

    public static ParseResult Parse(RootCommand command, IReadOnlyList<string> args) =>
        command.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader/EmailMetadataJsonContext.cs")"
cat > "MyImapDownloader/EmailMetadataJsonContext.cs" <<'MYIMAP_EOF'
using System.Text.Json.Serialization;

namespace MyImapDownloader;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EmailMetadata))]
internal partial class EmailMetadataJsonContext : JsonSerializerContext
{
}
MYIMAP_EOF

mkdir -p "$(dirname "MyImapDownloader/FolderCheckpointTracker.cs")"
cat > "MyImapDownloader/FolderCheckpointTracker.cs" <<'MYIMAP_EOF'
namespace MyImapDownloader;

public sealed class FolderCheckpointTracker
{
    private long _safeCheckpoint;
    private long? _lowestFailedUid;

    public long SafeCheckpoint => _safeCheckpoint;

    public long? LowestFailedUid => _lowestFailedUid;

    public void MarkProcessed(uint uid)
    {
        if (_lowestFailedUid is null || uid < _lowestFailedUid)
        {
            _safeCheckpoint = Math.Max(_safeCheckpoint, uid);
        }
    }

    public void MarkFailed(uint uid)
    {
        if (_lowestFailedUid is null || uid < _lowestFailedUid)
        {
            _lowestFailedUid = uid;
        }

        if (_safeCheckpoint >= _lowestFailedUid)
        {
            _safeCheckpoint = _lowestFailedUid.Value - 1;
        }
    }
}
MYIMAP_EOF

dotnet build
dotnet test
