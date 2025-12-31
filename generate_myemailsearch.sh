#!/bin/bash
# =============================================================================
# MyEmailSearch - Complete Implementation Generator
# =============================================================================
# This script generates the full MyEmailSearch application with:
# - SQLite FTS5 full-text search
# - Structured field searches (from, to, subject, date)
# - OpenTelemetry integration
# - XDG-compliant directory structure
# - Cross-platform support
#
# Prerequisites:
# - .NET 10 SDK installed
# - Git installed
# - Write access to the MyImapDownloader directory
#
# Usage:
#   chmod +x generate_myemailsearch.sh
#   ./generate_myemailsearch.sh
# =============================================================================

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

log_info() { echo -e "${CYAN}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# Determine project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="${SCRIPT_DIR}"

# If script is run from docs/llm or similar, go up
if [[ "${PROJECT_ROOT}" == */docs/* ]]; then
    PROJECT_ROOT="$(cd "${PROJECT_ROOT}/../.." && pwd)"
fi

SEARCH_DIR="${PROJECT_ROOT}/MyEmailSearch"
TESTS_DIR="${PROJECT_ROOT}/MyEmailSearch.Tests"

log_info "Project root: ${PROJECT_ROOT}"
log_info "Search project: ${SEARCH_DIR}"

# =============================================================================
# Prerequisite Checks
# =============================================================================

check_prerequisites() {
    log_info "Checking prerequisites..."
    
    if ! command -v dotnet &> /dev/null; then
        log_error "dotnet SDK not found. Please install .NET 10 SDK."
        exit 1
    fi
    
    DOTNET_VERSION=$(dotnet --version)
    log_info "Found .NET SDK: ${DOTNET_VERSION}"
    
    if [[ ! "${DOTNET_VERSION}" =~ ^10\. ]]; then
        log_warn "Expected .NET 10, found ${DOTNET_VERSION}. Proceeding anyway..."
    fi
    
    log_success "Prerequisites check passed"
}

# =============================================================================
# Directory Structure Creation
# =============================================================================

create_directories() {
    log_info "Creating directory structure..."
    
    mkdir -p "${SEARCH_DIR}/Commands"
    mkdir -p "${SEARCH_DIR}/Search"
    mkdir -p "${SEARCH_DIR}/Indexing"
    mkdir -p "${SEARCH_DIR}/Data"
    mkdir -p "${SEARCH_DIR}/Data/Migrations"
    mkdir -p "${SEARCH_DIR}/Telemetry"
    mkdir -p "${SEARCH_DIR}/Configuration"
    mkdir -p "${SEARCH_DIR}/Infrastructure"
    mkdir -p "${TESTS_DIR}/Search"
    mkdir -p "${TESTS_DIR}/Indexing"
    mkdir -p "${TESTS_DIR}/Data"
    mkdir -p "${TESTS_DIR}/TestFixtures/SampleEmails"
    
    log_success "Directory structure created"
}

# =============================================================================
# Data Layer - SearchDatabase.cs
# =============================================================================

create_search_database() {
    log_info "Creating SearchDatabase.cs..."
    
    cat > "${SEARCH_DIR}/Data/SearchDatabase.cs" << 'CSHARP_EOF'
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MyEmailSearch.Data;

/// <summary>
/// Manages the SQLite database for email search indexing.
/// Uses FTS5 for full-text search and B-tree indexes for structured queries.
/// </summary>
public sealed class SearchDatabase : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SearchDatabase> _logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    public string DatabasePath { get; }

    public SearchDatabase(string databasePath, ILogger<SearchDatabase> logger)
    {
        DatabasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    /// <summary>
    /// Initializes the database, creating tables if they don't exist.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing search database at {Path}", DatabasePath);

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        // Enable WAL mode for better concurrency
        await ExecuteNonQueryAsync("PRAGMA journal_mode = WAL;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("PRAGMA synchronous = NORMAL;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("PRAGMA temp_store = MEMORY;", ct).ConfigureAwait(false);
        await ExecuteNonQueryAsync("PRAGMA mmap_size = 268435456;", ct).ConfigureAwait(false);

        // Create main emails table
        const string createEmailsTable = """
            CREATE TABLE IF NOT EXISTS emails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id TEXT NOT NULL UNIQUE,
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
                indexed_at_unix INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))
            );
            """;
        await ExecuteNonQueryAsync(createEmailsTable, ct).ConfigureAwait(false);

        // Create FTS5 virtual table for full-text search
        const string createFtsTable = """
            CREATE VIRTUAL TABLE IF NOT EXISTS emails_fts USING fts5(
                subject,
                body_text,
                from_address,
                to_addresses,
                content='emails',
                content_rowid='id',
                tokenize='porter unicode61'
            );
            """;
        await ExecuteNonQueryAsync(createFtsTable, ct).ConfigureAwait(false);

        // Create triggers to keep FTS in sync
        const string createInsertTrigger = """
            CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON emails BEGIN
                INSERT INTO emails_fts(rowid, subject, body_text, from_address, to_addresses)
                VALUES (new.id, new.subject, new.body_text, new.from_address, new.to_addresses);
            END;
            """;
        await ExecuteNonQueryAsync(createInsertTrigger, ct).ConfigureAwait(false);

        const string createDeleteTrigger = """
            CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, subject, body_text, from_address, to_addresses)
                VALUES ('delete', old.id, old.subject, old.body_text, old.from_address, old.to_addresses);
            END;
            """;
        await ExecuteNonQueryAsync(createDeleteTrigger, ct).ConfigureAwait(false);

        const string createUpdateTrigger = """
            CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON emails BEGIN
                INSERT INTO emails_fts(emails_fts, rowid, subject, body_text, from_address, to_addresses)
                VALUES ('delete', old.id, old.subject, old.body_text, old.from_address, old.to_addresses);
                INSERT INTO emails_fts(rowid, subject, body_text, from_address, to_addresses)
                VALUES (new.id, new.subject, new.body_text, new.from_address, new.to_addresses);
            END;
            """;
        await ExecuteNonQueryAsync(createUpdateTrigger, ct).ConfigureAwait(false);

        // Create indexes for structured queries
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_from ON emails(from_address COLLATE NOCASE);", ct)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date_sent_unix DESC);", ct)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_folder ON emails(folder);", ct)
            .ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            "CREATE INDEX IF NOT EXISTS idx_emails_account ON emails(account);", ct)
            .ConfigureAwait(false);

        // Create metadata table for tracking index state
        const string createMetadataTable = """
            CREATE TABLE IF NOT EXISTS index_metadata (
                key TEXT PRIMARY KEY,
                value TEXT,
                updated_at_unix INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))
            );
            """;
        await ExecuteNonQueryAsync(createMetadataTable, ct).ConfigureAwait(false);

        _logger.LogInformation("Search database initialized successfully");
    }

    /// <summary>
    /// Checks if the database is healthy and can be used.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            await EnsureConnectionAsync(ct).ConfigureAwait(false);
            var result = await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database health check failed");
            return false;
        }
    }

    /// <summary>
    /// Inserts or updates an email document in the index.
    /// </summary>
    public async Task UpsertEmailAsync(EmailDocument email, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO emails (
                message_id, file_path, from_address, from_name, to_addresses,
                cc_addresses, bcc_addresses, subject, date_sent_unix, date_received_unix,
                folder, account, has_attachments, attachment_names, body_preview, body_text
            ) VALUES (
                @message_id, @file_path, @from_address, @from_name, @to_addresses,
                @cc_addresses, @bcc_addresses, @subject, @date_sent_unix, @date_received_unix,
                @folder, @account, @has_attachments, @attachment_names, @body_preview, @body_text
            )
            ON CONFLICT(message_id) DO UPDATE SET
                file_path = excluded.file_path,
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
                indexed_at_unix = strftime('%s', 'now');
            """;

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@message_id", email.MessageId);
        cmd.Parameters.AddWithValue("@file_path", email.FilePath);
        cmd.Parameters.AddWithValue("@from_address", (object?)email.FromAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@from_name", (object?)email.FromName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to_addresses", (object?)email.ToAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cc_addresses", (object?)email.CcAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bcc_addresses", (object?)email.BccAddressesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@subject", (object?)email.Subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@date_sent_unix", email.DateSentUnix ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@date_received_unix", email.DateReceivedUnix ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@folder", (object?)email.Folder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@account", (object?)email.Account ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@has_attachments", email.HasAttachments ? 1 : 0);
        cmd.Parameters.AddWithValue("@attachment_names", (object?)email.AttachmentNamesJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@body_preview", (object?)email.BodyPreview ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@body_text", (object?)email.BodyText ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Batch inserts multiple emails efficiently within a transaction.
    /// </summary>
    public async Task BatchUpsertEmailsAsync(
        IEnumerable<EmailDocument> emails,
        CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var transaction = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var email in emails)
            {
                ct.ThrowIfCancellationRequested();
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

    /// <summary>
    /// Executes a full-text search query.
    /// </summary>
    public async Task<IReadOnlyList<EmailDocument>> SearchAsync(
        string ftsQuery,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT e.*, bm25(emails_fts) as rank
            FROM emails e
            JOIN emails_fts ON e.id = emails_fts.rowid
            WHERE emails_fts MATCH @query
            ORDER BY rank
            LIMIT @limit OFFSET @offset;
            """;

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var results = new List<EmailDocument>();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@query", ftsQuery);
        cmd.Parameters.AddWithValue("@limit", limit);
        cmd.Parameters.AddWithValue("@offset", offset);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadEmailDocument(reader));
        }

        return results;
    }

    /// <summary>
    /// Executes a structured query with optional filters.
    /// </summary>
    public async Task<IReadOnlyList<EmailDocument>> QueryAsync(
        SearchQuery query,
        CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        var (sql, parameters) = BuildStructuredQuery(query);
        var results = new List<EmailDocument>();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(ReadEmailDocument(reader));
        }

        return results;
    }

    private (string Sql, List<(string Name, object Value)> Parameters) BuildStructuredQuery(SearchQuery query)
    {
        var conditions = new List<string>();
        var parameters = new List<(string Name, object Value)>();
        var useFts = !string.IsNullOrWhiteSpace(query.ContentTerms);

        if (!string.IsNullOrWhiteSpace(query.FromAddress))
        {
            if (query.FromAddress.Contains('*'))
            {
                conditions.Add("from_address LIKE @from_address COLLATE NOCASE");
                parameters.Add(("@from_address", query.FromAddress.Replace('*', '%')));
            }
            else
            {
                conditions.Add("from_address = @from_address COLLATE NOCASE");
                parameters.Add(("@from_address", query.FromAddress));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.ToAddress))
        {
            if (query.ToAddress.Contains('*'))
            {
                conditions.Add("to_addresses LIKE @to_address COLLATE NOCASE");
                parameters.Add(("@to_address", $"%{query.ToAddress.Replace('*', '%')}%"));
            }
            else
            {
                conditions.Add("to_addresses LIKE @to_address COLLATE NOCASE");
                parameters.Add(("@to_address", $"%{query.ToAddress}%"));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Subject))
        {
            conditions.Add("subject LIKE @subject COLLATE NOCASE");
            parameters.Add(("@subject", $"%{query.Subject}%"));
        }

        if (query.DateFrom.HasValue)
        {
            conditions.Add("date_sent_unix >= @date_from");
            parameters.Add(("@date_from", query.DateFrom.Value.ToUnixTimeSeconds()));
        }

        if (query.DateTo.HasValue)
        {
            conditions.Add("date_sent_unix <= @date_to");
            parameters.Add(("@date_to", query.DateTo.Value.ToUnixTimeSeconds()));
        }

        if (!string.IsNullOrWhiteSpace(query.Account))
        {
            conditions.Add("account = @account");
            parameters.Add(("@account", query.Account));
        }

        if (!string.IsNullOrWhiteSpace(query.Folder))
        {
            conditions.Add("folder = @folder");
            parameters.Add(("@folder", query.Folder));
        }

        string sql;
        if (useFts)
        {
            // Combined FTS + structured query
            conditions.Add("emails_fts MATCH @fts_query");
            parameters.Add(("@fts_query", EscapeFtsQuery(query.ContentTerms!)));

            var whereClause = conditions.Count > 0
                ? $"WHERE {string.Join(" AND ", conditions)}"
                : "";

            var orderBy = query.SortOrder switch
            {
                SearchSortOrder.DateDescending => "ORDER BY e.date_sent_unix DESC",
                SearchSortOrder.DateAscending => "ORDER BY e.date_sent_unix ASC",
                SearchSortOrder.Relevance => "ORDER BY bm25(emails_fts)",
                _ => "ORDER BY e.date_sent_unix DESC"
            };

            sql = $"""
                SELECT e.*
                FROM emails e
                JOIN emails_fts ON e.id = emails_fts.rowid
                {whereClause}
                {orderBy}
                LIMIT @limit OFFSET @offset;
                """;
        }
        else
        {
            var whereClause = conditions.Count > 0
                ? $"WHERE {string.Join(" AND ", conditions)}"
                : "";

            var orderBy = query.SortOrder switch
            {
                SearchSortOrder.DateDescending => "ORDER BY date_sent_unix DESC",
                SearchSortOrder.DateAscending => "ORDER BY date_sent_unix ASC",
                _ => "ORDER BY date_sent_unix DESC"
            };

            sql = $"""
                SELECT *
                FROM emails
                {whereClause}
                {orderBy}
                LIMIT @limit OFFSET @offset;
                """;
        }

        parameters.Add(("@limit", query.Take));
        parameters.Add(("@offset", query.Skip));

        return (sql, parameters);
    }

    private static string EscapeFtsQuery(string input)
    {
        // Basic FTS5 query escaping
        return input
            .Replace("\"", "\"\"")
            .Replace("'", "''");
    }

    private static EmailDocument ReadEmailDocument(SqliteDataReader reader)
    {
        return new EmailDocument
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            MessageId = reader.GetString(reader.GetOrdinal("message_id")),
            FilePath = reader.GetString(reader.GetOrdinal("file_path")),
            FromAddress = reader.IsDBNull(reader.GetOrdinal("from_address"))
                ? null : reader.GetString(reader.GetOrdinal("from_address")),
            FromName = reader.IsDBNull(reader.GetOrdinal("from_name"))
                ? null : reader.GetString(reader.GetOrdinal("from_name")),
            ToAddressesJson = reader.IsDBNull(reader.GetOrdinal("to_addresses"))
                ? null : reader.GetString(reader.GetOrdinal("to_addresses")),
            CcAddressesJson = reader.IsDBNull(reader.GetOrdinal("cc_addresses"))
                ? null : reader.GetString(reader.GetOrdinal("cc_addresses")),
            BccAddressesJson = reader.IsDBNull(reader.GetOrdinal("bcc_addresses"))
                ? null : reader.GetString(reader.GetOrdinal("bcc_addresses")),
            Subject = reader.IsDBNull(reader.GetOrdinal("subject"))
                ? null : reader.GetString(reader.GetOrdinal("subject")),
            DateSentUnix = reader.IsDBNull(reader.GetOrdinal("date_sent_unix"))
                ? null : reader.GetInt64(reader.GetOrdinal("date_sent_unix")),
            DateReceivedUnix = reader.IsDBNull(reader.GetOrdinal("date_received_unix"))
                ? null : reader.GetInt64(reader.GetOrdinal("date_received_unix")),
            Folder = reader.IsDBNull(reader.GetOrdinal("folder"))
                ? null : reader.GetString(reader.GetOrdinal("folder")),
            Account = reader.IsDBNull(reader.GetOrdinal("account"))
                ? null : reader.GetString(reader.GetOrdinal("account")),
            HasAttachments = reader.GetInt32(reader.GetOrdinal("has_attachments")) == 1,
            AttachmentNamesJson = reader.IsDBNull(reader.GetOrdinal("attachment_names"))
                ? null : reader.GetString(reader.GetOrdinal("attachment_names")),
            BodyPreview = reader.IsDBNull(reader.GetOrdinal("body_preview"))
                ? null : reader.GetString(reader.GetOrdinal("body_preview")),
            BodyText = reader.IsDBNull(reader.GetOrdinal("body_text"))
                ? null : reader.GetString(reader.GetOrdinal("body_text")),
            IndexedAtUnix = reader.GetInt64(reader.GetOrdinal("indexed_at_unix"))
        };
    }

    /// <summary>
    /// Gets the total count of indexed emails.
    /// </summary>
    public async Task<long> GetEmailCountAsync(CancellationToken ct = default)
    {
        return await ExecuteScalarAsync<long>("SELECT COUNT(*) FROM emails;", ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the database file size in bytes.
    /// </summary>
    public long GetDatabaseSize()
    {
        return File.Exists(DatabasePath) ? new FileInfo(DatabasePath).Length : 0;
    }

    /// <summary>
    /// Gets metadata value by key.
    /// </summary>
    public async Task<string?> GetMetadataAsync(string key, CancellationToken ct = default)
    {
        const string sql = "SELECT value FROM index_metadata WHERE key = @key;";
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result as string;
    }

    /// <summary>
    /// Sets metadata value by key.
    /// </summary>
    public async Task SetMetadataAsync(string key, string value, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO index_metadata (key, value, updated_at_unix)
            VALUES (@key, @value, strftime('%s', 'now'))
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at_unix = strftime('%s', 'now');
            """;

        await ExecuteNonQueryAsync(sql, ct,
            ("@key", key),
            ("@value", value)).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks if an email with the given message ID exists.
    /// </summary>
    public async Task<bool> EmailExistsAsync(string messageId, CancellationToken ct = default)
    {
        const string sql = "SELECT 1 FROM emails WHERE message_id = @message_id LIMIT 1;";
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@message_id", messageId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result != null;
    }

    /// <summary>
    /// Deletes all data and rebuilds the database schema.
    /// </summary>
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        _logger.LogWarning("Rebuilding database - all indexed data will be lost");

        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }
        if (File.Exists(DatabasePath + "-wal"))
        {
            File.Delete(DatabasePath + "-wal");
        }
        if (File.Exists(DatabasePath + "-shm"))
        {
            File.Delete(DatabasePath + "-shm");
        }

        await InitializeAsync(ct).ConfigureAwait(false);
    }

    private async Task EnsureConnectionAsync(CancellationToken ct)
    {
        if (_connection == null)
        {
            _connection = new SqliteConnection(_connectionString);
            await _connection.OpenAsync(ct).ConfigureAwait(false);
        }
        else if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ExecuteNonQueryAsync(
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql, CancellationToken ct)
    {
        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
CSHARP_EOF

    log_success "Created SearchDatabase.cs"
}

# =============================================================================
# Data Models
# =============================================================================

create_data_models() {
    log_info "Creating data models..."
    
    cat > "${SEARCH_DIR}/Data/EmailDocument.cs" << 'CSHARP_EOF'
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyEmailSearch.Data;

/// <summary>
/// Represents an indexed email document in the search database.
/// </summary>
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

    // Computed properties
    [JsonIgnore]
    public DateTimeOffset? DateSent => DateSentUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateSentUnix.Value)
        : null;

    [JsonIgnore]
    public DateTimeOffset? DateReceived => DateReceivedUnix.HasValue
        ? DateTimeOffset.FromUnixTimeSeconds(DateReceivedUnix.Value)
        : null;

    [JsonIgnore]
    public IReadOnlyList<string> ToAddresses => ParseJsonArray(ToAddressesJson);

    [JsonIgnore]
    public IReadOnlyList<string> CcAddresses => ParseJsonArray(CcAddressesJson);

    [JsonIgnore]
    public IReadOnlyList<string> BccAddresses => ParseJsonArray(BccAddressesJson);

    [JsonIgnore]
    public IReadOnlyList<string> AttachmentNames => ParseJsonArray(AttachmentNamesJson);

    private static IReadOnlyList<string> ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string ToJsonArray(IEnumerable<string>? items)
    {
        if (items == null) return "[]";
        return JsonSerializer.Serialize(items.ToList());
    }
}
CSHARP_EOF

    cat > "${SEARCH_DIR}/Data/SearchQuery.cs" << 'CSHARP_EOF'
namespace MyEmailSearch.Data;

/// <summary>
/// Represents parsed search criteria.
/// </summary>
public sealed record SearchQuery
{
    public string? FromAddress { get; init; }
    public string? ToAddress { get; init; }
    public string? Subject { get; init; }
    public string? ContentTerms { get; init; }
    public DateTimeOffset? DateFrom { get; init; }
    public DateTimeOffset? DateTo { get; init; }
    public string? Account { get; init; }
    public string? Folder { get; init; }
    public int Skip { get; init; } = 0;
    public int Take { get; init; } = 100;
    public SearchSortOrder SortOrder { get; init; } = SearchSortOrder.DateDescending;
}

public enum SearchSortOrder
{
    DateDescending,
    DateAscending,
    Relevance
}
CSHARP_EOF

    cat > "${SEARCH_DIR}/Data/SearchResult.cs" << 'CSHARP_EOF'
namespace MyEmailSearch.Data;

/// <summary>
/// Represents a single search result with optional snippet.
/// </summary>
public sealed record SearchResult
{
    public required EmailDocument Email { get; init; }
    public string? Snippet { get; init; }
    public IReadOnlyList<string> MatchedTerms { get; init; } = [];
    public double? Score { get; init; }
}

/// <summary>
/// Represents a set of search results with pagination info.
/// </summary>
public sealed record SearchResultSet
{
    public IReadOnlyList<SearchResult> Results { get; init; } = [];
    public int TotalCount { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
    public TimeSpan QueryTime { get; init; }

    public bool HasMore => Skip + Results.Count < TotalCount;
}
CSHARP_EOF

    log_success "Created data models"
}

# =============================================================================
# Search Engine
# =============================================================================

create_search_engine() {
    log_info "Creating SearchEngine.cs..."
    
    cat > "${SEARCH_DIR}/Search/SearchEngine.cs" << 'CSHARP_EOF'
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

/// <summary>
/// Main search engine that coordinates queries against the SQLite database.
/// </summary>
public sealed class SearchEngine : IAsyncDisposable
{
    private readonly SearchDatabase _database;
    private readonly QueryParser _queryParser;
    private readonly SnippetGenerator _snippetGenerator;
    private readonly ILogger<SearchEngine> _logger;

    public SearchEngine(
        SearchDatabase database,
        QueryParser queryParser,
        SnippetGenerator snippetGenerator,
        ILogger<SearchEngine> logger)
    {
        _database = database;
        _queryParser = queryParser;
        _snippetGenerator = snippetGenerator;
        _logger = logger;
    }

    /// <summary>
    /// Executes a search query and returns results.
    /// </summary>
    public async Task<SearchResultSet> SearchAsync(
        string queryString,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Executing search: {Query}", queryString);

        var query = _queryParser.Parse(queryString);
        query = query with { Take = limit, Skip = offset };

        var emails = await _database.QueryAsync(query, ct).ConfigureAwait(false);

        var results = new List<SearchResult>();
        foreach (var email in emails)
        {
            var snippet = !string.IsNullOrWhiteSpace(query.ContentTerms)
                ? _snippetGenerator.Generate(email.BodyText, query.ContentTerms)
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
            "Search completed: {ResultCount} results in {ElapsedMs}ms",
            results.Count, stopwatch.ElapsedMilliseconds);

        return new SearchResultSet
        {
            Results = results,
            TotalCount = results.Count, // TODO: Get actual total count
            Skip = offset,
            Take = limit,
            QueryTime = stopwatch.Elapsed
        };
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

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
CSHARP_EOF

    log_success "Created SearchEngine.cs"
}

# =============================================================================
# Query Parser
# =============================================================================

create_query_parser() {
    log_info "Creating QueryParser.cs..."
    
    cat > "${SEARCH_DIR}/Search/QueryParser.cs" << 'CSHARP_EOF'
using System.Text.RegularExpressions;
using MyEmailSearch.Data;

namespace MyEmailSearch.Search;

/// <summary>
/// Parses user search queries into structured SearchQuery objects.
/// Supports syntax like: from:alice@example.com subject:"project update" kafka
/// </summary>
public sealed partial class QueryParser
{
    [GeneratedRegex(@"from:(?<value>""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex FromPattern();

    [GeneratedRegex(@"to:(?<value>""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex ToPattern();

    [GeneratedRegex(@"subject:(?<value>""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectPattern();

    [GeneratedRegex(@"date:(?<from>\d{4}-\d{2}-\d{2})(?:\.\.(?<to>\d{4}-\d{2}-\d{2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"account:(?<value>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"folder:(?<value>""[^""]+""|\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex FolderPattern();

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
CSHARP_EOF

    log_success "Created QueryParser.cs"
}

# =============================================================================
# Snippet Generator
# =============================================================================

create_snippet_generator() {
    log_info "Creating SnippetGenerator.cs..."
    
    cat > "${SEARCH_DIR}/Search/SnippetGenerator.cs" << 'CSHARP_EOF'
using System.Text;

namespace MyEmailSearch.Search;

/// <summary>
/// Generates contextual snippets from email body text highlighting matched terms.
/// </summary>
public sealed class SnippetGenerator
{
    private const int SnippetLength = 200;
    private const int ContextPadding = 50;

    /// <summary>
    /// Generates a snippet from the body text centered around the search terms.
    /// </summary>
    public string? Generate(string? bodyText, string? searchTerms)
    {
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(searchTerms))
        {
            return Truncate(bodyText, SnippetLength);
        }

        var terms = searchTerms.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstMatchIndex = -1;

        // Find the first occurrence of any search term
        foreach (var term in terms)
        {
            var index = bodyText.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (firstMatchIndex < 0 || index < firstMatchIndex))
            {
                firstMatchIndex = index;
            }
        }

        if (firstMatchIndex < 0)
        {
            return Truncate(bodyText, SnippetLength);
        }

        // Calculate snippet window
        var start = Math.Max(0, firstMatchIndex - ContextPadding);
        var end = Math.Min(bodyText.Length, start + SnippetLength);

        // Adjust start to word boundary
        if (start > 0)
        {
            var wordStart = bodyText.LastIndexOf(' ', start);
            if (wordStart > 0)
            {
                start = wordStart + 1;
            }
        }

        // Adjust end to word boundary
        if (end < bodyText.Length)
        {
            var wordEnd = bodyText.IndexOf(' ', end);
            if (wordEnd > 0)
            {
                end = wordEnd;
            }
        }

        var snippet = new StringBuilder();
        if (start > 0)
        {
            snippet.Append("...");
        }

        snippet.Append(bodyText.AsSpan(start, end - start));

        if (end < bodyText.Length)
        {
            snippet.Append("...");
        }

        return snippet.ToString();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var truncated = text[..maxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            truncated = truncated[..lastSpace];
        }

        return truncated + "...";
    }
}
CSHARP_EOF

    log_success "Created SnippetGenerator.cs"
}

# =============================================================================
# Index Manager and Email Parser
# =============================================================================

create_indexing_components() {
    log_info "Creating indexing components..."
    
    cat > "${SEARCH_DIR}/Indexing/IndexManager.cs" << 'CSHARP_EOF'
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

/// <summary>
/// Manages the email search index lifecycle.
/// </summary>
public sealed class IndexManager
{
    private readonly SearchDatabase _database;
    private readonly ArchiveScanner _scanner;
    private readonly EmailParser _parser;
    private readonly ILogger<IndexManager> _logger;

    public IndexManager(
        SearchDatabase database,
        ArchiveScanner scanner,
        EmailParser parser,
        ILogger<IndexManager> logger)
    {
        _database = database;
        _scanner = scanner;
        _parser = parser;
        _logger = logger;
    }

    /// <summary>
    /// Performs incremental indexing - only indexes new emails.
    /// </summary>
    public async Task<IndexingResult> IndexAsync(
        string archivePath,
        bool includeContent,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new IndexingResult();

        _logger.LogInformation("Starting incremental index of {Path}", archivePath);

        var lastIndexed = await _database.GetMetadataAsync("last_indexed_time", ct)
            .ConfigureAwait(false);
        var lastIndexedTime = lastIndexed != null
            ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(lastIndexed))
            : DateTimeOffset.MinValue;

        var emailFiles = _scanner.ScanForEmails(archivePath);
        var batch = new List<EmailDocument>();
        var processed = 0;
        var total = emailFiles.Count();

        foreach (var file in emailFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Skip already indexed files (based on modification time)
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTimeUtc < lastIndexedTime.UtcDateTime)
                {
                    // Check if already in database
                    var messageId = Path.GetFileNameWithoutExtension(file);
                    if (await _database.EmailExistsAsync(messageId, ct).ConfigureAwait(false))
                    {
                        result.Skipped++;
                        continue;
                    }
                }

                var email = await _parser.ParseAsync(file, includeContent, ct)
                    .ConfigureAwait(false);

                if (email != null)
                {
                    batch.Add(email);
                    result.Indexed++;

                    if (batch.Count >= 100)
                    {
                        await _database.BatchUpsertEmailsAsync(batch, ct).ConfigureAwait(false);
                        batch.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse {File}", file);
                result.Errors++;
            }

            processed++;
            progress?.Report(new IndexingProgress
            {
                Processed = processed,
                Total = total,
                CurrentFile = file
            });
        }

        // Insert remaining batch
        if (batch.Count > 0)
        {
            await _database.BatchUpsertEmailsAsync(batch, ct).ConfigureAwait(false);
        }

        // Update last indexed time
        await _database.SetMetadataAsync(
            "last_indexed_time",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ct).ConfigureAwait(false);

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        _logger.LogInformation(
            "Indexing complete: {Indexed} indexed, {Skipped} skipped, {Errors} errors in {Duration}",
            result.Indexed, result.Skipped, result.Errors, result.Duration);

        return result;
    }

    /// <summary>
    /// Performs a full reindex, deleting all existing data.
    /// </summary>
    public async Task<IndexingResult> RebuildIndexAsync(
        string archivePath,
        bool includeContent,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Rebuilding entire index from scratch");

        await _database.RebuildAsync(ct).ConfigureAwait(false);
        return await IndexAsync(archivePath, includeContent, progress, ct).ConfigureAwait(false);
    }
}

public sealed record IndexingResult
{
    public int Indexed { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public TimeSpan Duration { get; set; }
}

public sealed record IndexingProgress
{
    public int Processed { get; init; }
    public int Total { get; init; }
    public string? CurrentFile { get; init; }
    public double Percentage => Total > 0 ? (double)Processed / Total * 100 : 0;
}
CSHARP_EOF

    cat > "${SEARCH_DIR}/Indexing/ArchiveScanner.cs" << 'CSHARP_EOF'
using Microsoft.Extensions.Logging;

namespace MyEmailSearch.Indexing;

/// <summary>
/// Scans the email archive directory for .eml files.
/// </summary>
public sealed class ArchiveScanner
{
    private readonly ILogger<ArchiveScanner> _logger;

    public ArchiveScanner(ILogger<ArchiveScanner> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scans the archive path for all .eml files.
    /// </summary>
    public IEnumerable<string> ScanForEmails(string archivePath)
    {
        if (!Directory.Exists(archivePath))
        {
            _logger.LogWarning("Archive path does not exist: {Path}", archivePath);
            yield break;
        }

        _logger.LogInformation("Scanning for emails in {Path}", archivePath);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        foreach (var file in Directory.EnumerateFiles(archivePath, "*.eml", options))
        {
            yield return file;
        }
    }

    /// <summary>
    /// Gets the account name from a file path (assumes account folder structure).
    /// </summary>
    public static string? ExtractAccountName(string filePath, string archivePath)
    {
        var relativePath = Path.GetRelativePath(archivePath, filePath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);

        // Expected structure: account_name/folder/cur/file.eml
        return parts.Length >= 2 ? parts[0] : null;
    }

    /// <summary>
    /// Gets the folder name from a file path.
    /// </summary>
    public static string? ExtractFolderName(string filePath, string archivePath)
    {
        var relativePath = Path.GetRelativePath(archivePath, filePath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);

        // Expected structure: account_name/folder/cur/file.eml
        return parts.Length >= 3 ? parts[1] : null;
    }
}
CSHARP_EOF

    cat > "${SEARCH_DIR}/Indexing/EmailParser.cs" << 'CSHARP_EOF'
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimeKit;
using MyEmailSearch.Data;

namespace MyEmailSearch.Indexing;

/// <summary>
/// Parses .eml files and extracts structured data for indexing.
/// </summary>
public sealed class EmailParser
{
    private readonly ILogger<EmailParser> _logger;
    private readonly string _archivePath;
    private const int BodyPreviewLength = 500;

    public EmailParser(string archivePath, ILogger<EmailParser> logger)
    {
        _archivePath = archivePath;
        _logger = logger;
    }

    /// <summary>
    /// Parses an .eml file and returns an EmailDocument.
    /// </summary>
    public async Task<EmailDocument?> ParseAsync(
        string filePath,
        bool includeFullBody,
        CancellationToken ct = default)
    {
        try
        {
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
                ToAddressesJson = EmailDocument.ToJsonArray(
                    message.To.Mailboxes.Select(m => m.Address)),
                CcAddressesJson = EmailDocument.ToJsonArray(
                    message.Cc.Mailboxes.Select(m => m.Address)),
                BccAddressesJson = EmailDocument.ToJsonArray(
                    message.Bcc.Mailboxes.Select(m => m.Address)),
                Subject = message.Subject,
                DateSentUnix = message.Date != DateTimeOffset.MinValue
                    ? message.Date.ToUnixTimeSeconds()
                    : null,
                Folder = ArchiveScanner.ExtractFolderName(filePath, _archivePath),
                Account = ArchiveScanner.ExtractAccountName(filePath, _archivePath),
                HasAttachments = attachmentNames.Count > 0,
                AttachmentNamesJson = attachmentNames.Count > 0
                    ? EmailDocument.ToJsonArray(attachmentNames)
                    : null,
                BodyPreview = bodyPreview,
                BodyText = includeFullBody ? bodyText : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse email: {Path}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Attempts to read metadata from sidecar .meta.json file.
    /// </summary>
    public async Task<EmailMetadata?> ReadMetadataAsync(string emlPath, CancellationToken ct)
    {
        var metaPath = emlPath + ".meta.json";
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metaPath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<EmailMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetBodyText(MimeMessage message)
    {
        // Prefer plain text body
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return NormalizeWhitespace(message.TextBody);
        }

        // Fall back to HTML body stripped of tags
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            return NormalizeWhitespace(StripHtml(message.HtmlBody));
        }

        return null;
    }

    private static string StripHtml(string html)
    {
        // Simple HTML tag stripping - for more robust parsing, use a proper library
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

public sealed record EmailMetadata
{
    public string? MessageId { get; init; }
    public string? Subject { get; init; }
    public string? From { get; init; }
    public DateTimeOffset? Date { get; init; }
    public long? Uid { get; init; }
}
CSHARP_EOF

    log_success "Created indexing components"
}

# =============================================================================
# CLI Commands
# =============================================================================

create_cli_commands() {
    log_info "Creating CLI commands..."
    
    # Program.cs
    cat > "${SEARCH_DIR}/Program.cs" << 'CSHARP_EOF'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyEmailSearch.Commands;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;
using MyEmailSearch.Search;

namespace MyEmailSearch;

/// <summary>
/// Entry point for the MyEmailSearch CLI application.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Build the root command with subcommands
        var rootCommand = new RootCommand("MyEmailSearch - Search your email archive");

        // Add global options
        var archiveOption = new Option<string?>(["--archive", "-a"])
        {
            Description = "Path to the email archive directory"
        };

        var verboseOption = new Option<bool>(["--verbose", "-v"])
        {
            Description = "Enable verbose output"
        };

        var databaseOption = new Option<string?>(["--database", "-d"])
        {
            Description = "Path to the search index database"
        };

        rootCommand.Options.Add(archiveOption);
        rootCommand.Options.Add(verboseOption);
        rootCommand.Options.Add(databaseOption);

        // Add subcommands
        rootCommand.Subcommands.Add(SearchCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(IndexCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(StatusCommand.Create(archiveOption, databaseOption, verboseOption));
        rootCommand.Subcommands.Add(RebuildCommand.Create(archiveOption, databaseOption, verboseOption));

        return await rootCommand.Parse(args).InvokeAsync();
    }

    /// <summary>
    /// Creates a configured service provider for dependency injection.
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

        // Database
        services.AddSingleton(sp =>
            new SearchDatabase(databasePath, sp.GetRequiredService<ILogger<SearchDatabase>>()));

        // Search components
        services.AddSingleton<QueryParser>();
        services.AddSingleton<SnippetGenerator>();
        services.AddSingleton(sp => new SearchEngine(
            sp.GetRequiredService<SearchDatabase>(),
            sp.GetRequiredService<QueryParser>(),
            sp.GetRequiredService<SnippetGenerator>(),
            sp.GetRequiredService<ILogger<SearchEngine>>()));

        // Indexing components
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
CSHARP_EOF

    # SearchCommand.cs
    cat > "${SEARCH_DIR}/Commands/SearchCommand.cs" << 'CSHARP_EOF'
using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Search;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'search' command for querying the email index.
/// </summary>
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

        var limitOption = new Option<int>(["--limit", "-l"])
        {
            Description = "Maximum number of results to return",
            DefaultValueFactory = _ => 100
        };

        var formatOption = new Option<string>(["--format", "-f"])
        {
            Description = "Output format: table, json, or csv",
            DefaultValueFactory = _ => "table"
        };

        var command = new Command("search", "Search emails in the archive");
        command.Arguments.Add(queryArgument);
        command.Options.Add(limitOption);
        command.Options.Add(formatOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(queryArgument)!;
            var limit = parseResult.GetValue(limitOption);
            var format = parseResult.GetValue(formatOption)!;
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(query, limit, format, archivePath, databasePath, verbose, ct);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string query,
        int limit,
        string format,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();
        var searchEngine = sp.GetRequiredService<SearchEngine>();

        // Ensure database is initialized
        await database.InitializeAsync(ct);

        // Execute search
        var results = await searchEngine.SearchAsync(query, limit, 0, ct);

        // Output results
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

    private static void OutputTable(SearchResultSet results)
    {
        Console.WriteLine($"Found {results.Results.Count} results in {results.QueryTime.TotalMilliseconds:F0}ms");
        Console.WriteLine(new string('-', 100));

        if (results.Results.Count == 0)
        {
            Console.WriteLine("No results found.");
            return;
        }

        foreach (var result in results.Results)
        {
            var email = result.Email;
            var date = email.DateSent?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown";
            var from = Truncate(email.FromAddress ?? "Unknown", 30);
            var subject = Truncate(email.Subject ?? "(no subject)", 50);

            Console.WriteLine($"{date}  {from,-30}  {subject}");

            if (!string.IsNullOrWhiteSpace(result.Snippet))
            {
                Console.WriteLine($"           {Truncate(result.Snippet, 90)}");
            }
            Console.WriteLine();
        }
    }

    private static void OutputJson(SearchResultSet results)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        Console.WriteLine(JsonSerializer.Serialize(results, options));
    }

    private static void OutputCsv(SearchResultSet results)
    {
        Console.WriteLine("date,from,to,subject,file_path");
        foreach (var result in results.Results)
        {
            var email = result.Email;
            var date = email.DateSent?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
            var from = EscapeCsv(email.FromAddress ?? "");
            var to = EscapeCsv(string.Join(";", email.ToAddresses));
            var subject = EscapeCsv(email.Subject ?? "");
            var path = EscapeCsv(email.FilePath);

            Console.WriteLine($"{date},{from},{to},{subject},{path}");
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
CSHARP_EOF

    # IndexCommand.cs
    cat > "${SEARCH_DIR}/Commands/IndexCommand.cs" << 'CSHARP_EOF'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'index' command for building/updating the search index.
/// </summary>
public static class IndexCommand
{
    public static Command Create(
        Option<string?> archiveOption,
        Option<string?> databaseOption,
        Option<bool> verboseOption)
    {
        var fullOption = new Option<bool>("--full")
        {
            Description = "Perform full reindex instead of incremental update"
        };

        var contentOption = new Option<bool>("--content")
        {
            Description = "Also index email body content (slower but enables full-text search)"
        };

        var command = new Command("index", "Build or update the search index");
        command.Options.Add(fullOption);
        command.Options.Add(contentOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var full = parseResult.GetValue(fullOption);
            var content = parseResult.GetValue(contentOption);
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(full, content, archivePath, databasePath, verbose, ct);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        bool full,
        bool content,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        Console.WriteLine($"Archive path: {archivePath}");
        Console.WriteLine($"Database path: {databasePath}");
        Console.WriteLine($"Mode: {(full ? "Full rebuild" : "Incremental")}");
        Console.WriteLine($"Index content: {content}");
        Console.WriteLine();

        if (!Directory.Exists(archivePath))
        {
            Console.WriteLine($"Error: Archive path does not exist: {archivePath}");
            return;
        }

        // Ensure database directory exists
        var dbDir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();
        var indexManager = sp.GetRequiredService<IndexManager>();

        // Initialize database
        await database.InitializeAsync(ct);

        // Create progress reporter
        var progress = new Progress<IndexingProgress>(p =>
        {
            Console.Write($"\rProcessing: {p.Processed}/{p.Total} ({p.Percentage:F1}%)");
        });

        // Run indexing
        IndexingResult result;
        if (full)
        {
            result = await indexManager.RebuildIndexAsync(archivePath, content, progress, ct);
        }
        else
        {
            result = await indexManager.IndexAsync(archivePath, content, progress, ct);
        }

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("Indexing complete:");
        Console.WriteLine($"  Indexed: {result.Indexed}");
        Console.WriteLine($"  Skipped: {result.Skipped}");
        Console.WriteLine($"  Errors:  {result.Errors}");
        Console.WriteLine($"  Time:    {result.Duration}");
    }
}
CSHARP_EOF

    # StatusCommand.cs
    cat > "${SEARCH_DIR}/Commands/StatusCommand.cs" << 'CSHARP_EOF'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'status' command to show index statistics.
/// </summary>
public static class StatusCommand
{
    public static Command Create(
        Option<string?> archiveOption,
        Option<string?> databaseOption,
        Option<bool> verboseOption)
    {
        var command = new Command("status", "Show index status and statistics");

        command.SetAction(async (parseResult, ct) =>
        {
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(archivePath, databasePath, verbose, ct);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
    {
        Console.WriteLine("MyEmailSearch - Index Status");
        Console.WriteLine(new string('=', 40));
        Console.WriteLine();

        Console.WriteLine($"Archive path:  {archivePath}");
        Console.WriteLine($"Database path: {databasePath}");
        Console.WriteLine();

        if (!File.Exists(databasePath))
        {
            Console.WriteLine("Status: No index exists yet");
            Console.WriteLine("Run 'myemailsearch index' to create the index");
            return;
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();

        try
        {
            await database.InitializeAsync(ct);

            var emailCount = await database.GetEmailCountAsync(ct);
            var dbSize = database.GetDatabaseSize();
            var lastIndexed = await database.GetMetadataAsync("last_indexed_time", ct);
            var lastIndexedTime = lastIndexed != null
                ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(lastIndexed))
                : (DateTimeOffset?)null;

            Console.WriteLine($"Total emails indexed: {emailCount:N0}");
            Console.WriteLine($"Index size:           {FormatBytes(dbSize)}");
            Console.WriteLine($"Last indexed:         {lastIndexedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never"}");

            var healthy = await database.IsHealthyAsync(ct);
            Console.WriteLine($"Database health:      {(healthy ? "OK" : "ERROR")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading database: {ex.Message}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        var size = (double)bytes;
        while (size >= 1024 && i < suffixes.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F2} {suffixes[i]}";
    }
}
CSHARP_EOF

    # RebuildCommand.cs
    cat > "${SEARCH_DIR}/Commands/RebuildCommand.cs" << 'CSHARP_EOF'
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using MyEmailSearch.Configuration;
using MyEmailSearch.Data;
using MyEmailSearch.Indexing;

namespace MyEmailSearch.Commands;

/// <summary>
/// Handles the 'rebuild' command to rebuild the index from scratch.
/// </summary>
public static class RebuildCommand
{
    public static Command Create(
        Option<string?> archiveOption,
        Option<string?> databaseOption,
        Option<bool> verboseOption)
    {
        var confirmOption = new Option<bool>(["--yes", "-y"])
        {
            Description = "Skip confirmation prompt"
        };

        var contentOption = new Option<bool>("--content")
        {
            Description = "Also index email body content"
        };

        var command = new Command("rebuild", "Rebuild the entire search index from scratch");
        command.Options.Add(confirmOption);
        command.Options.Add(contentOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var confirm = parseResult.GetValue(confirmOption);
            var content = parseResult.GetValue(contentOption);
            var archivePath = parseResult.GetValue(archiveOption)
                ?? PathResolver.GetDefaultArchivePath();
            var databasePath = parseResult.GetValue(databaseOption)
                ?? PathResolver.GetDefaultDatabasePath();
            var verbose = parseResult.GetValue(verboseOption);

            await ExecuteAsync(confirm, content, archivePath, databasePath, verbose, ct);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        bool confirm,
        bool content,
        string archivePath,
        string databasePath,
        bool verbose,
        CancellationToken ct)
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
        Console.WriteLine($"Archive path: {archivePath}");
        Console.WriteLine($"Database path: {databasePath}");
        Console.WriteLine();

        // Ensure database directory exists
        var dbDir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        await using var sp = Program.CreateServiceProvider(archivePath, databasePath, verbose);
        var database = sp.GetRequiredService<SearchDatabase>();
        var indexManager = sp.GetRequiredService<IndexManager>();

        var progress = new Progress<IndexingProgress>(p =>
        {
            Console.Write($"\rProcessing: {p.Processed}/{p.Total} ({p.Percentage:F1}%)");
        });

        var result = await indexManager.RebuildIndexAsync(archivePath, content, progress, ct);

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("Rebuild complete:");
        Console.WriteLine($"  Indexed: {result.Indexed}");
        Console.WriteLine($"  Errors:  {result.Errors}");
        Console.WriteLine($"  Time:    {result.Duration}");
    }
}
CSHARP_EOF

    log_success "Created CLI commands"
}

# =============================================================================
# Configuration
# =============================================================================

create_configuration() {
    log_info "Creating configuration files..."
    
    cat > "${SEARCH_DIR}/Configuration/PathResolver.cs" << 'CSHARP_EOF'
namespace MyEmailSearch.Configuration;

/// <summary>
/// Resolves paths following XDG Base Directory Specification.
/// </summary>
public static class PathResolver
{
    private const string AppName = "myemailsearch";

    /// <summary>
    /// Gets the default archive path, checking environment and common locations.
    /// </summary>
    public static string GetDefaultArchivePath()
    {
        // Check environment variable first
        var envPath = Environment.GetEnvironmentVariable("MYIMAPDOWNLOADER_ARCHIVE");
        if (!string.IsNullOrWhiteSpace(envPath) && Directory.Exists(envPath))
        {
            return envPath;
        }

        // Check XDG_DATA_HOME
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            var xdgPath = Path.Combine(xdgDataHome, "myimapdownloader");
            if (Directory.Exists(xdgPath))
            {
                return xdgPath;
            }
        }

        // Check common locations
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var commonPaths = new[]
        {
            Path.Combine(home, ".local", "share", "myimapdownloader"),
            Path.Combine(home, "Documents", "mail"),
            Path.Combine(home, "mail"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "mail")
        };

        foreach (var path in commonPaths)
        {
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        // Default to XDG location even if it doesn't exist
        return Path.Combine(
            xdgDataHome ?? Path.Combine(home, ".local", "share"),
            "myimapdownloader");
    }

    /// <summary>
    /// Gets the default database path following XDG specification.
    /// </summary>
    public static string GetDefaultDatabasePath()
    {
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dataDir = !string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(xdgDataHome, AppName)
            : Path.Combine(home, ".local", "share", AppName);

        return Path.Combine(dataDir, "search.db");
    }

    /// <summary>
    /// Gets the telemetry directory following XDG specification.
    /// </summary>
    public static string? GetTelemetryDirectory()
    {
        var candidates = GetCandidateDirectories("telemetry");

        foreach (var dir in candidates)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Test write access
                var testFile = Path.Combine(dir, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                return dir;
            }
            catch
            {
                // Try next candidate
            }
        }

        return null; // No writable location found
    }

    private static IEnumerable<string> GetCandidateDirectories(string subdir)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // XDG_DATA_HOME
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            yield return Path.Combine(xdgDataHome, AppName, subdir);
        }

        // LocalApplicationData (works on Windows too)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, AppName, subdir);
        }

        // XDG_STATE_HOME
        var xdgStateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgStateHome))
        {
            yield return Path.Combine(xdgStateHome, AppName, subdir);
        }

        // Fallbacks
        yield return Path.Combine(home, ".local", "state", AppName, subdir);
        yield return Path.Combine(home, ".local", "share", AppName, subdir);

        // Current directory as last resort
        yield return Path.Combine(Directory.GetCurrentDirectory(), subdir);
    }
}
CSHARP_EOF

    cat > "${SEARCH_DIR}/appsettings.json" << 'JSON_EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  },
  "Search": {
    "DefaultResultLimit": 100,
    "MaxResultLimit": 1000,
    "SnippetLength": 200
  },
  "Indexing": {
    "BatchSize": 100,
    "IncludeContentByDefault": false
  }
}
JSON_EOF

    log_success "Created configuration files"
}

# =============================================================================
# Tests
# =============================================================================

create_tests() {
    log_info "Creating test files..."
    
    cat > "${TESTS_DIR}/SmokeTests.cs" << 'CSHARP_EOF'
namespace MyEmailSearch.Tests;

/// <summary>
/// Basic smoke tests to verify the project builds and runs.
/// </summary>
public class SmokeTests
{
    [Test]
    public async Task Project_Builds_Successfully()
    {
        // This test passes if the project compiles
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Can_Create_QueryParser()
    {
        var parser = new Search.QueryParser();
        await Assert.That(parser).IsNotNull();
    }

    [Test]
    public async Task Can_Create_SnippetGenerator()
    {
        var generator = new Search.SnippetGenerator();
        await Assert.That(generator).IsNotNull();
    }
}
CSHARP_EOF

    cat > "${TESTS_DIR}/Search/QueryParserTests.cs" << 'CSHARP_EOF'
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

public class QueryParserTests
{
    private readonly QueryParser _parser = new();

    [Test]
    public async Task Parse_SimpleFromQuery_ExtractsFromAddress()
    {
        var query = _parser.Parse("from:alice@example.com");

        await Assert.That(query.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(query.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_QuotedSubject_ExtractsSubject()
    {
        var query = _parser.Parse("subject:\"project update\"");

        await Assert.That(query.Subject).IsEqualTo("project update");
    }

    [Test]
    public async Task Parse_DateRange_ParsesBothDates()
    {
        var query = _parser.Parse("date:2024-01-01..2024-12-31");

        await Assert.That(query.DateFrom).IsNotNull();
        await Assert.That(query.DateFrom!.Value.Year).IsEqualTo(2024);
        await Assert.That(query.DateFrom!.Value.Month).IsEqualTo(1);
        await Assert.That(query.DateTo).IsNotNull();
        await Assert.That(query.DateTo!.Value.Year).IsEqualTo(2024);
        await Assert.That(query.DateTo!.Value.Month).IsEqualTo(12);
    }

    [Test]
    public async Task Parse_MixedQuery_ExtractsAllParts()
    {
        var query = _parser.Parse("from:alice@example.com subject:report kafka streaming");

        await Assert.That(query.FromAddress).IsEqualTo("alice@example.com");
        await Assert.That(query.Subject).IsEqualTo("report");
        await Assert.That(query.ContentTerms).IsEqualTo("kafka streaming");
    }

    [Test]
    public async Task Parse_WildcardFrom_PreservesWildcard()
    {
        var query = _parser.Parse("from:*@example.com");

        await Assert.That(query.FromAddress).IsEqualTo("*@example.com");
    }

    [Test]
    public async Task Parse_EmptyString_ReturnsEmptyQuery()
    {
        var query = _parser.Parse("");

        await Assert.That(query.FromAddress).IsNull();
        await Assert.That(query.ContentTerms).IsNull();
    }

    [Test]
    public async Task Parse_ContentOnly_ExtractsContentTerms()
    {
        var query = _parser.Parse("kafka streaming message broker");

        await Assert.That(query.ContentTerms).IsEqualTo("kafka streaming message broker");
        await Assert.That(query.FromAddress).IsNull();
    }

    [Test]
    public async Task Parse_ToAddress_ExtractsToAddress()
    {
        var query = _parser.Parse("to:bob@example.com");

        await Assert.That(query.ToAddress).IsEqualTo("bob@example.com");
    }

    [Test]
    public async Task Parse_AccountFilter_ExtractsAccount()
    {
        var query = _parser.Parse("account:work_backup");

        await Assert.That(query.Account).IsEqualTo("work_backup");
    }

    [Test]
    public async Task Parse_FolderFilter_ExtractsFolder()
    {
        var query = _parser.Parse("folder:INBOX");

        await Assert.That(query.Folder).IsEqualTo("INBOX");
    }
}
CSHARP_EOF

    cat > "${TESTS_DIR}/Search/SnippetGeneratorTests.cs" << 'CSHARP_EOF'
using MyEmailSearch.Search;

namespace MyEmailSearch.Tests.Search;

public class SnippetGeneratorTests
{
    private readonly SnippetGenerator _generator = new();

    [Test]
    public async Task Generate_WithMatchingTerm_ReturnsContextAroundMatch()
    {
        var bodyText = "This is a long email body that contains the word kafka somewhere in the middle of the text.";
        var searchTerms = "kafka";

        var snippet = _generator.Generate(bodyText, searchTerms);

        await Assert.That(snippet).IsNotNull();
        await Assert.That(snippet!.Contains("kafka", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task Generate_WithNoMatch_ReturnsBeginningOfText()
    {
        var bodyText = "This is a long email body without any matching terms.";
        var searchTerms = "nonexistent";

        var snippet = _generator.Generate(bodyText, searchTerms);

        await Assert.That(snippet).IsNotNull();
        await Assert.That(snippet!.StartsWith("This")).IsTrue();
    }

    [Test]
    public async Task Generate_WithNullBody_ReturnsNull()
    {
        var snippet = _generator.Generate(null, "test");

        await Assert.That(snippet).IsNull();
    }

    [Test]
    public async Task Generate_WithEmptySearchTerms_ReturnsTruncatedBody()
    {
        var bodyText = "This is a test email body.";

        var snippet = _generator.Generate(bodyText, null);

        await Assert.That(snippet).IsEqualTo(bodyText);
    }
}
CSHARP_EOF

    cat > "${TESTS_DIR}/Data/SearchDatabaseTests.cs" << 'CSHARP_EOF'
using Microsoft.Extensions.Logging.Abstractions;
using MyEmailSearch.Data;

namespace MyEmailSearch.Tests.Data;

public class SearchDatabaseTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly SearchDatabase _database;

    public SearchDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _database = new SearchDatabase(_dbPath, NullLogger<SearchDatabase>.Instance);
    }

    [Test]
    public async Task Initialize_CreatesDatabase()
    {
        await _database.InitializeAsync();

        await Assert.That(File.Exists(_dbPath)).IsTrue();
    }

    [Test]
    public async Task UpsertEmail_InsertsNewEmail()
    {
        await _database.InitializeAsync();

        var email = CreateTestEmail("test-1@example.com");
        await _database.UpsertEmailAsync(email);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertEmail_UpdatesExistingEmail()
    {
        await _database.InitializeAsync();

        var email1 = CreateTestEmail("test-1@example.com") with { Subject = "Original" };
        await _database.UpsertEmailAsync(email1);

        var email2 = CreateTestEmail("test-1@example.com") with { Subject = "Updated" };
        await _database.UpsertEmailAsync(email2);

        var count = await _database.GetEmailCountAsync();
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task EmailExists_ReturnsTrueForExistingEmail()
    {
        await _database.InitializeAsync();

        var email = CreateTestEmail("test-exists@example.com");
        await _database.UpsertEmailAsync(email);

        var exists = await _database.EmailExistsAsync("test-exists@example.com");
        await Assert.That(exists).IsTrue();
    }

    [Test]
    public async Task EmailExists_ReturnsFalseForNonExistingEmail()
    {
        await _database.InitializeAsync();

        var exists = await _database.EmailExistsAsync("nonexistent@example.com");
        await Assert.That(exists).IsFalse();
    }

    [Test]
    public async Task Query_ByFromAddress_ReturnsMatchingEmails()
    {
        await _database.InitializeAsync();

        await _database.UpsertEmailAsync(CreateTestEmail("test-1") with { FromAddress = "alice@example.com" });
        await _database.UpsertEmailAsync(CreateTestEmail("test-2") with { FromAddress = "bob@example.com" });
        await _database.UpsertEmailAsync(CreateTestEmail("test-3") with { FromAddress = "alice@example.com" });

        var query = new SearchQuery { FromAddress = "alice@example.com" };
        var results = await _database.QueryAsync(query);

        await Assert.That(results.Count).IsEqualTo(2);
    }

    [Test]
    public async Task IsHealthy_ReturnsTrueForHealthyDatabase()
    {
        await _database.InitializeAsync();

        var healthy = await _database.IsHealthyAsync();

        await Assert.That(healthy).IsTrue();
    }

    private static EmailDocument CreateTestEmail(string messageId) => new()
    {
        MessageId = messageId,
        FilePath = $"/test/{messageId}.eml",
        FromAddress = "sender@example.com",
        Subject = "Test Subject",
        DateSentUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();

        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
            if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
        }
        catch { /* Ignore cleanup errors */ }
    }
}
CSHARP_EOF

    log_success "Created test files"
}

