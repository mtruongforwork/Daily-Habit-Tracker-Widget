using HabitGrid.Core.Domain.Icons;
using System.ComponentModel;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using DayOfWeekMask = HabitGrid.Core.Models.DayOfWeekMask;

namespace HabitGrid.WinUI.Pages;

/// <summary>Nội dung hộp thoại tạo/sửa thói quen. Mọi trạng thái nằm trong <see cref="HabitEditorViewModel"/>.</summary>
public sealed partial class HabitEditorView : UserControl
{
    /// <summary>Các emoji đang hiển thị trong lưới (theo danh mục hoặc kết quả tìm).</summary>
    private List<string> _shown = [];

    private IconCategory? _category;

    private bool _syncing;

    public HabitEditorView(HabitEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        BuildStaticControls();
        ApplyTexts();
        SyncFromViewModel();
        RebuildReminders();
        ViewModel.PropertyChanged += OnViewModelChanged;
    }

    public HabitEditorViewModel ViewModel { get; }

    /// <summary>Toàn bộ nội dung (kể cả phần cuộn), dùng để chụp ảnh trong smoke test.</summary>
    public UIElement FullContent => Content is Grid { Children: [_, ScrollViewer { Content: UIElement inner }] } ? inner : this;

    /// <summary>Thói quen vừa lưu thành công.</summary>
    public Habit? Saved { get; private set; }

    /// <summary>Lần lưu gần nhất bị chặn vì chạm giới hạn gói Free (để mở paywall sau khi đóng hộp thoại).</summary>
    public bool HitLimit { get; private set; }

    /// <summary>Lưu qua ViewModel; hiện lỗi (đã dịch) ngay trong hộp thoại nếu không hợp lệ.</summary>
    public async Task<bool> TrySaveAsync()
    {
        var result = await ViewModel.SaveAsync();

        if (result.Succeeded)
        {
            Saved = result.Habit;
            ErrorText.Visibility = Visibility.Collapsed;
            return true;
        }

        HitLimit = result.Errors.Any(e => e.Code == "limit.habits");
        ErrorText.Text = string.Join(" ", result.Errors.Select(e => ValidationText.For(Ui.Loc, e)).Distinct());
        ErrorText.Visibility = Visibility.Visible;
        return false;
    }

    // ------------------------------------------------------------------ dựng giao diện

    private void BuildStaticControls()
    {
        EmojiGrid.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
        ColorGrid.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();

        BuildIconCategories();
        RefreshIcons();

        foreach (var hex in HabitCreationService.PresetColors)
        {
            ColorGrid.Items.Add(new Ellipse { Width = 28, Height = 28, Fill = Ui.Brush(hex), Tag = hex, Margin = new Thickness(4) });
        }

        foreach (var key in new[] { "add.kind.boolean", "add.kind.count", "add.kind.duration", "add.kind.quit" })
        {
            KindRadio.Add(Ui.T(key));
        }

        foreach (var key in new[] { "editor.freq.daily", "editor.freq.weekdays", "editor.freq.timesPerWeek", "editor.freq.timesPerMonth", "editor.freq.everyNDays" })
        {
            FrequencyBox.Add(Ui.T(key));
        }

        foreach (var key in new[] { "group.anytime", "group.morning", "group.afternoon", "group.evening" })
        {
            TimeOfDayBox.Add(Ui.T(key));
        }

        var days = new[]
        {
            (DayOfWeek.Monday, "day.mon"), (DayOfWeek.Tuesday, "day.tue"), (DayOfWeek.Wednesday, "day.wed"),
            (DayOfWeek.Thursday, "day.thu"), (DayOfWeek.Friday, "day.fri"), (DayOfWeek.Saturday, "day.sat"), (DayOfWeek.Sunday, "day.sun")
        };

        foreach (var (day, key) in days)
        {
            var toggle = new ToggleButton { Content = Ui.T(key), Tag = day, MinWidth = 52, Padding = new Thickness(10, 6, 10, 6), Style = (Style)Application.Current.Resources["ChipStyle"] };
            toggle.Click += (_, _) =>
            {
                if (!_syncing)
                {
                    ViewModel.ToggleWeekDay(day);
                }
            };
            WeekDaysPanel.Children.Add(toggle);
        }
    }

