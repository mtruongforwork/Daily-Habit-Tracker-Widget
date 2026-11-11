namespace HabitGrid.Core.Domain.Calendar;

/// <summary>Một ô của lịch tháng. <see cref="Due"/> là số thói quen đến hạn (không tính bỏ qua/đóng băng), <see cref="Done"/> là số đã xong.</summary>
public sealed record CalendarCell(DateOnly Day, bool InMonth, bool IsToday, bool IsFuture, int Due, int Done)
{
    /// <summary>Tỷ lệ hoàn thành 0..1; 0 khi không có gì đến hạn.</summary>
    public double Fraction => Due == 0 ? 0 : Done / (double)Due;

    /// <summary>Có việc đến hạn và đã xong hết.</summary>
    public bool IsComplete => Due > 0 && Done >= Due;
}

/// <summary>Lưới tháng theo tuần (mỗi tuần 7 ô) cùng tổng kết của tháng.</summary>
public sealed record MonthGrid(DateOnly Month, IReadOnlyList<IReadOnlyList<CalendarCell>> Weeks, int PerfectDays, int ActiveDays, double AverageFraction);

/// <summary>
/// Dựng lịch tháng từ thói quen và nhật ký: mỗi ngày có bao nhiêu việc đến hạn và đã xong. Thuần logic, không phụ thuộc giao diện.
/// Thói quen cần bỏ (Quit) không tính vì không có thao tác "xong"; ngày trước ngày bắt đầu, đã lưu trữ hoặc đã xóa không tính.
/// </summary>
public static class MonthCalendar
{
    public static MonthGrid Build(
        DateOnly anyDayInMonth,
        DayOfWeek weekStart,
        IReadOnlyList<Habit> habits,
        IReadOnlyDictionary<(Guid HabitId, DateOnly Day), DayLog> logs,
        IScheduleCalculator schedule,
        DateOnly today)
    {
        var first = new DateOnly(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var gridStart = PeriodCalendar.StartOfWeek(first, weekStart);
        var gridEnd = PeriodCalendar.StartOfWeek(last, weekStart).AddDays(6);
        var tracked = habits.Where(h => h.Kind != HabitKind.Quit && h.DeletedAt is null).ToList();

        var weeks = new List<IReadOnlyList<CalendarCell>>();
        var perfect = 0;
        var active = 0;
        var fractionSum = 0.0;
        var fractionDays = 0;

        for (var weekFirst = gridStart; weekFirst <= gridEnd; weekFirst = weekFirst.AddDays(7))
        {
            var row = new List<CalendarCell>(7);

            for (var i = 0; i < 7; i++)
            {
                var day = weekFirst.AddDays(i);
                var due = 0;
                var done = 0;

                if (day <= today)
                {
                    foreach (var habit in tracked)
                    {
                        if (day < habit.StartDate || (habit.ArchivedAt is { } archived && DateOnly.FromDateTime(archived) <= day) || !schedule.IsScheduled(habit, day))
                        {
                            continue;
                        }

                        logs.TryGetValue((habit.Id, day), out var log);

                        if (log is { Status: DayStatus.Skipped or DayStatus.Frozen })
                        {
                            continue;
                        }

                        due++;

                        if (log is { Status: DayStatus.Done })
                        {
                            done++;
                        }
                    }
                }

                var cell = new CalendarCell(day, day.Month == first.Month && day.Year == first.Year, day == today, day > today, due, done);
                row.Add(cell);

                if (cell.InMonth && due > 0)
                {
                    active++;
                    fractionSum += cell.Fraction;
                    fractionDays++;

                    if (cell.IsComplete)
                    {
                        perfect++;
                    }
                }
            }

            weeks.Add(row);
        }

        return new MonthGrid(first, weeks, perfect, active, fractionDays == 0 ? 0 : fractionSum / fractionDays);
    }
}
