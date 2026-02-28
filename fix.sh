#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Fix refactor defects
# =============================================================================
# Defect 1: before: missing end-of-day in QueryParser.cs
# Defect 2: SnippetGenerator.Generate is static but SearchEngine stores
#           an unnecessary instance, producing CS0176 warning
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== Fixing refactor defects ==="
echo ""

# ---------------------------------------------------------------------------
# Fix 1: MyEmailSearch/Search/QueryParser.cs
# - before: should use end-of-day to be consistent with date: ranges
# ---------------------------------------------------------------------------
echo "--- Fixing QueryParser.cs (before: end-of-day) ---"

cat > MyEmailSearch/Search/QueryParser.cs << 'QUERYPARSER_EOF'
using System.Text.RegularExpressions;

using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

/// <summary>
/// Parses user search queries into structured SearchQuery objects.
/// Supports syntax like: from:alice@example.com subject:"project update" kafka
/// </summary>
public sealed partial class QueryParser
{
    [GeneratedRegex("""from:(?<value>"[^"]+"|\S+)""", RegexOptions.IgnoreCase)]
    private static partial Regex FromPattern();

    [GeneratedRegex("""to:(?<value>"[^"]+"|\S+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ToPattern();

    [GeneratedRegex("""subject:(?<value>"[^"]+"|\S+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectPattern();

    [GeneratedRegex(@"date:(?<from>\d{4}-\d{2}-\d{2})(?:\.\.(?<to>\d{4}-\d{2}-\d{2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"account:(?<value>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccountPattern();

    [GeneratedRegex("""folder:(?<value>"[^"]+"|\S+)""", RegexOptions.IgnoreCase)]
    private static partial Regex FolderPattern();

    [GeneratedRegex(@"after:(?<value>\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex AfterPattern();

    [GeneratedRegex(@"before:(?<value>\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex BeforePattern();

    /// <summary>
    /// Parses a user query string into a SearchQuery object.
    /// </summary>
    public SearchQuery Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SearchQuery();
        }

        var remaining = input;
        string? fromAddress = null;
        string? toAddress = null;
        string? subject = null;
        string? account = null;
        string? folder = null;
        DateTimeOffset? dateFrom = null;
        DateTimeOffset? dateTo = null;

        // Extract from: field
        var fromMatch = FromPattern().Match(remaining);
        if (fromMatch.Success)
        {
            fromAddress = ExtractValue(fromMatch.Groups["value"].Value);
            remaining = FromPattern().Replace(remaining, "", 1);
        }

        // Extract to: field
        var toMatch = ToPattern().Match(remaining);
        if (toMatch.Success)
        {
            toAddress = ExtractValue(toMatch.Groups["value"].Value);
            remaining = ToPattern().Replace(remaining, "", 1);
        }

        // Extract subject: field
        var subjectMatch = SubjectPattern().Match(remaining);
        if (subjectMatch.Success)
        {
            subject = ExtractValue(subjectMatch.Groups["value"].Value);
            remaining = SubjectPattern().Replace(remaining, "", 1);
        }

        // Extract date: field
        var dateMatch = DatePattern().Match(remaining);
        if (dateMatch.Success)
        {
            if (DateTimeOffset.TryParse(dateMatch.Groups["from"].Value, out var from))
            {
                dateFrom = from;
            }
            if (dateMatch.Groups["to"].Success &&
                DateTimeOffset.TryParse(dateMatch.Groups["to"].Value, out var to))
            {
                dateTo = to.AddDays(1).AddTicks(-1); // End of day
            }
            remaining = DatePattern().Replace(remaining, "", 1);
        }

        // Extract account: field
        var accountMatch = AccountPattern().Match(remaining);
        if (accountMatch.Success)
        {
            account = accountMatch.Groups["value"].Value;
            remaining = AccountPattern().Replace(remaining, "", 1);
        }

        // Extract folder: field
        var folderMatch = FolderPattern().Match(remaining);
        if (folderMatch.Success)
        {
            folder = ExtractValue(folderMatch.Groups["value"].Value);
            remaining = FolderPattern().Replace(remaining, "", 1);
        }

        // Extract after: field
        var afterMatch = AfterPattern().Match(remaining);
        if (afterMatch.Success)
        {
            if (DateTimeOffset.TryParse(afterMatch.Groups["value"].Value, out var date))
            {
                dateFrom = date;
            }
            remaining = AfterPattern().Replace(remaining, "", 1);
        }

        // Extract before: field (end of day, consistent with date: ranges)
        var beforeMatch = BeforePattern().Match(remaining);
        if (beforeMatch.Success)
        {
            if (DateTimeOffset.TryParse(beforeMatch.Groups["value"].Value, out var date))
            {
                dateTo = date.AddDays(1).AddTicks(-1); // End of day for consistency
            }
            remaining = BeforePattern().Replace(remaining, "", 1);
        }

        // Remaining text is full-text content search
        var contentTerms = remaining.Trim();

        return new SearchQuery
        {
            FromAddress = fromAddress,
            ToAddress = toAddress,
            Subject = subject,
            ContentTerms = string.IsNullOrWhiteSpace(contentTerms) ? null : contentTerms,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Account = account,
            Folder = folder
        };
    }

    private static string ExtractValue(string value)
    {
        // Remove surrounding quotes if present
        if (value.StartsWith('"') && value.EndsWith('"') && value.Length > 2)
        {
            return value[1..^1];
        }
        return value;
    }
}
QUERYPARSER_EOF

echo "  Written: MyEmailSearch/Search/QueryParser.cs"

# ---------------------------------------------------------------------------
# Fix 2: MyEmailSearch/Search/SearchEngine.cs
# - Remove SnippetGenerator instance dependency
# - Call SnippetGenerator.Generate() statically (it IS static)
# ---------------------------------------------------------------------------
echo "--- Fixing SearchEngine.cs (remove dead SnippetGenerator dependency) ---"

cat > MyEmailSearch/Search/SearchEngine.cs << 'SEARCHENGINE_EOF'
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

/// <summary>
/// Main search engine that coordinates queries against the SQLite database.
/// </summary>
public sealed class SearchEngine(
    SearchDatabase database,
    QueryParser queryParser,
    ILogger<SearchEngine> logger)
{
    private readonly SearchDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly QueryParser _queryParser = queryParser ?? throw new ArgumentNullException(nameof(queryParser));
    private readonly ILogger<SearchEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Executes a search query string and returns results.
    /// </summary>
    public async Task<SearchResultSet> SearchAsync(
        string queryString,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return new SearchResultSet
            {
                Results = [],
                TotalCount = 0,
                Skip = offset,
                Take = limit,
                QueryTime = TimeSpan.Zero
            };
        }

        var query = _queryParser.Parse(queryString);
        query = query with { Take = limit, Skip = offset };

        return await SearchAsync(query, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a parsed search query and returns results.
    /// </summary>
    private async Task<SearchResultSet> SearchAsync(
        SearchQuery query,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Executing search: {Query}", FormatQueryForLog(query));

        // Execute the search query (with LIMIT)
        var emails = await _database.QueryAsync(query, ct).ConfigureAwait(false);

        // Get actual total count (without LIMIT) for accurate pagination
        var totalCount = await _database.GetTotalCountForQueryAsync(query, ct).ConfigureAwait(false);

        var results = new List<SearchResult>();
        foreach (var email in emails)
        {
            var snippet = !string.IsNullOrWhiteSpace(query.ContentTerms)
                ? SnippetGenerator.Generate(email.BodyText, query.ContentTerms)
                : email.BodyPreview;

            results.Add(new SearchResult
            {
                Email = email,
                Snippet = snippet,
                MatchedTerms = ExtractMatchedTerms(query)
            });
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Search completed: {ResultCount} results returned, {TotalCount} total matches in {ElapsedMs}ms",
            results.Count, totalCount, stopwatch.ElapsedMilliseconds);

        return new SearchResultSet
        {
            Results = results,
            TotalCount = totalCount,
            Skip = query.Skip,
            Take = query.Take,
            QueryTime = stopwatch.Elapsed
        };
    }

    private static string FormatQueryForLog(SearchQuery query)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.FromAddress)) parts.Add($"from:{query.FromAddress}");
        if (!string.IsNullOrWhiteSpace(query.ToAddress)) parts.Add($"to:{query.ToAddress}");
        if (!string.IsNullOrWhiteSpace(query.Subject)) parts.Add($"subject:{query.Subject}");
        if (!string.IsNullOrWhiteSpace(query.ContentTerms)) parts.Add(query.ContentTerms);
        if (!string.IsNullOrWhiteSpace(query.Account)) parts.Add($"account:{query.Account}");
        if (!string.IsNullOrWhiteSpace(query.Folder)) parts.Add($"folder:{query.Folder}");
        return string.Join(" ", parts);
    }

    private static IReadOnlyList<string> ExtractMatchedTerms(SearchQuery query)
    {
        var terms = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.ContentTerms))
        {
            terms.AddRange(query.ContentTerms.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return terms;
    }
}
SEARCHENGINE_EOF

echo "  Written: MyEmailSearch/Search/SearchEngine.cs"

# ---------------------------------------------------------------------------
# Fix 3: MyEmailSearch/Program.cs
# - Remove SnippetGenerator from DI
# - Update SearchEngine construction (3 params instead of 4)
# ---------------------------------------------------------------------------
echo "--- Fixing Program.cs (remove SnippetGenerator DI) ---"

cat > MyEmailSearch/Program.cs << 'PROGRAM_EOF'
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

        // Define Global options
        var archiveOption = new Option<string?>("--archive", "-a")
        {
            Description = "Path to the email archive directory"
        };

        var databaseOption = new Option<string?>("--database", "-d")
        {
            Description = "Path to the search database file"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Enable verbose output"
        };

        // Add options to the root command (acting as global options)
        rootCommand.Options.Add(archiveOption);
        rootCommand.Options.Add(databaseOption);
        rootCommand.Options.Add(verboseOption);

        // Add subcommands using the Subcommands collection
        rootCommand.Subcommands.Add(SearchCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(IndexCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(StatusCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(RebuildCommand.Create(archiveOption, databaseOption, verboseOption));

        // Use the modern invocation pattern for System.CommandLine 2.0.x
        return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a service provider with all required dependencies, manually resolving path-based dependencies.
    /// </summary>
    public static ServiceProvider CreateServiceProvider(
        string archivePath,
        string databasePath,
        bool verbose)
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });

        // Database - manually passing the databasePath
        services.AddSingleton(sp =>
            new SearchDatabase(databasePath, sp.GetRequiredService<ILogger<SearchDatabase>>()));

        // Search components
        services.AddSingleton<QueryParser>();
        services.AddSingleton(sp => new SearchEngine(
            sp.GetRequiredService<SearchDatabase>(),
            sp.GetRequiredService<QueryParser>(),
            sp.GetRequiredService<ILogger<SearchEngine>>()));

        // Indexing components - manually passing the archivePath to EmailParser
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
PROGRAM_EOF

echo "  Written: MyEmailSearch/Program.cs"

# ---------------------------------------------------------------------------
# Fix 4: Update test files that construct SearchEngine
# Remove SnippetGenerator argument from constructor calls
# ---------------------------------------------------------------------------
echo "--- Fixing test files (SearchEngine constructor calls) ---"

# SearchEngineTests.cs
cat > MyEmailSearch.Tests/Search/SearchEngineTests.cs << 'SETESTS_EOF'
using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Search;

public class SearchEngineTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("search_engine_test");
    private SearchDatabase? _database;

    public async ValueTask DisposeAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
        await Task.Delay(100);
        _temp.Dispose();
    }

    private async Task<(SearchDatabase db, SearchEngine engine)> CreateServicesAsync()
    {
        var dbPath = Path.Combine(_temp.Path, "test.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var queryParser = new QueryParser();
        var engine = new SearchEngine(db, queryParser,
            NullLogger<SearchEngine>.Instance);

        return (db, engine);
    }

    [Test]
    public async Task SearchAsync_ReturnsResults()
    {
        var (db, engine) = await CreateServicesAsync();
        await db.UpsertEmailAsync(CreateDocument("search@example.com", "Test Subject"));

        var results = await engine.SearchAsync("test");

        await Assert.That(results.TotalCount).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_AppliesPagination()
    {
        var (db, engine) = await CreateServicesAsync();
        for (int i = 0; i < 15; i++)
        {
            await db.UpsertEmailAsync(CreateDocument($"page{i}@example.com", $"Page Test {i}"));
        }

        var results = await engine.SearchAsync("page", limit: 5, offset: 0);

        await Assert.That(results.Results.Count).IsEqualTo(5);
        await Assert.That(results.TotalCount).IsEqualTo(15);
        await Assert.That(results.HasMore).IsTrue();
    }

    [Test]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResults()
    {
        var (db, engine) = await CreateServicesAsync();
        await db.UpsertEmailAsync(CreateDocument("empty@example.com", "Subject"));

        var results = await engine.SearchAsync("");

        await Assert.That(results.TotalCount).IsEqualTo(0);
    }

    [Test]
    public async Task SearchAsync_ReturnsQueryTime()
    {
        var (db, engine) = await CreateServicesAsync();
        await db.UpsertEmailAsync(CreateDocument("time@example.com", "Subject"));

        var results = await engine.SearchAsync("subject");

        await Assert.That(results.QueryTime.TotalMilliseconds).IsGreaterThanOrEqualTo(0);
    }

    private static EmailDocument CreateDocument(string messageId, string subject)
    {
        return new EmailDocument
        {
            MessageId = messageId,
            FilePath = $"/test/{messageId}.eml",
            Subject = subject,
            FromAddress = "sender@example.com",
            ToAddressesJson = "[\"recipient@example.com\"]",
            DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = DateTime.UtcNow.Ticks
        };
    }
}
SETESTS_EOF

echo "  Written: MyEmailSearch.Tests/Search/SearchEngineTests.cs"

# SearchEngineEdgeCaseTests.cs
cat > MyEmailSearch.Tests/Search/SearchEngineEdgeCaseTests.cs << 'SEEDGE_EOF'
using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Edge case tests for SearchEngine.
/// </summary>
public class SearchEngineEdgeCaseTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("engine_edge_test");
    private SearchDatabase? _database;

    public async ValueTask DisposeAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
        }
        await Task.Delay(100);
        _temp.Dispose();
    }

    private async Task<(SearchDatabase db, SearchEngine engine)> CreateServicesAsync()
    {
        var dbPath = Path.Combine(_temp.Path, "test.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var engine = new SearchEngine(db, new QueryParser(),
            NullLogger<SearchEngine>.Instance);
        return (db, engine);
    }

    [Test]
    public async Task SearchAsync_OffsetBeyondResults_ReturnsEmpty()
    {
        var (db, engine) = await CreateServicesAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "only@example.com",
            FilePath = "/test/only.eml",
            Subject = "Only Result",
            FromAddress = "sender@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var results = await engine.SearchAsync("only", limit: 10, offset: 100);

        await Assert.That(results.Results.Count).IsEqualTo(0);
        await Assert.That(results.TotalCount).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_NoMatchingResults_ReturnsTotalCountZero()
    {
        var (db, engine) = await CreateServicesAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "existing@example.com",
            FilePath = "/test/existing.eml",
            Subject = "Existing Email",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var results = await engine.SearchAsync("nonexistentquerythatmatchesnothing");

        await Assert.That(results.TotalCount).IsEqualTo(0);
        await Assert.That(results.Results.Count).IsEqualTo(0);
        await Assert.That(results.HasMore).IsFalse();
    }

    [Test]
    public async Task SearchAsync_WithSnippets_GeneratesSnippetsForContentSearch()
    {
        var (db, engine) = await CreateServicesAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "snippet@example.com",
            FilePath = "/test/snippet.eml",
            Subject = "Snippet Test",
            BodyText = "This email contains a very specific keyword called xylophone in the body.",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var results = await engine.SearchAsync("xylophone");

        await Assert.That(results.Results.Count).IsEqualTo(1);
        // Snippet should be generated for content-based searches
    }

    [Test]
    public async Task SearchAsync_NullQuery_ReturnsEmpty()
    {
        var (_, engine) = await CreateServicesAsync();

        var results = await engine.SearchAsync((string)null!);

        await Assert.That(results.TotalCount).IsEqualTo(0);
    }
}
SEEDGE_EOF

echo "  Written: MyEmailSearch.Tests/Search/SearchEngineEdgeCaseTests.cs"

# SearchEngineCountTests.cs
cat > MyEmailSearch.Tests/Search/SearchEngineCountTests.cs << 'SECOUNT_EOF'
using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Tests for SearchEngine total count behavior.
/// </summary>
public class SearchEngineCountTests : IAsyncDisposable
{
    private readonly string _testDirectory;
    private SearchDatabase? _database;

    public SearchEngineCountTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"engine_count_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        if (_database != null)
        {
            await _database.DisposeAsync();
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

    private async Task<(SearchDatabase db, SearchEngine engine)> CreateServicesAsync()
    {
        var dbPath = Path.Combine(_testDirectory, "test.db");
        var dbLogger = new NullLogger<SearchDatabase>();
        var db = new SearchDatabase(dbPath, dbLogger);
        await db.InitializeAsync();
        _database = db;

        var queryParser = new QueryParser();
        var engineLogger = new NullLogger<SearchEngine>();
        var engine = new SearchEngine(db, queryParser, engineLogger);

        return (db, engine);
    }

    [Test]
    public async Task SearchAsync_WithLimit_ReturnsTotalCountOfAllMatches()
    {
        // Arrange
        var (db, engine) = await CreateServicesAsync();

        // Insert 150 emails to "recipient@tilde.team"
        for (var i = 0; i < 150; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "sender@example.com",
                ToAddressesJson = "[\"recipient@tilde.team\"]",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Act
        var results = await engine.SearchAsync("to:recipient@tilde.team", limit: 20, offset: 0);

        // Assert - TotalCount should be 150, not capped at 20
        results.TotalCount.Should().Be(150);
        results.Results.Should().HaveCount(20);
        results.HasMore.Should().BeTrue();
    }

    [Test]
    public async Task SearchAsync_Pagination_TotalCountConsistentAcrossPages()
    {
        // Arrange
        var (db, engine) = await CreateServicesAsync();

        for (var i = 0; i < 100; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"test{i}@example.com",
                FilePath = $"/test/email{i}.eml",
                Subject = $"Test Email {i}",
                FromAddress = "alice@example.com",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        // Act - Get first page
        var page1 = await engine.SearchAsync("from:alice@example.com", limit: 20, offset: 0);
        // Act - Get second page
        var page2 = await engine.SearchAsync("from:alice@example.com", limit: 20, offset: 20);
        // Act - Get third page
        var page3 = await engine.SearchAsync("from:alice@example.com", limit: 20, offset: 40);

        // Assert - Total count should be consistent across pages
        page1.TotalCount.Should().Be(100);
        page2.TotalCount.Should().Be(100);
        page3.TotalCount.Should().Be(100);

        // Results should be paginated correctly
        page1.Results.Should().HaveCount(20);
        page2.Results.Should().HaveCount(20);
        page3.Results.Should().HaveCount(20);
    }
}
SECOUNT_EOF

echo "  Written: MyEmailSearch.Tests/Search/SearchEngineCountTests.cs"

# ---------------------------------------------------------------------------
# Build and test
# ---------------------------------------------------------------------------
echo ""
echo "=== Building ==="
dotnet build --configuration Release 2>&1 | tail -5

echo ""
echo "=== Running tests ==="
dotnet test --configuration Release --verbosity normal 2>&1 | tail -20

echo ""
echo "=== Done ==="
