# Email Archive Search System
## Technical Specification Document

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Architecture Overview](#2-architecture-overview)
3. [Project Structure](#3-project-structure)
4. [Technology Stack](#4-technology-stack)
5. [Data Model](#5-data-model)
6. [Component Design](#6-component-design)
7. [Search Implementation](#7-search-implementation)
8. [Indexing Strategy](#8-indexing-strategy)
9. [Telemetry & Observability](#9-telemetry--observability)
10. [Configuration Management](#10-configuration-management)
11. [Error Handling & Resilience](#11-error-handling--resilience)
12. [Testing Strategy](#12-testing-strategy)
13. [CI/CD Pipeline](#13-cicd-pipeline)
14. [Security Considerations](#14-security-considerations)
15. [Performance Targets](#15-performance-targets)
16. [Shell Script Specification](#16-shell-script-specification)

---

## 1. Executive Summary

This document specifies the technical implementation of MyEmailSearch, a companion search utility for the MyImapDownloader email archival system. The search system will enable fast discovery of emails across a 35GB+ archive (scaling to hundreds of gigabytes) using a combination of SQLite full-text search (FTS5) for content queries and B-tree indexes for structured field searches.

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| SQLite FTS5 for full-text | Zero external dependencies, cross-platform, proven at scale |
| Single database file | Portable, easy backup, atomic operations |
| .NET 10 | Latest LTS-track features, native AOT potential |
| No PostgreSQL/SQL Server | Self-contained, no infrastructure requirements |
| Custom telemetry exporters | Reuse proven patterns from MyImapDownloader |
| TUnit for testing | Modern, source-generated, MIT licensed |

### Banned Packages (Non-Negotiable)

The following packages are explicitly banned due to licensing or controversy concerns:

- FluentAssertions (restrictive license changes)
- MassTransit (commercial licensing tiers)
- Moq (SponsorLink controversy)
- Any package with "non-commercial only" clauses
- Any package requiring paid licenses for production use

---

## 2. Architecture Overview

### System Context

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              User                                            │
│                         (CLI / Future API)                                   │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           MyEmailSearch                                      │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │   CLI Host  │  │  Search     │  │   Index     │  │    Telemetry        │ │
│  │   Layer     │──│  Engine     │──│   Manager   │──│    Pipeline         │ │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────────────┘ │
│                           │                │                                 │
│                           ▼                ▼                                 │
│                   ┌─────────────────────────────────┐                       │
│                   │      search.v1.db (SQLite)       │                       │
│                   │  • FTS5 content index            │                       │
│                   │  • Structured field indexes      │                       │
│                   │  • Telemetry tables              │                       │
│                   └─────────────────────────────────┘                       │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MyImapDownloader Archive                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                         │
│  │  .eml files │  │ .meta.json  │  │ index.v1.db │  (Read-Only Access)     │
│  │  (Source)   │  │  (Metadata) │  │ (Sync State)│                         │
│  └─────────────┘  └─────────────┘  └─────────────┘                         │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Data Flow

1. **Indexing Flow**: 
   - Scanner reads `.meta.json` sidecar files (fast metadata extraction)
   - Parser extracts body text from `.eml` files (on-demand)
   - Indexer writes to SQLite FTS5 and structured tables
   - Checkpoint recorded after each batch

2. **Search Flow**:
   - Query parser transforms user input into SQL
   - SQLite executes FTS5 MATCH or structured WHERE clauses
   - Results enriched with file paths and snippets
   - Paginated response returned to user

3. **Telemetry Flow**:
   - All operations emit OpenTelemetry spans and metrics
   - Dual export: JSONL files (existing pattern) + SQLite tables (new)
   - Graceful degradation if write fails

---

## 3. Project Structure

```
MyEmailSearch/
├── Directory.Build.props           # Shared build properties
├── Directory.Build.targets         # Shared build targets  
├── Directory.Packages.props        # Central package management
├── global.json                     # SDK and test runner config
├── MyEmailSearch.sln               # Solution file
├── README.md                       # Documentation
├── LICENSE                         # AGPL-3.0
├── .github/
│   └── workflows/
│       ├── ci.yml                  # Build/test on all branches
│       └── deploy.yml              # Deploy on main/master/develop
├── src/
│   └── MyEmailSearch/
│       ├── MyEmailSearch.csproj
│       ├── Program.cs              # Entry point, CLI setup
│       ├── appsettings.json        # Default configuration
│       ├── Commands/               # CLI command handlers
│       │   ├── SearchCommand.cs
│       │   ├── IndexCommand.cs
│       │   ├── StatusCommand.cs
│       │   └── RebuildCommand.cs
│       ├── Search/                 # Search engine
│       │   ├── SearchEngine.cs
│       │   ├── QueryParser.cs
│       │   ├── SearchResult.cs
│       │   └── SnippetGenerator.cs
│       ├── Indexing/               # Index management
│       │   ├── IndexManager.cs
│       │   ├── ArchiveScanner.cs
│       │   ├── EmailParser.cs
│       │   └── BatchIndexer.cs
│       ├── Data/                   # Data access layer
│       │   ├── SearchDatabase.cs
│       │   ├── Migrations/
│       │   │   └── V1_InitialSchema.cs
│       │   └── Repositories/
│       │       ├── EmailRepository.cs
│       │       └── TelemetryRepository.cs
│       ├── Telemetry/              # Observability (reused patterns)
│       │   ├── DiagnosticsConfig.cs
│       │   ├── TelemetryConfiguration.cs
│       │   ├── DirectoryResolver.cs
│       │   ├── Exporters/
│       │   │   ├── JsonFileExporter.cs
│       │   │   └── SqliteExporter.cs
│       │   └── TelemetryExtensions.cs
│       ├── Configuration/          # Configuration management
│       │   ├── SearchConfiguration.cs
│       │   └── ArchiveConfiguration.cs
│       └── Infrastructure/         # Cross-cutting concerns
│           ├── CancellationExtensions.cs
│           └── PathUtilities.cs
└── tests/
    └── MyEmailSearch.Tests/
        ├── MyEmailSearch.Tests.csproj
        ├── Search/
        │   ├── QueryParserTests.cs
        │   ├── SearchEngineTests.cs
        │   └── SnippetGeneratorTests.cs
        ├── Indexing/
        │   ├── ArchiveScannerTests.cs
        │   ├── EmailParserTests.cs
        │   └── BatchIndexerTests.cs
        ├── Data/
        │   ├── SearchDatabaseTests.cs
        │   └── MigrationTests.cs
        ├── Telemetry/
        │   └── TelemetryTests.cs
        ├── Integration/
        │   ├── EndToEndSearchTests.cs
        │   └── IndexingIntegrationTests.cs
        └── TestFixtures/
            ├── SampleEmails/
            └── TestDatabaseFixture.cs
```

---

## 4. Technology Stack

### Runtime & SDK

| Component | Version | Justification |
|-----------|---------|---------------|
| .NET | 10.0 | Latest features, performance improvements |
| C# | 13.0 | Latest language features via `<LangVersion>latest</LangVersion>` |
| Target Framework | net10.0 | Cross-platform support |

### NuGet Packages (Approved List)

All packages must be MIT, Apache 2.0, or similarly permissive licensed.

```xml
<!-- Directory.Packages.props -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- CLI Framework -->
    <PackageVersion Include="System.CommandLine" Version="2.0.0-beta5.25306.1" />
    
    <!-- Database -->
    <PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.1" />
    
    <!-- Configuration -->
    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.1" />
    
    <!-- Dependency Injection -->
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.1" />
    
    <!-- Logging -->
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Console" Version="10.0.1" />
    
    <!-- OpenTelemetry -->
    <PackageVersion Include="OpenTelemetry" Version="1.14.0" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.14.0" />
    
    <!-- Email Parsing (for body extraction) -->
    <PackageVersion Include="MimeKit" Version="4.14.1" />
    
    <!-- Resilience -->
    <PackageVersion Include="Polly" Version="8.6.5" />
    
    <!-- Testing -->
    <PackageVersion Include="TUnit" Version="1.7.7" />
    <PackageVersion Include="NSubstitute" Version="5.3.0" />
  </ItemGroup>
</Project>
```

### Packages NOT Used (By Design)

| Package | Reason for Exclusion | Alternative |
|---------|---------------------|-------------|
| Entity Framework Core | Overhead, we need raw SQL for FTS5 | Raw Microsoft.Data.Sqlite |
| Dapper | Not strictly needed for this use case | Hand-written data access |
| FluentAssertions | License concerns | TUnit's built-in assertions |
| Moq | SponsorLink controversy | NSubstitute |
| Serilog | Additional dependency | Microsoft.Extensions.Logging |
| AutoMapper | Not needed, simple DTOs | Manual mapping |

---

## 5. Data Model

### SQLite Schema (search.v1.db)

```sql
-- =============================================================================
-- PRAGMA Configuration
-- =============================================================================
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA cache_size = -64000;  -- 64MB cache
PRAGMA mmap_size = 268435456; -- 256MB memory-mapped I/O

-- =============================================================================
-- Schema Version Tracking
-- =============================================================================
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TEXT NOT NULL DEFAULT (datetime('now')),
    description TEXT
);

INSERT OR IGNORE INTO schema_version (version, description) 
VALUES (1, 'Initial schema with FTS5 and structured indexes');

-- =============================================================================
-- Email Metadata Table (Structured Fields)
-- =============================================================================
CREATE TABLE IF NOT EXISTS emails (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    
    -- Unique identifier (from .meta.json or computed)
    message_id TEXT NOT NULL UNIQUE,
    
    -- File location (relative to archive root)
    file_path TEXT NOT NULL UNIQUE,
    
    -- Structured searchable fields
    from_address TEXT,           -- Normalized: lowercase, trimmed
    from_display_name TEXT,      -- Display name portion
    to_addresses TEXT,           -- JSON array of addresses
    cc_addresses TEXT,           -- JSON array of addresses  
    bcc_addresses TEXT,          -- JSON array of addresses
    subject TEXT,
    
    -- Date handling
    date_sent TEXT,              -- ISO 8601 format
    date_sent_unix INTEGER,      -- Unix timestamp for range queries
    
    -- Metadata
    folder TEXT,                 -- Source folder (INBOX, Sent, etc.)
    account TEXT,                -- Account identifier
    has_attachments INTEGER DEFAULT 0,
    size_bytes INTEGER,
    
    -- Indexing state
    indexed_at TEXT NOT NULL DEFAULT (datetime('now')),
    content_indexed INTEGER DEFAULT 0,  -- 1 if body is in FTS
    
    -- Source tracking
    meta_file_path TEXT,         -- Path to .meta.json
    meta_file_hash TEXT          -- SHA256 of .meta.json for change detection
);

-- Indexes for structured queries
CREATE INDEX IF NOT EXISTS idx_emails_from ON emails(from_address);
CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date_sent_unix);
CREATE INDEX IF NOT EXISTS idx_emails_folder ON emails(folder);
CREATE INDEX IF NOT EXISTS idx_emails_account ON emails(account);
CREATE INDEX IF NOT EXISTS idx_emails_subject ON emails(subject);

-- Composite indexes for common query patterns
CREATE INDEX IF NOT EXISTS idx_emails_from_date ON emails(from_address, date_sent_unix);
CREATE INDEX IF NOT EXISTS idx_emails_account_folder ON emails(account, folder);

-- =============================================================================
-- Full-Text Search Table (FTS5)
-- =============================================================================
CREATE VIRTUAL TABLE IF NOT EXISTS emails_fts USING fts5(
    subject,           -- Indexed subject line
    body,              -- Email body text (plain text, no HTML)
    from_address,      -- For FTS on addresses too
    to_addresses,      -- Recipients searchable
    
    content='emails',  -- External content table
    content_rowid='id',
    
    -- Tokenization: Unicode-aware, case-insensitive
    tokenize='unicode61 remove_diacritics 2'
);

-- Triggers to keep FTS in sync with main table
CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON emails BEGIN
    INSERT INTO emails_fts(rowid, subject, body, from_address, to_addresses)
    VALUES (new.id, new.subject, '', new.from_address, new.to_addresses);
END;

CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON emails BEGIN
    INSERT INTO emails_fts(emails_fts, rowid, subject, body, from_address, to_addresses)
    VALUES ('delete', old.id, old.subject, '', old.from_address, old.to_addresses);
END;

CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON emails BEGIN
    INSERT INTO emails_fts(emails_fts, rowid, subject, body, from_address, to_addresses)
    VALUES ('delete', old.id, old.subject, '', old.from_address, old.to_addresses);
    INSERT INTO emails_fts(rowid, subject, body, from_address, to_addresses)
    VALUES (new.id, new.subject, '', new.from_address, new.to_addresses);
END;

-- =============================================================================
-- Indexing State (For Incremental Updates)
-- =============================================================================
CREATE TABLE IF NOT EXISTS index_state (
    id INTEGER PRIMARY KEY CHECK (id = 1),  -- Singleton row
    last_scan_at TEXT,
    last_scan_path TEXT,
    total_emails_indexed INTEGER DEFAULT 0,
    total_bytes_indexed INTEGER DEFAULT 0,
    index_version INTEGER DEFAULT 1
);

INSERT OR IGNORE INTO index_state (id) VALUES (1);

-- =============================================================================
-- Telemetry Tables (OpenTelemetry Storage)
-- =============================================================================
CREATE TABLE IF NOT EXISTS telemetry_traces (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,
    trace_id TEXT,
    span_id TEXT,
    parent_span_id TEXT,
    operation_name TEXT NOT NULL,
    duration_ms REAL,
    status TEXT,
    tags TEXT,  -- JSON object
    events TEXT -- JSON array
);

CREATE INDEX IF NOT EXISTS idx_traces_timestamp ON telemetry_traces(timestamp);
CREATE INDEX IF NOT EXISTS idx_traces_operation ON telemetry_traces(operation_name);

CREATE TABLE IF NOT EXISTS telemetry_metrics (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,
    metric_name TEXT NOT NULL,
    metric_type TEXT,  -- counter, gauge, histogram
    value REAL,
    tags TEXT  -- JSON object
);

CREATE INDEX IF NOT EXISTS idx_metrics_timestamp ON telemetry_metrics(timestamp);
CREATE INDEX IF NOT EXISTS idx_metrics_name ON telemetry_metrics(metric_name);

CREATE TABLE IF NOT EXISTS telemetry_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TEXT NOT NULL,
    level TEXT NOT NULL,
    message TEXT,
    exception TEXT,
    properties TEXT  -- JSON object
);

CREATE INDEX IF NOT EXISTS idx_logs_timestamp ON telemetry_logs(timestamp);
CREATE INDEX IF NOT EXISTS idx_logs_level ON telemetry_logs(level);
```

### Data Transfer Objects

```csharp
// EmailDocument.cs - Represents an indexed email
public sealed record EmailDocument
{
    public required long Id { get; init; }
    public required string MessageId { get; init; }
    public required string FilePath { get; init; }
    public string? FromAddress { get; init; }
    public string? FromDisplayName { get; init; }
    public IReadOnlyList<string> ToAddresses { get; init; } = [];
    public IReadOnlyList<string> CcAddresses { get; init; } = [];
    public IReadOnlyList<string> BccAddresses { get; init; } = [];
    public string? Subject { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public string? Folder { get; init; }
    public string? Account { get; init; }
    public bool HasAttachments { get; init; }
    public long SizeBytes { get; init; }
}

// SearchResult.cs - Represents a search match
public sealed record SearchResult
{
    public required EmailDocument Email { get; init; }
    public double RelevanceScore { get; init; }
    public string? Snippet { get; init; }  // Highlighted match context
    public IReadOnlyList<string> MatchedTerms { get; init; } = [];
}

// SearchQuery.cs - Parsed search criteria
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
```

---

## 6. Component Design

### 6.1 Search Engine

The search engine translates user queries into optimized SQLite queries combining FTS5 and structured searches.

```csharp
// SearchEngine.cs
public sealed class SearchEngine : IAsyncDisposable
{
    private readonly SearchDatabase _database;
    private readonly ILogger<SearchEngine> _logger;
    private readonly SnippetGenerator _snippetGenerator;
    
    public SearchEngine(
        SearchDatabase database,
        ILogger<SearchEngine> logger,
        SnippetGenerator snippetGenerator)
    {
        _database = database;
        _logger = logger;
        _snippetGenerator = snippetGenerator;
    }
    
    public async Task<SearchResultSet> SearchAsync(
        SearchQuery query,
        CancellationToken ct = default)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("Search");
        activity?.SetTag("query.has_content", !string.IsNullOrEmpty(query.ContentTerms));
        activity?.SetTag("query.has_from", !string.IsNullOrEmpty(query.FromAddress));
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Build SQL based on query type
            var (sql, parameters) = BuildSearchSql(query);
            
            // Execute search
            var results = await _database.QueryAsync<EmailDocument>(sql, parameters, ct);
            
            // Generate snippets for content searches
            if (!string.IsNullOrEmpty(query.ContentTerms))
            {
                results = await EnrichWithSnippetsAsync(results, query.ContentTerms, ct);
            }
            
            // Get total count (separate query for pagination)
            var totalCount = await GetTotalCountAsync(query, ct);
            
            stopwatch.Stop();
            DiagnosticsConfig.SearchDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            DiagnosticsConfig.SearchesExecuted.Add(1);
            
            return new SearchResultSet
            {
                Results = results,
                TotalCount = totalCount,
                Query = query,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            DiagnosticsConfig.SearchErrors.Add(1);
            throw;
        }
    }
    
    private (string Sql, Dictionary<string, object> Parameters) BuildSearchSql(SearchQuery query)
    {
        var parameters = new Dictionary<string, object>();
        var conditions = new List<string>();
        var joins = new List<string>();
        
        // Full-text search condition
        if (!string.IsNullOrEmpty(query.ContentTerms))
        {
            joins.Add("JOIN emails_fts ON emails.id = emails_fts.rowid");
            conditions.Add("emails_fts MATCH @contentTerms");
            parameters["@contentTerms"] = EscapeFtsQuery(query.ContentTerms);
        }
        
        // Structured field conditions
        if (!string.IsNullOrEmpty(query.FromAddress))
        {
            if (query.FromAddress.Contains('*'))
            {
                conditions.Add("from_address LIKE @fromAddress");
                parameters["@fromAddress"] = query.FromAddress.Replace('*', '%');
            }
            else
            {
                conditions.Add("from_address = @fromAddress");
                parameters["@fromAddress"] = query.FromAddress.ToLowerInvariant();
            }
        }
        
        if (!string.IsNullOrEmpty(query.Subject))
        {
            conditions.Add("subject LIKE @subject");
            parameters["@subject"] = $"%{query.Subject}%";
        }
        
        if (query.DateFrom.HasValue)
        {
            conditions.Add("date_sent_unix >= @dateFrom");
            parameters["@dateFrom"] = query.DateFrom.Value.ToUnixTimeSeconds();
        }
        
        if (query.DateTo.HasValue)
        {
            conditions.Add("date_sent_unix <= @dateTo");
            parameters["@dateTo"] = query.DateTo.Value.ToUnixTimeSeconds();
        }
        
        // Build final SQL
        var whereClause = conditions.Count > 0 
            ? $"WHERE {string.Join(" AND ", conditions)}" 
            : "";
            
        var joinClause = string.Join(" ", joins);
        
        var orderBy = query.SortOrder switch
        {
            SearchSortOrder.DateDescending => "ORDER BY date_sent_unix DESC",
            SearchSortOrder.DateAscending => "ORDER BY date_sent_unix ASC",
            SearchSortOrder.Relevance when !string.IsNullOrEmpty(query.ContentTerms) 
                => "ORDER BY bm25(emails_fts)",
            _ => "ORDER BY date_sent_unix DESC"
        };
        
        var sql = $@"
            SELECT emails.*
            FROM emails
            {joinClause}
            {whereClause}
            {orderBy}
            LIMIT @take OFFSET @skip";
            
        parameters["@take"] = query.Take;
        parameters["@skip"] = query.Skip;
        
        return (sql, parameters);
    }
    
    private static string EscapeFtsQuery(string input)
    {
        // Escape special FTS5 characters
        // Convert user-friendly syntax to FTS5 syntax
        var escaped = input
            .Replace("\"", "\"\"")  // Escape quotes
            .Replace("AND", "AND")  // Preserve boolean operators
            .Replace("OR", "OR")
            .Replace("NOT", "NOT");
            
        return escaped;
    }
    
    public async ValueTask DisposeAsync()
    {
        // Cleanup if needed
    }
}
```

### 6.2 Index Manager

The index manager handles building and maintaining the search index from the email archive.

```csharp
// IndexManager.cs
public sealed class IndexManager : IAsyncDisposable
{
    private readonly SearchDatabase _database;
    private readonly ArchiveScanner _scanner;
    private readonly EmailParser _parser;
    private readonly BatchIndexer _indexer;
    private readonly ILogger<IndexManager> _logger;
    private readonly IndexConfiguration _config;
    
    public IndexManager(
        SearchDatabase database,
        ArchiveScanner scanner,
        EmailParser parser,
        BatchIndexer indexer,
        ILogger<IndexManager> logger,
        IndexConfiguration config)
    {
        _database = database;
        _scanner = scanner;
        _parser = parser;
        _indexer = indexer;
        _logger = logger;
        _config = config;
    }
    
    /// <summary>
    /// Performs incremental indexing - only processes new/changed emails.
    /// </summary>
    public async Task<IndexingResult> IndexAsync(
        string archivePath,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("IndexArchive");
        activity?.SetTag("archive.path", archivePath);
        
        var result = new IndexingResult();
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Get last indexed state
            var lastState = await _database.GetIndexStateAsync(ct);
            
            // Scan archive for .meta.json files
            var metaFiles = _scanner.ScanForMetadata(archivePath, ct);
            
            var batch = new List<EmailIndexItem>();
            var processed = 0;
            
            await foreach (var metaFile in metaFiles.WithCancellation(ct))
            {
                // Check if already indexed (by hash)
                if (await IsAlreadyIndexedAsync(metaFile, ct))
                {
                    result.Skipped++;
                    continue;
                }
                
                // Parse metadata
                var metadata = await _parser.ParseMetadataAsync(metaFile, ct);
                if (metadata == null)
                {
                    result.Errors++;
                    continue;
                }
                
                // Add to batch
                batch.Add(new EmailIndexItem
                {
                    Metadata = metadata,
                    MetaFilePath = metaFile,
                    MetaFileHash = ComputeFileHash(metaFile)
                });
                
                // Process batch when full
                if (batch.Count >= _config.BatchSize)
                {
                    await ProcessBatchAsync(batch, result, ct);
                    batch.Clear();
                    
                    processed += _config.BatchSize;
                    progress?.Report(new IndexingProgress
                    {
                        ProcessedCount = processed,
                        CurrentFile = metaFile
                    });
                }
            }
            
            // Process remaining items
            if (batch.Count > 0)
            {
                await ProcessBatchAsync(batch, result, ct);
            }
            
            // Update index state
            await _database.UpdateIndexStateAsync(new IndexState
            {
                LastScanAt = DateTimeOffset.UtcNow,
                LastScanPath = archivePath,
                TotalEmailsIndexed = result.Indexed + result.Skipped
            }, ct);
            
            stopwatch.Stop();
            result.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
            
            DiagnosticsConfig.IndexingDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            DiagnosticsConfig.EmailsIndexed.Add(result.Indexed);
            
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Indexing cancelled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Indexing failed");
            throw;
        }
    }
    
    /// <summary>
    /// Rebuilds the entire index from scratch.
    /// </summary>
    public async Task<IndexingResult> RebuildAsync(
        string archivePath,
        IProgress<IndexingProgress>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Starting full index rebuild - this may take a while");
        
        // Clear existing data
        await _database.TruncateIndexAsync(ct);
        
        // Run full index
        return await IndexAsync(archivePath, progress, ct);
    }
    
    /// <summary>
    /// Indexes email body content for full-text search.
    /// This is separate from metadata indexing as it's more expensive.
    /// </summary>
    public async Task<int> IndexContentAsync(
        int batchSize = 100,
        CancellationToken ct = default)
    {
        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("IndexContent");
        
        var indexed = 0;
        
        // Get emails that haven't had content indexed
        var unindexed = await _database.GetUnindexedEmailsAsync(batchSize, ct);
        
        foreach (var email in unindexed)
        {
            ct.ThrowIfCancellationRequested();
            
            try
            {
                // Parse email body from .eml file
                var bodyText = await _parser.ExtractBodyTextAsync(email.FilePath, ct);
                
                if (!string.IsNullOrEmpty(bodyText))
                {
                    // Update FTS index
                    await _database.UpdateEmailContentAsync(email.Id, bodyText, ct);
                    indexed++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index content for {FilePath}", email.FilePath);
            }
        }
        
        return indexed;
    }
    
    private async Task ProcessBatchAsync(
        List<EmailIndexItem> batch,
        IndexingResult result,
        CancellationToken ct)
    {
        try
        {
            await _indexer.IndexBatchAsync(batch, ct);
            result.Indexed += batch.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index batch of {Count} items", batch.Count);
            result.Errors += batch.Count;
        }
    }
    
    private async Task<bool> IsAlreadyIndexedAsync(string metaFilePath, CancellationToken ct)
    {
        var hash = ComputeFileHash(metaFilePath);
        return await _database.ExistsByMetaHashAsync(hash, ct);
    }
    
    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
    
    public async ValueTask DisposeAsync()
    {
        // Cleanup
    }
}

public record IndexingResult
{
    public int Indexed { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public double DurationMs { get; set; }
}

public record IndexingProgress
{
    public int ProcessedCount { get; init; }
    public string? CurrentFile { get; init; }
}
```

### 6.3 Archive Scanner

Efficiently scans the archive directory for indexable content.

```csharp
// ArchiveScanner.cs
public sealed class ArchiveScanner
{
    private readonly ILogger<ArchiveScanner> _logger;
    
    public ArchiveScanner(ILogger<ArchiveScanner> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Scans archive directory for .meta.json files using async enumeration.
    /// </summary>
    public async IAsyncEnumerable<string> ScanForMetadata(
        string archivePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(archivePath))
        {
            _logger.LogError("Archive path does not exist: {Path}", archivePath);
            yield break;
        }
        
        _logger.LogInformation("Scanning archive: {Path}", archivePath);
        
        // Use EnumerateFiles for memory efficiency on large directories
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.System
        };
        
        var files = Directory.EnumerateFiles(
            archivePath, 
            "*.meta.json", 
            options);
        
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            
            // Yield control periodically to allow cancellation
            await Task.Yield();
            
            yield return file;
        }
    }
    
    /// <summary>
    /// Gets archive statistics without full scan.
    /// </summary>
    public async Task<ArchiveStats> GetStatsAsync(
        string archivePath,
        CancellationToken ct = default)
    {
        var stats = new ArchiveStats();
        
        await foreach (var _ in ScanForMetadata(archivePath, ct))
        {
            stats.TotalMetaFiles++;
        }
        
        // Count .eml files separately
        var emlFiles = Directory.EnumerateFiles(
            archivePath, 
            "*.eml", 
            SearchOption.AllDirectories);
            
        stats.TotalEmlFiles = emlFiles.Count();
        
        // Calculate total size
        var dirInfo = new DirectoryInfo(archivePath);
        stats.TotalSizeBytes = dirInfo
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
        
        return stats;
    }
}

public record ArchiveStats
{
    public int TotalMetaFiles { get; set; }
    public int TotalEmlFiles { get; set; }
    public long TotalSizeBytes { get; set; }
}
```

---

## 7. Search Implementation

### Query Parser

Transforms user input into structured SearchQuery objects.

```csharp
// QueryParser.cs
public sealed class QueryParser
{
    private static readonly Regex FromPattern = new(
        @"from:(?<value>""[^""]+""|\S+)", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
    private static readonly Regex ToPattern = new(
        @"to:(?<value>""[^""]+""|\S+)", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
    private static readonly Regex SubjectPattern = new(
        @"subject:(?<value>""[^""]+""|\S+)", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
    private static readonly Regex DatePattern = new(
        @"date:(?<from>\d{4}-\d{2}-\d{2})(?:\.\.(?<to>\d{4}-\d{2}-\d{2}))?", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
    private static readonly Regex AccountPattern = new(
        @"account:(?<value>\S+)", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
    private static readonly Regex FolderPattern = new(
        @"folder:(?<value>""[^""]+""|\S+)", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    /// <summary>
    /// Parses a user query string into a SearchQuery object.
    /// Supports syntax like: from:alice@example.com subject:"project update" kafka
    /// </summary>
    public SearchQuery Parse(string queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return new SearchQuery();
        }
        
        var query = new SearchQuery();
        var remaining = queryString;
        
        // Extract structured filters
        remaining = ExtractPattern(remaining, FromPattern, 
            v => query = query with { FromAddress = NormalizeEmail(v) });
            
        remaining = ExtractPattern(remaining, ToPattern, 
            v => query = query with { ToAddress = NormalizeEmail(v) });
            
        remaining = ExtractPattern(remaining, SubjectPattern, 
            v => query = query with { Subject = UnquoteValue(v) });
            
        remaining = ExtractDatePattern(remaining, 
            (from, to) => query = query with { DateFrom = from, DateTo = to });
            
        remaining = ExtractPattern(remaining, AccountPattern, 
            v => query = query with { Account = v });
            
        remaining = ExtractPattern(remaining, FolderPattern, 
            v => query = query with { Folder = UnquoteValue(v) });
        
        // Remaining text is full-text content search
        var contentTerms = remaining.Trim();
        if (!string.IsNullOrEmpty(contentTerms))
        {
            query = query with { ContentTerms = contentTerms };
        }
        
        return query;
    }
    
    private static string ExtractPattern(
        string input, 
        Regex pattern, 
        Action<string> setValue)
    {
        var match = pattern.Match(input);
        if (match.Success)
        {
            setValue(match.Groups["value"].Value);
            return pattern.Replace(input, "").Trim();
        }
        return input;
    }
    
    private static string ExtractDatePattern(
        string input,
        Action<DateTimeOffset?, DateTimeOffset?> setDates)
    {
        var match = DatePattern.Match(input);
        if (match.Success)
        {
            var fromStr = match.Groups["from"].Value;
            var toStr = match.Groups["to"].Value;
            
            DateTimeOffset? from = DateTimeOffset.TryParse(fromStr, out var f) ? f : null;
            DateTimeOffset? to = !string.IsNullOrEmpty(toStr) && DateTimeOffset.TryParse(toStr, out var t) 
                ? t : null;
            
            setDates(from, to);
            return DatePattern.Replace(input, "").Trim();
        }
        return input;
    }
    
    private static string NormalizeEmail(string email)
    {
        return UnquoteValue(email).ToLowerInvariant().Trim();
    }
    
    private static string UnquoteValue(string value)
    {
        if (value.StartsWith('"') && value.EndsWith('"'))
        {
            return value[1..^1];
        }
        return value;
    }
}
```

### Snippet Generator

Creates highlighted excerpts showing match context.

```csharp
// SnippetGenerator.cs
public sealed class SnippetGenerator
{
    private readonly int _snippetLength;
    private readonly string _highlightStart;
    private readonly string _highlightEnd;
    
    public SnippetGenerator(
        int snippetLength = 200,
        string highlightStart = "**",
        string highlightEnd = "**")
    {
        _snippetLength = snippetLength;
        _highlightStart = highlightStart;
        _highlightEnd = highlightEnd;
    }
    
    /// <summary>
    /// Generates a snippet from email body with search terms highlighted.
    /// </summary>
    public string Generate(string bodyText, string searchTerms)
    {
        if (string.IsNullOrEmpty(bodyText) || string.IsNullOrEmpty(searchTerms))
        {
            return TruncateWithEllipsis(bodyText ?? "", _snippetLength);
        }
        
        // Parse search terms
        var terms = ParseTerms(searchTerms);
        
        // Find best matching region
        var matchPosition = FindBestMatchPosition(bodyText, terms);
        
        // Extract snippet around match
        var snippet = ExtractSnippet(bodyText, matchPosition);
        
        // Highlight terms in snippet
        return HighlightTerms(snippet, terms);
    }
    
    private IReadOnlyList<string> ParseTerms(string searchTerms)
    {
        // Split on whitespace, respecting quoted phrases
        var terms = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        
        foreach (var c in searchTerms)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    terms.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        
        if (current.Length > 0)
        {
            terms.Add(current.ToString());
        }
        
        // Filter out boolean operators
        return terms
            .Where(t => !IsOperator(t))
            .ToList();
    }
    
    private static bool IsOperator(string term)
    {
        return term.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
               term.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
               term.Equals("NOT", StringComparison.OrdinalIgnoreCase);
    }
    
    private int FindBestMatchPosition(string text, IReadOnlyList<string> terms)
    {
        // Find first occurrence of any term
        var minPosition = int.MaxValue;
        
        foreach (var term in terms)
        {
            var pos = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (pos >= 0 && pos < minPosition)
            {
                minPosition = pos;
            }
        }
        
        return minPosition == int.MaxValue ? 0 : minPosition;
    }
    
    private string ExtractSnippet(string text, int position)
    {
        // Calculate start position (try to center on match)
        var start = Math.Max(0, position - _snippetLength / 2);
        var end = Math.Min(text.Length, start + _snippetLength);
        
        // Adjust start to not cut off words
        if (start > 0)
        {
            var wordStart = text.LastIndexOf(' ', start);
            if (wordStart > 0) start = wordStart + 1;
        }
        
        // Adjust end to not cut off words
        if (end < text.Length)
        {
            var wordEnd = text.IndexOf(' ', end);
            if (wordEnd > 0) end = wordEnd;
        }
        
        var snippet = text[start..end];
        
        // Add ellipsis
        if (start > 0) snippet = "..." + snippet;
        if (end < text.Length) snippet += "...";
        
        return snippet;
    }
    
    private string HighlightTerms(string snippet, IReadOnlyList<string> terms)
    {
        var result = snippet;
        
        foreach (var term in terms)
        {
            result = Regex.Replace(
                result,
                Regex.Escape(term),
                $"{_highlightStart}$0{_highlightEnd}",
                RegexOptions.IgnoreCase);
        }
        
        return result;
    }
    
    private static string TruncateWithEllipsis(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}
```

---

## 8. Indexing Strategy

### Initial Index Creation

```
Phase 1: Metadata Indexing (Fast)
┌─────────────────────────────────────────────────────────────────────────────┐
│  For each .meta.json file:                                                   │
│  1. Parse JSON metadata                                                      │
│  2. Normalize fields (lowercase emails, parse dates)                        │
│  3. Insert into emails table                                                │
│  4. Trigger inserts into FTS (subject, addresses only)                      │
│  5. Update index_state checkpoint                                           │
└─────────────────────────────────────────────────────────────────────────────┘
│
│  Estimated time: ~1 minute per 10,000 emails
│  35GB archive (~200K emails): ~20 minutes
│
▼
Phase 2: Content Indexing (Background, Optional)
┌─────────────────────────────────────────────────────────────────────────────┐
│  For each email where content_indexed = 0:                                   │
│  1. Parse .eml file with MimeKit                                            │
│  2. Extract plain text body (skip HTML if plain exists)                     │
│  3. Update FTS body column                                                  │
│  4. Set content_indexed = 1                                                 │
└─────────────────────────────────────────────────────────────────────────────┘
│
│  Estimated time: ~5 minutes per 10,000 emails (I/O bound)
│  35GB archive: ~2 hours (can run in background)
```

### Incremental Updates

```csharp
// Pseudocode for incremental update detection
async Task<bool> NeedsReindexing(string metaFilePath)
{
    // Compute current hash
    var currentHash = ComputeHash(metaFilePath);
    
    // Check against stored hash
    var storedHash = await db.GetMetaHashAsync(metaFilePath);
    
    if (storedHash == null)
    {
        // New file, needs indexing
        return true;
    }
    
    if (storedHash != currentHash)
    {
        // File changed, needs reindexing
        return true;
    }
    
    return false;
}
```

### Index Size Estimation

| Archive Size | Estimated Emails | Index Size (Metadata Only) | Index Size (Full Content) |
|--------------|------------------|---------------------------|--------------------------|
| 35 GB | ~200,000 | ~50 MB | ~500 MB |
| 100 GB | ~600,000 | ~150 MB | ~1.5 GB |
| 500 GB | ~3,000,000 | ~750 MB | ~7.5 GB |

The FTS5 index grows sublinearly due to compression and efficient tokenization.

---

## 9. Telemetry & Observability

### Dual Export Strategy

Telemetry is exported to both:
1. **JSONL files** (compatible with existing MyImapDownloader patterns)
2. **SQLite tables** (for local query/analysis)

```csharp
// TelemetryExtensions.cs
public static class TelemetryExtensions
{
    public static IServiceCollection AddSearchTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var config = new TelemetryConfiguration();
        configuration.GetSection("Telemetry").Bind(config);
        services.AddSingleton(config);
        
        // Resolve output directory with XDG fallback
        var outputDir = DirectoryResolver.ResolveTelemetryDirectory(config.ServiceName);
        
        // Create dual exporters
        JsonFileExporter? jsonExporter = null;
        SqliteExporter? sqliteExporter = null;
        
        if (outputDir != null)
        {
            try
            {
                jsonExporter = new JsonFileExporter(
                    Path.Combine(outputDir, "traces"),
                    config.MaxFileSizeMB * 1024 * 1024,
                    TimeSpan.FromSeconds(config.FlushIntervalSeconds));
            }
            catch
            {
                // Continue without JSON export
            }
            
            try
            {
                sqliteExporter = new SqliteExporter(
                    Path.Combine(outputDir, "telemetry.db"));
            }
            catch
            {
                // Continue without SQLite export
            }
        }
        
        // Register OpenTelemetry with dual export
        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder.AddSource(DiagnosticsConfig.ServiceName);
                
                if (jsonExporter != null)
                {
                    builder.AddProcessor(new BatchActivityExportProcessor(
                        new JsonTraceExporter(jsonExporter)));
                }
                
                if (sqliteExporter != null)
                {
                    builder.AddProcessor(new BatchActivityExportProcessor(
                        new SqliteTraceExporter(sqliteExporter)));
                }
            })
            .WithMetrics(builder =>
            {
                builder.AddMeter(DiagnosticsConfig.ServiceName);
                
                // Similar dual export for metrics
            });
        
        return services;
    }
}
```

### Defined Metrics

```csharp
// DiagnosticsConfig.cs
public static class DiagnosticsConfig
{
    public const string ServiceName = "MyEmailSearch";
    public const string ServiceVersion = "1.0.0";
    
    public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);
    
    // Search metrics
    public static readonly Counter<long> SearchesExecuted = Meter.CreateCounter<long>(
        "searches.executed", unit: "queries", description: "Total search queries executed");
        
    public static readonly Counter<long> SearchErrors = Meter.CreateCounter<long>(
        "searches.errors", unit: "errors", description: "Search query errors");
        
    public static readonly Histogram<double> SearchDuration = Meter.CreateHistogram<double>(
        "search.duration", unit: "ms", description: "Search query execution time");
        
    public static readonly Histogram<long> SearchResultCount = Meter.CreateHistogram<long>(
        "search.results", unit: "emails", description: "Number of results per search");
    
    // Indexing metrics
    public static readonly Counter<long> EmailsIndexed = Meter.CreateCounter<long>(
        "indexing.emails", unit: "emails", description: "Emails indexed");
        
    public static readonly Counter<long> IndexingErrors = Meter.CreateCounter<long>(
        "indexing.errors", unit: "errors", description: "Indexing errors");
        
    public static readonly Histogram<double> IndexingDuration = Meter.CreateHistogram<double>(
        "indexing.duration", unit: "ms", description: "Indexing operation duration");
    
    // Database metrics
    public static readonly ObservableGauge<long> IndexSizeBytes = Meter.CreateObservableGauge(
        "index.size", unit: "bytes", description: "Search index size",
        observeValue: () => GetIndexSize());
        
    public static readonly ObservableGauge<long> IndexedEmailCount = Meter.CreateObservableGauge(
        "index.emails", unit: "emails", description: "Total indexed emails",
        observeValue: () => GetIndexedEmailCount());
}
```

### Graceful Degradation

```csharp
// DirectoryResolver.cs
public static class DirectoryResolver
{
    /// <summary>
    /// Resolves telemetry output directory following XDG Base Directory Specification.
    /// Returns null if no writable location is available - telemetry will be disabled.
    /// </summary>
    public static string? ResolveTelemetryDirectory(string appName)
    {
        var candidates = new List<string>();
        
        // 1. XDG_DATA_HOME (Linux/macOS)
        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgData))
        {
            candidates.Add(Path.Combine(xdgData, appName, "telemetry"));
        }
        
        // 2. Platform-specific local data
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localData))
        {
            candidates.Add(Path.Combine(localData, appName, "telemetry"));
        }
        
        // 3. XDG_STATE_HOME
        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrEmpty(xdgState))
        {
            candidates.Add(Path.Combine(xdgState, appName, "telemetry"));
        }
        
        // 4. Home directory fallback
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            candidates.Add(Path.Combine(home, ".local", "state", appName, "telemetry"));
        }
        
        // 5. Current directory with timestamp (last resort)
        candidates.Add(Path.Combine(
            Environment.CurrentDirectory, 
            $"telemetry_{DateTime.UtcNow:yyyyMMdd_HHmmss}"));
        
        // Try each candidate
        foreach (var candidate in candidates)
        {
            if (TryCreateWritableDirectory(candidate))
            {
                return candidate;
            }
        }
        
        // No writable location found - telemetry disabled, but app continues
        return null;
    }
    
    private static bool TryCreateWritableDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            
            // Verify writability
            var testFile = Path.Combine(path, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

---

## 10. Configuration Management

### Configuration Files

```json
// appsettings.json
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
```

### Configuration Classes

```csharp
// SearchConfiguration.cs
public sealed class SearchConfiguration
{
    public const string SectionName = "Search";
    
    public int DefaultResultLimit { get; set; } = 100;
    public int MaxResultLimit { get; set; } = 1000;
    public int SnippetLength { get; set; } = 200;
    public bool EnableContentSearch { get; set; } = true;
}

// IndexConfiguration.cs
public sealed class IndexConfiguration
{
    public const string SectionName = "Indexing";
    
    public int BatchSize { get; set; } = 500;
    public bool ContentIndexingEnabled { get; set; } = true;
    public int ParallelismDegree { get; set; } = 4;
}

// ArchiveConfiguration.cs
public sealed class ArchiveConfiguration
{
    public const string SectionName = "Archive";
    
    public string BasePath { get; set; } = "";
    public bool AutoDetect { get; set; } = true;
    
    /// <summary>
    /// Resolves the archive path, attempting auto-detection if configured.
    /// </summary>
    public string ResolveArchivePath()
    {
        if (!string.IsNullOrEmpty(BasePath))
        {
            return Path.GetFullPath(BasePath);
        }
        
        if (AutoDetect)
        {
            // Try common locations
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EmailArchive"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EmailArchive"),
                "EmailArchive"
            };
            
            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate) && 
                    Directory.GetFiles(candidate, "*.meta.json", SearchOption.AllDirectories).Any())
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }
        
        throw new InvalidOperationException(
            "Archive path not configured and auto-detection failed. " +
            "Set Archive:BasePath in configuration or use --archive option.");
    }
}
```

---

## 11. Error Handling & Resilience

### Exception Hierarchy

```csharp
// Exceptions.cs
public class SearchException : Exception
{
    public SearchException(string message) : base(message) { }
    public SearchException(string message, Exception inner) : base(message, inner) { }
}

public class QueryParseException : SearchException
{
    public string QueryString { get; }
    public int Position { get; }
    
    public QueryParseException(string queryString, int position, string message)
        : base($"Invalid query at position {position}: {message}")
    {
        QueryString = queryString;
        Position = position;
    }
}

public class IndexCorruptionException : SearchException
{
    public string DatabasePath { get; }
    
    public IndexCorruptionException(string databasePath, Exception inner)
        : base($"Search index is corrupted: {databasePath}", inner)
    {
        DatabasePath = databasePath;
    }
}

public class ArchiveNotFoundException : SearchException
{
    public string ArchivePath { get; }
    
    public ArchiveNotFoundException(string archivePath)
        : base($"Email archive not found: {archivePath}")
    {
        ArchivePath = archivePath;
    }
}
```

### Retry Policies

```csharp
// ResiliencePolicies.cs
public static class ResiliencePolicies
{
    /// <summary>
    /// Retry policy for transient SQLite errors (busy, locked).
    /// </summary>
    public static readonly AsyncRetryPolicy DatabaseRetry = Policy
        .Handle<SqliteException>(ex => 
            ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_BUSY ||
            ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_LOCKED)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
            onRetry: (ex, delay, attempt, _) =>
            {
                // Log retry attempt
            });
    
    /// <summary>
    /// Retry policy for file I/O operations.
    /// </summary>
    public static readonly AsyncRetryPolicy FileRetry = Policy
        .Handle<IOException>()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(50 * attempt));
}
```

### Self-Healing Index

```csharp
// SearchDatabase.cs (partial)
public async Task InitializeAsync(CancellationToken ct)
{
    try
    {
        await OpenAndVerifyAsync(ct);
    }
    catch (SqliteException ex) when (IsCorruptionError(ex))
    {
        _logger.LogError(ex, "Database corruption detected, initiating recovery");
        await RecoverAsync(ct);
    }
}

private async Task RecoverAsync(CancellationToken ct)
{
    // 1. Close existing connection
    await DisposeAsync();
    
    // 2. Backup corrupt database
    if (File.Exists(_databasePath))
    {
        var backupPath = $"{_databasePath}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}";
        File.Move(_databasePath, backupPath);
        _logger.LogWarning("Backed up corrupt database to {Path}", backupPath);
    }
    
    // 3. Create fresh database
    await OpenAndMigrateAsync(ct);
    
    _logger.LogInformation("Database recovered. Index will need to be rebuilt.");
}

private static bool IsCorruptionError(SqliteException ex)
{
    return ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_CORRUPT ||
           ex.SqliteErrorCode == SQLitePCL.raw.SQLITE_NOTADB ||
           ex.Message.