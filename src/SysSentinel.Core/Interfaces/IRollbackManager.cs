using SysSentinel.Core.Common;
using SysSentinel.Core.Models;
namespace SysSentinel.Core.Interfaces;

public interface IRollbackManager
{
    /// <summary>Ghi một hành động (kèm trạng thái gốc) vào AuditLogs, trả về Id.</summary>
    Task<long> RecordAsync(AuditLog entry, CancellationToken ct = default);

    Task<IReadOnlyList<AuditLog>> GetPendingAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>Khôi phục một hành động theo Id, điều phối tới IRollbackHandler phù hợp.</summary>
    Task<OperationResult> RollbackAsync(long auditLogId, CancellationToken ct = default);
}

/// <summary>Strategy: mỗi RollbackType có một handler riêng (đăng ký DI ở các ngày sau).</summary>
public interface IRollbackHandler
{
    RollbackType Type { get; }
    Task<OperationResult> RollbackAsync(AuditLog entry, CancellationToken ct = default);
}
