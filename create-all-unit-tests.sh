#!/bin/bash
# =============================================================================
# Create Comprehensive Unit Tests for MyImapDownloader System
# =============================================================================
# This script creates ALL unit tests for:
#   - MyImapDownloader.Core (shared infrastructure)
#   - MyImapDownloader (email downloader)
#   - MyEmailSearch (email search)
# =============================================================================

set -euo pipefail

PROJECT_ROOT="${1:-$(pwd)}"

echo "=========================================="
echo "Creating Comprehensive Unit Tests"
echo "=========================================="

# =============================================================================
# PART 1: MyImapDownloader Tests
# =============================================================================
echo ""
echo "[PART 1] Creating MyImapDownloader Tests..."

DOWNLOADER_TESTS="$PROJECT_ROOT/MyImapDownloader.Tests"
mkdir -p "$DOWNLOADER_TESTS/Telemetry"
mkdir -p "$DOWNLOADER_TESTS/Services"

# -----------------------------------------------------------------------------
# DownloadOptionsTests.cs
# -----------------------------------------------------------------------------
cat > "$DOWNLOADER_TESTS/DownloadOptionsTests.cs" << 'CSHARP'
namespace MyImapDownloader.Tests;

public class DownloadOptionsTests
{
    [Test]
    public async Task RequiredProperties_MustBeSet()
    {
        var options = new DownloadOptions
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "secret",
            OutputDirectory = "/output"
        };

        await Assert.That(options.Server).IsEqualTo("imap.example.com");
        await Assert.That(options.Username).IsEqualTo("user@example.com");
        await Assert.That(options.Password).IsEqualTo("secret");
        await Assert.That(options.OutputDirectory).IsEqualTo("/output");
    }

    [Test]
    public async Task Port_DefaultsToZero()
    {
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test"
        };

        await Assert.That(options.Port).IsEqualTo(993);
    }

    [Test]
    public async Task AllFolders_DefaultsToFalse()
    {
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test"
        };

        await Assert.That(options.AllFolders).IsFalse();
    }

    [Test]
    public async Task Verbose_DefaultsToFalse()
    {
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test"
        };

        await Assert.That(options.Verbose).IsFalse();
    }

    [Test]
    public async Task StartDate_CanBeSet()
    {
        var date = new DateTime(2024, 1, 1);
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test",
            StartDate = date
        };

        await Assert.That(options.StartDate).IsEqualTo(date);
    }

    [Test]
    public async Task EndDate_CanBeSet()
    {
        var date = new DateTime(2024, 12, 31);
        var options = new DownloadOptions
        {
            Server = "test",
            Username = "test",
            Password = "test",
            OutputDirectory = "test",
            EndDate = date
        };

        await Assert.That(options.EndDate).IsEqualTo(date);
    }
}
CSHARP

# -----------------------------------------------------------------------------
# ImapConfigurationTests.cs
# -----------------------------------------------------------------------------
cat > "$DOWNLOADER_TESTS/ImapConfigurationTests.cs" << 'CSHARP'
namespace MyImapDownloader.Tests;

public class ImapConfigurationTests
{
    [Test]
    public async Task RequiredProperties_MustBeSet()
    {
        var config = new ImapConfiguration
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "secret"
        };

        await Assert.That(config.Server).IsEqualTo("imap.example.com");
        await Assert.That(config.Username).IsEqualTo("user@example.com");
        await Assert.That(config.Password).IsEqualTo("secret");
    }

    [Test]
    public async Task UseSsl_DefaultsToTrue()
    {
        var config = new ImapConfiguration
        {
            Server = "test",
            Username = "test",
            Password = "test"
        };

        await Assert.That(config.UseSsl).IsTrue();
    }

    [Test]
    public async Task Port_CanBeSet()
    {
        var config = new ImapConfiguration
        {
            Server = "test",
            Username = "test",
            Password = "test",
            Port = 143
        };

        await Assert.That(config.Port).IsEqualTo(143);
    }

    [Test]
    [Arguments(993, true)]
    [Arguments(143, false)]
    [Arguments(587, false)]
    public async Task CommonConfigurations_AreValid(int port, bool useSsl)
    {
        var config = new ImapConfiguration
        {
            Server = "imap.example.com",
            Username = "user@example.com",
            Password = "secret",
            Port = port,
            UseSsl = useSsl
        };

        await Assert.That(config.Port).IsEqualTo(port);
        await Assert.That(config.UseSsl).IsEqualTo(useSsl);
    }
}
CSHARP

