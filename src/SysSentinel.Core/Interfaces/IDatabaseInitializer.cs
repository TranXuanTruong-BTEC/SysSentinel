namespace SysSentinel.Core.Interfaces;

public interface IDatabaseInitializer
{
    /// <summary>Tạo DB, áp dụng migration và seed dữ liệu mặc định. Ném exception nếu thất bại.</summary>
    Task InitializeAsync(CancellationToken ct = default);
}
