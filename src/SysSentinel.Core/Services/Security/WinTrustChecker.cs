using System.Runtime.InteropServices;
using SysSentinel.Core.Models;
using SysSentinel.Core.Native;

namespace SysSentinel.Core.Services.Security;

/// <summary>
/// Gọi WinVerifyTrust (WINTRUST_ACTION_GENERIC_VERIFY_V2): thử chữ ký nhúng trước,
/// nếu file không có chữ ký nhúng thì tra catalog (.cat) bằng SHA-256 rồi SHA-1.
/// </summary>
internal static class WinTrustChecker
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private static readonly string[] CatalogHashAlgorithms = { "SHA256", "SHA1" };

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_CHOICE_CATALOG = 2;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    private const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
    private const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);

    public static SignatureStatus Check(string path, bool checkRevocation)
    {
        SignatureStatus embedded = VerifyEmbedded(path, checkRevocation, out bool tryCatalog);
        if (!tryCatalog) return embedded;

        // Không có chữ ký nhúng -> thử catalog (đa số file Windows nằm ở đây).
        return VerifyViaCatalog(path, checkRevocation) ?? SignatureStatus.Unsigned;
    }

    // ---------------------------------------------------------------- chữ ký nhúng
    private static SignatureStatus VerifyEmbedded(string path, bool checkRevocation, out bool tryCatalog)
    {
        tryCatalog = false;
        using var mem = new UnmanagedAllocations();

        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = mem.AllocString(path),
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };
        WINTRUST_DATA data = NewData(checkRevocation, WTD_CHOICE_FILE, mem.AllocStruct(fileInfo));

        int hr = RunWinVerifyTrust(ref data, out int lastError);
        if (hr == 0) return SignatureStatus.Valid;

        if (hr == TRUST_E_NOSIGNATURE)
        {
            if (lastError is NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND)
                return SignatureStatus.FileNotFound;

            // Không có chữ ký nhúng (hoặc không đọc được): thử tra catalog trước khi kết luận Unsigned.
            tryCatalog = true;
            return SignatureStatus.Unsigned;
        }

        if (hr is TRUST_E_SUBJECT_FORM_UNKNOWN or TRUST_E_PROVIDER_UNKNOWN)
        {
            tryCatalog = true;
            return SignatureStatus.Unsigned;
        }

        return MapFailure(hr);
    }

    // ---------------------------------------------------------------- catalog
    private static SignatureStatus? VerifyViaCatalog(string path, bool checkRevocation)
    {
        FileStream fs;
        try
        {
            // Cho phép đọc file đang chạy (bị khóa ghi) bằng FileShare.ReadWrite | Delete.
            fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return SignatureStatus.FileNotFound;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SignatureStatus.Unknown;
        }

        using (fs)
        {
            IntPtr hFile = fs.SafeFileHandle.DangerousGetHandle();
            foreach (string algorithm in CatalogHashAlgorithms)
            {
                SignatureStatus? result = VerifyViaCatalog(path, hFile, algorithm, checkRevocation);
                if (result.HasValue) return result;
            }
            GC.KeepAlive(fs);
            return null; // không có catalog nào chứa hash của file này
        }
    }

    private static SignatureStatus? VerifyViaCatalog(string path, IntPtr hFile, string algorithm, bool checkRevocation)
    {
        IntPtr hCatAdmin = IntPtr.Zero;
        IntPtr hCatInfo = IntPtr.Zero;
        try
        {
            if (!NativeMethods.CryptCATAdminAcquireContext2(out hCatAdmin, IntPtr.Zero, algorithm, IntPtr.Zero, 0))
                return null;

            uint cbHash = 0;
            NativeMethods.CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, hFile, ref cbHash, null, 0);
            if (cbHash == 0) return null;

            byte[] hash = new byte[cbHash];
            if (!NativeMethods.CryptCATAdminCalcHashFromFileHandle2(hCatAdmin, hFile, ref cbHash, hash, 0))
                return null;

            hCatInfo = NativeMethods.CryptCATAdminEnumCatalogFromHash(hCatAdmin, hash, cbHash, 0, IntPtr.Zero);
            if (hCatInfo == IntPtr.Zero) return null;

            var catInfo = new CATALOG_INFO { cbStruct = (uint)Marshal.SizeOf<CATALOG_INFO>(), wszCatalogFile = string.Empty };
            if (!NativeMethods.CryptCATCatalogInfoFromContext(hCatInfo, ref catInfo, 0)) return null;

            using var mem = new UnmanagedAllocations();
            var catalog = new WINTRUST_CATALOG_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_CATALOG_INFO>(),
                dwCatalogVersion = 0,
                pcwszCatalogFilePath = mem.AllocString(catInfo.wszCatalogFile),
                pcwszMemberTag = mem.AllocString(Convert.ToHexString(hash, 0, (int)cbHash)),
                pcwszMemberFilePath = mem.AllocString(path),
                hMemberFile = IntPtr.Zero,
                pbCalculatedFileHash = mem.AllocBytes(hash),
                cbCalculatedFileHash = cbHash,
                pcCatalogContext = IntPtr.Zero,
                hCatAdmin = hCatAdmin
            };
            WINTRUST_DATA data = NewData(checkRevocation, WTD_CHOICE_CATALOG, mem.AllocStruct(catalog));

            int hr = RunWinVerifyTrust(ref data, out _);
            return hr == 0 ? SignatureStatus.Valid : MapFailure(hr);
        }
        finally
        {
            if (hCatInfo != IntPtr.Zero) NativeMethods.CryptCATAdminReleaseCatalogContext(hCatAdmin, hCatInfo, 0);
            if (hCatAdmin != IntPtr.Zero) NativeMethods.CryptCATAdminReleaseContext(hCatAdmin, 0);
        }
    }

    // ---------------------------------------------------------------- helper
    private static WINTRUST_DATA NewData(bool checkRevocation, uint unionChoice, IntPtr pUnion) => new()
    {
        cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
        dwUIChoice = WTD_UI_NONE,                       // không bao giờ hiện UI
        fdwRevocationChecks = checkRevocation ? WTD_REVOKE_WHOLECHAIN : WTD_REVOKE_NONE,
        dwUnionChoice = unionChoice,
        pUnion = pUnion,
        // Tắt revocation + không tải URL => không gọi mạng, không bị treo.
        dwProvFlags = checkRevocation ? 0u : (WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL)
    };

    /// <summary>VERIFY rồi luôn CLOSE (WTD_STATEACTION_CLOSE) để giải phóng state của provider.</summary>
    private static int RunWinVerifyTrust(ref WINTRUST_DATA data, out int lastError)
    {
        Guid action = GenericVerifyV2;
        IntPtr noWindow = new(-1);                      // INVALID_HANDLE_VALUE = không có cửa sổ cha / không UI
        data.dwStateAction = WTD_STATEACTION_VERIFY;
        lastError = 0;
        try
        {
            int hr = NativeMethods.WinVerifyTrust(noWindow, ref action, ref data);
            lastError = Marshal.GetLastWin32Error();    // lấy NGAY, trước khi lệnh CLOSE ghi đè
            return hr;
        }
        finally
        {
            data.dwStateAction = WTD_STATEACTION_CLOSE;
            NativeMethods.WinVerifyTrust(noWindow, ref action, ref data);
        }
    }

    private static SignatureStatus MapFailure(int hr)
    {
        if (hr == TRUST_E_NOSIGNATURE) return SignatureStatus.Unsigned;

        int facility = (hr >> 16) & 0x1FFF;
        int code = hr & 0xFFFF;
        if (hr > 0 && hr <= 0xFFFF) { facility = 7; code = hr; }   // mã lỗi Win32 thuần

        if (facility == 7)                                          // FACILITY_WIN32
            return code is NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND
                ? SignatureStatus.FileNotFound
                : SignatureStatus.Unknown;

        // 0x800B.... (CERT_E_*, TRUST_E_*) và 0x8009.... (CRYPT_E_*, TRUST_E_BAD_DIGEST):
        // có chữ ký nhưng hết hạn / bị sửa / root không tin cậy / bị thu hồi / bị chặn.
        if (facility is 9 or 11) return SignatureStatus.Invalid;

        return SignatureStatus.Unknown;
    }
}
