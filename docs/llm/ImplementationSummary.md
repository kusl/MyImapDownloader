# OpenTelemetry Implementation Summary

## New/Modified Files

```
MyImapDownloader/
├── appsettings.json                          # NEW - Configuration file
├── Directory.Packages.props                  # MODIFIED - Added OTel packages
├── MyImapDownloader/
│   ├── MyImapDownloader.csproj               # MODIFIED - Added package refs
│   ├── Program.cs                            # MODIFIED - Added telemetry setup
│   ├── EmailDownloadService.cs               # MODIFIED - Added instrumentation
│   ├── EmailStorageService.cs                # MODIFIED - Added instrumentation
│   └── Telemetry/                            # NEW - Directory
│       ├── TelemetryConfiguration.cs         # NEW - Config model
│       ├── DiagnosticsConfig.cs              # NEW - Metrics & ActivitySource
│       ├── TelemetryExtensions.cs            # NEW - DI setup
│       ├── JsonTelemetryFileWriter.cs        # NEW - File writer
│       ├── JsonFileTraceExporter.cs          # NEW - Trace exporter
│       ├── JsonFileMetricsExporter.cs        # NEW - Metrics exporter
│       └── JsonFileLogExporter.cs            # NEW - Log exporter
```

## Telemetry Output Structure

```
telemetry/
├── traces/
│   ├── traces_2025-12-18_0001.jsonl
│   ├── traces_2025-12-18_0002.jsonl  # New file when size > 25MB
│   └── ...
├── metrics/
│   ├── metrics_2025-12-18_0001.jsonl
│   └── ...
└── logs/
    ├── logs_2025-12-18_0001.jsonl
    └── ...
```

## Configuration (appsettings.json)

| Setting | Default | Description |
|---------|---------|-------------|
| `ServiceName` | MyImapDownloader | Service identifier |
| `ServiceVersion` | 1.0.0 | Version tag |
| `OutputDirectory` | telemetry | Base output path |
| `MaxFileSizeMB` | 25 | Max file size before rotation |
| `EnableTracing` | true | Enable trace export |
| `EnableMetrics` | true | Enable metrics export |
| `EnableLogging` | true | Enable log export |
| `FlushIntervalSeconds` | 5 | Buffer flush interval |
| `MetricsExportIntervalSeconds` | 15 | Metrics collection interval |

## Metrics Collected

### Counters
- `emails.downloaded` - Total emails successfully downloaded
- `emails.skipped` - Duplicate emails skipped
- `emails.errors` - Download errors
- `bytes.downloaded` - Total bytes downloaded
- `folders.processed` - Folders processed
- `connection.attempts` - IMAP connection attempts
- `retry.attempts` - Retry operations
- `storage.files.written` - Files written to disk
- `storage.bytes.written` - Bytes written to disk
- `storage.duplicates.detected` - Duplicates detected at storage

### Histograms
- `email.download.duration` - Per-email download time (ms)
- `folder.processing.duration` - Folder processing time (ms)
- `batch.processing.duration` - Batch processing time (ms)
- `email.size` - Email sizes (bytes)
- `storage.write.latency` - Disk write latency (ms)

### Gauges
- `connections.active` - Current active connections
- `emails.queued` - Emails pending in queue
- `emails.total.session` - Total emails this session

## Traces (Spans)

- `EmailArchiveSession` - Root span for entire session
- `DownloadEmails` - Main download operation
- `ConnectAndAuthenticate` - IMAP connection
- `GetAllFolders` - Folder enumeration
- `DownloadFolder` - Per-folder processing
- `DownloadBatch` - Batch processing
- `DownloadEmail` - Individual email download
- `StoreEmail` - Storage operation
- `LoadIndex` / `SaveIndex` / `RebuildIndex` - Index operations
- `Disconnect` - Connection cleanup
- `CircuitBreakerOpened` / `CircuitBreakerReset` - Resilience events

## JSON Line Format (JSONL)

Each line is a complete, valid JSON object:

```json
{"type":"trace","timestamp":"2025-12-18T13:30:00Z","traceId":"abc123","spanId":"def456",...}
{"type":"metric","timestamp":"2025-12-18T13:30:00Z","metricName":"emails.downloaded",...}
{"type":"log","timestamp":"2025-12-18T13:30:00Z","logLevel":"Information",...}
```

This format allows:
- Easy parsing (one JSON per line)
- Streaming processing
- Efficient file appending
- Compatible with log aggregation tools
