using SysSentinel.Core.Models;
namespace SysSentinel.Core.Interfaces;

public interface IProcessCollector
{
    /// <summary>Chụp danh sách tiến trình hiện tại kèm CPU/RAM.</summary>
    Task<IReadOnlyList<ProcessSnapshot>> CollectAsync(CancellationToken ct = default);
}
