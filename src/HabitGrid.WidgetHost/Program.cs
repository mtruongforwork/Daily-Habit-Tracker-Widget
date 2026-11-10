using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using System.Windows;
using CommunityToolkit.WinUI.Notifications;
using HabitGrid.Core.Repositories;
using HabitGrid.Core.Settings;
using HabitGrid.Data.V3;

namespace HabitGrid.WidgetHost;

/// <summary>Cài đặt v1 chỉ dùng cho menu khay (chế độ sắp xếp); giữ trong bộ nhớ.</summary>
internal sealed class MemorySettings : ISettingsRepository
{
    private readonly Dictionary<string, string> _values = [];

    public Task<string?> GetAsync(string key) => Task.FromResult(_values.GetValueOrDefault(key));

    public Task<T> GetAsync<T>(string key, T defaultValue) =>
        Task.FromResult(_values.TryGetValue(key, out var raw) ? (T)Convert.ChangeType(raw, typeof(T), System.Globalization.CultureInfo.InvariantCulture) : defaultValue);

    public Task SetAsync<T>(string key, T value)
    {
        _values[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return Task.CompletedTask;
    }
}

public static class Program
{
    private const string DataDirVariable = "HABITGRID_DATA_DIR";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--package-check", StringComparer.OrdinalIgnoreCase))
        {
            return PackageCheck();
        }

        var explorerTest = args.Contains("--selftest-explorer", StringComparer.OrdinalIgnoreCase);
        var selfTest = explorerTest || args.Contains("--selftest", StringComparer.OrdinalIgnoreCase);
        var customDir = Environment.GetEnvironmentVariable(DataDirVariable);
        var dataDir = string.IsNullOrWhiteSpace(customDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HabitGrid")
            : Path.GetFullPath(customDir);
        var dbPath = Path.Combine(dataDir, HabitGridV3.FileName);

        // Một WidgetHost cho mỗi thư mục dữ liệu.
        var key = "HabitGrid.WidgetHost." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDir)))[..12];
        using var mutex = new Mutex(true, key, out var isFirst);

        if (!isFirst)
        {
            return 0;
        }

        // Chưa có CSDL v3 (app chính chưa chạy lần nào): không tự tạo, thoát im lặng.
        if (!File.Exists(dbPath))
        {
            return selfTest ? 3 : 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var database = HabitGridV3.Open(dbPath);
        var settings = new AppSettings(new MemorySettings());
        var controller = new WidgetController(database) { EditMode = () => settings.EditMode, OpenMainApp = OpenMainApp, DataDir = dataDir };
        var started = false;
        var pendingToasts = new List<ToastArguments>();

        // Nút trên toast: Windows gọi vào tiến trình đang chạy, hoặc khởi động WidgetHost chỉ để xử lý (khi đó làm sau khi sẵn sàng).
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var parsed = ToastArguments.Parse(e.Argument);
            app.Dispatcher.InvokeAsync(async () =>
            {
                if (started)
                {
                    await controller.HandleToastAsync(parsed);
                }
                else
                {
                    pendingToasts.Add(parsed);
                }
            });
        };
        var exitCode = 0;

        app.Startup += async (_, _) =>
        {
            try
            {
                await controller.StartAsync();
                started = true;

                foreach (var pending in pendingToasts)
                {
                    await controller.HandleToastAsync(pending);
                }

                pendingToasts.Clear();

                if (selfTest)
                {
                    exitCode = explorerTest
                        ? await SelfTest.RunExplorerRestartAsync(controller, dbPath)
                        : await SelfTest.RunAsync(controller, dbPath);
                    app.Shutdown();
                    return;
                }

                var tray = new HabitGrid.App.SystemTrayManager(
                    settings,
                    OpenMainApp,
                    OpenMainApp,
                    controller.ToggleVisibility,
                    () => app.Shutdown());
                app.Exit += (_, _) => tray.Dispose();

                Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
                {
                    if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
                    {
                        app.Dispatcher.InvokeAsync(controller.OnSuspendAsync);
                    }
                    else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                    {
                        app.Dispatcher.InvokeAsync(controller.OnResumeAsync);
                    }
                };

                // Đổi giờ hệ thống / múi giờ: tính lại ngày logic và lịch nhắc (SPEC-V2-09 §4).
                Microsoft.Win32.SystemEvents.TimeChanged += (_, _) =>
                {
                    System.Globalization.CultureInfo.CurrentCulture.ClearCachedData();
                    TimeZoneInfo.ClearCachedData();
                    app.Dispatcher.InvokeAsync(controller.RefreshAsync);
                };
            }
            catch (Exception ex)
            {
                Log(dataDir, ex);
                exitCode = 2;
                app.Shutdown();
            }
        };

        app.Run();
        controller.Dispose();
        database.Dispose();
        return exitCode;
    }

    /// <summary>
    /// CI kiểm tra gói MSIX đã cài: tiến trình này phải có danh tính gói và thấy CHÍNH CSDL mà app chính vừa tạo
    /// (app chính chạy <c>--package-check</c> trước để gieo hai thói quen mẫu). 0 = đạt, 2 = hỏng, 3 = không thấy CSDL.
    /// </summary>
    private static int PackageCheck()
    {
        try
        {
            if (!HabitGrid.Core.Services.PackageIdentity.IsPackaged)
            {
                Console.Error.WriteLine("Không có danh tính gói.");
                return 2;
            }

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HabitGrid");
            var path = Path.Combine(dir, HabitGridV3.FileName);

            if (!File.Exists(path))
            {
                return 3;
            }

            using var database = HabitGridV3.Open(path);
            var habits = new HabitGrid.Data.V3.HabitRepository(database, new HabitGrid.Core.Domain.HlcClock("pkgcheck"), null).GetAllActiveAsync().GetAwaiter().GetResult();
            var found = habits.Count(h => h.Name.StartsWith("PKG-CHECK", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(dir, "widgethost-package-check.txt"), $"package={HabitGrid.Core.Services.PackageIdentity.FullName}\nhabits={found}\n");
            return found >= 2 ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    private static void OpenMainApp()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "HabitGrid.WinUI.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "HabitGrid.WinUI", "HabitGrid.WinUI.exe")
        })
        {
            if (File.Exists(candidate))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(candidate) { UseShellExecute = true });
                return;
            }
        }
    }

    private static void Log(string dataDir, Exception ex)
    {
        try
        {
            var dir = Path.Combine(dataDir, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "widgethost.log"), $"{DateTime.Now:HH:mm:ss} {ex}\n");
        }
        catch
        {
            // Không để việc ghi log làm hỏng tiến trình.
        }
    }
}
