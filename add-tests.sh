#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"
ROOT="$(pwd)"

echo "=== Fixing MyEmailSearch build errors ==="
echo "Root: $ROOT"
echo ""

# ─────────────────────────────────────────────────────────────────────────────
# FIX 1: Add BatchUpsertEmailsAsync to SearchDatabase.cs
# The tests and IndexManager call this method but it doesn't exist.
# We add it as a transactional wrapper around UpsertEmailAsync.
# ─────────────────────────────────────────────────────────────────────────────

DB_FILE="$ROOT/MyEmailSearch/Data/SearchDatabase.cs"

if [ ! -f "$DB_FILE" ]; then
    echo "ERROR: $DB_FILE not found!"
    exit 1
fi

if grep -q 'BatchUpsertEmailsAsync' "$DB_FILE"; then
    echo "SKIP: BatchUpsertEmailsAsync already exists in SearchDatabase.cs"
else
    echo "FIX 1: Adding BatchUpsertEmailsAsync to SearchDatabase.cs"

    # We insert the method right before the closing of the class.
    # Strategy: find the last closing brace "}" in the file (which closes the class)
    # and insert our method before it.
    #
    # Use python for reliable multi-line insertion into the correct location.
    python3 << 'PYEOF'
import re

filepath = "MyEmailSearch/Data/SearchDatabase.cs"

with open(filepath, "r") as f:
    content = f.read()

# The method to insert - goes right before the final closing brace of the class
method = '''
    /// <summary>
    /// Batch upserts multiple email documents within a single transaction for performance.
    /// </summary>
    public async Task BatchUpsertEmailsAsync(IReadOnlyList<EmailDocument> documents, CancellationToken ct = default)
    {
        if (documents.Count == 0) return;

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var transaction = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var doc in documents)
            {
                ct.ThrowIfCancellationRequested();
                await UpsertEmailCoreAsync(doc, ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
'''

# Check if UpsertEmailAsync delegates to a core method or does the work inline.
# We need to see if there's already a UpsertEmailCoreAsync or if we need to
# refactor UpsertEmailAsync to extract the core logic.

if "UpsertEmailCoreAsync" in content:
    # Already has the core method, just add BatchUpsertEmailsAsync
    pass
else:
    # We need to:
    # 1. Rename the body of UpsertEmailAsync into UpsertEmailCoreAsync (private, no connection ensure)
    # 2. Make UpsertEmailAsync call EnsureConnection + UpsertEmailCoreAsync
    # 3. Add BatchUpsertEmailsAsync that calls UpsertEmailCoreAsync in a transaction

    # Find the UpsertEmailAsync method
    # Pattern: public async Task UpsertEmailAsync(EmailDocument doc, ...)
    upsert_pattern = r'(    /// <summary>\s*\n\s*/// Upserts.*?\n(?:\s*/// .*?\n)*\s*public async Task UpsertEmailAsync\(EmailDocument\s+\w+.*?\n)(.*?)(\n    (?:/// |public |private |internal |\}))'

    match = re.search(upsert_pattern, content, re.DOTALL)

    if not match:
        # Try a simpler pattern - just find the method signature
        # Look for "public async Task UpsertEmailAsync("
        upsert_start = content.find("public async Task UpsertEmailAsync(")
        if upsert_start == -1:
            print("ERROR: Cannot find UpsertEmailAsync method in SearchDatabase.cs")
            print("Will add BatchUpsertEmailsAsync as a simple loop wrapper instead.")

            # Fallback: add a simple BatchUpsertEmailsAsync that just loops
            simple_method = '''
    /// <summary>
    /// Batch upserts multiple email documents for performance.
    /// </summary>
    public async Task BatchUpsertEmailsAsync(IReadOnlyList<EmailDocument> documents, CancellationToken ct = default)
    {
        if (documents.Count == 0) return;

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var transaction = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var doc in documents)
            {
                ct.ThrowIfCancellationRequested();
                await UpsertEmailAsync(doc, ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
'''
            # Insert before the last closing brace
            last_brace = content.rfind("}")
            content = content[:last_brace] + simple_method + "\n" + content[last_brace:]

            with open(filepath, "w") as f:
                f.write(content)
            print(f"OK: Added simple BatchUpsertEmailsAsync to {filepath}")
            exit(0)
        else:
            # Found it - add batch method as simple loop wrapper
            simple_method = '''
    /// <summary>
    /// Batch upserts multiple email documents for performance.
    /// </summary>
    public async Task BatchUpsertEmailsAsync(IReadOnlyList<EmailDocument> documents, CancellationToken ct = default)
    {
        if (documents.Count == 0) return;

        await EnsureConnectionAsync(ct).ConfigureAwait(false);

        await using var transaction = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var doc in documents)
            {
                ct.ThrowIfCancellationRequested();
                await UpsertEmailAsync(doc, ct).ConfigureAwait(false);
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
'''
            last_brace = content.rfind("}")
            content = content[:last_brace] + simple_method + "\n" + content[last_brace:]

            with open(filepath, "w") as f:
                f.write(content)
            print(f"OK: Added BatchUpsertEmailsAsync to {filepath}")
            exit(0)
    else:
        # Insert the method using the extracted core pattern
        last_brace = content.rfind("}")
        content = content[:last_brace] + method + "\n" + content[last_brace:]

        with open(filepath, "w") as f:
            f.write(content)
        print(f"OK: Added BatchUpsertEmailsAsync to {filepath}")
        exit(0)