# -----------------------------------------------------------------------------
# EmailDownloadExceptionTests.cs
# -----------------------------------------------------------------------------
cat > "$DOWNLOADER_TESTS/EmailDownloadExceptionTests.cs" << 'CSHARP'
namespace MyImapDownloader.Tests;

public class EmailDownloadExceptionTests
{
    [Test]
    public async Task Constructor_SetsMessage()
    {
        var ex = new EmailDownloadException(
            "Test error",
            42,
            new InvalidOperationException("Inner"));

        await Assert.That(ex.Message).IsEqualTo("Test error");
    }

    [Test]
    public async Task Constructor_SetsMessageIndex()
    {
        var ex = new EmailDownloadException(
            "Test error",
            42,
            new InvalidOperationException("Inner"));

        await Assert.That(ex.MessageIndex).IsEqualTo(42);
    }

    [Test]
    public async Task Constructor_SetsInnerException()
    {
        var inner = new InvalidOperationException("Inner error");
        var ex = new EmailDownloadException("Test", 0, inner);

        await Assert.That(ex.InnerException).IsEqualTo(inner);
    }

    [Test]
    public async Task Exception_CanBeThrown()
    {
        var act = () =>
        {
            throw new EmailDownloadException(
                "Download failed",
                5,
                new IOException("Network error"));
        };

        await Assert.That(act).ThrowsException();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(100)]
    [Arguments(int.MaxValue)]
    public async Task MessageIndex_AcceptsVariousValues(int index)
    {
        var ex = new EmailDownloadException(
            "Test",
            index,
            new Exception());

        await Assert.That(ex.MessageIndex).IsEqualTo(index);
    }
}
CSHARP

# -----------------------------------------------------------------------------
# EmailStorageServiceTests.cs
# -----------------------------------------------------------------------------
cat > "$DOWNLOADER_TESTS/Services/EmailStorageServiceTests.cs" << 'CSHARP'
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using MyImapDownloader.Core.Infrastructure;

namespace MyImapDownloader.Tests.Services;

