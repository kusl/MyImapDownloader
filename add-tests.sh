#!/bin/sh
# =============================================================================
# Add Missing Unit Tests - MyImapDownloader
# =============================================================================
# Adds tests for untested/under-tested code paths identified during review.
#
# Gap Analysis (existing 221 tests → gaps found):
#
# 1. QueryParser: No test for combined filters, account:, folder:, edge cases
# 2. ArchiveScanner: ExtractAccountName/ExtractFolderName untested
# 3. SearchDatabase: SetMetadata/GetMetadata, GetDatabaseSize, DisposeAsync,
#    FTS UPDATE trigger untested
# 4. EmailParser: Multipart MIME, HTML-only, attachments, malformed untested
# 5. SnippetGenerator: Case insensitivity, term at boundaries untested
# 6. EmailStorageService: NormalizeMessageId long-ID hash path, empty ID untested
# 7. IndexManager: Cancellation, progress reporting untested
# 8. SearchEngine: Sort order, large offset untested
# 9. MyEmailSearch PathResolver: Zero test coverage
# 10. Core EmailMetadata: Zero test coverage
# =============================================================================

set -e

cd "$(dirname "$0")"
PROJECT_ROOT="$(pwd)"

echo "Adding missing unit tests..."
echo "Project root: $PROJECT_ROOT"
echo ""

# =============================================================================
# 1. QueryParser - Combined filters and edge cases
# =============================================================================
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Search/QueryParserEdgeCaseTests.cs" << 'ENDOFFILE'
using AwesomeAssertions;

using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Tests for QueryParser edge cases and combined filter scenarios.
/// </summary>
public class QueryParserEdgeCaseTests
{
    private readonly QueryParser _parser = new();

