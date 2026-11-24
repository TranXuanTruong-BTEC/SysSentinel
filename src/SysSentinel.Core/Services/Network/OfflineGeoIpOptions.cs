namespace SysSentinel.Core.Services.Network;

public sealed class OfflineGeoIpOptions
{
    public const string DefaultFileName = "GeoLite2-Country.mmdb";

    /// <summary>Đường dẫn file .mmdb chỉ định tường minh (ưu tiên cao nhất). Null = tự dò theo danh sách mặc định.</summary>
    public string? DatabasePath { get; set; }

    /// <summary>Số IP tối đa trong cache; vượt quá thì cache được xóa sạch và xây lại.</summary>
    public int CacheMaxEntries { get; set; } = 20000;

    /// <summary>Các vị trí sẽ dò theo thứ tự.</summary>
    public IEnumerable<string> EnumerateCandidatePaths()
    {
        if (!string.IsNullOrWhiteSpace(DatabasePath)) yield return DatabasePath;

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysSentinel", "GeoIP", DefaultFileName);

        yield return Path.Combine(AppContext.BaseDirectory, "GeoIP", DefaultFileName);
    }
}
