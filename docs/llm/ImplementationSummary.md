# Implementation Summary: MyImapDownloader Architecture

This document provides a technical overview of the MyImapDownloader architecture, covering the SQLite-backed indexing system, delta sync logic, storage patterns, resilience mechanisms, and OpenTelemetry instrumentation.

## 1. Core Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         Program.cs                              │
│  (CLI parsing, DI setup, root activity span)                   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    EmailDownloadService                         │
│  • Polly retry/circuit breaker policies                        │
│  • IMAP connection management                                   │
│  • Folder enumeration and delta sync orchestration             │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    EmailStorageService                          │
│  • SQLite index management (Messages + SyncState tables)       │
│  • Atomic file writes (tmp → cur pattern)                      │
│  • Self-healing database recovery                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      File System                                │
│  • Maildir-style structure (cur/new/tmp)                       │
│  • .eml files + .meta.json sidecars                            │
│  • index.v1.db SQLite database                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Principles

1. **Read-Only IMAP Access**: Folders are opened with `FolderAccess.ReadOnly`—no server modifications ever
2. **Append-Only Local Storage**: Emails are never deleted or modified locally
3. **Crash Safety**: Atomic write pattern prevents partial file corruption
4. **Self-Healing**: Database corruption triggers automatic rebuild from sidecar files

## 2. Delta Sync Implementation

### UID-Based Synchronization

The system tracks sync state per folder using two values:

| Field | Purpose |
|-------|---------|
| `LastUid` | Highest successfully archived UID in this folder |
| `UidValidity` | Server-assigned folder version; if it changes, UIDs are invalid |

### Sync Algorithm

```csharp
// 1. Load checkpoint from SQLite
long lastUid = await _storage.GetLastUidAsync(folder.FullName, folder.UidValidity, ct);

// 2. Build targeted search query
var query = SearchQuery.All;
if (lastUid > 0)
{
    var range = new UniqueIdRange(new UniqueId((uint)lastUid + 1), UniqueId.MaxValue);
    query = SearchQuery.Uids(range);
}

// 3. Execute server-side search (returns only new UIDs)
var uids = await folder.SearchAsync(query, ct);

// 4. Process in batches, updating checkpoint after each
foreach (var batch in uids.Chunk(50))
{
    long maxUid = await DownloadBatchAsync(folder, batch, ct);
    await _storage.UpdateLastUidAsync(folder.FullName, maxUid, folder.UidValidity, ct);
}
```

### UIDVALIDITY Handling

When the server's `UidValidity` changes (indicating folder reconstruction):

1. The stored `LastUid` becomes invalid
2. System resets to `LastUid = 0`
3. Full folder re-scan occurs
4. Existing emails are skipped via Message-ID deduplication

## 3. Storage Layer

### SQLite Schema

```sql
-- Deduplication index
CREATE TABLE Messages (
    MessageId TEXT PRIMARY KEY,    -- Normalized Message-ID header
    Folder TEXT NOT NULL,          -- Source folder name
    ImportedAt TEXT NOT NULL       -- ISO 8601 timestamp
);

-- Sync state tracking
CREATE TABLE SyncState (
    Folder TEXT PRIMARY KEY,
    LastUid INTEGER NOT NULL,
    UidValidity INTEGER NOT NULL
);

-- Performance index
CREATE INDEX IX_Messages_Folder ON Messages(Folder);
```

### Database Configuration

```csharp
// WAL mode for concurrent reads during writes
cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
```

### Atomic Write Pattern

```
Network Stream → tmp/{timestamp}.{guid}.tmp
                          │
                          ▼ (parse headers, extract metadata)
                          │
                          ▼ File.Move()
               cur/{timestamp}.{messageId}.{hostname}:2,S.eml
                          │
                          ▼
               cur/{...}.eml.meta.json
                          │
                          ▼
               INSERT INTO Messages (...)
```

### Self-Healing Recovery

When database corruption is detected:

```csharp
private async Task RecoverDatabaseAsync(CancellationToken ct)
{
    // 1. Backup corrupt database
    File.Move(_dbPath, _dbPath + $".corrupt.{DateTime.UtcNow.Ticks}");
    
    // 2. Create fresh database
    await OpenAndMigrateAsync(ct);
    
    // 3. Rebuild from disk (sidecar files are source of truth)
    foreach (var metaFile in Directory.EnumerateFiles(_baseDirectory, "*.meta.json", SearchOption.AllDirectories))
    {
        var meta = JsonSerializer.Deserialize<EmailMetadata>(await File.ReadAllTextAsync(metaFile));
        await InsertMessageRecordAsync(meta.MessageId, meta.Folder, ct);
    }
}
```

## 4. Resilience Patterns

### Retry Policy

```csharp
_retryPolicy = Policy
    .Handle<Exception>(ex => ex is not AuthenticationException)
    .WaitAndRetryForeverAsync(
        retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 300)),
        // Exponential backoff: 2s, 4s, 8s, 16s, ... capped at 5 minutes
        (exception, retryCount, timeSpan) => {
            _logger.LogWarning("Retry {Count} in {Delay}: {Message}", 
                retryCount, timeSpan, exception.Message);
        });
```

### Circuit Breaker

```csharp
_circuitBreakerPolicy = Policy
    .Handle<Exception>(ex => ex is not AuthenticationException)
    .CircuitBreakerAsync(
        exceptionsAllowedBeforeBreaking: 5,
        durationOfBreak: TimeSpan.FromMinutes(2));
```

### Policy Composition

