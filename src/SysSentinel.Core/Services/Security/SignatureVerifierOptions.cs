namespace SysSentinel.Core.Services.Security;

public sealed class SignatureVerifierOptions
{
    /// <summary>Thời gian sống của kết quả xác định (Valid/Unsigned/Invalid). Hết hạn sẽ verify lại.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Thời gian sống ngắn hơn cho kết quả tạm (Unknown/FileNotFound).</summary>
    public TimeSpan TransientTtl { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Số mục tối đa trong cache; vượt quá sẽ dọn mục hết hạn rồi mục cũ nhất.</summary>
    public int MaxEntries { get; set; } = 5000;

    /// <summary>true = kiểm tra thu hồi chứng chỉ (có thể gọi mạng, chậm). Mặc định tắt.</summary>
    public bool CheckRevocation { get; set; }
}
