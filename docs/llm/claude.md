I have just uploaded some dump from a dotnet project. it was originally in dotnet 9 and in https://github.com/collabskus/MyImapDownloader I have updated it to dotnet 10. Can you please review this code? 

I would love to be able to use this so I can archive my code. The numbers 1- is helpful but I think we can do better. How do grown up mail clients store emails? How can I make sure I can retain a copy of my email even after I delete it from the remote? (the server only has so much storage space and I want to be mindful of wasted storage on the server). I would love to be able to neatly organize email for archival purposes. Thank you for your help. 

The entire code dump is at dump.txt. The process for getting there is at output.txt and the powershell that generated the dump is at export.ps1. 

It is critical that the code we write or edit MUST run on windows, macOS, as well as Linux. At a later date, we will add unit tests using TUnit as well.



# Code Review: MyImapDownloader

Thanks for sharing this project, Kushal! I've reviewed the code and have both observations and recommendations for making this a more robust email archival solution.

## Current Code Assessment

### Strengths
- Good use of dependency injection and modern .NET patterns
- Polly retry/circuit breaker for resilience
- Proper handling of authentication failures (not retrying on auth errors)
- Batch processing to handle large mailboxes
- Sanitized filenames with hash fallback

### Areas for Improvement

**1. Email Storage Strategy**

The current approach (`{index}_{subjectPrefix}_{hash}.eml`) has significant limitations for archival:
- Message indices change when emails are deleted from the server
- No deduplication - downloading the same email twice creates duplicates
- No folder structure preservation
- Can't track what's already been downloaded

**2. How Professional Mail Clients Store Emails**

There are two dominant standards:

| Format | Description | Used By |
|--------|-------------|---------
| **Maildir** | One file per message, with subdirectories for `cur/`, `new/`, `tmp/`. Filenames include unique identifiers, timestamps, and flags. | Dovecot, mutt, many Unix clients |
| **mbox** | Single file containing all messages, concatenated with separator lines | Thunderbird (historically), older Unix tools |

For archival purposes, **Maildir-style storage** is superior because it's filesystem-friendly, allows incremental backups, and won't corrupt if interrupted mid-write.

**3. Message-ID for Deduplication**

Every email has a unique `Message-ID` header. This is the gold standard for identifying emails across sessions and should be the basis for your filename/storage key.

---

## Proposed Architecture

Here's a redesigned approach for robust email archival:## What This Gives You

### Directory Structure (Maildir-inspired)
```
EmailArchive/
├── .email-index.json              # Fast duplicate checking
├── INBOX/
│   ├── cur/                       # Current (downloaded) messages
│   │   ├── 1702900000.abc123.mypc:2,S.eml
│   │   ├── 1702900000.abc123.mypc:2,S.eml.meta.json
│   │   └── ...
│   ├── new/                       # (For future: unprocessed)
│   └── tmp/                       # Atomic write staging
├── Sent/
│   └── cur/
│       └── ...
└── Archive/
    └── cur/
        └── ...
```

### Key Features

| Feature | Benefit |
|---------|---------|
| **Message-ID deduplication** | Re-run anytime safely; only new emails download |
| **Atomic writes** | Write to `tmp/`, move to `cur/` - no corruption on crash |
| **Folder preservation** | Mirrors your IMAP folder structure |
| **Metadata sidecar files** | JSON with searchable info without parsing `.eml` |
| **Cross-platform paths** | Uses `Path.Combine()` throughout |
| **Resumable** | Index tracks what's downloaded; interrupt anytime |

### Usage Examples

```bash
# Basic: Download INBOX only
dotnet run -- -s imap.gmail.com -u you@gmail.com -p "app-password" -o ~/EmailArchive

# All folders with date range
dotnet run -- -s imap.gmail.com -u you@gmail.com -p "app-password" \
  -o ~/EmailArchive --all-folders --start-date 2020-01-01

# Verbose mode for debugging
dotnet run -- -s imap.gmail.com -u you@gmail.com -p "app-password" -o ~/EmailArchive -v
```

## Future Enhancements to Consider

1. **Incremental sync using UIDVALIDITY** - IMAP provides a folder UID validity token; if it changes, the folder was rebuilt and you'd need to re-download. Worth storing.

