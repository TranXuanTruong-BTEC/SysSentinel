using System.Text.Json;
using Microsoft.Data.Sqlite;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;
using SysSentinel.Data.Connection;
using SysSentinel.Data.Internal;

namespace SysSentinel.Data.Repositories;

public sealed class ProfileRepository : IProfileRepository
{
    private const string Columns =
        "Id, Name, Description, PowerPlanGuid, IsFocusModeEnabled, AutoTriggerProcesses, IsActive, CreatedAt";

    private readonly ISqliteConnectionFactory _factory;

    public ProfileRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM Profiles ORDER BY Id;";
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<Profile?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM Profiles WHERE Id = $id;";
        cmd.AddParam("$id", id);
        return (await ReadAllAsync(cmd, ct)).FirstOrDefault();
    }

    public async Task<Profile?> GetActiveAsync(CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM Profiles WHERE IsActive = 1 LIMIT 1;";
        return (await ReadAllAsync(cmd, ct)).FirstOrDefault();
    }

    public async Task<int> AddAsync(Profile p, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Profiles (Name, Description, PowerPlanGuid, IsFocusModeEnabled, AutoTriggerProcesses, IsActive, CreatedAt)
            VALUES ($name, $desc, $plan, $focus, $triggers, 0, $created)
            RETURNING Id;
            """;
        cmd.AddParam("$name", p.Name);
        cmd.AddParam("$desc", p.Description);
        cmd.AddParam("$plan", p.PowerPlanGuid);
        cmd.AddParam("$focus", p.IsFocusModeEnabled ? 1 : 0);
        cmd.AddParam("$triggers", JsonSerializer.Serialize(p.AutoTriggerProcesses));
        cmd.AddParam("$created", p.CreatedAt.ToDb());
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<bool> UpdateAsync(Profile p, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE Profiles
               SET Name = $name, Description = $desc, PowerPlanGuid = $plan,
                   IsFocusModeEnabled = $focus, AutoTriggerProcesses = $triggers
             WHERE Id = $id;
            """;
        cmd.AddParam("$id", p.Id);
        cmd.AddParam("$name", p.Name);
        cmd.AddParam("$desc", p.Description);
        cmd.AddParam("$plan", p.PowerPlanGuid);
        cmd.AddParam("$focus", p.IsFocusModeEnabled ? 1 : 0);
        cmd.AddParam("$triggers", JsonSerializer.Serialize(p.AutoTriggerProcesses));
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // AutomationRules sẽ bị xóa theo (ON DELETE CASCADE). Không cho xóa profile đang active.
        cmd.CommandText = "DELETE FROM Profiles WHERE Id = $id AND IsActive = 0;";
        cmd.AddParam("$id", id);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> SetActiveAsync(int id, CancellationToken ct = default)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var off = conn.CreateCommand())
            {
                off.Transaction = tx;
                off.CommandText = "UPDATE Profiles SET IsActive = 0 WHERE IsActive = 1;";
                await off.ExecuteNonQueryAsync(ct);
            }

            int affected;
            await using (var on = conn.CreateCommand())
            {
                on.Transaction = tx;
                on.CommandText = "UPDATE Profiles SET IsActive = 1 WHERE Id = $id;";
                on.AddParam("$id", id);
                affected = await on.ExecuteNonQueryAsync(ct);
            }

            if (affected != 1)
            {
                await tx.RollbackAsync(CancellationToken.None); // id không tồn tại -> giữ nguyên profile cũ
                return false;
            }

            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<IReadOnlyList<Profile>> ReadAllAsync(SqliteCommand cmd, CancellationToken ct)
    {
        var list = new List<Profile>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new Profile
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Description = r.GetString(2),
                PowerPlanGuid = r.GetNullableString(3),
                IsFocusModeEnabled = r.GetBoolean(4),
                AutoTriggerProcesses = DeserializeList(r.GetString(5)),
                IsActive = r.GetBoolean(6),
                CreatedAt = SqliteExtensions.FromDb(r.GetString(7))
            });
        }
        return list;
    }

    private static List<string> DeserializeList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
