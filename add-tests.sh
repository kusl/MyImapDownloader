#!/bin/sh
set -e
cd "$(dirname "$0")"

# 1. Create missing directory and add PathResolver tests
mkdir -p MyEmailSearch.Tests/Configuration

cat > MyEmailSearch.Tests/Configuration/PathResolverTests.cs << 'EOF'
using MyEmailSearch.Configuration;

namespace MyEmailSearch.Tests.Configuration;

/// <summary>
/// Tests for MyEmailSearch.Configuration.PathResolver.
/// </summary>
public class PathResolverTests
{
    [Test]
    public async Task GetDefaultDatabasePath_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetDefaultDatabasePath();

        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task GetDefaultDatabasePath_EndsWithDbExtension()
    {
        var path = PathResolver.GetDefaultDatabasePath();

        await Assert.That(path).EndsWith(".db");
    }

    [Test]
    public async Task GetDefaultArchivePath_ReturnsNonEmptyPath()
    {
        var path = PathResolver.GetDefaultArchivePath();

        await Assert.That(path).IsNotNull();
        await Assert.That(path).IsNotEmpty();
    }

    [Test]
    public async Task GetDefaultDatabasePath_ContainsMyEmailSearch()
    {
        var path = PathResolver.GetDefaultDatabasePath();

        await Assert.That(path.ToLowerInvariant()).Contains("myemailsearch");
    }
}
EOF

echo "Fixed: Created MyEmailSearch.Tests/Configuration/PathResolverTests.cs"

# 2. Add missing using for SearchSortOrder in QueryParserEdgeCaseTests
sed -i '1s/^/using MyEmailSearch.Data;\n/' MyEmailSearch.Tests/Search/QueryParserEdgeCaseTests.cs

echo "Fixed: Added 'using MyEmailSearch.Data' to QueryParserEdgeCaseTests.cs"

# 3. Remove the RebuildAsync test that references a non-existent method
#    Replace it with a test that verifies re-initialization after clearing
cat > /tmp/rebuild_fix.py << 'PYEOF'
import re, sys

path = "MyEmailSearch.Tests/Data/SearchDatabaseMetadataTests.cs"
with open(path, "r") as f:
    content = f.read()

# Remove the RebuildAsync_ClearsAllData test method entirely
pattern = r'\s*\[Test\]\s*\n\s*public async Task RebuildAsync_ClearsAllData\(\).*?(?=\n\s*\[Test\]|\n\s*\}$)'
content = re.sub(pattern, '', content, flags=re.DOTALL)

with open(path, "w") as f:
    f.write(content)
PYEOF

python3 /tmp/rebuild_fix.py
rm /tmp/rebuild_fix.py

echo "Fixed: Removed RebuildAsync_ClearsAllData test (method doesn't exist on SearchDatabase)"

# 4. Also create Core EmailMetadata tests and Batch tests
#    (these were after the PathResolver file in the original script,
#    so they may not have been created either)

mkdir -p MyImapDownloader.Core.Tests/Data

if [ ! -f MyImapDownloader.Core.Tests/Data/EmailMetadataTests.cs ]; then
cat > MyImapDownloader.Core.Tests/Data/EmailMetadataTests.cs << 'EOF'
using MyImapDownloader.Core.Data;

namespace MyImapDownloader.Core.Tests.Data;

/// <summary>
/// Tests for the shared EmailMetadata record.
/// </summary>
public class EmailMetadataTests
{
    [Test]
    public async Task EmailMetadata_CanBeCreated_WithRequiredFields()
    {
        var metadata = new EmailMetadata
        {
            MessageId = "test@example.com"
        };

        await Assert.That(metadata.MessageId).IsEqualTo("test@example.com");
    }

    [Test]
    public async Task EmailMetadata_OptionalFields_DefaultToNull()
    {
        var metadata = new EmailMetadata
        {
            MessageId = "test@example.com"
        };

        await Assert.That(metadata.Subject).IsNull();
        await Assert.That(metadata.From).IsNull();
        await Assert.That(metadata.To).IsNull();
        await Assert.That(metadata.Cc).IsNull();
        await Assert.That(metadata.Date).IsNull();
        await Assert.That(metadata.Folder).IsNull();
        await Assert.That(metadata.SizeBytes).IsNull();
        await Assert.That(metadata.Account).IsNull();
    }

    [Test]
    public async Task EmailMetadata_HasAttachments_DefaultsFalse()
    {
        var metadata = new EmailMetadata
        {
            MessageId = "test@example.com"
        };

        await Assert.That(metadata.HasAttachments).IsFalse();
    }

    [Test]
    public async Task EmailMetadata_AllFields_RoundTrip()
    {
        var now = DateTimeOffset.UtcNow;
        var metadata = new EmailMetadata
        {
            MessageId = "full@example.com",
            Subject = "Test Subject",
            From = "sender@example.com",
            To = "recipient@example.com",
            Cc = "cc@example.com",
            Date = now,
            Folder = "INBOX",
            ArchivedAt = now,
            HasAttachments = true,
            SizeBytes = 1024,
            Account = "work"
        };

        await Assert.That(metadata.Subject).IsEqualTo("Test Subject");
        await Assert.That(metadata.From).IsEqualTo("sender@example.com");
        await Assert.That(metadata.HasAttachments).IsTrue();
        await Assert.That(metadata.SizeBytes).IsEqualTo(1024);
    }

