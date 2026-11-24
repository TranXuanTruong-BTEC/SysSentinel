namespace SysSentinel.Core.Models;

public enum SignatureStatus
{
    /// <summary>Không xác định: không có đường dẫn, không đọc được file, hoặc tính năng bị tắt.</summary>
    Unknown = 0,

    /// <summary>Chữ ký hợp lệ và được tin cậy theo chính sách Windows (nhúng trong file hoặc qua catalog).</summary>
    Valid,

    /// <summary>Không có chữ ký số (TRUST_E_NOSIGNATURE).</summary>
    Unsigned,

    /// <summary>Có chữ ký nhưng không hợp lệ: hết hạn, bị can thiệp, root không tin cậy, bị thu hồi...</summary>
    Invalid,

    /// <summary>Đường dẫn đã biết nhưng file không còn tồn tại.</summary>
    FileNotFound,

    /// <summary>Chưa kiểm tra xong: đang xếp hàng verify nền, lần quét sau sẽ có kết quả.</summary>
    Pending
}
