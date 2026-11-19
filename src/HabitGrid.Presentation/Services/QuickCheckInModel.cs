using System.Globalization;
using System.Text;
using HabitGrid.Core.Domain;

namespace HabitGrid.Presentation.Services;

/// <summary>Một dòng trong danh sách "hôm nay" của widget Today List và Quick check-in.</summary>
public sealed record QuickItem(Habit Habit, int Value, DayStatus Status)
{
    public Guid Id => Habit.Id;

    public string Name => Habit.Name;

    public int Target => Habit.EffectiveTarget;

    public bool IsNumeric => Habit.Kind is HabitKind.Count or HabitKind.Duration;

    public bool IsDone => Status == DayStatus.Done;

    public bool IsSkipped => Status is DayStatus.Skipped or DayStatus.Frozen;
}

/// <summary>
/// Dữ liệu và thao tác cho widget Today List và cửa sổ Quick check-in (SPEC-V2-05 §2.1, §4): thói quen đến hạn hôm nay,
/// lọc không dấu, check một bấm, tăng/giảm cho loại đếm. Không phụ thuộc giao diện.
/// </summary>
public sealed class QuickCheckInModel
{
    private readonly IHabitRepository _habits;
    private readonly IDayLogRepository _logs;
    private readonly CheckInService _checkIn;
    private readonly IScheduleCalculator _schedule;
    private List<QuickItem> _items = [];

    private readonly ISettingsStore? _settings;
    private readonly IQuickTodoRepository? _quickTodos;
    private List<QuickTodo> _todos = [];

    public QuickCheckInModel(
        IHabitRepository habits,
        IDayLogRepository logs,
        IScheduleCalculator? schedule = null,
        ISettingsStore? settings = null,
        IQuickTodoRepository? quickTodos = null)
    {
        _habits = habits ?? throw new ArgumentNullException(nameof(habits));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        _settings = settings;
        _quickTodos = quickTodos;
        _checkIn = new CheckInService(habits, logs, settings);
        _schedule = schedule ?? new ScheduleCalculator();
    }

    public DateOnly Today { get; private set; }

    public Guid? GoalOfDayHabitId { get; private set; }

    public bool IsGoalOfDay(Guid habitId) => GoalOfTheDayRules.IsGoal(GoalOfDayHabitId, habitId);

    public IReadOnlyList<QuickTodo> QuickTodos => _todos;

    /// <summary>Mọi thói quen đến hạn hôm nay (trừ loại cần bỏ), theo thứ tự người dùng sắp.</summary>
    public IReadOnlyList<QuickItem> Items => _items;

    public int DueCount => _items.Count(i => !i.IsSkipped);

    public int DoneCount => _items.Count(i => i.IsDone);

    /// <summary>Tỷ lệ hoàn thành 0–1; 1 khi không có gì đến hạn.</summary>
    public double Fraction => DueCount == 0 ? 1 : DoneCount / (double)DueCount;

    public async Task LoadAsync(DateOnly today, CancellationToken ct = default)
    {
        Today = today;
        // Thói quen chỉ đọc (vượt giới hạn Free) không hiện trong danh sách check nhanh.
        var readOnly = _settings is null ? new HashSet<Guid>() : await ReadOnlyHabits.LoadAsync(_settings, ct);
        var habits = (await _habits.GetAllActiveAsync(ct))
            .Where(h => h.Kind != HabitKind.Quit && today >= h.StartDate && _schedule.IsScheduled(h, today) && !readOnly.Contains(h.Id))
            .OrderBy(h => h.SortKey, StringComparer.Ordinal)
            .ToList();

        var logs = await _logs.GetForDayAsync(today, ct);
        var byHabit = logs.GroupBy(l => l.HabitId).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.UpdatedAt).First());

        _items = habits
            .Select(h => byHabit.TryGetValue(h.Id, out var log) ? new QuickItem(h, log.Value, log.Status) : new QuickItem(h, 0, DayStatus.None))
            .ToList();

        GoalOfDayHabitId = null;
        if (_settings is not null)
        {
            var goalDate = await _settings.GetAsync(SettingKeys.GoalOfDayDate, ct);
            var goalIdRaw = await _settings.GetAsync(SettingKeys.GoalOfDayHabitId, ct);
            if (goalDate == today.ToString("yyyy-MM-dd") && Guid.TryParse(goalIdRaw, out var parsedGoalId))
            {
                GoalOfDayHabitId = parsedGoalId;
            }
        }

        _todos = _quickTodos is not null
            ? (await _quickTodos.GetForDayAsync(today, ct)).ToList()
            : [];
    }

    public async Task ToggleQuickTodoAsync(QuickTodo todo, CancellationToken ct = default)
    {
        if (_quickTodos is null)
        {
            return;
        }

        var nextDone = !todo.IsDone;
        await _quickTodos.SetDoneAsync(todo.Id, nextDone, nextDone ? DateTime.UtcNow : null, ct);
    }

    /// <summary>Lọc theo tên, không phân biệt hoa/thường và dấu; chưa xong lên trên, đã xong/bỏ qua xuống dưới.</summary>
    public IReadOnlyList<QuickItem> Filter(string? query) =>
        _items
            .Where(i => Matches(i.Name, query))
            .OrderBy(i => i.IsDone || i.IsSkipped ? 1 : 0)
            .ToList();

    /// <summary>Có/không bật-tắt; đếm +1 rồi về 0 khi đã đủ.</summary>
    public Task ToggleAsync(QuickItem item, CancellationToken ct = default) =>
        _checkIn.ToggleAsync(item.Id, Today, ct);

    /// <summary>Tăng/giảm giá trị (loại đếm/thời lượng theo bước 1; có/không: dương = xong, âm = bỏ xong).</summary>
    public async Task AdjustAsync(QuickItem item, int delta, CancellationToken ct = default)
    {
        var habit = item.Habit;
        var current = item.IsSkipped ? 0 : item.Value;

        var next = habit.Kind == HabitKind.Boolean
            ? (delta > 0 ? 1 : 0)
            : Math.Max(0, current + delta);

        if (next == current && !item.IsSkipped)
        {
            return;
        }

        if (next == 0)
        {
            await _logs.DeleteAsync(habit.Id, Today, ct);
            return;
        }

        var existing = await _logs.GetAsync(habit.Id, Today, ct);

        await _logs.UpsertAsync(DayLogRules.Normalize(habit, new DayLog
        {
            HabitId = habit.Id,
            Day = Today,
            Value = next,
            Note = existing?.Note,
            Mood = existing?.Mood,
            CheckedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }), ct);
    }

    /// <summary>Khớp không dấu: "doc" khớp "Đọc sách"; mọi từ của truy vấn phải xuất hiện.</summary>
    public static bool Matches(string name, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var haystack = Fold(name);
        return Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(haystack.Contains);
    }

    /// <summary>Bỏ dấu và hạ chữ thường (đ → d).</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
