using Microsoft.Data.Sqlite;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;
using SysSentinel.Data.Connection;
using SysSentinel.Data.Internal;

namespace SysSentinel.Data.Repositories;

public sealed class AutomationRuleRepository : IAutomationRuleRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public AutomationRuleRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<AutomationRule>> GetByProfileAsync(
        int profileId, bool onlyEnabled = false, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, ProfileId, ConditionType, ConditionValue, ActionType, ActionPayload, Priority, IsEnabled
              FROM AutomationRules
             WHERE ProfileId = $pid AND ($onlyEnabled = 0 OR IsEnabled = 1)
             ORDER BY Priority, Id;
            """;
        cmd.AddParam("$pid", profileId);
        cmd.AddParam("$onlyEnabled", onlyEnabled ? 1 : 0);

        var list = new List<AutomationRule>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new AutomationRule
            {
                Id = r.GetInt32(0),
                ProfileId = r.GetInt32(1),
                ConditionType = SqliteExtensions.ParseEnum(r.GetString(2), RuleConditionType.ProcessRunning),
                ConditionValue = r.GetString(3),
                ActionType = SqliteExtensions.ParseEnum(r.GetString(4), RuleActionType.SetPowerPlan),
                ActionPayload = r.GetNullableString(5),
                Priority = r.GetInt32(6),
                IsEnabled = r.GetBoolean(7)
            });
        }
        return list;
    }

    public async Task<int> AddAsync(AutomationRule rule, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AutomationRules (ProfileId, ConditionType, ConditionValue, ActionType, ActionPayload, Priority, IsEnabled)
            VALUES ($pid, $ct, $cv, $at, $ap, $prio, $en)
            RETURNING Id;
            """;
        Bind(cmd, rule);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<bool> UpdateAsync(AutomationRule rule, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE AutomationRules
               SET ProfileId = $pid, ConditionType = $ct, ConditionValue = $cv,
                   ActionType = $at, ActionPayload = $ap, Priority = $prio, IsEnabled = $en
             WHERE Id = $id;
            """;
        Bind(cmd, rule);
        cmd.AddParam("$id", rule.Id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM AutomationRules WHERE Id = $id;";
        cmd.AddParam("$id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    private static void Bind(SqliteCommand cmd, AutomationRule rule)
    {
        cmd.AddParam("$pid", rule.ProfileId);
        cmd.AddParam("$ct", rule.ConditionType.ToString());
        cmd.AddParam("$cv", rule.ConditionValue);
        cmd.AddParam("$at", rule.ActionType.ToString());
        cmd.AddParam("$ap", rule.ActionPayload);
        cmd.AddParam("$prio", rule.Priority);
        cmd.AddParam("$en", rule.IsEnabled ? 1 : 0);
    }
}