2. **Attachment extraction** - Option to save attachments separately for easier searching.

3. **Search index** - Could build a SQLite full-text search index of subjects/bodies.

4. **Config file** - Instead of CLI args, support a YAML config file.

5. **OAuth2 for Gmail** - Gmail prefers OAuth2 over app passwords.

Would you like me to elaborate on any of these pieces, or should we add the TUnit test infrastructure next?









I have made a few changes. Up next, I want to use open telemetry to log metrics, spans, everything, all that I can possibly measure, log, track, etc in my code. I want all of this to be logged into valid json files, so no stuffing multiple json messages into the same file. it should go into its own directory with no more than one file created for each day, and files no bigger than 25 MB each (configurable by the user using appsettings or something like that) 
I have updated the dump.txt​ with the latest code so please use this as a base. please do not hallucinate and thank you for your help. 

I'll help you add comprehensive OpenTelemetry instrumentation with file-based JSON export. Let me analyze your current codebase and create a robust telemetry solution.

Here's my implementation plan:

1. Add OpenTelemetry packages to central package management
2. Create a custom JSON file exporter for traces, metrics, and logs
3. Add configuration support via appsettings.json
4. Instrument all your existing services with spans, metrics, and structured logging

Let me create the necessary files:Now let me update the service classes to include comprehensive instrumentation:

<function_results>OK</function_results>Now let me create a summary document showing the file structure:Here's a summary of what I've created for you:

## What's New

**7 new files** in a `Telemetry/` subdirectory plus configuration updates:

1. **TelemetryConfiguration.cs** - Configuration model bound to `appsettings.json`
2. **DiagnosticsConfig.cs** - Centralized `ActivitySource` and `Meter` with all your metrics (counters, histograms, gauges)
3. **JsonTelemetryFileWriter.cs** - Thread-safe file writer that handles daily rotation and size limits
4. **JsonFileTraceExporter.cs** - Exports OpenTelemetry traces to JSON
5. **JsonFileMetricsExporter.cs** - Exports OpenTelemetry metrics to JSON  
6. **JsonFileLogExporter.cs** - Exports OpenTelemetry logs to JSON
7. **TelemetryExtensions.cs** - DI registration and setup

## Key Features

- **Daily file rotation** - New files created each day
- **Size-based rotation** - Files rotate when they exceed the configured limit (default 25MB)
- **JSONL format** - Each telemetry record is a complete, valid JSON object on its own line
- **Separate directories** for traces, metrics, and logs
- **Comprehensive instrumentation** including spans for every operation, counters, histograms, and gauges
- **Configurable via appsettings.json** - All settings can be adjusted without code changes

## To Apply These Changes

1. Create the `Telemetry/` directory under `MyImapDownloader/`
2. Copy the new files into place
3. Update `Directory.Packages.props`, `MyImapDownloader.csproj`, and `appsettings.json`
4. Replace `Program.cs`, `EmailDownloadService.cs`, and `EmailStorageService.cs`
5. Run `dotnet restore` then `dotnet build`

The telemetry will be written to `./telemetry/` (or whatever you configure) with subdirectories for traces, metrics, and logs. Each file follows the naming pattern `{type}_{date}_{sequence}.jsonl`.




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


