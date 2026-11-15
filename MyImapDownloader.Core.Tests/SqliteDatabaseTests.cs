using Microsoft.Data.Sqlite;

using MyImapDownloader.Core.Storage;

namespace MyImapDownloader.Core.Tests;

public class SqliteDatabaseTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string DatabasePath => Path.Combine(_temp.Path, "test.db");

    [Test]
    public async Task OpenAsync_CreatesDatabaseInWalMode()
    {
        await using var connection = await SqliteDatabase.OpenAsync(DatabasePath, CancellationToken.None);

        var mode = await connection.ScalarAsync("PRAGMA journal_mode;", CancellationToken.None);

        await Assert.That(File.Exists(DatabasePath)).IsTrue();
        await Assert.That(mode as string).IsEqualTo("wal");
    }

    [Test]
    public async Task ExecuteAndScalar_BindNamedParameters()
    {
        await using var connection = await SqliteDatabase.OpenAsync(DatabasePath, CancellationToken.None);
        await connection.ExecuteAsync("CREATE TABLE items (name TEXT, note TEXT);", CancellationToken.None);

        var inserted = await connection.ExecuteAsync(
            "INSERT INTO items (name, note) VALUES (@name, @note);",
            CancellationToken.None,
            [("@name", "first"), ("@note", null)]);
        var name = await connection.ScalarAsync(
            "SELECT name FROM items WHERE note IS NULL;", CancellationToken.None);
        var missing = await connection.ScalarAsync(
            "SELECT note FROM items WHERE name = @name;", CancellationToken.None, [("@name", "first")]);

        await Assert.That(inserted).IsEqualTo(1);
        await Assert.That(name as string).IsEqualTo("first");
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task OpenAsync_ReleasesTheFileWhenDisposed()
    {
        var connection = await SqliteDatabase.OpenAsync(DatabasePath, CancellationToken.None);
        await connection.DisposeAsync();

        File.Move(DatabasePath, DatabasePath + ".moved");

        await Assert.That(File.Exists(DatabasePath + ".moved")).IsTrue();
    }

    [Test]
    public async Task OpenAsync_ReportsGarbageFileAsCorruption()
    {
        await File.WriteAllTextAsync(DatabasePath, new string('x', 4096));

        var exception = await Assert.ThrowsAsync<SqliteException>(
            async () => await SqliteDatabase.OpenAsync(DatabasePath, CancellationToken.None));

        await Assert.That(SqliteDatabase.IsCorruption(exception!)).IsTrue();
    }
}
