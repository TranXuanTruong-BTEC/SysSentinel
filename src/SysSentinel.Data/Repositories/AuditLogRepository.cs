using Microsoft.Data.Sqlite;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;
using SysSentinel.Data.Connection;
using SysSentinel.Data.Internal;

namespace SysSentinel.Data.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private const string Columns =
        "Id, Category, ActionName, Target, Status, RollbackType, OriginalStateJson, IsRolledBack, Timestamp";

    private readonly ISqliteConnectionFactory _factory;

    public AuditLogRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> AddAsync(AuditLog log, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AuditLogs (Category, ActionName, Target, Status, RollbackType, OriginalStateJson, IsRolledBack, Timestamp)
            VALUES ($cat, $act, $target, $status, $rbType, $state, $rolled, $ts)
            RETURNING Id;
            """;
        cmd.AddParam("$cat", log.Category);
        cmd.AddParam("$act", log.ActionName);
        cmd.AddParam("$target", log.Target);
        cmd.AddParam("$status", log.Status.ToString());
        cmd.AddParam("$rbType", log.RollbackType.ToString());
        cmd.AddParam("$state", log.OriginalStateJson);
        cmd.AddParam("$rolled", log.IsRolledBack ? 1 : 0);
        cmd.AddParam("$ts", log.Timestamp.ToDb());
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<IReadOnlyList<AuditLog>> GetRecentAsync(int limit = 200, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM AuditLogs ORDER BY Timestamp DESC, Id DESC LIMIT $limit;";
        cmd.AddParam("$limit", limit);
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<AuditLog?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM AuditLogs WHERE Id = $id;";
        cmd.AddParam("$id", id);
        return (await ReadAllAsync(cmd, ct)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<AuditLog>> GetPendingRollbackAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // WHERE khớp đúng partial index IX_AuditLogs_PendingRollback
        cmd.CommandText = "SELECT " + Columns + """
             FROM AuditLogs
            WHERE IsRolledBack = 0 AND RollbackType <> 'None'
            ORDER BY Timestamp DESC, Id DESC
            LIMIT $limit;
            """;
        cmd.AddParam("$limit", limit);
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<bool> MarkRolledBackAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE AuditLogs SET IsRolledBack = 1 WHERE Id = $id AND IsRolledBack = 0;";
        cmd.AddParam("$id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    private static async Task<IReadOnlyList<AuditLog>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<AuditLog>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new AuditLog
            {
                Id = r.GetInt64(0),
                Category = r.GetString(1),
                ActionName = r.GetString(2),
                Target = r.GetString(3),
                Status = SqliteExtensions.ParseEnum(r.GetString(4), AuditStatus.Success),
                RollbackType = SqliteExtensions.ParseEnum(r.GetString(5), RollbackType.None),
                OriginalStateJson = r.GetNullableString(6),
                IsRolledBack = r.GetBoolean(7),
                Timestamp = SqliteExtensions.FromDb(r.GetString(8))
            });
        }
        return list;
    }
}
