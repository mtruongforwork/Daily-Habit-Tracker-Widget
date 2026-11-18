using FluentAssertions;
using HabitGrid.Core.Domain;
using Xunit;
using static HabitGrid.Core.Tests.Domain.DomainTestData;

namespace HabitGrid.Core.Tests.Domain;

public class HeatmapBuilderTests
{
    [Fact]
    public void Grid_HasRequestedWeeks_SevenRows_AndEndsInTheWeekOfToday()
    {
        var habit = NewHabit(start: Today.AddDays(-400));

        var data = HeatmapBuilder.Build(habit, [], Today, weeks: 52);

        data.Weeks.Should().HaveCount(52);
        data.Weeks.Should().OnlyContain(w => w.Count == 7);
        // 2026-10-22 là thứ Năm → tuần cuối bắt đầu thứ Hai 19/10 và kết thúc 25/10.
        data.Weeks[^1][0].Day.Should().Be(new DateOnly(2026, 10, 19));
        data.To.Should().Be(new DateOnly(2026, 10, 25));
        data.Weeks[0][0].Day.Should().Be(new DateOnly(2026, 10, 19).AddDays(-7 * 51));
        data.Cells.Select(c => c.Day).Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
    }

    [Fact]
    public void FutureCells_AreFlagged_AndTodayIsMarked()
    {
        var habit = NewHabit(start: Today.AddDays(-30));

        var data = HeatmapBuilder.Build(habit, [], Today, weeks: 4);

        data.Cells.Where(c => c.Day > Today).Should().OnlyContain(c => c.IsFuture && c.Level == 0);
        data.Cells.Single(c => c.IsToday).Day.Should().Be(Today);
        data.Cells.Count(c => c.IsFuture).Should().Be(3, "thứ Sáu, Bảy, Chủ nhật của tuần hiện tại");
    }

    [Fact]
    public void BeforeStartDate_IsFlagged_EvenWhenLogsExist()
    {
        var habit = NewHabit(start: Today.AddDays(-3));
        var logs = new[] { Done(habit, Today.AddDays(-10)) };

        var data = HeatmapBuilder.Build(habit, logs, Today, weeks: 3);

        data.Cells.Where(c => c.Day < habit.StartDate).Should().OnlyContain(c => c.IsBeforeStart && c.Level == 0);
    }

    [Fact]
    public void Boolean_DoneIsLevelFour_OthersZero()
    {
        var habit = NewHabit(start: Today.AddDays(-20));
        var logs = new[] { Done(habit, Today.AddDays(-1)), WithStatus(habit, Today.AddDays(-2), DayStatus.None) };

        var data = HeatmapBuilder.Build(habit, logs, Today, weeks: 2);

        data.Cells.Single(c => c.Day == Today.AddDays(-1)).Level.Should().Be(4);
        data.Cells.Single(c => c.Day == Today.AddDays(-2)).Level.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]   // 30%
    [InlineData(4, 2)]   // 40%
    [InlineData(6, 2)]   // 60%
    [InlineData(7, 3)]   // 70%
    [InlineData(9, 3)]   // 90%
    [InlineData(10, 4)]
    [InlineData(25, 4)]
    public void Count_LevelFollowsRatioToTarget(int value, int expectedLevel)
    {
        var habit = NewHabit(HabitKind.Count, target: 10, start: Today.AddDays(-20));
        var logs = value == 0 ? Array.Empty<DayLog>() : new[] { WithStatus(habit, Today.AddDays(-1), DayStatus.Partial, value) };

        var data = HeatmapBuilder.Build(habit, logs, Today, weeks: 2);

        data.Cells.Single(c => c.Day == Today.AddDays(-1)).Level.Should().Be(expectedLevel);
    }

    [Fact]
    public void Skipped_AndFrozen_AreZeroButKeepTheirStatus()
    {
        var habit = NewHabit(start: Today.AddDays(-20));
        var logs = new[]
        {
            WithStatus(habit, Today.AddDays(-1), DayStatus.Skipped),
            WithStatus(habit, Today.AddDays(-2), DayStatus.Frozen)
        };

        var data = HeatmapBuilder.Build(habit, logs, Today, weeks: 2);

        var skipped = data.Cells.Single(c => c.Day == Today.AddDays(-1));
        skipped.Level.Should().Be(0);
        skipped.Status.Should().Be(DayStatus.Skipped);
        data.Cells.Single(c => c.Day == Today.AddDays(-2)).Status.Should().Be(DayStatus.Frozen);
    }