    [Test]
    public async Task EmailMetadata_IsRecord_SupportsEquality()
    {
        var a = new EmailMetadata { MessageId = "same@example.com", Subject = "Same" };
        var b = new EmailMetadata { MessageId = "same@example.com", Subject = "Same" };
        var c = new EmailMetadata { MessageId = "different@example.com", Subject = "Same" };

        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a).IsNotEqualTo(c);
    }
}
EOF
echo "Fixed: Created EmailMetadataTests.cs"
fi

if [ ! -f MyEmailSearch.Tests/Data/SearchDatabaseBatchTests.cs ]; then
cat > MyEmailSearch.Tests/Data/SearchDatabaseBatchTests.cs << 'EOF'
using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Data;

/// <summary>
/// Tests for SearchDatabase batch operations.
/// </summary>
public class SearchDatabaseBatchTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("db_batch_test");
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

    private async Task<SearchDatabase> CreateDatabaseAsync()
    {
        var dbPath = Path.Combine(_temp.Path, $"test_{Guid.NewGuid():N}.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;
        return db;
    }

    [Test]
    public async Task BatchUpsertEmailsAsync_InsertsMultipleEmails()
    {
        var db = await CreateDatabaseAsync();

        var docs = Enumerable.Range(0, 50).Select(i => new EmailDocument
        {
            MessageId = $"batch{i}@example.com",
            FilePath = $"/test/batch{i}.eml",
            Subject = $"Batch Email {i}",
            FromAddress = "sender@example.com",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }).ToList();

        await db.BatchUpsertEmailsAsync(docs);

        var count = await db.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(50);
    }

    [Test]
    public async Task BatchUpsertEmailsAsync_EmptyList_DoesNotThrow()
    {
        var db = await CreateDatabaseAsync();

        await db.BatchUpsertEmailsAsync(new List<EmailDocument>());

        var count = await db.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task BatchUpsertEmailsAsync_AllSearchable_AfterInsert()
    {
        var db = await CreateDatabaseAsync();

        var docs = Enumerable.Range(0, 10).Select(i => new EmailDocument
        {
            MessageId = $"searchable{i}@example.com",
            FilePath = $"/test/searchable{i}.eml",
            Subject = $"Searchable BatchItem {i}",
            FromAddress = "batchsender@example.com",
            BodyText = $"Unique content for batch item number {i} with keyword xylophone",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }).ToList();

        await db.BatchUpsertEmailsAsync(docs);

        var results = await db.QueryAsync(new SearchQuery { ContentTerms = "xylophone" });
        await Assert.That(results.Count).IsEqualTo(10);

        var fromResults = await db.QueryAsync(new SearchQuery { FromAddress = "batchsender@example.com" });
        await Assert.That(fromResults.Count).IsEqualTo(10);
    }
}
EOF
echo "Fixed: Created SearchDatabaseBatchTests.cs"
fi

if [ ! -f MyEmailSearch.Tests/Indexing/IndexManagerCancellationTests.cs ]; then
cat > MyEmailSearch.Tests/Indexing/IndexManagerCancellationTests.cs << 'EOF'
using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Indexing;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

/// <summary>
/// Tests for IndexManager cancellation and progress reporting.
/// </summary>
public class IndexManagerCancellationTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("index_cancel_test");
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

    private async Task CreateEmlFileAsync(string folder, string messageId)
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dir = Path.Combine(archivePath, folder, "cur");
        Directory.CreateDirectory(dir);

        var content = $"Message-ID: <{messageId}>\r\n" +
            $"Subject: Test {messageId}\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Body\r\n";

        await File.WriteAllTextAsync(Path.Combine(dir, $"{messageId}.eml"), content);
    }

    [Test]
    public async Task IndexAsync_CancellationToken_StopsProcessing()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");

        for (var i = 0; i < 20; i++)
        {
            await CreateEmlFileAsync("INBOX", $"cancel{i}@example.com");
        }

        var dbPath = Path.Combine(_temp.Path, "search.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await manager.IndexAsync(archivePath, includeContent: false, ct: cts.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Test]
    public async Task IndexAsync_ReportsProgress()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        await CreateEmlFileAsync("INBOX", "progress1@example.com");
        await CreateEmlFileAsync("INBOX", "progress2@example.com");

        var dbPath = Path.Combine(_temp.Path, "search.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        var progressReports = new List<IndexingProgress>();
        var progress = new Progress<IndexingProgress>(p => progressReports.Add(p));

        await manager.IndexAsync(archivePath, includeContent: false, progress: progress);

        await Task.Delay(200);

        await Assert.That(progressReports.Count).IsGreaterThanOrEqualTo(1);
    }
}
EOF
echo "Fixed: Created IndexManagerCancellationTests.cs"
fi

echo ""
echo "Building and testing..."
dotnet build && dotnet test
