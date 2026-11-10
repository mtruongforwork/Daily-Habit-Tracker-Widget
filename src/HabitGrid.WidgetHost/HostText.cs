using System.Globalization;

namespace HabitGrid.WidgetHost;

/// <summary>Chữ hiển thị của WidgetHost (tiến trình nhẹ, không nạp bộ localizer của app): Việt/Anh theo ngôn ngữ hệ thống.</summary>
public static class HostText
{
    public static bool IsVietnamese { get; set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi";

    public static string Today => IsVietnamese ? "Hôm nay" : "Today";

    public static string AllDone => IsVietnamese ? "Xong hết rồi! 🎉" : "All done! 🎉";

    public static string NothingDue => IsVietnamese ? "Hôm nay không có gì cần làm 😴" : "Nothing due today 😴";

    public static string QuickPlaceholder => IsVietnamese ? "Gõ để lọc, Enter để check…" : "Type to filter, Enter to check…";

    public static string QuickHint => IsVietnamese
        ? "Enter: check   + / −: tăng giảm   Esc: đóng"
        : "Enter: check   + / −: adjust   Esc: close";

    public static string NoMatch => IsVietnamese ? "Không có thói quen nào khớp" : "No habit matches";

    public static string Progress(int done, int total) => $"{done}/{total}";

    public static string ReminderSingle(string name) => IsVietnamese ? $"Đến giờ: {name}" : $"Time for: {name}";

    public static string ReminderMany(int count) => IsVietnamese ? $"Đến giờ {count} thói quen" : $"{count} habits are due";

    public static string ReminderFollowUp => IsVietnamese ? "Vẫn chưa xong nè 👀" : "Still not done 👀";

    public static string ReminderRescue => IsVietnamese ? "Giữ chuỗi hôm nay! 🔥" : "Save your streak today! 🔥";

    public static string ReminderMorning => IsVietnamese ? "Hôm nay bạn có" : "Today you have";

    public static string AndMore(int count) => IsVietnamese ? $"và {count} thói quen khác" : $"and {count} more";

    public static string Done => IsVietnamese ? "Xong" : "Done";

    public static string DoneAll => IsVietnamese ? "Xong tất cả" : "Done all";

    public static string Snooze => IsVietnamese ? "Nhắc lại sau 30 phút" : "Remind me in 30 min";

    public static string SkipToday => IsVietnamese ? "Bỏ qua hôm nay" : "Skip today";

    public static string Open => IsVietnamese ? "Mở HabitGrid" : "Open HabitGrid";

    public static string FocusTitle => "Focus";

    public static string FocusStart => IsVietnamese ? "Bắt đầu" : "Start";

    public static string FocusPause => IsVietnamese ? "Tạm dừng" : "Pause";

    public static string FocusResume => IsVietnamese ? "Tiếp tục" : "Resume";

    public static string FocusStop => IsVietnamese ? "Dừng" : "Stop";

    public static string FocusBreak => IsVietnamese ? "Nghỉ ngơi ☕" : "Break ☕";

    public static string FocusNoHabit => IsVietnamese ? "(Không gắn thói quen)" : "(No habit)";

    public static string FocusDone(int minutes) => IsVietnamese ? $"Xong {minutes} phút tập trung! 🎉" : $"{minutes} focused minutes done! 🎉";

    public static string FocusPausedBySleep => IsVietnamese ? "Đã tạm dừng khi máy ngủ" : "Paused while the PC slept";

    public static string MomentEmpty => IsVietnamese ? "Chưa có khoảnh khắc" : "No moments yet";

    public static string MomentEmptyHint => IsVietnamese ? "Chụp ảnh trong trang thói quen của app" : "Take a photo on a habit page in the app";

    public static string MomentMonthAgo => IsVietnamese ? "Tháng trước" : "Last month";

    public static string MomentToggleHint => IsVietnamese ? "Bấm để xem ngày này tháng trước" : "Click to see this day last month";

    public static string BuddyLevel(int level) => IsVietnamese ? $"Cấp {level}" : $"Level {level}";

    public static string GoalsTitle => IsVietnamese ? "Mục tiêu" : "Goals";

    public static string GoalsEmpty => IsVietnamese
        ? "Chưa có ý định tuần hay thử thách. Mở HabitGrid để chọn."
        : "No weekly intention or challenge yet. Open HabitGrid to pick one.";

    public static string GoalsIntention => IsVietnamese ? "Ý định tuần" : "Intention";

    public static string GoalsDay(int day, int total) => IsVietnamese ? $"Ngày {day}/{total}" : $"Day {day}/{total}";

    public static string GoalsNone => IsVietnamese ? "Chưa có mục tiêu" : "No goals yet";

    public static string HubAtRisk(int pending) => IsVietnamese ? $"⚠ Còn {pending} việc, giữ chuỗi nhé!" : $"⚠ {pending} left, keep your streak!";
}
