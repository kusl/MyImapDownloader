# .NET 11 rewrite

## Compatibility

| Area | Status |
|---|---|
| Archive layout, `.eml` names, `.meta.json` format | Unchanged. Naming was fuzzed against the previous implementation (200,000 inputs, no differences) and sidecars are byte-identical. |
| `index.v1.db` | Unchanged schema, opened in place. |
| Search index `search.db` | Reused as is. No rebuild. One index (`idx_emails_from_nocase`) is added on first open. |
| Telemetry JSONL | Same directories, file names and record shapes. |
| CLI options, exit codes, `--format json` shape | Unchanged, plus the additions below. |
| Release asset names and `install.sh` | Unchanged. |

## Behaviour changes

- `--start-date` / `--end-date` runs no longer read or advance `LastUid`. Previously a windowed run moved the cursor past older mail, which later full runs then skipped.
- A crash between moving an `.eml` into `cur/` and recording it no longer produces a `_1` duplicate on the next run; the identical file is adopted. Distinct content is never dropped (the old code gave up after 10 name attempts).
- Message and sidecar are flushed to disk before the rename. The sidecar is written atomically.
- Index recovery only runs for corrupt or non-SQLite files, not for every SQLite error such as a locked database.
- A message whose headers MimeKit cannot parse is still archived. MimeKit 5 rejects headers above 16 KB by default; the limit is raised to 1 MB and a failure only blanks the sidecar header fields.
- `--password` may be omitted when `MYIMAPDOWNLOADER_PASSWORD` is set.
- The circuit breaker is gone. With one sequential caller behind an unbounded backoff retry it never changed the outcome.
- Search: `from:` is case-insensitive, terms are matched only at word starts, quoted values may contain other terms, `--format` rejects unknown values, snippets fall back to the preview when content was not indexed, per-search log lines moved to debug level.
- `rebuild` drops and recreates the tables instead of deleting row by row.

## Platform

- `net11.0`, C# 15. `global.json` pins `11.0.100-rc.3` with `rollForward: latestFeature`, so the local RC3 SDK and the GA SDK on CI both resolve.
- C# 15: `union StoreOutcome(Stored, Duplicate)`, collection expression arguments (`[with(capacity: ...)]`). C# 14: extension blocks.
- `Activity.AddException`, `Convert.ToHexStringLower`, `System.Threading.Lock`, `TimeProvider`, the built-in `System.Runtime` meter.
- Runtime async is still a preview opt-in in .NET 11 and is not enabled.

## Packages

| Package | Change |
|---|---|
| Microsoft.Data.Sqlite, Microsoft.Extensions.* | 10.0.14 → 11.0.0 |
| MailKit | 4.18.2 → 5.0.0 |
| MimeKit | replaced by MimeKit.Core 5.0.0 (no BouncyCastle) |
| System.CommandLine | 2.0.14 → 3.0.0 |
| OpenTelemetry | 1.20.1 → 1.21.0 |
| Polly | replaced by Polly.Core 8.9.1 |
| TUnit | 1.81.10 → 1.85.0 |
| Microsoft.Extensions.Hosting, OpenTelemetry.Extensions.Hosting | removed; providers and logging are composed directly |
| OpenTelemetry.Instrumentation.Runtime | removed; `System.Runtime` meter is built in |
| Microsoft.NET.Test.Sdk | removed; TUnit documents it as incompatible |
| NSubstitute, AwesomeAssertions | removed; unused or replaced by TUnit assertions |

GitHub Actions were already on the latest majors (`checkout@v7`, `setup-dotnet@v6`, `upload-artifact@v7`). `ci.yml` now also publishes the rolling release, so `release.yml` is removed and `main` no longer runs the test matrix twice.

## Files to delete

```bash
git rm .github/workflows/release.yml Directory.Build.targets
git rm -r MyImapDownloader.Core/Configuration MyImapDownloader.Core/Infrastructure
git rm MyImapDownloader.Core/Telemetry/{DiagnosticsConfig,JsonFileLogExporter,JsonFileMetricsExporter,JsonFileTraceExporter,JsonTelemetryFileWriter,TelemetryConfiguration,TelemetryDirectoryResolver,TelemetryExtensions,TelemetryJsonContext,TelemetryTagFormatter}.cs
git rm -r MyImapDownloader.Core.Tests/Configuration MyImapDownloader.Core.Tests/Infrastructure MyImapDownloader.Core.Tests/Telemetry
git rm MyImapDownloader/{EmailDownloadException,EmailDownloadService,EmailMetadata,EmailMetadataJsonContext,EmailStorageService,FolderCheckpointTracker,ImapConfiguration,ImapConnectionFailure}.cs
git rm MyImapDownloader/Telemetry/{ActivityExtension,DiagnosticsConfig,TelemetryExtensions}.cs
git rm -r MyImapDownloader.Tests/Services MyImapDownloader.Tests/Telemetry
git rm MyImapDownloader.Tests/{DownloadOptionsTests,EmailDownloadExceptionTests,EmailStorageSanitizationTests,EmailStorageServiceNoMessageIdTests,EmailStorageServiceParsingTests,EmailStorageServiceTests,ImapConfigurationTests,ImapConnectionFailureTests,NormalizeMessageIdTests,TempDirectory,TestLogger}.cs
git rm -r MyEmailSearch/Configuration/PathResolver.cs MyEmailSearch/Data/DatabaseStatistics.cs MyEmailSearch/Data/IndexStatistics.cs
git rm -r MyEmailSearch.Tests/Configuration MyEmailSearch.Tests/Data MyEmailSearch.Tests/Indexing MyEmailSearch.Tests/Search MyEmailSearch.Tests/TestFixtures MyEmailSearch.Tests/SmokeTests.cs
```
