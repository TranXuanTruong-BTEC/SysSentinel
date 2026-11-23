using Microsoft.Data.Sqlite;

namespace SysSentinel.Data.Connection;

public interface ISqliteConnectionFactory
{
    string DatabasePath { get; }

    /// <summary>Mở connection mới đã áp dụng PRAGMA. Người gọi phải dispose (await using).</summary>
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default);
}
