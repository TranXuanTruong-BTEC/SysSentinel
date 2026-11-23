namespace SysSentinel.Core.Models;

public sealed class AuditLog
{
    public long Id { get; set; }
    public string Category { get; set; } = AuditCategories.System;
    public string ActionName { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public AuditStatus Status { get; set; } = AuditStatus.Success;
    public RollbackType RollbackType { get; set; } = RollbackType.None;

    /// <summary>Trạng thái gốc (JSON) trước khi thao tác, dùng để khôi phục.</summary>
    public string? OriginalStateJson { get; set; }

    public bool IsRolledBack { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
