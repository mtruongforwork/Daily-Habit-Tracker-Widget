using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Dashboard;
using HabitGrid.Presentation.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HabitGrid.WinUI.Pages;

/// <summary>
/// Bảng điều khiển tùy biến của trang Hôm nay: bật/tắt và đổi thứ tự các khối (dải tuần, tâm trạng, tổng kết tuần, routine, thử thách,
/// tiến độ + chuỗi). Bố cục lưu trong cài đặt (<see cref="DashboardLayout.SettingKey"/>) và áp dụng ngay khi đổi.
/// </summary>
public sealed partial class TodayPage
{
    private DashboardLayout _layout = DashboardLayout.Default();

    /// <summary>Khung bọc của từng khối, theo id (xem <see cref="DashboardSections"/>).</summary>
    private Dictionary<string, FrameworkElement> Sections => new()
    {
        [DashboardSections.Week] = SecWeek,
        [DashboardSections.Mood] = SecMood,
        [DashboardSections.Recap] = SecRecap,
        [DashboardSections.Routines] = SecRoutines,
        [DashboardSections.Challenges] = SecChallenges,
        [DashboardSections.Hero] = SecHero
    };

    /// <summary>Bố cục đang áp dụng (cho smoke test).</summary>
    public DashboardLayout CurrentLayout => _layout;

    /// <summary>Nạp bố cục đã lưu rồi áp dụng.</summary>
    private async Task LoadLayoutAsync()
    {
        var saved = await App.Host.Get<ISettingsStore>().GetAsync(DashboardLayout.SettingKey);
        _layout = DashboardLayout.Parse(saved);
        ApplyLayout();
    }

    /// <summary>Xếp lại các khung trong cột chính theo bố cục: giữ chỗ chèn ở vị trí khung đầu tiên cũ, ẩn khung bị tắt.</summary>
    public void ApplyLayout()
    {
        // Dùng Scroll.Content (cột chính) thay cho Parent của khung: Parent còn null khi đang điều hướng vào trang.
        if (Scroll.Content is not Panel parent)
        {
            return;
        }

        var sections = Sections;
        var slot = sections.Values.Min(e => parent.Children.IndexOf(e));

        foreach (var element in sections.Values)
        {
            parent.Children.Remove(element);
        }

        var index = slot;

        foreach (var (id, visible) in _layout.Items)
        {
            var element = sections[id];
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            parent.Children.Insert(index++, element);
        }
    }

    private async Task SetLayoutAsync(DashboardLayout layout)
    {
        _layout = layout;
        ApplyLayout();
        await App.Host.Get<ISettingsStore>().SetAsync(DashboardLayout.SettingKey, layout.Serialize());
    }

    private void OnCustomizeClick(object sender, RoutedEventArgs e) => ShowCustomizeFlyout((FrameworkElement)sender);

    private void ShowCustomizeFlyout(FrameworkElement anchor)
    {
        var flyout = new Flyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        flyout.Content = BuildCustomizePanel(flyout);
        flyout.ShowAt(anchor);
    }

    /// <summary>Nội dung bảng tùy chỉnh (cho smoke test kiểm tra dựng được và đủ 6 hàng kéo-thả được).</summary>
    public StackPanel BuildCustomizeContentForTest() => BuildCustomizePanel(new Flyout());

    private StackPanel BuildCustomizePanel(Flyout flyout)
    {
        var panel = new StackPanel { Spacing = 8, Width = 340 };
        panel.Children.Add(new TextBlock { Text = Ui.T("dashboard.title"), Style = (Style)Application.Current.Resources["CardTitleTextStyle"] });
        panel.Children.Add(new TextBlock { Text = Ui.T("dashboard.hint"), Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });

        // Kéo một hàng để đổi thứ tự (ListView tự dựng hoạt ảnh kéo-thả); nút lên/xuống vẫn có cho bàn phím và trình đọc màn hình.
        var rows = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            CanDragItems = true,
            CanReorderItems = true,
            AllowDrop = true,
            Height = DashboardSections.Default.Count * 50,
            IsItemClickEnabled = false
        };
        AutomationProperties.SetName(rows, Ui.T("dashboard.title"));
        panel.Children.Add(rows);

        void Rebuild()
        {
            rows.Items.Clear();
            var items = _layout.Items;

            for (var i = 0; i < items.Count; i++)
            {
                rows.Items.Add(BuildRow(items[i].Id, items[i].Visible, i == 0, i == items.Count - 1, Rebuild));
            }
        }

        rows.DragItemsCompleted += async (_, _) =>
        {
            var ids = rows.Items.OfType<FrameworkElement>().Select(r => (string)r.Tag).ToList();

            if (!ids.SequenceEqual(_layout.Items.Select(i => i.Id)))
            {
                await SetLayoutAsync(_layout.Reorder(ids));
                DispatcherQueue.TryEnqueue(Rebuild);
            }
        };

        Rebuild();

        var reset = new Button { Content = Ui.T("dashboard.reset"), Style = (Style)Application.Current.Resources["GhostButtonStyle"], HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += async (_, _) =>
        {
            await SetLayoutAsync(DashboardLayout.Default());
            Rebuild();
        };
        panel.Children.Add(reset);
        return panel;
    }

    private UIElement BuildRow(string id, bool visible, bool first, bool last, Action rebuild)
    {
        var name = Ui.T("dashboard.section." + id);
        var row = new Grid { ColumnSpacing = 6, Tag = id };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Tay nắm kéo.
        row.Children.Add(new TextBlock { Text = "⋮⋮", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 });

        var toggle = new ToggleSwitch { Header = null, OnContent = name, OffContent = name, IsOn = visible, MinWidth = 0 };
        AutomationProperties.SetName(toggle, name);
        toggle.Toggled += async (_, _) =>
        {
            if (toggle.IsOn != _layout.IsVisible(id))
            {
                await SetLayoutAsync(_layout.WithVisible(id, toggle.IsOn));
            }
        };
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);

        var up = MoveButton("", name, "dashboard.up", enabled: !first, async () =>
        {
            await SetLayoutAsync(_layout.Move(id, -1));
            rebuild();
        });
        Grid.SetColumn(up, 2);
        row.Children.Add(up);

        var down = MoveButton("", name, "dashboard.down", enabled: !last, async () =>
        {
            await SetLayoutAsync(_layout.Move(id, 1));
            rebuild();
        });
        Grid.SetColumn(down, 3);
        row.Children.Add(down);
        return row;
    }

    private static Button MoveButton(string glyph, string sectionName, string nameKey, bool enabled, Func<Task> click)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(8, 6, 8, 6),
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            IsEnabled = enabled
        };
        AutomationProperties.SetName(button, $"{Ui.T(nameKey)}: {sectionName}");
        button.Click += async (_, _) => await click();
        return button;
    }
}
