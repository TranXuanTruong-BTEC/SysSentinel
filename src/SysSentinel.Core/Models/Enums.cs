namespace SysSentinel.Core.Models;

public enum FirewallDirection { Inbound, Outbound }

public enum FirewallAction { Allow, Block }

public enum AuditStatus { Success, Failed, Skipped }

/// <summary>Loại hành động khôi phục - mỗi loại có một IRollbackHandler tương ứng.</summary>
public enum RollbackType
{
    None,
    FirewallRule,
    ProcessPriority,
    ProcessAffinity,
    PowerPlan,
    HostsFile,
    ServiceState,
    ProfileSwitch
}

public enum RuleConditionType
{
    ProcessRunning,
    ProcessNotRunning,
    TimeRange,
    CpuAbove,
    MemoryAbove,
    OnBattery,
    NetworkConnected
}

public enum RuleActionType
{
    SetPowerPlan,
    SetProcessPriority,
    SuspendProcess,
    TerminateProcess,
    EnableFocusMode,
    BlockApplication
}

/// <summary>Hằng số Category cho AuditLogs để tránh "magic string".</summary>
public static class AuditCategories
{
    public const string System = "System";
    public const string Process = "Process";
    public const string Network = "Network";
    public const string Firewall = "Firewall";
    public const string Hosts = "Hosts";
    public const string Power = "Power";
    public const string Profile = "Profile";
    public const string Database = "Database";
}