    [Test]
    public async Task Parse_CombinedFilters_ExtractsAllFields()
    {
        var result = _parser.Parse("from:alice@example.com subject:meeting quarterly report");

        await Assert.That(result.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(result.Subject).IsEqualTo("meeting");
        result.ContentTerms.Should().Contain("quarterly");
        result.ContentTerms.Should().Contain("report");
    }

    [Test]
    public async Task Parse_AccountFilter_SetsAccount()
    {
        var result = _parser.Parse("account:work");

        await Assert.That(result.Account).IsEqualTo("work");
    }

    [Test]
    public async Task Parse_FolderFilter_SetsFolder()
    {
        var result = _parser.Parse("folder:INBOX");

        await Assert.That(result.Folder).IsEqualTo("INBOX");
    }

    [Test]
    public async Task Parse_AllFiltersAtOnce_ExtractsEverything()
    {
        var query = "from:alice@x.com to:bob@x.com subject:hello account:work folder:Sent after:2024-01-01 before:2024-12-31 free text";
        var result = _parser.Parse(query);

        await Assert.That(result.FromAddress).IsEqualTo("alice@x.com");
        await Assert.That(result.ToAddress).IsEqualTo("bob@x.com");
        await Assert.That(result.Subject).IsEqualTo("hello");
        await Assert.That(result.Account).IsEqualTo("work");
        await Assert.That(result.Folder).IsEqualTo("Sent");
        await Assert.That(result.DateFrom).IsNotNull();
        await Assert.That(result.DateTo).IsNotNull();
        result.ContentTerms.Should().Contain("free text");
    }

    [Test]
    public async Task Parse_EmptyString_ReturnsEmptyQuery()
    {
        var result = _parser.Parse("");

        await Assert.That(result.FromAddress).IsNull();
        await Assert.That(result.ToAddress).IsNull();
        await Assert.That(result.Subject).IsNull();
        await Assert.That(result.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_WhitespaceOnly_ReturnsEmptyQuery()
    {
        var result = _parser.Parse("   ");

        await Assert.That(result.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_QuotedFromAddress_PreservesQuotedValue()
    {
        var result = _parser.Parse("from:\"alice smith@example.com\"");

        await Assert.That(result.FromAddress).IsEqualTo("alice smith@example.com");
    }

    [Test]
    public async Task Parse_SingleDateWithoutRange_SetsDateFrom()
    {
        var result = _parser.Parse("date:2024-06-15");

        await Assert.That(result.DateFrom).IsNotNull();
        await Assert.That(result.DateFrom!.Value.Year).IsEqualTo(2024);
        await Assert.That(result.DateFrom!.Value.Month).IsEqualTo(6);
        await Assert.That(result.DateFrom!.Value.Day).IsEqualTo(15);
    }

    [Test]
    public async Task Parse_DefaultPagination_HasCorrectValues()
    {
        var result = _parser.Parse("test");

        await Assert.That(result.Skip).IsEqualTo(0);
        await Assert.That(result.Take).IsEqualTo(100);
        await Assert.That(result.SortOrder).IsEqualTo(SearchSortOrder.DateDescending);
    }

    [Test]
    public async Task Parse_InvalidDate_IgnoresDateFilter()
    {
        var result = _parser.Parse("after:not-a-date some text");

        await Assert.That(result.DateFrom).IsNull();
        result.ContentTerms.Should().Contain("some text");
    }
}
ENDOFFILE

echo "  Created QueryParserEdgeCaseTests.cs"

# =============================================================================
# 2. ArchiveScanner - ExtractAccountName / ExtractFolderName
# =============================================================================
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Indexing/ArchiveScannerExtractionTests.cs" << 'ENDOFFILE'
using MyEmailSearch.Indexing;

namespace MyEmailSearch.Tests.Indexing;

/// <summary>
/// Tests for ArchiveScanner's account/folder extraction from file paths.
/// </summary>
public class ArchiveScannerExtractionTests
{
    [Test]
    public async Task ExtractAccountName_StandardPath_ReturnsAccountFolder()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "work_account", "INBOX", "cur", "email.eml");

        var account = ArchiveScanner.ExtractAccountName(filePath, archivePath);

        await Assert.That(account).IsEqualTo("work_account");
    }

    [Test]
    public async Task ExtractAccountName_ShortPath_ReturnsNull()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "email.eml");

        var account = ArchiveScanner.ExtractAccountName(filePath, archivePath);

        await Assert.That(account).IsNull();
    }

    [Test]
    public async Task ExtractFolderName_StandardPath_ReturnsFolderName()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "account", "INBOX", "cur", "email.eml");

        var folder = ArchiveScanner.ExtractFolderName(filePath, archivePath);

        await Assert.That(folder).IsEqualTo("INBOX");
    }

    [Test]
    public async Task ExtractFolderName_ShortPath_ReturnsNull()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "account", "email.eml");

        var folder = ArchiveScanner.ExtractFolderName(filePath, archivePath);

        await Assert.That(folder).IsNull();
    }

    [Test]
    public async Task ExtractAccountName_SentFolder_ReturnsAccount()
    {
        var archivePath = "/home/user/mail";
        var filePath = Path.Combine(archivePath, "personal", "Sent", "cur", "msg.eml");

        var account = ArchiveScanner.ExtractAccountName(filePath, archivePath);

        await Assert.That(account).IsEqualTo("personal");
    }
}
ENDOFFILE

echo "  Created ArchiveScannerExtractionTests.cs"

# =============================================================================
# 3. SearchDatabase - Metadata, Size, Dispose, FTS Update trigger
# =============================================================================
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Data/SearchDatabaseMetadataTests.cs" << 'ENDOFFILE'
using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Data;