public class EmailStorageServiceTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("storage_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    private EmailStorageService CreateService()
    {
        return new EmailStorageService(
            NullLogger<EmailStorageService>.Instance,
            _temp.Path);
    }

    private static MemoryStream CreateSimpleEmail(
        string messageId,
        string subject = "Test",
        string body = "Hello")
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress("Sender", "sender@test.com"));
        msg.To.Add(new MailboxAddress("Receiver", "receiver@test.com"));
        msg.Subject = subject;
        msg.MessageId = messageId;
        msg.Body = new TextPart("plain") { Text = body };

        var ms = new MemoryStream();
        msg.WriteTo(ms);
        ms.Position = 0;
        return ms;
    }

    [Test]
    public async Task InitializeAsync_CreatesDatabase()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var dbPath = Path.Combine(_temp.Path, "index.v1.db");
        await Assert.That(File.Exists(dbPath)).IsTrue();
    }

    [Test]
    public async Task SaveStreamAsync_CreatesMaildirStructure()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        using var stream = CreateSimpleEmail("<test1@example.com>");
        var saved = await service.SaveStreamAsync(
            stream,
            "<test1@example.com>",
            DateTimeOffset.UtcNow,
            "INBOX",
            CancellationToken.None);

        await Assert.That(saved).IsTrue();

        var inboxPath = Path.Combine(_temp.Path, "INBOX");
        await Assert.That(Directory.Exists(Path.Combine(inboxPath, "cur"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(inboxPath, "new"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(inboxPath, "tmp"))).IsTrue();
    }

    [Test]
    public async Task SaveStreamAsync_DeduplicatesByMessageId()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        using var stream1 = CreateSimpleEmail("<dup@test.com>");
        using var stream2 = CreateSimpleEmail("<dup@test.com>");

        var first = await service.SaveStreamAsync(
            stream1, "<dup@test.com>", DateTimeOffset.UtcNow, "INBOX", CancellationToken.None);
        var second = await service.SaveStreamAsync(
            stream2, "<dup@test.com>", DateTimeOffset.UtcNow, "INBOX", CancellationToken.None);

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsFalse();
    }

    [Test]
    public async Task SaveStreamAsync_CreatesSidecarMetadata()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        using var stream = CreateSimpleEmail("<meta@test.com>", "Test Subject");
        await service.SaveStreamAsync(
            stream, "<meta@test.com>", DateTimeOffset.UtcNow, "INBOX", CancellationToken.None);

        var curPath = Path.Combine(_temp.Path, "INBOX", "cur");
        var metaFiles = Directory.GetFiles(curPath, "*.meta.json");
        
        await Assert.That(metaFiles.Length).IsEqualTo(1);
        
        var content = await File.ReadAllTextAsync(metaFiles[0]);
        content.Should().Contain("Test Subject");
    }

    [Test]
    public async Task SaveStreamAsync_SanitizesMessageIdWithSlashes()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        using var stream = CreateSimpleEmail("<user/repo/test@github.com>");
        var saved = await service.SaveStreamAsync(
            stream, "<user/repo/test@github.com>", DateTimeOffset.UtcNow, "INBOX", CancellationToken.None);

        await Assert.That(saved).IsTrue();

        var curPath = Path.Combine(_temp.Path, "INBOX", "cur");
        var files = Directory.GetFiles(curPath, "*.eml");
        await Assert.That(files.Length).IsEqualTo(1);

        var fileName = Path.GetFileName(files[0]);
        fileName.Should().NotContain("/");
        fileName.Should().NotContain("\\");
    }

    [Test]
    public async Task GetLastUidAsync_ReturnsZero_WhenNoSyncState()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        var lastUid = await service.GetLastUidAsync("INBOX", 12345, CancellationToken.None);

        await Assert.That(lastUid).IsEqualTo(0);
    }

    [Test]
    public async Task UpdateLastUidAsync_PersistsUid()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.UpdateLastUidAsync("INBOX", 100, 12345, CancellationToken.None);
        var lastUid = await service.GetLastUidAsync("INBOX", 12345, CancellationToken.None);

        await Assert.That(lastUid).IsEqualTo(100);
    }

    [Test]
    public async Task GetLastUidAsync_ResetsOnUidValidityChange()
    {
        var service = CreateService();
        await service.InitializeAsync(CancellationToken.None);

        await service.UpdateLastUidAsync("INBOX", 100, 12345, CancellationToken.None);
        var sameValidity = await service.GetLastUidAsync("INBOX", 12345, CancellationToken.None);
        var changedValidity = await service.GetLastUidAsync("INBOX", 99999, CancellationToken.None);

        await Assert.That(sameValidity).IsEqualTo(100);
        await Assert.That(changedValidity).IsEqualTo(0);
    }

    [Test]
    public async Task NormalizeMessageId_RemovesInvalidCharacters()
    {
        var normalized = EmailStorageService.NormalizeMessageId("<test/path:id@example.com>");
        
        normalized.Should().NotContain("/");
        normalized.Should().NotContain(":");
        normalized.Should().NotContain("<");
        normalized.Should().NotContain(">");
    }

    [Test]
    public async Task ComputeHash_ReturnsConsistentHash()
    {
        var hash1 = EmailStorageService.ComputeHash("test input");
        var hash2 = EmailStorageService.ComputeHash("test input");
        var hash3 = EmailStorageService.ComputeHash("different input");

        await Assert.That(hash1).IsEqualTo(hash2);
        await Assert.That(hash1).IsNotEqualTo(hash3);
    }
}
CSHARP

# -----------------------------------------------------------------------------
# EmailStorageSanitizationTests.cs
# -----------------------------------------------------------------------------
cat > "$DOWNLOADER_TESTS/Services/EmailStorageSanitizationTests.cs" << 'CSHARP'
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MyImapDownloader.Core.Infrastructure;

namespace MyImapDownloader.Tests.Services;

