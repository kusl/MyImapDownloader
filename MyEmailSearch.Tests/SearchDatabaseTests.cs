using Microsoft.Data.Sqlite;

using MyEmailSearch.Data;

namespace MyEmailSearch.Tests;

public class SearchDatabaseTests : IAsyncDisposable
{
    private const string LegacySchema = """
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

        INSERT INTO emails (message_id, file_path, from_address, subject, body_text, indexed_at_unix, last_modified_ticks)
        VALUES ('legacy@example.com', '/mail/work/INBOX/cur/legacy.eml', 'Alice@Example.com', 'Legacy invoice', 'kafka migration notes', 1700000000, 638000000000000000);
        INSERT INTO index_metadata (key, value) VALUES ('last_indexed_time', '1700000000');
        """;

    private readonly TempDirectory _temp = new();
    private SearchDatabase? _database;

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }

        _temp.Dispose();
    }

    private string DatabasePath => Path.Combine(_temp.Path, "nested", "search.db");

    private async Task<SearchDatabase> OpenAsync() => _database = await SearchDatabase.OpenAsync(DatabasePath);

    [Test]
    public async Task OpenAsync_CreatesDatabaseAndParentDirectory()
    {
        var database = await OpenAsync();

        await Assert.That(File.Exists(DatabasePath)).IsTrue();
        await Assert.That(database.SizeBytes).IsGreaterThan(0L);
        await Assert.That(await database.IsHealthyAsync()).IsTrue();
        await Assert.That(await database.GetEmailCountAsync()).IsEqualTo(0L);
    }

    [Test]
    public async Task UpsertAsync_InsertsAndRoundTripsAllFields()
    {
        var database = await OpenAsync();
        var document = new EmailDocument
        {
            MessageId = "full@example.com",
            FilePath = "/mail/a.eml",
            FromAddress = "alice@example.com",
            FromName = "Alice",
            ToAddressesJson = "[\"bob@example.com\"]",
            CcAddressesJson = "[\"carol@example.com\"]",
            BccAddressesJson = "[\"dave@example.com\"]",
            Subject = "Hello",
            DateSentUnix = 1_700_000_000,
            DateReceivedUnix = 1_700_000_001,
            Folder = "INBOX",
            Account = "work",
            HasAttachments = true,
            AttachmentNamesJson = "[\"report.pdf\"]",
            BodyPreview = "preview",
            BodyText = "full body",
            IndexedAtUnix = 1_700_000_100,
            LastModifiedTicks = 42
        };

        await database.UpsertAsync([document]);
        var stored = (await database.QueryAsync(new SearchQuery())).Single();

        await Assert.That(stored.Id).IsGreaterThan(0L);
        await Assert.That(stored.MessageId).IsEqualTo("full@example.com");
        await Assert.That(stored.FilePath).IsEqualTo("/mail/a.eml");
        await Assert.That(stored.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(stored.FromName).IsEqualTo("Alice");
        await Assert.That(stored.ToAddresses[0]).IsEqualTo("bob@example.com");
        await Assert.That(stored.CcAddresses[0]).IsEqualTo("carol@example.com");
        await Assert.That(stored.BccAddresses[0]).IsEqualTo("dave@example.com");
        await Assert.That(stored.Subject).IsEqualTo("Hello");
        await Assert.That(stored.DateSentUnix).IsEqualTo(1_700_000_000L);
        await Assert.That(stored.DateReceivedUnix).IsEqualTo(1_700_000_001L);
        await Assert.That(stored.Folder).IsEqualTo("INBOX");
        await Assert.That(stored.Account).IsEqualTo("work");
        await Assert.That(stored.HasAttachments).IsTrue();
        await Assert.That(stored.AttachmentNames[0]).IsEqualTo("report.pdf");
        await Assert.That(stored.BodyPreview).IsEqualTo("preview");
        await Assert.That(stored.BodyText).IsEqualTo("full body");
        await Assert.That(stored.IndexedAtUnix).IsEqualTo(1_700_000_100L);
        await Assert.That(stored.LastModifiedTicks).IsEqualTo(42L);
    }

    [Test]
    public async Task UpsertAsync_StoresMissingValuesAsNull()
    {
        var database = await OpenAsync();

        await database.UpsertAsync([Sample.Document("/mail/a.eml", from: null, subject: null, to: null, dateSentUnix: null, account: null, folder: null)]);
        var stored = (await database.QueryAsync(new SearchQuery())).Single();

        await Assert.That(stored.FromAddress).IsNull();
        await Assert.That(stored.Subject).IsNull();
        await Assert.That(stored.DateSentUnix).IsNull();
        await Assert.That(stored.ToAddresses.Count).IsEqualTo(0);
        await Assert.That(stored.HasAttachments).IsFalse();
    }

    [Test]
    public async Task UpsertAsync_SameFilePathUpdatesRowAndFullTextIndex()
    {
        var database = await OpenAsync();
        await database.UpsertAsync([Sample.Document("/mail/a.eml", subject: "Original", body: "alpha")]);

        await database.UpsertAsync([Sample.Document("/mail/a.eml", subject: "Updated", body: "beta", lastModifiedTicks: 2)]);

        await Assert.That(await database.GetEmailCountAsync()).IsEqualTo(1L);
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "alpha" })).IsEqualTo(0);
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "beta" })).IsEqualTo(1);
        await Assert.That((await database.GetKnownFilesAsync())["/mail/a.eml"]).IsEqualTo(2L);
    }

    [Test]
    public async Task UpsertAsync_EmptyBatchIsANoOp()
    {
        var database = await OpenAsync();

        await database.UpsertAsync([]);

        await Assert.That(await database.GetEmailCountAsync()).IsEqualTo(0L);
    }

    [Test]
    public async Task Query_FullTextMatchesSubjectBodySenderAndStems()
    {
        var database = await OpenAsync();
        await database.UpsertAsync(
        [
            Sample.Document("/mail/1.eml", subject: "Important meeting", body: "agenda attached"),
            Sample.Document("/mail/2.eml", subject: "Casual chat", body: "we were running late"),
            Sample.Document("/mail/3.eml", subject: "Other", body: "nothing", from: "zed@kafka.example")
        ]);

        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "meeting" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "run" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "kafka" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "agen*" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "absent" })).IsEqualTo(0);
    }

    [Test]
    [Arguments("say \"hello\"")]
    [Arguments("a AND (b OR")]
    [Arguments("subject:x NEAR/2 y")]
    [Arguments("it's -negated ^caret")]
    public async Task Query_TreatsFullTextSyntaxAsLiteralText(string terms)
    {
        var database = await OpenAsync();
        await database.UpsertAsync([Sample.Document("/mail/1.eml", body: "plain words")]);

        var count = await database.CountAsync(new SearchQuery { ContentTerms = terms });

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task Query_FiltersBySenderExactlyIgnoringCaseOrByWildcard()
    {
        var database = await OpenAsync();
        await database.UpsertAsync(
        [
            Sample.Document("/mail/1.eml", from: "Alice@Example.com"),
            Sample.Document("/mail/2.eml", from: "bob@example.com"),
            Sample.Document("/mail/3.eml", from: "carol@other.org")
        ]);

        await Assert.That(await database.CountAsync(new SearchQuery { FromAddress = "alice@example.com" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { FromAddress = "*@example.com" })).IsEqualTo(2);
        await Assert.That(await database.CountAsync(new SearchQuery { FromAddress = "alice" })).IsEqualTo(0);
    }

    [Test]
    public async Task Query_FiltersByRecipientSubjectAccountFolderAndDate()
    {
        var database = await OpenAsync();
        await database.UpsertAsync(
        [
            Sample.Document("/mail/1.eml", subject: "Budget 2025", to: "[\"bob@example.com\"]", account: "work", folder: "INBOX", dateSentUnix: 1000),
            Sample.Document("/mail/2.eml", subject: "Holiday", to: "[\"carol@example.com\"]", account: "home", folder: "Sent", dateSentUnix: 2000)
        ]);

        await Assert.That(await database.CountAsync(new SearchQuery { ToAddress = "carol@" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { Subject = "budget" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { Account = "home" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { Folder = "INBOX" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { DateFrom = DateTimeOffset.FromUnixTimeSeconds(1500) })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { DateTo = DateTimeOffset.FromUnixTimeSeconds(1500) })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { Account = "work", Folder = "Sent" })).IsEqualTo(0);
    }

    [Test]
    public async Task Query_PagesNewestFirstWhileCountIgnoresPaging()
    {
        var database = await OpenAsync();
        await database.UpsertAsync(
            Enumerable.Range(1, 5).Select(i => Sample.Document($"/mail/{i}.eml", dateSentUnix: i * 100)).ToList());

        var page = await database.QueryAsync(new SearchQuery { Take = 2, Skip = 1 });

        await Assert.That(page.Count).IsEqualTo(2);
        await Assert.That(page[0].FilePath).IsEqualTo("/mail/4.eml");
        await Assert.That(page[1].FilePath).IsEqualTo("/mail/3.eml");
        await Assert.That(await database.CountAsync(new SearchQuery { Take = 2, Skip = 1 })).IsEqualTo(5);
    }

    [Test]
    public async Task Metadata_RoundTripsAndOverwrites()
    {
        var database = await OpenAsync();

        await database.SetMetadataAsync("key", "one");
        await database.SetMetadataAsync("key", "two");

        await Assert.That(await database.GetMetadataAsync("key")).IsEqualTo("two");
        await Assert.That(await database.GetMetadataAsync("missing")).IsNull();
    }

    [Test]
    public async Task ResetAsync_DiscardsEverythingAndStaysUsable()
    {
        var database = await OpenAsync();
        await database.UpsertAsync([Sample.Document("/mail/1.eml", body: "alpha")]);
        await database.SetMetadataAsync("key", "value");

        await database.ResetAsync();
        await database.UpsertAsync([Sample.Document("/mail/2.eml", body: "beta")]);

        await Assert.That(await database.GetEmailCountAsync()).IsEqualTo(1L);
        await Assert.That(await database.GetMetadataAsync("key")).IsNull();
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "alpha" })).IsEqualTo(0);
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "beta" })).IsEqualTo(1);
        await Assert.That(await database.IsHealthyAsync()).IsTrue();
    }

    [Test]
    public async Task OpenAsync_ReusesIndexBuiltByEarlierVersionsWithoutRebuild()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = LegacySchema;
            await command.ExecuteNonQueryAsync();
        }

        var database = await OpenAsync();
        await database.UpsertAsync([Sample.Document("/mail/new.eml", from: "bob@example.com", body: "fresh content")]);

        var known = await database.GetKnownFilesAsync();
        await Assert.That(known["/mail/work/INBOX/cur/legacy.eml"]).IsEqualTo(638000000000000000L);
        await Assert.That(await database.GetMetadataAsync(SearchDatabase.LastIndexedKey)).IsEqualTo("1700000000");
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "kafka" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { FromAddress = "alice@example.com" })).IsEqualTo(1);
        await Assert.That(await database.CountAsync(new SearchQuery { ContentTerms = "fresh" })).IsEqualTo(1);
        await Assert.That(await database.GetEmailCountAsync()).IsEqualTo(2L);
    }
}
