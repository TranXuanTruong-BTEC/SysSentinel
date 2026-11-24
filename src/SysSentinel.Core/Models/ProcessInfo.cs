namespace SysSentinel.Core.Models;

public sealed record ProcessInfo(
    int Pid,
    string Name,
    string? ExecutablePath,
    long WorkingSetBytes,
    long PrivateBytes,
    double CpuPercent,
    DateTime? StartTimeUtc,
    SignatureStatus Signature,
    bool IsAccessDenied)
{
    public double WorkingSetMb => WorkingSetBytes / 1048576.0;
    public double PrivateMb => PrivateBytes / 1048576.0;
}
