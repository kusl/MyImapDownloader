# Email Archive Search System
## Business Requirements Document

---

## 1. Executive Summary

The MyImapDownloader application currently archives email from multiple IMAP accounts into a unified 35GB+ collection of `.eml` files with JSON metadata sidecars. This document outlines the functional and non-functional requirements for a search and discovery system that will enable users to quickly locate emails across this growing archive, which may eventually contain hundreds of gigabytes of data.

The search system must provide both **structured searching** (targeting specific email fields with exact or partial matches) and **unstructured searching** (full-text content discovery) with response times measured in milliseconds to seconds, regardless of archive size.

---

## 2. Business Objectives

1. **Enable Fast Discovery**: Users should be able to find emails by sender, recipient, subject, date, or content within seconds, even as the archive grows to hundreds of gigabytes.

2. **Support Multiple Search Patterns**: The system must support both field-specific searches (e.g., "find all emails from alice@example.com") and content-based searches (e.g., "find all emails mentioning Kafka").

3. **Maintain Archive Integrity**: The search system must not modify, delete, or corrupt the underlying `.eml` files or metadata. The archive remains the source of truth.

4. **Scale Gracefully**: Performance must remain acceptable as the archive grows from 35GB to 100GB, 500GB, or more, without requiring manual maintenance or index rebuilding by the user.

5. **Non-Invasive Integration**: The search system should integrate seamlessly with the existing MyImapDownloader workflow without disrupting current synchronization operations.

---

## 3. Functional Requirements

### 3.1 Structured Searches

The system must support fast, precise searches across the following email fields:

#### 3.1.1 Sender / From Address
- **Exact Match**: Find all emails from `alice@example.com`
- **Partial Match**: Find all emails from anyone with address `alice*` or `*@example.com`
- **Domain Match**: Find all emails from any address in the `example.com` domain
- **Case-Insensitive**: All address searches should ignore case (alice@EXAMPLE.COM = alice@example.com)

#### 3.1.2 Recipient Fields (To, Cc, Bcc)
- **Exact Match**: Find all emails sent to a specific recipient
- **Partial Match**: Find emails sent to anyone matching a pattern
- **Multi-Field Search**: Allow searching across To/Cc/Bcc simultaneously or separately
- **Case-Insensitive**: Address matching ignores case

#### 3.1.3 Subject Line
- **Exact Match**: Find emails with subject line containing exact phrase
- **Partial / Substring Match**: Find emails where subject contains partial text (e.g., searching "project" finds "Q3 Project Review", "Project Kickoff", etc.)
- **Word Boundary Matching**: Optionally, find only whole-word matches (searching for "test" should find "testing" if supported, or not find it if strict word boundaries are enforced)
- **Case-Insensitive**: Subject searches ignore case

#### 3.1.4 Date Range Filtering
- **Single Date**: Find emails sent on a specific date
- **Date Range**: Find emails sent between two dates (inclusive or exclusive as needed)
- **Relative Dates**: (Optional, may be added later) "emails from the last 30 days", "this month", etc.
- **Precision**: Support searching by day, not just arbitrary ranges

### 3.2 Full-Text Search

The system must support content-based searching across the email body:

#### 3.2.1 Word / Phrase Search
- **Single Word**: Find all emails containing the word "kafka" anywhere in the body
- **Phrase Search**: Find emails containing an exact phrase: "message broker"
- **Case-Insensitive**: Content searches ignore case
- **Partial Word Matching**: (To be determined during design) Should "kafka" match "Kafka" and "KAFKA"? (Yes, case-insensitive)

#### 3.2.2 Multiple Term Matching
- **AND Logic**: Find emails containing all of multiple terms (e.g., "dotnet" AND "async")
- **OR Logic**: Find emails containing any of multiple terms
- **NOT Logic**: Exclude emails containing certain terms

#### 3.2.3 Scope of Search
- **Body Only**: Search only the email body text, excluding headers
- **Headers + Body**: (Optional) Search both headers and body
- **Exclude Quoted Text**: (Optional) Exclude commonly quoted email chains from search results to reduce noise

