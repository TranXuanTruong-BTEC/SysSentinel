using System.Runtime.InteropServices;

namespace SysSentinel.Core.Native;

/// <summary>P/Invoke tới iphlpapi.dll (IP Helper). Bảng trả về được đọc bằng offset, không cần struct/unsafe.</summary>
internal static class IpHelperNativeMethods
{
    public const uint NO_ERROR = 0;
    public const uint ERROR_NOT_SUPPORTED = 50;
    public const uint ERROR_INVALID_PARAMETER = 87;
    public const uint ERROR_INSUFFICIENT_BUFFER = 122;

    public const uint AF_INET = 2;
    public const uint AF_INET6 = 23;

    /// <summary>TCP_TABLE_OWNER_PID_ALL: mọi trạng thái, kèm PID chủ sở hữu.</summary>
    public const int TCP_TABLE_OWNER_PID_ALL = 5;

    /// <summary>UDP_TABLE_OWNER_PID: endpoint UDP kèm PID chủ sở hữu.</summary>
    public const int UDP_TABLE_OWNER_PID = 1;

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    public static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref uint pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        uint ulAf, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    public static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref uint pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        uint ulAf, int tableClass, uint reserved);
}
