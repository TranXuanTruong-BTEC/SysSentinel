using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;
using SysSentinel.Core.Native;

namespace SysSentinel.Core.Services.Processes;

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessCollector : IProcessCollector, IDisposable
{
    private readonly record struct CpuSample(long CreationTicks, long CpuTicks, long Timestamp);

    private readonly record struct RawProcess(
        int Pid, string Name, string? Path, long WorkingSet, long PrivateBytes,
        double Cpu, DateTime? StartUtc, bool AccessDenied);

    private readonly ISignatureVerifier _verifier;
    private readonly ProcessCollectorOptions _options;
    private readonly ILogger<WindowsProcessCollector> _logger;

    private readonly SemaphoreSlim _scanGate = new(1, 1);       // không cho hai lần quét chạy chồng nhau
    private readonly SemaphoreSlim _sigThrottle;
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly double _cores = Environment.ProcessorCount;

    private Dictionary<int, CpuSample> _previous = new();
    private int _disposed;

    public WindowsProcessCollector(
        ISignatureVerifier verifier, ProcessCollectorOptions options, ILogger<WindowsProcessCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(options);
        _verifier = verifier;
        _options = options;
        _logger = logger;
        _sigThrottle = new SemaphoreSlim(Math.Max(1, options.MaxSignatureParallelism));
    }

    public async Task<IReadOnlyList<ProcessInfo>> CollectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _scanGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => ScanCore(ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    // ------------------------------------------------------------------ quét
    private IReadOnlyList<ProcessInfo> ScanCore(CancellationToken ct)
    {
        long started = Stopwatch.GetTimestamp();
        Dictionary<int, CpuSample> previous = _previous;
        var next = new Dictionary<int, CpuSample>(previous.Count + 16);
        var raws = new List<RawProcess>(512);

        Process[] processes = Process.GetProcesses();
        try
        {
            foreach (Process p in processes)
            {
                ct.ThrowIfCancellationRequested();
                if (TryReadProcess(p, previous, next, out RawProcess raw)) raws.Add(raw);
            }
        }
        finally
        {
            foreach (Process p in processes) p.Dispose();       // giải phóng handle của từng Process
        }

        Dictionary<string, SignatureStatus> signatures = ResolveSignatures(raws, ct);

        var result = new List<ProcessInfo>(raws.Count);
        int denied = 0;
        foreach (RawProcess r in raws)
        {
            SignatureStatus sig = SignatureStatus.Unknown;
            if (r.Path is not null && signatures.TryGetValue(r.Path, out SignatureStatus found)) sig = found;
            if (r.AccessDenied) denied++;

            result.Add(new ProcessInfo(
                r.Pid, r.Name, r.Path, r.WorkingSet, r.PrivateBytes, r.Cpu, r.StartUtc, sig, r.AccessDenied));
        }
        result.Sort((a, b) => a.Pid.CompareTo(b.Pid));

        _previous = next;                                       // chỉ cập nhật khi quét hoàn tất

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Quét {Count} tiến trình ({Denied} bị từ chối truy cập) trong {Ms:F1} ms",
                result.Count, denied, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return result;
    }

    private bool TryReadProcess(
        Process p, Dictionary<int, CpuSample> previous, Dictionary<int, CpuSample> next, out RawProcess raw)
    {
        raw = default;

        int pid;
        string name;
        long workingSet, privateBytes;
        try
        {
            // Các thuộc tính này lấy từ snapshot hệ thống, không mở handle.
            pid = p.Id;
            name = p.ProcessName;
            workingSet = p.WorkingSet64;
            privateBytes = p.PrivateMemorySize64;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;                                       // tiến trình đã thoát giữa chừng
        }

        string? path = null;
        bool denied = false, handleOk = false;
        double cpu = 0;
        DateTime? startUtc = null;

        if (pid != 0)                                           // PID 0 = System Idle Process, không mở được
        {
            using SafeProcHandle handle = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);

            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == NativeMethods.ERROR_INVALID_PARAMETER) return false;   // PID không còn tồn tại
                denied = err == NativeMethods.ERROR_ACCESS_DENIED;
            }
            else
            {
                handleOk = true;
                path = QueryImagePath(handle);

                if (NativeMethods.GetProcessTimes(handle, out long creation, out _, out long kernel, out long user))
                {
                    long now = Stopwatch.GetTimestamp();
                    long cpuTicks = kernel + user;              // đơn vị 100ns
                    if (creation > 0) startUtc = DateTime.FromFileTimeUtc(creation);

                    // Chỉ tính delta khi cùng PID VÀ cùng thời điểm tạo (tránh PID bị tái sử dụng).
                    if (previous.TryGetValue(pid, out CpuSample prev) && prev.CreationTicks == creation)
                    {
                        double wallSeconds = Stopwatch.GetElapsedTime(prev.Timestamp, now).TotalSeconds;
                        if (wallSeconds > 0)
                        {
                            double cpuSeconds = (cpuTicks - prev.CpuTicks) / 10_000_000.0;
                            cpu = Math.Clamp(cpuSeconds / (wallSeconds * _cores) * 100.0, 0.0, 100.0);
                        }
                    }
                    next[pid] = new CpuSample(creation, cpuTicks, now);
                }
            }
        }

        // Dự phòng: chỉ khi mở được handle mà vẫn không lấy được đường dẫn (hiếm). Bỏ qua tiến trình hệ thống.
        if (path is null && handleOk && pid > 4 && _options.UseMainModuleFallback)
            path = TryMainModulePath(p);

        raw = new RawProcess(pid, name, path, workingSet, privateBytes, cpu, startUtc, denied);
        return true;
    }

    private static string? QueryImagePath(SafeProcHandle handle)
    {
        char[] buffer = ArrayPool<char>.Shared.Rent(1024);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                uint size = (uint)buffer.Length;
                if (NativeMethods.QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                    return new string(buffer, 0, (int)size);

                if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_INSUFFICIENT_BUFFER) return null;

                ArrayPool<char>.Shared.Return(buffer);
                buffer = ArrayPool<char>.Shared.Rent(32768);     // đường dẫn dài tối đa của NT
            }
            return null;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static string? TryMainModulePath(Process p)
    {
        try
        {
            return p.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ chữ ký
    private Dictionary<string, SignatureStatus> ResolveSignatures(List<RawProcess> raws, CancellationToken ct)
    {
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RawProcess r in raws)
            if (r.Path is not null) distinct.Add(r.Path);

        var result = new Dictionary<string, SignatureStatus>(distinct.Count, StringComparer.OrdinalIgnoreCase);

        switch (_options.SignatureMode)
        {
            case SignatureCheckMode.Disabled:
                foreach (string path in distinct) result[path] = SignatureStatus.Unknown;
                break;

            case SignatureCheckMode.Background:
                foreach (string path in distinct)
                {
                    if (_verifier.TryGetCached(path, out SignatureStatus cached))
                    {
                        result[path] = cached;
                    }
                    else
                    {
                        result[path] = SignatureStatus.Pending;
                        QueueBackgroundVerification(path);
                    }
                }
                break;

            case SignatureCheckMode.Blocking:
                var verified = new ConcurrentDictionary<string, SignatureStatus>(StringComparer.OrdinalIgnoreCase);
                var parallel = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, _options.MaxSignatureParallelism),
                    CancellationToken = ct
                };
                Parallel.ForEach(distinct, parallel, path => verified[path] = _verifier.Verify(path));
                foreach (KeyValuePair<string, SignatureStatus> kv in verified) result[kv.Key] = kv.Value;
                break;
        }
        return result;
    }

    private void QueueBackgroundVerification(string path)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!_inFlight.TryAdd(path, 0)) return;                  // đã có tác vụ verify cho file này

        CancellationToken token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _sigThrottle.WaitAsync(token).ConfigureAwait(false);
                try { _verifier.Verify(path); }
                finally { _sigThrottle.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Verify nền thất bại cho {Path}", path);
            }
            finally
            {
                _inFlight.TryRemove(path, out _);
            }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _cts.Cancel();                                           // dừng các tác vụ verify nền
        _cts.Dispose();
        // Hai SemaphoreSlim không dùng AvailableWaitHandle nên không giữ tài nguyên OS; để GC thu dọn
        // (tránh ObjectDisposedException khi tác vụ nền gọi Release sau khi Dispose).
    }
}