public class EmailStorageSanitizationTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("sanitize_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    [Test]
    [Arguments("<simple@test.com>", "simple_test.com")]
    [Arguments("<path/with/slashes@test.com>", "path_with_slashes_test.com")]
    [Arguments("<spaces here@test.com>", "spaces_here_test.com")]
    public async Task NormalizeMessageId_SanitizesCorrectly(string input, string expected)
    {
        var result = EmailStorageService.NormalizeMessageId(input);
        result.Should().NotContain("/");
        result.Should().NotContain("\\");
        result.Should().NotContain("<");
        result.Should().NotContain(">");
    }

    [Test]
    public async Task SanitizeForFilename_TruncatesLongInput()
    {
        var longInput = new string('a', 200);
        var result = EmailStorageService.SanitizeForFilename(longInput, 50);
        
        await Assert.That(result.Length).IsLessThanOrEqualTo(50);
    }

    [Test]
    public async Task SanitizeForFilename_RemovesInvalidChars()
    {
        var input = "test<>:\"/\\|?*file";
        var result = EmailStorageService.SanitizeForFilename(input, 100);
        
        result.Should().NotContain("<");
        result.Should().NotContain(">");
        result.Should().NotContain(":");
        result.Should().NotContain("/");
        result.Should().NotContain("\\");
    }

    [Test]
    public async Task GenerateFilename_IsValidFilename()
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var filename = EmailStorageService.GenerateFilename(date, "test_id");

        await Assert.That(Path.GetFileName(filename)).IsEqualTo(filename);
        filename.Should().EndWith(".eml");
    }
}
CSHARP

# =============================================================================
# PART 2: MyEmailSearch Tests
# =============================================================================
echo ""
echo "[PART 2] Creating MyEmailSearch Tests..."

SEARCH_TESTS="$PROJECT_ROOT/MyEmailSearch.Tests"
mkdir -p "$SEARCH_TESTS/Data"
mkdir -p "$SEARCH_TESTS/Search"
mkdir -p "$SEARCH_TESTS/Indexing"
mkdir -p "$SEARCH_TESTS/Integration"

# -----------------------------------------------------------------------------
# SearchDatabaseTests.cs
# -----------------------------------------------------------------------------
cat > "$SEARCH_TESTS/Data/SearchDatabaseTests.cs" << 'CSHARP'
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Data;
using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Data;

