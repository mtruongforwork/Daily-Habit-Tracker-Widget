namespace HabitGrid.Core.Domain;

/// <summary>Nhóm thói quen theo thứ tự.</summary>
public sealed record Routine(
    Guid Id,
    string Name,
    HabitIcon Icon,
    TimeOfDay TimeOfDay,
    string SortKey,
    IReadOnlyList<Guid> HabitIds);

/// <summary>Hồ sơ cục bộ của người dùng.</summary>
public sealed record UserProfile
{
    public string DisplayName { get; init; } = string.Empty;

    public string BuddyName { get; init; } = "Buddy";

    public string BuddyColor { get; init; } = "#8B5CF6";

    public long Xp { get; init; }

    public int Level { get; init; } = 1;

    public int FreezeTokens { get; init; }

    /// <summary>Id múi giờ dùng để tính ngày logic.</summary>
    public string TimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
}

/// <summary>Huy hiệu đã mở khóa.</summary>
public sealed record Achievement(string Id, DateTime UnlockedAt);

/// <summary>Nhật ký XP để tính lại được khi đổi quy tắc.</summary>
public sealed record XpEvent(Guid Id, string Kind, int Amount, DateTime OccurredAt, Guid? HabitId);

/// <summary>Giới hạn theo gói; Core chỉ hỏi interface, Platform cung cấp hiện thực.</summary>
public interface IEntitlements
{
    int MaxActiveHabits { get; }

    int MaxRemindersPerHabit { get; }

    bool CanUseTheme(string themeId);

    bool CanUseAiCoach { get; }

    /// <summary>Có quyền Plus (mặc định suy từ giới hạn thói quen để không phá các hiện thực cũ).</summary>
    bool IsPlus => MaxActiveHabits == int.MaxValue;

    /// <summary>Số ảnh khoảnh khắc tối đa cho một thói quen trong một ngày (Free 1, Plus không giới hạn).</summary>
    int MaxMomentsPerDay => IsPlus ? int.MaxValue : 1;

    /// <summary>Số ngày tạm nghỉ tối đa mỗi lần (Free 14, Plus 60).</summary>
    int MaxVacationDays => IsPlus ? 60 : 14;

    /// <summary>Số lĩnh vực (Areas) tối đa (Free 3, Plus không giới hạn).</summary>
    int MaxAreas => IsPlus ? int.MaxValue : FreeEntitlements.FreeMaxAreas;

    /// <summary>Số phần thưởng tự tạo tối đa (Free 3, Plus không giới hạn).</summary>
    int MaxCustomRewards => IsPlus ? int.MaxValue : FreeEntitlements.FreeMaxCustomRewards;

    /// <summary>Số routine tối đa (Free 2, Plus không giới hạn).</summary>
    int MaxRoutines => IsPlus ? int.MaxValue : 2;
}
