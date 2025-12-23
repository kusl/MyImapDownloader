# Implementation Summary: SQLite Indexing & Observability

This document summarizes the transition from a stateless file-system search to a persistent **SQLite-backed indexing system** and the integration of a comprehensive **OpenTelemetry** pipeline.

## 1. Core Architectural Changes

### High-Performance Delta Sync

* 
**UID Tracking**: The system now records `LastUid` and `UidValidity` for every folder in a local SQLite database (`index.v1.db`).


* 
**Targeted Fetching**: Subsequent runs perform a server-side search for UIDs strictly greater than the last successfully archived message, drastically reducing network overhead.


* 
**Batch Processing**: Downloads are executed in batches (50 messages) with checkpoints updated in the database after each successful batch.



### SQLite Message Index

* 
**Deduplication**: A `Messages` table serves as the primary index for `MessageId` values, allowing O(1) duplicate checks before attempting a network fetch.


* 
**Self-Healing Recovery**: If database corruption is detected, the system automatically relocates the corrupt file and rebuilds the entire SQLite index by scanning the `.meta.json` sidecar files on disk.


* 
**WAL Mode**: The database is configured with **Write-Ahead Logging (WAL)** to support better concurrency and resilience during high-throughput storage operations.



---

## 2. OpenTelemetry Implementation

The application now features a native OpenTelemetry provider that exports data to **JSON Lines (JSONL)** files for distributed tracing, metrics, and structured logging.

### New Telemetry Components

| File | Responsibility |
| --- | --- |
| `DiagnosticsConfig.cs` | Centralized `ActivitySource` and `Meter` definitions.

 |
| `JsonTelemetryFileWriter.cs` | Handles thread-safe, rotating file writes for JSON telemetry data.

 |
| `TelemetryExtensions.cs` | DI setup for registering OTel providers and local file exporters.

 |
| `ActivityExtension.cs` | Helper methods for enriching spans with exception data and tags.

 |

### Instrumentation Spans (Traces)

* 
**`EmailArchiveSession`**: The root span tracking the entire application lifecycle.


* 
**`DownloadEmails`**: Tracks the overall IMAP connection and folder enumeration.


* 
**`ProcessFolder`**: Captures delta sync calculations and batching logic per folder.


* 
**`SaveStream`**: High-resolution span covering the atomic write pattern, header parsing, and sidecar creation.


* 
**`RebuildIndex`**: Spans the recovery operation when reconstructing the database from disk.



### Key Performance Metrics

* 
**`storage.files.written`**: Counter for the total number of `.eml` files successfully archived.


* 
**`storage.bytes.written`**: Counter tracking the cumulative disk usage of archived messages.


* 
**`storage.write.latency`**: Histogram recording the total time (ms) spent on disk I/O and metadata serialization.



---

## 3. Storage & Reliability Patterns

### Atomic Write Pattern

To prevent partial file corruption, the `EmailStorageService` now implements a strict **TMP-to-CUR** move pattern:

1. Stream the network response directly to a `.tmp` file in the `tmp/` subdirectory.


2. Parse headers from the local file (using **MimeKit**) to generate the `.meta.json` sidecar.


3. Perform an atomic `File.Move` to the final `cur/` destination.



### Resilience via Polly

* 
**Retry Policy**: Exponential backoff (up to 5 minutes) handles transient network failures.


* 
**Circuit Breaker**: Automatically halts operations for 2 minutes if 5 consecutive authentication or connection failures occur to protect against account lockouts.



### Centralized Package Management

The project has moved to `Directory.Packages.props`, utilizing **Central Package Management (CPM)** to ensure version consistency across the main application and the new telemetry test suites.