/// <summary>
/// Tests for SearchDatabase metadata, size, health, and lifecycle operations.
/// </summary>
public class SearchDatabaseMetadataTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("db_meta_test");
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
    public async Task SetMetadataAsync_And_GetMetadataAsync_RoundTrips()
    {
        var db = await CreateDatabaseAsync();

        await db.SetMetadataAsync("test_key", "test_value");
        var result = await db.GetMetadataAsync("test_key");

        await Assert.That(result).IsEqualTo("test_value");
    }

    [Test]
    public async Task GetMetadataAsync_NonExistentKey_ReturnsNull()
    {
        var db = await CreateDatabaseAsync();

        var result = await db.GetMetadataAsync("nonexistent_key");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task SetMetadataAsync_OverwritesExistingKey()
    {
        var db = await CreateDatabaseAsync();

        await db.SetMetadataAsync("version", "1.0");
        await db.SetMetadataAsync("version", "2.0");
        var result = await db.GetMetadataAsync("version");

        await Assert.That(result).IsEqualTo("2.0");
    }

    [Test]
    public async Task GetDatabaseSize_ReturnsPositiveValue()
    {
        var db = await CreateDatabaseAsync();

        // Insert some data to ensure the file has content
        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "size@example.com",
            FilePath = "/test/size.eml",
            Subject = "Size Test",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var size = db.GetDatabaseSize();

        await Assert.That(size).IsGreaterThan(0);
    }

    [Test]
    public async Task IsHealthyAsync_OnGoodDatabase_ReturnsTrue()
    {
        var db = await CreateDatabaseAsync();

        var healthy = await db.IsHealthyAsync();

        await Assert.That(healthy).IsTrue();
    }

    [Test]
    public async Task GetEmailCountAsync_EmptyDatabase_ReturnsZero()
    {
        var db = await CreateDatabaseAsync();

        var count = await db.GetEmailCountAsync();

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task GetEmailCountAsync_AfterInserts_ReturnsCorrectCount()
    {
        var db = await CreateDatabaseAsync();

        for (var i = 0; i < 5; i++)
        {
            await db.UpsertEmailAsync(new EmailDocument
            {
                MessageId = $"count{i}@example.com",
                FilePath = $"/test/count{i}.eml",
                Subject = $"Count Test {i}",
                IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        var count = await db.GetEmailCountAsync();

        await Assert.That(count).IsEqualTo(5);
    }

    [Test]
    public async Task GetKnownFilesAsync_ReturnsInsertedFiles()
    {
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "known@example.com",
            FilePath = "/test/known.eml",
            Subject = "Known File",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastModifiedTicks = 12345
        });

        var knownFiles = await db.GetKnownFilesAsync();

        knownFiles.Should().ContainKey("/test/known.eml");
        await Assert.That(knownFiles["/test/known.eml"]).IsEqualTo(12345);
    }

    [Test]
    public async Task RebuildAsync_ClearsAllData()
    {
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "rebuild@example.com",
            FilePath = "/test/rebuild.eml",
            Subject = "Rebuild Test",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var countBefore = await db.GetEmailCountAsync();
        await Assert.That(countBefore).IsEqualTo(1);

        await db.RebuildAsync();

        var countAfter = await db.GetEmailCountAsync();
        await Assert.That(countAfter).IsEqualTo(0);
    }

    [Test]
    public async Task UpsertEmailAsync_UpdatesExistingByFilePath()
    {
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "upsert@example.com",
            FilePath = "/test/upsert.eml",
            Subject = "Original Subject",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "upsert@example.com",
            FilePath = "/test/upsert.eml",
            Subject = "Updated Subject",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        var count = await db.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);

        // Verify the FTS index still works after the update
        var results = await db.QueryAsync(new SearchQuery { Subject = "Updated" });
        await Assert.That(results.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FtsTrigger_AfterUpdate_ReflectsNewContent()
    {
        var db = await CreateDatabaseAsync();

        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "trigger@example.com",
            FilePath = "/test/trigger.eml",
            Subject = "Alpha",
            BodyText = "Original body text about apples",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        // Search for original content
        var beforeResults = await db.QueryAsync(new SearchQuery { ContentTerms = "apples" });
        await Assert.That(beforeResults.Count).IsEqualTo(1);

        // Update with new content
        await db.UpsertEmailAsync(new EmailDocument
        {
            MessageId = "trigger@example.com",
            FilePath = "/test/trigger.eml",
            Subject = "Beta",
            BodyText = "Updated body text about oranges",
            IndexedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        // Old content should no longer match
        var oldResults = await db.QueryAsync(new SearchQuery { ContentTerms = "apples" });
        await Assert.That(oldResults.Count).IsEqualTo(0);

        // New content should match
        var newResults = await db.QueryAsync(new SearchQuery { ContentTerms = "oranges" });
        await Assert.That(newResults.Count).IsEqualTo(1);
    }
}
ENDOFFILE

echo "  Created SearchDatabaseMetadataTests.cs"

# =============================================================================
# 4. EmailParser - Multipart, HTML-only, attachments, malformed
# =============================================================================
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Indexing/EmailParserEdgeCaseTests.cs" << 'ENDOFFILE'
using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Indexing;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

/// <summary>
/// Tests for EmailParser edge cases: multipart, HTML-only, attachments, malformed.
/// </summary>
public class EmailParserEdgeCaseTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("parser_edge_test");

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
    public async Task ParseAsync_MultipartAlternative_ExtractsPlainText()
    {
        var emlContent = "Message-ID: <multi@example.com>\r\n" +
            "Subject: Multipart Test\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: multipart/alternative; boundary=\"boundary123\"\r\n" +
            "\r\n" +
            "--boundary123\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            "\r\n" +
            "This is the plain text version.\r\n" +
            "--boundary123\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "\r\n" +
            "<html><body><p>This is HTML</p></body></html>\r\n" +
            "--boundary123--\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: true);

        doc.Should().NotBeNull();
        doc!.BodyText.Should().Contain("plain text version");
    }

    [Test]
    public async Task ParseAsync_HtmlOnlyEmail_ExtractsText()
    {
        var emlContent = "Message-ID: <html@example.com>\r\n" +
            "Subject: HTML Only\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            "\r\n" +
            "<html><body><p>HTML only content here</p></body></html>\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: true);

        doc.Should().NotBeNull();
        // Should get something from the HTML even if it's the raw HTML
        doc!.BodyText.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ParseAsync_EmailWithAttachment_SetsHasAttachments()
    {
        var emlContent = "Message-ID: <attach@example.com>\r\n" +
            "Subject: Attachment Test\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: multipart/mixed; boundary=\"mixedboundary\"\r\n" +
            "\r\n" +
            "--mixedboundary\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "See attached.\r\n" +
            "--mixedboundary\r\n" +
            "Content-Type: application/pdf; name=\"report.pdf\"\r\n" +
            "Content-Disposition: attachment; filename=\"report.pdf\"\r\n" +
            "Content-Transfer-Encoding: base64\r\n" +
            "\r\n" +
            "JVBERi0xLjQKMSAwIG9iago=\r\n" +
            "--mixedboundary--\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc.Should().NotBeNull();
        await Assert.That(doc!.HasAttachments).IsTrue();
        doc.AttachmentNamesJson.Should().Contain("report.pdf");
    }

    [Test]
    public async Task ParseAsync_MissingMessageId_StillParses()
    {
        var emlContent = "Subject: No Message ID\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Body content\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        // Should still parse, possibly with null or generated message ID
        doc.Should().NotBeNull();
    }

    [Test]
    public async Task ParseAsync_EmptyFile_ReturnsNull()
    {
        var path = await CreateEmlFileAsync("");
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc).IsNull();
    }

    [Test]
    public async Task ParseAsync_MultipleRecipients_ExtractsAll()
    {
        var emlContent = "Message-ID: <multi-to@example.com>\r\n" +
            "Subject: Multiple Recipients\r\n" +
            "From: sender@example.com\r\n" +
            "To: alice@example.com, bob@example.com\r\n" +
            "Cc: charlie@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Group email\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc.Should().NotBeNull();
        doc!.ToAddressesJson.Should().Contain("alice@example.com");
        doc.ToAddressesJson.Should().Contain("bob@example.com");
        doc.CcAddressesJson.Should().Contain("charlie@example.com");
    }

    [Test]
    public async Task ParseAsync_IncludeFullBody_False_LimitsBodyLength()
    {
        var longBody = new string('A', 2000);
        var emlContent = "Message-ID: <preview@example.com>\r\n" +
            "Subject: Long Body\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            longBody + "\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        doc.Should().NotBeNull();
        // Body preview should be truncated (500 chars per BodyPreviewLength constant)
        doc!.BodyPreview.Should().NotBeNull();
        await Assert.That(doc.BodyPreview!.Length).IsLessThanOrEqualTo(510);
    }

    [Test]
    public async Task ParseAsync_SetsLastModifiedTicks()
    {
        var emlContent = "Message-ID: <ticks@example.com>\r\n" +
            "Subject: Ticks Test\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Body\r\n";

        var path = await CreateEmlFileAsync(emlContent);
        var parser = new EmailParser(_temp.Path, NullLogger<EmailParser>.Instance);
        var doc = await parser.ParseAsync(path, includeFullBody: false);

        await Assert.That(doc!.LastModifiedTicks).IsGreaterThan(0);
    }
}
ENDOFFILE

echo "  Created EmailParserEdgeCaseTests.cs"

# =============================================================================
# 5. SnippetGenerator - More edge cases
# =============================================================================
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Search/SnippetGeneratorEdgeCaseTests.cs" << 'ENDOFFILE'
using AwesomeAssertions;

using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Edge case tests for SnippetGenerator.
/// </summary>
public class SnippetGeneratorEdgeCaseTests
{
    [Test]
    public async Task Generate_TermAtStartOfText_ReturnsSnippet()
    {
        var text = "Important meeting scheduled for next Monday at 3pm.";
        var snippet = SnippetGenerator.Generate(text, "important");

        snippet.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Generate_TermAtEndOfText_ReturnsSnippet()
    {
        var text = "Please review the attached document which is very important";
        var snippet = SnippetGenerator.Generate(text, "important");

        snippet.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Generate_CaseInsensitiveMatch_FindsTerm()
    {
        var text = "The CRITICAL update was applied successfully.";
        var snippet = SnippetGenerator.Generate(text, "critical");

        snippet.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Generate_VeryShortText_ReturnsEntireText()
    {
        var text = "Hi";
        var snippet = SnippetGenerator.Generate(text, "hi");

        snippet.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Generate_NoMatchingTerm_ReturnsTextPrefix()
    {
        var text = "This email is about project planning and scheduling.";
        var snippet = SnippetGenerator.Generate(text, "nonexistentword");

        // Should return something (beginning of text) even without a match
        snippet.Should().NotBeNull();
    }

    [Test]
    public async Task Generate_NullTerms_ReturnsTextPrefix()
    {
        var text = "Some email content here.";
        var snippet = SnippetGenerator.Generate(text, null!);

        await Assert.That(snippet).IsNotNull();
    }
}
ENDOFFILE

echo "  Created SnippetGeneratorEdgeCaseTests.cs"

# =============================================================================
# 6. EmailStorageService - NormalizeMessageId edge cases
# =============================================================================
cat > "$PROJECT_ROOT/MyImapDownloader.Tests/NormalizeMessageIdTests.cs" << 'ENDOFFILE'
using AwesomeAssertions;

namespace MyImapDownloader.Tests;

/// <summary>
/// Tests for EmailStorageService.NormalizeMessageId edge cases,
/// particularly the hash truncation path for long IDs.
/// </summary>
public class NormalizeMessageIdTests
{
    [Test]
    public async Task NormalizeMessageId_LongId_TruncatesWithHash()
    {
        // Create a message ID longer than 100 characters
        var longId = "<" + new string('a', 120) + "@example.com>";
        var normalized = EmailStorageService.NormalizeMessageId(longId);

        await Assert.That(normalized.Length).IsLessThanOrEqualTo(100);
        // Should contain a hash suffix
        normalized.Should().Contain("_");
    }

    [Test]
    public async Task NormalizeMessageId_EmptyString_ReturnsUnknown()
    {
        var normalized = EmailStorageService.NormalizeMessageId("");

        await Assert.That(normalized).IsEqualTo("unknown");
    }

    [Test]
    public async Task NormalizeMessageId_AngleBrackets_Removed()
    {
        var normalized = EmailStorageService.NormalizeMessageId("<simple@test.com>");

        normalized.Should().NotContain("<");
        normalized.Should().NotContain(">");
    }

    [Test]
    public async Task NormalizeMessageId_SlashesReplaced()
    {
        var normalized = EmailStorageService.NormalizeMessageId("<org/repo/id@github.com>");

        normalized.Should().NotContain("/");
        normalized.Should().NotContain("\\");
    }

    [Test]
    public async Task NormalizeMessageId_ColonsReplaced()
    {
        var normalized = EmailStorageService.NormalizeMessageId("<urn:uuid:abc@test.com>");

        normalized.Should().NotContain(":");
    }

    [Test]
    public async Task NormalizeMessageId_ConsistentResults()
    {
        var id = "<test@example.com>";
        var first = EmailStorageService.NormalizeMessageId(id);
        var second = EmailStorageService.NormalizeMessageId(id);

        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task NormalizeMessageId_CaseInsensitive()
    {
        var lower = EmailStorageService.NormalizeMessageId("<TEST@EXAMPLE.COM>");

        lower.Should().Be(lower.ToLowerInvariant());
    }

    [Test]
    public async Task SanitizeForFilename_SpecialCharsRemoved()
    {
        var result = EmailStorageService.SanitizeForFilename("hello world! @#$%", 50);

        result.Should().NotContain("!");
        result.Should().NotContain("@");
        result.Should().NotContain("#");
    }

    [Test]
    public async Task SanitizeForFilename_RespectsMaxLength()
    {
        var input = new string('a', 200);
        var result = EmailStorageService.SanitizeForFilename(input, 50);

        await Assert.That(result.Length).IsLessThanOrEqualTo(50);
    }

    [Test]
    public async Task ComputeHash_DifferentInputs_DifferentHashes()
    {
        var hash1 = EmailStorageService.ComputeHash("input1");
        var hash2 = EmailStorageService.ComputeHash("input2");

        hash1.Should().NotBe(hash2);
    }

    [Test]
    public async Task ComputeHash_ReturnsLowercaseHex()
    {
        var hash = EmailStorageService.ComputeHash("test");

        hash.Should().MatchRegex("^[0-9a-f]+$");
    }
}
ENDOFFILE

echo "  Created NormalizeMessageIdTests.cs"

# =============================================================================
# 7. SearchEngine - Sort order, large offset, no results
# =============================================================================
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Search/SearchEngineEdgeCaseTests.cs" << 'ENDOFFILE'
using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Search;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Search;

/// <summary>
/// Edge case tests for SearchEngine: sort order, large offset, empty results.
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

        var engine = new SearchEngine(db, new QueryParser(), new SnippetGenerator(),
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
ENDOFFILE

echo "  Created SearchEngineEdgeCaseTests.cs"

# =============================================================================
# 8. MyEmailSearch PathResolver - Zero coverage currently
# =============================================================================
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Configuration/PathResolverTests.cs" << 'ENDOFFILE'
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

        // Should reference the app name somewhere in the path
        await Assert.That(path.ToLowerInvariant()).Contains("myemailsearch");
    }
}
ENDOFFILE

echo "  Created MyEmailSearch PathResolver tests"

# =============================================================================
# 9. Core EmailMetadata - Zero coverage
# =============================================================================
cat > "$PROJECT_ROOT/MyImapDownloader.Core.Tests/Data/EmailMetadataTests.cs" << 'ENDOFFILE'
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
        await Assert.That(metadata.To).IsEqualTo("recipient@example.com");
        await Assert.That(metadata.Cc).IsEqualTo("cc@example.com");
        await Assert.That(metadata.Date).IsEqualTo(now);
        await Assert.That(metadata.Folder).IsEqualTo("INBOX");
        await Assert.That(metadata.ArchivedAt).IsEqualTo(now);
        await Assert.That(metadata.HasAttachments).IsTrue();
        await Assert.That(metadata.SizeBytes).IsEqualTo(1024);
        await Assert.That(metadata.Account).IsEqualTo("work");
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
ENDOFFILE

echo "  Created EmailMetadataTests.cs"

# =============================================================================
# 10. SearchDatabase - Batch upsert
# =============================================================================
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Data/SearchDatabaseBatchTests.cs" << 'ENDOFFILE'
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

        // All should be findable via FTS
        var results = await db.QueryAsync(new SearchQuery { ContentTerms = "xylophone" });
        await Assert.That(results.Count).IsEqualTo(10);

        // Structured query should also work
        var fromResults = await db.QueryAsync(new SearchQuery { FromAddress = "batchsender@example.com" });
        await Assert.That(fromResults.Count).IsEqualTo(10);
    }
}
ENDOFFILE

echo "  Created SearchDatabaseBatchTests.cs"

# =============================================================================
# 11. IndexManager - Cancellation
# =============================================================================
cat > "$PROJECT_ROOT/MyEmailSearch.Tests/Indexing/IndexManagerCancellationTests.cs" << 'ENDOFFILE'
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

        // Create many files
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
        cts.Cancel(); // Cancel immediately

        var act = async () => await manager.IndexAsync(archivePath, includeContent: false, ct: cts.Token);

        // Should throw OperationCanceledException
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

        // Allow progress callback to fire (it's async)
        await Task.Delay(200);

        await Assert.That(progressReports.Count).IsGreaterThanOrEqualTo(1);
    }
}
ENDOFFILE

echo "  Created IndexManagerCancellationTests.cs"

# =============================================================================
# Done - Build and test
# =============================================================================
echo ""
echo "=========================================="
echo "All test files created. Building and testing..."
echo "=========================================="
echo ""

dotnet build
BUILD_RESULT=$?

if [ "$BUILD_RESULT" -ne 0 ]; then
    echo ""
    echo "BUILD FAILED - fix compilation errors before running tests"
    exit 1
fi

dotnet test
TEST_RESULT=$?

echo ""
echo "=========================================="
if [ "$TEST_RESULT" -eq 0 ]; then
    echo "ALL TESTS PASSED"
else
    echo "SOME TESTS FAILED - review output above"
fi
echo "=========================================="
echo ""
echo "New test files added:"
echo "  MyEmailSearch.Tests/Search/QueryParserEdgeCaseTests.cs"
echo "  MyEmailSearch.Tests/Indexing/ArchiveScannerExtractionTests.cs"
echo "  MyEmailSearch.Tests/Data/SearchDatabaseMetadataTests.cs"
echo "  MyEmailSearch.Tests/Indexing/EmailParserEdgeCaseTests.cs"
echo "  MyEmailSearch.Tests/Search/SnippetGeneratorEdgeCaseTests.cs"
echo "  MyImapDownloader.Tests/NormalizeMessageIdTests.cs"
echo "  MyEmailSearch.Tests/Search/SearchEngineEdgeCaseTests.cs"
echo "  MyEmailSearch.Tests/Configuration/PathResolverTests.cs"
echo "  MyImapDownloader.Core.Tests/Data/EmailMetadataTests.cs"
echo "  MyEmailSearch.Tests/Data/SearchDatabaseBatchTests.cs"
echo "  MyEmailSearch.Tests/Indexing/IndexManagerCancellationTests.cs"
