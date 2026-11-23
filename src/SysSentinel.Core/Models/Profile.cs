namespace SysSentinel.Core.Models;

public sealed class Profile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>GUID power plan của Windows (powercfg). Null = không đổi.</summary>
    public string? PowerPlanGuid { get; set; }

    public bool IsFocusModeEnabled { get; set; }

    /// <summary>Danh sách tên process kích hoạt profile tự động (lưu JSON trong DB).</summary>
    public List<string> AutoTriggerProcesses { get; set; } = new();

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>GUID power plan chuẩn của Windows.</summary>
public static class PowerPlans
{
    public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string PowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
}
