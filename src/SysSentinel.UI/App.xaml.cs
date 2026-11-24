using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SysSentinel.Core.Interfaces;
using SysSentinel.Core.Services.Network;
using SysSentinel.Core.Services.Processes;
using SysSentinel.Core.Services.Security;
using SysSentinel.Data;
using SysSentinel.UI.Logging;
using SysSentinel.UI.ViewModels;

namespace SysSentinel.UI;

public partial class App : Application
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SysSentinel", "logs");

    private readonly FileLoggerProvider _fileLog = new(LogDirectory);
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Chưa có cửa sổ nào -> tránh app tự thoát trong lúc khởi tạo bất đồng bộ.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        RegisterGlobalExceptionHandlers();

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDebug();
                    logging.AddProvider(_fileLog);
                })
                .ConfigureServices(ConfigureServices)
                .Build();

            await _host.StartAsync();

            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("SysSentinel khởi động. Administrator = {IsAdmin}", IsRunningAsAdministrator());
            if (!IsRunningAsAdministrator())
                logger.LogWarning("Ứng dụng KHÔNG chạy với quyền Administrator - các thao tác hệ thống sẽ thất bại.");

            // Khởi tạo DB (tạo file, migration, seed) TRƯỚC khi hiện UI.
            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception ex)
        {
            _fileLog.Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Critical   ] Startup: Khởi động thất bại{Environment.NewLine}{ex}");
            MessageBox.Show(
                $"SysSentinel không thể khởi động.\n\n{ex.Message}\n\nChi tiết xem tại:\n{LogDirectory}",
                "SysSentinel - Lỗi khởi động", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // Data layer: connection factory, initializer, repositories
        services.AddSysSentinelData();

        // Core (Ngày 2): chữ ký số + thu thập tiến trình.
        services.AddSingleton(new SignatureVerifierOptions());
        services.AddSingleton(new ProcessCollectorOptions());      // mặc định: SignatureMode = Background
        services.AddSingleton<ISignatureVerifier, AuthenticodeSignatureVerifier>();
        services.AddSingleton<IProcessCollector, WindowsProcessCollector>();

        // Core (Ngày 3): bảng kết nối mạng theo tiến trình + GeoIP ngoại tuyến.
        services.AddSysSentinelNetworkCollector();

        // UI layer
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host.Dispose();   // dispose luôn collector và GeoIP resolver
        }
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogFatal("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"Đã xảy ra lỗi không mong muốn:\n{args.Exception.Message}",
                "SysSentinel", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogFatal("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogFatal("UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private void LogFatal(string source, Exception? ex)
        => _fileLog.Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Critical   ] {source}{Environment.NewLine}{ex}");

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
