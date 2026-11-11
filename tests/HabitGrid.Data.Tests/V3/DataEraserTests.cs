using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Data.V3;

namespace HabitGrid.Data.Tests.V3;

public class DataEraserTests
{
    [Fact]
    public async Task EraseAll_RemovesHabitsLogsSettingsAndOutbox_ButKeepsTheSchema()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        await f.Habits.InsertAsync(habit);
        await f.DayLogs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = new DateOnly(2026, 2, 1), Value = 1, Status = DayStatus.Done });
        f.Execute("INSERT INTO settings (key, value) VALUES ('theme', 'dark');");

        f.Scalar("SELECT COUNT(*) FROM habits;").Should().BeGreaterThan(0);
        f.OutboxCount.Should().BeGreaterThan(0);

        var removed = await DataEraser.EraseAllAsync(f.Database);

        removed.Should().BeGreaterThan(0);
        foreach (var table in new[] { "habits", "day_logs", "settings", "sync_outbox", "reminders", "routines", "profile", "achievements" })
        {
            f.Scalar($"SELECT COUNT(*) FROM {table};").Should().Be(0, table);
        }

        // Cấu trúc còn nguyên: ghi lại được ngay.
        await f.Habits.InsertAsync(V3Fixture.NewHabit("Mới"));
        f.Scalar("SELECT COUNT(*) FROM habits;").Should().Be(1);
    }

    [Fact]
    public async Task EraseAll_OnAnEmptyDatabase_IsHarmless()
    {
        using var f = new V3Fixture();
        (await DataEraser.EraseAllAsync(f.Database)).Should().Be(0);
    }
}
