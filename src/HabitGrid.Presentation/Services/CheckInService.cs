using HabitGrid.Core.Domain;

namespace HabitGrid.Presentation.Services;

/// <summary>
/// Check-in nhanh một bấm (widget, tray, quick check-in): có/không bật-tắt; đếm +1 rồi về 0 khi đã đủ;
/// thời lượng bấm là đủ mục tiêu, bấm lại là xóa. Thói quen cần bỏ không check qua đây.
/// </summary>
public sealed class CheckInService
{
    private readonly IDayLogRepository _logs;
    private readonly ISettingsStore? _settings;

    public CheckInService(IDayLogRepository logs, ISettingsStore? settings = null)
    {
        _settings = settings;
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
    }

    /// <summary>Trả về bản ghi mới của ngày, hoặc null nếu đã xóa/không áp dụng.</summary>
    public async Task<DayLog?> ToggleAsync(Guid habitId, DateOnly day, CancellationToken ct = default)
    {
        return await ChangeAsync(habitId, day, (habit, current) =>
        {
            var target = habit.EffectiveTarget;
            return habit.Kind switch
            {
                HabitKind.Boolean => current > 0 ? 0 : 1,
                HabitKind.Duration => current >= target ? 0 : target,
                _ => current >= target ? 0 : current + 1
            };
        }, ct);
    }

    public Task<DayLog?> AdjustAsync(Guid habitId, DateOnly day, int delta, CancellationToken ct = default) =>
        ChangeAsync(habitId, day, (habit, current) => habit.Kind == HabitKind.Boolean
            ? (delta > 0 ? 1 : 0)
            : Math.Max(0, current + delta), ct, ignoreUnchanged: true);

    private async Task<DayLog?> ChangeAsync(Guid habitId, DateOnly day,
        Func<Habit, int, int> getNext, CancellationToken ct, bool ignoreUnchanged = false)
    {
        if (_settings is not null && (await ReadOnlyHabits.LoadAsync(_settings, ct)).Contains(habitId))
        {
            return await _logs.GetAsync(habitId, day, ct);
        }

        return await _logs.UpdateAsync(habitId, day, (habit, existing) =>
        {
            if (habit.Kind == HabitKind.Quit || day < habit.StartDate || existing?.Status == DayStatus.Frozen)
            {
                return existing;
            }

            var current = existing?.Status == DayStatus.Skipped ? 0 : existing?.Value ?? 0;
            var next = getNext(habit, current);
            if (ignoreUnchanged && next == current && existing?.Status != DayStatus.Skipped)
            {
                return existing;
            }

            return next == 0 ? null : DayLogRules.Normalize(habit, new DayLog
            {
                HabitId = habitId,
                Day = day,
                Value = next,
                Note = existing?.Note,
                Mood = existing?.Mood,
                CheckedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }, ct);
    }
}