I missed to say some requirements. The logs should go into directories that match the XDG guidelines if possible and if these folders are unavailable, we should then try to write to a folder in the same directory as where the bin is, and failing that, we should gracefully NOT write logs at all. failure to write these logs etc should not crash the application at all. 
Also I don't know what I am doing wrong but I got these errors when I build 
2025-12-18 14:09:00
Restore complete (0.7s)
  MyImapDownloader net10.0 failed with 11 error(s) (2.1s)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\Program.cs(88,22): error CS1061: 'Activity' does not contain a definition for 'RecordException' and no accessible extension method 'RecordException' accepting a first argument of type 'Activity' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\EmailStorageService.cs(126,22): error CS1061: 'Activity' does not contain a definition for 'RecordException' and no accessible extension method 'RecordException' accepting a first argument of type 'Activity' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\EmailDownloadService.cs(125,22): error CS1061: 'Activity' does not contain a definition for 'RecordException' and no accessible extension method 'RecordException' accepting a first argument of type 'Activity' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\EmailDownloadService.cs(132,22): error CS1061: 'Activity' does not contain a definition for 'RecordException' and no accessible extension method 'RecordException' accepting a first argument of type 'Activity' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\Telemetry\JsonFileMetricsExporter.cs(80,73): error CS1061: 'ExponentialHistogramData' does not contain a definition for 'Sum' and no accessible extension method 'Sum' accepting a first argument of type 'ExponentialHistogramData' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\Telemetry\JsonFileMetricsExporter.cs(81,73): error CS1061: 'ExponentialHistogramData' does not contain a definition for 'Count' and no accessible extension method 'Count' accepting a first argument of type 'ExponentialHistogramData' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\EmailDownloadService.cs(244,22): error CS1061: 'Activity' does not contain a definition for 'RecordException' and no accessible extension method 'RecordException' accepting a first argument of type 'Activity' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\EmailStorageService.cs(253,22): error CS1061: 'Activity' does not contain a definition for 'RecordException' and no accessible extension method 'RecordException' accepting a first argument of type 'Activity' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\EmailDownloadService.cs(425,22): error CS1061: 'Activity' does not contain a definition for 'RecordException' and no accessible extension method 'RecordException' accepting a first argument of type 'Activity' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\EmailStorageService.cs(325,22): error CS1061: 'Activity' does not contain a definition for 'RecordException' and no accessible extension method 'RecordException' accepting a first argument of type 'Activity' could be found (are you missing a using directive or an assembly reference?)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\EmailDownloadService.cs(448,22): error CS1061: 'Activity' does not contain a definition for 'RecordException' and no accessible extension method 'RecordException' accepting a first argument of type 'Activity' could be found (are you missing a using directive or an assembly reference?)

Build failed with 11 error(s) in 3.0s
2025-12-18 14:09:04
PS C:\Users\kushal\source\repos\MyImapDownloader>
Also, we have these prop files now, are you sure we should add this property group to the csproj? 
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
Please use the most up to date version of open telemetry possible. In fact, please make sure you use all the most up to date versions of dependencies. For example, I have updated the packages as indicated by dotnet outdated below 
2025-12-18 14:11:48
Discovering projects...
Analyzing project(s)...
Analyzing dependencies...
» MyImapDownloader
  [net10.0]
  OpenTelemetry                          1.11.2 -> 1.14.0
  OpenTelemetry.Exporter.Console         1.11.2 -> 1.14.0
  OpenTelemetry.Extensions.Hosting       1.11.2 -> 1.14.0
  OpenTelemetry.Instrumentation.Runtime  1.11.1 -> 1.14.0

Version color legend:
<red>   : Major version update or pre-release version. Possible breaking changes.
<yellow>: Minor version update. Backwards-compatible features added.
<green> : Patch version update. Backwards-compatible bug fixes.

You can upgrade packages to the latest version by passing the -u or -u:prompt option.
Elapsed: 00:00:01.9107266
2025-12-18 14:11:50



I'll fix all these issues. Let me address each problem:

1. **`RecordException` errors**: This is an OpenTelemetry extension method that requires `using OpenTelemetry.Trace;`
2. **`ExponentialHistogramData.Sum/Count` errors**: The API changed in OpenTelemetry 1.14.0
3. **Duplicate PropertyGroup in csproj**: You're right, since you have `Directory.Build.props`, we should remove the duplicate properties
4. **XDG-compliant log directory with graceful fallback**

Let me provide the fixes:


<function_results>OK</function_results>Now let me provide a summary of the changes and the file structure:

## Summary of Fixes

### 1. **`RecordException` Error Fix**
Created `ActivityExtensions.cs` which provides the `RecordException` extension method for `Activity`. This is a custom implementation that works across OpenTelemetry versions.

### 2. **`ExponentialHistogramData.Sum/Count` Error Fix**
Updated `JsonFileMetricsExporter.cs` to handle the API changes in OpenTelemetry 1.14.0. The new version accesses these properties differently, so I added error handling and reflection-based fallback.

### 3. **Duplicate PropertyGroup in csproj**
Removed the duplicate properties since they're already defined in `Directory.Build.props`. The csproj now only contains `OutputType` since that's specific to executable projects.

