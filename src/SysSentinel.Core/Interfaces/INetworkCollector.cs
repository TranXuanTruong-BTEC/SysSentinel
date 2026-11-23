using SysSentinel.Core.Models;
namespace SysSentinel.Core.Interfaces;

public interface INetworkCollector
{
    /// <summary>Chụp bảng kết nối TCP/UDP và ánh xạ sang process sở hữu.</summary>
    Task<IReadOnlyList<NetworkConnectionSnapshot>> CollectAsync(CancellationToken ct = default);
}
