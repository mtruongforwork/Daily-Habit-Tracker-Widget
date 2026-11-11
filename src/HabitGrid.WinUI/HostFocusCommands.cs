using HabitGrid.Core.Domain.Focus;
using HabitGrid.Presentation.Services;

namespace HabitGrid.WinUI;

/// <summary>Điều khiển Focus của trang Focus: gửi lệnh tới WidgetHost (nơi phiên thật sự chạy) qua <see cref="HostLink"/>.</summary>
public sealed class HostFocusCommands : IFocusCommands
{
    public Task StartAsync(Guid? habitId, int workMinutes, FocusMode mode, int breakMinutes) =>
        HostLink.StartFocusAsync(habitId, workMinutes, mode, breakMinutes);

    public Task ToggleAsync() => HostLink.ToggleFocusAsync();

    public Task StopAsync() => HostLink.StopFocusAsync();
}
