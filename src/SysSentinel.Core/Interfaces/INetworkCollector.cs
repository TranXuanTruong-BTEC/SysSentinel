using SysSentinel.Core.Models;
namespace SysSentinel.Core.Interfaces;

public interface INetworkCollector
{
    /// <summary>
    /// Chụp bảng kết nối TCP/UDP (IPv4 + IPv6) của hệ điều hành, mỗi dòng kèm PID chủ sở hữu và quốc gia IP đích.
    /// Không ném exception khi một bảng đọc lỗi: trả về phần đã đọc được và ghi log.
    /// </summary>
    Task<IReadOnlyList<NetworkConnectionInfo>> CollectAsync(CancellationToken ct = default);
}