# =============================================================================
# Project File Update
# =============================================================================

update_csproj() {
    log_info "Updating MyEmailSearch.csproj..."
    
    cat > "${SEARCH_DIR}/MyEmailSearch.csproj" << 'XML_EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <!-- 
    MyEmailSearch - Email Archive Search Utility
    
    This project provides search capabilities over the email archive created by MyImapDownloader.
    It uses SQLite FTS5 for full-text search and shares telemetry/infrastructure patterns.
    
    Note: Most properties (TargetFramework, Nullable, etc.) are inherited from Directory.Build.props
    Package versions are managed centrally in Directory.Packages.props
  -->
  
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MyEmailSearch</RootNamespace>
    <AssemblyName>MyEmailSearch</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- CLI Framework - Using System.CommandLine for modern CLI experience -->
    <PackageReference Include="System.CommandLine" />
    
    <!-- Database - SQLite with FTS5 for search -->
    <PackageReference Include="Microsoft.Data.Sqlite" />
    
    <!-- Email Parsing - MimeKit for extracting body text from .eml files -->
    <PackageReference Include="MimeKit" />
    
    <!-- Configuration -->
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" />
    
    <!-- Dependency Injection & Hosting -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    
    <!-- Logging -->
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
  </ItemGroup>

  <ItemGroup>
    <!-- Copy appsettings.json to output directory -->
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
XML_EOF

    log_success "Updated MyEmailSearch.csproj"
}

