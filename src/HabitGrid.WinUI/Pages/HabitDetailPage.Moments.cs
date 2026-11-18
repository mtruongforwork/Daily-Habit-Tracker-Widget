using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using HabitGrid.WinUI.Moments;
using HabitGrid.WinUI.Plus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HabitGrid.WinUI.Pages;

/// <summary>Khu "Khoảnh khắc" trên trang chi tiết: chụp/chọn ảnh cho hôm nay, xem ảnh cũ, tạo thẻ chia sẻ photobooth.</summary>
public sealed partial class HabitDetailPage
{
    private const int ThumbWidth = 132;

    /// <summary>Thêm ảnh cho hôm nay từ mảng byte (smoke test dùng; giao diện đi qua camera/file).</summary>
    public MomentAddResult AddMomentForTest(byte[] jpeg, int daysAgo = 0)
    {
        var result = ViewModel.Habit is { } habit
            ? App.Host.Get<MomentService>().Add(habit, ViewModel.Today.AddDays(-daysAgo), jpeg, ".jpg").Result
            : MomentAddResult.NotAllowed;
        BuildMoments();
        return result;
    }

    public Task OpenShareForTestAsync() => ViewModel.Habit is { } habit ? ShareCardDialog.ShowAsync(XamlRoot, habit.Id) : Task.CompletedTask;

    private void BuildMoments()
    {
        MomentsHost.Children.Clear();

        if (ViewModel.Habit is not { } habit)
        {
            return;
        }

        var service = App.Host.Get<MomentService>();
        var moments = service.Store.ForHabit(habit.Id);

        // v3: khối hẹp 1 cột, nên tiêu đề ở trên, các nút tự xuống dòng bên dưới.
        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(new TextBlock { Text = Ui.T("moments.title").ToUpperInvariant(), Style = (Style)Application.Current.Resources["SectionHeader"], Margin = new Thickness(0) });

        var buttons = new HabitGrid.WinUI.Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        var capture = Action("📷 " + Ui.T("moments.capture"), Ui.T("moments.capture"), async () => await AddFromAsync(MomentCapture.CaptureAsync));
        var pick = Action("🖼 " + Ui.T("moments.pick"), Ui.T("moments.pick"), async () => await AddFromAsync(MomentCapture.PickAsync));
        var share = Action("🎞 " + Ui.T("moments.share"), Ui.T("moments.share"), async () => await ShareCardDialog.ShowAsync(XamlRoot, habit.Id));
        buttons.Children.Add(capture);
        buttons.Children.Add(pick);
        buttons.Children.Add(share);
        header.Children.Add(buttons);

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(header);

        if (!service.CanAdd(habit.Id, ViewModel.Today))
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("moments.limit"), Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });
        }

        if (moments.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("moments.empty"), Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            // Khung ảnh kiểu polaroid kính mờ: 2 ảnh mới nhất cạnh nhau (giữ khối thấp ngang heatmap; bấm để mở ảnh, chuột phải để xóa).
            var strip = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            strip.ColumnDefinitions.Add(new ColumnDefinition());
            strip.ColumnDefinitions.Add(new ColumnDefinition());
            var shown = moments.Take(2).ToList();

            for (var i = 0; i < shown.Count; i++)
            {
                strip.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            foreach (var (moment, index) in shown.Select((m, i) => (m, i)))
            {
                var image = new Image { Source = MomentCapture.Thumbnail(moment.Path, ThumbWidth), Height = 104, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                var label = Ui.LongDate(moment.Day);
                var polaroid = new StackPanel { Spacing = 6 };
                polaroid.Children.Add(new Border { CornerRadius = new CornerRadius(8), Child = image });
                polaroid.Children.Add(new TextBlock { Text = moment.Day.ToString("dd/MM", Ui.CurrentCulture), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"] });
                var tile = new Button
                {
                    Padding = new Thickness(6, 6, 6, 4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(12),
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["GlassStrokeBrush"],
                    BorderThickness = new Thickness(1),
                    Content = polaroid
                };
                Grid.SetColumn(tile, index % 2);
                Grid.SetRow(tile, index / 2);
                AutomationProperties.SetName(tile, Ui.Loc.Format("moments.photo", label));
                ToolTipService.SetToolTip(tile, label);
                tile.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(moment.Path) { UseShellExecute = true });

                var delete = new MenuFlyoutItem { Text = Ui.T("moments.delete"), Icon = new FontIcon { Glyph = "" } };
                delete.Click += (_, _) =>
                {
                    service.Store.Delete(moment);
                    BuildMoments();
                };
                tile.ContextFlyout = new MenuFlyout { Items = { delete } };
                strip.Children.Add(tile);
            }

            stack.Children.Add(strip);

            if (moments.Count > shown.Count)
            {
                stack.Children.Add(new TextBlock { Text = Ui.Loc.Format("moments.more", moments.Count - shown.Count), Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"] });
            }
        }

        MomentsHost.Children.Add(new Border { Style = (Style)Application.Current.Resources["GlassCard"], Child = stack });
    }

    private static Button Action(string text, string name, Func<Task> run)
    {
        var button = new Button { Content = text, Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
        AutomationProperties.SetName(button, name);
        button.Click += async (_, _) =>
        {
            try
            {
                await run();
            }
            catch (Exception ex)
            {
                App.Host.Get<IAppLogger>().Error($"Khoảnh khắc: {name} lỗi", ex);
            }
        };
        return button;
    }

    /// <summary>Lấy ảnh (camera hoặc file), thu nhỏ, lưu theo quyền. Hết lượt Free thì mở paywall vì người dùng vừa bấm thêm ảnh.</summary>
    private async Task AddFromAsync(Func<Task<Windows.Storage.StorageFile?>> source)
    {
        if (ViewModel.Habit is not { } habit)
        {
            return;
        }

        var service = App.Host.Get<MomentService>();

        if (!service.CanAdd(habit.Id, ViewModel.Today) && !await PaywallDialog.ShowAsync(XamlRoot, PaywallSource.Menu))
        {
            return;
        }

        if (await source() is not { } file)
        {
            return;
        }

        var bytes = await MomentCapture.NormalizeAsync(file);
        var (result, _) = service.Add(habit, ViewModel.Today, bytes, ".jpg");

        if (result != MomentAddResult.Added)
        {
            await new ContentDialog { XamlRoot = XamlRoot, Content = Ui.T("moments.failed." + result), CloseButtonText = Ui.T("celebrate.close") }.ShowAsync();
        }

        BuildMoments();
    }
}
