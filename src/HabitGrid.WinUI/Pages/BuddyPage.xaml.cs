using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Gamification;
using HabitGrid.WinUI.Plus;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HabitGrid.WinUI.Pages;

/// <summary>Trang Buddy: linh vật tiến hóa theo level, thanh XP, token freeze và lưới huy hiệu (có huy hiệu bí mật ẩn điều kiện).</summary>
public sealed partial class BuddyPage : Page
{
    private readonly StackPanel _root = new() { Spacing = 16, Padding = new Thickness(36, 28, 36, 32), MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Center };

    public BuddyPage()
    {
        ViewModel = App.Host.Get<BuddyViewModel>();
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        Scroll.Content = _root;
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
            Build();
        }
        catch (Exception ex)
        {
            App.Host.Get<IAppLogger>().Error("Mở trang Buddy lỗi", ex);
        }
    }

    private void Build()
    {
        _root.Children.Clear();
        _root.Children.Add(new TextBlock { Text = Ui.T("buddy.title"), Style = Res("TitleDisplayTextStyle") });
        _root.Children.Add(BuildHero());
        _root.Children.Add(BuildWardrobe());

        var header = new TextBlock
        {
            Text = $"{Ui.T("buddy.badges").ToUpperInvariant()} · {Ui.Loc.Format("buddy.badges.count", ViewModel.UnlockedCount, ViewModel.TotalCount)}",
            Style = Res("SectionHeader"),
            Margin = new Thickness(4, 12, 0, 0)
        };
        _root.Children.Add(header);

        var repeater = new ItemsRepeater
        {
            Layout = new UniformGridLayout { MinItemWidth = 230, MinColumnSpacing = 12, MinRowSpacing = 12, ItemsStretch = UniformGridLayoutItemsStretch.Fill }
        };
        var tiles = ViewModel.Badges
            .OrderByDescending(b => b.Unlocked)
            .ThenBy(b => b.Hidden)
            .Select(BuildBadge)
            .ToList();
        repeater.ItemsSource = tiles;
        _root.Children.Add(repeater);
    }

    private UIElement BuildHero()
    {
        var stops = HabitGradient.From(ViewModel.Color);
        var hero = new Grid { CornerRadius = new CornerRadius(32), MinHeight = 200 };
        hero.Children.Add(new Border { CornerRadius = new CornerRadius(32), Background = Ui.GradientBrush(stops) });
        hero.Children.Add(new Border { CornerRadius = new CornerRadius(32), Background = new SolidColorBrush(Microsoft.UI.Colors.Black), Opacity = stops.ScrimOpacity });

        var content = new Grid { Padding = new Thickness(28, 22, 28, 22), ColumnSpacing = 24 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());

        var avatar = new Grid { Width = 140, Height = 150, VerticalAlignment = VerticalAlignment.Center };
        avatar.Children.Add(new TextBlock { Text = StageEmoji(ViewModel.Stage), FontSize = 92, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });

        foreach (var (slot, accessory) in _wardrobe.Worn)
        {
            avatar.Children.Add(new TextBlock
            {
                Text = accessory.Emoji,
                FontSize = slot == AccessorySlot.Head ? 52 : 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = slot switch { AccessorySlot.Head => VerticalAlignment.Top, AccessorySlot.Face => VerticalAlignment.Center, _ => VerticalAlignment.Bottom },
                Margin = slot switch { AccessorySlot.Head => new Thickness(0, -14, 0, 0), AccessorySlot.Face => new Thickness(0, 14, 0, 0), _ => new Thickness(0, 0, 0, -6) }
            });
        }

        var worn = _wardrobe.Worn.Count == 0 ? string.Empty : ", " + string.Join(", ", _wardrobe.Worn.Values.Select(a => Ui.T("accessory." + a.Id)));
        AutomationProperties.SetName(avatar, $"{ViewModel.Name}, {Ui.T("stage." + ViewModel.Stage.ToString().ToLowerInvariant())}{worn}");
        content.Children.Add(avatar);

        var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = ViewModel.Name, FontFamily = Font("DisplayFont"), FontSize = 28, Foreground = White() });
        info.Children.Add(new TextBlock
        {
            Text = $"{Ui.Loc.Format("buddy.level", ViewModel.Level)} · {Ui.T("stage." + ViewModel.Stage.ToString().ToLowerInvariant())}",
            FontSize = 16,
            Foreground = White(230)
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

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        chips.Children.Add(Pill("❄ " + Ui.Loc.Format("buddy.freeze", ViewModel.FreezeTokens)));
        chips.Children.Add(Pill("🔥 " + Ui.Loc.Format("buddy.streak", ViewModel.OverallStreak)));
        info.Children.Add(chips);

        Grid.SetColumn(info, 1);
        content.Children.Add(info);
        hero.Children.Add(content);
        return hero;
    }

    /// <summary>Tủ phụ kiện (P6): bấm đeo/tháo; món chưa mở ghi rõ điều kiện; món Plus bấm vào thì mở paywall.</summary>
    private UIElement BuildWardrobe()
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = Ui.T("wardrobe.title").ToUpperInvariant(), Style = Res("SectionHeader"), Margin = new Thickness(4, 12, 0, 0) });
        stack.Children.Add(new TextBlock { Text = Ui.T("wardrobe.hint"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });

        foreach (var slot in Enum.GetValues<AccessorySlot>())
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("wardrobe.slot." + slot), FontSize = 13, Foreground = (Brush)Application.Current.Resources["TextMutedBrush"] });
            // Lưới tự xuống dòng thay cho hàng cuộn ngang (lỗi thanh cuộn ngang và cắt chữ "Hoa tulip" ở kệ phụ kiện).
            var tiles = _wardrobe.Items.Where(i => i.Accessory.Slot == slot).Select(BuildAccessoryTile).ToList();
            stack.Children.Add(new ItemsRepeater
            {
                ItemsSource = tiles,
                Layout = new UniformGridLayout { MinItemWidth = 124, MinItemHeight = 136, MinColumnSpacing = 10, MinRowSpacing = 10, ItemsStretch = UniformGridLayoutItemsStretch.Fill }
            });
        }

        return stack;
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