### 3.3 Combined Searches

The system must allow combining structured and full-text searches:

- **AND Combination**: "Find emails from alice@example.com (structured) that mention Kafka (full-text)"
- **Date + Content**: "Find emails mentioning 'project deadline' sent in the last 90 days"
- **Sender + Subject**: "Find emails from the engineering team (@company.com) with subject containing 'incident'"

### 3.4 Search Result Delivery

- **Result Set**: Return matching emails as a list of file paths or message identifiers
- **Result Metadata**: For each match, include sender, recipient, subject, date, and a preview (first 200 characters of body)
- **Result Ordering**: Results should be ordered by relevance (for full-text) or date (for structured searches)
- **Result Count**: Display total number of matches
- **Pagination**: Support returning results in batches (e.g., first 100, next 100) for large result sets

---

## 4. Non-Functional Requirements

### 4.1 Performance

#### 4.1.1 Response Time Targets
- **Structured Searches** (by sender, recipient, subject, date):
  - **35GB archive**: < 500ms response time
  - **100GB archive**: < 1 second response time
  - **500GB archive**: < 2 seconds response time
  - Target: User perceives the search as "instantaneous" or near-instantaneous

- **Full-Text Searches** (single word):
  - **35GB archive**: < 1 second response time
  - **100GB archive**: < 2 seconds response time
  - **500GB archive**: < 5 seconds response time

- **Complex Searches** (multiple criteria combined):
  - Response times should scale predictably; a combination of two filters should not degrade performance by more than 50% compared to a single filter

#### 4.1.2 Indexing Performance
- Initial index creation should complete in reasonable time (to be defined during design; aim for < 1 hour for 35GB, but stretch to < 4 hours if necessary)
- Incremental index updates (when new emails are archived) should be near-instantaneous and not block synchronization operations

### 4.2 Scalability

- **Archive Growth**: The system must remain performant as the archive grows to 100GB, 500GB, and potentially 1TB+
- **Index Size**: The index should scale sublinearly relative to the archive size (i.e., index size should not grow 1:1 with archive size)
- **Memory Usage**: Search operations should not require loading the entire archive or index into memory; streaming/disk-based processing is acceptable
- **Concurrent Access**: If multiple searches are performed simultaneously, they should not block each other or corrupt the index

### 4.3 Reliability & Data Safety

- **No Data Loss**: The search system must never modify, delete, or corrupt the underlying `.eml` files or JSON metadata
- **Index Corruption Recovery**: If the search index becomes corrupted, the system should detect it and either:
  - Automatically rebuild the index from the source `.eml` files, OR
  - Alert the user and provide instructions to rebuild
- **Atomic Operations**: Index updates should be atomic; if an update fails mid-way, the index should remain in a consistent state
- **Crash Safety**: If the system crashes while indexing or searching, it should recover gracefully without data loss or index corruption

### 4.4 User Experience

- **Ease of Use**: The search interface should be intuitive; users should not need to learn a complex query language
- **Clear Output**: Search results should be clearly presented with relevant context (sender, subject, date, snippet)
- **Feedback**: During long-running searches, provide progress feedback (e.g., "Searched 10GB of 35GB...")
- **Help / Suggestions**: Provide built-in help for search syntax or suggest common search patterns

### 4.5 Maintainability & Operations

- **Minimal User Intervention**: The system should work automatically with minimal configuration or maintenance required
- **Transparent Indexing**: Index creation and updates should happen transparently in the background, ideally without disrupting the email archival workflow
- **Observability**: The system should provide logs or metrics about search operations (queries executed, response times, index size) for troubleshooting

### 4.6 Integration & Compatibility

- **XDG Compliance**: (Inherited from MyImapDownloader) Index and temporary files should follow XDG Base Directory Specification on Linux/macOS and standard paths on Windows
- **Multi-Account Support**: The archive may contain emails from multiple IMAP accounts (e.g., kushal_gmx_backup, kushal_disroot_backup). The search system must handle this gracefully, either by:
  - Searching across all accounts by default
  - Allowing users to filter searches by account
  - Both of the above

