using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HabitGrid.Core.Domain;
using IDayProvider = HabitGrid.Core.Services.IDayProvider;
using HabitGrid.Presentation.Services;
using DomainDayLog = HabitGrid.Core.Domain.DayLog;
using DomainHabit = HabitGrid.Core.Domain.Habit;

namespace HabitGrid.Presentation.ViewModels;

public sealed record MonthRate(int Year, int Month, int? Percent);

public sealed record WeekdayRate(DayOfWeek Day, int? Percent);

public sealed record JournalEntry(DateOnly Day, string Note, int? Mood);

/// <summary>Trang chi tiết một thói quen (03-UI-REBUILD-GLOW2 §5.3): chuỗi, thống kê, heatmap 52 tuần, nhật ký.</summary>
public sealed partial class HabitDetailViewModel : ObservableObject
{
    public const int HeatmapWeeks = 52;
    public const int MonthsShown = 6;

    private readonly IHabitRepository _habits;
    private readonly IDayLogRepository _logs;
    private readonly IDayProvider _dayProvider;
    private readonly IStreakCalculator _streaks;
    private readonly IStatsCalculator _stats;
    private readonly IUiDispatcher _dispatcher;
    private readonly IAppLogger _logger;

    public HabitDetailViewModel(
        IHabitRepository habits,
        IDayLogRepository logs,
        IDayProvider dayProvider,
        IStreakCalculator? streaks = null,
        IStatsCalculator? stats = null,
        IUiDispatcher? dispatcher = null,
        IAppLogger? logger = null)
    {
        _habits = habits ?? throw new ArgumentNullException(nameof(habits));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        _dayProvider = dayProvider ?? throw new ArgumentNullException(nameof(dayProvider));
        _streaks = streaks ?? new StreakCalculator();
        _stats = stats ?? new StatsCalculator();
        _dispatcher = dispatcher ?? new ImmediateDispatcher();
        _logger = logger ?? NullLogger.Instance;
    }

    [ObservableProperty]
    private DomainHabit? _habit;

    [ObservableProperty]
    private int _currentStreak;

    [ObservableProperty]
    private int _bestStreak;

    [ObservableProperty]
    private StreakUnit _streakUnit;

    /// <summary>Tỷ lệ 30 ngày gần nhất, 0–100.</summary>
    [ObservableProperty]
    private int? _rate30;

    /// <summary>Chênh lệch so với 30 ngày trước đó (điểm phần trăm); null khi thiếu dữ liệu.</summary>
    [ObservableProperty]
    private int? _rateDelta;

    [ObservableProperty]
    private int _totalDone;

    [ObservableProperty]
    private HeatmapData? _heatmap;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _notFound;

    public ObservableCollection<MonthRate> Months { get; } = [];

    public ObservableCollection<WeekdayRate> Weekdays { get; } = [];

    public IReadOnlyList<StreakRun> StreakTimeline { get; private set; } = [];

    public ObservableCollection<JournalEntry> Journal { get; } = [];

    public DateOnly Today => _dayProvider.GetToday();

