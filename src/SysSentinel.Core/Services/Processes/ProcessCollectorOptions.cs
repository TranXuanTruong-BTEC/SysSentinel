namespace SysSentinel.Core.Services.Processes;

public enum SignatureCheckMode
{
    /// <summary>Không kiểm tra chữ ký (Signature = Unknown).</summary>
    Disabled,

    /// <summary>Quét nhanh; file chưa có trong cache trả về Pending và được verify nền.</summary>
    Background,

    /// <summary>Chờ verify xong mọi file chưa có trong cache (chậm ở lần quét lạnh).</summary>
    Blocking
}

public sealed class ProcessCollectorOptions
{
    public SignatureCheckMode SignatureMode { get; set; } = SignatureCheckMode.Background;

    /// <summary>Số luồng verify song song tối đa (nền và Blocking).</summary>
    public int MaxSignatureParallelism { get; set; } = Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>Thử Process.MainModule khi QueryFullProcessImageName thất bại dù đã mở được handle.</summary>
    public bool UseMainModuleFallback { get; set; } = true;
}
