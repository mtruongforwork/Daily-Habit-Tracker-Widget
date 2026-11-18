using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Insights;
using HabitGrid.Presentation.ViewModels;
using HabitGrid.WinUI.Controls;
using HabitGrid.WinUI.Plus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HabitGrid.WinUI.Pages;

/// <summary>Insights nâng cao (P2, Plus): tháng so với tháng, tương quan thói quen, so sánh hai thói quen, lịch năm gộp.</summary>
public sealed partial class InsightsPage
{
    /// <summary>Cuộn tới phần nâng cao (smoke chụp ảnh phần nằm dưới màn hình).</summary>
    public void ScrollToAdvancedForTest(bool toEnd)
    {
        Scroll.UpdateLayout();
        Scroll.ChangeView(null, toEnd ? Scroll.ScrollableHeight : 0, null, disableAnimation: true);
    }

    private Guid? _compareA;
    private Guid? _compareB;

    /// <summary>
    /// Tầng 3 + 4 của lưới Insights. Cột trái: từng thói quen + so với tháng trước; cột phải: so sánh hai thói quen + thói quen đi cùng nhau;
    /// cuối cùng là lịch cả năm trải 4 cột. Free: cột phải là thẻ giới thiệu, bấm "Mở khóa" mới mở paywall.
    /// </summary>
    private void AddAdvanced(HabitGrid.WinUI.Controls.BentoGrid grid, UIElement habits)
    {
        var left = new StackPanel { Spacing = 16 };
        left.Children.Add(habits);

        if (ViewModel.Advanced is not { } data)
        {
            grid.Add(left, columns: 2);
            grid.Add(BuildAdvanced(), columns: 2);
            return;
        }

        left.Children.Add(BuildMonths(data.Months));
        var right = new StackPanel { Spacing = 16 };
        right.Children.Add(BuildCompare());
        right.Children.Add(BuildCorrelations(data.Correlations));
        grid.Add(left, columns: 2);
        grid.Add(right, columns: 2);
        grid.Add(BuildYear(data.YearHeatmap), columns: 4);
    }

