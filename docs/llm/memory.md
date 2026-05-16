Manage project memory
Claude regenerates project memory every evening from your past chats in this project. Only you can see this memory, and it is not shared with other project users.

Purpose & context

Kushal is building a personal email archival and search system on Fedora Linux, currently consisting of a multi-project .NET 10 solution:

MyImapDownloader – downloads and archives emails from IMAP servers (read-only, append-only, never deletes)
MyImapDownloader.Core – shared infrastructure library extracted from both apps (telemetry, path resolution, email metadata models, SQLite helpers)
MyEmailSearch – CLI search tool over the archived .eml files using SQLite FTS5
Corresponding test projects for each
The archive spans multiple email accounts stored under ~/Documents/mail/, with compiled binaries deployed via install.sh to /opt/ with /usr/local/bin symlinks for system-wide access. The overarching goals are data integrity (emails are never lost or overwritten), correctness, and high test coverage.

Current state

Codebase is in a stable, verified state: all previously identified bugs resolved, all tests passing, clean builds across platforms
Two CLI parsing libraries coexist intentionally: CommandLineParser 2.9.1 in MyImapDownloader (simple, flat CLI) and System.CommandLine 2.0.x in MyEmailSearch (subcommands) — keeping both as-is is the current decision
MyImapDownloader.Core is the shared library holding telemetry, PathResolver (XDG-compliant), EmailMetadata, and SQLite helpers
Known non-blocking technical debt tracked: telemetry code duplication between Core and the app (diverged exporters), EmailMetadata naming collision across namespaces, TestLogger in a production assembly, SearchDatabase.cs could be split
Dapper and Microsoft.Data.SqlClient appear in Directory.Packages.props as unused/worth removing
On the horizon

Resolving the telemetry duplication between Core and the main app
Potential cleanup of unused packages from centralized package management
Continued growth of the email archive (potentially hundreds of GB), which may prompt further performance or scalability work
Key learnings & principles

Verify before reporting: Claude must check actual source before flagging issues — if code compiles and tests pass, there is a reason; find it rather than assuming a defect
No hallucination: all guidance must be grounded in actual codebase state from dump.txt
Code is the means to an end: unused code should be deleted, not left as dead weight
Intentional changes aren't defects: API visibility changes, method renames, and property mutability shifts that are internally consistent are enhancements, not bugs
Consolidation risk: merging libraries or refactoring working code requires clear functional benefit to justify the risk
Approach & patterns

Deliberate and methodical: Kushal explicitly pumps the brakes on implementation to do full read-only analysis first before any changes
Comprehensive, single-shot deliverables: prefers complete bash scripts over fragmented multi-file instructions; scripts should be immediately executable
Cross-referencing: verifies current codebase state against historical decisions before moving forward
Scope boundaries: sets clear boundaries ("read-only review," "no code changes yet") using direct, polite phrasing
Test-first confidence: uses passing test count and clean build output as the primary signal of correctness before merging or deploying
Tools & resources

Languages/runtime: C# / .NET 10, targeting net10.0
Testing: TUnit (with Microsoft.Testing.Platform runner, --disable-logo not --nologo), NSubstitute, AwesomeAssertions
Search: SQLite FTS5 with Porter stemming, WAL mode
CLI: System.CommandLine 2.0.x (MyEmailSearch), CommandLineParser 2.9.1 (MyImapDownloader)
Email parsing: MimeKit, MailKit
Telemetry: OpenTelemetry with JSONL file exporters, XDG-compliant directory paths
Resilience: Polly (retry, circuit breaker)
Centralized package management: Directory.Packages.props, Directory.Build.props, Directory.Build.targets
Solution format: .slnx
CI/CD: GitHub Actions using only GitHub primitive actions (no third-party marketplace actions)
OS: Fedora Linux; deployment via install.sh to /opt/
