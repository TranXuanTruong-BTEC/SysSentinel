using SysSentinel.Core.Models;
namespace SysSentinel.Core.Interfaces;

public interface IProfileRepository
{
    Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken ct = default);
    Task<Profile?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Profile?> GetActiveAsync(CancellationToken ct = default);
    Task<int> AddAsync(Profile profile, CancellationToken ct = default);
    Task<bool> UpdateAsync(Profile profile, CancellationToken ct = default);
    /// <summary>Không xóa được profile đang active.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>Đặt profile active trong 1 transaction (tắt cái cũ, bật cái mới).</summary>
    Task<bool> SetActiveAsync(int id, CancellationToken ct = default);
}

public interface IAutomationRuleRepository
{
    Task<IReadOnlyList<AutomationRule>> GetByProfileAsync(int profileId, bool onlyEnabled = false, CancellationToken ct = default);
    Task<int> AddAsync(AutomationRule rule, CancellationToken ct = default);
    Task<bool> UpdateAsync(AutomationRule rule, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

public interface IFirewallRuleRepository
{
    Task<IReadOnlyList<FirewallRule>> GetAllAsync(CancellationToken ct = default);
    Task<FirewallRule?> GetByNameAsync(string ruleName, CancellationToken ct = default);
    Task<int> AddAsync(FirewallRule rule, CancellationToken ct = default);
    Task<bool> SetEnabledAsync(int id, bool enabled, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

public interface IAuditLogRepository
{
    Task<long> AddAsync(AuditLog log, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> GetRecentAsync(int limit = 200, CancellationToken ct = default);
    Task<AuditLog?> GetByIdAsync(long id, CancellationToken ct = default);
    /// <summary>Các log còn khôi phục được (RollbackType != None và chưa rollback).</summary>
    Task<IReadOnlyList<AuditLog>> GetPendingRollbackAsync(int limit = 100, CancellationToken ct = default);
    Task<bool> MarkRolledBackAsync(long id, CancellationToken ct = default);
}
