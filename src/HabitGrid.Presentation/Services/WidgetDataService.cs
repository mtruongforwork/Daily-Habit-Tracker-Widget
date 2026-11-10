using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Challenges;
using HabitGrid.Core.Domain.Gamification;
using HabitGrid.Core.Domain.Insights;
using HabitGrid.Core.Domain.Widgets;
using HabitGrid.Presentation.ViewModels;
using IDayProvider = HabitGrid.Core.Services.IDayProvider;

namespace HabitGrid.Presentation.Services;

public sealed record BuddyWidgetData(string Name, int Level, BuddyStage Stage, IReadOnlyList<(AccessorySlot Slot, string Id, string Emoji)> Worn, int TodayDone, int TodayDue, int FreezeTokens)
{
    public double TodayFraction => TodayDue == 0 ? 0 : TodayDone / (double)TodayDue;
}

public sealed record GoalsWidgetData(IntentProgress? Intent, IReadOnlyList<ChallengeProgress> Challenges)
{
    public bool IsEmpty => Intent is null && Challenges.Count == 0;
}

/// <summary>Một dòng của Multi Grid. <see cref="Cells"/> có <c>Weeks × 7</c> ô theo cột (tuần), cũ → mới: 0 trống, 1 đã xong, 2 tạm nghỉ/bỏ qua, -1 chưa tới/ngoài thời gian theo dõi.</summary>
public sealed record MultiGridRow(Guid HabitId, string Name, string Icon, string ColorHue, int Streak, StreakUnit Unit, IReadOnlyList<int> Cells);

public sealed record MultiGridData(int Weeks, IReadOnlyList<MultiGridRow> Rows);

/// <summary>
/// Dữ liệu cho các widget Plus của WidgetHost (M1). Gom ở tầng Presentation để kiểm thử được mà không cần cửa sổ WPF:
/// WidgetHost chỉ vẽ. Dùng lại đúng các dịch vụ của app (phụ kiện, thử thách, ý định tuần) nên widget và app luôn khớp nhau.
/// </summary>
public sealed class WidgetDataService
{
    public const int MultiGridWeeks = 13;

    private readonly IHabitRepository _habits;
    private readonly IDayLogRepository _logs;
    private readonly IProfileRepository _profile;
    private readonly BuddyWardrobeService _wardrobe;
    private readonly ChallengeService _challenges;
    private readonly WeeklyRecapService _weekly;
    private readonly IDayProvider _days;
    private readonly IScheduleCalculator _schedule = new ScheduleCalculator();

    public WidgetDataService(
        IHabitRepository habits,
        IDayLogRepository logs,
        IProfileRepository profile,
        BuddyWardrobeService wardrobe,
        ChallengeService challenges,
        WeeklyRecapService weekly,
        IDayProvider days)
    {
        _habits = habits ?? throw new ArgumentNullException(nameof(habits));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _wardrobe = wardrobe ?? throw new ArgumentNullException(nameof(wardrobe));
        _challenges = challenges ?? throw new ArgumentNullException(nameof(challenges));
        _weekly = weekly ?? throw new ArgumentNullException(nameof(weekly));
        _days = days ?? throw new ArgumentNullException(nameof(days));
    }

    public async Task<BuddyWidgetData> BuddyAsync(CancellationToken ct = default)
    {
        var profile = await _profile.GetAsync(ct);
        var wardrobe = await _wardrobe.GetAsync(ct);
        var today = _days.GetToday();
        var (done, due) = await TodayProgressAsync(today, ct);
        var worn = wardrobe.Worn
            .OrderBy(p => p.Key)
            .Select(p => (p.Key, p.Value.Id, p.Value.Emoji))
            .ToList();

        return new BuddyWidgetData(profile.BuddyName, profile.Level, BuddyViewModel.StageFor(profile.Level), worn, done, due, profile.FreezeTokens);
    }

    public async Task<GoalsWidgetData> GoalsAsync(CancellationToken ct = default)
    {
        var intent = (await _weekly.GetAsync(ct)).Intent;
        var challenges = (await _challenges.RefreshAsync(ct)).Items
            .Where(p => p.State == ChallengeState.Active)
            .Take(WidgetRules.GoalsMaxChallenges)
            .ToList();
        return new GoalsWidgetData(intent, challenges);
    }

    public async Task<MultiGridData> MultiGridAsync(CancellationToken ct = default)
    {
        var today = _days.GetToday();
        var habits = (await _habits.GetAllActiveAsync(ct))
            .Where(h => h.Kind != HabitKind.Quit)
            .OrderBy(h => h.SortKey, StringComparer.Ordinal)
            .Take(WidgetRules.MultiGridMaxHabits)
            .ToList();

        var weekStart = PeriodCalendar.StartOfWeek(today, DayOfWeek.Monday);
        var first = weekStart.AddDays(-7 * (MultiGridWeeks - 1));
        var streaks = new StreakCalculator(DayOfWeek.Monday, _schedule);
        var rows = new List<MultiGridRow>();

        foreach (var habit in habits)
        {
            var logs = await _logs.GetAllForHabitAsync(habit.Id, ct);
            var latest = InsightsCalculator.Latest(logs);
            var cells = new List<int>(MultiGridWeeks * 7);

            for (var i = 0; i < MultiGridWeeks * 7; i++)
            {
                var day = first.AddDays(i);

                if (day > today || day < habit.StartDate)
                {
                    cells.Add(-1);
                    continue;
                }

                var status = latest.TryGetValue((habit.Id, day), out var log) ? log.Status : DayStatus.None;
                cells.Add(status switch
                {
                    DayStatus.Done or DayStatus.Partial => 1,
                    DayStatus.Frozen or DayStatus.Skipped => 2,
                    _ => 0
                });
            }

            var streak = streaks.Calculate(habit, logs, today);
            rows.Add(new MultiGridRow(habit.Id, habit.Name, habit.Icon.Value, habit.ColorHue, streak.Current, streak.Unit, cells));
        }

        return new MultiGridData(MultiGridWeeks, rows);
    }

    private async Task<(int Done, int Due)> TodayProgressAsync(DateOnly today, CancellationToken ct)
    {
        var logs = (await _logs.GetForDayAsync(today, ct)).GroupBy(l => l.HabitId).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.UpdatedAt).First());
        var done = 0;
        var due = 0;

        foreach (var habit in (await _habits.GetAllActiveAsync(ct)).Where(h => h.Kind != HabitKind.Quit))
        {
            if (today < habit.StartDate || !_schedule.IsScheduled(habit, today))
            {
                continue;
            }

            var status = logs.TryGetValue(habit.Id, out var log) ? log.Status : DayStatus.None;

            if (status is DayStatus.Frozen or DayStatus.Skipped)
            {
                continue;
            }

            due++;

            if (status == DayStatus.Done)
            {
                done++;
            }
        }

        return (done, due);
    }
}