    private void ApplyTexts()
    {
        IconHeader.Text = Ui.T("editor.icon");
        IconSearchBox.PlaceholderText = Ui.T("icon.search");
        CustomEmojiBox.PlaceholderText = Ui.T("editor.icon.custom");
        ColorHeader.Text = Ui.T("editor.color");
        KindHeader.Text = Ui.T("editor.kind");
        FrequencyHeader.Text = Ui.T("editor.frequency");
        TimeOfDayHeader.Text = Ui.T("editor.timeOfDay");
        RemindersHeader.Text = Ui.T("editor.reminders");
        NameBox.PlaceholderText = Ui.T("editor.name");
        TargetBox.Header = Ui.T("editor.target");
        UnitBox.Header = Ui.T("editor.unit");
        TimesWeekBox.Header = Ui.T("editor.freq.times");
        TimesMonthBox.Header = Ui.T("editor.freq.times");
        IntervalBox.Header = Ui.T("editor.freq.interval");
        AddReminderButton.Content = "＋ " + Ui.T("editor.reminder.add");
        AdvancedExpander.Header = Ui.T("editor.advanced");
        MoodSwitch.Header = Ui.T("editor.askMood");
        NotesSwitch.Header = Ui.T("editor.allowNotes");
        FreezeSwitch.Header = Ui.T("editor.allowFreeze");
        DescriptionBox.Header = Ui.T("editor.description");
        TagsBox.Header = Ui.T("editor.tags");
        TagsBox.PlaceholderText = Ui.T("editor.tags.hint");
        StartDatePicker.Header = Ui.T("editor.startDate");
    }

    // ------------------------------------------------------------------ ViewModel → giao diện

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Các ô văn bản đã ràng buộc hai chiều nên không cần (và không được) đồng bộ lại mỗi lần gõ phím.
        if (e.PropertyName is nameof(HabitEditorViewModel.Name) or nameof(HabitEditorViewModel.Description)
            or nameof(HabitEditorViewModel.TagsText) or nameof(HabitEditorViewModel.Unit))
        {
            if (e.PropertyName == nameof(HabitEditorViewModel.Name))
            {
                UpdateSuggestions();
            }

            UpdatePreview();
            return;
        }

