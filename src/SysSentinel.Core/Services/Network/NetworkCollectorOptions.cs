namespace SysSentinel.Core.Services.Network;

public sealed class NetworkCollectorOptions
{
    public bool IncludeTcp { get; set; } = true;
    public bool IncludeUdp { get; set; } = true;
    public bool IncludeIPv4 { get; set; } = true;
    public bool IncludeIPv6 { get; set; } = true;

    /// <summary>Giữ cả socket TCP đang LISTEN.</summary>
    public bool IncludeListening { get; set; } = true;

    /// <summary>Tra quốc gia cho IP đích (dùng cache của IGeoIpResolver).</summary>
    public bool ResolveGeo { get; set; } = true;
}
