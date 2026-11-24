using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;
using SysSentinel.Core.Services.Network;
using SysSentinel.Core.Services.Processes;
using SysSentinel.Core.Services.Security;

namespace SysSentinel.ScanTest;

/// <summary>
/// Kiểm thử độc lập cho Network Telemetry Collector (Ngày 3):
/// 1) phân loại IP / GeoIP, 2) tự kiểm tra giải mã port & IP bằng socket thật,
/// 3) đo thời gian quét, 4) đo CPU khi quét mỗi 2 giây, 5) in bảng đối chiếu với TCPView.
/// </summary>
public static class NetworkCollectorDemoTest
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);
    private const double CpuBudgetPercent = 1.5;

    public static async Task<bool> RunAsync(
        TextWriter o, int maxRows = 60, int cpuCycles = 6, CancellationToken ct = default)
    {
        bool allOk = true;
        Line(o, '=');
        o.WriteLine("SysSentinel - Ngày 3: Network Telemetry Collector (demo / nghiệm thu)");
        o.WriteLine($"CPU logic: {Environment.ProcessorCount} | 64-bit: {Environment.Is64BitProcess} | PID của tool: {Environment.ProcessId}");

        var geoOptions = new OfflineGeoIpOptions();
        using var geo = new OfflineGeoIpResolver(geoOptions, NullLogger<OfflineGeoIpResolver>.Instance);
        var netCollector = new WindowsNetworkCollector(
            geo, new NetworkCollectorOptions(), NullLogger<WindowsNetworkCollector>.Instance);
        var verifier = new AuthenticodeSignatureVerifier(
            new SignatureVerifierOptions(), NullLogger<AuthenticodeSignatureVerifier>.Instance);
        using var procCollector = new WindowsProcessCollector(
            verifier,
            new ProcessCollectorOptions { SignatureMode = SignatureCheckMode.Blocking },
            NullLogger<WindowsProcessCollector>.Instance);

        // ---------------------------------------------------------------- 1. GeoIP
        o.WriteLine();
        o.WriteLine("[1] GeoIP ngoại tuyến");
        o.WriteLine(geo.IsDatabaseLoaded
            ? $"    Đã nạp database: {geo.DatabasePath}"
            : "    CHƯA có file .mmdb -> chế độ dự phòng (IP công cộng = Unknown). Đặt GeoLite2-Country.mmdb vào một trong:");
        if (!geo.IsDatabaseLoaded)
            foreach (string p in geoOptions.EnumerateCandidatePaths()) o.WriteLine($"      - {p}");

        (string Ip, GeoIpScope Expected)[] cases =
        {
            ("127.0.0.1", GeoIpScope.LocalNetwork), ("10.1.2.3", GeoIpScope.LocalNetwork),
            ("172.16.0.1", GeoIpScope.LocalNetwork), ("172.31.255.255", GeoIpScope.LocalNetwork),
            ("172.32.0.1", GeoIpScope.Public), ("192.168.1.1", GeoIpScope.LocalNetwork),
            ("169.254.10.10", GeoIpScope.LocalNetwork), ("224.0.0.251", GeoIpScope.LocalNetwork),
            ("100.64.0.1", GeoIpScope.LocalNetwork), ("0.0.0.0", GeoIpScope.Unspecified),
            ("8.8.8.8", GeoIpScope.Public), ("1.1.1.1", GeoIpScope.Public),
            ("::1", GeoIpScope.LocalNetwork), ("fe80::1", GeoIpScope.LocalNetwork),
            ("fc00::1", GeoIpScope.LocalNetwork), ("ff02::1", GeoIpScope.LocalNetwork),
            ("::", GeoIpScope.Unspecified), ("2001:4860:4860::8888", GeoIpScope.Public),
            ("::ffff:192.168.1.5", GeoIpScope.LocalNetwork), ("::ffff:8.8.8.8", GeoIpScope.Public)
        };
        foreach ((string ip, GeoIpScope expected) in cases)
        {
            GeoIpResult r = geo.Resolve(IPAddress.Parse(ip));
            allOk &= Check(o, $"{ip,-24} -> {r.Label}", r.Scope == expected, $"mong đợi {expected}");
        }

        // ---------------------------------------------------------------- 2. Socket thật
        o.WriteLine();
        o.WriteLine("[2] Tự kiểm tra giải mã port / IP / PID bằng socket thật của chính tool");
        allOk &= await RunSocketSelfTestAsync(o, netCollector, ct);

        // ---------------------------------------------------------------- 3. Thời gian quét
        o.WriteLine();
        o.WriteLine("[3] Thời gian quét");
        await netCollector.CollectAsync(ct);          // làm nóng JIT
        await procCollector.CollectAsync(ct);         // nạp cache chữ ký
        var times = new List<double>();
        IReadOnlyList<NetworkConnectionInfo> conns = Array.Empty<NetworkConnectionInfo>();
        for (int i = 0; i < 10; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            conns = await netCollector.CollectAsync(ct);
            times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        }
        o.WriteLine($"    Quét x10: min {times.Min():F1} | avg {times.Average():F1} | max {times.Max():F1} ms");
        o.WriteLine($"    {conns.Count} dòng: TCP4 {Count(conns, NetworkProtocol.Tcp, false)} | TCP6 {Count(conns, NetworkProtocol.Tcp, true)} | " +
                    $"UDP4 {Count(conns, NetworkProtocol.Udp, false)} | UDP6 {Count(conns, NetworkProtocol.Udp, true)} | cache GeoIP {geo.CachedCount} IP");

        // ---------------------------------------------------------------- 4. CPU
        o.WriteLine();
        o.WriteLine($"[4] CPU khi quét mỗi {ScanInterval.TotalSeconds:F0} giây ({cpuCycles} chu kỳ mỗi phép đo, mục tiêu < {CpuBudgetPercent}% tổng CPU)");
        (double totalNet, double coreNet, double msNet) = await MeasureCpuAsync(
            () => netCollector.CollectAsync(ct), cpuCycles, ct);
        o.WriteLine($"    Chỉ network:        {totalNet,6:F3}% tổng CPU | {coreNet,6:F3}% của 1 core | {msNet:F1} ms/lần");

        (double totalAll, double coreAll, double msAll) = await MeasureCpuAsync(async () =>
        {
            IReadOnlyList<NetworkConnectionInfo> c = await netCollector.CollectAsync(ct);
            IReadOnlyList<ProcessInfo> p = await procCollector.CollectAsync(ct);
            NetworkProcessMapper.Map(c, p);
        }, cpuCycles, ct);
        o.WriteLine($"    Network + tiến trình + ghép PID: {totalAll,6:F3}% tổng CPU | {coreAll,6:F3}% của 1 core | {msAll:F1} ms/lần");
        allOk &= Check(o, $"CPU network + process + map < {CpuBudgetPercent}% tổng CPU", totalAll < CpuBudgetPercent, $"đo được {totalAll:F3}%");

        // ---------------------------------------------------------------- 5. Bảng đối chiếu TCPView
        IReadOnlyList<NetworkConnectionInfo> snapshot = await netCollector.CollectAsync(ct);
        IReadOnlyList<ProcessInfo> procs = await procCollector.CollectAsync(ct);
        var names = new Dictionary<int, string>(procs.Count);
        foreach (ProcessInfo p in procs) names.TryAdd(p.Pid, p.Name);

        o.WriteLine();
        Line(o, '=');
        o.WriteLine("[5] Bảng kết nối (đối chiếu với TCPView của Sysinternals: cùng Process / PID / Proto / Local / Remote / State)");
        o.WriteLine($"{"Process",-22} {"PID",6} {"Proto",-5} {"Local address",-30} {"Remote address",-30} {"State",-12} Quốc gia");

        var ordered = snapshot
            .OrderBy(c => names.TryGetValue(c.Pid, out string? n) ? n : "~", StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Pid).ThenBy(c => c.Protocol).ThenBy(c => c.LocalPort)
            .ToList();
        int printed = 0;
        foreach (NetworkConnectionInfo c in ordered)
        {
            if (printed >= maxRows) break;
            string name = names.TryGetValue(c.Pid, out string? n) ? n : (c.Pid == 0 ? "System Idle Process" : "<đã thoát>");
            o.WriteLine($"{Trunc(name, 22),-22} {c.Pid,6} {ProtoLabel(c),-5} {Trunc(c.LocalEndpoint, 30),-30} " +
                        $"{Trunc(c.RemoteEndpoint ?? "*:*", 30),-30} {c.TcpState?.ToString() ?? "-",-12} {c.RemoteGeo.Label}");
            printed++;
        }
        if (ordered.Count > printed)
            o.WriteLine($"... còn {ordered.Count - printed} dòng (chạy:  dotnet run --project tools/SysSentinel.ScanTest -- network all)");

        IReadOnlyList<ProcessNetworkSummary> summaries = NetworkProcessMapper.Map(snapshot, procs);
        o.WriteLine();
        o.WriteLine("Top 10 tiến trình theo số kết nối:");
        o.WriteLine($"{"Process",-26} {"PID",6} {"Tổng",5} {"ESTAB",6} {"LISTEN",7} {"UDP",4} {"Host",5} {"Chữ ký",-10} Quốc gia đích");
        foreach (ProcessNetworkSummary s in summaries.Take(10))
        {
            o.WriteLine($"{Trunc(s.ProcessName, 26),-26} {s.Pid,6} {s.TotalConnections,5} {s.EstablishedCount,6} {s.ListeningCount,7} " +
                        $"{s.UdpEndpointCount,4} {s.RemoteHostCount,5} {s.Signature,-10} {string.Join(",", s.RemoteCountries)}");
        }
        int exited = summaries.Count(s => !s.ProcessFound && !s.IsSystemPseudoProcess);
        o.WriteLine();
        o.WriteLine($"Tổng {snapshot.Count} dòng | {summaries.Count} tiến trình có kết nối | " +
                    $"ESTABLISHED {snapshot.Count(c => c.TcpState == TcpConnectionState.Established)} | " +
                    $"LISTEN {snapshot.Count(c => c.TcpState == TcpConnectionState.Listen)} | " +
                    $"PID đã thoát/không map được {exited}");

        Line(o, '=');
        o.WriteLine(allOk ? "KẾT QUẢ: TẤT CẢ KIỂM TRA TỰ ĐỘNG ĐỀU PASS" : "KẾT QUẢ: CÓ KIỂM TRA FAIL (xem các dòng [FAIL] ở trên)");
        return allOk;
    }

    // ---------------------------------------------------------------------------------
    private static async Task<bool> RunSocketSelfTestAsync(TextWriter o, INetworkCollector collector, CancellationToken ct)
    {
        bool ok = true;
        int self = Environment.ProcessId;
        TcpListener? l4 = null, l6 = null;
        TcpClient? client = null, server = null;
        UdpClient? udp = null;
        try
        {
            l4 = new TcpListener(IPAddress.Loopback, 0);
            l4.Start();
            int port4 = ((IPEndPoint)l4.LocalEndpoint).Port;

            client = new TcpClient(AddressFamily.InterNetwork);
            ValueTask<TcpClient> acceptTask = l4.AcceptTcpClientAsync(ct);
            await client.ConnectAsync(IPAddress.Loopback, port4, ct);
            server = await acceptTask;
            int clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

            udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int udpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;

            int port6 = 0;
            try
            {
                l6 = new TcpListener(IPAddress.IPv6Loopback, 0);
                l6.Start();
                port6 = ((IPEndPoint)l6.LocalEndpoint).Port;
            }
            catch (SocketException)
            {
                l6?.Stop();
                l6 = null;
            }

            IReadOnlyList<NetworkConnectionInfo> all = await collector.CollectAsync(ct);

            ok &= Check(o, $"TCP LISTEN 127.0.0.1:{port4} thuộc PID {self}",
                all.Any(x => x.Protocol == NetworkProtocol.Tcp && x.TcpState == TcpConnectionState.Listen &&
                             x.LocalPort == port4 && x.LocalAddress.Equals(IPAddress.Loopback) && x.Pid == self));

            ok &= Check(o, $"TCP ESTABLISHED phía client {clientPort} -> {port4}",
                all.Any(x => x.Protocol == NetworkProtocol.Tcp && x.TcpState == TcpConnectionState.Established &&
                             x.LocalPort == clientPort && x.RemotePort == port4 &&
                             x.RemoteAddress != null && x.RemoteAddress.Equals(IPAddress.Loopback) && x.Pid == self));

            ok &= Check(o, $"TCP ESTABLISHED phía server {port4} <- {clientPort}",
                all.Any(x => x.Protocol == NetworkProtocol.Tcp && x.TcpState == TcpConnectionState.Established &&
                             x.LocalPort == port4 && x.RemotePort == clientPort && x.Pid == self));

            ok &= Check(o, $"UDP 127.0.0.1:{udpPort} thuộc PID {self}",
                all.Any(x => x.Protocol == NetworkProtocol.Udp && x.LocalPort == udpPort &&
                             x.LocalAddress.Equals(IPAddress.Loopback) && x.Pid == self));

            if (l6 is not null)
                ok &= Check(o, $"TCP6 LISTEN [::1]:{port6} thuộc PID {self}",
                    all.Any(x => x.Protocol == NetworkProtocol.Tcp && x.TcpState == TcpConnectionState.Listen &&
                                 x.LocalPort == port6 && x.LocalAddress.Equals(IPAddress.IPv6Loopback) && x.Pid == self));
            else
                o.WriteLine("  [SKIP] IPv6 không khả dụng trên máy này, bỏ qua kiểm tra TCP6.");

            ok &= Check(o, "Endpoint loopback được gắn nhãn Local / LAN",
                all.Where(x => x.Pid == self && x.RemoteAddress != null && x.RemoteAddress.Equals(IPAddress.Loopback))
                   .All(x => x.RemoteGeo.Scope == GeoIpScope.LocalNetwork));
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException or IOException)
        {
            ok &= Check(o, "Tạo socket thử nghiệm", false, ex.Message);
        }
        finally
        {
            server?.Dispose();
            client?.Dispose();
            udp?.Dispose();
            l4?.Stop();
            l6?.Stop();
        }
        return ok;
    }

    /// <summary>Chạy <paramref name="work"/> mỗi 2 giây trong N chu kỳ, trả về (%tổng CPU, %của 1 core, ms trung bình/lần).</summary>
    private static async Task<(double TotalPercent, double CorePercent, double AvgMs)> MeasureCpuAsync(
        Func<Task> work, int cycles, CancellationToken ct)
    {
        using Process self = Process.GetCurrentProcess();
        self.Refresh();
        TimeSpan cpuStart = self.TotalProcessorTime;
        long wallStart = Stopwatch.GetTimestamp();
        double sumMs = 0;

        for (int i = 0; i < cycles; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            await work();
            sumMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            await Task.Delay(ScanInterval, ct);
        }

        self.Refresh();
        double cpuSeconds = (self.TotalProcessorTime - cpuStart).TotalSeconds;
        double wallSeconds = Stopwatch.GetElapsedTime(wallStart).TotalSeconds;
        double corePercent = wallSeconds > 0 ? cpuSeconds / wallSeconds * 100.0 : 0;
        return (corePercent / Environment.ProcessorCount, corePercent, sumMs / Math.Max(1, cycles));
    }

    private static bool Check(TextWriter o, string name, bool ok, string? detail = null)
    {
        o.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(!ok && detail is not null ? "  -> " + detail : string.Empty)}");
        return ok;
    }

    private static int Count(IReadOnlyList<NetworkConnectionInfo> list, NetworkProtocol protocol, bool ipv6) =>
        list.Count(c => c.Protocol == protocol && c.IsIPv6 == ipv6);

    private static string ProtoLabel(NetworkConnectionInfo c) => (c.Protocol, c.IsIPv6) switch
    {
        (NetworkProtocol.Tcp, false) => "TCP",
        (NetworkProtocol.Tcp, true) => "TCP6",
        (NetworkProtocol.Udp, false) => "UDP",
        _ => "UDP6"
    };

    private static string Trunc(string s, int max) => s.Length <= max ? s : "..." + s[^(max - 3)..];

    private static void Line(TextWriter o, char c) => o.WriteLine(new string(c, 118));
}