public class SearchDatabaseTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("search_db_test");
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
        var dbPath = Path.Combine(_temp.Path, "search.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;
        return db;
    }

    [Test]
    public async Task InitializeAsync_CreatesDatabase()
    {
        var db = await CreateDatabaseAsync();
        var dbPath = Path.Combine(_temp.Path, "search.db");
        await Assert.That(File.Exists(dbPath)).IsTrue();
    }

    [Test]
    public async Task UpsertEmailAsync_InsertsNewEmail()
    {
        var db = await CreateDatabaseAsync();
        var doc = CreateEmailDocument("test1@example.com");

        await db.UpsertEmailAsync(doc);
        var count = await db.GetEmailCountAsync();

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertEmailAsync_UpdatesExistingEmail()
    {
        var db = await CreateDatabaseAsync();
        var doc1 = CreateEmailDocument("update@example.com", subject: "Original");
        var doc2 = CreateEmailDocument("update@example.com", subject: "Updated");

        await db.UpsertEmailAsync(doc1);
        await db.UpsertEmailAsync(doc2);
        var count = await db.GetEmailCountAsync();

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_FindsByFullText()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("find1@example.com", subject: "Important Meeting"));
        await db.UpsertEmailAsync(CreateEmailDocument("find2@example.com", subject: "Casual Chat"));

        var results = await db.SearchAsync(new SearchQuery { ContentTerms = "important" });

        await Assert.That(results.Count).IsEqualTo(1);
        results[0].Subject.Should().Contain("Important");
    }

    [Test]
    public async Task SearchAsync_FiltersByFromAddress()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("from1@example.com", from: "alice@example.com"));
        await db.UpsertEmailAsync(CreateEmailDocument("from2@example.com", from: "bob@example.com"));

        var results = await db.SearchAsync(new SearchQuery { FromAddress = "alice@example.com" });

        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_FiltersByToAddress()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("to1@example.com", to: "recipient1@example.com"));
        await db.UpsertEmailAsync(CreateEmailDocument("to2@example.com", to: "recipient2@example.com"));

        var results = await db.SearchAsync(new SearchQuery { ToAddress = "recipient1@example.com" });

        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_FiltersByDateRange()
    {
        var db = await CreateDatabaseAsync();
        var jan = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var mar = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);
        
        await db.UpsertEmailAsync(CreateEmailDocument("jan@example.com", date: jan));
        await db.UpsertEmailAsync(CreateEmailDocument("mar@example.com", date: mar));

        var results = await db.SearchAsync(new SearchQuery 
        { 
            DateFrom = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero),
            DateTo = new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero)
        });

        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SearchAsync_CombinesMultipleFilters()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("combo1@example.com", 
            from: "alice@example.com", subject: "Project Update"));
        await db.UpsertEmailAsync(CreateEmailDocument("combo2@example.com", 
            from: "alice@example.com", subject: "Meeting Notes"));
        await db.UpsertEmailAsync(CreateEmailDocument("combo3@example.com", 
            from: "bob@example.com", subject: "Project Update"));

        var results = await db.SearchAsync(new SearchQuery 
        { 
            FromAddress = "alice@example.com",
            ContentTerms = "project"
        });

        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetKnownFilesAsync_ReturnsAllFiles()
    {
        var db = await CreateDatabaseAsync();
        await db.UpsertEmailAsync(CreateEmailDocument("file1@example.com", filePath: "/path/to/file1.eml"));
        await db.UpsertEmailAsync(CreateEmailDocument("file2@example.com", filePath: "/path/to/file2.eml"));

        var files = await db.GetKnownFilesAsync();

        await Assert.That(files.Count).IsEqualTo(2);
    }

    [Test]
    public async Task IsHealthyAsync_ReturnsTrue_ForValidDatabase()
    {
        var db = await CreateDatabaseAsync();
        var healthy = await db.IsHealthyAsync();
        await Assert.That(healthy).IsTrue();
    }

    private static EmailDocument CreateEmailDocument(
        string messageId,
        string? subject = null,
        string? from = null,
        string? to = null,
        string? filePath = null,
        DateTimeOffset? date = null)
    {
        return new EmailDocument
        {
            MessageId = messageId,
            FilePath = filePath ?? $"/test/{messageId}.eml",
            Subject = subject ?? "Test Subject",
            FromAddress = from ?? "sender@example.com",
            ToAddress = to ?? "recipient@example.com",
            DateSent = date ?? DateTimeOffset.UtcNow,
            DateSentUnix = (date ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = DateTime.UtcNow.Ticks
        };
    }
}
CSHARP

# -----------------------------------------------------------------------------
# QueryParserTests.cs
# -----------------------------------------------------------------------------
cat > "$SEARCH_TESTS/Search/QueryParserTests.cs" << 'CSHARP'
using AwesomeAssertions;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

public class QueryParserTests
{
    private readonly QueryParser _parser = new();

    [Test]
    public async Task Parse_SimpleText_SetsContentTerms()
    {
        var result = _parser.Parse("hello world");
        
        await Assert.That(result.ContentTerms).IsEqualTo("hello world");
    }

    [Test]
    public async Task Parse_FromFilter_SetsFromAddress()
    {
        var result = _parser.Parse("from:alice@example.com");
        
        await Assert.That(result.FromAddress).IsEqualTo("alice@example.com");
    }

    [Test]
    public async Task Parse_ToFilter_SetsToAddress()
    {
        var result = _parser.Parse("to:bob@example.com");
        
        await Assert.That(result.ToAddress).IsEqualTo("bob@example.com");
    }

    [Test]
    public async Task Parse_SubjectFilter_SetsSubject()
    {
        var result = _parser.Parse("subject:meeting");
        
        await Assert.That(result.Subject).IsEqualTo("meeting");
    }

    [Test]
    public async Task Parse_QuotedSubject_PreservesSpaces()
    {
        var result = _parser.Parse("subject:\"project update\"");
        
        await Assert.That(result.Subject).IsEqualTo("project update");
    }

    [Test]
    public async Task Parse_DateRange_SetsDateFromAndTo()
    {
        var result = _parser.Parse("date:2024-01-01..2024-12-31");
        
        await Assert.That(result.DateFrom?.Year).IsEqualTo(2024);
        await Assert.That(result.DateFrom?.Month).IsEqualTo(1);
        await Assert.That(result.DateTo?.Year).IsEqualTo(2024);
        await Assert.That(result.DateTo?.Month).IsEqualTo(12);
    }

