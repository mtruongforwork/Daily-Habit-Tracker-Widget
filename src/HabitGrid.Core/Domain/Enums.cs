namespace HabitGrid.Core.Domain;

/// <summary>Loại thói quen v2.</summary>
public enum HabitKind
{
    /// <summary>Có/không.</summary>
    Boolean = 0,

    /// <summary>Đếm số lần (ly nước, trang sách…).</summary>
    Count = 1,

    /// <summary>Thời lượng tính bằng phút.</summary>
    Duration = 2,

    /// <summary>Thói quen cần bỏ: giá trị là số lần lỡ, 0 = giữ được.</summary>
    Quit = 3,

    /// <summary>Thói quen 2 chiều (+ / −): làm tốt cộng điểm, lỡ trừ nhẹ.</summary>
    Dual = 4
}

/// <summary>Trạng thái của một ngày. Phần lớn được suy ra từ giá trị, trừ Skipped/Frozen do người dùng đặt.</summary>
public enum DayStatus
{
    None = 0,
    Done = 1,
    Partial = 2,
    Skipped = 3,
    Frozen = 4,

    /// <summary>Chỉ dùng cho thói quen Quit khi đã lỡ.</summary>
    Failed = 5
}

/// <summary>Khung giờ trong ngày để nhóm thói quen.</summary>
public enum TimeOfDay
{
    Anytime = 0,
    Morning = 1,
    Afternoon = 2,
    Evening = 3
}

/// <summary>Đơn vị mà streak được đếm.</summary>
public enum StreakUnit
{
    Day = 0,
    Week = 1,
    Month = 2,
    Window = 3
}