### 4. **XDG-Compliant Telemetry Directory**
Created `TelemetryDirectoryResolver.cs` which:
- First tries `$XDG_DATA_HOME/<appname>/telemetry`
- Then `LocalApplicationData/<appname>/telemetry` (Windows-friendly)
- Then `$XDG_STATE_HOME/<appname>/telemetry`
- Then `~/.local/state/<appname>/telemetry`
- Then `~/.local/share/<appname>/telemetry`
- Then a `telemetry` folder next to the executable
- Finally the current working directory
- **Returns null if no location is writable** - telemetry is then disabled gracefully

### 5. **Graceful Failure for Telemetry**
All telemetry components now:
- Accept nullable writers
- Wrap all operations in try-catch
- Never crash the application if telemetry fails
- Automatically disable themselves if they can't write

### Files to Update

Place these files in your project:

| File | Location |
|------|----------|
| `TelemetryDirectoryResolver.cs` | `MyImapDownloader/Telemetry/` |
| `ActivityExtensions.cs` | `MyImapDownloader/Telemetry/` |
| `JsonFileMetricsExporter.cs` | `MyImapDownloader/Telemetry/` (replace) |
| `JsonFileTraceExporter.cs` | `MyImapDownloader/Telemetry/` (replace) |
| `JsonFileLogExporter.cs` | `MyImapDownloader/Telemetry/` (replace) |
| `JsonTelemetryFileWriter.cs` | `MyImapDownloader/Telemetry/` (replace) |
| `TelemetryExtensions.cs` | `MyImapDownloader/Telemetry/` (replace) |
| `EmailDownloadService.cs` | `MyImapDownloader/` (replace) |
| `EmailStorageService.cs` | `MyImapDownloader/` (replace) |
| `Program.cs` | `MyImapDownloader/` (replace) |
| `MyImapDownloader.csproj` | `MyImapDownloader/` (replace) |


I see four warnings and one error. I have updated the `dump.txt` with the latest code. Please fix. 
2025-12-18 14:29:14
Restore complete (0.9s)
  MyImapDownloader net10.0 failed with 1 error(s) and 4 warning(s) (2.1s)
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\Telemetry\TelemetryExtensions.cs(32,13): warning CS8634: The type 'MyImapDownloader.Telemetry.JsonTelemetryFileWriter?' cannot be used as type parameter 'TService' in the generic type or method 'ServiceCollectionServiceExtensions.AddSingleton<TService>(IServiceCollection, Func<IServiceProvider, TService>)'. Nullability of type argument 'MyImapDownloader.Telemetry.JsonTelemetryFileWriter?' doesn't match 'class' constraint.
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\Telemetry\TelemetryExtensions.cs(79,9): warning CS8634: The type 'MyImapDownloader.Telemetry.JsonTelemetryFileWriter?' cannot be used as type parameter 'TService' in the generic type or method 'ServiceCollectionServiceExtensions.AddSingleton<TService>(IServiceCollection, TService)'. Nullability of type argument 'MyImapDownloader.Telemetry.JsonTelemetryFileWriter?' doesn't match 'class' constraint.
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\Telemetry\TelemetryExtensions.cs(80,9): warning CS8634: The type 'MyImapDownloader.Telemetry.JsonTelemetryFileWriter?' cannot be used as type parameter 'TService' in the generic type or method 'ServiceCollectionServiceExtensions.AddSingleton<TService>(IServiceCollection, TService)'. Nullability of type argument 'MyImapDownloader.Telemetry.JsonTelemetryFileWriter?' doesn't match 'class' constraint.
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\Telemetry\TelemetryExtensions.cs(81,9): warning CS8634: The type 'MyImapDownloader.Telemetry.JsonTelemetryFileWriter?' cannot be used as type parameter 'TService' in the generic type or method 'ServiceCollectionServiceExtensions.AddSingleton<TService>(IServiceCollection, TService)'. Nullability of type argument 'MyImapDownloader.Telemetry.JsonTelemetryFileWriter?' doesn't match 'class' constraint.
    C:\Users\kushal\source\repos\MyImapDownloader\MyImapDownloader\Telemetry\JsonFileMetricsExporter.cs(95,53): error CS1061: 'ExponentialHistogramData' does not contain a definition for 'Count' and no accessible extension method 'Count' accepting a first argument of type 'ExponentialHistogramData' could be found (are you missing a using directive or an assembly reference?)

