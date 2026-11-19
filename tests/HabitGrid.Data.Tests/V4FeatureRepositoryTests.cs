using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Data;
using HabitGrid.Data.V3;

namespace HabitGrid.Data.Tests;

public sealed class V4FeatureRepositoryTests
{
    [Fact]
    public async Task HabitRepository_RoundTrips_SkipWeekends_And_AreaId()
    {
        using var db = HabitGridV3.OpenInMemory();
        var clock = new HlcClock("test");
        var areas = new HabitAreaRepository(db, clock);
        var habits = new HabitRepository(db, clock);

        var area = new HabitArea(Guid.CreateVersion7(), "Work", "💼", "#3B82F6");
        await areas.UpsertAsync(area);

        var habit = new Habit
        {
            Name = "Standup",
            Kind = HabitKind.Dual,
            SkipWeekends = true,
            AreaId = area.Id
        };
        await habits.InsertAsync(habit);

        var loaded = await habits.GetByIdAsync(habit.Id);
        loaded.Should().NotBeNull();
        loaded!.Kind.Should().Be(HabitKind.Dual);
        loaded.SkipWeekends.Should().BeTrue();
        loaded.AreaId.Should().Be(area.Id);

        // Deleting area clears area_id on the habit.
        await areas.DeleteAsync(area.Id);
        var afterDelete = await habits.GetByIdAsync(habit.Id);
        afterDelete!.AreaId.Should().BeNull();
    }

    [Fact]
    public async Task QuickTodo_And_RewardRepositories_WorkAndEraseCleanly()
    {
        using var db = HabitGridV3.OpenInMemory();
        var clock = new HlcClock("test");
        var todos = new QuickTodoRepository(db, clock);
        var rewards = new RewardRepository(db, clock);

        var day = new DateOnly(2026, 11, 20);
        var todo = new QuickTodo(Guid.CreateVersion7(), "Buy coffee beans", day, false, DateTime.UtcNow);
        await todos.InsertAsync(todo);
        await todos.SetDoneAsync(todo.Id, true, DateTime.UtcNow);

        var list = await todos.GetForDayAsync(day);
        list.Should().ContainSingle(t => t.Id == todo.Id && t.IsDone);

        var reward = new CustomReward(Guid.CreateVersion7(), "1 episode", "📺", 30);
        await rewards.UpsertAsync(reward);
        await rewards.RecordRedemptionAsync(new RewardRedemption(Guid.CreateVersion7(), reward.Id, reward.Title, reward.CostCoins, DateTime.UtcNow));

        (await rewards.GetAllAsync()).Should().ContainSingle(r => r.Title == "1 episode");
        (await rewards.GetRecentRedemptionsAsync()).Should().ContainSingle(r => r.RewardId == reward.Id);

        await DataEraser.EraseAllAsync(db);
        (await todos.GetForDayAsync(day)).Should().BeEmpty();
        (await rewards.GetAllAsync()).Should().BeEmpty();
        (await rewards.GetRecentRedemptionsAsync()).Should().BeEmpty();
    }
}
