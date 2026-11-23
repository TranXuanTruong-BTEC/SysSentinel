namespace SysSentinel.Core.Common;

/// <summary>Kết quả của một thao tác hệ thống (không ném exception cho lỗi nghiệp vụ).</summary>
public sealed record OperationResult(bool Success, string? Message = null, Exception? Error = null)
{
    public static OperationResult Ok(string? message = null) => new(true, message);
    public static OperationResult Fail(string message, Exception? error = null) => new(false, message, error);
}
