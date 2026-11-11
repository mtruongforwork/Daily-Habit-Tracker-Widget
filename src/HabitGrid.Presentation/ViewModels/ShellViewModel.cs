using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HabitGrid.Presentation.Services;

namespace HabitGrid.Presentation.ViewModels;

/// <summary>Một mục trong thanh điều hướng.</summary>
public sealed record NavItem(string Key, string TitleKey, string Glyph);

public static class PageKeys
{
    public const string Today = "today";
    public const string Habits = "habits";
    public const string Insights = "insights";
    public const string Calendar = "calendar";
    public const string Journal = "journal";
    public const string Focus = "focus";
    public const string Buddy = "buddy";
    public const string Settings = "settings";
}

/// <summary>Trạng thái shell: các mục điều hướng và trang đang chọn.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(ILocalizer localizer)
    {
        Localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public ILocalizer Localizer { get; }

    /// <summary>Các trang chính (Segoe Fluent Icons).</summary>
    public IReadOnlyList<NavItem> MainItems { get; } =
    [
        new(PageKeys.Today, "nav.today", "\uE80F"),
        new(PageKeys.Calendar, "nav.calendar", ""),
        new(PageKeys.Journal, "nav.journal", ""),
        new(PageKeys.Focus, "nav.focus", ""),
        new(PageKeys.Habits, "nav.habits", ""),
        new(PageKeys.Insights, "nav.insights", ""),
        new(PageKeys.Buddy, "nav.buddy", "")
    ];

    public NavItem SettingsItem { get; } = new(PageKeys.Settings, "nav.settings", "");

    public IEnumerable<NavItem> AllItems => MainItems.Append(SettingsItem);

    [ObservableProperty]
    private string _selectedKey = PageKeys.Today;

    public string TitleOf(string key)
    {
        var item = AllItems.FirstOrDefault(i => i.Key == key);
        return item is null ? string.Empty : Localizer[item.TitleKey];
    }
}
