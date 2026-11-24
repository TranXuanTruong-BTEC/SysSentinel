# SysSentinel - Ngày 2 (overlay lên Ngày 1)

## Cách chèn vào Ngày 1
1. Đóng Visual Studio.
2. Giải nén file zip này vào thư mục CHA của `SysSentinel` (ví dụ Desktop), chọn "Replace the files in the destination".
   (Hoặc copy toàn bộ nội dung thư mục `SysSentinel\` trong zip đè lên thư mục `SysSentinel` của bạn.)
3. Mở terminal bằng quyền Administrator:

    cd C:\Users\Truong\Desktop\SysSentinel
    dotnet restore
    dotnet build
    dotnet run --project tools/SysSentinel.ScanTest

## File mới / file bị ghi đè
MỚI : Core/Models/{SignatureStatus,ProcessInfo}.cs
      Core/Interfaces/ISignatureVerifier.cs
      Core/Native/{NativeMethods,UnmanagedAllocations}.cs
      Core/Services/Security/*  |  Core/Services/Processes/*
      tools/SysSentinel.ScanTest/*
GHI ĐÈ: Core/SysSentinel.Core.csproj (thêm Logging.Abstractions)
        Core/Models/Snapshots.cs (bỏ ProcessSnapshot)
        Core/Interfaces/IProcessCollector.cs (trả về ProcessInfo)
        UI/App.xaml.cs (đăng ký DI)
        UI/SysSentinel.UI.csproj (đã bỏ SupportedOSPlatformVersion gây lỗi NETSDK1135)
        SysSentinel.sln (thêm project ScanTest)

## Checklist nghiệm thu Ngày 2
[ ] dotnet build: 4 project đều succeeded, 0 error
[ ] ScanTest A3 (quét có cache): avg < 200 ms
[ ] ScanTest A2: Pending về 0 trong vòng vài chục giây; A1 không bị treo
[ ] ScanTest C: %CPU của tool xấp xỉ 100/số_core (đốt 1 core)
[ ] Số tiến trình gần bằng Process Explorer / Task Manager (chênh vài cái do thời điểm)
[ ] svchost.exe, cmd.exe, explorer.exe = Valid (kiểm tra cả catalog)
[ ] Chép 1 file .exe bất kỳ chưa ký, chạy lên => Unsigned
[ ] Sửa 1 byte file .exe đã ký (bản sao) => Invalid
[ ] Tiến trình hệ thống/PPL: không văng exception, IsAccessDenied hoặc ExecutablePath rỗng
[ ] Đóng tool: không treo (verify nền được hủy khi Dispose)
[ ] ./SysSentinel.UI vẫn chạy bình thường (3 profile, Admin: Có)
