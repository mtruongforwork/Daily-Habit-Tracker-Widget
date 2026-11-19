using FluentAssertions;
using HabitGrid.Core.Domain;

namespace HabitGrid.Core.Tests;

public sealed class V4FeaturesTests
{
    [Fact]
    public void SkipWeekends_ExcludesSaturdayAndSunday_AndPreservesStreakAcrossWeekend()
    {
        var habit = new Habit
        {
            Name = "Work deep focus",
            Kind = HabitKind.Boolean,
            StartDate = new DateOnly(2026, 11, 13), // Friday
            SkipWeekends = true
        };

        var schedule = new ScheduleCalculator();
        schedule.IsScheduled(habit, new DateOnly(2026, 11, 13)).Should().BeTrue();  // Fri
        schedule.IsScheduled(habit, new DateOnly(2026, 11, 14)).Should().BeFalse(); // Sat
        schedule.IsScheduled(habit, new DateOnly(2026, 11, 15)).Should().BeFalse(); // Sun
        schedule.IsScheduled(habit, new DateOnly(2026, 11, 16)).Should().BeTrue();  // Mon

        var logs = new List<DayLog>
        {
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 13), Value = 1, Status = DayStatus.Done },
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 16), Value = 1, Status = DayStatus.Done }
        };

        var streak = new StreakCalculator(DayOfWeek.Monday, schedule).Calculate(habit, logs, new DateOnly(2026, 11, 16));
        streak.Current.Should().Be(2);
        streak.Best.Should().Be(2);
    }

    [Fact]
    public void DualHabit_HasEffectiveTargetOne()
    {
        var habit = new Habit { Name = "Mood & snacks", Kind = HabitKind.Dual, Target = new HabitTarget(5) };
        habit.EffectiveTarget.Should().Be(1);
    }

    [Fact]
    public void RewardWallet_ComputesGoalBonus_ClampsAtZero_AndRedeems()
    {
        RewardWallet.EarnForCheckIn(isGoalOfDay: false).Should().Be(10);
        RewardWallet.EarnForCheckIn(isGoalOfDay: true).Should().Be(20);
        GoalOfTheDayRules.ApplyXpBonus(15, isGoal: true).Should().Be(30);

        RewardWallet.ApplyDelta(2, -10).Should().Be(0);
        RewardWallet.CanRedeem(25, 30).Should().BeFalse();
        RewardWallet.CanRedeem(30, 30).Should().BeTrue();
        RewardWallet.Redeem(50, 30).Should().Be(20);

        RewardWallet.IsValidReward("Watch 1 episode", 25).Should().BeTrue();
        RewardWallet.IsValidReward("", 25).Should().BeFalse();
        RewardWallet.IsValidReward("Too cheap", 1).Should().BeFalse();
    }

    [Fact]
    public void NeverMissTwice_DetectsYesterdayMissAfterStreak()
    {
        var habit = new Habit
        {
            Name = "Read",
            Kind = HabitKind.Boolean,
            StartDate = new DateOnly(2026, 11, 10)
        };
        var today = new DateOnly(2026, 11, 13);
        var logs = new List<DayLog>
        {
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 11), Value = 1, Status = DayStatus.Done }
            // 2026-11-12 missed!
        };

        GentleStreakRules.IsAtRiskForSecondMiss(habit, logs, today).Should().BeTrue();
        GentleStreakRules.FindRepairableMissedDay(habit, logs, today).Should().Be(new DateOnly(2026, 11, 12));

        // Once today is completed, no longer at risk for second miss.
        logs.Add(new DayLog { HabitId = habit.Id, Day = today, Value = 1, Status = DayStatus.Done });
        GentleStreakRules.IsAtRiskForSecondMiss(habit, logs, today).Should().BeFalse();
    }
}
