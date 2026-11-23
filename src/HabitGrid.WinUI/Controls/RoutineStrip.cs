using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HabitGrid.WinUI.Controls;

/// <summary>
/// Dải routine trên trang Hôm nay (F1): mỗi routine một thẻ gồm tiến độ, các bước (bấm để check) và nút "Xong cả routine".
/// Thẻ thói quen ở bên dưới vẫn giữ nguyên; routine là lối tắt để làm theo chuỗi.
/// </summary>
public sealed class RoutineStrip : UserControl
{
    private readonly StackPanel _panel = new() { Spacing = 12 };

    public RoutineStrip()
    {
        IsTabStop = false;
        Content = _panel;
    }

    /// <summary>Có ít nhất một routine đang hiển thị.</summary>
    public bool HasItems => _panel.Children.Count > 0;

    /// <summary>Phát khi người dùng check một bước hoặc xong cả routine (trang Hôm nay nạp lại thẻ).</summary>
    public event EventHandler? Changed;

    public async Task ReloadAsync(DateOnly day)
    {
        var progress = await App.Host.Get<RoutineService>().ProgressAsync(day);
        _panel.Children.Clear();

        foreach (var routine in progress)
        {
            _panel.Children.Add(BuildCard(routine, day));
        }

        Visibility = _panel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement BuildCard(RoutineProgress progress, DateOnly day)
    {
        var routine = progress.Routine;
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = Ui.IconText(routine.Icon.Value), FontSize = 24, VerticalAlignment = VerticalAlignment.Center });

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = routine.Name, Style = (Style)Application.Current.Resources["CardTitleTextStyle"], TextTrimming = TextTrimming.CharacterEllipsis });
        titles.Children.Add(new TextBlock
        {
            Text = progress.IsComplete ? Ui.T("routine.complete") : Ui.Loc.Format("routine.progress", progress.Done, progress.Total),
            Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"]
        });
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);

        var all = new Button
        {
            Content = progress.IsComplete ? "✓" : Ui.T("routine.completeAll"),
            Style = (Style)Application.Current.Resources[progress.IsComplete ? "GhostButtonStyle" : "GradientButtonStyle"],
            IsEnabled = !progress.IsComplete
        };
        AutomationProperties.SetName(all, $"{Ui.T("routine.completeAll")}: {routine.Name}");
        all.Click += async (_, _) =>
        {
            await App.Host.Get<RoutineService>().CompleteAllAsync(routine.Id, day);
            Changed?.Invoke(this, EventArgs.Empty);
        };
        Grid.SetColumn(all, 3);
        head.Children.Add(all);

        // P4: chạy routine hẹn giờ từng bước. Free bấm vào thì mở paywall.
        var run = new Button { Content = "⏱ " + Ui.T("routine.run.title"), Style = (Style)Application.Current.Resources["GhostButtonStyle"], IsEnabled = !progress.IsComplete };
        AutomationProperties.SetName(run, $"{Ui.T("routine.run.title")}: {routine.Name}");
        run.Click += async (_, _) =>
        {
            if (await Pages.RoutineRunDialog.ShowAsync(XamlRoot, routine, day))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        Grid.SetColumn(run, 2);
        head.Children.Add(run);

        var track = new Grid { Height = 8, CornerRadius = new CornerRadius(4), Background = (Brush)Application.Current.Resources["HeatEmptyBrush"] };
        var fill = new Border { CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Background = (Brush)Application.Current.Resources["AccentGradientBrush"] };
        track.Children.Add(fill);
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * progress.Fraction;

        var steps = new ItemsRepeater
        {
            Layout = new UniformGridLayout { MinItemWidth = 170, MinColumnSpacing = 8, MinRowSpacing = 8, ItemsStretch = UniformGridLayoutItemsStretch.Fill }
        };
        steps.ItemsSource = progress.Steps.Select(step => BuildStep(step, day)).ToList();

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(head);
        stack.Children.Add(track);
        stack.Children.Add(steps);
        return new Border { Style = (Style)Application.Current.Resources["CardBorder"], Child = stack };
    }

    private UIElement BuildStep(RoutineStep step, DateOnly day)
    {
        var stops = HabitGradient.From(step.ColorHue);
        var text = new TextBlock
        {
            Text = $"{(step.IsDone ? "✓" : "○")}  {Ui.IconText(step.Icon)} {step.Name}",
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = step.IsDone ? (Brush)Application.Current.Resources["OnGradientBrush"] : (Brush)Application.Current.Resources["TextPrimaryBrush"]
        };

        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(14),
            Background = step.IsDone ? Ui.GradientBrush(stops) : (Brush)Application.Current.Resources["CardIdleBrush"],
            BorderBrush = Ui.GradientBrush(stops, 0.6),
            BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetName(button, $"{step.Name}, {Ui.T(step.IsDone ? "today.state.done" : "routine.pending")}");
        button.Click += async (_, _) =>
        {
            await new CheckInService(App.Host.Get<IDayLogRepository>(), App.Host.Get<ISettingsStore>())
                .ToggleAsync(step.HabitId, day);
            Changed?.Invoke(this, EventArgs.Empty);
        };
        return button;
    }
}
