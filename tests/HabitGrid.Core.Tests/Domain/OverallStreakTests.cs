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
    public void Longest_FindsTheBestRunInHistory()
    {
        // 10-8 ngày trước: 3 ngày liền; hôm nay và hôm qua: 2 ngày liền.
        OverallStreak.Longest([Log(10), Log(9), Log(8), Log(1), Log(0)]).Should().Be(3);
    }

    [Fact]
    public void Longest_BridgesFrozenDaysWithoutCountingThem()
    {
        OverallStreak.Longest([Log(5), Log(4, DayStatus.Frozen), Log(3), Log(1)]).Should().Be(2);
    }

    [Fact]
    public void Longest_WithNoDoneDays_IsZero() => OverallStreak.Longest([Log(1, DayStatus.Skipped)]).Should().Be(0);

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
