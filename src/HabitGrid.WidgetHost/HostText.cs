using System.Globalization;

namespace HabitGrid.WidgetHost;

/// <summary>Chữ hiển thị của WidgetHost (tiến trình nhẹ, không nạp bộ localizer của app): chọn theo mã ngôn ngữ (vi/en/ja/ko).</summary>
public static class HostText
{
    public static string Language { get; set; } = Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public static bool LockedForShots { get; set; }

    public static bool IsVietnamese
    {
        get => Language == "vi";
        set => Language = value ? "vi" : "en";
    }

    public static void SetLanguage(string? language) => Language = Normalize(language);

    public static string Normalize(string? language)
    {
        var code = (language ?? string.Empty).Trim().ToLowerInvariant();
        return code switch
        {
            _ when code.StartsWith("vi") => "vi",
            _ when code.StartsWith("ja") => "ja",
            _ when code.StartsWith("ko") => "ko",
            _ => "en"
        };
    }

    private static string T(string vi, string en, string ja, string ko) => Language switch
    {
        "vi" => vi,
        "ja" => ja,
        "ko" => ko,
        _ => en
    };

    public static string Today => T("Hôm nay", "Today", "今日", "오늘");

    public static string AllDone => T("Xong hết rồi! 🎉", "All done! 🎉", "すべて完了！ 🎉", "모두 완료! 🎉");

    public static string NothingDue => T("Hôm nay không có gì cần làm 😴", "Nothing due today 😴", "今日の予定はありません 😴", "오늘 할 일이 없어요 😴");

    public static string QuickPlaceholder => T("Gõ để lọc, Enter để check…", "Type to filter, Enter to check…", "入力して絞り込み、Enterでチェック…", "입력하여 필터링, Enter로 체크…");

    public static string QuickHint => T(
        "Enter: check   + / −: tăng giảm   Esc: đóng",
        "Enter: check   + / −: adjust   Esc: close",
        "Enter: チェック   + / −: 調整   Esc: 閉じる",
        "Enter: 체크   + / −: 조절   Esc: 닫기");

    public static string NoMatch => T("Không có thói quen nào khớp", "No habit matches", "一致する習慣がありません", "일치하는 습관이 없습니다");

    public static string Progress(int done, int total) => $"{done}/{total}";

    public static string ReminderSingle(string name) => T($"Đến giờ: {name}", $"Time for: {name}", $"時間です: {name}", $"할 시간이에요: {name}");

    public static string ReminderMany(int count) => T($"Đến giờ {count} thói quen", $"{count} habits are due", $"{count}件の習慣の時間です", $"습관 {count}개 할 시간이에요");

    public static string ReminderFollowUp => T("Vẫn chưa xong nè 👀", "Still not done 👀", "まだ終わっていません 👀", "아직 완료하지 않았어요 👀");

    public static string ReminderRescue => T("Giữ chuỗi hôm nay! 🔥", "Save your streak today! 🔥", "今日も連続記録を守ろう！ 🔥", "오늘도 연속 기록을 지켜요! 🔥");

    public static string ReminderMorning => T("Hôm nay bạn có", "Today you have", "今日の予定", "오늘의 습관");

    public static string AndMore(int count) => T($"và {count} thói quen khác", $"and {count} more", $"ほか{count}件", $"외 {count}개");

    public static string Done => T("Xong", "Done", "完了", "완료");

    public static string DoneAll => T("Xong tất cả", "Done all", "すべて完了", "모두 완료");

    public static string Snooze => T("Nhắc lại sau 30 phút", "Remind me in 30 min", "30分後に再通知", "30분 후 다시 알림");

    public static string SkipToday => T("Bỏ qua hôm nay", "Skip today", "今日はスキップ", "오늘 건너뛰기");

    public static string Open => T("Mở HabitGrid", "Open HabitGrid", "HabitGridを開く", "HabitGrid 열기");

    public static string FocusTitle => "Focus";

    public static string FocusStart => T("Bắt đầu", "Start", "開始", "시작");

    public static string FocusPause => T("Tạm dừng", "Pause", "一時停止", "일시정지");

    public static string FocusResume => T("Tiếp tục", "Resume", "再開", "재개");

    public static string FocusStop => T("Dừng", "Stop", "停止", "중지");

    public static string FocusBreak => T("Nghỉ ngơi ☕", "Break ☕", "休憩 ☕", "휴식 ☕");

    public static string FocusNoHabit => T("(Không gắn thói quen)", "(No habit)", "(習慣なし)", "(습관 없음)");

