using Microsoft.Data.Sqlite;

using MyImapDownloader.Core.Storage;

namespace MyEmailSearch.Data;

public sealed class SearchDatabase : IAsyncDisposable
{
    public const string LastIndexedKey = "last_indexed_time";

    private const string Schema = """
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
        CREATE INDEX IF NOT EXISTS idx_emails_from_nocase ON emails(from_address COLLATE NOCASE);
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

    private const string Upsert = """
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
            message_id = excluded.message_id,
            from_address = excluded.from_address,
            from_name = excluded.from_name,
            to_addresses = excluded.to_addresses,
            cc_addresses = excluded.cc_addresses,
            bcc_addresses = excluded.bcc_addresses,
            subject = excluded.subject,
            date_sent_unix = excluded.date_sent_unix,
            date_received_unix = excluded.date_received_unix,
            folder = excluded.folder,
            account = excluded.account,
            has_attachments = excluded.has_attachments,
            attachment_names = excluded.attachment_names,
            body_preview = excluded.body_preview,
            body_text = excluded.body_text,
            indexed_at_unix = excluded.indexed_at_unix,
            last_modified_ticks = excluded.last_modified_ticks;
        """;

    private readonly SqliteConnection _connection;
    private readonly string _path;

    private SearchDatabase(SqliteConnection connection, string path)
    {
        _connection = connection;
        _path = path;
    }

    public static async Task<SearchDatabase> OpenAsync(string path, CancellationToken ct = default)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        var connection = await SqliteDatabase.OpenAsync(path, ct);
        try
        {
            await connection.ExecuteAsync(Schema, ct);
            return new SearchDatabase(connection, path);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public long SizeBytes => new FileInfo(_path) is { Exists: true } file ? file.Length : 0;

    public async Task<List<EmailDocument>> QueryAsync(SearchQuery query, CancellationToken ct = default)
    {
        var statement = SearchSql.Select(query);
        await using var command = _connection.Prepare(statement.Text, statement.Parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);

        List<EmailDocument> results = [];
        while (await reader.ReadAsync(ct))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<int> CountAsync(SearchQuery query, CancellationToken ct = default)
    {
        var statement = SearchSql.Count(query);
        return Convert.ToInt32(await _connection.ScalarAsync(statement.Text, ct, statement.Parameters));
    }

    public async Task<long> GetEmailCountAsync(CancellationToken ct = default) =>
        Convert.ToInt64(await _connection.ScalarAsync("SELECT COUNT(*) FROM emails;", ct));

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var tables = await _connection.ScalarAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('emails', 'emails_fts', 'index_metadata');", ct);
            return Convert.ToInt32(tables) == 3;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public async Task<string?> GetMetadataAsync(string key, CancellationToken ct = default) =>
        await _connection.ScalarAsync("SELECT value FROM index_metadata WHERE key = @key;", ct, [("@key", key)])
            as string;

    public Task SetMetadataAsync(string key, string value, CancellationToken ct = default) =>
        _connection.ExecuteAsync(
            """
            INSERT INTO index_metadata (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            ct,
            [("@key", key), ("@value", value)]);

    public async Task UpsertAsync(IReadOnlyCollection<EmailDocument> emails, CancellationToken ct = default)
    {
        if (emails.Count == 0)
        {
            return;
        }

        await using var transaction = await _connection.BeginTransactionAsync(ct);
        foreach (var email in emails)
        {
            await _connection.ExecuteAsync(Upsert, ct, Bind(email));
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<Dictionary<string, long>> GetKnownFilesAsync(CancellationToken ct = default)
    {
        await using var command = _connection.Prepare("SELECT file_path, last_modified_ticks FROM emails;");
        await using var reader = await command.ExecuteReaderAsync(ct);

        Dictionary<string, long> known = new(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            known[reader.GetString(0)] = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
        }

        return known;
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _connection.ExecuteAsync(
            """
            DROP TABLE IF EXISTS emails_fts;
            DROP TABLE IF EXISTS emails;
            DROP TABLE IF EXISTS index_metadata;
            """,
            ct);
        await _connection.ExecuteAsync("VACUUM;", ct);
        await _connection.ExecuteAsync(Schema, ct);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    private static (string Name, object? Value)[] Bind(EmailDocument email) =>
    [
        ("@messageId", email.MessageId),
        ("@filePath", email.FilePath),
        ("@fromAddress", email.FromAddress),
        ("@fromName", email.FromName),
        ("@toAddresses", email.ToAddressesJson),
        ("@ccAddresses", email.CcAddressesJson),
        ("@bccAddresses", email.BccAddressesJson),
        ("@subject", email.Subject),
        ("@dateSentUnix", email.DateSentUnix),
        ("@dateReceivedUnix", email.DateReceivedUnix),
        ("@folder", email.Folder),
        ("@account", email.Account),
        ("@hasAttachments", email.HasAttachments ? 1 : 0),
        ("@attachmentNames", email.AttachmentNamesJson),
        ("@bodyPreview", email.BodyPreview),
        ("@bodyText", email.BodyText),
        ("@indexedAtUnix", email.IndexedAtUnix),
        ("@lastModifiedTicks", email.LastModifiedTicks)
    ];

    private static EmailDocument Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        MessageId = reader.GetString(1),
        FilePath = reader.GetString(2),
        FromAddress = Text(reader, 3),
        FromName = Text(reader, 4),
        ToAddressesJson = Text(reader, 5),
        CcAddressesJson = Text(reader, 6),
        BccAddressesJson = Text(reader, 7),
        Subject = Text(reader, 8),
        DateSentUnix = Integer(reader, 9),
        DateReceivedUnix = Integer(reader, 10),
        Folder = Text(reader, 11),
        Account = Text(reader, 12),
        HasAttachments = Integer(reader, 13) == 1,
        AttachmentNamesJson = Text(reader, 14),
        BodyPreview = Text(reader, 15),
        BodyText = Text(reader, 16),
        IndexedAtUnix = reader.GetInt64(17),
        LastModifiedTicks = Integer(reader, 18) ?? 0
    };

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static long? Integer(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}
