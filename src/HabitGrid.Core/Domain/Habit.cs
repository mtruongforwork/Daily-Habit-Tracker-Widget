using System.Text.Json.Serialization;
using HabitGrid.Core.Models;

namespace HabitGrid.Core.Domain;

/// <summary>Mục tiêu mỗi ngày. Boolean luôn là 1; Duration tính bằng phút.</summary>
public sealed record HabitTarget(int Value = 1, string? Unit = null);

/// <summary>Một lần nhắc nhở.</summary>
public sealed record Reminder(
    TimeOnly Time,
    DayOfWeekMask Days = DayOfWeekMask.AllWeek,
    bool Smart = false,
    bool FollowUp = false);

/// <summary>Thói quen v2 (SPEC-V2-01 §2.1).</summary>
public sealed class Habit
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public HabitIcon Icon { get; set; } = HabitIcon.Default;

    /// <summary>Màu gốc dạng #RRGGBB, thay cho PaletteId/CustomColorHex của v1.</summary>
    public string ColorHue { get; set; } = "#10B981";

    public HabitKind Kind { get; set; } = HabitKind.Boolean;

    public HabitTarget Target { get; set; } = new();

    public HabitFrequency Frequency { get; set; } = new HabitFrequency.Daily();

    public TimeOfDay TimeOfDay { get; set; } = TimeOfDay.Anytime;

    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    public List<Reminder> Reminders { get; set; } = [];

    public bool AskMood { get; set; }

    public bool AllowNotes { get; set; } = true;

    public bool AllowFreeze { get; set; } = true;

    public Guid? RoutineId { get; set; }

    public int RoutineOrder { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>Khóa sắp xếp dạng fractional index, kéo-thả không cần đánh lại số.</summary>
    public string SortKey { get; set; } = "V";

    public DateTime? ArchivedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Đồng hồ logic lai dùng cho đồng bộ (SPEC-V2-02).</summary>
    public Hlc Version { get; set; }

    public Guid? SharedChallengeId { get; set; }

    /// <summary>Tự động nghỉ T7 và CN (không tính lịch và không làm đứt chuỗi).</summary>
    public bool SkipWeekends { get; set; }

    /// <summary>Lĩnh vực (Area) phân loại thói quen (tùy chọn).</summary>
    public Guid? AreaId { get; set; }

    [JsonIgnore]
    public bool IsActive => DeletedAt is null && ArchivedAt is null;

    [JsonIgnore]
    public bool IsArchived => ArchivedAt is not null && DeletedAt is null;

    /// <summary>Mục tiêu hiệu lực: Boolean, Quit và Dual luôn là 1.</summary>
    [JsonIgnore]
    public int EffectiveTarget => Kind is HabitKind.Boolean or HabitKind.Quit or HabitKind.Dual ? 1 : Math.Max(1, Target.Value);
}