    public async Task LoadAsync(Guid habitId)
    {
        try
        {
            ErrorMessage = null;
            var habit = await _habits.GetByIdAsync(habitId);

            if (habit is null)
            {
                _dispatcher.Post(() => NotFound = true);
                return;
            }

            var today = _dayProvider.GetToday();
            var logs = await _logs.GetAllForHabitAsync(habitId);
            var streak = _streaks.Calculate(habit, logs, today);

            var rate = _stats.CompletionRate(habit, logs, today.AddDays(-29), today);
            var previous = _stats.CompletionRate(habit, logs, today.AddDays(-59), today.AddDays(-30));
            int? delta = rate is { } r && previous is { } p ? (int)Math.Round((r - p) * 100) : null;

            var heatmap = HeatmapBuilder.Build(habit, logs, today, HeatmapWeeks);
            var months = BuildMonths(habit, logs, today);
            var byWeekday = _stats.RateByWeekday(habit, logs, today.AddDays(-89), today);
            var weekdays = Enumerable.Range(0, 7)
                .Select(i => (DayOfWeek)(((int)DayOfWeek.Monday + i) % 7))
                .Select(d => new WeekdayRate(d, byWeekday.TryGetValue(d, out var v) && v is { } x ? (int)Math.Round(x * 100) : null))
                .ToList();
            var timeline = StreakTimelineBuilder.BuildTopRuns(habit, logs, today);
            var journal = logs
                .Where(l => !string.IsNullOrWhiteSpace(l.Note))
                .OrderByDescending(l => l.Day)
                .Take(30)
                .Select(l => new JournalEntry(l.Day, l.Note!, l.Mood))
                .ToList();

            _dispatcher.Post(() =>
            {
                NotFound = false;
                Habit = habit;
                CurrentStreak = streak.Current;
                BestStreak = streak.Best;
                StreakUnit = streak.Unit;
                Rate30 = rate is { } v ? (int)Math.Round(v * 100) : null;
                RateDelta = delta;
                TotalDone = _stats.TotalDone(logs);
                Heatmap = heatmap;
                StreakTimeline = timeline;
                Replace(Months, months);
                Replace(Weekdays, weekdays);
                Replace(Journal, journal);
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Tải chi tiết thói quen thất bại", ex);
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Lấy bản ghi của một ngày để mở trình sửa ngày; null nếu chưa có.</summary>
    public Task<DomainDayLog?> GetDayAsync(DateOnly day) =>
        Habit is null ? Task.FromResult<DomainDayLog?>(null) : _logs.GetAsync(Habit.Id, day);

    /// <summary>Ghi một ngày. <paramref name="skipped"/> đặt trạng thái Bỏ qua (tối đa 2 lần/7 ngày).</summary>
    public async Task<bool> SaveDayAsync(DateOnly day, int value, bool skipped, string? note, int? mood)
    {
        if (Habit is not { } habit)
        {
            return false;
        }

        if (day > _dayProvider.GetToday() || day < habit.StartDate)
        {
            ErrorMessage = "day.invalid";
            return false;
        }

        try
        {
            ErrorMessage = null;

            if (skipped)
            {
                var all = await _logs.GetAllForHabitAsync(habit.Id);

                if (!DayLogRules.CanSkip(all, day))
                {
                    ErrorMessage = "skip.limit";
                    return false;
                }
            }

            var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

            if (!skipped && value <= 0 && trimmedNote is null && mood is null && habit.Kind != HabitKind.Quit)
            {
                await _logs.DeleteAsync(habit.Id, day);
            }
            else
            {
                var log = new DomainDayLog
                {
                    HabitId = habit.Id,
                    Day = day,
                    Value = skipped ? 0 : value,
                    Status = skipped ? DayStatus.Skipped : DayStatus.None,
                    Note = trimmedNote,
                    Mood = mood,
                    CheckedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _logs.UpsertAsync(DayLogRules.Normalize(habit, log));
            }

            await LoadAsync(habit.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error("Lưu ngày thất bại", ex);
            ErrorMessage = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    public async Task ArchiveAsync()
    {
        if (Habit is { } habit)
        {
            await _habits.ArchiveAsync(habit.Id);
        }
    }

    private List<MonthRate> BuildMonths(DomainHabit habit, IReadOnlyList<DomainDayLog> logs, DateOnly today)
    {
        var result = new List<MonthRate>(MonthsShown);

        for (var i = MonthsShown - 1; i >= 0; i--)
        {
            var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            var last = i == 0 ? today : first.AddMonths(1).AddDays(-1);
            var rate = _stats.CompletionRate(habit, logs, first, last);
            result.Add(new MonthRate(first.Year, first.Month, rate is { } r ? (int)Math.Round(r * 100) : null));
        }

        return result;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();

        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
