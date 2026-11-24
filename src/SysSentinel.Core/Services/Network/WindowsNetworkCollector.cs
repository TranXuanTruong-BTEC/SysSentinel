using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;
using SysSentinel.Core.Native;

namespace SysSentinel.Core.Services.Network;

/// <summary>
/// Đọc bảng kết nối TCP/UDP (IPv4 + IPv6) bằng GetExtendedTcpTable / GetExtendedUdpTable (iphlpapi).
/// Mỗi bảng: hỏi kích thước -> AllocHGlobal -> đọc -> luôn FreeHGlobal trong finally.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsNetworkCollector : INetworkCollector
{
    private delegate uint TableFetcher(IntPtr buffer, ref uint size);

    // Kích thước hàng (byte) của MIB_TCPROW_OWNER_PID / MIB_TCP6ROW_OWNER_PID / MIB_UDPROW_OWNER_PID / MIB_UDP6ROW_OWNER_PID.
    private const int Tcp4RowSize = 24;
    private const int Tcp6RowSize = 56;
    private const int Udp4RowSize = 12;
    private const int Udp6RowSize = 28;

    private const int MaxBufferAttempts = 5;
    private const uint MaxTableBytes = 256u * 1024 * 1024;
    private const int BufferHeadroom = 4096;     // bảng có thể phình ra giữa hai lần gọi API

    private readonly IGeoIpResolver _geo;
    private readonly NetworkCollectorOptions _options;
    private readonly ILogger<WindowsNetworkCollector> _logger;

    public WindowsNetworkCollector(
        IGeoIpResolver geo, NetworkCollectorOptions options, ILogger<WindowsNetworkCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(geo);
        ArgumentNullException.ThrowIfNull(options);
        _geo = geo;
        _options = options;
        _logger = logger;
    }

    public Task<IReadOnlyList<NetworkConnectionInfo>> CollectAsync(CancellationToken ct = default)
        => Task.Run(() => CollectCore(ct), ct);

    // ------------------------------------------------------------------ quét
    private IReadOnlyList<NetworkConnectionInfo> CollectCore(CancellationToken ct)
    {
        long started = Stopwatch.GetTimestamp();
        var result = new List<NetworkConnectionInfo>(512);
        int attempted = 0, failed = 0;

        if (_options.IncludeTcp)
        {
            if (_options.IncludeIPv4)
            {
                attempted++;
                if (!ReadTable("TCP/IPv4", FetchTcp4, Tcp4RowSize, (p, n) => ParseTcp4(p, n, result), ct)) failed++;
            }
            if (_options.IncludeIPv6)
            {
                attempted++;
                if (!ReadTable("TCP/IPv6", FetchTcp6, Tcp6RowSize, (p, n) => ParseTcp6(p, n, result), ct)) failed++;
            }
        }

        if (_options.IncludeUdp)
        {
            if (_options.IncludeIPv4)
            {
                attempted++;
                if (!ReadTable("UDP/IPv4", FetchUdp4, Udp4RowSize, (p, n) => ParseUdp4(p, n, result), ct)) failed++;
            }
            if (_options.IncludeIPv6)
            {
                attempted++;
                if (!ReadTable("UDP/IPv6", FetchUdp6, Udp6RowSize, (p, n) => ParseUdp6(p, n, result), ct)) failed++;
            }
        }

        if (attempted > 0 && failed == attempted)
            _logger.LogError("Không đọc được bất kỳ bảng kết nối nào (iphlpapi). Trả về danh sách rỗng.");

        result.Sort(CompareConnections);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Quét {Count} kết nối ({Failed}/{Attempted} bảng lỗi) trong {Ms:F1} ms",
                result.Count, failed, attempted, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return result;
    }

    /// <summary>
    /// Cấp phát buffer động theo đúng quy trình của IP Helper và luôn giải phóng trong finally.
    /// Trả về true nếu bảng được đọc xong (hoặc không áp dụng, ví dụ máy tắt IPv6); false nếu lỗi thật sự.
    /// </summary>
    private bool ReadTable(string name, TableFetcher fetch, int rowSize, Action<IntPtr, int> parseRows, CancellationToken ct)
    {
        IntPtr buffer = IntPtr.Zero;
        uint size = 0;
        try
        {
            for (int attempt = 0; attempt < MaxBufferAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                uint rc = fetch(buffer, ref size);

                if (rc == IpHelperNativeMethods.NO_ERROR)
                {
                    if (buffer == IntPtr.Zero || size < 4) return true;

                    int declared = Marshal.ReadInt32(buffer);
                    long fitting = ((long)size - 4) / rowSize;                  // không bao giờ đọc vượt buffer
                    int count = (int)Math.Min(Math.Max(declared, 0), fitting);
                    parseRows(IntPtr.Add(buffer, 4), count);
                    return true;
                }

                if (rc == IpHelperNativeMethods.ERROR_INSUFFICIENT_BUFFER)
                {
                    if (size == 0 || size > MaxTableBytes)
                    {
                        _logger.LogWarning("Bảng {Table} yêu cầu kích thước bất thường: {Size} byte.", name, size);
                        return false;
                    }

                    if (buffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(buffer);
                        buffer = IntPtr.Zero;                                   // tránh double-free nếu AllocHGlobal bên dưới ném lỗi
                    }

                    size += BufferHeadroom;
                    buffer = Marshal.AllocHGlobal(checked((int)size));
                    continue;
                }

                if (rc == IpHelperNativeMethods.ERROR_NOT_SUPPORTED)
                {
                    _logger.LogDebug("Bảng {Table} không được hỗ trợ trên máy này (ví dụ đã tắt IPv6).", name);
                    return true;
                }

                _logger.LogWarning("GetExtended*Table({Table}) thất bại với mã lỗi {Code}.", name, rc);
                return false;
            }

            _logger.LogWarning("Bảng {Table} liên tục phình to, bỏ qua lần quét này.", name);
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    // ------------------------------------------------------------------ fetchers
    private static uint FetchTcp4(IntPtr buffer, ref uint size) =>
        IpHelperNativeMethods.GetExtendedTcpTable(buffer, ref size, false,
            IpHelperNativeMethods.AF_INET, IpHelperNativeMethods.TCP_TABLE_OWNER_PID_ALL, 0);

    private static uint FetchTcp6(IntPtr buffer, ref uint size) =>
        IpHelperNativeMethods.GetExtendedTcpTable(buffer, ref size, false,
            IpHelperNativeMethods.AF_INET6, IpHelperNativeMethods.TCP_TABLE_OWNER_PID_ALL, 0);

    private static uint FetchUdp4(IntPtr buffer, ref uint size) =>
        IpHelperNativeMethods.GetExtendedUdpTable(buffer, ref size, false,
            IpHelperNativeMethods.AF_INET, IpHelperNativeMethods.UDP_TABLE_OWNER_PID, 0);

    private static uint FetchUdp6(IntPtr buffer, ref uint size) =>
        IpHelperNativeMethods.GetExtendedUdpTable(buffer, ref size, false,
            IpHelperNativeMethods.AF_INET6, IpHelperNativeMethods.UDP_TABLE_OWNER_PID, 0);

    // ------------------------------------------------------------------ parsers
    // MIB_TCPROW_OWNER_PID: state(0) localAddr(4) localPort(8) remoteAddr(12) remotePort(16) pid(20)
    private void ParseTcp4(IntPtr rows, int count, List<NetworkConnectionInfo> output)
    {
        for (int i = 0; i < count; i++)
        {
            IntPtr row = IntPtr.Add(rows, i * Tcp4RowSize);
            TcpConnectionState state = ToState(Marshal.ReadInt32(row, 0));
            if (!_options.IncludeListening && state == TcpConnectionState.Listen) continue;

            int localAddr = Marshal.ReadInt32(row, 4);
            int localPort = DecodePort(Marshal.ReadInt32(row, 8));
            int remoteAddr = Marshal.ReadInt32(row, 12);
            int remotePortRaw = Marshal.ReadInt32(row, 16);
            int pid = Marshal.ReadInt32(row, 20);

            IPAddress? remote = null;
            int? remotePort = null;
            if (remoteAddr != 0 || (remotePortRaw & 0xFFFF) != 0)
            {
                remote = DecodeV4(remoteAddr);
                remotePort = DecodePort(remotePortRaw);
            }

            output.Add(Build(NetworkProtocol.Tcp, DecodeV4(localAddr), localPort, remote, remotePort, state, pid));
        }
    }

    // MIB_TCP6ROW_OWNER_PID: localAddr[16](0) localScope(16) localPort(20) remoteAddr[16](24) remoteScope(40) remotePort(44) state(48) pid(52)
    private void ParseTcp6(IntPtr rows, int count, List<NetworkConnectionInfo> output)
    {
        for (int i = 0; i < count; i++)
        {
            IntPtr row = IntPtr.Add(rows, i * Tcp6RowSize);
            TcpConnectionState state = ToState(Marshal.ReadInt32(row, 48));
            if (!_options.IncludeListening && state == TcpConnectionState.Listen) continue;

            int localPort = DecodePort(Marshal.ReadInt32(row, 20));
            int remotePortRaw = Marshal.ReadInt32(row, 44);
            int pid = Marshal.ReadInt32(row, 52);

            IPAddress? remote = null;
            int? remotePort = null;
            if (!IsAllZero(row, 24) || (remotePortRaw & 0xFFFF) != 0)
            {
                remote = DecodeV6(row, 24, 40);
                remotePort = DecodePort(remotePortRaw);
            }

            output.Add(Build(NetworkProtocol.Tcp, DecodeV6(row, 0, 16), localPort, remote, remotePort, state, pid));
        }
    }

    // MIB_UDPROW_OWNER_PID: localAddr(0) localPort(4) pid(8)
    private void ParseUdp4(IntPtr rows, int count, List<NetworkConnectionInfo> output)
    {
        for (int i = 0; i < count; i++)
        {
            IntPtr row = IntPtr.Add(rows, i * Udp4RowSize);
            int localAddr = Marshal.ReadInt32(row, 0);
            int localPort = DecodePort(Marshal.ReadInt32(row, 4));
            int pid = Marshal.ReadInt32(row, 8);
            output.Add(Build(NetworkProtocol.Udp, DecodeV4(localAddr), localPort, null, null, null, pid));
        }
    }

    // MIB_UDP6ROW_OWNER_PID: localAddr[16](0) localScope(16) localPort(20) pid(24)
    private void ParseUdp6(IntPtr rows, int count, List<NetworkConnectionInfo> output)
    {
        for (int i = 0; i < count; i++)
        {
            IntPtr row = IntPtr.Add(rows, i * Udp6RowSize);
            int localPort = DecodePort(Marshal.ReadInt32(row, 20));
            int pid = Marshal.ReadInt32(row, 24);
            output.Add(Build(NetworkProtocol.Udp, DecodeV6(row, 0, 16), localPort, null, null, null, pid));
        }
    }

    private NetworkConnectionInfo Build(
        NetworkProtocol protocol, IPAddress local, int localPort,
        IPAddress? remote, int? remotePort, TcpConnectionState? state, int pid)
    {
        GeoIpResult geo = _options.ResolveGeo && remote is not null
            ? _geo.Resolve(remote)
            : GeoIpResult.Unspecified;
        return new NetworkConnectionInfo(protocol, local, localPort, remote, remotePort, state, pid, geo);
    }

    // ------------------------------------------------------------------ giải mã
    /// <summary>Port lưu ở 16 bit thấp của DWORD theo network byte order -> host order.</summary>
    private static int DecodePort(int raw) => BinaryPrimitives.ReverseEndianness((ushort)(raw & 0xFFFF));

    /// <summary>DWORD IPv4 theo network order: byte nhớ [a,b,c,d] -> a.b.c.d.</summary>
    private static IPAddress DecodeV4(int raw)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, raw);
        return new IPAddress(bytes);
    }

    private static IPAddress DecodeV6(IntPtr row, int addressOffset, int scopeOffset)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, Marshal.ReadInt64(row, addressOffset));
        BinaryPrimitives.WriteInt64LittleEndian(bytes[8..], Marshal.ReadInt64(row, addressOffset + 8));
        long scope = (uint)Marshal.ReadInt32(row, scopeOffset);
        return scope == 0 ? new IPAddress(bytes) : new IPAddress(bytes, scope);
    }

    private static bool IsAllZero(IntPtr row, int offset) =>
        Marshal.ReadInt64(row, offset) == 0 && Marshal.ReadInt64(row, offset + 8) == 0;

    private static TcpConnectionState ToState(int raw) =>
        raw is >= 1 and <= 12 ? (TcpConnectionState)raw : TcpConnectionState.Unknown;

    private static int CompareConnections(NetworkConnectionInfo a, NetworkConnectionInfo b)
    {
        int c = a.Pid.CompareTo(b.Pid);
        if (c != 0) return c;
        c = a.Protocol.CompareTo(b.Protocol);
        if (c != 0) return c;
        c = a.LocalPort.CompareTo(b.LocalPort);
        if (c != 0) return c;
        return Nullable.Compare(a.RemotePort, b.RemotePort);
    }
}
