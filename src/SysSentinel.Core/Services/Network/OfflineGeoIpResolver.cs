using System.Collections.Concurrent;
using System.Net;
using MaxMind.Db;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Responses;
using Microsoft.Extensions.Logging;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Models;

namespace SysSentinel.Core.Services.Network;

/// <summary>
/// Tra quốc gia ngoại tuyến bằng MaxMind GeoLite2 Country (.mmdb). Thiếu file .mmdb vẫn hoạt động:
/// IP nội bộ -> "Local / LAN", IP công cộng -> "Unknown". Kết quả được cache theo địa chỉ IP.
/// </summary>
public sealed class OfflineGeoIpResolver : IGeoIpResolver, IDisposable
{
    private readonly ConcurrentDictionary<IPAddress, GeoIpResult> _cache = new();
    private readonly DatabaseReader? _reader;
    private readonly ILogger<OfflineGeoIpResolver> _logger;
    private readonly int _maxEntries;
    private int _disposed;

    public string? DatabasePath { get; }
    public bool IsDatabaseLoaded => _reader is not null;
    public int CachedCount => _cache.Count;

    public OfflineGeoIpResolver(OfflineGeoIpOptions options, ILogger<OfflineGeoIpResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.CacheMaxEntries < 1) throw new ArgumentOutOfRangeException(nameof(options), "CacheMaxEntries phải >= 1.");
        _maxEntries = options.CacheMaxEntries;
        _logger = logger;

        DatabaseReader? reader = null;
        string? loadedPath = null;
        foreach (string candidate in options.EnumerateCandidatePaths())
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                // Mode.Memory: nạp cả file vào RAM (~vài MB) để không khóa file .mmdb khi cập nhật DB.
                reader = new DatabaseReader(candidate, FileAccessMode.Memory);
                loadedPath = candidate;
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không nạp được GeoIP database {Path}; thử vị trí tiếp theo.", candidate);
            }
        }

        _reader = reader;
        DatabasePath = loadedPath;

        if (_reader is null)
            _logger.LogWarning(
                "Chưa có {File}: GeoIP chạy ở chế độ dự phòng (IP công cộng hiển thị 'Unknown'). Đặt file vào một trong: {Paths}",
                OfflineGeoIpOptions.DefaultFileName, string.Join(" | ", options.EnumerateCandidatePaths()));
        else
            _logger.LogInformation("Đã nạp GeoIP database: {Path}", loadedPath);
    }

    public GeoIpResult Resolve(IPAddress? address)
    {
        if (address is null) return GeoIpResult.Unspecified;

        IPAddress ip = IpClassifier.Normalize(address);
        if (_cache.TryGetValue(ip, out GeoIpResult? cached)) return cached;

        GeoIpResult result = Compute(ip);
        if (_cache.Count >= _maxEntries) _cache.Clear();
        _cache[ip] = result;
        return result;
    }

    public void ClearCache() => _cache.Clear();

    private GeoIpResult Compute(IPAddress ip)
    {
        if (IpClassifier.IsUnspecified(ip)) return GeoIpResult.Unspecified;
        if (IpClassifier.IsLocalOrReserved(ip)) return GeoIpResult.Local;
        if (_reader is null || Volatile.Read(ref _disposed) != 0) return GeoIpResult.UnknownPublic;

        try
        {
            if (_reader.TryCountry(ip, out CountryResponse? response) &&
                response?.Country?.IsoCode is { Length: > 0 } code)
            {
                return new GeoIpResult(GeoIpScope.Public, code, response.Country.Name);
            }
            return GeoIpResult.UnknownPublic;
        }
        catch (Exception ex)   // DB hỏng / sai loại: không bao giờ làm sập luồng quét
        {
            _logger.LogDebug(ex, "Tra GeoIP thất bại cho {Ip}", ip);
            return GeoIpResult.UnknownPublic;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _reader?.Dispose();
        _cache.Clear();
    }
}
