namespace SysSentinel.Core.Models;

/// <summary>Tổng hợp các kết nối mạng của một tiến trình (ghép với <see cref="ProcessInfo"/> của Ngày 2 qua PID).</summary>
public sealed record ProcessNetworkSummary(
    int Pid,
    string ProcessName,
    string? ExecutablePath,
    SignatureStatus Signature,
    bool ProcessFound,
    ProcessInfo? Process,
    IReadOnlyList<NetworkConnectionInfo> Connections,
    int EstablishedCount,
    int ListeningCount,
    int UdpEndpointCount,
    int RemoteHostCount,
    IReadOnlyList<string> RemoteCountries)
{
    public int TotalConnections => Connections.Count;

    /// <summary>PID 0 (Idle) hoặc 4 (System): không phải tiến trình người dùng.</summary>
    public bool IsSystemPseudoProcess => Pid is 0 or 4;
}