        DispatcherQueue.TryEnqueue(SyncFromViewModel);
    }

    private void SyncFromViewModel()
    {
        _syncing = true;

        try
        {
            IconPreviewText.Text = Ui.IconText(ViewModel.Icon);
            UpdatePreview();

            var emojiIndex = _shown.IndexOf(ViewModel.Icon.StartsWith("emoji:") ? ViewModel.Icon["emoji:".Length..] : string.Empty);
            EmojiGrid.SelectedIndex = emojiIndex;

            var colorIndex = HabitCreationService.PresetColors.ToList().FindIndex(c => c.Equals(ViewModel.ColorHue, StringComparison.OrdinalIgnoreCase));
            ColorGrid.SelectedIndex = colorIndex;
            BuildPreview();

            KindRadio.SelectedIndex = (int)ViewModel.Kind;
            TargetPanel.Visibility = ViewModel.HasNumericTarget ? Visibility.Visible : Visibility.Collapsed;

            FrequencyBox.SelectedIndex = (int)ViewModel.FrequencyType;
            WeekDaysPanel.Visibility = ViewModel.FrequencyType == FrequencyType.WeekDays ? Visibility.Visible : Visibility.Collapsed;
            TimesWeekBox.Visibility = ViewModel.FrequencyType == FrequencyType.TimesPerWeek ? Visibility.Visible : Visibility.Collapsed;
            TimesMonthBox.Visibility = ViewModel.FrequencyType == FrequencyType.TimesPerMonth ? Visibility.Visible : Visibility.Collapsed;
            IntervalBox.Visibility = ViewModel.FrequencyType == FrequencyType.EveryNDays ? Visibility.Visible : Visibility.Collapsed;
            TimesWeekBox.Value = ViewModel.TimesPerWeek;
            TimesMonthBox.Value = ViewModel.TimesPerMonth;
            IntervalBox.Value = ViewModel.IntervalDays;

            foreach (var child in WeekDaysPanel.Children.OfType<ToggleButton>())
            {
                var flag = Core.Models.DayOfWeekMaskExtensions.ToMask((DayOfWeek)child.Tag);
                child.IsChecked = ViewModel.WeekDays.HasFlag(flag);
            }

            TimeOfDayBox.SelectedIndex = (int)ViewModel.TimeOfDay;
            StartDatePicker.Date = new DateTimeOffset(ViewModel.StartDate.ToDateTime(TimeOnly.MinValue));
            TargetBox.Value = ViewModel.Target;
            AddReminderButton.IsEnabled = ViewModel.CanAddReminder;
        }
        finally
        {
            _syncing = false;
        }
    }

    private static readonly string[] KindKeys = ["add.kind.boolean", "add.kind.count", "add.kind.duration", "add.kind.quit"];

    private static readonly string[] TimeKeys = ["group.anytime", "group.morning", "group.afternoon", "group.evening"];

    private void UpdatePreview()
    {
        try
        {
            var stops = HabitGradient.From(ViewModel.ColorHue);
            PreviewFill.Background = Ui.GradientBrush(stops);
            PreviewScrim.Opacity = stops.ScrimOpacity;
        }
        catch (FormatException)
        {
            // Màu đang nhập dở: giữ nguyên gradient cũ.
        }

        IconPreviewText.Text = Ui.IconText(ViewModel.Icon);
        PreviewName.Text = string.IsNullOrWhiteSpace(ViewModel.Name) ? Ui.T("editor.name") : ViewModel.Name;
        var kind = KindKeys[Math.Clamp((int)ViewModel.Kind, 0, KindKeys.Length - 1)];
        var time = TimeKeys[Math.Clamp((int)ViewModel.TimeOfDay, 0, TimeKeys.Length - 1)];
        PreviewSub.Text = Ui.T(kind) + " · " + Ui.T(time);
    }

    /// <summary>Dải xem trước thang màu 5 mức theo màu đang chọn (theo theme hiện tại).</summary>
    private void BuildPreview()
    {
        PreviewStrip.Children.Clear();
        var dark = ActualTheme == ElementTheme.Dark || (ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        HeatmapShades shades;
        try
        {
            shades = OklchPalette.Generate(ViewModel.ColorHue, dark);
        }
        catch (FormatException)
        {
            return;
        }

        for (var level = 0; level <= 4; level++)
        {
            PreviewStrip.Children.Add(new Rectangle
            {
                Width = 26,
                Height = 14,
                RadiusX = 4,
                RadiusY = 4,
                Fill = Ui.Brush(shades[level])
            });
        }
    }

    private void RebuildReminders()
    {
        RemindersPanel.Children.Clear();

        foreach (var reminder in ViewModel.Reminders.ToList())
        {
            var row = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };

            var picker = new TimePicker
            {
                Time = reminder.Time.ToTimeSpan(),
                MinuteIncrement = 5,
                ClockIdentifier = "24HourClock"
            };
            picker.TimeChanged += (_, e) =>
            {
                if (!_syncing)
                {
                    reminder.Time = TimeOnly.FromTimeSpan(e.NewTime);
                }
            };

            var flags = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var smart = new CheckBox { Content = Ui.T("editor.reminder.smart"), IsChecked = reminder.Smart, MinWidth = 0 };
            smart.Click += (_, _) => reminder.Smart = smart.IsChecked == true;
            var follow = new CheckBox { Content = Ui.T("editor.reminder.followUp"), IsChecked = reminder.FollowUp, MinWidth = 0 };
            follow.Click += (_, _) => reminder.FollowUp = follow.IsChecked == true;
            flags.Children.Add(smart);
            flags.Children.Add(follow);

            var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(remove, Ui.T("editor.reminder.remove"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, Ui.T("editor.reminder.remove"));
            remove.Click += (_, _) =>
            {
                ViewModel.RemoveReminder(reminder);
                RebuildReminders();
            };

            row.Children.Add(picker);
            Grid.SetColumn(flags, 1);
            row.Children.Add(flags);
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);
            RemindersPanel.Children.Add(row);
        }
    }


    // ------------------------------------------------------------------ thư viện biểu tượng

    private void BuildIconCategories()
    {
        IconCategoryRow.Children.Clear();
        IconCategoryRow.Children.Add(CategoryChip(null));

        foreach (var category in Enum.GetValues<IconCategory>())
        {
            IconCategoryRow.Children.Add(CategoryChip(category));
        }
    }

    private ToggleButton CategoryChip(IconCategory? category)
    {
        var label = Ui.T("icon.cat." + (category?.ToString().ToLowerInvariant() ?? "all"));
        var chip = new ToggleButton { Style = (Style)Application.Current.Resources["ChipStyle"], Content = label, Tag = category, IsChecked = _category == category };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, label);
        chip.Click += (_, _) =>
        {
            _category = category;
            IconSearchBox.Text = string.Empty;

            foreach (var other in IconCategoryRow.Children.OfType<ToggleButton>())
            {
                other.IsChecked = Equals(other.Tag, category);
            }

            RefreshIcons();
        };
        return chip;
    }

    private void OnIconSearchChanged(object sender, TextChangedEventArgs e) => RefreshIcons();

    /// <summary>Đổ lại lưới: tìm theo từ khóa nếu có, không thì theo danh mục đã chọn (mặc định tất cả).</summary>
    private void RefreshIcons()
    {
        var wasSyncing = _syncing;
        _syncing = true;

        try
        {
            IEnumerable<IconEntry> source = string.IsNullOrWhiteSpace(IconSearchBox.Text)
                ? (_category is { } c ? IconLibrary.InCategory(c) : IconLibrary.All)
                : IconLibrary.Search(IconSearchBox.Text);

            _shown = source.Select(i => i.Emoji).ToList();
            EmojiGrid.Items.Clear();

            foreach (var emoji in _shown)
            {
                EmojiGrid.Items.Add(new TextBlock { Text = emoji, FontSize = 22, Margin = new Thickness(6) });
            }

            var current = ViewModel.Icon.StartsWith("emoji:", StringComparison.Ordinal) ? ViewModel.Icon["emoji:".Length..] : string.Empty;
            EmojiGrid.SelectedIndex = _shown.IndexOf(current);
            UpdateSuggestions();
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    /// <summary>Gợi ý biểu tượng theo tên thói quen đang gõ; ẩn khi không có gợi ý.</summary>
    private void UpdateSuggestions()
    {
        SuggestRow.Children.Clear();
        var suggestions = IconLibrary.Suggest(ViewModel.Name, 6);
        SuggestRow.Visibility = suggestions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (suggestions.Count == 0)
        {
            return;
        }

        SuggestRow.Children.Add(new TextBlock { Text = Ui.T("icon.suggest"), VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"] });

        foreach (var entry in suggestions)
        {
            var button = new Button { Content = new TextBlock { Text = entry.Emoji, FontSize = 20 }, Padding = new Thickness(8, 4, 8, 4), Style = (Style)Application.Current.Resources["GhostButtonStyle"] };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, entry.Keywords.Split(' ')[0] + " " + entry.Emoji);
            button.Click += (_, _) => ViewModel.Icon = HabitIcon.Emoji(entry.Emoji).Value;
            SuggestRow.Children.Add(button);
        }
    }

    // ------------------------------------------------------------------ giao diện → ViewModel

    private void OnEmojiSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && EmojiGrid.SelectedIndex >= 0)
        {
            ViewModel.Icon = HabitIcon.Emoji(_shown[EmojiGrid.SelectedIndex]).Value;
        }
    }

    private void OnCustomEmojiChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing && !string.IsNullOrWhiteSpace(CustomEmojiBox.Text))
        {
            ViewModel.Icon = HabitIcon.Emoji(CustomEmojiBox.Text.Trim()).Value;
        }
    }

    private void OnColorSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && ColorGrid.SelectedItem is Ellipse { Tag: string hex })
        {
            ViewModel.ColorHue = hex;
        }
    }

    private void OnKindChanged(object? sender, EventArgs e)
    {
        if (!_syncing && KindRadio.SelectedIndex >= 0)
        {
            ViewModel.Kind = (HabitKind)KindRadio.SelectedIndex;
        }
    }

    private void OnTargetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_syncing && !double.IsNaN(args.NewValue))
        {
            ViewModel.Target = (int)args.NewValue;
        }
    }

    private void OnFrequencyChanged(object? sender, EventArgs e)
    {
        if (!_syncing && FrequencyBox.SelectedIndex >= 0)
        {
            ViewModel.FrequencyType = (FrequencyType)FrequencyBox.SelectedIndex;
        }
    }

    private void OnTimesWeekChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_syncing && !double.IsNaN(args.NewValue))
        {
            ViewModel.TimesPerWeek = (int)args.NewValue;
        }
    }

    private void OnTimesMonthChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_syncing && !double.IsNaN(args.NewValue))
        {
            ViewModel.TimesPerMonth = (int)args.NewValue;
        }
    }

    private void OnIntervalChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_syncing && !double.IsNaN(args.NewValue))
        {
            ViewModel.IntervalDays = (int)args.NewValue;
        }
    }

    private void OnTimeOfDayChanged(object? sender, EventArgs e)
    {
        if (!_syncing && TimeOfDayBox.SelectedIndex >= 0)
        {
            ViewModel.TimeOfDay = (TimeOfDay)TimeOfDayBox.SelectedIndex;
        }
    }

    private void OnStartDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_syncing && args.NewDate is { } date)
        {
            ViewModel.StartDate = DateOnly.FromDateTime(date.DateTime);
        }
    }

    private void OnAddReminder(object sender, RoutedEventArgs e)
    {
        ViewModel.AddReminder();
        RebuildReminders();
    }
}
