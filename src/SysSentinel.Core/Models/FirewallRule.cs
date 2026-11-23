namespace SysSentinel.Core.Models;

/// <summary>Rule tường lửa do SysSentinel tạo (tên rule luôn có tiền tố "SysSentinel_").</summary>
public sealed class FirewallRule
{
    public const string NamePrefix = "SysSentinel_";

    public int Id { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public string? RemoteIp { get; set; }
    public FirewallDirection Direction { get; set; } = FirewallDirection.Outbound;
    public FirewallAction Action { get; set; } = FirewallAction.Block;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
