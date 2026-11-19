namespace HabitGrid.Core.Domain;

/// <summary>Một ô trong bảng Matrix Board: mang độ dài chuỗi liên tiếp để tô đậm dần theo kiểu everyday.app.</summary>
public sealed record MatrixCell(DateOnly Day, DayStatus Status, int ConsecutiveRun, double Intensity);

public static class MatrixBoardBuilder
{
    public const int FreeDays = 14;
    public const int PlusDays = 30;

    public static IReadOnlyList<MatrixCell> BuildRow(
        Habit habit,
        IReadOnlyList<DayLog> logs,
        DateOnly today,
        int days)
    {
        ArgumentNullException.ThrowIfNull(habit);
        ArgumentNullException.ThrowIfNull(logs);

        var count = Math.Clamp(days, 1, 365);
        var start = today.AddDays(-(count - 1));
        var byDay = logs.ToDictionary(l => l.Day);
        var result = new List<MatrixCell>(count);
        var run = 0;

        for (var i = 0; i < count; i++)
        {
            var day = start.AddDays(i);
            var status = byDay.TryGetValue(day, out var log) ? log.Status : DayStatus.None;

            if (status == DayStatus.Done)
            {
                run++;
                // Đậm dần từ 0.38 (ngày 1) lên 1.0 (từ ngày 7 trở đi).
                var intensity = Math.Min(1.0, 0.30 + run * 0.10);
                result.Add(new MatrixCell(day, status, run, intensity));
            }
            else if (status is DayStatus.Skipped or DayStatus.Frozen)
            {
                // Giữ nguyên độ dài chuỗi hiện tại nhưng không cộng thêm.
                result.Add(new MatrixCell(day, status, run, 0.22));
            }
            else
            {
                run = 0;
                result.Add(new MatrixCell(day, status, 0, status == DayStatus.Partial ? 0.25 : 0.0));
            }
        }

        return result;
    }
}

/// <summary>Một đoạn chuỗi liên tiếp kèm ngày bắt đầu – kết thúc để vẽ Streak Timeline ngang.</summary>
public sealed record StreakRun(DateOnly StartDay, DateOnly EndDay, int Length, bool IsCurrent);

public static class StreakTimelineBuilder
{
    public static IReadOnlyList<StreakRun> BuildTopRuns(
        Habit habit,
        IReadOnlyList<DayLog> logs,
        DateOnly today,
        int maxRuns = 5)
    {
        ArgumentNullException.ThrowIfNull(habit);
        ArgumentNullException.ThrowIfNull(logs);

        var doneDays = logs
            .Where(l => l.Status == DayStatus.Done && l.Day <= today)
            .Select(l => l.Day)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (doneDays.Count == 0)
        {
            return [];
        }

        var runs = new List<StreakRun>();
        var runStart = doneDays[0];
        var prev = doneDays[0];

        for (var i = 1; i < doneDays.Count; i++)
        {
            var cur = doneDays[i];
            if (cur == prev.AddDays(1))
            {
                prev = cur;
            }
            else
            {
                var len = prev.DayNumber - runStart.DayNumber + 1;
                runs.Add(new StreakRun(runStart, prev, len, prev >= today.AddDays(-1)));
                runStart = cur;
                prev = cur;
            }
        }

        var lastLen = prev.DayNumber - runStart.DayNumber + 1;
        runs.Add(new StreakRun(runStart, prev, lastLen, prev >= today.AddDays(-1)));

        return runs
            .OrderByDescending(r => r.Length)
            .ThenByDescending(r => r.EndDay)
            .Take(Math.Max(1, maxRuns))
            .ToList();
    }
}

public enum UrgencyLevel
{
    Fresh = 0,
    Steady = 1,
    Slipping = 2,
    Neglected = 3
}

public sealed record UrgencyAssessment(UrgencyLevel Level, int MissedScheduledDays, int RevivalBonusXp);

/// <summary>Đánh giá mức độ bỏ bê (kiểu Habitica) để gợi ý sắc thái màu và thưởng XP "Hồi sinh".</summary>
public static class TaskUrgencyEvaluator
{
    public static UrgencyAssessment Evaluate(
        Habit habit,
        IReadOnlyList<DayLog> logs,
        DateOnly today,
        IScheduleCalculator? schedule = null)
    {
        ArgumentNullException.ThrowIfNull(habit);
        ArgumentNullException.ThrowIfNull(logs);

        if (habit.Kind == HabitKind.Quit || today <= habit.StartDate)
        {
            return new UrgencyAssessment(UrgencyLevel.Fresh, 0, 0);
        }

        schedule ??= new ScheduleCalculator();
        var byDay = logs.ToDictionary(l => l.Day);

        if (byDay.TryGetValue(today, out var todayLog) && todayLog.Status == DayStatus.Done)
        {
            return new UrgencyAssessment(UrgencyLevel.Fresh, 0, 0);
        }

        var missed = 0;
        for (var offset = 1; offset <= 10; offset++)
        {
            var d = today.AddDays(-offset);
            if (d < habit.StartDate)
            {
                break;
            }

            if (!schedule.IsScheduled(habit, d))
            {
                continue;
            }

            if (byDay.TryGetValue(d, out var log) && log.Status is DayStatus.Done or DayStatus.Skipped or DayStatus.Frozen)
            {
                break;
            }

            missed++;
        }

        return missed switch
        {
            0 => new UrgencyAssessment(UrgencyLevel.Fresh, 0, 0),
            1 => new UrgencyAssessment(UrgencyLevel.Steady, 1, 5),
            <= 3 => new UrgencyAssessment(UrgencyLevel.Slipping, missed, 10),
            _ => new UrgencyAssessment(UrgencyLevel.Neglected, missed, 15)
        };
    }
}

public enum RoomTimeOfDay
{
    Morning = 0,
    Afternoon = 1,
    Evening = 2,
    Night = 3
}

public sealed record MochiRoomAppearance(RoomTimeOfDay TimeSlot, string TopHex, string BottomHex, string? WeatherEmoji);

/// <summary>Phòng của Mochi đổi ánh sáng theo giờ trong ngày và thời tiết theo âm thanh Focus.</summary>
public static class MochiRoomTheme
{
    public static MochiRoomAppearance For(TimeOnly time, string? ambientSoundId = null)
    {
        var slot = time.Hour switch
        {
            >= 5 and < 12 => RoomTimeOfDay.Morning,
            >= 12 and < 17 => RoomTimeOfDay.Afternoon,
            >= 17 and < 21 => RoomTimeOfDay.Evening,
            _ => RoomTimeOfDay.Night
        };

        var (top, bottom) = slot switch
        {
            RoomTimeOfDay.Morning => ("#6366F1", "#F472B6"),
            RoomTimeOfDay.Afternoon => ("#3B82F6", "#8B5CF6"),
            RoomTimeOfDay.Evening => ("#7C3AED", "#EC4899"),
            _ => ("#1E1B4B", "#4C1D95")
        };

        var weather = ambientSoundId?.Trim().ToLowerInvariant() switch
        {
            "rain" => "🌧️",
            "waves" => "🌊",
            _ => null
        };

        return new MochiRoomAppearance(slot, top, bottom, weather);
    }
}
