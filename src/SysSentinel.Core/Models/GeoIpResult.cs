namespace SysSentinel.Core.Models;

public enum GeoIpScope
{
    /// <summary>Không có địa chỉ đích (LISTEN/UDP) hoặc địa chỉ 0.0.0.0 / ::.</summary>
    Unspecified,

    /// <summary>Loopback, LAN, link-local, multicast, reserved.</summary>
    LocalNetwork,

    /// <summary>IP công cộng (có thể có hoặc chưa có thông tin quốc gia).</summary>
    Public
}

public sealed record GeoIpResult(GeoIpScope Scope, string? CountryCode, string? CountryName)
{
    public static readonly GeoIpResult Unspecified = new(GeoIpScope.Unspecified, null, null);
    public static readonly GeoIpResult Local = new(GeoIpScope.LocalNetwork, null, null);
    public static readonly GeoIpResult UnknownPublic = new(GeoIpScope.Public, null, null);

    public string Label => Scope switch
    {
        GeoIpScope.LocalNetwork => "Local / LAN",
        GeoIpScope.Public when CountryCode is not null => $"{CountryName ?? CountryCode} ({CountryCode})",
        GeoIpScope.Public => "Unknown",
        _ => "-"
    };
}
