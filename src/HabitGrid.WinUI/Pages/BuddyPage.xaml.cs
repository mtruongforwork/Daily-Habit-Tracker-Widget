using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Gamification;
using HabitGrid.WinUI.Plus;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HabitGrid.WinUI.Pages;

/// <summary>Trang Buddy: linh vật tiến hóa theo level, thanh XP, token freeze và lưới huy hiệu (có huy hiệu bí mật ẩn điều kiện).</summary>
public sealed partial class BuddyPage : Page
{
    /// <summary>Bề rộng từ đó dùng bố cục "phòng thay đồ" 2 nửa (sân khấu cố định bên trái).</summary>
    private const double SplitFrom = 1000;

    private readonly StackPanel _root = new() { Spacing = 16, Padding = new Thickness(32, 24, 32, 32) };
    private AccessorySlot _slot = AccessorySlot.Head;
    private bool? _split;
    private int _coins;
    private IReadOnlyList<CustomReward> _rewards = [];

    public BuddyPage()
    {
        ViewModel = App.Host.Get<BuddyViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        Scroll.Content = _root;

        // Đổi giữa 2 nửa và 1 cột khi qua mốc bề rộng.
        SizeChanged += (_, e) =>
        {
            if (_split is { } split && split != (e.NewSize.Width >= SplitFrom))
            {
                Build();
            }
        };
    }

    public BuddyViewModel ViewModel { get; }

