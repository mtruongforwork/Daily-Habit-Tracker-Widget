using HabitGrid.Core.Models;

namespace HabitGrid.Core.Domain;

/// <summary>Xác định ngày nào "cần làm" theo tần suất.</summary>
public interface IScheduleCalculator
{
    /// <summary>
    /// True nếu <paramref name="day"/> là ngày có lịch. Với tần suất linh hoạt (X lần/tuần, X lần/tháng, mỗi N ngày)
    /// mọi ngày từ ngày bắt đầu đều được coi là có thể làm.
    /// </summary>
    bool IsScheduled(Habit habit, DateOnly day);

    /// <summary>Đếm số ngày có lịch trong khoảng [from, to] (đã giới hạn từ ngày bắt đầu).</summary>
    int CountScheduledDays(Habit habit, DateOnly from, DateOnly to);
}

public sealed class ScheduleCalculator : IScheduleCalculator
{
    public bool IsScheduled(Habit habit, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(habit);

        if (day < habit.StartDate)
        {
            return false;
        }

        if (habit.SkipWeekends && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        return habit.Frequency switch
        {
            HabitFrequency.WeekDays w => w.Days.IsInMask(day.DayOfWeek),
            _ => true
        };
    }

    public int CountScheduledDays(Habit habit, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(habit);

        var start = from < habit.StartDate ? habit.StartDate : from;
        var count = 0;

        for (var day = start; day <= to; day = day.AddDays(1))
        {
            if (IsScheduled(habit, day))
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>Tiện ích lịch: đầu tuần, đầu tháng, cửa sổ N ngày.</summary>
public static class PeriodCalendar
{
    public static DateOnly StartOfWeek(DateOnly day, DayOfWeek weekStart)
    {
        var diff = ((int)day.DayOfWeek - (int)weekStart + 7) % 7;
        return day.AddDays(-diff);
    }

    public static DateOnly StartOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    public static DateOnly EndOfMonth(DateOnly day) =>
        new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));

    /// <summary>Chỉ số cửa sổ chứa <paramref name="day"/> (có thể âm nếu trước mốc Anchor).</summary>
    public static int WindowIndex(DateOnly day, DateOnly anchor, int n)
    {
        var diff = day.DayNumber - anchor.DayNumber;
        return (int)Math.Floor(diff / (double)n);
    }

    public static DateOnly WindowStart(int index, DateOnly anchor, int n) => anchor.AddDays(index * n);
}
