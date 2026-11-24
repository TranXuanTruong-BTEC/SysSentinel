using System.Net;
using SysSentinel.Core.Models;
namespace SysSentinel.Core.Interfaces;

public interface IGeoIpResolver
{
    /// <summary>true nếu đã nạp được file .mmdb; false = chỉ phân loại Local/Public, quốc gia luôn Unknown.</summary>
    bool IsDatabaseLoaded { get; }

    /// <summary>Đường dẫn file .mmdb đang dùng (null nếu chưa nạp được).</summary>
    string? DatabasePath { get; }

    /// <summary>Số IP đang nằm trong cache.</summary>
    int CachedCount { get; }

    /// <summary>Tra cứu IP (có cache). Không bao giờ ném exception.</summary>
    GeoIpResult Resolve(IPAddress? address);

    void ClearCache();
}
