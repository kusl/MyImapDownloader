Manage project memory
Claude regenerates project memory every evening from your past chats in this project. Only you can see this memory, and it is not shared with other project users.

Purpose & context

Kushal is building a personal email archiving and search system on Fedora Linux, deployed on an Acer Swift Go. The system consists of two companion .NET 10 CLI applications:

MyImapDownloader – IMAP email archiver with read-only server access, delta sync, SQLite-backed deduplication, and OpenTelemetry telemetry
MyEmailSearch – full-text search over the archived email corpus using SQLite FTS5
The project lives in a multi-project .NET solution with a shared MyImapDownloader.Core library. Self-contained binaries are deployed via install.sh to /opt/ with symlinks in /usr/local/bin. Email archives are stored under ~/Documents/mail/ with per-account subdirectories, and the archive has grown to 35GB+.

Core values and constraints:

Safety-first: IMAP access is strictly read-only; emails are never deleted or overwritten locally; atomic write patterns (tmp → cur) for crash safety
FOSS only: no proprietary dependencies; banned packages include FluentAssertions, Moq, MassTransit
Minimal third-party dependencies: prefer implementing directly over third-party marketplace actions in CI
Data integrity above all: existing .eml files must never be touched; indices can be rebuilt
Testing stack: TUnit + NSubstitute + AwesomeAssertions. CI uses GitHub Actions with only GitHub's primitive actions.

Current state

The codebase is in a verified, regression-free state with all tests passing and a clean build. Key confirmed-resolved bugs include:

TotalCount pagination bug in SearchEngine (was capped at limit)
JsonTelemetryFileWriter dispose-before-flush ordering
before: date semantics inconsistency in QueryParser
SnippetGenerator static/instance confusion in SearchEngine
Duplicate TelemetryConfiguration.cs causing DI type mismatch
Unused ActivityExtensions in Core (deleted)
FTS5 escaping test expectation mismatch
MailKit 4.x nullable annotation warnings (CS8604/CS8602) in EmailDownloadService.cs
Active known technical debt (non-blocking):

Telemetry code duplication between Core and app layer (diverged trace/metrics exporters)
EmailMetadata naming collision across three namespaces
TestLogger resides in a production assembly
SearchDatabase.cs is large enough to warrant splitting
Unguarded PersonalNamespaces[0] index access
Indefinite Polly retry with no cap
Crash window between .eml and .meta.json writes
Fixed Task.Delay for telemetry flushing in Program.cs
Two CLI libraries currently coexist: CommandLineParser 2.9.1 (MyImapDownloader) and System.CommandLine 2.0.x (MyEmailSearch). Consolidation was explicitly deferred — MyImapDownloader's CLI is simple and working; migration risk outweighs benefit for now.

Key learnings & principles

Verify before reporting: Claude must check actual source (dump.txt) before flagging anything as broken. If code compiles and tests pass, there is a reason — find it rather than assuming a defect.
Conservative change scope: code reviews should fix only confirmed defects; intentional design decisions (visibility changes, renames, init properties) are not bugs.
Document non-changes explicitly: when issues are identified but intentionally left alone to avoid regressions, explain why.
Cascading warning awareness: fixing one nullable warning can introduce new ones on subsequent lines; fixes must account for the full chain (e.g., hoisting item.Envelope into a local variable).
Code is a means to an end: unused code should be deleted, not preserved; test count reductions from deleting dead code are acceptable.
Unused code is a liability: confirmed twice (ActivityExtensions, duplicate TelemetryConfiguration) — delete rather than maintain.
Approach & patterns

dump.txt as source of truth: all reviews and fixes must be grounded in the current codebase export; no speculative changes
Complete file outputs: Kushal prefers receiving full corrected file contents (not diffs or partial snippets) with detailed explanations of every change
Single consolidated scripts: deliverables should be complete, immediately executable shell scripts — not fragmented instructions across multiple files
Thorough verification cadence: before merging branches or moving forward, Kushal cross-references all past-identified issues against the current codebase state
Build output as ground truth: compiler warnings and test counts from actual build output are used to confirm review accuracy
Python for reliable file manipulation in bash scripts (preferred over fragile sed/awk for multi-line edits)
Tools & resources

.NET 10, centralized package management via Directory.Packages.props
MailKit / MimeKit for IMAP and email parsing
SQLite with FTS5 (Porter stemming) for search indexing; WAL mode
OpenTelemetry with JSONL file exporters, XDG-compliant paths
Polly for retry/circuit-breaker resilience
TUnit (Microsoft.Testing.Platform runner) + NSubstitute + AwesomeAssertions
System.CommandLine 2.0.x (stable GA, SetAction/ParseResult API — not SetHandler)
GitHub Actions for CI/CD
Solution format: .slnx
