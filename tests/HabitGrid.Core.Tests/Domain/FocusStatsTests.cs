using FluentAssertions;
using HabitGrid.Core.Domain.Focus;
using Xunit;

namespace HabitGrid.Core.Tests.Domain;

public class FocusStatsTests
{
    private static readonly DateOnly Today = new(2026, 11, 11); // thứ Tư

    // Giờ địa phương giữa trưa để ngày không lệch do múi giờ.
    private static FocusRecord Record(DateOnly day, int minutes) =>
        new(Guid.NewGuid(), null, day.ToDateTime(new TimeOnly(11, 0)).ToUniversalTime(), day.ToDateTime(new TimeOnly(12, 0)).ToUniversalTime(), minutes, FocusMode.Pomodoro);

    [Fact]
    public void Empty_IsAllZero_WithSevenDays()
    {
        var stats = FocusStats.Compute([], Today);

        stats.TodayMinutes.Should().Be(0);
        stats.TotalMinutes.Should().Be(0);
        stats.Sessions.Should().Be(0);
        stats.BestDayMinutes.Should().Be(0);
        stats.Last7Days.Should().HaveCount(7);
        stats.Last7Days[^1].Day.Should().Be(Today);
        stats.Last7Days[0].Day.Should().Be(Today.AddDays(-6));
    }

    [Fact]
    public void Sums_TodayWeekAndTotal()
    {
        var records = new[]
        {
            Record(Today, 25), Record(Today, 25),
            Record(Today.AddDays(-1), 50), // thứ Ba, cùng tuần
            Record(Today.AddDays(-3), 40), // chủ nhật tuần trước
            Record(Today.AddDays(-30), 60)
        };

        var stats = FocusStats.Compute(records, Today);

        stats.TodayMinutes.Should().Be(50);
        stats.WeekMinutes.Should().Be(100);
        stats.TotalMinutes.Should().Be(200);
        stats.Sessions.Should().Be(5);
        stats.BestDayMinutes.Should().Be(60);
    }

    [Fact]
    public void Last7Days_HasMinutesPerDay_OldestFirst_AndDropsOlderDays()
    {
        var stats = FocusStats.Compute([Record(Today, 10), Record(Today.AddDays(-6), 20), Record(Today.AddDays(-7), 99)], Today);

        stats.Last7Days.Select(d => d.Minutes).Should().Equal(20, 0, 0, 0, 0, 0, 10);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(45, 0, 45)]
    [InlineData(65, 1, 5)]
    [InlineData(600, 10, 0)]
    public void Split_GivesHoursAndMinutes(int total, int hours, int minutes)
    {
        FocusStats.Split(total).Should().Be((hours, minutes));
    }
}
