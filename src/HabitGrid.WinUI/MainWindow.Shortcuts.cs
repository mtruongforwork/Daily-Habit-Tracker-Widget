using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using HabitGrid.WinUI.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace HabitGrid.WinUI;

/// <summary>Phím tắt trong app (SPEC-V2-09 §3): Ctrl+K lệnh nhanh, Ctrl+N thói quen mới, Ctrl+Z hoàn tác, Ctrl+, cài đặt, 1–9 check trên trang Hôm nay.</summary>
public sealed partial class MainWindow
{
    private const VirtualKey Comma = (VirtualKey)188;
    private bool _paletteOpen;

    private void RegisterShortcuts()
    {
        AddAccelerator(VirtualKey.K, async () => await ShowPaletteAsync());
        AddAccelerator(VirtualKey.N, async () => await AddHabitAsync());
        AddAccelerator(Comma, () =>
        {
            Navigate(PageKeys.Settings);
            return Task.CompletedTask;
        });
        AddAccelerator(VirtualKey.Z, UndoAsync, skipWhenTyping: true);

        Root.KeyDown += async (_, e) => await OnRootKeyDownAsync(e);
    }

    private void AddAccelerator(VirtualKey key, Func<Task> action, bool skipWhenTyping = false)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += async (_, args) =>
        {
            // Ô nhập đang có focus thì để nó tự xử lý (ví dụ Ctrl+Z trong ô tên thói quen).
            if (skipWhenTyping && IsTyping())
            {
                return;
            }

            args.Handled = true;

            if (IsOnboarding)
            {
                return;
            }

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                App.Host.Get<IAppLogger>().Error($"Phím tắt Ctrl+{key} lỗi", ex);
            }
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    private bool IsTyping() =>
        FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or PasswordBox or AutoSuggestBox or NumberBox or RichEditBox;

    /// <summary>1–9 (không kèm phím bổ trợ) trên trang Hôm nay: check dòng tương ứng theo thứ tự hiển thị.</summary>
    private async Task OnRootKeyDownAsync(KeyRoutedEventArgs e)
    {
        if (IsOnboarding || IsTyping() || ContentFrame.Content is not TodayPage page)
        {
            return;
        }

        var modifiers = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        var index = e.Key switch
        {
            >= VirtualKey.Number1 and <= VirtualKey.Number9 => e.Key - VirtualKey.Number1,
            >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => e.Key - VirtualKey.NumberPad1,
            _ => -1
        };

        if (index < 0 || modifiers)
        {
            return;
        }

        e.Handled = true;
        await page.CheckRowAsync(index);
    }

    private async Task UndoAsync()
    {
        switch (ContentFrame.Content)
        {
            case TodayPage:
                var today = App.Host.Get<TodayViewModel>();

                if (today.CanUndo)
                {
                    await today.UndoAsync();
                }

                break;
            case HabitsPage:
                var habits = App.Host.Get<HabitsViewModel>();

                if (habits.LastDeletedId is not null)
                {
                    await habits.UndoDeleteAsync();
                }

                break;
        }
    }

    /// <summary>Danh sách lệnh: trang, hành động, mở/check từng thói quen.</summary>
    private async Task<IReadOnlyList<PaletteCommand>> BuildCommandsAsync()
    {
        var commands = new List<PaletteCommand>
        {
            new(PaletteKind.Page, Ui.T("nav.today"), PageKeys.Today, "📅"),
            new(PaletteKind.Page, Ui.T("nav.habits"), PageKeys.Habits, "📋"),
            new(PaletteKind.Page, Ui.T("nav.calendar"), PageKeys.Calendar, "📅"),
            new(PaletteKind.Page, Ui.T("nav.journal"), PageKeys.Journal, "📝"),
            new(PaletteKind.Page, Ui.T("nav.focus"), PageKeys.Focus, "⏱"),
            new(PaletteKind.Page, Ui.T("nav.insights"), PageKeys.Insights, "📊"),
            new(PaletteKind.Page, Ui.T("nav.buddy"), PageKeys.Buddy, "🐣"),
            new(PaletteKind.Page, Ui.T("nav.settings"), PageKeys.Settings, "⚙️"),
            new(PaletteKind.Action, Ui.T("cmd.new"), "new", "➕"),
            new(PaletteKind.Action, Ui.T("cmd.focus"), "focus", "⏱"),
            new(PaletteKind.Action, Ui.T("cmd.focus.stop"), "focus.stop", "⏹"),
            new(PaletteKind.Action, Ui.T("cmd.quick"), "quick", "⚡"),
            new(PaletteKind.Action, Ui.T("cmd.undo"), "undo", "↩")
        };

        foreach (var habit in await App.Host.Get<IHabitRepository>().GetAllActiveAsync())
        {
            var id = habit.Id.ToString();
            commands.Add(new PaletteCommand(PaletteKind.OpenHabit, Ui.Loc.Format("cmd.open", habit.Name), id, Ui.IconText(habit.Icon.Value)));

            if (habit.Kind != HabitKind.Quit)
            {
                commands.Add(new PaletteCommand(PaletteKind.CheckHabit, Ui.Loc.Format("cmd.check", habit.Name), id, "✓"));
            }
        }

        return commands;
    }

    private async Task ShowPaletteAsync()
    {
        if (_paletteOpen)
        {
            return;
        }

        _paletteOpen = true;

        try
        {
            var chosen = await CommandPaletteDialog.ShowAsync(Root.XamlRoot, await BuildCommandsAsync());

            if (chosen is not null)
            {
                await RunCommandAsync(chosen);
            }
        }
        finally
        {
            _paletteOpen = false;
        }
    }

    /// <summary>Chạy một lệnh của palette (công khai để smoke test gọi trực tiếp).</summary>
    public async Task RunCommandAsync(PaletteCommand command)
    {
        switch (command.Kind)
        {
            case PaletteKind.Page:
                Navigate(command.Argument);
                break;
            case PaletteKind.OpenHabit when Guid.TryParse(command.Argument, out var id):
                Navigate(PageKeys.Habits);
                ContentFrame.Navigate(typeof(HabitDetailPage), id);
                break;
            case PaletteKind.CheckHabit when Guid.TryParse(command.Argument, out var id):
                await new CheckInService(App.Host.Get<IHabitRepository>(), App.Host.Get<IDayLogRepository>())
                    .ToggleAsync(id, App.Host.Get<HabitGrid.Core.Services.IDayProvider>().GetToday());
                await App.Host.Get<TodayViewModel>().LoadAsync();
                break;
            case PaletteKind.Action:
                switch (command.Argument)
                {
                    case "new":
                        await AddHabitAsync();
                        break;
                    case "focus":
                        await HostLink.StartFocusAsync(null);
                        break;
                    case "focus.stop":
                        await HostLink.SendAsync("focus.stop");
                        break;
                    case "quick":
                        await HostLink.SendAsync("quick.show");
                        break;
                    case "undo":
                        await UndoAsync();
                        break;
                }

                break;
        }
    }
}