---

## 5. Out of Scope (For Now)

The following are explicitly **not** required in the initial version:

1. **Machine Learning / AI Ranking**: No ML-based relevance ranking; simple text matching is sufficient
2. **Fuzzy Matching**: No tolerance for typos or misspellings (e.g., searching "alice" should not match "alise")
3. **Advanced NLP**: No natural language processing, sentiment analysis, or entity extraction
4. **Email Threading**: The system does not need to group emails into conversation threads
5. **Attachment Search**: Searching within attachment contents is not required
6. **Duplicate Detection**: The system does not need to identify or flag duplicate emails (this is handled by MyImapDownloader's deduplication)
7. **User Accounts / Permissions**: The system assumes a single-user, single-archive environment (no multi-user access control)
8. **Web / GUI Interface**: A web browser interface is not required for version 1.0; a command-line or text-based interface is acceptable

---

## 6. Implementation Approach (Guidelines, Not Requirements)

The following are guidelines to inform the design, but not hard requirements:

- **Incremental Approach**: Start with structured field searches (sender, recipient, subject, date). Add full-text search in a later phase if needed.
- **Storage Format**: The search index should be stored alongside the `.eml` files in the archive directory, making it portable and self-contained.
- **Automation**: Index updates should be triggered automatically when new emails are archived, not requiring manual intervention.
- **Transparency**: Users should be able to verify that the search index is accurate by spot-checking a few results against the original `.eml` files.

---

## 7. Success Criteria

The search system will be considered successful when:

1. ✅ Users can find emails by sender, recipient, subject, or date range in < 500ms (at 35GB)
2. ✅ Users can perform full-text searches for single words in < 1 second (at 35GB)
3. ✅ Combined searches (e.g., sender + date range) work as expected
4. ✅ Search results are accurate (no false positives/negatives in spot checks)
5. ✅ The underlying `.eml` files remain untouched and uncorrupted
6. ✅ The system scales gracefully to 100GB+ archives without requiring manual index maintenance
7. ✅ The index is automatically updated whenever new emails are archived
8. ✅ Users find the search interface intuitive and helpful for their email discovery workflow

---

## 8. Future Enhancements (Post-MVP)

These are not required for the initial release but may be added later:

- Relative date queries ("last 30 days")
- Fuzzy matching for typo tolerance
- Search history / saved searches
- Bulk operations on search results (e.g., "export all matching emails")
- Advanced NLP for entity extraction (e.g., "find emails mentioning person names")
- Attachment search
- Email threading / conversation grouping
- Performance analytics and search telemetry

---

## 9. Appendix: Example User Workflows

### Workflow 1: Find Emails from a Colleague
> "I need to find all emails from alice@example.com"
- **Input**: `search --from alice@example.com`
- **Output**: List of matching emails with subject, date, preview
- **Target Time**: < 500ms

### Workflow 2: Find Emails About a Project
> "I need all emails mentioning 'Project Alpha' sent in Q3 2024"
- **Input**: `search --subject "Project Alpha" --from 2024-07-01 --to 2024-09-30`
- **Output**: List of matching emails with subject, date, preview
- **Target Time**: < 1 second

### Workflow 3: Find Technical Discussion
> "I need to find all discussions about Kafka or message brokers in the last year"
- **Input**: `search --content "kafka OR message broker" --from 2023-12-01`
- **Output**: List of matching emails with snippet showing where the keyword appears
- **Target Time**: < 2 seconds

### Workflow 4: Find Emails with Recipients
> "Find all emails where I CC'd the engineering team (@company.com)"
- **Input**: `search --cc "*@company.com"`
- **Output**: List of matching emails
- **Target Time**: < 500ms

---

**Document Version**: 1.0  
**Last Updated**: December 2025  
**Status**: Ready for Design Phase






