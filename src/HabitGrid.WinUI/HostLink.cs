using System.Diagnostics;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;

namespace HabitGrid.WinUI;

/// <summary>
/// Gửi lệnh tới HabitGrid Widgets (WidgetHost) qua bảng settings chung (WidgetHost đọc mỗi 2 giây) và bảo đảm nó đang chạy.
/// Đơn giản và đủ dùng cho vài lệnh hiếm (Focus, Quick check-in); không cần named pipe.
/// </summary>
public static class HostLink
{
    private const string ProcessName = "HabitGrid.WidgetHost";
    private const string ExeName = ProcessName + ".exe";

    /// <summary>Khớp <c>Alias</c> trong AppxManifest.WinUI.xml.</summary>
    public const string PackagedAlias = "habitgrid-widgets.exe";

    public static async Task<bool> SendAsync(string command)
    {
        await App.Host.Get<ISettingsStore>().SetAsync(ReminderSettingsStore.HostCommandKey, command);
        return EnsureRunning();
    }

    public static Task<bool> StartFocusAsync(Guid? habitId, int? minutes = null) =>
        SendAsync($"focus.start|{habitId?.ToString() ?? string.Empty}|{minutes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty}");

    /// <summary>True nếu WidgetHost đang chạy hoặc vừa khởi động được.</summary>
    public static bool EnsureRunning()
    {
        if (Process.GetProcessesByName(ProcessName).Length > 0)
        {
            return true;
        }

        // Trong gói MSIX: mở qua alias để WidgetHost có cùng danh tính gói (và vì thế cùng vùng dữ liệu) với app chính.
        var path = HabitGrid.Core.Services.PackageIdentity.IsPackaged ? PackagedAlias : FindExecutable();

        if (path is null)
        {
            return false;
        }

        try
        {
            var start = new ProcessStartInfo(path) { UseShellExecute = false };

            // Smoke test / dữ liệu riêng: WidgetHost phải dùng cùng thư mục dữ liệu với app.
            if (AppPaths.IsCustomDataDir)
            {
                start.Environment[AppPaths.DataDirVariable] = AppPaths.DataDir;
            }

            Process.Start(start);
            return true;
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Không khởi động được WidgetHost", ex);
            return false;
        }
    }

    /// <summary>Bản cài: cùng thư mục với app. Bản dev: tìm trong thư mục build của dự án WidgetHost.</summary>
    private static string? FindExecutable()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, ExeName);

        if (File.Exists(beside))
        {
            return beside;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var project = Path.Combine(dir.FullName, "HabitGrid.WidgetHost", "bin");

            if (Directory.Exists(project))
            {
                return Directory.EnumerateFiles(project, ExeName, SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault()?.FullName;
            }
        }

        return null;
    }
}
