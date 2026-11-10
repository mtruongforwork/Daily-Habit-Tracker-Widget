namespace HabitGrid.Core.Domain.Widgets;

/// <summary>Các widget ghim desktop ngoài widget lưới thói quen và Today List (Plus).</summary>
public enum ExtraWidget
{
    /// <summary>Buddy đang đeo phụ kiện, tiến độ hôm nay.</summary>
    Buddy,

    /// <summary>Ý định tuần này và các thử thách đang chạy.</summary>
    Goals,

    /// <summary>Nhiều thói quen, mỗi thói quen một heatmap nhỏ.</summary>
    MultiGrid
}

/// <summary>
/// Phân gói widget (M1). Free: một widget lưới + Today List. Plus: tối đa 3 widget lưới và thêm Buddy / Mục tiêu / Multi Grid
/// (tổng tối đa 6 widget, đúng ngân sách RAM đã đo). Widget Plus chỉ ẩn khi hết Plus, cấu hình bật/tắt vẫn được giữ.
/// </summary>
public static class WidgetRules
{
    public const int FreeMaxGrids = 1;
    public const int PlusMaxGrids = 3;
    public const int MultiGridMaxHabits = 4;
    public const int MultiGridMinHabits = 2;
    public const int GoalsMaxChallenges = 3;

    public static int MaxGrids(bool isPlus) => isPlus ? PlusMaxGrids : FreeMaxGrids;

    public static bool CanUse(ExtraWidget widget, bool isPlus) => isPlus;

    /// <summary>Khóa settings bật/tắt (do Cài đặt của app ghi, WidgetHost đọc).</summary>
    public static string EnabledKey(ExtraWidget widget) => $"widgets.{widget.ToString().ToLowerInvariant()}.enabled";

    /// <summary>Khóa lưu vị trí cuối cùng của widget.</summary>
    public static string PositionKey(ExtraWidget widget) => $"widget.pos.{widget.ToString().ToLowerInvariant()}";

    /// <summary>Widget mới mặc định TẮT: chỉ hiện khi người dùng bật trong Cài đặt (tránh bật bất ngờ cho người đã có Plus).</summary>
    public static bool IsEnabled(string? stored) => stored == "1";

    /// <summary>Có hiện widget này không: đủ quyền và người dùng đã bật.</summary>
    public static bool ShouldShow(ExtraWidget widget, bool isPlus, string? stored) => CanUse(widget, isPlus) && IsEnabled(stored);
}
