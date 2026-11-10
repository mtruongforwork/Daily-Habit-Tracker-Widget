using HabitGrid.Core.Domain.Monetization;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HabitGrid.WinUI.Plus;

/// <summary>
/// Paywall Plus (02-UX-SCREENS §12): Buddy cầm vé, lợi ích, 3 gói với giá đầy đủ, ghi rõ tự gia hạn và cách hủy,
/// nút "Để sau" rõ ràng, nút khôi phục. Chỉ mở khi người dùng chạm giới hạn hoặc tự mở; mua chỉ khi bấm "Chọn gói này".
/// </summary>
public static class PaywallDialog
{
    private static bool _open;

    /// <summary>Trả về true nếu sau khi đóng người dùng đang có Plus.</summary>
    public static async Task<bool> ShowAsync(XamlRoot root, PaywallSource source, int? themeUnlockLevel = null)
    {
        if (_open)
        {
            return App.Host.Get<PlusService>().Status.IsPlus;
        }

        _open = true;

        try
        {
            var vm = new PaywallViewModel(App.Host.Get<PlusService>(), source);
            await vm.LoadAsync();

            var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            var panel = new StackPanel { Spacing = 12, MaxWidth = 620 };
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Content = new ScrollViewer { Content = panel, MaxHeight = 640, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                CloseButtonText = Ui.T("plus.close"),
                SecondaryButtonText = Ui.T("plus.restore"),
                DefaultButton = ContentDialogButton.None
            };

            // Đầu trang: Buddy cầm vé + lý do mở (nếu chạm giới hạn).
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
            head.Children.Add(new TextBlock { Text = "🐣🎟️", FontSize = 40, VerticalAlignment = VerticalAlignment.Center });
            var titles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = Ui.T("plus.name"), Style = (Style)Application.Current.Resources["TitleDisplayTextStyle"] });
            titles.Children.Add(new TextBlock { Text = Ui.T("plus.tagline"), Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });
            head.Children.Add(titles);
            panel.Children.Add(head);

            var reason = source switch
            {
                PaywallSource.HabitLimit => Ui.T("plus.reason.limit"),
                PaywallSource.Theme => Ui.Loc.Format("plus.reason.theme", themeUnlockLevel ?? 10),
                _ => null
            };

            var plus = App.Host.Get<PlusService>();

