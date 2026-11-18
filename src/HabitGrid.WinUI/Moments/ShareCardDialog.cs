using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Moments;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using HabitGrid.WinUI.Controls;
using HabitGrid.WinUI.Plus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace HabitGrid.WinUI.Moments;

/// <summary>
/// Thẻ chia sẻ kiểu photobooth: dải dọc 9:16 gồm ảnh khoảnh khắc, chuỗi ngày, lưới 12 tuần và Buddy, trên khung theo chủ đề.
/// Người dùng lưu PNG (1080×1920) hoặc sao chép rồi tự đăng Story/Locket/Threads — app không đăng nhập tài khoản mạng xã hội nào.
/// </summary>
public static class ShareCardDialog
{
    public const double CardWidth = 360;
    public const double CardHeight = 640;

    /// <summary>Phần tử thẻ đang hiện trong hộp thoại (smoke test dùng để chụp).</summary>
    public static FrameworkElement? CurrentCard { get; private set; }

    /// <summary>Chọn bố cục trong hộp thoại đang mở như khi người dùng bấm chip (smoke test).</summary>
    public static Func<ShareLayout, Task>? SelectLayoutForTest { get; private set; }

    public static async Task ShowAsync(XamlRoot root, Guid habitId)
    {
        var service = App.Host.Get<ShareCardService>();
        var frameId = ShareFrames.Default.Id;
        var layout = ShareLayout.Single;
        var hideWatermark = false;
        var host = new Border { Width = CardWidth, Height = CardHeight, HorizontalAlignment = HorizontalAlignment.Center };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        // Các khung tự xuống dòng thay vì cuộn ngang (lỗi nút khung thứ 5 bị cắt ngoài mép).
        var frames = new HabitGrid.WinUI.Controls.WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        var watermark = new CheckBox { Content = Ui.T("share.watermark.hide"), IsEnabled = service.IsPlus };
        AutomationProperties.SetName(watermark, Ui.T("share.watermark.hide"));

        async Task RenderAsync()
        {
            if (await service.BuildAsync(habitId, frameId, hideWatermark, layout: layout) is { } card)
            {
                var photo = card.PhotoPath is { } path ? await MomentCapture.LoadNowAsync(path, 720) : null;
                var strip = new List<Microsoft.UI.Xaml.Media.Imaging.BitmapImage>();

                foreach (var stripPath in card.StripPhotos ?? [])
                {
                    strip.Add(await MomentCapture.LoadNowAsync(stripPath, 540));
                }

                var element = Build(card, photo, strip);
                host.Child = element;
                CurrentCard = element;
            }
        }

        foreach (var frame in ShareFrames.All)
        {
            var locked = !service.CanUse(frame);
            var chip = new ToggleButton
            {
                Style = (Style)Application.Current.Resources["ChipStyle"],
                Content = $"{frame.Sticker} {Ui.T(frame.NameKey)}{(locked ? " 🔒" : string.Empty)}",
                IsChecked = frame.Id == frameId,
                Padding = new Thickness(12, 6, 12, 6)
            };
            AutomationProperties.SetName(chip, Ui.T(frame.NameKey));
            chip.Click += async (_, _) =>
            {
                if (locked && !await PaywallDialog.ShowAsync(root, PaywallSource.Menu))
                {
                    chip.IsChecked = false;
                    return;
                }

                frameId = frame.Id;

                foreach (var other in frames.Children.OfType<ToggleButton>())
                {
                    other.IsChecked = ReferenceEquals(other, chip);
                }

                await RenderAsync();
            };
            frames.Children.Add(chip);
        }

        watermark.Click += async (_, _) =>
        {
            hideWatermark = watermark.IsChecked == true;
            await RenderAsync();
        };

        // Bố cục: một ảnh (Free) hoặc dải 4 ảnh (Plus; bấm khi chưa có Plus thì mở paywall).
        var layouts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var layoutButtons = new List<(ToggleButton Button, ShareLayout Layout)>();

        foreach (var (value, key) in new[] { (ShareLayout.Single, "share.layout.single"), (ShareLayout.Strip4, "share.layout.strip") })
        {
            var locked = !ShareLayouts.CanUse(value, service.IsPlus);
            var chip = new ToggleButton
            {
                Style = (Style)Application.Current.Resources["ChipStyle"],
                Content = Ui.T(key) + (locked ? " 🔒" : string.Empty),
                IsChecked = value == layout,
                Padding = new Thickness(12, 6, 12, 6)
            };
            AutomationProperties.SetName(chip, Ui.T(key));
            chip.Click += async (_, _) =>
            {
                if (locked && !await PaywallDialog.ShowAsync(root, PaywallSource.Menu))
                {
                    chip.IsChecked = layout == value;
                    return;
                }

                layout = value;
                layoutButtons.ForEach(b => b.Button.IsChecked = b.Layout == layout);
                await RenderAsync();
            };
            layoutButtons.Add((chip, value));
            layouts.Children.Add(chip);
        }

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(layouts);
        panel.Children.Add(frames);
        // Chỉ thu nhỏ phần HIỂN THỊ (80%) để thẻ không bị cắt đáy trong hộp thoại; ảnh lưu vẫn render từ thẻ gốc 360×640 ở 1080×1920.
        panel.Children.Add(new Viewbox { Child = host, Height = CardHeight * 0.8, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(watermark);
        panel.Children.Add(new TextBlock { Text = Ui.T("share.hint"), TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"] });
        panel.Children.Add(status);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Ui.T("share.title"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 760 },
            PrimaryButtonText = Ui.T("share.save"),
            SecondaryButtonText = Ui.T("share.copy"),
            CloseButtonText = Ui.T("plus.close")
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();

            try
            {
                var bytes = await MomentCapture.RenderPngAsync(host.Child!, 1080, 1920);
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "HabitGrid");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"habitgrid-{DateTime.Now:yyyyMMdd-HHmmss}.png");
                await File.WriteAllBytesAsync(path, bytes);
                Show(status, Ui.Loc.Format("share.saved", path));
                await App.Host.Get<GamificationService>().SetFlagAsync("shared_card");
            }
            catch (Exception ex)
            {
                App.Host.Get<IAppLogger>().Error("Lưu thẻ chia sẻ lỗi", ex);
                Show(status, Ui.T("share.failed"));
            }
            finally
            {
                deferral.Complete();
            }
        };

        dialog.SecondaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();

            try
            {
                var bytes = await MomentCapture.RenderPngAsync(host.Child!, 1080, 1920);
                var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
                stream.Seek(0);
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream));
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                Show(status, Ui.T("share.copied"));
                await App.Host.Get<GamificationService>().SetFlagAsync("shared_card");
            }
            catch (Exception ex)
            {
                App.Host.Get<IAppLogger>().Error("Sao chép thẻ chia sẻ lỗi", ex);
                Show(status, Ui.T("share.failed"));
            }
            finally
            {
                deferral.Complete();
            }
        };

        await RenderAsync();
        SelectLayoutForTest = async value =>
        {
            layout = value;
            layoutButtons.ForEach(b => b.Button.IsChecked = b.Layout == layout);
            await RenderAsync();
        };

        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            CurrentCard = null;
            SelectLayoutForTest = null;
        }
    }

    private static void Show(TextBlock status, string text)
    {
        status.Text = text;
        status.Visibility = Visibility.Visible;
    }

    /// <summary>Dựng thẻ 360×640 (logic px); khi lưu được render lại ở 1080×1920.</summary>
    public static FrameworkElement Build(ShareCard card, Microsoft.UI.Xaml.Media.Imaging.BitmapImage? photoImage = null, IReadOnlyList<Microsoft.UI.Xaml.Media.Imaging.BitmapImage>? stripImages = null)
    {
        if (card.Layout == ShareLayout.Strip4 && stripImages is { Count: >= ShareLayouts.StripMinPhotos })
        {
            return BuildStrip(card, stripImages);
        }

        var frame = card.Frame;
        var ink = new SolidColorBrush(Ui.ParseColor(frame.Ink));
        var panelBrush = new SolidColorBrush(Ui.ParseColor(frame.Panel));
        var background = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        background.GradientStops.Add(new GradientStop { Color = Ui.ParseColor(frame.BackgroundStart), Offset = 0 });
        background.GradientStops.Add(new GradientStop { Color = Ui.ParseColor(frame.BackgroundEnd), Offset = 1 });

        var stack = new StackPanel { Spacing = 10, Padding = new Thickness(18, 18, 18, 14) };

        // Tiêu đề: emoji + tên + ngày.
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = Ui.IconText(card.Icon), FontSize = 26, VerticalAlignment = VerticalAlignment.Center });
        var title = new TextBlock { Text = card.HabitName, FontFamily = (FontFamily)Application.Current.Resources["DisplayFont"], FontSize = 20, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        var date = new TextBlock { Text = card.Day.ToString("dd.MM.yyyy", Ui.CurrentCulture), FontSize = 12, Foreground = ink, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(date, 2);
        head.Children.Add(date);
        stack.Children.Add(head);

        // Ô 1: ảnh khoảnh khắc (hoặc emoji lớn nếu chưa có ảnh).
        var photo = new Border { Height = 236, CornerRadius = new CornerRadius(18), Background = panelBrush };
        photo.Child = photoImage is not null
            ? new Image { Source = photoImage, Stretch = Stretch.UniformToFill }
            : new TextBlock { Text = Ui.IconText(card.Icon), FontSize = 96, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(photo);

        // Ô 2 + 3: chuỗi và Buddy.
        var row = new Grid { ColumnSpacing = 10, Height = 112 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var streak = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        streak.Children.Add(new TextBlock { Text = $"🔥 {card.Streak}", FontFamily = (FontFamily)Application.Current.Resources["DisplayFont"], FontSize = 36, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center });
        streak.Children.Add(new TextBlock { Text = Ui.StreakText(card.Streak, card.StreakUnit).Replace(card.Streak.ToString(Ui.CurrentCulture), string.Empty).Trim(), FontSize = 13, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center });
        row.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = panelBrush, Child = streak });

        var buddy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        buddy.Children.Add(new TextBlock { Text = StageEmoji(card.Stage), FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center });
        buddy.Children.Add(new TextBlock { Text = $"{card.BuddyName} · {Ui.Loc.Format("buddy.level", card.Level)}", FontSize = 12, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var buddyBox = new Border { CornerRadius = new CornerRadius(18), Background = panelBrush, Child = buddy };
        Grid.SetColumn(buddyBox, 1);
        row.Children.Add(buddyBox);
        stack.Children.Add(row);

        // Ô 4: lưới 12 tuần + tỷ lệ 30 ngày.
        var grid = new StackPanel { Spacing = 6, Padding = new Thickness(12, 10, 12, 10) };
        grid.Children.Add(new HeatmapGrid { Data = card.Heatmap, ColorHue = card.ColorHue, CellSize = 15, HorizontalAlignment = HorizontalAlignment.Center });

        if (card.Rate30 is { } rate)
        {
            grid.Children.Add(new TextBlock { Text = Ui.Loc.Format("habits.rate30", rate), FontSize = 12, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center });
        }

        stack.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = panelBrush, Child = grid });

        var root = new Grid { Width = CardWidth, Height = CardHeight, Background = background, CornerRadius = new CornerRadius(24) };
        root.Children.Add(stack);
        root.Children.Add(new TextBlock { Text = frame.Sticker, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 10) });

        if (card.ShowWatermark)
        {
            root.Children.Add(new TextBlock { Text = "HabitGrid", FontSize = 11, Foreground = ink, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 12) });
        }

        AutomationProperties.SetName(root, $"{card.HabitName}: {card.Streak}");
        return root;
    }

    /// <summary>Dải photobooth: 4 ô ảnh xếp dọc (ô thiếu để trống), một dòng chuỗi + Buddy, lưới 12 tuần.</summary>
    private static FrameworkElement BuildStrip(ShareCard card, IReadOnlyList<Microsoft.UI.Xaml.Media.Imaging.BitmapImage> images)
    {
        var frame = card.Frame;
        var ink = new SolidColorBrush(Ui.ParseColor(frame.Ink));
        var panelBrush = new SolidColorBrush(Ui.ParseColor(frame.Panel));
        var background = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) };
        background.GradientStops.Add(new GradientStop { Color = Ui.ParseColor(frame.BackgroundStart), Offset = 0 });
        background.GradientStops.Add(new GradientStop { Color = Ui.ParseColor(frame.BackgroundEnd), Offset = 1 });

        var stack = new StackPanel { Spacing = 8, Padding = new Thickness(18, 16, 18, 12) };

        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = $"{Ui.IconText(card.Icon)}  {card.HabitName}", FontFamily = (FontFamily)Application.Current.Resources["DisplayFont"], FontSize = 18, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis });
        var date = new TextBlock { Text = card.Day.ToString("dd.MM.yyyy", Ui.CurrentCulture), FontSize = 12, Foreground = ink, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(date, 1);
        head.Children.Add(date);
        stack.Children.Add(head);

        // 4 ô ảnh: viền trắng kiểu ảnh photobooth.
        for (var i = 0; i < ShareLayouts.StripSlots; i++)
        {
            UIElement content = i < images.Count
                ? new Image { Source = images[i], Stretch = Stretch.UniformToFill }
                : new TextBlock { Text = Ui.IconText(card.Icon), FontSize = 30, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new Border
            {
                Height = 70,
                CornerRadius = new CornerRadius(12),
                Background = panelBrush,
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White),
                BorderThickness = new Thickness(3),
                Child = new Grid { CornerRadius = new CornerRadius(9), Children = { content } }
            });
        }

        var info = new Grid { Height = 48, ColumnSpacing = 8 };
        info.ColumnDefinitions.Add(new ColumnDefinition());
        info.ColumnDefinitions.Add(new ColumnDefinition());
        info.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = panelBrush,
            Child = new TextBlock { Text = $"🔥 {card.Streak}  {Ui.StreakText(card.Streak, card.StreakUnit).Replace(card.Streak.ToString(Ui.CurrentCulture), string.Empty).Trim()}", FontSize = 16, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var buddy = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = panelBrush,
            Child = new TextBlock { Text = $"{StageEmoji(card.Stage)} {card.BuddyName} · {Ui.Loc.Format("buddy.level", card.Level)}", FontSize = 13, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
        };
        Grid.SetColumn(buddy, 1);
        info.Children.Add(buddy);
        stack.Children.Add(info);

        stack.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = panelBrush,
            Padding = new Thickness(8),
            Child = new HeatmapGrid { Data = card.Heatmap, ColorHue = card.ColorHue, CellSize = 15, HorizontalAlignment = HorizontalAlignment.Center }
        });

        var root = new Grid { Width = CardWidth, Height = CardHeight, Background = background, CornerRadius = new CornerRadius(24) };
        root.Children.Add(stack);
        root.Children.Add(new TextBlock { Text = frame.Sticker, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 8) });

        if (card.ShowWatermark)
        {
            root.Children.Add(new TextBlock { Text = "HabitGrid", FontSize = 11, Foreground = ink, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 0, 10) });
        }

        AutomationProperties.SetName(root, $"{card.HabitName}: {card.Streak}");
        return root;
    }

    private static string StageEmoji(BuddyStage stage) => stage switch
    {
        BuddyStage.Egg => "🥚",
        BuddyStage.Baby => "🐣",
        BuddyStage.Teen => "🐥",
        BuddyStage.Adult => "🦉",
        _ => "🐉"
    };
}
