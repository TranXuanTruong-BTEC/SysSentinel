namespace SysSentinel.Core.Models;

public sealed record ProcessSnapshot(
    int Pid,
    string Name,
    string? ExecutablePath,
    double CpuPercent,
    long WorkingSetBytes,
    int ThreadCount,
    int SessionId,
    string? PriorityClass,
    DateTime? StartTimeUtc);

public sealed record NetworkConnectionSnapshot(
    string Protocol,          // "TCP" | "UDP"
    string LocalAddress,
    int LocalPort,
    string? RemoteAddress,
    int? RemotePort,
    string State,             // "Established", "Listen", ...
    int OwningPid,
    string? ProcessName);
