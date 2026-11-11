using System.Windows;
using System.Windows.Threading;
using HabitGrid.Core.Domain.Widgets;
using HabitGrid.Platform.Desktop;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Tự ẩn widget khi có ứng dụng toàn màn hình (trò chơi, video, trình chiếu…) ở phía trước, hiện lại khi hết.
/// Chỉ cần khi widget nằm trên cùng (kiểu ghim "luôn trên cùng", mặc định của Dynamic Island); widget ghim ở lớp desktop vốn nằm dưới mọi cửa sổ.
/// Đo mỗi giây; ẩn ngay khi thấy, hiện lại sau hai lần đo liên tiếp không thấy (tránh nhấp nháy khi Alt+Tab).
/// </summary>
public sealed partial class WidgetController
{
    private readonly FullscreenGate _fullscreenGate = new(showAfter: 2);
    private readonly DispatcherTimer _fullscreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Bộ phát hiện app toàn màn hình (thay được trong tự kiểm tra).</summary>
    public IFullscreenDetector Fullscreen { get; set; } = new WindowsFullscreenDetector();

    /// <summary>Widget đang bị ẩn vì có app toàn màn hình.</summary>
    public bool FullscreenSuppressed => _fullscreenGate.Suppressed;

    private void StartFullscreenWatch()
    {
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();
        _fullscreenTimer.Start();
    }

    /// <summary>Đo một lần và ẩn/hiện widget nếu trạng thái đổi (cũng dùng cho tự kiểm tra).</summary>
    public void CheckFullscreen()
    {
        var topmost = _appliedPin == PinChoice.Top || _placement == WidgetPlacementMode.TopCenter;
        var changed = _fullscreenGate.Update(topmost && Fullscreen.IsFullscreenAppActive());

        // Đang ẩn thì ẩn luôn cả cửa sổ mới dựng lại trong lúc đó.
        if (changed || _fullscreenGate.Suppressed)
        {
            ApplyVisibility();
        }
    }

    /// <summary>Mọi cửa sổ widget đang quản lý.</summary>
    private IEnumerable<Window> AllWidgetWindows()
    {
        foreach (var window in new Window?[] { _todayList, _momentWidget, _hub })
        {
            if (window is not null)
            {
                yield return window;
            }
        }

        foreach (var (window, _) in _widgets.Values)
        {
            yield return window;
        }

        foreach (var extra in _extras.Values)
        {
            yield return extra;
        }
    }

    /// <summary>Hiện khi người dùng không tắt bằng phím tắt VÀ không có app toàn màn hình; ngược lại ẩn.</summary>
    private void ApplyVisibility()
    {
        var show = _visible && !_fullscreenGate.Suppressed;

        foreach (var window in AllWidgetWindows())
        {
            if (show && !window.IsVisible)
            {
                window.Show();
            }
            else if (!show && window.IsVisible)
            {
                window.Hide();
            }
        }
    }
}