    public static string FocusDone(int minutes) => T(
        $"Xong {minutes} phút tập trung! 🎉",
        $"{minutes} focused minutes done! 🎉",
        $"{minutes}分の集中が完了！ 🎉",
        $"{minutes}분 집중 완료! 🎉");

    public static string FocusPausedBySleep => T("Đã tạm dừng khi máy ngủ", "Paused while the PC slept", "スリープ中に一時停止しました", "PC 절전 중 일시정지됨");

    public static string MomentEmpty => T("Chưa có khoảnh khắc", "No moments yet", "まだモーメントがありません", "아직 모먼트가 없어요");

    public static string MomentEmptyHint => T("Chụp ảnh trong trang thói quen của app", "Take a photo on a habit page in the app", "アプリの習慣ページで写真を撮りましょう", "앱의 습관 페이지에서 사진을 찍어보세요");

    public static string MomentMonthAgo => T("Tháng trước", "Last month", "先月", "지난달");

    public static string MomentToggleHint => T("Bấm để xem ngày này tháng trước", "Click to see this day last month", "クリックして先月の同日を表示", "클릭하여 지난달 같은 날 보기");

    public static string BuddyLevel(int level) => T($"Cấp {level}", $"Level {level}", $"Lv {level}", $"레벨 {level}");

    public static string GoalsTitle => T("Mục tiêu", "Goals", "目標", "목표");

    public static string GoalsEmpty => T(
        "Chưa có ý định tuần hay thử thách. Mở HabitGrid để chọn.",
        "No weekly intention or challenge yet. Open HabitGrid to pick one.",
        "週間テーマやチャレンジがありません。HabitGridで選べます。",
        "주간 목표나 챌린지가 없습니다. HabitGrid에서 선택하세요.");

    public static string GoalsIntention => T("Ý định tuần", "Intention", "週間テーマ", "주간 목표");

    public static string GoalsDay(int day, int total) => T($"Ngày {day}/{total}", $"Day {day}/{total}", $"{day}/{total}日目", $"{day}/{total}일차");

    public static string GoalsNone => T("Chưa có mục tiêu", "No goals yet", "まだ目標がありません", "아직 목표가 없어요");

    public static string HubAtRisk(int pending) => T(
        $"⚠ Còn {pending} việc, giữ chuỗi nhé!",
        $"⚠ {pending} left, keep your streak!",
        $"⚠ 残り{pending}件、連続記録を守ろう！",
        $"⚠ {pending}개 남았어요, 연속 기록을 지켜요!");

    public static string HubAllDonePeek => T("Xong hết hôm nay!", "All done today!", "今日はすべて完了！", "오늘 모두 완료!");

    public static string HubFocusStarted(string? habit) => T(
        habit is null ? "Bắt đầu Focus" : $"Focus: {habit}",
        habit is null ? "Focus started" : $"Focus: {habit}",
        habit is null ? "フォーカス開始" : $"フォーカス: {habit}",
        habit is null ? "포커스 시작" : $"포커스: {habit}");

    public static string HubLevelUp(int level) => T($"Lên cấp {level}!", $"Level {level}!", $"レベル{level}にアップ！", $"레벨 {level} 달성!");

    public static string IslandLeft(int n) => T($"{n} còn", $"{n} left", $"残り{n}", $"{n}개 남음");

    /// <summary>"còn 6g30 hôm nay": thời gian còn lại tới hết ngày (làm tròn xuống phút; dưới 1 giờ thì chỉ phút).</summary>
    public static string IslandTimeLeft(TimeSpan left)
    {
        var minutes = Math.Max(0, (int)left.TotalMinutes);
        var h = minutes / 60;
        var m = minutes % 60;
        return Language switch
        {
            "vi" => $"còn {(h > 0 ? $"{h}g{m:00}" : $"{m} phút")} hôm nay",
            "ja" => $"今日あと{(h > 0 ? $"{h}時間{m:00}分" : $"{m}分")}",
            "ko" => $"오늘 {(h > 0 ? $"{h}시간 {m:00}분" : $"{m}분")} 남음",
            _ => $"{(h > 0 ? $"{h}h{m:00}" : $"{m} min")} left today"
        };
    }

    public static string IslandFocusing => T("Đang focus", "Focusing", "集中中", "집중 중");

    public static string IslandBreak => T("Nghỉ giải lao", "Break", "休憩中", "휴식 중");

    public static string IslandDoneTitle => T("Xong hết hôm nay", "All done today", "今日はすべて完了", "오늘 모두 완료");

    public static string IslandDoneSub(bool streak) => T(
        streak ? "Chuỗi được giữ · hẹn mai nhé" : "Hẹn mai nhé",
        streak ? "Streak kept · see you tomorrow" : "See you tomorrow",
        streak ? "連続記録キープ・また明日" : "また明日",
        streak ? "연속 기록 유지 · 내일 만나요" : "내일 만나요");

