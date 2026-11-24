using System.Net;
using System.Net.Sockets;

namespace SysSentinel.Core.Services.Network;

/// <summary>Phân loại địa chỉ IP: loopback / LAN / link-local / multicast / reserved (không cần tra GeoIP).</summary>
internal static class IpClassifier
{
    /// <summary>Đưa IPv4-mapped IPv6 (::ffff:a.b.c.d) về IPv4 thuần.</summary>
    public static IPAddress Normalize(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6
            ? address.MapToIPv4()
            : address;

    /// <summary>0.0.0.0 hoặc ::</summary>
    public static bool IsUnspecified(IPAddress address)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out int written)) return false;
        for (int i = 0; i < written; i++)
            if (bytes[i] != 0) return false;
        return true;
    }

    /// <summary>true nếu địa chỉ thuộc dải nội bộ / loopback / reserved / multicast.</summary>
    public static bool IsLocalOrReserved(IPAddress address)
    {
        IPAddress a = Normalize(address);
        if (IsUnspecified(a)) return true;

        Span<byte> b = stackalloc byte[16];
        if (!a.TryWriteBytes(b, out _)) return true;     // định dạng lạ: không tra cứu

        if (a.AddressFamily == AddressFamily.InterNetwork)
            return IsLocalV4(b[0], b[1], b[2]);

        // ---- IPv6 ----
        if (IPAddress.IsLoopback(a) || a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast) return true;
        if ((b[0] & 0xFE) == 0xFC) return true;                                   // fc00::/7 unique local
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return true; // 2001:db8::/32 tài liệu
        return false;
    }

    private static bool IsLocalV4(byte b0, byte b1, byte b2)
    {
        if (b0 == 0) return true;                               // 0.0.0.0/8
        if (b0 == 10) return true;                              // 10.0.0.0/8
        if (b0 == 127) return true;                             // loopback
        if (b0 == 100 && (b1 & 0xC0) == 64) return true;        // 100.64.0.0/10 CGNAT
        if (b0 == 169 && b1 == 254) return true;                // link-local
        if (b0 == 172 && (b1 & 0xF0) == 16) return true;        // 172.16.0.0/12
        if (b0 == 192 && b1 == 168) return true;                // 192.168.0.0/16
        if (b0 == 192 && b1 == 0 && b2 == 0) return true;       // 192.0.0.0/24
        if (b0 == 192 && b1 == 0 && b2 == 2) return true;       // TEST-NET-1
        if (b0 == 198 && (b1 & 0xFE) == 18) return true;        // 198.18.0.0/15
        if (b0 == 198 && b1 == 51 && b2 == 100) return true;    // TEST-NET-2
        if (b0 == 203 && b1 == 0 && b2 == 113) return true;     // TEST-NET-3
        if (b0 >= 224) return true;                             // multicast 224/4 + reserved 240/4 + broadcast
        return false;
    }
}
