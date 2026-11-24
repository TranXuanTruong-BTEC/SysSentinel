using SysSentinel.Core.Models;
namespace SysSentinel.Core.Interfaces;

public interface ISignatureVerifier
{
    /// <summary>Số mục đang có trong cache.</summary>
    int CachedCount { get; }

    /// <summary>Kiểm tra chữ ký (có dùng cache). Không bao giờ ném exception.</summary>
    SignatureStatus Verify(string? executablePath);

    /// <summary>
    /// Trả về true khi đã có câu trả lời mà KHÔNG cần chạy WinVerifyTrust
    /// (có trong cache còn hạn, hoặc đường dẫn không hợp lệ/không tồn tại).
    /// </summary>
    bool TryGetCached(string? executablePath, out SignatureStatus status);

    void Invalidate(string executablePath);
    void ClearCache();
}
