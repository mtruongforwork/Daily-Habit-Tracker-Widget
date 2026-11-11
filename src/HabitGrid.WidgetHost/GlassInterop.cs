using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Kính mờ (Acrylic) thật cho cửa sổ WPF: Windows làm mờ hình nền và mọi thứ phía sau cửa sổ. Dùng <c>SetWindowCompositionAttribute</c>
/// (cách duy nhất hoạt động cả khi cửa sổ không bao giờ được kích hoạt như widget) cộng góc bo của Windows 11.
/// Chỉ dùng khi cửa sổ KHÔNG phải cửa sổ phân lớp (<c>AllowsTransparency = false</c>); Windows cũ hơn 11 thì giữ thẻ đặc.
/// </summary>
public static class GlassInterop
{
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int WcaAccentPolicy = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Có dùng kính mờ được không: Windows 11 (build 22000 trở lên).</summary>
    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22000;

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Bật kính mờ cho cửa sổ. <paramref name="tintArgb"/> là màu phủ lên kính (alpha càng thấp càng trong).
    /// Trả về false nếu Windows từ chối (khi đó cửa sổ vẫn dùng được, chỉ không có kính).
    /// </summary>
    public static bool Enable(IntPtr hwnd, Color tint, bool dark)
    {
        if (!IsSupported || hwnd == IntPtr.Zero)
        {
            return false;
        }

        // Bản thân HwndSource phải trong suốt để kính lộ ra (WPF mặc định vẽ nền đen đặc cho cửa sổ không phân lớp).
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }

        var accent = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = (uint)((tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R)
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var pointer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(accent, pointer, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = pointer, SizeOfData = size };
            var ok = SetWindowCompositionAttribute(hwnd, ref data) != 0;

            var round = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
            var darkMode = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
            return ok;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