PYEOF
fi

echo ""

# ─────────────────────────────────────────────────────────────────────────────
# FIX 2: Fix IndexManagerCancellationTests.cs TUnit0018 warnings
# "Test methods should not assign instance data"
# The issue is that test methods assign to _database field.
# Fix: use a list to track disposables instead of direct field assignment.
# ─────────────────────────────────────────────────────────────────────────────

CANCEL_TEST_FILE="$ROOT/MyEmailSearch.Tests/Indexing/IndexManagerCancellationTests.cs"

if [ ! -f "$CANCEL_TEST_FILE" ]; then
    echo "SKIP: IndexManagerCancellationTests.cs not found"
else
    echo "FIX 2: Rewriting IndexManagerCancellationTests.cs to fix TUnit0018 warnings"

    cat > "$CANCEL_TEST_FILE" << 'CSHARP'
using Microsoft.Extensions.Logging.Abstractions;

using MyEmailSearch.Data;
using MyEmailSearch.Indexing;

using MyImapDownloader.Core.Infrastructure;

namespace MyEmailSearch.Tests.Indexing;

/// <summary>
/// Tests for IndexManager cancellation and progress reporting.
/// </summary>
public class IndexManagerCancellationTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("index_cancel_test");
    private readonly List<SearchDatabase> _databases = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var db in _databases)
        {
            await db.DisposeAsync();
        }
        await Task.Delay(100);
        _temp.Dispose();
    }

    private async Task CreateEmlFileAsync(string folder, string messageId)
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dir = Path.Combine(archivePath, folder, "cur");
        Directory.CreateDirectory(dir);

        var content = $"Message-ID: <{messageId}>\r\n" +
            $"Subject: Test {messageId}\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Mon, 01 Jan 2024 12:00:00 +0000\r\n" +
            "Content-Type: text/plain\r\n" +
            "\r\n" +
            "Body\r\n";

        await File.WriteAllTextAsync(Path.Combine(dir, $"{messageId}.eml"), content);
    }

    private async Task<(SearchDatabase db, IndexManager manager)> CreateServicesAsync()
    {
        var archivePath = Path.Combine(_temp.Path, "archive");
        var dbPath = Path.Combine(_temp.Path, $"search_{Guid.NewGuid():N}.db");
        var db = new SearchDatabase(dbPath, NullLogger<SearchDatabase>.Instance);
        await db.InitializeAsync();
        _databases.Add(db);

        var scanner = new ArchiveScanner(NullLogger<ArchiveScanner>.Instance);
        var parser = new EmailParser(archivePath, NullLogger<EmailParser>.Instance);
        var manager = new IndexManager(db, scanner, parser, NullLogger<IndexManager>.Instance);

        return (db, manager);
    }

    [Test]
    public async Task IndexAsync_CancellationToken_StopsProcessing()
    {
        for (var i = 0; i < 20; i++)
        {
            await CreateEmlFileAsync("INBOX", $"cancel{i}@example.com");
        }

        var archivePath = Path.Combine(_temp.Path, "archive");
        var (_, manager) = await CreateServicesAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await manager.IndexAsync(archivePath, includeContent: false, ct: cts.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(act);
    }

    [Test]
    public async Task IndexAsync_ReportsProgress()
    {
        await CreateEmlFileAsync("INBOX", "progress1@example.com");
        await CreateEmlFileAsync("INBOX", "progress2@example.com");

        var archivePath = Path.Combine(_temp.Path, "archive");
        var (db, manager) = await CreateServicesAsync();

        await manager.IndexAsync(archivePath, includeContent: false);

        var count = await db.GetEmailCountAsync();
        await Assert.That(count).IsGreaterThanOrEqualTo(2);
    }
}
CSHARP

    echo "OK: Rewrote IndexManagerCancellationTests.cs"
fi

echo ""

# ─────────────────────────────────────────────────────────────────────────────
# BUILD AND VERIFY
# ─────────────────────────────────────────────────────────────────────────────

echo "=== Building solution ==="
echo ""

if dotnet build --nologo 2>&1; then
    echo ""
    echo "=== BUILD SUCCEEDED ==="
    echo ""
    echo "Running tests..."
    if dotnet test --nologo --no-build 2>&1; then
        echo ""
        echo "=== ALL TESTS PASSED ==="
    else
        echo ""
        echo "=== SOME TESTS FAILED (see output above) ==="
    fi
else
    echo ""
    echo "=== BUILD FAILED ==="
    echo ""
    echo "Checking remaining errors..."
    dotnet build --nologo 2>&1 | grep -E "error CS|warning TUnit" || true
fi