    /// <summary>Hoàn tất khi dữ liệu đã nạp và giao diện đã dựng (smoke test chờ tín hiệu này).</summary>
    public Task Ready { get; private set; } = Task.CompletedTask;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Ready = LoadAsync();
    }

    private Wardrobe _wardrobe = new([], new Dictionary<AccessorySlot, BuddyAccessory>());

    private async Task LoadAsync()
    {
        try
        {
            await ViewModel.LoadAsync();
            _wardrobe = await App.Host.Get<BuddyWardrobeService>().GetAsync();
            var rewardService = App.Host.Get<RewardService>();
            _coins = await rewardService.GetBalanceAsync();
            _rewards = await rewardService.GetRewardsAsync();
            Build();
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Mở trang Buddy lỗi", ex);
        }
    }

    /// <summary>
    /// Bố cục "phòng thay đồ tương tác" (v3): cửa sổ rộng thì sân khấu Mochi cố định bên trái (1.5 phần), bên phải (2.5 phần) cuộn riêng
    /// gồm tủ đồ theo tab Đầu / Mặt / Cổ và lưới huy hiệu, nên đổi đồ vẫn luôn thấy Mochi. Cửa sổ hẹp thì xếp một cột như cũ.
    /// </summary>
    private void Build()
    {
        _root.Children.Clear();
        var split = ActualWidth == 0 || ActualWidth >= SplitFrom;
        _split = split;
        Scroll.VerticalScrollMode = split ? ScrollMode.Disabled : ScrollMode.Enabled;
        Scroll.VerticalScrollBarVisibility = split ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        _root.Children.Add(new TextBlock { Text = Ui.T("buddy.title"), Style = Res("TitleDisplayTextStyle") });

        var right = new StackPanel { Spacing = 16 };
        right.Children.Add(BuildWardrobe());
        right.Children.Add(BuildRewardsCard());
        right.Children.Add(BuildBadges());

        if (!split)
        {
            _root.Children.Add(BuildHero(vertical: false));
            _root.Children.Add(right);
            return;
        }

        var body = new Grid { ColumnSpacing = 20 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
        body.Children.Add(BuildHero(vertical: true));
        var scroller = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(scroller, 1);
        body.Children.Add(scroller);

        // Hai nửa cao bằng phần còn lại của cửa sổ (trừ tiêu đề): sân khấu đứng yên, nửa phải cuộn.
        body.Height = Math.Max(420, ActualHeight - 120);
        _root.Children.Add(body);
    }

    private FrameworkElement BuildRewardsCard()
    {
        var stack = new StackPanel { Spacing = 12 };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = Ui.T("rewards.title").ToUpperInvariant(), Style = Res("SectionHeader"), Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center });
        var coinText = new TextBlock { Text = "🪙 " + Ui.Loc.Format("rewards.coins", _coins), Style = Res("BodySecondaryTextStyle"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(coinText, 1);
        header.Children.Add(coinText);
        stack.Children.Add(header);

        var status = new TextBlock { Style = Res("BodySecondaryTextStyle"), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };

        var addRow = new Grid { ColumnSpacing = 8 };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleBox = new TextBox { PlaceholderText = Ui.T("rewards.add"), MaxLength = 60 };
        var costBox = new NumberBox { Value = 30, Minimum = RewardWallet.MinCostCoins, Maximum = RewardWallet.MaxCostCoins, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var addBtn = new Button { Content = "＋", Style = Res("GradientButtonStyle"), Padding = new Thickness(12, 8, 12, 8) };
        AutomationProperties.SetName(costBox, Ui.T("rewards.title"));
        AutomationProperties.SetName(addBtn, Ui.T("rewards.add"));
        Grid.SetColumn(costBox, 1);
        Grid.SetColumn(addBtn, 2);
        addRow.Children.Add(titleBox);
        addRow.Children.Add(costBox);
        addRow.Children.Add(addBtn);
        stack.Children.Add(addRow);

        addBtn.Click += async (_, _) =>
        {
            var svc = App.Host.Get<RewardService>();
            var cost = double.IsNaN(costBox.Value) ? 30 : (int)costBox.Value;
            var (result, _) = await svc.SaveRewardAsync(null, titleBox.Text, "🎁", cost);

            if (result == RewardResult.LimitReached)
            {
                status.Text = Ui.T("rewards.limit");
                status.Visibility = Visibility.Visible;
                return;
            }

            if (result == RewardResult.Ok)
            {
                _rewards = await svc.GetRewardsAsync();
                Build();
            }
        };

        foreach (var reward in _rewards)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock { Text = reward.Emoji, FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = reward.Title, TextTrimming = TextTrimming.CharacterEllipsis });
            label.Children.Add(new TextBlock { Text = "🪙 " + Ui.Loc.Format("rewards.coins", reward.CostCoins), Style = Res("BodySecondaryTextStyle"), FontSize = 12 });
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            var redeemBtn = new Button
            {
                Content = Ui.T("rewards.redeem"),
                Style = Res("GhostButtonStyle"),
                IsEnabled = RewardWallet.CanRedeem(_coins, reward.CostCoins)
            };
            redeemBtn.Click += async (_, _) =>
            {
                var svc = App.Host.Get<RewardService>();
                var (res, next) = await svc.RedeemAsync(reward.Id);
                if (res == RewardResult.Ok)
                {
                    _coins = next;
                    Build();
                }
                else
                {
                    status.Text = Ui.T("rewards.insufficient");
                    status.Visibility = Visibility.Visible;
                }
            };
            Grid.SetColumn(redeemBtn, 2);
            row.Children.Add(redeemBtn);

            stack.Children.Add(new Border { Style = Res("CardBorder"), Padding = new Thickness(12, 10, 12, 10), Child = row });
        }

        stack.Children.Add(status);
        return new Border { Style = Res("GlassCard"), Child = stack };
    }

    private FrameworkElement BuildBadges()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock
        {
            Text = $"{Ui.T("buddy.badges").ToUpperInvariant()} · {Ui.Loc.Format("buddy.badges.count", ViewModel.UnlockedCount, ViewModel.TotalCount)}",
            Style = Res("SectionHeader"),
            Margin = new Thickness(0)
        });

        var repeater = new ItemsRepeater
        {
            Layout = new UniformGridLayout { MinItemWidth = 220, MinColumnSpacing = 12, MinRowSpacing = 12, ItemsStretch = UniformGridLayoutItemsStretch.Fill }
        };
        repeater.ItemsSource = ViewModel.Badges
            .OrderByDescending(b => b.Unlocked)
            .ThenBy(b => b.Hidden)
            .Select(BuildBadge)
            .ToList();
        stack.Children.Add(repeater);
        return new Border { Style = Res("GlassCard"), Child = stack };
    }

    private UIElement BuildHero(bool vertical)
    {
        var stops = HabitGradient.From(ViewModel.Color);
        var hero = new Grid { CornerRadius = new CornerRadius(32), MinHeight = 200 };
        hero.Children.Add(new Border { CornerRadius = new CornerRadius(32), Background = Ui.GradientBrush(stops) });
        hero.Children.Add(new Border { CornerRadius = new CornerRadius(32), Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Opacity = stops.ScrimOpacity });

        if (vertical)
        {
            // Đèn sân khấu: quầng sáng trắng mờ sau Mochi.
            var spot = new RadialGradientBrush { Center = new Windows.Foundation.Point(0.5, 0.32), RadiusX = 0.55, RadiusY = 0.4, GradientOrigin = new Windows.Foundation.Point(0.5, 0.32) };
            spot.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x66, 255, 255, 255), Offset = 0 });
            spot.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0x00, 255, 255, 255), Offset = 1 });
            hero.Children.Add(new Border { CornerRadius = new CornerRadius(32), Background = spot });
        }

        var content = new Grid { Padding = new Thickness(28, 22, 28, 22), ColumnSpacing = 24, RowSpacing = 16, VerticalAlignment = VerticalAlignment.Center };

        if (vertical)
        {
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
        }

        var scale = vertical ? 1.5 : 1;
        var avatar = new Grid { Width = 140 * scale, Height = 150 * scale, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        avatar.Children.Add(new TextBlock { Text = StageEmoji(ViewModel.Stage), FontSize = 92 * scale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });

        foreach (var (slot, accessory) in _wardrobe.Worn)
        {
            avatar.Children.Add(new TextBlock
            {
                Text = accessory.Emoji,
                FontSize = (slot == AccessorySlot.Head ? 52 : 40) * scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = slot switch { AccessorySlot.Head => VerticalAlignment.Top, AccessorySlot.Face => VerticalAlignment.Center, _ => VerticalAlignment.Bottom },
                Margin = slot switch { AccessorySlot.Head => new Thickness(0, -14 * scale, 0, 0), AccessorySlot.Face => new Thickness(0, 14 * scale, 0, 0), _ => new Thickness(0, 0, 0, -6 * scale) }
            });
        }

        var worn = _wardrobe.Worn.Count == 0 ? string.Empty : ", " + string.Join(", ", _wardrobe.Worn.Values.Select(a => Ui.T("accessory." + a.Id)));
        AutomationProperties.SetName(avatar, $"{ViewModel.Name}, {Ui.T("stage." + ViewModel.Stage.ToString().ToLowerInvariant())}{worn}");
        content.Children.Add(avatar);

        var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = ViewModel.Name, FontFamily = Font("DisplayFont"), FontSize = 28, Foreground = White(), HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left }); 
        info.Children.Add(new TextBlock
        {
            Text = $"{Ui.Loc.Format("buddy.level", ViewModel.Level)} · {Ui.T("stage." + ViewModel.Stage.ToString().ToLowerInvariant())}",
            FontSize = 16,
            Foreground = White(230),
            HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left
        });

        var track = new Grid { Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 255, 255, 255)), Margin = new Thickness(0, 6, 0, 0) };
        var fill = new Border { CornerRadius = new CornerRadius(5), Background = White(), HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(fill);
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * ViewModel.LevelProgress;
        AutomationProperties.SetName(track, Ui.T("buddy.xp"));
        info.Children.Add(track);

        info.Children.Add(new TextBlock
        {
            Text = ViewModel.Level >= 100 ? Ui.T("buddy.maxlevel") : Ui.Loc.Format("buddy.xp", ViewModel.XpIntoLevel, ViewModel.XpForNextLevel),
            FontSize = 13,
            Foreground = White(220)
        });

        var chips = new HabitGrid.WinUI.Controls.WrapPanel { HorizontalSpacing = 10, VerticalSpacing = 8, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        chips.Children.Add(Pill("❄ " + Ui.Loc.Format("buddy.freeze", ViewModel.FreezeTokens)));
        chips.Children.Add(Pill("🔥 " + Ui.Loc.Format("buddy.streak", ViewModel.OverallStreak)));
        chips.Children.Add(Pill("🪙 " + Ui.Loc.Format("rewards.coins", _coins)));
        info.Children.Add(chips);

        if (vertical)
        {
            Grid.SetRow(info, 1);
        }
        else
        {
            Grid.SetColumn(info, 1);
        }

        content.Children.Add(info);
        hero.Children.Add(content);
        return hero;
    }

    /// <summary>Tủ phụ kiện (P6): bấm đeo/tháo; món chưa mở ghi rõ điều kiện; món Plus bấm vào thì mở paywall.</summary>
    private UIElement BuildWardrobe()
    {
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock { Text = Ui.T("wardrobe.title").ToUpperInvariant(), Style = Res("SectionHeader"), Margin = new Thickness(0) });
        stack.Children.Add(new TextBlock { Text = Ui.T("wardrobe.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });

        // Thanh tab kiểu segmented control: Đầu (7) | Mặt (3) | Cổ (7); mỗi tab một lưới tự xuống dòng (không còn thanh cuộn ngang).
        var tabs = new Grid { ColumnSpacing = 4, Padding = new Thickness(4), CornerRadius = new CornerRadius(14), Background = (Brush)Application.Current.Resources["SurfaceBrush"] };
        var slots = Enum.GetValues<AccessorySlot>();

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            tabs.ColumnDefinitions.Add(new ColumnDefinition());
            var count = _wardrobe.Items.Count(x => x.Accessory.Slot == slot);
            var label = $"{Ui.T("wardrobe.slot." + slot)} ({count})";
            var tab = new ToggleButton { Style = Res("ChipStyle"), Content = label, IsChecked = slot == _slot, HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(tab, label);
            tab.Click += (_, _) =>
            {
                _slot = slot;
                Build();
            };
            Grid.SetColumn(tab, i);
            tabs.Children.Add(tab);
        }

        stack.Children.Add(tabs);
        stack.Children.Add(new ItemsRepeater
        {
            ItemsSource = _wardrobe.Items.Where(i => i.Accessory.Slot == _slot).Select(BuildAccessoryTile).ToList(),
            Layout = new UniformGridLayout { MinItemWidth = 124, MinItemHeight = 136, MinColumnSpacing = 10, MinRowSpacing = 10, ItemsStretch = UniformGridLayoutItemsStretch.Fill, MaximumRowsOrColumns = 4 }
        });

        return new Border { Style = Res("GlassCard"), Child = stack };
    }

    private UIElement BuildAccessoryTile(WardrobeItem item)
    {
        var a = item.Accessory;
        var name = Ui.T("accessory." + a.Id);
        var requirement = a.Unlock switch
        {
            AccessoryUnlockKind.Level => Ui.Loc.Format("wardrobe.unlock.level", a.RequiredLevel),
            AccessoryUnlockKind.Badge => Ui.Loc.Format("wardrobe.unlock.badge", Ui.T($"badge.{a.Requirement}.name")),
            AccessoryUnlockKind.Seasonal => Ui.Loc.Format("wardrobe.unlock.season", Ui.T("season." + a.Season), Seasons.FirstMonth(a.Season!.Value)),
            _ => Ui.T("wardrobe.unlock.plus")
        };

        var text = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        text.Children.Add(new TextBlock { Text = item.Owned ? a.Emoji : "🔒", FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center, Opacity = item.Owned ? 1 : 0.6 });
        text.Children.Add(new TextBlock { Text = name, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        text.Children.Add(new TextBlock
        {
            Text = item.Equipped ? Ui.T("wardrobe.worn") : item.Owned ? string.Empty : requirement,
            FontSize = 11,
            MaxWidth = 120,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = item.Equipped ? (Brush)Application.Current.Resources["AccentBrandBrush"] : (Brush)Application.Current.Resources["TextMutedBrush"]
        });

        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 10, 8, 10),
            CornerRadius = new CornerRadius(16),
            Background = item.Equipped ? (Brush)Application.Current.Resources["Surface2Brush"] : (Brush)Application.Current.Resources["CardIdleBrush"],
            BorderBrush = item.Equipped ? (Brush)Application.Current.Resources["AccentBrandBrush"] : (Brush)Application.Current.Resources["StrokeSubtleBrush"],
            BorderThickness = new Thickness(item.Equipped ? 2 : 1)
        };
        AutomationProperties.SetName(button, $"{name}, {(item.Equipped ? Ui.T("wardrobe.worn") : item.Owned ? string.Empty : requirement)}");
        button.Click += async (_, _) =>
        {
            if (!item.Owned && a.Unlock is AccessoryUnlockKind.Plus or AccessoryUnlockKind.Seasonal)
            {
                await PaywallDialog.ShowAsync(XamlRoot, HabitGrid.Presentation.ViewModels.PaywallSource.Menu);
            }

            await App.Host.Get<BuddyWardrobeService>().ToggleAsync(a.Id);
            _wardrobe = await App.Host.Get<BuddyWardrobeService>().GetAsync();
            var rewardService = App.Host.Get<RewardService>();
            _coins = await rewardService.GetBalanceAsync();
            _rewards = await rewardService.GetRewardsAsync();
            Build();
        };
        return button;
    }

    /// <summary>Đeo một món cho smoke test (bỏ qua paywall).</summary>
    public async Task<EquipResult> ToggleAccessoryForTestAsync(string id)
    {
        var result = await App.Host.Get<BuddyWardrobeService>().ToggleAsync(id);
        _wardrobe = await App.Host.Get<BuddyWardrobeService>().GetAsync();
        Build();
        return result;
    }

    private UIElement BuildBadge(BadgeItem badge)
    {
        var name = badge.Hidden ? Ui.T("badge.secret.name") : Ui.T($"badge.{badge.Id}.name");
        var description = badge.Hidden ? Ui.T("badge.secret.desc") : Ui.T($"badge.{badge.Id}.desc");

        var icon = new TextBlock { Text = badge.Hidden ? "❓" : GroupEmoji(badge.Group), FontSize = 30, VerticalAlignment = VerticalAlignment.Center };
        // Tiêu đề tối đa 2 dòng thay vì cắt "..." ("Ngày hoàn hả...", "Chiến binh cu...").
        var title = new TextBlock { Text = name, Style = Res("CardTitleTextStyle"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 22 };
        var body = new TextBlock { Text = description, Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(title);
        text.Children.Add(body);

        if (badge.Unlocked)
        {
            if (badge.TierCount > 1)
            {
                text.Children.Add(new TextBlock { Text = Ui.T($"badge.tier.{Math.Clamp(badge.Tier, 1, 3)}"), FontSize = 12, Foreground = (Brush)Application.Current.Resources["AccentBrandBrush"] });
            }
        }
        else if (!badge.Hidden)
        {
            // Chỉ huy hiệu thường mới hiện tiến độ; huy hiệu bí mật không lộ điều kiện.
            text.Children.Add(new TextBlock
            {
                Text = Ui.Loc.Format("badge.progress", Math.Min(badge.Progress, badge.Target), badge.Target),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextMutedBrush"]
            });
        }

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(icon);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var card = new Border
        {
            Style = Res2("CardBorder"),
            Child = row,
            Opacity = badge.Unlocked ? 1 : 0.55,
            BorderBrush = badge.Unlocked ? (Brush)Application.Current.Resources["AccentGradientBrush"] : (Brush)Application.Current.Resources["StrokeSubtleBrush"],
            BorderThickness = new Thickness(badge.Unlocked ? 1.5 : 1)
        };
        AutomationProperties.SetName(card, $"{name}. {description}");
        return card;
    }

    private static Border Pill(string text) => new()
    {
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(12, 5, 12, 5),
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255)),
        Child = new TextBlock { Text = text, Foreground = White(), FontSize = 13 }
    };

    private static SolidColorBrush White(byte alpha = 255) => new(Windows.UI.Color.FromArgb(alpha, 255, 255, 255));

    private static FontFamily Font(string key) => (FontFamily)Application.Current.Resources[key];

    private static Style Res(string key) => (Style)Application.Current.Resources[key];

    private static Style Res2(string key) => (Style)Application.Current.Resources[key];

    private static string StageEmoji(BuddyStage stage) => stage switch
    {
        BuddyStage.Egg => "🥚",
        BuddyStage.Baby => "🐣",
        BuddyStage.Teen => "🐥",
        BuddyStage.Adult => "🦉",
        _ => "🐉"
    };

    private static string GroupEmoji(string group) => group switch
    {
        "start" => "⭐",
        "streak" => "🔥",
        "volume" => "📦",
        "time" => "⏰",
        "discovery" => "🧭",
        _ => "🏅"
    };
}
