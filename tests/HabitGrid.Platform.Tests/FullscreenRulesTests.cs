using FluentAssertions;
using HabitGrid.Platform.Desktop;

namespace HabitGrid.Platform.Tests;

public class FullscreenRulesTests
{
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void BorderlessWindowCoveringTheMonitor_IsFullscreen()
    {
        FullscreenRules.IsFullscreen(new ScreenRect(0, 0, 1920, 1080), Monitor, "UnityWndClass", hasCaption: false, isMinimized: false, isVisible: true).Should().BeTrue();
    }

    [Fact]
    public void MaximizedNormalWindow_WithATitleBar_IsNotFullscreen_EvenWithAnAutoHideTaskbar()
    {
        // Phóng to bình thường lớn hơn màn hình vài pixel vì viền, nhưng vẫn có thanh tiêu đề.
        FullscreenRules.IsFullscreen(new ScreenRect(-8, -8, 1936, 1096), Monitor, "Chrome_WidgetWin_1", hasCaption: true, isMinimized: false, isVisible: true).Should().BeFalse();
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("shell_traywnd")]
    public void TheDesktopAndTheTaskbar_AreNeverFullscreen(string className)
    {
        FullscreenRules.IsFullscreen(Monitor, Monitor, className, hasCaption: false, isMinimized: false, isVisible: true).Should().BeFalse();
    }

    [Fact]
    public void SmallerThanTheMonitor_IsNotFullscreen()
    {
        FullscreenRules.IsFullscreen(new ScreenRect(0, 0, 1920, 1040), Monitor, "Notepad", hasCaption: false, isMinimized: false, isVisible: true).Should().BeFalse();
        FullscreenRules.IsFullscreen(new ScreenRect(100, 0, 1820, 1080), Monitor, "Notepad", hasCaption: false, isMinimized: false, isVisible: true).Should().BeFalse();
    }

    [Fact]
    public void HiddenOrMinimisedWindows_AreIgnored()
    {
        FullscreenRules.IsFullscreen(Monitor, Monitor, "Game", hasCaption: false, isMinimized: true, isVisible: true).Should().BeFalse();
        FullscreenRules.IsFullscreen(Monitor, Monitor, "Game", hasCaption: false, isMinimized: false, isVisible: false).Should().BeFalse();
    }

    [Fact]
    public void WorksOnASecondMonitorWithOffsetCoordinates()
    {
        var second = new ScreenRect(1920, 0, 2560, 1440);

        FullscreenRules.IsFullscreen(new ScreenRect(1920, 0, 2560, 1440), second, "Game", hasCaption: false, isMinimized: false, isVisible: true).Should().BeTrue();
        FullscreenRules.IsFullscreen(new ScreenRect(0, 0, 1920, 1080), second, "Game", hasCaption: false, isMinimized: false, isVisible: true).Should().BeFalse();
    }

    [Fact]
    public void Gate_HidesImmediately_AndShowsAgainOnlyAfterConsecutiveClearReadings()
    {
        var gate = new FullscreenGate(showAfter: 2);

        gate.Update(false).Should().BeFalse();
        gate.Suppressed.Should().BeFalse();

        gate.Update(true).Should().BeTrue("ẩn ngay khi thấy toàn màn hình");
        gate.Suppressed.Should().BeTrue();

        gate.Update(false).Should().BeFalse("mới một lần không thấy, chưa hiện lại");
        gate.Suppressed.Should().BeTrue();

        gate.Update(true).Should().BeFalse("tín hiệu chập chờn: đếm lại từ đầu");
        gate.Update(false).Should().BeFalse();
        gate.Update(false).Should().BeTrue("đủ hai lần liên tiếp thì hiện lại");
        gate.Suppressed.Should().BeFalse();
    }

    [Fact]
    public void Gate_RejectsAnInvalidThreshold()
    {
        var act = () => new FullscreenGate(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
