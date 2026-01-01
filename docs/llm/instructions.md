# MyImapDownloader Development Guidelines

## Code Source of Truth

Always check `dump.txt` in the project root for the latest complete codebase state. This file serves as the authoritative reference for all existing code. Before starting any new work:

1. Review `dump.txt` to understand the current implementation
2. Use it as the basis for all code modifications and additions
3. When proposing changes, reference relevant sections from `dump.txt`
4. Keep `dump.txt` updated after significant changes

## Code Quality Principles

### Testability First

- Write all code to be testable from the outset
- Structure classes and methods to allow dependency injection where applicable
- Avoid static dependencies and hidden state
- Design for unit testing without requiring external services or complex setup
- Prefer composition over inheritance to enable easier mocking

### Documentation and Clarity

- Code should be self-documenting through clear naming and structure
- Include XML documentation comments on public APIs
- Document non-obvious logic or design decisions inline
- Maintain up-to-date README and implementation documentation

### Safety and Reliability

- Maintain the read-only guarantee on IMAP servers—never delete emails remotely
- Use append-only storage patterns locally; never modify or delete archived emails
- Implement atomic write patterns to ensure crash safety
- Use OpenTelemetry instrumentation for observability
- Apply Polly resilience patterns for network operations

## Dependency Management

### Free and Open Source Only

- Use only FOSS dependencies unless no viable alternative exists
- Prefer well-maintained, widely-adopted open source libraries
- Evaluate dependencies for license compatibility (prefer MIT, Apache 2.0, BSD)
- Document any non-FOSS exceptions with justification

### Approved Technologies

- **.NET 10**: Cross-platform runtime
- **SQLite**: Lightweight, embedded database for indexing
- **OpenTelemetry**: Observability and telemetry (open standard)
- **Polly**: Resilience and transient-fault handling
- All dependencies must have clear FOSS licensing

## Delivery Format

### Single-File Shell Scripts

When providing scripts or configuration changes:

- Deliver complete, runnable shell scripts in a single file
- Avoid fragmented instructions requiring changes to multiple files across different directories
- Include inline comments explaining each section
- Ensure scripts are idempotent where applicable
- Test script logic before delivery

### Code Changes

- Provide complete file contents when modifying code
- Show the full context of changes rather than snippets
- Include any required supporting files in one clear delivery
- Specify exact file paths and locations

## GitHub Actions

### Implementation Approach

- Prefer implementing workflow logic directly in bash/shell when possible
- Use GitHub primitive actions (setup-dotnet, checkout, etc.) as infrastructure
- Avoid relying on third-party marketplace actions for custom logic
- Study existing action implementations to understand patterns and replicate them yourself
- Keep workflows readable and maintainable by anyone reviewing the repository

### Acceptable Exceptions

- Use GitHub's official actions for standard operations (checkout, setup language runtimes, artifact upload/download)
- Use community-maintained actions only when reimplementation would be unreasonably complex
- Document any third-party action usage with justification

## Coding Style Reference

Based on MyImapDownloader conventions:

- Use meaningful names: `EmailArchiveService`, `DeltaSynchronizationManager`, not generic terms
- Structure for delta operations: only process new/changed data
- Implement self-healing database recovery patterns
- Use UIDVALIDITY and Message-ID indexing for deduplication
- Follow XDG directory specifications for configuration and data storage
- Log comprehensively via OpenTelemetry with structured metrics

## General Guidelines

- No speculative or hallucinated implementation details
- Reference the actual codebase (`dump.txt`) when discussing implementation
- Propose changes that align with existing architecture and patterns
- Consider scalability (current 35GB, designed for hundreds of gigabytes)
- Maintain backwards compatibility with existing archives when possible
- Keep safety and data integrity as paramount concerns

## Change Documentation

- Update this guidelines document as new patterns or practices emerge
- Document architectural decisions in implementation summaries
- Keep README synchronized with actual implementation
- Note any deviations from these guidelines with clear rationale
