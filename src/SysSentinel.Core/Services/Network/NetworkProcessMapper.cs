using System.Net;
using SysSentinel.Core.Models;

namespace SysSentinel.Core.Services.Network;

/// <summary>
/// Ghép danh sách kết nối với danh sách tiến trình (Ngày 2) theo PID.
/// Lưu ý: hai danh sách được chụp ở hai thời điểm khác nhau nên một PID có thể đã thoát hoặc bị tái sử dụng
/// giữa hai lần chụp; trường hợp không tìm thấy tiến trình được gắn nhãn rõ ràng thay vì ném lỗi.
/// </summary>
public static class NetworkProcessMapper
{
    public static IReadOnlyList<ProcessNetworkSummary> Map(
        IReadOnlyList<NetworkConnectionInfo> connections, IReadOnlyList<ProcessInfo> processes)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(processes);

        var byPid = new Dictionary<int, ProcessInfo>(processes.Count);
        foreach (ProcessInfo p in processes) byPid.TryAdd(p.Pid, p);

        var groups = new Dictionary<int, List<NetworkConnectionInfo>>();
        foreach (NetworkConnectionInfo c in connections)
        {
            if (!groups.TryGetValue(c.Pid, out List<NetworkConnectionInfo>? list))
            {
                list = new List<NetworkConnectionInfo>();
                groups[c.Pid] = list;
            }
            list.Add(c);
        }

        var result = new List<ProcessNetworkSummary>(groups.Count);
        foreach (KeyValuePair<int, List<NetworkConnectionInfo>> kv in groups)
        {
            int pid = kv.Key;
            List<NetworkConnectionInfo> conns = kv.Value;
            byPid.TryGetValue(pid, out ProcessInfo? process);

            int established = 0, listening = 0, udp = 0;
            var remoteHosts = new HashSet<IPAddress>();
            var countries = new SortedSet<string>(StringComparer.Ordinal);

            foreach (NetworkConnectionInfo c in conns)
            {
                if (c.Protocol == NetworkProtocol.Udp) udp++;
                else if (c.TcpState == TcpConnectionState.Established) established++;
                else if (c.TcpState == TcpConnectionState.Listen) listening++;

                if (c.RemoteAddress is not null) remoteHosts.Add(c.RemoteAddress);
                if (c.RemoteGeo.Scope == GeoIpScope.Public && c.RemoteGeo.CountryCode is not null)
                    countries.Add(c.RemoteGeo.CountryCode);
            }

            result.Add(new ProcessNetworkSummary(
                Pid: pid,
                ProcessName: process?.Name ?? FallbackName(pid),
                ExecutablePath: process?.ExecutablePath,
                Signature: process?.Signature ?? SignatureStatus.Unknown,
                ProcessFound: process is not null,
                Process: process,
                Connections: conns,
                EstablishedCount: established,
                ListeningCount: listening,
                UdpEndpointCount: udp,
                RemoteHostCount: remoteHosts.Count,
                RemoteCountries: countries.ToList()));
        }

        result.Sort((a, b) =>
        {
            int c = b.TotalConnections.CompareTo(a.TotalConnections);
            return c != 0 ? c : a.Pid.CompareTo(b.Pid);
        });
        return result;
    }

    /// <summary>
    /// PID 0: socket TCP không còn chủ (thường TIME_WAIT) do hệ thống giữ; PID 4: kernel (HTTP.sys, SMB...).
    /// PID khác không có trong danh sách tiến trình: đã thoát trước khi ghép.
    /// </summary>
    private static string FallbackName(int pid) => pid switch
    {
        0 => "System Idle Process",
        4 => "System",
        _ => $"<đã thoát> (PID {pid})"
    };
}
