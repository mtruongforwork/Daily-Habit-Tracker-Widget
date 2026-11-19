namespace HabitGrid.Core.Domain;

/// <summary>Cài đặt dạng khóa–giá trị, lưu theo máy (không đồng bộ).</summary>
public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string value, CancellationToken ct = default);

    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>Các khóa cài đặt dùng chung.</summary>
public static class SettingKeys
{
    public const string OnboardingDone = "onboarding.done";
    public const string OnboardingState = "onboarding.state";
    public const string Language = "language";
    public const string DayStartHour = "dayStartHour";
    public const string Theme = "theme";
    public const string ThemeMode = "themeMode";
    public const string ReduceMotion = "reduceMotion";

    /// <summary>"0" tắt, còn lại (hoặc chưa có) = WidgetHost chạy cùng Windows.</summary>
    public const string WidgetsAutoStart = "widgets.autostart";

    public const string GoalOfDayHabitId = "v4.goalOfDay.habitId";
    public const string GoalOfDayDate = "v4.goalOfDay.date";
    public const string CoinsBalance = "v4.wallet.coins";
    public const string RepairTokens = "v4.streak.repairTokens";
}

/// <summary>Giới hạn của gói Free (SPEC-V2-10 §1). Plus có hiện thực riêng ở tầng Platform.</summary>
public sealed class FreeEntitlements : IEntitlements
{
    public const int FreeMaxActiveHabits = 5;
    public const int FreeMaxAreas = 3;
    public const int FreeMaxCustomRewards = 3;

    public int MaxActiveHabits => FreeMaxActiveHabits;

    public int MaxAreas => FreeMaxAreas;

    public int MaxCustomRewards => FreeMaxCustomRewards;

    public int MaxRemindersPerHabit => HabitValidator.MaxReminders;

    public bool CanUseAiCoach => false;

    /// <summary>Theme miễn phí; theme mở bằng level xét riêng ở <see cref="Monetization.ThemeAccess"/>.</summary>
    public bool CanUseTheme(string themeId) => Monetization.ThemeAccess.FreeThemes.Contains(themeId);
}

/// <summary>Không giới hạn (dùng trong test và cho gói Plus khi đã có Platform).</summary>
public sealed class UnlimitedEntitlements : IEntitlements
{
    public int MaxActiveHabits => int.MaxValue;

    public int MaxAreas => int.MaxValue;

    public int MaxCustomRewards => int.MaxValue;

    public int MaxRemindersPerHabit => HabitValidator.MaxReminders;

    public bool CanUseAiCoach => true;

    public bool CanUseTheme(string themeId) => true;
}
