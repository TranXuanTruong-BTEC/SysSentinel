using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;

namespace SysSentinel.Core.Services.Security;

/// <summary>
/// Verifier Authenticode có cache theo ExecutablePath (không phân biệt hoa/thường).
/// Mục cache tự vô hiệu khi LastWriteTimeUtc hoặc kích thước file đổi, hoặc khi hết TTL.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AuthenticodeSignatureVerifier : ISignatureVerifier
{
    private readonly record struct FileStamp(long LastWriteTicks, long Length);
    private readonly record struct CacheEntry(SignatureStatus Status, FileStamp Stamp, DateTime VerifiedAtUtc);
    private enum StatOutcome { Ok, Missing, Error }

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SignatureVerifierOptions _options;
    private readonly ILogger<AuthenticodeSignatureVerifier> _logger;
    private int _trimming;

    public AuthenticodeSignatureVerifier(SignatureVerifierOptions options, ILogger<AuthenticodeSignatureVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxEntries < 1) throw new ArgumentOutOfRangeException(nameof(options), "MaxEntries phải >= 1.");
        _options = options;
        _logger = logger;
    }

    public int CachedCount => _cache.Count;

    public bool TryGetCached(string? executablePath, out SignatureStatus status)
    {
        status = SignatureStatus.Unknown;
        if (!IsUsablePath(executablePath)) return true;          // không có gì để verify
        string path = executablePath!;

        if (!_cache.TryGetValue(path, out CacheEntry entry)) return false;

        StatOutcome outcome = Stat(path, out FileStamp stamp);
        if (outcome == StatOutcome.Missing)
        {
            _cache.TryRemove(path, out _);
            status = SignatureStatus.FileNotFound;
            return true;
        }
        if (outcome == StatOutcome.Error)
        {
            status = SignatureStatus.Unknown;
            return true;
        }

        if (entry.Stamp == stamp && !IsExpired(entry, DateTime.UtcNow))
        {
            status = entry.Status;
            return true;
        }
        return false;                                            // file đổi hoặc hết TTL
    }

    public SignatureStatus Verify(string? executablePath)
    {
        if (!IsUsablePath(executablePath)) return SignatureStatus.Unknown;
        string path = executablePath!;

        StatOutcome outcome = Stat(path, out FileStamp stamp);
        if (outcome == StatOutcome.Missing)
        {
            _cache.TryRemove(path, out _);
            return SignatureStatus.FileNotFound;
        }
        if (outcome == StatOutcome.Error) return SignatureStatus.Unknown;

        if (_cache.TryGetValue(path, out CacheEntry cached) &&
            cached.Stamp == stamp && !IsExpired(cached, DateTime.UtcNow))
        {
            return cached.Status;
        }

        SignatureStatus status;
        long started = Stopwatch.GetTimestamp();
        try
        {
            status = WinTrustChecker.Check(path, _options.CheckRevocation);
        }
        catch (Exception ex)   // lớp native không bao giờ được làm sập luồng gọi
        {
            _logger.LogWarning(ex, "Verify chữ ký thất bại bất ngờ cho {Path}", path);
            status = SignatureStatus.Unknown;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Signature {Status} ({Ms:F1} ms): {Path}",
                status, Stopwatch.GetElapsedTime(started).TotalMilliseconds, path);

        _cache[path] = new CacheEntry(status, stamp, DateTime.UtcNow);
        TrimIfNeeded();
        return status;
    }

    public void Invalidate(string executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath)) _cache.TryRemove(executablePath, out _);
    }

    public void ClearCache() => _cache.Clear();

    // ------------------------------------------------------------------ nội bộ
    private static bool IsUsablePath(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);

    private static StatOutcome Stat(string path, out FileStamp stamp)
    {
        stamp = default;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return StatOutcome.Missing;
            stamp = new FileStamp(fi.LastWriteTimeUtc.Ticks, fi.Length);
            return StatOutcome.Ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException)
        {
            return StatOutcome.Error;
        }
    }

    private bool IsExpired(CacheEntry entry, DateTime nowUtc)
    {
        TimeSpan ttl = entry.Status is SignatureStatus.Unknown or SignatureStatus.FileNotFound
            ? _options.TransientTtl
            : _options.CacheTtl;
        return nowUtc - entry.VerifiedAtUtc >= ttl;
    }

    private void TrimIfNeeded()
    {
        if (_cache.Count <= _options.MaxEntries) return;
        if (Interlocked.Exchange(ref _trimming, 1) == 1) return;   // đã có luồng khác đang dọn
        try
        {
            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, CacheEntry> kv in _cache)
                if (IsExpired(kv.Value, now)) _cache.TryRemove(kv);

            int excess = _cache.Count - _options.MaxEntries;
            if (excess > 0)
            {
                int toRemove = excess + _options.MaxEntries / 10;
                foreach (var kv in _cache.OrderBy(k => k.Value.VerifiedAtUtc).Take(toRemove).ToList())
                    _cache.TryRemove(kv.Key, out _);
            }
        }
        finally
        {
            Volatile.Write(ref _trimming, 0);
        }
    }
}
