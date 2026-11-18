namespace HabitGrid.Core.Domain;

/// <summary>Một ô của heatmap.</summary>
/// <param name="Level">Độ đậm 0–4 (0 = trống). Xem <see cref="HeatmapBuilder"/> để biết cách tính.</param>
public sealed record HeatCell(
    DateOnly Day,
    int Level,
    DayStatus Status,
    int Value,
    bool IsFuture,
    bool IsBeforeStart,
    bool IsToday,
    bool IsScheduled);

/// <summary>Lưới heatmap: <see cref="Weeks"/>[cột = tuần][hàng = thứ, bắt đầu từ ngày đầu tuần].</summary>
public sealed record HeatmapData(IReadOnlyList<IReadOnlyList<HeatCell>> Weeks, DateOnly From, DateOnly To, DayOfWeek WeekStart)
{
    public IEnumerable<HeatCell> Cells => Weeks.SelectMany(w => w);

    public int FilledCount => Cells.Count(c => c.Level > 0);
}

/// <summary>
/// Dựng dữ liệu heatmap cho một thói quen (03-UI-REBUILD-GLOW2 §5.3). Hàm thuần, không phụ thuộc giao diện.
/// Quy tắc độ đậm: có/không → Done = 4; đếm/thời lượng → theo tỷ lệ giá trị/mục tiêu (&gt;0 = 1, ≥ 34% = 2, ≥ 67% = 3, ≥ 100% = 4);
/// cần bỏ → ngày sạch = 4, ngày lỡ = 0; Skipped/Frozen = 0 nhưng giữ trạng thái để giao diện vẽ riêng.
/// </summary>
public static class HeatmapBuilder
{
    public static HeatmapData Build(
        Habit habit,
        IReadOnlyList<DayLog> logs,
        DateOnly today,
        int weeks,
        DayOfWeek weekStart = DayOfWeek.Monday,
        IScheduleCalculator? schedule = null)
    {
        ArgumentNullException.ThrowIfNull(habit);
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentOutOfRangeException.ThrowIfLessThan(weeks, 1);

        schedule ??= new ScheduleCalculator();

        var byDay = new Dictionary<DateOnly, DayLog>();
        foreach (var log in logs)
        {
            if (!byDay.TryGetValue(log.Day, out var existing) || log.UpdatedAt >= existing.UpdatedAt)
            {
                byDay[log.Day] = log;
            }
        }

        var lastWeekStart = PeriodCalendar.StartOfWeek(today, weekStart);
        var firstWeekStart = lastWeekStart.AddDays(-7 * (weeks - 1));
        var columns = new List<IReadOnlyList<HeatCell>>(weeks);

        for (var w = 0; w < weeks; w++)
        {
            var cells = new List<HeatCell>(7);

            for (var d = 0; d < 7; d++)
            {
                var day = firstWeekStart.AddDays(w * 7 + d);
                byDay.TryGetValue(day, out var log);
                cells.Add(BuildCell(habit, day, log, today, schedule));
            }

            columns.Add(cells);
        }

        return new HeatmapData(columns, firstWeekStart, lastWeekStart.AddDays(6), weekStart);
    }

    private static HeatCell BuildCell(Habit habit, DateOnly day, DayLog? log, DateOnly today, IScheduleCalculator schedule)
    {
        var future = day > today;
        var beforeStart = day < habit.StartDate;
        var scheduled = schedule.IsScheduled(habit, day);
        var status = log?.Status ?? DayStatus.None;
        var value = log?.Value ?? 0;

        var level = 0;

        if (!future && !beforeStart)
        {
            level = LevelFor(habit, status, value);
        }

        return new HeatCell(day, level, status, value, future, beforeStart, day == today, scheduled);
    }

    private static int LevelFor(Habit habit, DayStatus status, int value)
    {
        if (status is DayStatus.Skipped or DayStatus.Frozen)
        {
            return 0;
        }

        if (habit.Kind == HabitKind.Quit)
        {
            return status == DayStatus.Failed || value > 0 ? 0 : 4;
        }

        if (habit.Kind == HabitKind.Boolean)
        {
            return status == DayStatus.Done ? 4 : 0;
        }

        if (value <= 0)
        {
            return 0;
        }

        var ratio = value / (double)habit.EffectiveTarget;
        return ratio >= 1.0 ? 4 : ratio >= 0.67 ? 3 : ratio >= 0.34 ? 2 : 1;
    }

    /// <summary>Nhãn tháng cho trục ngang: (chỉ số cột, tháng) ở tuần đầu tiên của mỗi tháng.</summary>
    public static IReadOnlyList<(int Column, int Month, int Year)> MonthLabels(HeatmapData data) =>
        MonthLabels(data.Weeks.Select(w => w[0].Day).ToList());

    /// <summary>
    /// Nhãn tháng theo ngày đầu của từng cột tuần: gắn ở cột đầu mỗi tháng. Tháng chỉ có ít hơn <paramref name="minColumns"/> cột
    /// trước nhãn kế tiếp (thường là tháng đầu lưới) thì bỏ nhãn, để hai nhãn không đè lên nhau ("Thg 11" / "Thg 12").
    /// </summary>
    public static IReadOnlyList<(int Column, int Month, int Year)> MonthLabels(IReadOnlyList<DateOnly> weekStarts, int minColumns = 3)
    {
        ArgumentNullException.ThrowIfNull(weekStarts);
        var starts = new List<(int Column, int Month, int Year)>();
        var lastMonth = -1;

        for (var column = 0; column < weekStarts.Count; column++)
        {
            var day = weekStarts[column];

            if (day.Month != lastMonth)
            {
                starts.Add((column, day.Month, day.Year));
                lastMonth = day.Month;
            }
        }

        var labels = new List<(int, int, int)>(starts.Count);

        for (var i = 0; i < starts.Count; i++)
        {
            var next = i + 1 < starts.Count ? starts[i + 1].Column : int.MaxValue;

            if (next - starts[i].Column >= minColumns)
            {
                labels.Add(starts[i]);
            }
        }

        return labels;
    }
}
