using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Data;
using HabitGrid.Data.V3;
using HabitGrid.Presentation.Services;

namespace HabitGrid.Presentation.Tests;

public sealed class V4FeatureServicesTests
{
    [Fact]
    public async Task RewardService_EnforcesFreeLimit_AndRedeemsCoins()
    {
        using var db = HabitGridV3.OpenInMemory();
        var clock = new HlcClock("test");
        var repo = new RewardRepository(db, clock);
        var settings = new SettingsStore(db);
        var service = new RewardService(repo, settings, new FreeEntitlements());

        for (var i = 1; i <= 3; i++)
        {
            var (res, _) = await service.SaveRewardAsync(null, $"Reward {i}", "🎁", 20);
            res.Should().Be(RewardResult.Ok);
        }

        var (limitRes, _) = await service.SaveRewardAsync(null, "Reward 4", "🎁", 20);
        limitRes.Should().Be(RewardResult.LimitReached);

        await service.AddCoinsAsync(50);
        var first = (await service.GetRewardsAsync())[0];
        var (redeemRes, remaining) = await service.RedeemAsync(first.Id);
        redeemRes.Should().Be(RewardResult.Ok);
        remaining.Should().Be(30);
        (await service.GetRecentRedemptionsAsync()).Should().ContainSingle(r => r.RewardId == first.Id);
    }

    [Fact]
    public async Task GoalOfTheDayService_AppliesOnlyToSavedDate()
    {
        using var db = HabitGridV3.OpenInMemory();
        var service = new GoalOfTheDayService(new SettingsStore(db));
        var today = new DateOnly(2026, 11, 20);
        var habitId = Guid.CreateVersion7();

        await service.SetGoalAsync(habitId, today);
        (await service.GetGoalHabitIdAsync(today)).Should().Be(habitId);
        (await service.GetGoalHabitIdAsync(today.AddDays(1))).Should().BeNull();
    }

    [Fact]
    public async Task AreaService_EnforcesFreeLimit_AndQuickTodoAwardsCoins()
    {
        using var db = HabitGridV3.OpenInMemory();
        var clock = new HlcClock("test");
        var settings = new SettingsStore(db);
        var areaService = new AreaService(new HabitAreaRepository(db, clock), new FreeEntitlements());

        for (var i = 1; i <= 3; i++)
        {
            var (ok, _, _) = await areaService.SaveAsync(null, $"Area {i}", "🏷️", "#8B5CF6");
            ok.Should().BeTrue();
        }

        var (overOk, errKey, _) = await areaService.SaveAsync(null, "Area 4", "🏷️", "#8B5CF6");
        overOk.Should().BeFalse();
        errKey.Should().Be("area.err.limit");

        var rewardService = new RewardService(new RewardRepository(db, clock), settings, new FreeEntitlements());
        var todoService = new QuickTodoService(new QuickTodoRepository(db, clock), rewardService);
        var todo = await todoService.AddAsync("Send email", new DateOnly(2026, 11, 20));
        todo.Should().NotBeNull();

        await todoService.ToggleAsync(todo!);
        (await rewardService.GetBalanceAsync()).Should().Be(RewardWallet.QuickTodoCoins);
    }

    [Fact]
    public async Task GentleRepairService_RepairsMissedDayWithFrozenStatus()
    {
        using var db = HabitGridV3.OpenInMemory();
        var clock = new HlcClock("test");
        var habits = new HabitRepository(db, clock);
        var logs = new DayLogRepository(db, clock);
        var repair = new GentleRepairService(habits, logs);

        var habit = new Habit { Name = "Journal", StartDate = new DateOnly(2026, 11, 10) };
        await habits.InsertAsync(habit);
        await logs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = new DateOnly(2026, 11, 18), Value = 1, Status = DayStatus.Done });

        var repairedDay = await repair.RepairLatestAsync(habit.Id, new DateOnly(2026, 11, 20));
        repairedDay.Should().Be(new DateOnly(2026, 11, 19));

        var log = await logs.GetAsync(habit.Id, new DateOnly(2026, 11, 19));
        log.Should().NotBeNull();
        log!.Status.Should().Be(DayStatus.Frozen);
    }
}
