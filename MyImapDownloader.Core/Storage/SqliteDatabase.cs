using Microsoft.Data.Sqlite;

namespace MyImapDownloader.Core.Storage;

public static class SqliteDatabase
{
    private const int CorruptErrorCode = 11;
    private const int NotADatabaseErrorCode = 26;

    public static async Task<SqliteConnection> OpenAsync(string path, CancellationToken ct)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await connection.ExecuteAsync("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;", ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public static bool IsCorruption(SqliteException exception) =>
        exception.SqliteErrorCode is CorruptErrorCode or NotADatabaseErrorCode;

    extension(SqliteConnection connection)
    {
        public SqliteCommand Prepare(string sql, IEnumerable<(string Name, object? Value)>? parameters = null)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters ?? [])
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            return command;
        }

        public async Task<int> ExecuteAsync(
            string sql,
            CancellationToken ct,
            IEnumerable<(string Name, object? Value)>? parameters = null)
        {
            await using var command = connection.Prepare(sql, parameters);
            return await command.ExecuteNonQueryAsync(ct);
        }

        public async Task<object?> ScalarAsync(
            string sql,
            CancellationToken ct,
            IEnumerable<(string Name, object? Value)>? parameters = null)
        {
            await using var command = connection.Prepare(sql, parameters);
            var value = await command.ExecuteScalarAsync(ct);
            return value is DBNull ? null : value;
        }
    }
}
