using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Calendar;
using Xunit;

namespace HabitGrid.Core.Tests.Domain;

public class MonthCalendarTests
{
    private static readonly IScheduleCalculator Schedule = new ScheduleCalculator();

    private static Habit Daily(string name, DateOnly start, HabitKind kind = HabitKind.Boolean) =>
        new() { Name = name, StartDate = start, Kind = kind };

    private static Dictionary<(Guid, DateOnly), DayLog> Logs(params (Habit Habit, DateOnly Day, DayStatus Status)[] entries) =>
        entries.ToDictionary(e => (e.Habit.Id, e.Day), e => new DayLog { HabitId = e.Habit.Id, Day = e.Day, Status = e.Status, Value = 1 });

    [Fact]
    public void Weeks_AreFullAndStartOnTheChosenDay()
    {
        var grid = MonthCalendar.Build(new DateOnly(2026, 11, 15), DayOfWeek.Monday, [], new Dictionary<(Guid, DateOnly), DayLog>(), Schedule, new DateOnly(2026, 11, 11));

        grid.Weeks.Should().OnlyContain(w => w.Count == 7);
        grid.Weeks[0][0].Day.DayOfWeek.Should().Be(DayOfWeek.Monday);
        grid.Weeks.SelectMany(w => w).Count(c => c.InMonth).Should().Be(30);
        grid.Month.Should().Be(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public void CountsDueAndDone_AndIgnoresTheFuture()
    {
        var today = new DateOnly(2026, 11, 11);
        var gym = Daily("Gym", new DateOnly(2026, 11, 1));
        var read = Daily("Read", new DateOnly(2026, 11, 1));
        var logs = Logs((gym, new DateOnly(2026, 11, 10), DayStatus.Done), (read, new DateOnly(2026, 11, 10), DayStatus.Done), (gym, new DateOnly(2026, 11, 9), DayStatus.Done));

        var grid = MonthCalendar.Build(today, DayOfWeek.Monday, [gym, read], logs, Schedule, today);
        var cells = grid.Weeks.SelectMany(w => w).ToDictionary(c => c.Day);

        cells[new DateOnly(2026, 11, 10)].IsComplete.Should().BeTrue();
        cells[new DateOnly(2026, 11, 9)].Should().Match<CalendarCell>(c => c.Due == 2 && c.Done == 1);
        cells[new DateOnly(2026, 11, 9)].Fraction.Should().Be(0.5);
        cells[new DateOnly(2026, 11, 20)].Due.Should().Be(0);
        cells[new DateOnly(2026, 11, 20)].IsFuture.Should().BeTrue();
        cells[today].IsToday.Should().BeTrue();
    }

    [Fact]
    public void SkippedDays_AreNotDue()
    {
        var today = new DateOnly(2026, 11, 11);
        var habit = Daily("Gym", new DateOnly(2026, 11, 1));
        var logs = Logs((habit, new DateOnly(2026, 11, 5), DayStatus.Skipped), (habit, new DateOnly(2026, 11, 6), DayStatus.Frozen));

        var grid = MonthCalendar.Build(today, DayOfWeek.Monday, [habit], logs, Schedule, today);
        var cells = grid.Weeks.SelectMany(w => w).ToDictionary(c => c.Day);

        cells[new DateOnly(2026, 11, 5)].Due.Should().Be(0);
        cells[new DateOnly(2026, 11, 6)].Due.Should().Be(0);
        cells[new DateOnly(2026, 11, 7)].Due.Should().Be(1);
    }

    [Fact]
    public void DaysBeforeTheStartDate_AndQuitHabits_AreNotCounted()
    {
        var today = new DateOnly(2026, 11, 11);
        var late = Daily("New", new DateOnly(2026, 11, 8));
        var quit = Daily("Smoking", new DateOnly(2026, 11, 1), HabitKind.Quit);

        var grid = MonthCalendar.Build(today, DayOfWeek.Monday, [late, quit], new Dictionary<(Guid, DateOnly), DayLog>(), Schedule, today);
        var cells = grid.Weeks.SelectMany(w => w).ToDictionary(c => c.Day);

        cells[new DateOnly(2026, 11, 7)].Due.Should().Be(0);
        cells[new DateOnly(2026, 11, 8)].Due.Should().Be(1);
    }

    [Fact]
    public void Summary_CountsPerfectAndActiveDaysInsideTheMonthOnly()
    {
        var today = new DateOnly(2026, 11, 4);
        var habit = Daily("Gym", new DateOnly(2026, 11, 1));
        var logs = Logs((habit, new DateOnly(2026, 11, 1), DayStatus.Done), (habit, new DateOnly(2026, 11, 2), DayStatus.Done));

        var grid = MonthCalendar.Build(today, DayOfWeek.Monday, [habit], logs, Schedule, today);

        grid.ActiveDays.Should().Be(4);
        grid.PerfectDays.Should().Be(2);
        grid.AverageFraction.Should().BeApproximately(0.5, 1e-9);
    }
}
