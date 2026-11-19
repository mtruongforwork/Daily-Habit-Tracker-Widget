using System.ComponentModel;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using HabitGrid.WinUI.Controls;
using HabitGrid.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HabitGrid.WinUI.Pages;

public sealed partial class HabitsPage : Page
{
    public HabitsPage()
    {
        ViewModel = App.Host.Get<HabitsViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
        // Không dùng hiệu ứng vào/ra mặc định của GridView: nó làm thẻ chớp mờ khi tải lại danh sách.
        List.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
    }

    public HabitsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        try
        {
            TitleText.Text = Ui.T("habits.title");
            AddButtonText.Text = Ui.T("habits.add");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AddButton, AddButtonText.Text);
            EmptyText.Text = Ui.T("habits.empty");
            UndoButton.Content = Ui.T("today.undo");
            UpdateChips();
            await ViewModel.LoadAsync();
            UpdateEmpty();
            await BuildExtrasAsync();
        }
        catch (Exception ex)
        {
            Log(ex, "Mở trang Thói quen lỗi");
        }
    }

    private readonly RoutineStrip _routines = new();
    private readonly ChallengeStrip _challenges = new();
    private bool _extrasWired;

    /// <summary>Hai khối Bento dưới lưới thẻ: Routine (theo hôm nay) và Thử thách đang mở, mỗi khối có nút Quản lý và lời gợi ý khi trống.</summary>
    private async Task BuildExtrasAsync()
    {
        if (!_extrasWired)
        {
            _extrasWired = true;
            _routines.Changed += async (_, _) => await ReloadExtrasAsync();
            _challenges.Changed += async (_, _) => await ReloadExtrasAsync();
        }

        await ReloadExtrasAsync();
    }

    private async Task ReloadExtrasAsync()
    {
        try
        {
            await _routines.ReloadAsync(DateOnly.FromDateTime(DateTime.Today));
            await _challenges.ReloadAsync();
            Extras.Children.Clear();
            Extras.Visibility = ViewModel.ShowArchived ? Visibility.Collapsed : Visibility.Visible;
            Extras.Add(ExtraCard("🔗", "routine.title", "habits.routines.hint", "habits.routines.empty", _routines, _routines.HasItems, OnRoutineClick), columns: 2);
            Extras.Add(ExtraCard("🏁", "challenge.title", "habits.challenges.hint", "habits.challenges.empty", _challenges, _challenges.HasItems, OnChallengeClick), columns: 2);
        }
        catch (Exception ex)
        {
            Log(ex, "Nạp routine/thử thách lỗi");
        }
    }

    private static Border ExtraCard(string emoji, string titleKey, string hintKey, string emptyKey, FrameworkElement content, bool hasItems, RoutedEventHandler manage)
    {
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = $"{emoji}  {Ui.T(titleKey)}", Style = (Style)Application.Current.Resources["CardTitleTextStyle"] });
        titles.Children.Add(new TextBlock { Text = Ui.T(hintKey), Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });
        head.Children.Add(titles);
        var button = new Button { Content = Ui.T("habits.manage"), Style = (Style)Application.Current.Resources["GhostButtonStyle"], VerticalAlignment = VerticalAlignment.Top };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{Ui.T("habits.manage")}: {Ui.T(titleKey)}");
        button.Click += manage;
        Grid.SetColumn(button, 1);
        head.Children.Add(button);

        if (content.Parent is Panel old)
        {
            old.Children.Remove(content);
        }

        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(head);
        stack.Children.Add(hasItems ? content : new TextBlock { Text = Ui.T(emptyKey), Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });
        return new Border { Style = (Style)Application.Current.Resources["GlassCard"], Child = stack };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HabitsViewModel.IsEmpty) or nameof(HabitsViewModel.ShowArchived))
        {
            UpdateChips();
            UpdateEmpty();
        }
        else if (e.PropertyName == nameof(HabitsViewModel.ArchivedCount))
        {
            UpdateChips();
        }
        else if (e.PropertyName == nameof(HabitsViewModel.LastDeletedId))
        {
            UndoBar.Message = Ui.T("habits.delete");
            UndoBar.IsOpen = ViewModel.LastDeletedId is not null;
        }
    }

    private void UpdateEmpty()
    {
        EmptyPanel.Visibility = ViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        var showMatrix = _matrixView && !ViewModel.IsEmpty && !ViewModel.ShowArchived;
        List.Visibility = ViewModel.IsEmpty || showMatrix ? Visibility.Collapsed : Visibility.Visible;
        MatrixCard.Visibility = showMatrix ? Visibility.Visible : Visibility.Collapsed;
        if (showMatrix)
        {
            _ = RebuildMatrixAsync();
        }
    }

    private bool _matrixView;

    private void OnCardsViewClick(object sender, RoutedEventArgs e)
    {
        _matrixView = false;
        UpdateChips();
        UpdateEmpty();
    }

    private void OnMatrixViewClick(object sender, RoutedEventArgs e)
    {
        _matrixView = true;
        UpdateChips();
        UpdateEmpty();
    }

    internal async Task SetMatrixViewForSmokeAsync(bool enabled)
    {
        _matrixView = enabled;
        UpdateChips();
        List.Visibility = ViewModel.IsEmpty || enabled ? Visibility.Collapsed : Visibility.Visible;
        MatrixCard.Visibility = enabled && !ViewModel.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && !ViewModel.IsEmpty)
        {
            await RebuildMatrixAsync();
        }
    }

    internal int MatrixRowCountForSmoke => MatrixHost.Children.Count;

    private async Task RebuildMatrixAsync()
    {
        try
        {
            MatrixHost.Children.Clear();
            var entitlements = App.Host.Get<IEntitlements>();
            var days = entitlements.IsPlus ? MatrixBoardBuilder.PlusDays : MatrixBoardBuilder.FreeDays;
            var today = DateOnly.FromDateTime(DateTime.Today);
            var habitRepo = App.Host.Get<IHabitRepository>();
            var habits = await habitRepo.GetAllActiveAsync();
            var logRepo = App.Host.Get<IDayLogRepository>();
            var checkIn = new CheckInService(habitRepo, logRepo, App.Host.Get<ISettingsStore>());

            var header = new Grid { ColumnSpacing = 4 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            for (var i = 0; i < days; i++)
            {
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var d = today.AddDays(-(days - 1 - i));
                var lbl = new TextBlock
                {
                    Text = d.Day.ToString(Ui.CurrentCulture),
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources[d == today ? "AccentBrandBrush" : "TextMutedBrush"]
                };
                Grid.SetColumn(lbl, i + 1);
                header.Children.Add(lbl);
            }
            MatrixHost.Children.Add(header);

            foreach (var habit in habits)
            {
                var logs = await logRepo.GetAllForHabitAsync(habit.Id);
                var cells = MatrixBoardBuilder.BuildRow(habit, logs, today, days);
                var row = new Grid { ColumnSpacing = 4 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

                var nameText = new TextBlock
                {
                    Text = $"{Ui.IconText(habit.Icon.Value)}  {habit.Name}",
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                row.Children.Add(nameText);

                var baseColor = Ui.ParseColor(habit.ColorHue);
                var emptyBrush = (Brush)Application.Current.Resources["HeatEmptyBrush"];

                for (var i = 0; i < cells.Count; i++)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    var cell = cells[i];
                    var alpha = cell.Intensity > 0 ? (byte)Math.Clamp((int)Math.Round(cell.Intensity * 255), 50, 255) : (byte)0;
                    var bg = alpha > 0
                        ? new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B))
                        : emptyBrush;
                    var btn = new Button
                    {
                        Height = 26,
                        MinWidth = 0,
                        Padding = new Thickness(0),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        CornerRadius = new CornerRadius(6),
                        Background = bg,
                        BorderThickness = new Thickness(cell.Day == today ? 1 : 0),
                        BorderBrush = (Brush)Application.Current.Resources["StrokeHighlightBrush"]
                    };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, $"{habit.Name} {cell.Day:dd/MM}");
                    var targetDay = cell.Day;
                    var habitId = habit.Id;
                    btn.Click += async (_, _) =>
                    {
                        await checkIn.ToggleAsync(habitId, targetDay);
                        await ViewModel.LoadAsync();
                        await RebuildMatrixAsync();
                    };
                    Grid.SetColumn(btn, i + 1);
                    row.Children.Add(btn);
                }

                MatrixHost.Children.Add(row);
            }
        }
        catch (Exception ex)
        {
            Log(ex, "Dựng bảng ma trận lỗi");
        }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await HabitEditorDialog.ShowAsync(XamlRoot) is not null)
            {
                await ViewModel.LoadAsync();
            }
        }
        catch (Exception ex)
        {
            Log(ex, "Thêm thói quen lỗi");
        }
    }

    private async void OnChallengeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ChallengeDialog.ShowAsync(XamlRoot);
            await ViewModel.LoadAsync();
            await ReloadExtrasAsync();
        }
        catch (Exception ex)
        {
            Log(ex, "Thử thách lỗi");
        }
    }

    private async void OnRoutineClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await RoutineManagerDialog.ShowAsync(XamlRoot);
            await ViewModel.LoadAsync();
            await ReloadExtrasAsync();
        }
        catch (Exception ex)
        {
            Log(ex, "Quản lý routine lỗi");
        }
    }

    private void UpdateChips()
    {
        ActiveChip.Content = Ui.T("habits.filter.active");
        CardsViewChip.Content = Ui.T("habits.view.cards");
        MatrixViewChip.Content = Ui.T("habits.view.matrix");
        CardsViewChip.IsChecked = !_matrixView;
        MatrixViewChip.IsChecked = _matrixView;
        ArchivedChip.Content = ViewModel.ArchivedCount > 0
            ? $"{Ui.T("habits.filter.archived")} · {ViewModel.ArchivedCount}"
            : Ui.T("habits.filter.archived");
        ActiveChip.IsChecked = !ViewModel.ShowArchived;
        ArchivedChip.IsChecked = ViewModel.ShowArchived;
        EmptyText.Text = Ui.T(ViewModel.ShowArchived ? "habits.empty.archived" : "habits.empty");
        List.CanReorderItems = !ViewModel.ShowArchived;
        AddButton.Visibility = ViewModel.ShowArchived ? Visibility.Collapsed : Visibility.Visible;
        Extras.Visibility = ViewModel.ShowArchived ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnActiveChipClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowArchived = false;
        UpdateChips();
    }

    private void OnArchivedChipClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ShowArchived = true;
        UpdateChips();
    }

    private async void OnCardAction(object? sender, CardActionEventArgs e)
    {
        try
        {
            switch (e.Action)
            {
                case CardAction.Open:
                    Frame.Navigate(typeof(HabitDetailPage), e.Item.Id);
                    break;
                case CardAction.Edit:
                    if (await HabitEditorDialog.ShowAsync(XamlRoot, e.Item.Habit) is not null)
                    {
                        await ViewModel.LoadAsync();
                    }

                    break;
                case CardAction.MoveUp:
                    await ViewModel.MoveByAsync(e.Item, -1);
                    break;
                case CardAction.MoveDown:
                    await ViewModel.MoveByAsync(e.Item, 1);
                    break;
                case CardAction.Archive:
                    await ViewModel.ArchiveAsync(e.Item);
                    break;
                case CardAction.Unarchive:
                    await ViewModel.UnarchiveAsync(e.Item);
                    break;
                case CardAction.Delete:
                    await ViewModel.DeleteAsync(e.Item);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log(ex, "Thao tác thẻ thói quen lỗi");
        }
    }

    private async void OnUndoDeleteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.UndoDeleteAsync();
            UndoBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            Log(ex, "Hoàn tác xóa lỗi");
        }
    }

    /// <summary>Kéo-thả xong: danh sách đã được ListView đổi chỗ; ghi vị trí mới (một dòng nhờ fractional index).</summary>
    private async void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        try
        {
            if (args.Items.FirstOrDefault() is HabitListItem item)
            {
                var index = ViewModel.Items.IndexOf(item);
                if (index >= 0)
                {
                    await ViewModel.MoveAsync(item, index);
                }
            }
        }
        catch (Exception ex)
        {
            Log(ex, "Đổi vị trí lỗi");
        }
    }

    private static void Log(Exception ex, string message) =>
        App.Host.Get<IAppLogger>().Error(message, ex);
}
