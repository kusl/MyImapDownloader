Index
Last updated
Oct 2, 2026
Summary
myimapdownloader-linux

Overview
Last updated
Nov 15, 2026
Summary
Purpose & architecture

Details
Kushal is building and maintaining a personal email archiving and search system in .NET 10: two CLI apps plus a shared core library
Goals: reliable, safe archiving (read-only IMAP, never deletes), fast full-text search over a large, growing archive (35GB+), production-quality code with high test coverage and observability
Runs on Fedora Linux; self-contained binaries deployed to /opt via install.sh, symlinked in /usr/local/bin
MyImapDownloader — downloads emails from IMAP servers; the original application
MyImapDownloader.Core — shared infrastructure extracted from both apps (telemetry, path resolution, email metadata models, SQLite helpers)
MyEmailSearch — CLI search tool over archived .eml files using SQLite FTS5
Each project has a corresponding *.Tests project; solution format is .slnx
Architecture principles: read-only IMAP, atomic writes for crash safety (tmp/ → cur/), delta sync with UIDVALIDITY-based dedup, XDG-compliant directory resolution, OpenTelemetry instrumentation (JSON/JSONL file exporters), SQLite with WAL mode and FTS5
Archives multiple accounts across mailo.com, disroot.org, and gmx.com, in separate subdirectories under ~/Documents/mail/
Deletion policy: never deletes downloaded emails; IMAP folders open in FolderAccess.ReadOnly; only tmp/ cleanup on failed atomic writes occurs locally
Tools & stack
.NET 10, C#, central package management (Directory.Packages.props, Directory.Build.props)
SQLite with FTS5 (Porter stemming), WAL mode; Microsoft.Data.Sqlite
MailKit / MimeKit for IMAP and email parsing
System.CommandLine 2.0.x (stable GA) for MyEmailSearch; CommandLineParser 2.9.1 for MyImapDownloader
OpenTelemetry with custom JSON/JSONL file exporters; Polly for retry and circuit breaker
TUnit for testing, NSubstitute for mocking, AwesomeAssertions for assertions
Authenticated GitHub REST API used for advisory and package version lookups during security triage
Current state
Stable, verified: clean builds, all tests passing, all previously identified bugs confirmed resolved after multiple review/refactor cycles
Fixed and test-covered: TelemetryConfiguration type mismatch, JsonTelemetryFileWriter disposal ordering, SearchEngine TotalCount bug, SnippetGenerator static/instance confusion, QueryParser before: date semantics, FTS5 escaping
NuGet vulnerability NU1903 (SQLitePCLRaw lib.e_sqlite3 ≤ 2.1.11) resolved by upgrading to SQLitePCLRaw.bundle_e_sqlite3 3.0.5 across all projects
Non-blocking tech debt: telemetry duplication between Core and main app with diverged trace/metrics exporters; EmailMetadata naming collision across three namespaces; TestLogger in a production assembly; SearchDatabase.cs large enough to split; Dapper and Microsoft.Data.SqlClient appear unused in Directory.Packages.props
Asked to reimagine the whole project for .NET 11 / C# 15 (previously .NET 10) with all packages and actions upgraded; hard constraints: existing archive file structure preserved, no existing emails ever deleted, search caches rebuilt only with a valid justification

Ways Of Working
Last updated
Nov 15, 2026
Summary
How Kushal wants Claude to work on the email archiver — dump.txt as source of truth, delivery format, change discipline, technical learnings

Details
Workflow
dump.txt (full codebase export) is the authoritative reference; read and cross-reference it before making any claims or changes
Verify before reporting: check actual source and build output before claiming something is broken; if it compiles and tests pass, find the reason; intentional design changes are not bugs
Deliver complete copy-pasteable file contents or single shell scripts (often using Python for precise text manipulation), not fragmented diffs or partial snippets
Conservative scope: fix only confirmed defects, document intentional non-changes with rationale, avoid regressions; flag non-blocking issues but leave them unless Kushal decides to address them
After any fix, check build output and test results; Kushal shares terminal output and Claude diagnoses from it
Before declaring the codebase correct, cross-reference past conversations against current dump.txt to confirm every historical bug is resolved
FOSS dependencies only, exceptions only when no viable alternative; GitHub Actions use only GitHub's primitive actions, no third-party marketplace actions
"Code is the means to an end" — delete unused code rather than preserving it speculatively
Technical learnings
In Central Package Management a bare <PackageVersion> cannot override transitive dependencies; an explicit <PackageReference> in the appropriate project is required
SQLitePCLRaw 3.x wrapper version (e.g. 3.0.5) is distinct from native SQLite sub-package version (3.53.x); the bundle package removes vulnerable lib.e_sqlite3 from the graph
System.CommandLine 2.0.x: SetAction (not SetHandler), Options.Add() (not AddGlobalOption unless truly global), ParseResult-based handlers, property init for Description/DefaultValueFactory; collection expressions need C# 12+, new[] { } is safer
TUnit uses --disable-logo (not --nologo); needs <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport> and global.json runner config
MailKit 4.x: ImapClient.Inbox and IMessageSummary.Envelope have updated nullable annotations; fix cascade warnings by hoisting into local variables, not inline null checks
Delivery: under ~5 changed files → full path plus FULL file each; many files → one tar.gz with unambiguous placement, containing only changed files, deletions called out explicitly, no bespoke sed/patch scripts, docs updated alongside code, low verbosity, no code comments