    [Fact]
    public void Quit_CleanDaysAreFull_SlipDaysAreEmpty_AndNothingBeforeStart()
    {
        var habit = NewHabit(HabitKind.Quit, start: Today.AddDays(-5));
        var logs = new[] { WithStatus(habit, Today.AddDays(-2), DayStatus.Failed, 1) };

        var data = HeatmapBuilder.Build(habit, logs, Today, weeks: 2);

        data.Cells.Single(c => c.Day == Today.AddDays(-2)).Level.Should().Be(0);
        data.Cells.Single(c => c.Day == Today.AddDays(-3)).Level.Should().Be(4);
        data.Cells.Single(c => c.Day == Today).Level.Should().Be(4);
        data.Cells.Where(c => c.IsBeforeStart).Should().OnlyContain(c => c.Level == 0);
    }

    [Fact]
    public void NonScheduledDays_AreFlagged()
    {
        var habit = NewHabit(
            frequency: new HabitFrequency.WeekDays(HabitGrid.Core.Models.DayOfWeekMask.Monday),
            start: Today.AddDays(-30));

        var data = HeatmapBuilder.Build(habit, [], Today, weeks: 4);

        data.Cells.Where(c => !c.IsBeforeStart && c.Day.DayOfWeek != DayOfWeek.Monday).Should().OnlyContain(c => !c.IsScheduled);
        data.Cells.Where(c => !c.IsBeforeStart && c.Day.DayOfWeek == DayOfWeek.Monday).Should().OnlyContain(c => c.IsScheduled);
    }

    [Fact]
    public void WeekStartSunday_ShiftsRows()
    {
        var habit = NewHabit(start: Today.AddDays(-30));

        var data = HeatmapBuilder.Build(habit, [], Today, weeks: 3, DayOfWeek.Sunday);

        data.Weeks.Should().OnlyContain(w => w[0].Day.DayOfWeek == DayOfWeek.Sunday);
        data.Weeks[^1][0].Day.Should().Be(new DateOnly(2026, 10, 18));
    }

    [Fact]
    public void LatestLogWins_WhenDuplicatesExist()
    {
        var habit = NewHabit(start: Today.AddDays(-10));
        var older = Done(habit, Today.AddDays(-1)) with { UpdatedAt = new DateTime(2026, 10, 21, 8, 0, 0, DateTimeKind.Utc) };
        var newer = WithStatus(habit, Today.AddDays(-1), DayStatus.None) with { UpdatedAt = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc) };

        var data = HeatmapBuilder.Build(habit, [older, newer], Today, weeks: 2);

        data.Cells.Single(c => c.Day == Today.AddDays(-1)).Level.Should().Be(0);
    }

    [Fact]
    public void MonthLabels_MarkTheFirstColumnOfEachMonth()
    {
        var habit = NewHabit(start: Today.AddDays(-400));
        var data = HeatmapBuilder.Build(habit, [], Today, weeks: 52);

        var labels = HeatmapBuilder.MonthLabels(data);

        labels.Should().HaveCountGreaterThanOrEqualTo(12);
        labels.Select(l => l.Column).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        labels[0].Column.Should().BeLessThan(5);
        for (var i = 1; i < labels.Count; i++)
        {
            labels[i].Month.Should().NotBe(labels[i - 1].Month);
            (labels[i].Column - labels[i - 1].Column).Should().BeGreaterThanOrEqualTo(3, "hai nhãn tháng sát nhau sẽ đè chữ");
        }
    }

    [Fact]
    public void MonthLabels_HideAShortFirstMonth_SoLabelsDoNotOverlap()
    {
        // Tháng 10 chỉ có 2 cột ở đầu lưới: bỏ nhãn "Thg 10", nhãn đầu tiên là tháng 11 ở cột 2.
        var starts = Enumerable.Range(0, 10).Select(i => new DateOnly(2026, 10, 19).AddDays(7 * i)).ToList();

        var labels = HeatmapBuilder.MonthLabels(starts);

        labels.Select(l => (l.Column, l.Month)).Should().Equal((2, 11), (7, 12));
    }

    [Fact]
    public void MonthLabels_KeepAFirstMonthWithEnoughColumns()
    {
        var starts = Enumerable.Range(0, 8).Select(i => new DateOnly(2026, 10, 5).AddDays(7 * i)).ToList();

        HeatmapBuilder.MonthLabels(starts).First().Should().Be((0, 10, 2026));
    }

    [Fact]
    public void FilledCount_CountsNonEmptyCells()
    {
        var habit = NewHabit(start: Today.AddDays(-20));
        var logs = new[] { Done(habit, Today), Done(habit, Today.AddDays(-1)), Done(habit, Today.AddDays(-5)) };

        HeatmapBuilder.Build(habit, logs, Today, weeks: 4).FilledCount.Should().Be(3);
    }
}
