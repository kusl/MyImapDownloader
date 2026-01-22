using Microsoft.Data.Sqlite;
using MyImapDownloader.Core.Infrastructure;

namespace MyImapDownloader.Core.Tests.Infrastructure;

public class SqliteHelperTests : IAsyncDisposable
{
    private readonly TempDirectory _temp = new("sqlite_test");

    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
        _temp.Dispose();
    }

    [Test]
    public async Task CreateConnectionString_IncludesDataSource()
    {
        var dbPath = Path.Combine(_temp.Path, "test.db");
        var connStr = SqliteHelper.CreateConnectionString(dbPath);

        await Assert.That(connStr).Contains(dbPath);
    }

    [Test]
    public async Task ApplyRecommendedPragmas_DoesNotThrow()
    {
        var dbPath = Path.Combine(_temp.Path, "pragmas.db");
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        await SqliteHelper.ApplyRecommendedPragmasAsync(conn);

        // Verify WAL mode is set
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var mode = await cmd.ExecuteScalarAsync();
        await Assert.That(mode?.ToString()?.ToLower()).IsEqualTo("wal");
    }

    [Test]
    public async Task ExecuteNonQueryAsync_CreatesTable()
    {
        var dbPath = Path.Combine(_temp.Path, "nonquery.db");
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var result = await SqliteHelper.ExecuteNonQueryAsync(
            conn,
            "CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");

        await Assert.That(result).IsEqualTo(0); // DDL returns 0

        // Verify table exists
        var count = await SqliteHelper.ExecuteScalarAsync<long>(
            conn,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='test'");
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteScalarAsync_WithParameters_ReturnsValue()
    {
        var dbPath = Path.Combine(_temp.Path, "scalar.db");
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        await SqliteHelper.ExecuteNonQueryAsync(conn, 
            "CREATE TABLE kv (key TEXT PRIMARY KEY, value TEXT)");
        await SqliteHelper.ExecuteNonQueryAsync(conn,
            "INSERT INTO kv (key, value) VALUES (@k, @v)",
            new Dictionary<string, object?> { ["@k"] = "test", ["@v"] = "hello" });

        var result = await SqliteHelper.ExecuteScalarAsync<string>(
            conn,
            "SELECT value FROM kv WHERE key = @k",
            new Dictionary<string, object?> { ["@k"] = "test" });

        await Assert.That(result).IsEqualTo("hello");
    }
}
