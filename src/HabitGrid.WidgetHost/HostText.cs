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

    public static string HubAllDonePeek => IsVietnamese ? "Xong hết hôm nay!" : "All done today!";

    public static string HubFocusStarted(string? habit) =>
        IsVietnamese ? (habit is null ? "Bắt đầu Focus" : $"Focus: {habit}") : (habit is null ? "Focus started" : $"Focus: {habit}");

    public static string HubLevelUp(int level) => IsVietnamese ? $"Lên cấp {level}!" : $"Level {level}!";

    public static string IslandLeft(int n) => IsVietnamese ? $"{n} còn" : $"{n} left";

    /// <summary>"còn 6g30 hôm nay": thời gian còn lại tới hết ngày (làm tròn xuống phút; dưới 1 giờ thì chỉ phút).</summary>
    public static string IslandTimeLeft(TimeSpan left)
    {
        var minutes = Math.Max(0, (int)left.TotalMinutes);
        var h = minutes / 60;
        var m = minutes % 60;
        var vi = h > 0 ? $"{h}g{m:00}" : $"{m} phút";
        var en = h > 0 ? $"{h}h{m:00}" : $"{m} min";
        return IsVietnamese ? $"còn {vi} hôm nay" : $"{en} left today";
    }

    public static string IslandFocusing => IsVietnamese ? "Đang focus" : "Focusing";

    public static string IslandBreak => IsVietnamese ? "Nghỉ giải lao" : "Break";

    public static string IslandDoneTitle => IsVietnamese ? "Xong hết hôm nay" : "All done today";

    public static string IslandDoneSub(bool streak) => IsVietnamese ? (streak ? "Chuỗi được giữ · hẹn mai nhé" : "Hẹn mai nhé") : (streak ? "Streak kept · see you tomorrow" : "See you tomorrow");

    public static string IslandFreeTitle => IsVietnamese ? "Hôm nay rảnh" : "Free today";

    public static string IslandFreeSub => IsVietnamese ? "Không có việc nào đến hạn" : "Nothing is due";

    public static string PanelRisk(int n) => IsVietnamese ? $"Còn {n} thói quen nữa là giữ được chuỗi" : $"{n} more habit(s) to keep your streak";

    public static string PanelXpToNext(long xp, int level) => IsVietnamese ? $"còn {xp} XP lên Lv {level}" : $"{xp} XP to Lv {level}";

    public static string PanelStatToday => IsVietnamese ? "Hôm nay" : "Today";

    public static string PanelStatBest => IsVietnamese ? "Chuỗi dài nhất" : "Longest streak";

    public static string PanelStatLast7 => IsVietnamese ? "7 ngày qua" : "Last 7 days";

    public static string PanelDays(int n) => IsVietnamese ? $"{n} ngày" : (n == 1 ? "1 day" : $"{n} days");

    public static string PanelTabWeek => IsVietnamese ? "Tuần này" : "This week";

    public static string PanelTabGoals => IsVietnamese ? "Nhiệm vụ" : "Missions";

    public static string PanelTimeLeft(TimeSpan left)
    {
        var minutes = Math.Max(0, (int)left.TotalMinutes);
        var h = minutes / 60;
        var m = minutes % 60;
        return IsVietnamese ? $"Còn {h}g{m:00} đến hết ngày" : $"{h}h{m:00} left today";
    }

    public static string PanelAddPlaceholder => IsVietnamese ? "Thêm thói quen mới…" : "Add a new habit…";

    public static string PanelPerDay(int n) => IsVietnamese ? $"{n} lần/ngày" : $"{n}×/day";

    public static string PanelAdd => IsVietnamese ? "Thêm" : "Add";

    public static string PanelAddLimit => IsVietnamese ? "Gói Free đã đủ số thói quen. Nâng cấp Plus để thêm." : "Free plan habit limit reached. Upgrade to Plus.";

    public static string PanelAddFailed => IsVietnamese ? "Không thêm được thói quen." : "Couldn't add the habit.";

    public static string PanelFocusSession => IsVietnamese ? "Phiên focus" : "Focus session";

    public static string PanelFocusStart => IsVietnamese ? "Bắt đầu" : "Start";

    public static string PanelFocusStop => IsVietnamese ? "Dừng" : "Stop";

    public static string PanelFocusing => IsVietnamese ? "Đang focus" : "Focusing";

    public static string PanelHotkey(string keys) => IsVietnamese ? $"{keys} để mở nhanh widget" : $"{keys} to toggle the widget";

    public static string PanelOpenApp => IsVietnamese ? "Mở app đầy đủ" : "Open full app";

    public static string IslandLater =>IsVietnamese ? "Để sau" : "Later";

    public static string IslandStreak(int days) => IsVietnamese ? $"{days} ngày" : $"{days} days";

    public static string IslandMore(int n) => IsVietnamese ? $"+{n} việc nữa · bấm để mở" : $"+{n} more · click to open";

    public static string IslandAllDone => IsVietnamese ? "Xong hết ✓" : "All done ✓";

    public static string IslandNothing => IsVietnamese ? "Rảnh" : "Free";

    public static string HubFocusEnded => IsVietnamese ? "Xong phiên Focus!" : "Focus session done!";
}