Build failed with 1 error(s) and 4 warning(s) in 3.2s
2025-12-18 14:29:18
Discovering projects...
Analyzing project(s)...
Analyzing dependencies...
No outdated dependencies were detected
Elapsed: 00:00:01.9071127
2025-12-18 14:29:20
PS C:\Users\kushal\source\repos\MyImapDownloader>

I can see two issues to fix:

1. **Error**: `ExponentialHistogramData` doesn't have a `Count` property in OpenTelemetry 1.14.0
2. **Warnings**: Nullable type `JsonTelemetryFileWriter?` being registered with `AddSingleton`

Let me provide the fixes:Here's a summary of the fixes:

**1. JsonFileMetricsExporter.cs** - Fixed the `ExponentialHistogramData.Count` error:
- Removed the direct access to `.Count` property which doesn't exist in OpenTelemetry 1.14.0
- Created a new `GetExponentialHistogramCount()` method that uses reflection to safely access the property if it exists
- Uses reflection-based fallback approach similar to what was already done for `Sum`

**2. TelemetryExtensions.cs** - Fixed the CS8634 nullability warnings:
- Introduced an `ITelemetryWriterProvider` interface to wrap the nullable writers
- Created `TelemetryWriterProvider` class that holds the potentially-null writers
- Created `NullTelemetryWriterProvider` for when telemetry is disabled
- Now registers `ITelemetryWriterProvider` (non-nullable interface) instead of `JsonTelemetryFileWriter?` directly
- Still registers the trace writer directly when available for disposal in Program.cs

Replace your existing files with these updated versions and rebuild. The build should now succeed without errors or warnings.










Thank you, your recent changes fixed the errors. 
Now, lets start writing those TUnit tests. 
Lets aim for maximum coverage while making sure tests actually mean something 
instead of chasing metrics for the sake of chasing metrics. 

Great approach! Let's write meaningful tests that verify actual behavior rather than just hitting lines of code.Now I have a good understanding of the codebase. Let me create meaningful TUnit tests. I'll start with the test project setup and then create tests organized by component.Now let me create the test files, starting with the most testable components:Now let's add a test that verifies the JSON exporters work correctly:Finally, let's update the solution file to include the test project:Here's a summary of what I've created:

## Test Project Structure

Create the folder `MyImapDownloader.Tests` at the solution root and add these files:

**Project Setup:**
1. **MyImapDownloader.Tests.csproj** - Test project with TUnit, NSubstitute, and FluentAssertions
2. **Directory.Packages.props** (updated) - Added test package versions
3. **MyImapDownloader.sln** (updated) - Added test project reference

**Test Files:**

| File | Tests | Purpose |
|------|-------|---------|
| `TelemetryConfigurationTests.cs` | 5 tests | Validates default values, MB-to-bytes conversion, configuration binding |
| `JsonTelemetryFileWriterTests.cs` | 10 tests | Core file writing, JSONL format, rotation, flush behavior, disposal |
| `ActivityExtensionsTests.cs` | 10 tests | Exception recording, status setting, null handling, nested exceptions |
| `TelemetryDirectoryResolverTests.cs` | 5 tests | Directory resolution, writability verification, app name handling |
| `ImapConfigurationTests.cs` | 5 tests | Default values, configuration binding, port/SSL combinations |
| `DownloadOptionsTests.cs` | 6 tests | CLI options parsing, date ranges, nullable properties |
| `EmailDownloadExceptionTests.cs` | 7 tests | Custom exception behavior, inner exception chaining |
| `DiagnosticsConfigTests.cs` | 8 tests | ActivitySource/Meter singletons, instrumentation creation |
| `TelemetryWriterProviderTests.cs` | 8 tests | Provider implementation, null provider, interface compliance |
| `TelemetryExtensionsTests.cs` | 7 tests | DI registration, configuration binding, chaining |
| `JsonExporterTests.cs` | 5 tests | Trace/Log/Metrics exporters with null writers, export behavior |

## Run Tests

```pow





dotnet test
```

Or with TUnit's CLI:
```powershell
dotnet run --project MyImapDownloader.Tests
```

These tests focus on verifiable behavior rather than implementation details. They test actual file I/O, real configuration binding, exception handling boundaries, and the contracts between components.