    public static string IslandFreeTitle => T("Hôm nay rảnh", "Free today", "今日は休み", "오늘 일정 없음");

    public static string IslandFreeSub => T("Không có việc nào đến hạn", "Nothing is due", "予定はありません", "예정된 습관이 없어요");

    public static string PanelRisk(int n) => T(
        $"Còn {n} thói quen nữa là giữ được chuỗi",
        $"{n} more habit(s) to keep your streak",
        $"連続記録を守るまであと{n}件",
        $"연속 기록 유지까지 {n}개 남음");

    public static string PanelXpToNext(long xp, int level) => T(
        $"còn {xp} XP lên Lv {level}",
        $"{xp} XP to Lv {level}",
        $"Lv {level}まであと{xp} XP",
        $"Lv {level}까지 {xp} XP 남음");

    public static string PanelStatToday => T("Hôm nay", "Today", "今日", "오늘");

    public static string PanelStatBest => T("Chuỗi dài nhất", "Longest streak", "最長連続", "최장 연속");

    public static string PanelStatLast7 => T("7 ngày qua", "Last 7 days", "過去7日間", "최근 7일");

    public static string PanelDays(int n) => Language switch
    {
        "vi" => $"{n} ngày",
        "ja" => $"{n}日",
        "ko" => $"{n}일",
        _ => n == 1 ? "1 day" : $"{n} days"
    };

    public static string PanelTabWeek => T("Tuần này", "This week", "今週", "이번 주");

    public static string PanelTabGoals => T("Nhiệm vụ", "Missions", "ミッション", "미션");

    public static string PanelTimeLeft(TimeSpan left)
    {
        var minutes = Math.Max(0, (int)left.TotalMinutes);
        var h = minutes / 60;
        var m = minutes % 60;
        return Language switch
        {
            "vi" => $"Còn {h}g{m:00} đến hết ngày",
            "ja" => $"今日あと{h}時間{m:00}分",
            "ko" => $"오늘 {h}시간 {m:00}분 남음",
            _ => $"{h}h{m:00} left today"
        };
    }

    public static string PanelAddPlaceholder => T("Thêm thói quen mới…", "Add a new habit…", "新しい習慣を追加…", "새 습관 추가…");

    public static string PanelPerDay(int n) => T($"{n} lần/ngày", $"{n}×/day", $"1日{n}回", $"하루 {n}회");

    public static string PanelAdd => T("Thêm", "Add", "追加", "추가");

    public static string PanelAddLimit => T(
        "Gói Free đã đủ số thói quen. Nâng cấp Plus để thêm.",
        "Free plan habit limit reached. Upgrade to Plus.",
        "無料プランの上限に達しました。Plusにアップグレードしてください。",
        "무료 플랜 한도에 도달했습니다. Plus로 업그레이드하세요.");

    public static string PanelAddFailed => T("Không thêm được thói quen.", "Couldn't add the habit.", "習慣を追加できませんでした。", "습관을 추가할 수 없습니다.");

    public static string PanelFocusSession => T("Phiên focus", "Focus session", "集中セッション", "포커스 세션");

    public static string PanelFocusStart => T("Bắt đầu", "Start", "開始", "시작");

    public static string PanelFocusStop => T("Dừng", "Stop", "停止", "중지");

    public static string PanelFocusing => T("Đang focus", "Focusing", "集中中", "집중 중");

    public static string PanelHotkey(string keys) => T(
        $"{keys} để mở nhanh widget",
        $"{keys} to toggle the widget",
        $"{keys} でウィジェットを切り替え",
        $"{keys} 로 위젯 열기/닫기");

    public static string PanelOpenApp => T("Mở app đầy đủ", "Open full app", "アプリを開く", "전체 앱 열기");

    public static string IslandLater => T("Để sau", "Later", "あとで", "나중에");

    public static string IslandStreak(int days) => T($"{days} ngày", $"{days} days", $"{days}日", $"{days}일");

    public static string IslandMore(int n) => T(
        $"+{n} việc nữa · bấm để mở",
        $"+{n} more · click to open",
        $"ほか{n}件・クリックで開く",
        $"외 {n}개 · 클릭하여 열기");

    public static string IslandAllDone => T("Xong hết ✓", "All done ✓", "すべて完了 ✓", "모두 완료 ✓");

    public static string IslandNothing => T("Rảnh", "Free", "空き", "여유");

    public static string HubFocusEnded => T("Xong phiên Focus!", "Focus session done!", "集中セッション完了！", "포커스 세션 완료!");
}
