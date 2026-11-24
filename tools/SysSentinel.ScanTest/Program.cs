using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;
using SysSentinel.Core.Services.Processes;
using SysSentinel.Core.Services.Security;
using SysSentinel.ScanTest;

Console.OutputEncoding = Encoding.UTF8;

// Ngày 3:  dotnet run --project tools/SysSentinel.ScanTest -- network        (in 60 dòng đầu)
//          dotnet run --project tools/SysSentinel.ScanTest -- network all    (in toàn bộ)
if (args.Length > 0 && args[0].Equals("network", StringComparison.OrdinalIgnoreCase))
{
    int maxRows = args.Length > 1 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase) ? int.MaxValue : 60;
    bool ok = await NetworkCollectorDemoTest.RunAsync(Console.Out, maxRows);
    Environment.ExitCode = ok ? 0 : 1;
    return;
}

// Ngày 2: quét tiến trình + chữ ký số (chạy mặc định khi không có tham số)
Console.WriteLine($"CPU logic: {Environment.ProcessorCount} | 64-bit: {Environment.Is64BitProcess}");
Console.WriteLine(new string('=', 110));

// ===== Phần A: chế độ Background (chế độ dùng trong app thật) =====
var verifier = new AuthenticodeSignatureVerifier(
    new SignatureVerifierOptions(), NullLogger<AuthenticodeSignatureVerifier>.Instance);
using var collector = new WindowsProcessCollector(
    verifier,
    new ProcessCollectorOptions { SignatureMode = SignatureCheckMode.Background },
    NullLogger<WindowsProcessCollector>.Instance);

await collector.CollectAsync();   // làm nóng JIT, KHÔNG tính vào số đo
verifier.ClearCache();

await TimedScan("A1. Background, cache lạnh      ", collector);

var settle = Stopwatch.StartNew();
IReadOnlyList<ProcessInfo> list;
do
{
    await Task.Delay(500);
    list = await collector.CollectAsync();
} while (list.Any(p => p.Signature == SignatureStatus.Pending) && settle.Elapsed < TimeSpan.FromSeconds(120));
Console.WriteLine($"A2. Verify nền xong sau {settle.Elapsed.TotalSeconds:F1}s | cache = {verifier.CachedCount} file");

var warm = new List<double>();
for (int i = 0; i < 10; i++)
{
    var sw = Stopwatch.StartNew();
    await collector.CollectAsync();
    sw.Stop();
    warm.Add(sw.Elapsed.TotalMilliseconds);
    await Task.Delay(200);
}
Console.WriteLine($"A3. Quét có cache x10: min {warm.Min():F1} | avg {warm.Average():F1} | max {warm.Max():F1} ms   (mục tiêu < 200 ms)");
Console.WriteLine(new string('-', 110));

// ===== Phần B: chế độ Blocking, cache lạnh (có đủ kết quả chữ ký ngay lần đầu) =====
var coldVerifier = new AuthenticodeSignatureVerifier(
    new SignatureVerifierOptions(), NullLogger<AuthenticodeSignatureVerifier>.Instance);
using var blocking = new WindowsProcessCollector(
    coldVerifier,
    new ProcessCollectorOptions { SignatureMode = SignatureCheckMode.Blocking },
    NullLogger<WindowsProcessCollector>.Instance);

await TimedScan("B1. Blocking, cache lạnh (chậm là bình thường)", blocking);
await TimedScan("B2. Blocking, cache nóng        ", blocking);
Console.WriteLine(new string('-', 110));

// ===== Phần C: kiểm tra %CPU bằng cách đốt đúng 1 core trong chính tiến trình này =====
using var stop = new CancellationTokenSource();
Task spinner = Task.Run(() => { while (!stop.IsCancellationRequested) Thread.SpinWait(100); });
await collector.CollectAsync();
await Task.Delay(3000);
var cpuList = await collector.CollectAsync();
stop.Cancel();
await spinner;
ProcessInfo self = cpuList.First(p => p.Pid == Environment.ProcessId);
Console.WriteLine($"C. %CPU của chính tool: {self.CpuPercent:F1}%   (kỳ vọng ≈ {100.0 / Environment.ProcessorCount:F1}% = 1 core / {Environment.ProcessorCount} core)");
Console.WriteLine(new string('=', 110));

// ===== Phần D: in bảng để đối chiếu với Process Explorer =====
await Task.Delay(1000);
var snapshot = await collector.CollectAsync();
Console.WriteLine($"Tổng {snapshot.Count} tiến trình | Valid {snapshot.Count(p => p.Signature == SignatureStatus.Valid)} | " +
                  $"Unsigned {snapshot.Count(p => p.Signature == SignatureStatus.Unsigned)} | " +
                  $"Invalid {snapshot.Count(p => p.Signature == SignatureStatus.Invalid)} | " +
                  $"Unknown {snapshot.Count(p => p.Signature == SignatureStatus.Unknown)} | " +
                  $"Pending {snapshot.Count(p => p.Signature == SignatureStatus.Pending)} | " +
                  $"Bị từ chối truy cập {snapshot.Count(p => p.IsAccessDenied)}");
Console.WriteLine();
Console.WriteLine("Top 15 theo CPU:");
PrintTable(snapshot.OrderByDescending(p => p.CpuPercent).Take(15));
Console.WriteLine();
Console.WriteLine("Top 10 theo RAM (Working Set):");
PrintTable(snapshot.OrderByDescending(p => p.WorkingSetBytes).Take(10));
Console.WriteLine();
Console.WriteLine("Không phải Valid (Unsigned / Invalid / FileNotFound):");
PrintTable(snapshot
    .Where(p => p.Signature is SignatureStatus.Unsigned or SignatureStatus.Invalid or SignatureStatus.FileNotFound)
    .OrderBy(p => p.Signature).ThenBy(p => p.Name));

// ---------------------------------------------------------------------------
static async Task TimedScan(string label, IProcessCollector c)
{
    var sw = Stopwatch.StartNew();
    var l = await c.CollectAsync();
    sw.Stop();
    int pending = l.Count(p => p.Signature == SignatureStatus.Pending);
    Console.WriteLine($"{label}: {sw.Elapsed.TotalMilliseconds,8:F1} ms | {l.Count} tiến trình | Pending {pending}");
}

static void PrintTable(IEnumerable<ProcessInfo> items)
{
    Console.WriteLine($"{"PID",6} {"Tên",-26} {"CPU%",6} {"WS(MB)",9} {"Priv(MB)",9} {"Chữ ký",-12} Đường dẫn");
    foreach (ProcessInfo p in items)
    {
        string path = p.ExecutablePath ?? (p.IsAccessDenied ? "<access denied>" : "<không rõ>");
        Console.WriteLine($"{p.Pid,6} {Trunc(p.Name, 26),-26} {p.CpuPercent,6:F1} {p.WorkingSetMb,9:F1} {p.PrivateMb,9:F1} {p.Signature,-12} {Trunc(path, 60)}");
    }
}

static string Trunc(string s, int max) => s.Length <= max ? s : "..." + s[^(max - 3)..];