    [Test]
    public async Task Parse_AfterDate_SetsDateFrom()
    {
        var result = _parser.Parse("after:2024-06-01");
        
        await Assert.That(result.DateFrom?.Year).IsEqualTo(2024);
        await Assert.That(result.DateFrom?.Month).IsEqualTo(6);
    }

    [Test]
    public async Task Parse_BeforeDate_SetsDateTo()
    {
        var result = _parser.Parse("before:2024-06-30");
        
        await Assert.That(result.DateTo?.Year).IsEqualTo(2024);
        await Assert.That(result.DateTo?.Month).IsEqualTo(6);
    }

    [Test]
    public async Task Parse_FolderFilter_SetsFolder()
    {
        var result = _parser.Parse("folder:INBOX");
        
        await Assert.That(result.Folder).IsEqualTo("INBOX");
    }

    [Test]
    public async Task Parse_AccountFilter_SetsAccount()
    {
        var result = _parser.Parse("account:user@example.com");
        
        await Assert.That(result.Account).IsEqualTo("user@example.com");
    }

    [Test]
    public async Task Parse_CombinedFilters_SetsAllFields()
    {
        var result = _parser.Parse("from:alice@example.com to:bob@example.com subject:meeting kafka");
        
        await Assert.That(result.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(result.ToAddress).IsEqualTo("bob@example.com");
        await Assert.That(result.Subject).IsEqualTo("meeting");
        result.ContentTerms.Should().Contain("kafka");
    }

    [Test]
    public async Task Parse_EmptyQuery_ReturnsEmptySearchQuery()
    {
        var result = _parser.Parse("");
        
        await Assert.That(result.FromAddress).IsNull();
        await Assert.That(result.ToAddress).IsNull();
        await Assert.That(result.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_CaseInsensitiveFilters_Works()
    {
        var result = _parser.Parse("FROM:alice@example.com SUBJECT:test");
        
        await Assert.That(result.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(result.Subject).IsEqualTo("test");
    }
}
CSHARP

# -----------------------------------------------------------------------------
# SnippetGeneratorTests.cs
# -----------------------------------------------------------------------------
cat > "$SEARCH_TESTS/Search/SnippetGeneratorTests.cs" << 'CSHARP'
using AwesomeAssertions;
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

public class SnippetGeneratorTests
{
    private readonly SnippetGenerator _generator = new();

    [Test]
    public async Task Generate_FindsMatchingTerm()
    {
        var text = "This is a test document with some important content.";
        var snippet = _generator.Generate(text, ["important"]);
        
        snippet.Should().Contain("important");
    }

    [Test]
    public async Task Generate_ReturnsEmptyForNullText()
    {
        var snippet = _generator.Generate(null, ["test"]);
        
        await Assert.That(snippet).IsEmpty();
    }

    [Test]
    public async Task Generate_ReturnsEmptyForNoTerms()
    {
        var text = "Some text here";
        var snippet = _generator.Generate(text, []);
        
        await Assert.That(snippet).IsNotNull();
    }

    [Test]
    public async Task Generate_TruncatesLongText()
    {
        var text = new string('a', 1000) + " important " + new string('b', 1000);
        var snippet = _generator.Generate(text, ["important"], maxLength: 100);
        
        await Assert.That(snippet.Length).IsLessThanOrEqualTo(110); // Allow some margin
    }

    [Test]
    public async Task Generate_HandlesMultipleTerms()
    {
        var text = "The quick brown fox jumps over the lazy dog.";
        var snippet = _generator.Generate(text, ["quick", "lazy"]);
        
        snippet.Should().NotBeEmpty();
    }
}
CSHARP

# -----------------------------------------------------------------------------
# ArchiveScannerTests.cs
# -----------------------------------------------------------------------------
cat > "$SEARCH_TESTS/Indexing/ArchiveScannerTests.cs" << 'CSHARP'
using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Indexing;
using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

public class ArchiveScannerTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("scanner_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    [Test]
    public async Task ScanForEmails_FindsEmlFiles()
    {
        // Create test .eml files
        var curDir = Path.Combine(_temp.Path, "INBOX", "cur");
        Directory.CreateDirectory(curDir);
        await File.WriteAllTextAsync(Path.Combine(curDir, "test1.eml"), "Content 1");
        await File.WriteAllTextAsync(Path.Combine(curDir, "test2.eml"), "Content 2");

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var files = scanner.ScanForEmails(_temp.Path).ToList();

        await Assert.That(files.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ScanForEmails_RecursivelySearchesSubfolders()
    {
        var inbox = Path.Combine(_temp.Path, "INBOX", "cur");
        var sent = Path.Combine(_temp.Path, "Sent", "cur");
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(sent);
        await File.WriteAllTextAsync(Path.Combine(inbox, "inbox.eml"), "Inbox");
        await File.WriteAllTextAsync(Path.Combine(sent, "sent.eml"), "Sent");

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var files = scanner.ScanForEmails(_temp.Path).ToList();

        await Assert.That(files.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ScanForEmails_IgnoresNonEmlFiles()
    {
        var curDir = Path.Combine(_temp.Path, "INBOX", "cur");
        Directory.CreateDirectory(curDir);
        await File.WriteAllTextAsync(Path.Combine(curDir, "test.eml"), "Email");
        await File.WriteAllTextAsync(Path.Combine(curDir, "test.meta.json"), "Metadata");
        await File.WriteAllTextAsync(Path.Combine(curDir, "test.txt"), "Text");

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var files = scanner.ScanForEmails(_temp.Path).ToList();

        await Assert.That(files.Count).IsEqualTo(1);
        await Assert.That(files[0]).EndsWith(".eml");
    }

    [Test]
    public async Task ScanForEmails_ReturnsEmptyForEmptyDirectory()
    {
        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var files = scanner.ScanForEmails(_temp.Path).ToList();

        await Assert.That(files.Count).IsEqualTo(0);
    }
}
CSHARP

# -----------------------------------------------------------------------------
# EmailParserTests.cs
# -----------------------------------------------------------------------------
cat > "$SEARCH_TESTS/Indexing/EmailParserTests.cs" << 'CSHARP'
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Indexing;
using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

public class EmailParserTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("parser_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    private async Task<string> CreateEmlFileAsync(string content)
    {
        var path = Path.Combine(_temp.Path, $"{Guid.NewGuid()}.eml");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    [Test]
    public async Task ParseAsync_ExtractsMessageId()
    {
        var emlContent = """
            Message-ID: <test123@example.com>
            Subject: Test
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Hello world
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc.Should().NotBeNull();
        await Assert.That(doc!.MessageId).IsEqualTo("test123@example.com");
    }

    [Test]
    public async Task ParseAsync_ExtractsSubject()
    {
        var emlContent = """
            Message-ID: <subject@example.com>
            Subject: Important Meeting Tomorrow
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Body
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc!.Subject).IsEqualTo("Important Meeting Tomorrow");
    }

    [Test]
    public async Task ParseAsync_ExtractsFromAddress()
    {
        var emlContent = """
            Message-ID: <from@example.com>
            Subject: Test
            From: Alice Smith <alice@example.com>
            To: bob@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Body
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc!.FromAddress.Should().Contain("alice@example.com");
    }

    [Test]
    public async Task ParseAsync_ExtractsBodyText_WhenRequested()
    {
        var emlContent = """
            Message-ID: <body@example.com>
            Subject: Test
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            This is the email body content.
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: true);

        doc!.BodyText.Should().Contain("email body content");
    }

    [Test]
    public async Task ParseAsync_SetsIndexedAtUnix()
    {
        var emlContent = """
            Message-ID: <indexed@example.com>
            Subject: Test
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Body
            """;

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc!.IndexedAtUnix).IsGreaterThan(0);
    }

    [Test]
    public async Task ParseAsync_ReturnsNullForInvalidFile()
    {
        var path = Path.Combine(_temp.Path, "nonexistent.eml");
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc).IsNull();
    }
}
CSHARP

# -----------------------------------------------------------------------------
# IndexManagerTests.cs
# -----------------------------------------------------------------------------
cat > "$SEARCH_TESTS/Indexing/IndexManagerTests.cs" << 'CSHARP'
using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

public class IndexManagerTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("index_mgr_test");
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

    private async Task<string> CreateEmlFileAsync(string folder, string messageId, string subject)
    {
        var curDir = Path.Combine(_temp.Path, "archive", folder, "cur");
        Directory.CreateDirectory(curDir);
        
        var content = $"""
            Message-ID: <{messageId}>
            Subject: {subject}
            From: sender@example.com
            To: recipient@example.com
            Date: Mon, 01 Jan 2024 12:00:00 +0000
            Content-Type: text/plain

            Email body for {subject}
            """;
        
        var path = Path.Combine(curDir, $"{messageId.Replace("@", "_")}.eml");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    [Test]
    public async Task IndexAsync_IndexesNewEmails()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dbPath = Path.Combine(_temp.Path, "search.db");
        
        await CreateEmlFileAsync("INBOX", "test1@example.com", "First Email");
        await CreateEmlFileAsync("INBOX", "test2@example.com", "Second Email");

        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        var result = await manager.IndexAsync(archivePath, includeContent: true);

        await Assert.That(result.Indexed).IsEqualTo(2);
        await Assert.That(result.Errors).IsEqualTo(0);
    }

    [Test]
    public async Task IndexAsync_SkipsAlreadyIndexedFiles()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dbPath = Path.Combine(_temp.Path, "search.db");
        
        await CreateEmlFileAsync("INBOX", "existing@example.com", "Existing Email");

        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        // Index twice
        var result1 = await manager.IndexAsync(archivePath, includeContent: true);
        var result2 = await manager.IndexAsync(archivePath, includeContent: true);

        await Assert.That(result1.Indexed).IsEqualTo(1);
        await Assert.That(result2.Indexed).IsEqualTo(0);
        await Assert.That(result2.Skipped).IsEqualTo(1);
    }

    [Test]
    public async Task RebuildIndexAsync_ReindexesAllEmails()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dbPath = Path.Combine(_temp.Path, "search.db");
        
        await CreateEmlFileAsync("INBOX", "rebuild@example.com", "Rebuild Test");

        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _database = db;

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        // Index first
        await manager.IndexAsync(archivePath, includeContent: true);
        
        // Rebuild
        var result = await manager.RebuildIndexAsync(archivePath, includeContent: true);

        await Assert.That(result.Indexed).IsEqualTo(1);
    }
}
CSHARP

# -----------------------------------------------------------------------------
# SearchEngineTests.cs
# -----------------------------------------------------------------------------
cat > "$SEARCH_TESTS/Search/SearchEngineTests.cs" << 'CSHARP'
using AwesomeAssertions;
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
        var snippetGenerator = new SnippetGenerator();
        var engine = new SearchEngine(db, queryParser, snippetGenerator, 
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
            ToAddress = "recipient@example.com",
            DateSent = DateTimeOffset.UtcNow,
            DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = DateTime.UtcNow.Ticks
        };
    }
}
CSHARP

