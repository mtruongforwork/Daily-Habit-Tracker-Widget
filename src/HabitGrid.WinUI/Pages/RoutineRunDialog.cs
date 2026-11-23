using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Routines;
using HabitGrid.Presentation.Services;
using HabitGrid.WinUI.Plus;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HabitGrid.WinUI.Pages;

/// <summary>
/// Chạy routine hẹn giờ (P4, Plus): đặt thời lượng từng bước rồi chạy nối tiếp. Hết giờ hoặc bấm "Xong" thì bước được đánh dấu hoàn thành;
/// "Bỏ qua" không đánh dấu. Free bấm vào thì mở paywall.
/// </summary>
public static class RoutineRunDialog
{
    /// <summary>Trả về true nếu có bước nào được đánh dấu (để trang nạp lại).</summary>
    public static async Task<bool> ShowAsync(XamlRoot root, Routine routine, DateOnly day)
    {
        var timer = App.Host.Get<RoutineTimerService>();

        if (!timer.IsAvailable)
        {
            await PaywallDialog.ShowAsync(root, HabitGrid.Presentation.ViewModels.PaywallSource.Menu);

            if (!timer.IsAvailable)
            {
                return false;
            }
        }

        var plan = (await timer.PlanAsync(routine.Id, day)).Where(p => !p.IsDone).ToList();

        if (plan.Count == 0)
        {
            await new ContentDialog { XamlRoot = root, Title = routine.Name, Content = Ui.T("routine.run.nothing"), CloseButtonText = Ui.T("plus.close") }.ShowAsync();
            return false;
        }

        var boxes = new List<(TimedStepPlan Step, NumberBox Box)>();
        var setup = new StackPanel { Spacing = 10, MinWidth = 420 };
        setup.Children.Add(new TextBlock { Text = Ui.T("routine.run.hint"), TextWrapping = TextWrapping.Wrap });

        foreach (var step in plan)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"{Ui.IconText(step.Icon)}  {step.Name}", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var box = new NumberBox
            {
                Value = step.Minutes,
                Minimum = RoutineRunner.MinMinutes,
                Maximum = RoutineRunner.MaxMinutes,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Width = 120
            };
            AutomationProperties.SetName(box, Ui.Loc.Format("routine.run.minutesFor", step.Name));
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            setup.Children.Add(row);
            boxes.Add((step, box));
        }

        var changed = false;
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = routine.Name,
            Content = setup,
            PrimaryButtonText = Ui.T("routine.run.start"),
            CloseButtonText = Ui.T("plus.close"),
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        foreach (var (step, box) in boxes)
        {
            var minutes = RoutineTimerService.Clamp(double.IsNaN(box.Value) ? RoutineRunner.DefaultMinutes : (int)box.Value);
            await timer.SetDurationAsync(step.HabitId, minutes);
        }

        var runner = await timer.BuildRunnerAsync(routine.Id, day);

        if (runner is null)
        {
            return false;
        }

        changed = await RunAsync(root, routine, runner, day);
        return changed;
    }

    private static async Task<bool> RunAsync(XamlRoot root, Routine routine, RoutineRunner runner, DateOnly day)
    {
        var check = new CheckInService(App.Host.Get<IDayLogRepository>(), App.Host.Get<ISettingsStore>());
        var logs = App.Host.Get<IDayLogRepository>();
        var marked = 0;
        var stepText = new TextBlock { FontSize = 22, TextWrapping = TextWrapping.Wrap };
        var clock = new TextBlock { FontSize = 48, HorizontalAlignment = HorizontalAlignment.Center };
        var progress = new TextBlock { Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"] };
        var bar = new ProgressBar { Minimum = 0, Maximum = runner.Steps.Count };

        var pause = new Button { Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        var done = new Button { Content = Ui.T("routine.run.done"), Style = (Style)Application.Current.Resources["GradientButtonStyle"] };
        var skip = new Button { Content = Ui.T("routine.run.skip"), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        AutomationProperties.SetName(pause, Ui.T("routine.run.pause"));
        AutomationProperties.SetName(done, Ui.T("routine.run.done"));
        AutomationProperties.SetName(skip, Ui.T("routine.run.skip"));
        AutomationProperties.SetName(clock, Ui.T("routine.run.remaining"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(pause);
        buttons.Children.Add(done);
        buttons.Children.Add(skip);

        var panel = new StackPanel { Spacing = 14, MinWidth = 420 };
        panel.Children.Add(progress);
        panel.Children.Add(bar);
        panel.Children.Add(stepText);
        panel.Children.Add(clock);
        panel.Children.Add(buttons);

        var dialog = new ContentDialog { XamlRoot = root, Title = routine.Name, Content = panel, CloseButtonText = Ui.T("routine.run.stop") };
        var ticker = DispatcherQueue.GetForCurrentThread().CreateTimer();
        ticker.Interval = TimeSpan.FromMilliseconds(500);

        async Task MarkAsync(StepOutcome outcome)
        {
            if (!outcome.Completed)
            {
                return;
            }

            // Đã xong ở nơi khác thì không bật tắt ngược lại.
            if ((await logs.GetAsync(outcome.Step.HabitId, day))?.Status == DayStatus.Done)
            {
                return;
            }

            await check.ToggleAsync(outcome.Step.HabitId, day);
            marked++;
        }

        void Refresh()
        {
            var now = DateTime.UtcNow;

            if (runner.IsFinished)
            {
                ticker.Stop();
                stepText.Text = Ui.Loc.Format("routine.run.finished", marked);
                clock.Text = "🎉";
                progress.Text = string.Empty;
                bar.Value = runner.Steps.Count;
                buttons.Visibility = Visibility.Collapsed;
                dialog.CloseButtonText = Ui.T("plus.close");
                return;
            }

            var left = runner.Remaining(now);
            stepText.Text = $"{runner.Current!.Name}";
            clock.Text = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
            progress.Text = Ui.Loc.Format("routine.run.step", runner.Index + 1, runner.Steps.Count) + " · " + Ui.Loc.Format("routine.run.total", (int)Math.Ceiling(runner.TotalRemaining(now).TotalMinutes));
            bar.Value = runner.Index;
            pause.Content = runner.IsPaused ? Ui.T("routine.run.resume") : Ui.T("routine.run.pause");
        }

        ticker.Tick += async (_, _) =>
        {
            foreach (var outcome in runner.Tick(DateTime.UtcNow))
            {
                await MarkAsync(outcome);
            }

            Refresh();
        };
        pause.Click += (_, _) =>
        {
            if (runner.IsPaused)
            {
                runner.Resume(DateTime.UtcNow);
            }
            else
            {
                runner.Pause(DateTime.UtcNow);
            }

            Refresh();
        };
        done.Click += async (_, _) =>
        {
            if (runner.CompleteCurrent(DateTime.UtcNow) is { } outcome)
            {
                await MarkAsync(outcome);
            }

            Refresh();
        };
        skip.Click += async (_, _) =>
        {
            if (runner.SkipCurrent(DateTime.UtcNow) is { } outcome)
            {
                await MarkAsync(outcome);
            }

            Refresh();
        };

        runner.Start(DateTime.UtcNow);
        Refresh();
        ticker.Start();
        await dialog.ShowAsync();
        ticker.Stop();
        return marked > 0;
    }
}
