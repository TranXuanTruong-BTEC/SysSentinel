using System.Net;
using System.Net.Sockets;

namespace SysSentinel.Core.Models;

/// <summary>Một dòng của bảng kết nối (TCP) hoặc endpoint (UDP) kèm PID chủ sở hữu và quốc gia đích.</summary>
public sealed record NetworkConnectionInfo(
    NetworkProtocol Protocol,
    IPAddress LocalAddress,
    int LocalPort,
    IPAddress? RemoteAddress,
    int? RemotePort,
    TcpConnectionState? TcpState,
    int Pid,
    GeoIpResult RemoteGeo)
{
    public bool IsIPv6 => LocalAddress.AddressFamily == AddressFamily.InterNetworkV6;

    public string LocalEndpoint => Format(LocalAddress, LocalPort);

    public string? RemoteEndpoint =>
        RemoteAddress is null || RemotePort is null ? null : Format(RemoteAddress, RemotePort.Value);

    private static string Format(IPAddress address, int port) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]:{port}" : $"{address}:{port}";
}
