using Microsoft.Data.Sqlite;

namespace MyImapDownloader.Core.Infrastructure;

/// <summary>
/// Helper class for common SQLite operations.
/// </summary>
public static class SqliteHelper
{
    /// <summary>
    /// Creates a connection string with recommended settings.
    /// </summary>
    public static string CreateConnectionString(string dbPath, bool readOnly = false)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Applies recommended pragmas for performance and safety.
    /// </summary>
    public static async Task ApplyRecommendedPragmasAsync(
        SqliteConnection connection,
        CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA mmap_size = 268435456;
            PRAGMA cache_size = -64000;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Executes a non-query command.
    /// </summary>
    public static async Task<int> ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        
        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
            }
        }

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Executes a scalar query.
    /// </summary>
    public static async Task<T?> ExecuteScalarAsync<T>(
        SqliteConnection connection,
        string sql,
        Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
            }
        }

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result == null || result == DBNull.Value)
        {
            return default;
        }
        return (T)Convert.ChangeType(result, typeof(T));
    }
}
