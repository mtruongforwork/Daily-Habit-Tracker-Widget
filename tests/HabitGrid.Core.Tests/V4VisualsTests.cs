using FluentAssertions;
using HabitGrid.Core.Domain;

namespace HabitGrid.Core.Tests;

public sealed class V4VisualsTests
{
    [Fact]
    public void MatrixBoardBuilder_DeepensIntensityAlongConsecutiveRun()
    {
        var habit = new Habit { Name = "Read", StartDate = new DateOnly(2026, 11, 1) };
        var today = new DateOnly(2026, 11, 5);
        var logs = new List<DayLog>
        {
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 2), Value = 1, Status = DayStatus.Done },
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 3), Value = 1, Status = DayStatus.Done },
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 4), Value = 1, Status = DayStatus.Done }
        };

        var row = MatrixBoardBuilder.BuildRow(habit, logs, today, 5);
        row.Should().HaveCount(5);
        row[0].Intensity.Should().Be(0.0); // Nov 1: None
        row[1].Intensity.Should().BeApproximately(0.40, 0.01); // Nov 2: run 1
        row[2].Intensity.Should().BeApproximately(0.50, 0.01); // Nov 3: run 2
        row[3].Intensity.Should().BeApproximately(0.60, 0.01); // Nov 4: run 3
        row[4].ConsecutiveRun.Should().Be(0); // Nov 5: None
    }

    [Fact]
    public void StreakTimelineBuilder_FindsTopRunsOrderedByLength()
    {
        var habit = new Habit { Name = "Run", StartDate = new DateOnly(2026, 11, 1) };
        var today = new DateOnly(2026, 11, 10);
        var logs = new List<DayLog>
        {
            // Run 1: Nov 1..2 (len 2)
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 1), Value = 1, Status = DayStatus.Done },
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 2), Value = 1, Status = DayStatus.Done },
            // Run 2: Nov 6..9 (len 4, current)
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 6), Value = 1, Status = DayStatus.Done },
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 7), Value = 1, Status = DayStatus.Done },
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 8), Value = 1, Status = DayStatus.Done },
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 9), Value = 1, Status = DayStatus.Done }
        };

        var runs = StreakTimelineBuilder.BuildTopRuns(habit, logs, today);
        runs.Should().HaveCount(2);
        runs[0].Length.Should().Be(4);
        runs[0].IsCurrent.Should().BeTrue();
        runs[1].Length.Should().Be(2);
        runs[1].IsCurrent.Should().BeFalse();
    }

    [Fact]
    public void TaskUrgencyEvaluator_EscalatesWithMissedDays()
    {
        var habit = new Habit { Name = "Meditate", StartDate = new DateOnly(2026, 11, 1) };
        var today = new DateOnly(2026, 11, 10);
        var logs = new List<DayLog>
        {
            new() { HabitId = habit.Id, Day = new DateOnly(2026, 11, 5), Value = 1, Status = DayStatus.Done }
        };

        var urgency = TaskUrgencyEvaluator.Evaluate(habit, logs, today);
        urgency.Level.Should().Be(UrgencyLevel.Neglected);
        urgency.MissedScheduledDays.Should().Be(4);
        urgency.RevivalBonusXp.Should().Be(15);
    }

    [Fact]
    public void MochiRoomTheme_AdaptsToTimeAndAmbientSound()
    {
        var morning = MochiRoomTheme.For(new TimeOnly(8, 0), "Rain");
        morning.TimeSlot.Should().Be(RoomTimeOfDay.Morning);
        morning.WeatherEmoji.Should().Be("🌧️");

        var night = MochiRoomTheme.For(new TimeOnly(23, 0), null);
        night.TimeSlot.Should().Be(RoomTimeOfDay.Night);
        night.WeatherEmoji.Should().BeNull();
    }
}
