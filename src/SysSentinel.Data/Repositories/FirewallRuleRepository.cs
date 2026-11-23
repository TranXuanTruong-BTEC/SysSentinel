using Microsoft.Data.Sqlite;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;
using SysSentinel.Data.Connection;
using SysSentinel.Data.Internal;

namespace SysSentinel.Data.Repositories;

public sealed class FirewallRuleRepository : IFirewallRuleRepository
{
    private const string Columns = "Id, RuleName, TargetPath, RemoteIp, Direction, Action, IsEnabled, CreatedAt";
    private readonly ISqliteConnectionFactory _factory;

    public FirewallRuleRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<FirewallRule>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM FirewallRules ORDER BY Id;";
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<FirewallRule?> GetByNameAsync(string ruleName, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM FirewallRules WHERE RuleName = $name;";
        cmd.AddParam("$name", ruleName);
        return (await ReadAllAsync(cmd, ct)).FirstOrDefault();
    }

    public async Task<int> AddAsync(FirewallRule rule, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FirewallRules (RuleName, TargetPath, RemoteIp, Direction, Action, IsEnabled, CreatedAt)
            VALUES ($name, $path, $ip, $dir, $act, $en, $created)
            RETURNING Id;
            """;
        cmd.AddParam("$name", rule.RuleName);
        cmd.AddParam("$path", rule.TargetPath);
        cmd.AddParam("$ip", rule.RemoteIp);
        cmd.AddParam("$dir", rule.Direction.ToString());
        cmd.AddParam("$act", rule.Action.ToString());
        cmd.AddParam("$en", rule.IsEnabled ? 1 : 0);
        cmd.AddParam("$created", rule.CreatedAt.ToDb());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<bool> SetEnabledAsync(int id, bool enabled, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE FirewallRules SET IsEnabled = $en WHERE Id = $id;";
        cmd.AddParam("$en", enabled ? 1 : 0);
        cmd.AddParam("$id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM FirewallRules WHERE Id = $id;";
        cmd.AddParam("$id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    private static async Task<IReadOnlyList<FirewallRule>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<FirewallRule>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new FirewallRule
            {
                Id = r.GetInt32(0),
                RuleName = r.GetString(1),
                TargetPath = r.GetNullableString(2),
                RemoteIp = r.GetNullableString(3),
                Direction = SqliteExtensions.ParseEnum(r.GetString(4), FirewallDirection.Outbound),
                Action = SqliteExtensions.ParseEnum(r.GetString(5), FirewallAction.Block),
                IsEnabled = r.GetBoolean(6),
                CreatedAt = SqliteExtensions.FromDb(r.GetString(7))
            });
        }
        return list;
    }
}
