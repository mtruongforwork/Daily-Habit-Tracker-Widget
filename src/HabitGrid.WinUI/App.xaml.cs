using System.Runtime.InteropServices;
using HabitGrid.Presentation.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace HabitGrid.WinUI;

public partial class App : Application
{
    public static AppHost Host { get; private set; } = null!;

    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
        RegisterGlobalErrorHandlers();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Mọi event handler async phải có try/catch: một lỗi khởi động không được làm app thoát im lặng.
        try
        {
            var arguments = Environment.GetCommandLineArgs();
            var smoke = arguments.Contains("--smoke", StringComparer.OrdinalIgnoreCase);

            // Smoke test ghi dữ liệu: từ chối NGAY, trước cả khi dựng host (host có thể tạo/nhập CSDL).
            if (smoke && !AppPaths.IsCustomDataDir)
            {
                // Không log, không hộp thoại: không được tạo bất cứ thứ gì trong thư mục dữ liệu thật.
                System.Diagnostics.Debug.WriteLine($"--smoke bắt buộc đặt {AppPaths.DataDirVariable}.");
                Environment.Exit(3);
                return;
            }

            // Single instance TRƯỚC khi mở CSDL để bản thứ hai không đụng vào dữ liệu.
            var main = AppInstance.FindOrRegisterForKey(AppPaths.InstanceKey);
            if (!main.IsCurrent)
            {
                await main.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
                Environment.Exit(0);
                return;
            }

            Host = await AppHost.CreateAsync(DispatcherQueue.GetForCurrentThread());

            // CI kiểm tra gói MSIX đã cài: tạo CSDL ở vị trí mặc định (vùng dữ liệu của gói) và gieo hai thói quen mẫu cho WidgetHost đọc.
            if (arguments.Contains("--package-check", StringComparer.OrdinalIgnoreCase))
            {
                Environment.Exit(await PackageCheckAsync());
                return;
            }

            var window = new MainWindow();
            MainWindowInstance = window;
            main.Activated += (_, _) => window.DispatcherQueue.TryEnqueue(window.BringToFront);
            window.Activate();
            await window.InitializeAsync();

            if (smoke)
            {
                _ = SmokeRunner.RunAsync(window);
            }
            else
            {
                // Mở app thì bảo đảm widget đang chạy (WidgetHost là tiến trình riêng, nên không tự có mặt nếu chưa đăng nhập lại hoặc đã thoát).
                _ = Task.Run(HostLink.EnsureRunning);
            }
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
        }
    }

    private static async Task<int> PackageCheckAsync()
    {
        try
        {
            if (!HabitGrid.Core.Services.PackageIdentity.IsPackaged)
            {
                return 2;
            }

            var habits = Host.Get<HabitGrid.Core.Domain.IHabitRepository>();

            for (var i = 1; i <= 2; i++)
            {
                if (!(await habits.GetAllActiveAsync()).Any(h => h.Name == $"PKG-CHECK-{i}"))
                {
                    await habits.InsertAsync(new HabitGrid.Core.Domain.Habit { Name = $"PKG-CHECK-{i}", SortKey = $"V{i}" });
                }
            }

            File.WriteAllText(Path.Combine(AppPaths.DataDir, "app-package-check.txt"), $"package={HabitGrid.Core.Services.PackageIdentity.FullName}\n");
            return 0;
        }
        catch (Exception ex)
        {
            LogSafe("package-check", ex);
            return 2;
        }
    }

    private void RegisterGlobalErrorHandlers()
    {
        UnhandledException += (_, e) =>
        {
            // Ghi log và giữ app sống; trang lỗi thân thiện sẽ do shell hiển thị ở gói sau.
            LogSafe("UnhandledException", e.Exception);
            CrashReporting.Record("UnhandledException", e.Exception);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogSafe("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            CrashReporting.Record("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogSafe("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void LogSafe(string where, Exception? ex)
    {
        try
        {
            var logger = Host is null ? new FileLogger(AppPaths.LogDir) : Host.Get<IAppLogger>();
            logger.Error(where, ex);
        }
        catch
        {
            // không để lỗi ghi log che lỗi gốc
        }
    }

    private static void ReportFatal(Exception ex)
    {
        LogSafe("Fatal startup error", ex);
        CrashReporting.Record("Fatal startup error", ex);
        MessageBox(
            IntPtr.Zero,
            $"HabitGrid không thể khởi động.\n\n{ex.Message}\n\nNhật ký: {AppPaths.LogDir}",
            "HabitGrid",
            0x10);
        Environment.Exit(1);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
