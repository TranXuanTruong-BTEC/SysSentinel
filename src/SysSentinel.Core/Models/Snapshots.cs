namespace SysSentinel.Core.Models;

public sealed record NetworkConnectionSnapshot(
    string Protocol,          // "TCP" | "UDP"
    string LocalAddress,
    int LocalPort,
    string? RemoteAddress,
    int? RemotePort,
    string State,             // "Established", "Listen", ...
    int OwningPid,
    string? ProcessName);