```csharp
var policy = Policy.WrapAsync(_retryPolicy, _circuitBreakerPolicy);
await policy.ExecuteAsync(async () => {
    // Entire IMAP session wrapped in resilience policies
    using var client = new ImapClient();
    await ConnectAndAuthenticateAsync(client, ct);
    // ... process folders
});
```

## 5. OpenTelemetry Implementation

### Telemetry Components

| Component | File | Responsibility |
|-----------|------|----------------|
| Activity Source | `DiagnosticsConfig.cs` | Creates trace spans |
| Meter | `DiagnosticsConfig.cs` | Creates metrics instruments |
| File Writer | `JsonTelemetryFileWriter.cs` | Thread-safe JSONL output with rotation |
| Trace Exporter | `JsonFileTraceExporter.cs` | Exports Activity spans |
| Metrics Exporter | `JsonFileMetricsExporter.cs` | Exports metric data points |
| Log Exporter | `JsonFileLogExporter.cs` | Exports structured logs |
| Directory Resolver | `TelemetryDirectoryResolver.cs` | XDG-compliant path resolution |

### Instrumentation Points

```csharp
// Root span (Program.cs)
using var rootActivity = DiagnosticsConfig.ActivitySource.StartActivity(
    "EmailArchiveSession", ActivityKind.Server);

// Folder processing span
using var activity = DiagnosticsConfig.ActivitySource.StartActivity("ProcessFolder");
activity?.SetTag("folder", folder.FullName);

// Storage metrics
FilesWritten.Add(1);
BytesWritten.Add(bytesWritten);
WriteLatency.Record(sw.Elapsed.TotalMilliseconds);
```

### Defined Metrics

| Metric Name | Type | Unit | Description |
|-------------|------|------|-------------|
| `emails.downloaded` | Counter | emails | Successfully downloaded count |
| `emails.skipped` | Counter | emails | Duplicates skipped |
| `emails.errors` | Counter | errors | Download failures |
| `storage.files.written` | Counter | files | Files written to disk |
| `storage.bytes.written` | Counter | bytes | Total bytes written |
| `storage.write.latency` | Histogram | ms | Write operation duration |
| `connections.active` | Gauge | connections | Active IMAP connections |

### Output Format (JSONL)

```jsonl
{"type":"trace","timestamp":"2025-12-24T12:00:00Z","traceId":"abc123","spanId":"def456","operationName":"ProcessFolder","durationMs":1234.5}
{"type":"metric","timestamp":"2025-12-24T12:00:00Z","metricName":"storage.files.written","value":{"longValue":42}}
{"type":"log","timestamp":"2025-12-24T12:00:00Z","logLevel":"Information","formattedMessage":"Downloaded: Re: Hello World"}
```

### File Rotation

- Daily rotation: New file each day
- Size-based rotation: New file when exceeding `MaxFileSizeMB` (default: 25 MB)
- Naming pattern: `{type}_{date}_{sequence}.jsonl`

## 6. Dependency Management

### Central Package Management

All package versions are defined in `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="MailKit" Version="4.14.1" />
    <PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.1" />
    <PackageVersion Include="Polly" Version="8.6.5" />
    <PackageVersion Include="OpenTelemetry" Version="1.14.0" />
    <!-- ... -->
  </ItemGroup>
</Project>
```

### Project References

Individual `.csproj` files reference packages without versions:

```xml
<PackageReference Include="MailKit" />
<PackageReference Include="Microsoft.Data.Sqlite" />
```

## 7. Testing Infrastructure

### Framework: TUnit

- Modern .NET testing framework with source-generated test discovery
- Integrated with Microsoft.Testing.Platform for .NET 10 compatibility

### Test Categories

| Category | Coverage |
|----------|----------|
| Configuration | `DownloadOptions`, `ImapConfiguration`, `TelemetryConfiguration` |
| Telemetry | All exporters, file writer, directory resolver |
| Core Logic | Exception handling, extension methods |

### Running Tests

```bash
# Via dotnet test (requires global.json configuration)
dotnet test

# Via direct execution
dotnet run --project MyImapDownloader.Tests
```

### .NET 10 Compatibility

The project uses Microsoft.Testing.Platform mode, configured in `global.json`:

```json
{
    "test": {
        "runner": "Microsoft.Testing.Platform"
    }
}
```

## 8. Build & CI

### GitHub Actions Workflow

```yaml
jobs:
  build-and-test:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    steps:
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet restore
      - run: dotnet build --configuration Release
      - run: dotnet test --configuration Release
      - run: dotnet publish --configuration Release
```

### Shared Build Properties

`Directory.Build.props`:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <LangVersion>latest</LangVersion>
</PropertyGroup>
```

## 9. Future Considerations

### Potential Enhancements

1. **OAuth2 Authentication**: Replace app passwords with proper OAuth2 flow for Gmail/Outlook
2. **IDLE Push**: Real-time sync using IMAP IDLE command
3. **Attachment Extraction**: Option to save attachments separately with index
4. **Full-Text Search**: SQLite FTS5 extension for searchable archive
5. **Configuration File**: YAML-based configuration alongside CLI arguments
6. **Incremental CONDSTORE**: Use MODSEQ for even more efficient delta detection

### Performance Optimizations

1. **Parallel Folder Processing**: Download multiple folders concurrently
2. **Connection Pooling**: Reuse IMAP connections across folders
3. **Batch Inserts**: Group SQLite inserts in transactions
4. **Memory-Mapped Index**: For very large archives (millions of emails)
