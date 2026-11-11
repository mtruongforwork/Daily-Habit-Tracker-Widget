using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;

namespace HabitGrid.Presentation.Tests;

public class CalendarModelTests
{
    private static CalendarModel Model(AppFixture f) => new(f.Habits, f.Logs, f.Days, f.Settings);

    [Fact]
    public async Task Load_DefaultsToTheCurrentMonthAndToday()
    {
        using var f = new AppFixture();
        await f.AddAsync("Gym", startDaysAgo: 30);
        var model = Model(f);

        await model.LoadAsync();

        model.Month.Should().Be(new DateOnly(2026, 10, 1));
        model.SelectedDay.Should().Be(AppFixture.Today);
        model.Grid.Should().NotBeNull();
        model.DayItems.Should().ContainSingle(i => i.Name == "Gym");
        model.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public async Task ReviewingAPastDay_CanCheckItIn_AndTheGridUpdates()
    {
        using var f = new AppFixture();
        var gym = await f.AddAsync("Gym", startDaysAgo: 30);
        var model = Model(f);
        var past = AppFixture.Today.AddDays(-3);

        await model.LoadAsync(selected: past);
        model.SelectedDay.Should().Be(past);

        await model.ToggleAsync(model.DayItems.Single());

        (await f.Logs.GetAsync(gym.Id, past))!.Status.Should().Be(DayStatus.Done);
        var cell = model.Grid!.Weeks.SelectMany(w => w).Single(c => c.Day == past);
        cell.IsComplete.Should().BeTrue();
        model.SelectedDay.Should().Be(past);
    }

    [Fact]
    public async Task CountHabit_CanBeAdjustedOnAPastDay()
    {
        using var f = new AppFixture();
        var water = await f.AddAsync("Water", HabitKind.Count, target: 3, startDaysAgo: 30);
        var model = Model(f);
        var past = AppFixture.Today.AddDays(-2);

        await model.LoadAsync(selected: past);
        await model.AdjustAsync(model.DayItems.Single(), 1);
        await model.AdjustAsync(model.DayItems.Single(), 1);

        (await f.Logs.GetAsync(water.Id, past))!.Value.Should().Be(2);
        model.Grid!.Weeks.SelectMany(w => w).Single(c => c.Day == past).IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task ShiftMonth_GoesBackButNeverPastTheCurrentMonth()
    {
        using var f = new AppFixture();
        await f.AddAsync("Gym", startDaysAgo: 90);
        var model = Model(f);
        await model.LoadAsync();

        await model.ShiftMonthAsync(-1);
        model.Month.Should().Be(new DateOnly(2026, 9, 1));
        model.CanGoNext.Should().BeTrue();
        model.SelectedDay.Should().Be(new DateOnly(2026, 9, 30), "chọn ngày cuối của tháng đã qua");

        await model.ShiftMonthAsync(1);
        await model.ShiftMonthAsync(1);
        model.Month.Should().Be(new DateOnly(2026, 10, 1));
    }
}