    /// <summary>Thẻ giới thiệu phần nâng cao cho gói Free.</summary>
    private UIElement BuildAdvanced()
    {
        var stack = new StackPanel { Spacing = 16 };

        if (ViewModel.Advanced is null)
        {
            var teaser = new StackPanel { Spacing = 8 };
            teaser.Children.Add(new TextBlock { Text = "🔒 " + Ui.T("adv.locked.title"), Style = Res("CardTitleTextStyle") });
            teaser.Children.Add(new TextBlock { Text = Ui.T("adv.locked.body"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
            var unlock = new Button { Content = Ui.T("adv.unlock"), Style = Res("GradientButtonStyle") };
            unlock.Click += async (_, _) =>
            {
                if (await PaywallDialog.ShowAsync(XamlRoot, PaywallSource.Menu))
                {
                    await ViewModel.LoadAsync();
                    Build();
                }
            };
            teaser.Children.Add(unlock);
            stack.Children.Add(new Border { Style = Res("GlassCard"), Child = teaser });
        }

        return stack;
    }

    private UIElement BuildMonths(MonthComparison months)
    {
        var stack = new StackPanel { Spacing = 8 };
        var culture = Ui.CurrentCulture;
        stack.Children.Add(new TextBlock
        {
            Text = Ui.Loc.Format("adv.months.range", months.ThisFrom.ToString("dd/MM", culture), months.ThisTo.ToString("dd/MM", culture), months.LastFrom.ToString("dd/MM", culture), months.LastTo.ToString("dd/MM", culture)),
            Style = Res("BodySecondaryTextStyle")
        });

        stack.Children.Add(new TextBlock
        {
            Text = Ui.Loc.Format("adv.months.overall", Percent(months.OverallThis), Percent(months.OverallLast), Arrow(months.OverallPoints)),
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = Ui.Loc.Format("adv.months.checkins", months.CheckInsThis, months.CheckInsLast),
            Style = Res("BodySecondaryTextStyle")
        });

        foreach (var row in months.Habits)
        {
            var line = new Grid { ColumnSpacing = 10 };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(new TextBlock { Text = $"{Ui.IconText(row.Icon)}  {row.Name}", TextTrimming = TextTrimming.CharacterEllipsis });
            var delta = new TextBlock
            {
                Text = $"{Percent(row.ThisMonth)} · {Percent(row.LastMonth)}  {Arrow(row.Points)}",
                Foreground = row.Points switch
                {
                    > 0 => (Brush)Application.Current.Resources["SuccessBrush"],
                    < 0 => (Brush)Application.Current.Resources["WarningBrush"],
                    _ => (Brush)Application.Current.Resources["TextSecondaryBrush"]
                }
            };
            Grid.SetColumn(delta, 1);
            line.Children.Add(delta);
            stack.Children.Add(line);
        }

        return Card(Ui.T("adv.months"), stack);
    }

    private UIElement BuildCorrelations(IReadOnlyList<HabitCorrelation> found)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = Ui.T("adv.corr.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });

        if (found.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("adv.corr.none"), TextWrapping = TextWrapping.Wrap });
        }

        foreach (var c in found)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "🔗  " + Ui.Loc.Format("adv.corr.line", c.A.Name, c.B.Name, (int)Math.Round(c.BGivenA * 100), (int)Math.Round(c.BGivenNotA * 100)),
                TextWrapping = TextWrapping.Wrap
            });
        }

        return Card(Ui.T("adv.corr"), stack);
    }

    private UIElement BuildCompare()
    {
        var stack = new StackPanel { Spacing = 10 };
        var habits = ViewModel.Habits;

        if (habits.Count < 2)
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("adv.compare.need2"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
            return Card(Ui.T("adv.compare"), stack);
        }

        _compareA ??= habits[0].Id;
        _compareB ??= habits[1].Id;
        var result = new StackPanel { Spacing = 8 };

        ComboBox Picker(Guid selected, Action<Guid> onChange, string header)
        {
            var box = new ComboBox { Header = header, MinWidth = 220 };
            AutomationProperties.SetName(box, header);

            foreach (var habit in habits)
            {
                box.Items.Add(new ComboBoxItem { Content = $"{Ui.IconText(habit.Icon.Value)}  {habit.Name}", Tag = habit.Id });
            }

            box.SelectedIndex = habits.ToList().FindIndex(h => h.Id == selected);
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is ComboBoxItem { Tag: Guid id })
                {
                    onChange(id);
                    RenderComparison(result);
                }
            };
            return box;
        }

        var pickers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        pickers.Children.Add(Picker(_compareA.Value, id => _compareA = id, Ui.T("adv.compare.a")));
        pickers.Children.Add(Picker(_compareB.Value, id => _compareB = id, Ui.T("adv.compare.b")));
        stack.Children.Add(pickers);
        stack.Children.Add(result);
        RenderComparison(result);
        return Card(Ui.T("adv.compare"), stack);
    }

    private void RenderComparison(StackPanel host)
    {
        host.Children.Clear();

        if (_compareA is not { } a || _compareB is not { } b || ViewModel.Compare(a, b) is not { } c)
        {
            host.Children.Add(new TextBlock { Text = Ui.T("adv.compare.same"), Style = Res("BodySecondaryTextStyle") });
            return;
        }

        void Row(string label, string left, string right)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, Style = Res("BodySecondaryTextStyle") });
            var l = new TextBlock { Text = left };
            var r = new TextBlock { Text = right };
            Grid.SetColumn(l, 1);
            Grid.SetColumn(r, 2);
            row.Children.Add(l);
            row.Children.Add(r);
            host.Children.Add(row);
        }

        Row(string.Empty, $"{Ui.IconText(c.A.Icon.Value)} {c.A.Name}", $"{Ui.IconText(c.B.Icon.Value)} {c.B.Name}");
        Row(Ui.T("insights.rate"), Percent(c.RateA), Percent(c.RateB));
        Row(Ui.T("adv.compare.streak"), c.StreakA.ToString(Ui.CurrentCulture), c.StreakB.ToString(Ui.CurrentCulture));
        Row(Ui.T("insights.beststreak"), c.BestA.ToString(Ui.CurrentCulture), c.BestB.ToString(Ui.CurrentCulture));
        Row(Ui.T("insights.checkins"), c.DoneA.ToString(Ui.CurrentCulture), c.DoneB.ToString(Ui.CurrentCulture));
        host.Children.Add(new TextBlock { Text = Ui.Loc.Format("adv.compare.both", c.BothDone), Style = Res("BodySecondaryTextStyle") });

        // Xu hướng 12 tuần của hai thói quen: hai dãy cột cạnh nhau.
        var chart = new Grid { Height = 110, ColumnSpacing = 4 };

        for (var i = 0; i < c.Weeks.Count; i++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            var pair = new Grid { VerticalAlignment = VerticalAlignment.Bottom, ColumnSpacing = 1 };
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            pair.ColumnDefinitions.Add(new ColumnDefinition());

            foreach (var (rate, hue, column) in new[] { (c.Weeks[i].RateA, c.A.ColorHue, 0), (c.Weeks[i].RateB, c.B.ColorHue, 1) })
            {
                var bar = new Border
                {
                    Height = Math.Max(3, (rate ?? 0) * 100),
                    CornerRadius = new CornerRadius(3),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = rate is null ? (Brush)Application.Current.Resources["HeatEmptyBrush"] : Ui.GradientBrush(HabitGradient.From(hue))
                };
                Grid.SetColumn(bar, column);
                pair.Children.Add(bar);
            }

            AutomationProperties.SetName(pair, $"{c.Weeks[i].WeekStart:dd/MM}: {Percent(c.Weeks[i].RateA)} / {Percent(c.Weeks[i].RateB)}");
            Grid.SetColumn(pair, i);
            chart.Children.Add(pair);
        }

        host.Children.Add(chart);
    }

    private UIElement BuildYear(HeatmapData data)
    {
        var scroll = new ScrollViewer
        {
            // Ô to hơn (16) và căn giữa: lưới cả năm lấp chiều rộng 4 cột thay vì dồn sang trái.
            Content = new HeatmapGrid { Data = data, ColorHue = "#8B5CF6", CellSize = 16, IsInteractive = true, HorizontalAlignment = HorizontalAlignment.Center },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled
        };
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = Ui.T("adv.year.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(scroll);
        return Card(Ui.T("adv.year"), stack);
    }

    private static string Percent(double? value) => value is { } v ? $"{Math.Round(v * 100)}%" : "—";

    private static string Arrow(int? points) => points switch
    {
        > 0 => $"▲ {points}",
        < 0 => $"▼ {-points}",
        0 => "＝",
        _ => string.Empty
    };
}