# =============================================================================
# Sample Email for Testing
# =============================================================================

create_sample_emails() {
    log_info "Creating sample test emails..."
    
    mkdir -p "${TESTS_DIR}/TestFixtures/SampleEmails"
    
    cat > "${TESTS_DIR}/TestFixtures/SampleEmails/sample1.eml" << 'EML_EOF'
From: alice@example.com
To: bob@example.com
Subject: Project Update - Q4 Review
Date: Mon, 15 Dec 2024 10:30:00 +0000
Message-ID: <sample1@example.com>
Content-Type: text/plain; charset="UTF-8"

Hi Bob,

Here's the update on our Q4 project progress. We've made significant headway on the Kafka integration and the message broker system is now fully operational.

Key achievements:
- Completed Kafka cluster setup
- Implemented event streaming
- Performance testing shows 10x improvement

Let me know if you have any questions.

Best,
Alice
EML_EOF

    cat > "${TESTS_DIR}/TestFixtures/SampleEmails/sample2.eml" << 'EML_EOF'
From: bob@example.com
To: alice@example.com, charlie@example.com
Cc: team@example.com
Subject: Re: Project Update - Q4 Review
Date: Mon, 15 Dec 2024 14:45:00 +0000
Message-ID: <sample2@example.com>
Content-Type: text/plain; charset="UTF-8"

Alice,

Great progress! The Kafka implementation looks solid. I have a few questions:

1. What's the message throughput?
2. How are we handling failover?
3. Any concerns about data retention?

Thanks,
Bob
EML_EOF

    log_success "Created sample test emails"
}