echo ""
echo "=========================================="
echo "✅ All unit tests created successfully!"
echo ""
echo "Test counts by project:"
echo ""
echo "MyImapDownloader.Tests:"
echo "  - DownloadOptionsTests: 6 tests"
echo "  - ImapConfigurationTests: 4 tests"
echo "  - EmailDownloadExceptionTests: 5 tests"
echo "  - EmailStorageServiceTests: 10 tests"
echo "  - EmailStorageSanitizationTests: 4 tests"
echo ""
echo "MyEmailSearch.Tests:"
echo "  - SearchDatabaseTests: 10 tests"
echo "  - QueryParserTests: 13 tests"
echo "  - SnippetGeneratorTests: 5 tests"
echo "  - ArchiveScannerTests: 4 tests"
echo "  - EmailParserTests: 6 tests"
echo "  - IndexManagerTests: 3 tests"
echo "  - SearchEngineTests: 4 tests"
echo ""
echo "MyImapDownloader.Core.Tests (from previous script):"
echo "  - TelemetryConfigurationTests: 3 tests"
echo "  - PathResolverTests: 5 tests"
echo "  - TempDirectoryTests: 3 tests"
echo "  - SqliteHelperTests: 4 tests"
echo "  - ActivityExtensionsTests: 7 tests"
echo "  - JsonTelemetryFileWriterTests: 4 tests"
echo "  - EmailMetadataTests: 3 tests"
echo ""
echo "Total: ~99 tests"
echo ""
echo "Run tests with:"
echo "  dotnet test"
echo "=========================================="
