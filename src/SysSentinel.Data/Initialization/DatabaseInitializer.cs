using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SysSentinel.Core.Interfaces;
using SysSentinel.Data.Connection;
using SysSentinel.Data.Internal;

namespace SysSentinel.Data.Initialization;

public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(ISqliteConnectionFactory factory, ILogger<DatabaseInitializer> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var path = _factory.DatabasePath;
        try
        {
            _logger.LogInformation("Khởi tạo database tại: {Path}", path);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await using var conn = await _factory.OpenConnectionAsync(ct);

            await EnableWalAsync(conn, ct);
            await VerifyForeignKeysAsync(conn, ct);
            await VerifyIntegrityAsync(conn, ct);
            await ApplyMigrationsAsync(conn, ct);
            await SeedIfEmptyAsync(conn, ct);

            _logger.LogInformation("Database sẵn sàng (schema v{Version}).", Migrations.LatestVersion);
        }
        catch (Exception ex) when (ex is SqliteException or IOException
                                      or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogCritical(ex, "KHỞI TẠO DATABASE THẤT BẠI. Path={Path}. Lý do: {Reason}", path, ex.Message);
            throw new DatabaseInitializationException(
                $"Không thể khởi tạo cơ sở dữ liệu tại '{path}': {ex.Message}", ex);
        }
    }

    private async Task EnableWalAsync(SqliteConnection conn, CancellationToken ct)
    {
        // WAL được lưu bền trong file DB; cho phép đọc/ghi đồng thời tốt hơn (collector + UI).
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL;";
        var mode = Convert.ToString(await cmd.ExecuteScalarAsync(ct));
        _logger.LogDebug("journal_mode = {Mode}", mode);
    }

    private static async Task VerifyForeignKeysAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys;";
        var value = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        if (value != 1)
            throw new InvalidOperationException("PRAGMA foreign_keys không bật được trên connection này.");
    }

    private static async Task VerifyIntegrityAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(await cmd.ExecuteScalarAsync(ct));
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"File database bị hỏng (quick_check: {result}).");
    }

    private async Task ApplyMigrationsAsync(SqliteConnection conn, CancellationToken ct)
    {
        int current;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            current = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        if (current > Migrations.LatestVersion)
            throw new InvalidOperationException(
                $"Database có schema v{current} mới hơn ứng dụng (v{Migrations.LatestVersion}). Hãy cập nhật SysSentinel.");

        foreach (var migration in Migrations.All.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            _logger.LogInformation("Áp dụng migration v{Version}: {Description}", migration.Version, migration.Description);

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = migration.Sql;
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var setVersion = conn.CreateCommand())
                {
                    setVersion.Transaction = tx;
                    // PRAGMA không hỗ trợ parameter; giá trị là int hằng số nội bộ nên an toàn.
                    setVersion.CommandText = $"PRAGMA user_version = {migration.Version};";
                    await setVersion.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(CancellationToken.None);
                _logger.LogError("Migration v{Version} thất bại, đã rollback.", migration.Version);
                throw;
            }
        }
    }

    private async Task SeedIfEmptyAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            long count;
            await using (var countCmd = conn.CreateCommand())
            {
                countCmd.Transaction = tx;
                countCmd.CommandText = "SELECT COUNT(*) FROM Profiles;";
                count = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));
            }

            if (count > 0)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return;
            }

            foreach (var p in SeedData.DefaultProfiles())
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO Profiles
                        (Name, Description, PowerPlanGuid, IsFocusModeEnabled, AutoTriggerProcesses, IsActive, CreatedAt)
                    VALUES
                        ($name, $desc, $plan, $focus, $triggers, $active, $created);
                    """;
                cmd.AddParam("$name", p.Name);
                cmd.AddParam("$desc", p.Description);
                cmd.AddParam("$plan", p.PowerPlanGuid);
                cmd.AddParam("$focus", p.IsFocusModeEnabled ? 1 : 0);
                cmd.AddParam("$triggers", JsonSerializer.Serialize(p.AutoTriggerProcesses));
                cmd.AddParam("$active", p.IsActive ? 1 : 0);
                cmd.AddParam("$created", p.CreatedAt.ToDb());
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            _logger.LogInformation("Đã seed {Count} profile mặc định.", SeedData.DefaultProfiles().Count);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
