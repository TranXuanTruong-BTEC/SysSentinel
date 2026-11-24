using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SysSentinel.Core.Interfaces;

namespace SysSentinel.Core.Services.Network;

public static class NetworkCollectorServiceExtensions
{
    /// <summary>
    /// Đăng ký INetworkCollector + IGeoIpResolver (singleton: cache GeoIP phải sống suốt vòng đời ứng dụng).
    /// Dùng TryAdd: nếu đã đăng ký trước đó thì giữ nguyên bản đăng ký cũ.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddSysSentinelNetworkCollector(
        this IServiceCollection services,
        Action<NetworkCollectorOptions>? configureCollector = null,
        Action<OfflineGeoIpOptions>? configureGeoIp = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var collectorOptions = new NetworkCollectorOptions();
        configureCollector?.Invoke(collectorOptions);
        var geoOptions = new OfflineGeoIpOptions();
        configureGeoIp?.Invoke(geoOptions);

        services.TryAddSingleton(collectorOptions);
        services.TryAddSingleton(geoOptions);
        services.TryAddSingleton<IGeoIpResolver, OfflineGeoIpResolver>();
        services.TryAddSingleton<INetworkCollector, WindowsNetworkCollector>();
        return services;
    }
}
