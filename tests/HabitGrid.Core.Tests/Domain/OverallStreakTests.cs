using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Streaks;
using Xunit;

namespace HabitGrid.Core.Tests.Domain;

public class OverallStreakTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static DayLog Log(int daysAgo, DayStatus status = DayStatus.Done, Guid? habit = null) =>
        new() { HabitId = habit ?? Guid.NewGuid(), Day = Today.AddDays(-daysAgo), Status = status, Value = 1 };

    [Fact]
    public void NoLogs_IsZero() => OverallStreak.Compute([], Today).Should().Be(0);

    [Fact]
    public void CountsConsecutiveDays_IncludingToday()
    {
        OverallStreak.Compute([Log(0), Log(1), Log(2)], Today).Should().Be(3);
    }

    [Fact]
    public void NotCheckedToday_CountsUpToYesterday()
    {
        OverallStreak.Compute([Log(1), Log(2)], Today).Should().Be(2);
    }

    [Fact]
    public void ABrokenDay_EndsTheStreak()
    {
        OverallStreak.Compute([Log(0), Log(1), Log(3)], Today).Should().Be(2);
    }

    [Fact]
    public void FrozenDays_BridgeTheStreakButAreNotCounted()
    {
        OverallStreak.Compute([Log(0), Log(1, DayStatus.Frozen), Log(2)], Today).Should().Be(2);
    }

    [Fact]
    public void SeveralHabitsOnTheSameDay_CountOnce()
    {
        OverallStreak.Compute([Log(0), Log(0), Log(1), Log(1)], Today).Should().Be(2);
    }

    [Fact]
    public void SkippedDays_DoNotBridge()
    {
        OverallStreak.Compute([Log(0), Log(1, DayStatus.Skipped), Log(2)], Today).Should().Be(1);
    }
}
