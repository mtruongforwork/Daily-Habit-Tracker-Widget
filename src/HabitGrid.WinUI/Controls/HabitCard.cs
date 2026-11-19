using System.ComponentModel;
using System.Numerics;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using HabitGrid.WinUI.Pages;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace HabitGrid.WinUI.Controls;

/// <summary>
/// Thẻ thói quen dạng bento (03-UI-REBUILD-GLOW2 §5.2). Cả thẻ là vùng bấm (bấm = check-in), chuột phải mở menu.
/// Chưa xong: nền tối + viền gradient + emoji lớn. Xong: cả thẻ đổ gradient của chính thói quen với hiệu ứng
/// "liquid fill" lan từ điểm chạm. Đếm/thời lượng có thanh tiến độ ngay trong thẻ.
/// </summary>
public sealed class HabitCard : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(HabitTodayItem), typeof(HabitCard), new PropertyMetadata(null, OnItemChanged));

    private const double Radius = 28;

    private readonly Grid _root = new() { MinHeight = 176, CornerRadius = new CornerRadius(Radius) };
    private readonly Border _idle = new() { CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(1.5) };
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(Radius), Opacity = 0 };
    private readonly Border _scrim = new() { CornerRadius = new CornerRadius(Radius), Opacity = 0, Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
    private readonly TextBlock _emoji = new() { FontSize = 38, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _name = new();
    private readonly TextBlock _sub = new();
    private readonly Border _badge = new() { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _badgeText = new() { FontFamily = (FontFamily)Application.Current.Resources["DisplayBoldFont"], FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
    private readonly Grid _bar = new() { Height = 8, CornerRadius = new CornerRadius(4), Visibility = Visibility.Collapsed };
    private readonly Border _barFill = new() { CornerRadius = new CornerRadius(4) };
    private readonly MiniWeek _week = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _inlineActions = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _minusBtn = new() { Content = "−1", Padding = new Thickness(8, 3, 8, 3), MinWidth = 0, MinHeight = 0, CornerRadius = new CornerRadius(10), FontSize = 12 };
    private readonly Button _plusBtn = new() { Content = "+1", Padding = new Thickness(8, 3, 8, 3), MinWidth = 0, MinHeight = 0, CornerRadius = new CornerRadius(10), FontSize = 12 };
    private readonly Button _focusBtn = new() { Padding = new Thickness(10, 3, 10, 3), MinWidth = 0, MinHeight = 0, CornerRadius = new CornerRadius(10), FontSize = 12 };
    private readonly Border _checkPill = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1.5), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _checkGlyph = new() { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private HabitTodayItem? _item;
    private Windows.Foundation.Point _lastPointer;
    private bool _wasDone;
    private WeakRelay? _relay;

    public HabitCard()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Padding = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

        _name.Style = (Style)Application.Current.Resources["CardTitleTextStyle"];
        _sub.Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"];
        _sub.TextTrimming = TextTrimming.CharacterEllipsis;
        _badge.Child = _badgeText;

        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _bar.Children.Add(_barFill);

        var content = new Grid
        {
            Padding = new Thickness(20, 18, 20, 18),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var top = new Grid();
        top.Children.Add(_emoji);
        top.Children.Add(_badge);

        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 3, Margin = new Thickness(0, 8, 0, 10) };
        middle.Children.Add(_name);
        middle.Children.Add(_sub);

        var bottom = new StackPanel { Spacing = 10 };
        bottom.Children.Add(_bar);
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_week);

        _checkPill.Child = _checkGlyph;
        _focusBtn.Content = Ui.T("today.inline.focus");
        _inlineActions.Children.Add(_minusBtn);
        _inlineActions.Children.Add(_plusBtn);
        _inlineActions.Children.Add(_focusBtn);
        _inlineActions.Children.Add(_checkPill);
        Grid.SetColumn(_inlineActions, 1);
        footer.Children.Add(_inlineActions);
        bottom.Children.Add(footer);

        WireInlineActions();
        Motion.AttachGlassHover(this);

        content.Children.Add(top);
        Grid.SetRow(middle, 1);
        content.Children.Add(middle);
        Grid.SetRow(bottom, 2);
        content.Children.Add(bottom);

        _root.Children.Add(_idle);
        _root.Children.Add(_fill);
        _root.Children.Add(_scrim);
        _root.Children.Add(content);
        Content = _root;

        PointerPressed += (_, e) => _lastPointer = e.GetCurrentPoint(_root).Position;
        Tapped += async (_, _) => await ActivateAsync();
        KeyDown += async (_, e) =>
        {
            if (e.Key is VirtualKey.Space or VirtualKey.Enter)
            {
                _lastPointer = new Windows.Foundation.Point(ActualWidth / 2, ActualHeight / 2);
                await ActivateAsync();
                e.Handled = true;
            }
        };

        ContextFlyout = new MenuFlyout();
        ((MenuFlyout)ContextFlyout).Opening += (_, _) => BuildMenu((MenuFlyout)ContextFlyout);
        // Hiển thị lại (trang cache, phần tử tái sử dụng): vẽ lại theo dữ liệu hiện tại, không chạy hiệu ứng.
        Loaded += (_, _) =>
        {
            _wasDone = _item?.IsDone ?? false;
            Refresh(animate: false);
        };
    }

    public HabitTodayItem? Item
    {
        get => (HabitTodayItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>Thẻ đang hiển thị trạng thái "xong" (nền gradient đã phủ). Dùng để smoke test đối chiếu giao diện với ViewModel.</summary>
    public bool IsShowingDone => _fill.Opacity > 0.5;

    /// <summary>Thông tin chẩn đoán cho smoke test.</summary>
    public string Diagnostics => $"relay={_relay is not null} loaded={IsLoaded} itemHash={_item?.GetHashCode()} value={_item?.Value} status={_item?.Status} fill={_fill.Opacity} sub='{_sub.Text}'";

    /// <summary>Dòng phụ đang hiển thị trên thẻ.</summary>
    public string DisplayedSubText => _sub.Text;

    /// <summary>Kích hoạt như người dùng bấm (smoke test, trợ năng).</summary>
    public Task PerformClickAsync()
    {
        _lastPointer = new Windows.Foundation.Point(ActualWidth / 2, ActualHeight / 2);
        return ActivateAsync();
    }

    // ------------------------------------------------------------------ gắn dữ liệu

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HabitCard)d).Bind(e.NewValue as HabitTodayItem);

    private void Bind(HabitTodayItem? item)
    {
        Detach();
        _item = item;

        if (item is null)
        {
            return;
        }

        _wasDone = item.IsDone;

        // Đăng ký CỐ ĐỊNH (không phụ thuộc Loaded/Unloaded: ItemsRepeater tái sử dụng phần tử nên thứ tự các sự kiện đó
        // không đáng tin) qua một lớp trung gian chỉ giữ tham chiếu yếu tới thẻ, nên thẻ bị thu gom thì tự gỡ đăng ký.
        _relay = new WeakRelay(this, item);
        item.PropertyChanged += _relay.OnChanged;
        Refresh(animate: false);
    }

    private void Detach()
    {
        if (_relay is not null)
        {
            _relay.Item.PropertyChanged -= _relay.OnChanged;
            _relay = null;
        }
    }

    private sealed class WeakRelay
    {
        private readonly WeakReference<HabitCard> _card;

        public WeakRelay(HabitCard card, HabitTodayItem item)
        {
            _card = new WeakReference<HabitCard>(card);
            Item = item;
        }

        public HabitTodayItem Item { get; }

        public void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_card.TryGetTarget(out var card))
            {
                card.OnItemPropertyChanged(sender, e);
            }
            else
            {
                Item.PropertyChanged -= OnChanged;
            }
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DispatcherQueue is { } queue && !queue.HasThreadAccess)
        {
            queue.TryEnqueue(() => Refresh(animate: true));
        }
        else
        {
            Refresh(animate: true);
        }
    }

    // ------------------------------------------------------------------ hiển thị

    private void Refresh(bool animate)
    {
        if (_item is not { } item)
        {
            return;
        }

        var stops = HabitGradient.From(item.ColorHue);
        var gradient = Ui.GradientBrush(stops);
        var done = item.IsDone;
        var skipped = item.IsSkipped;

        _emoji.Text = Ui.IconText(item.Icon);
        _name.Text = item.Name;
        _sub.Text = Ui.SubText(item.Kind, item.Value, item.Target, item.Unit, item.Streak, item.StreakUnit, item.AtRisk);

        _idle.Background = (Brush)Application.Current.Resources["CardIdleBrush"];
        _idle.BorderBrush = Ui.GradientBrush(stops, 0.55);
        _fill.Background = gradient;
        _scrim.Opacity = done ? stops.ScrimOpacity : 0;
        Opacity = item.IsReadOnly ? 0.5 : skipped ? 0.6 : 1;

        var textBrush = done ? (Brush)Application.Current.Resources["OnGradientBrush"] : (Brush)Application.Current.Resources["TextPrimaryBrush"];
        var subBrush = done ? new SolidColorBrush(Windows.UI.Color.FromArgb(220, 255, 255, 255)) : (Brush)Application.Current.Resources["TextSecondaryBrush"];
        _name.Foreground = textBrush;
        _sub.Foreground = subBrush;

        // Huy hiệu góc phải: ✓ khi xong, ⏭ khi bỏ qua, 🔥 streak khi đang chờ.
        if (done)
        {
            Badge("✓  " + Ui.T("today.state.done"), new SolidColorBrush(Windows.UI.Color.FromArgb(70, 255, 255, 255)), Microsoft.UI.Colors.White);
        }
        else if (item.IsReadOnly)
        {
            Badge(Ui.T("today.state.readonly"), (Brush)Application.Current.Resources["Surface2Brush"], Ui.ParseColor("#B4B4C6"));
        }
        else if (skipped)
        {
            Badge(Ui.T("today.state.skipped"), (Brush)Application.Current.Resources["Surface2Brush"], Ui.ParseColor("#B4B4C6"));
        }
        else if (item.Streak > 0 && item.Kind != HabitKind.Quit)
        {
            Badge($"🔥 {item.Streak}", (Brush)Application.Current.Resources["Surface2Brush"], Ui.ParseColor("#FFB020"));
        }
        else
        {
            _badge.Visibility = Visibility.Collapsed;
        }

        // Thanh tiến độ cho loại số.
        var numeric = item.Kind is HabitKind.Count or HabitKind.Duration;
        _bar.Visibility = numeric ? Visibility.Visible : Visibility.Collapsed;

        if (numeric)
        {
            var progress = Math.Clamp(item.Progress, 0, 1);
            _bar.Background = new SolidColorBrush(done ? Windows.UI.Color.FromArgb(70, 255, 255, 255) : Windows.UI.Color.FromArgb(40, 255, 255, 255));
            _barFill.Background = done ? new SolidColorBrush(Microsoft.UI.Colors.White) : gradient;
            _bar.ColumnDefinitions[0].Width = new GridLength(progress, GridUnitType.Star);
            _bar.ColumnDefinitions[1].Width = new GridLength(1 - progress, GridUnitType.Star);
        }

        _week.ColorHue = done ? "#FFFFFF" : item.ColorHue;
        _week.Statuses = item.LastSevenDays;

        // Inline Context Actions theo loại thói quen
        var isCountLike = item.Kind is HabitKind.Count or HabitKind.Dual;
        var isDuration = item.Kind == HabitKind.Duration;
        var isCheckbox = !isCountLike && !isDuration;

        _minusBtn.Visibility = isCountLike && !item.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        _minusBtn.IsEnabled = item.Value > 0;
        _plusBtn.Visibility = isCountLike && !item.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        _focusBtn.Visibility = isDuration && !item.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        _checkPill.Visibility = isCheckbox ? Visibility.Visible : Visibility.Collapsed;
        _checkPill.Background = done
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(80, 255, 255, 255))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(24, 255, 255, 255));
        _checkPill.BorderBrush = done
            ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : Ui.GradientBrush(stops, 0.7);
        _checkGlyph.Text = done ? "✓" : "";
        _checkGlyph.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);

        AutomationProperties.SetName(
            this,
            $"{item.Name}, {(done ? Ui.T("today.state.done") : skipped ? Ui.T("today.state.skipped") : Ui.SubText(item.Kind, item.Value, item.Target, item.Unit, item.Streak, item.StreakUnit, false))}");

        var becameDone = done && !_wasDone;
        _wasDone = done;

        if (becameDone && animate && AnimationsEnabled)
        {
            LiquidFill();
            Motion.SpringPop(_checkPill, 0.72f);
        }
        else
        {
            ResetFill(done);
        }
    }

    private void WireInlineActions()
    {
        void SwallowTap(UIElement el)
        {
            el.PointerPressed += (_, e) => e.Handled = true;
            el.Tapped += (_, e) => e.Handled = true;
        }

        SwallowTap(_minusBtn);
        SwallowTap(_plusBtn);
        SwallowTap(_focusBtn);

        _minusBtn.Click += async (_, _) => await Guard(async () =>
        {
            if (_item is { } item && item.Value > 0)
            {
                Motion.SpringPop(_minusBtn, 0.85f);
                await App.Host.Get<TodayViewModel>().DecrementAsync(item);
            }
        });

        _plusBtn.Click += async (_, _) => await Guard(async () =>
        {
            if (_item is { } item)
            {
                Motion.SpringPop(_plusBtn, 0.85f);
                Motion.SpringPop(_bar, 0.96f);
                _ = AppSound.PlayAsync(SoundToneKind.Increment);
                await App.Host.Get<TodayViewModel>().CheckInAsync(item);
            }
        });

        _focusBtn.Click += async (_, _) => await Guard(async () =>
        {
            if (_item is { } item)
            {
                Motion.SpringPop(_focusBtn, 0.88f);
                var minutes = Math.Clamp(item.Target > 0 ? item.Target : 25, 5, 60);
                var model = App.Host.Get<FocusPageModel>();
                await model.LoadAsync();
                await model.StartAsync(new FocusChoice(HabitGrid.Core.Domain.Focus.FocusMode.Pomodoro, minutes, 5, item.Id), DateTime.UtcNow);
                _ = AppSound.PlayAsync(SoundToneKind.Increment);
            }
        });
    }

    private void Badge(string text, Brush background, Windows.UI.Color foreground)
    {
        _badge.Visibility = Visibility.Visible;
        _badge.Background = background;
        _badgeText.Text = text;
        _badgeText.Foreground = new SolidColorBrush(foreground);
    }

    private static bool AnimationsEnabled => App.Host.Get<ThemeService>().AnimationsEnabled;

    // ------------------------------------------------------------------ hiệu ứng

    private void ResetFill(bool done)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_fill);
        visual.Clip = null;
        _fill.Opacity = done ? 1 : 0;
    }

    /// <summary>Hình tròn gradient lan từ điểm chạm ra phủ kín thẻ (≈ 420 ms).</summary>
    private void LiquidFill()
    {
        var visual = ElementCompositionPreview.GetElementVisual(_fill);
        var compositor = visual.Compositor;

        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);
        var origin = new Vector2((float)_lastPointer.X, (float)_lastPointer.Y);

        // Bán kính đủ phủ góc xa nhất.
        var far = Math.Max(
            Math.Max(Distance(origin, 0, 0), Distance(origin, (float)width, 0)),
            Math.Max(Distance(origin, 0, (float)height), Distance(origin, (float)width, (float)height)));

        var ellipse = compositor.CreateEllipseGeometry();
        ellipse.Center = origin;
        ellipse.Radius = Vector2.Zero;
        visual.Clip = compositor.CreateGeometricClip(ellipse);
        _fill.Opacity = 1;

        var animation = compositor.CreateVector2KeyFrameAnimation();
        animation.InsertKeyFrame(1f, new Vector2(far, far), compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.8f), new Vector2(0.2f, 1f)));
        animation.Duration = TimeSpan.FromMilliseconds(420);

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        ellipse.StartAnimation("Radius", animation);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() => visual.Clip = null);

        Bounce();
    }

    private static float Distance(Vector2 origin, float x, float y) => Vector2.Distance(origin, new Vector2(x, y));

    /// <summary>Thẻ co nhẹ rồi bật lại bằng spring.</summary>
    private void Bounce()
    {
        var visual = ElementCompositionPreview.GetElementVisual(this);
        visual.CenterPoint = new Vector3((float)(ActualWidth / 2), (float)(ActualHeight / 2), 0);

        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.InitialValue = new Vector3(0.95f, 0.95f, 1f);
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = 0.5f;
        spring.Period = TimeSpan.FromMilliseconds(50);
        visual.StartAnimation("Scale", spring);
    }

    // ------------------------------------------------------------------ hành động

    private async Task ActivateAsync()
    {
        if (_item is not { } item)
        {
            return;
        }

        try
        {
            // Bấm một thẻ chưa xong cũng cho cảm giác "nhấn" ngay cả khi tắt hiệu ứng xong/hủy.
            if (!item.IsDone && !item.IsReadOnly && AnimationsEnabled)
            {
                Bounce();
                Motion.Ripple(_root, _lastPointer, Microsoft.UI.Colors.White);
            }

            if (!item.IsDone && !item.IsReadOnly)
            {
                _ = AppSound.PlayAsync(item.Kind is HabitKind.Count or HabitKind.Dual
                    ? SoundToneKind.Increment
                    : SoundToneKind.CheckInPop);
            }

            await App.Host.Get<TodayViewModel>().CheckInAsync(item);
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Check-in từ thẻ lỗi", ex);
        }
    }

    private void BuildMenu(MenuFlyout flyout)
    {
        flyout.Items.Clear();

        if (_item is not { } item)
        {
            return;
        }

        var vm = App.Host.Get<TodayViewModel>();

        var edit = new MenuFlyoutItem { Text = Ui.T("habits.edit"), Icon = new FontIcon { Glyph = "" } };
        edit.Click += async (_, _) => await Guard(async () =>
        {
            if (await HabitEditorDialog.ShowAsync(XamlRoot, item.Habit) is not null)
            {
                await vm.LoadAsync();
            }
        });
        flyout.Items.Add(edit);

        if (item.Kind is HabitKind.Count or HabitKind.Duration or HabitKind.Quit && item.Value > 0)
        {
            var minus = new MenuFlyoutItem { Text = Ui.T("today.menu.minus"), Icon = new FontIcon { Glyph = "" } };
            minus.Click += async (_, _) => await Guard(() => vm.DecrementAsync(item));
            flyout.Items.Add(minus);
        }

        var skip = new MenuFlyoutItem
        {
            Text = Ui.T(item.Status == DayStatus.Skipped ? "today.menu.unskip" : "today.menu.skip"),
            Icon = new FontIcon { Glyph = "" }
        };
        skip.Click += async (_, _) => await Guard(() => vm.SkipAsync(item));
        flyout.Items.Add(skip);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var archive = new MenuFlyoutItem { Text = Ui.T("habits.archive"), Icon = new FontIcon { Glyph = "" } };
        archive.Click += async (_, _) => await Guard(async () =>
        {
            await App.Host.Get<IHabitRepository>().ArchiveAsync(item.Id);
            await vm.LoadAsync();
        });
        flyout.Items.Add(archive);
    }

    private static async Task Guard(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Hành động trên thẻ lỗi", ex);
        }
    }
}