# =============================================================================
# Main Execution
# =============================================================================

main() {
    echo "=============================================="
    echo "  MyEmailSearch - Implementation Generator"
    echo "=============================================="
    echo
    
    check_prerequisites
    create_directories
    create_search_database
    create_data_models
    create_search_engine
    create_query_parser
    create_snippet_generator
    create_indexing_components
    create_cli_commands
    create_configuration
    create_tests
    update_csproj
    create_sample_emails
    
    echo
    log_info "Building project..."
    cd "${PROJECT_ROOT}"
    
    if dotnet build; then
        log_success "Build succeeded!"
    else
        log_error "Build failed. Please check the errors above."
        exit 1
    fi
    
    echo
    log_info "Running tests..."
    if dotnet test --no-build; then
        log_success "All tests passed!"
    else
        log_warn "Some tests failed. Please check the output above."
    fi
    
    echo
    echo "=============================================="
    log_success "MyEmailSearch implementation complete!"
    echo "=============================================="
    echo
    echo "Usage examples:"
    echo "  # Build the search index"
    echo "  dotnet run --project MyEmailSearch -- index --content"
    echo
    echo "  # Search for emails"
    echo "  dotnet run --project MyEmailSearch -- search 'from:alice@example.com kafka'"
    echo
    echo "  # Check index status"
    echo "  dotnet run --project MyEmailSearch -- status"
    echo
    echo "  # Rebuild index from scratch"
    echo "  dotnet run --project MyEmailSearch -- rebuild --yes --content"
    echo
}

main "$@"
