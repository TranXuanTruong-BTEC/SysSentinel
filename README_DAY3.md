# SysSentinel - Ngày 3: Network Telemetry Collector (overlay lên Ngày 2)

## Cài đặt
1. Đóng Visual Studio.
2. Giải nén zip TRỰC TIẾP vào thư mục dự án (nơi có SysSentinel.sln), chọn Replace.
   (Zip không có thư mục bọc; nếu Explorer tạo thư mục con thì copy nội dung ra ngoài rồi xóa thư mục con.)
3. (Tùy chọn) Xóa file không còn dùng:  del src\SysSentinel.Core\Models\Snapshots.cs
4. Terminal Administrator:
      cd <thư mục dự án>
      dotnet restore SysSentinel.sln
      dotnet build SysSentinel.sln
      dotnet run --project tools/SysSentinel.ScanTest -- network

## GeoIP (tùy chọn)
Tải GeoLite2-Country.mmdb từ MaxMind (tài khoản miễn phí) và đặt vào MỘT trong:
   %LocalAppData%\SysSentinel\GeoIP\GeoLite2-Country.mmdb
   <thư mục exe>\GeoIP\GeoLite2-Country.mmdb
Không có file: vẫn chạy, IP công cộng hiển thị "Unknown", IP nội bộ hiển thị "Local / LAN".

## Gói NuGet (đã khai báo sẵn trong SysSentinel.Core.csproj)
   dotnet add src/SysSentinel.Core package MaxMind.GeoIP2
   dotnet add src/SysSentinel.Core package Microsoft.Extensions.DependencyInjection.Abstractions
