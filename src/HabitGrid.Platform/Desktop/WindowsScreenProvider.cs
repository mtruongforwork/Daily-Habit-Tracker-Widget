using System.Runtime.InteropServices;

namespace HabitGrid.Platform.Desktop;

/// <summary>
/// Hiện thực <see cref="IScreenProvider"/> trên Windows bằng EnumDisplayMonitors.
/// </summary>
public sealed class WindowsScreenProvider : IScreenProvider
{
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;

    public IReadOnlyList<ScreenDescriptor> GetScreens()
    {
        var screens = new List<ScreenDescriptor>();
        var primaryDevice = GetPrimaryDeviceName();

        WindowInterop.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new WindowInterop.MonitorInfoEx
            {
                Size = Marshal.SizeOf<WindowInterop.MonitorInfoEx>()
            };

            if (WindowInterop.GetMonitorInfo(hMonitor, ref info))
            {
                var devicePath = GetDevicePath(info.Device);
                var workArea = new ScreenRect(info.Work.Left, info.Work.Top, info.Work.Width, info.Work.Height);

                screens.Add(new ScreenDescriptor(
                    devicePath,
                    workArea,
                    GetDpiScale(hMonitor),
                    string.Equals(devicePath, primaryDevice, StringComparison.OrdinalIgnoreCase),
                    new ScreenRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Width, info.Monitor.Height)));
            }

            return true;
        }, IntPtr.Zero);

        return screens;
    }

    public ScreenDescriptor? GetScreenAt(int x, int y)
    {
        foreach (var screen in GetScreens())
        {
            var area = screen.WorkArea;

            if (x >= area.Left && x < area.Right && y >= area.Top && y < area.Bottom)
            {
                return screen;
            }
        }

        return null;
    }

    public ScreenDescriptor? GetScreenByDevicePath(string devicePath)
    {
        return GetScreens().FirstOrDefault(s => string.Equals(s.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));
    }

    public ScreenDescriptor GetPrimaryScreen()
    {
        var screens = GetScreens();

        return screens.FirstOrDefault(s => s.IsPrimary)
               ?? screens.FirstOrDefault()
               ?? new ScreenDescriptor(@"\\.\DISPLAY1", new ScreenRect(0, 0, 1920, 1080), 1.0, true);
    }

    /// <summary>Lấy định danh PnP của thiết bị hiển thị, dùng làm khoá lưu vị trí widget.</summary>
    private static string GetDevicePath(string deviceName)
    {
        var displayDevice = new WindowInterop.DisplayDevice
        {
            Size = Marshal.SizeOf<WindowInterop.DisplayDevice>()
        };

        if (WindowInterop.EnumDisplayDevices(deviceName, 0, ref displayDevice, 0) &&
            !string.IsNullOrWhiteSpace(displayDevice.DeviceID))
        {
            return displayDevice.DeviceID;
        }

        return deviceName;
    }

    /// <summary>Tên thiết bị hiển thị chính, ví dụ "\\.\DISPLAY1".</summary>
    private static string GetPrimaryDeviceName()
    {
        for (uint index = 0; index < 16; index++)
        {
            var displayDevice = new WindowInterop.DisplayDevice
            {
                Size = Marshal.SizeOf<WindowInterop.DisplayDevice>()
            };

            if (!WindowInterop.EnumDisplayDevices(null, index, ref displayDevice, 0))
            {
                break;
            }

            if ((displayDevice.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                return displayDevice.DeviceName;
            }
        }

        return @"\\.\DISPLAY1";
    }

    /// <summary>Tỷ lệ DPI của màn hình (1.0 = 100%).</summary>
    private static double GetDpiScale(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero)
        {
            return 1.0;
        }

        try
        {
            var result = GetDpiForMonitor(hMonitor, MonitorDpiType.EffectiveDpi, out var dpiX, out _);
            return result == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
        }
        catch (DllNotFoundException)
        {
            return 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    private const int MDT_EFFECTIVE_DPI = 0;

    private enum MonitorDpiType
    {
        EffectiveDpi = MDT_EFFECTIVE_DPI
    }

    [DllImport("Shcore.dll", SetLastError = true)]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);
}
