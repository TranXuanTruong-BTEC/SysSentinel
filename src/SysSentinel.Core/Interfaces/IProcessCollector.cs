using SysSentinel.Core.Models;
namespace SysSentinel.Core.Interfaces;

public interface IProcessCollector
{
    /// <summary>
    /// Chụp danh sách tiến trình hiện tại. %CPU tính theo khoảng thời gian giữa hai lần gọi liên tiếp
    /// (lần đầu tiên luôn là 0 vì chưa có mẫu trước đó).
    /// </summary>
    Task<IReadOnlyList<ProcessInfo>> CollectAsync(CancellationToken ct = default);
}