            if (plus.Status.Tier == HabitGrid.Core.Domain.Monetization.PlusTier.Promo)
            {
                panel.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Success, Message = Ui.Loc.Format("plus.promo.active", plus.PromoDaysLeft) });
            }

            if (reason is not null)
            {
                panel.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational, Message = reason });
            }

            var benefits = new StackPanel { Spacing = 4 };
            // Chỉ liệt kê quyền lợi đã thật sự có trong app (không bán thứ chưa làm).
            foreach (var (emoji, key) in new[] { ("♾️", "plus.benefit.habits"), ("📊", "plus.benefit.insights"), ("🧭", "plus.benefit.advanced"), ("☁️", "plus.benefit.backup"), ("🌈", "plus.benefit.accent"), ("⏱", "plus.benefit.routineTimer"), ("🎩", "plus.benefit.accessories"), ("✨", "plus.benefit.wrapped"), ("📷", "plus.benefit.moments"), ("🎞", "plus.benefit.share"), ("🖼", "plus.benefit.momentWidget"), ("🎨", "plus.benefit.themes"), ("❄️", "plus.benefit.freeze"), ("💜", "plus.benefit.support") })
            {
                benefits.Children.Add(new TextBlock { Text = $"{emoji}  {Ui.T(key)}", TextWrapping = TextWrapping.Wrap });
            }

            panel.Children.Add(benefits);

            // Ba gói, giá đầy đủ.
            var plans = new Grid { ColumnSpacing = 10 };

            for (var i = 0; i < vm.Plans.Count; i++)
            {
                plans.ColumnDefinitions.Add(new ColumnDefinition());
                var card = BuildPlanCard(vm, vm.Plans[i], dialog, message);
                Grid.SetColumn(card, i);
                plans.Children.Add(card);
            }

            panel.Children.Add(plans);

            if (!vm.StoreAvailable)
            {
                panel.Children.Add(new TextBlock { Text = Ui.T("plus.nostore"), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["WarningBrush"] });
            }
            else if (!vm.HasStoreProducts)
            {
                // Chưa có sản phẩm Plus trên Store: không hiện như đang bán.
                panel.Children.Add(new TextBlock { Text = Ui.T("plus.soon"), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["WarningBrush"] });
            }
            else if (vm.PricesAreReference)
            {
                panel.Children.Add(new TextBlock { Text = Ui.T("plus.reference"), TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"] });
            }

            panel.Children.Add(message);
            panel.Children.Add(new TextBlock { Text = Ui.T("plus.terms"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextMutedBrush"] });

            dialog.SecondaryButtonClick += async (_, args) =>
            {
                // Khôi phục không đóng hộp thoại để người dùng thấy kết quả.
                args.Cancel = true;
                var deferral = args.GetDeferral();

                try
                {
                    await vm.RestoreAsync();
                    Show(message, vm.MessageKey);
                }
                finally
                {
                    deferral.Complete();
                }
            };

            await dialog.ShowAsync();
            return vm.IsPlus;
        }
        finally
        {
            _open = false;
        }
    }

    private static Border BuildPlanCard(PaywallViewModel vm, PaywallPlan plan, ContentDialog dialog, TextBlock message)
    {
        var (nameKey, periodKey) = plan.Plan switch
        {
            PlusPlan.Monthly => ("plus.plan.monthly", "plus.per.month"),
            PlusPlan.Yearly => ("plus.plan.yearly", "plus.per.year"),
            _ => ("plus.plan.lifetime", "plus.once")
        };

        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = Ui.T(nameKey), Style = (Style)Application.Current.Resources["CardTitleTextStyle"] });
        stack.Children.Add(new TextBlock { Text = plan.Price, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = Ui.T(periodKey), Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"] });

        if (plan.SavePercent is { } save)
        {
            stack.Children.Add(new TextBlock { Text = Ui.Loc.Format("plus.save", save), Foreground = (Brush)Application.Current.Resources["SuccessBrush"], FontSize = 12 });
        }

        if (plan.HasTrial)
        {
            stack.Children.Add(new TextBlock { Text = Ui.T("plus.trial"), Foreground = (Brush)Application.Current.Resources["AccentBrandBrush"], FontSize = 12 });
        }

        var buy = new Button
        {
            Content = Ui.T("plus.buy"),
            Style = (Style)Application.Current.Resources["GradientButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 6, 0, 0),
            IsEnabled = vm.PurchasesOpen && !vm.IsPlus
        };
        AutomationProperties.SetName(buy, $"{Ui.T("plus.buy")}: {Ui.T(nameKey)}, {plan.Price}");

        // Nút Mua: đường DUY NHẤT trong giao diện gọi mua hàng (có test quét mã nguồn).
        buy.Click += async (_, _) =>
        {
            buy.IsEnabled = false;
            var hwnd = App.MainWindowInstance is { } window ? WinRT.Interop.WindowNative.GetWindowHandle(window) : IntPtr.Zero;
            var outcome = await vm.BuyAsync(plan.Plan, hwnd);
            Show(message, vm.MessageKey);

            if (outcome is PurchaseOutcome.Succeeded or PurchaseOutcome.AlreadyOwned)
            {
                dialog.CloseButtonText = Ui.T("celebrate.close");
            }
            else
            {
                buy.IsEnabled = vm.PurchasesOpen;
            }
        };
        stack.Children.Add(buy);

        return new Border
        {
            Style = (Style)Application.Current.Resources["CardBorder"],
            Padding = new Thickness(14),
            Child = stack,
            BorderBrush = plan.IsBestValue ? (Brush)Application.Current.Resources["AccentGradientBrush"] : (Brush)Application.Current.Resources["StrokeSubtleBrush"],
            BorderThickness = new Thickness(plan.IsBestValue ? 2 : 1)
        };
    }

    private static void Show(TextBlock message, string? key)
    {
        message.Visibility = key is null ? Visibility.Collapsed : Visibility.Visible;
        message.Text = key is null ? string.Empty : Ui.T(key);
    }
}

/// <summary>Hộp chọn thói quen tiếp tục chạy sau khi hạ cấp (SPEC-V2-10 §5). Không xóa dữ liệu nào.</summary>
public static class ChooseRunningDialog
{
    public static async Task ShowAsync(XamlRoot root)
    {
        var plus = App.Host.Get<PlusService>();
        var limit = App.Host.Get<HabitGrid.Core.Domain.IEntitlements>().MaxActiveHabits;
        var allowance = await plus.GetAllowanceAsync();
        var habits = (await App.Host.Get<HabitGrid.Core.Domain.IHabitRepository>().GetAllActiveAsync())
            .OrderBy(h => h.SortKey, StringComparer.Ordinal)
            .ToList();

        var count = new TextBlock();
        var boxes = new List<(CheckBox Box, Guid Id)>();
        var list = new StackPanel { Spacing = 2 };

        void Update()
        {
            var selected = boxes.Count(b => b.Box.IsChecked == true);
            count.Text = Ui.Loc.Format("plan.choose.count", selected, limit);

            foreach (var (box, _) in boxes)
            {
                box.IsEnabled = box.IsChecked == true || selected < limit;
            }
        }

        foreach (var habit in habits)
        {
            var box = new CheckBox
            {
                Content = $"{Ui.IconText(habit.Icon.Value)}  {habit.Name}",
                IsChecked = allowance.Running.Contains(habit.Id)
            };
            box.Click += (_, _) => Update();
            boxes.Add((box, habit.Id));
            list.Children.Add(box);
        }

        var panel = new StackPanel { Spacing = 10, MaxWidth = 480 };
        panel.Children.Add(new TextBlock { Text = Ui.Loc.Format("plan.choose.body", limit), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(count);
        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360 });
        Update();

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Ui.Loc.Format("plan.choose.title", limit),
            Content = panel,
            PrimaryButtonText = Ui.T("plan.choose.save"),
            CloseButtonText = Ui.T("plus.close"),
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await plus.ChooseRunningAsync(boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Id));
        }
    }
}
