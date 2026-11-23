using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Gamification;
using HabitGrid.Core.Domain.Monetization;
using HabitGrid.Core.Domain.Reminders;
using HabitGrid.Core.Domain.Vacation;
using HabitGrid.Presentation.Services;

namespace HabitGrid.Presentation.Tests;

public class VacationTests
{
    private static readonly DateOnly Today = AppFixture.Today;

    private static VacationService Create(AppFixture f, bool plus = false)
    {
        var live = new LiveEntitlements();
        live.Set(plus);
        return new VacationService(f.Habits, f.Logs, f.Settings, f.Days, live);
    }

    [Theory]
    [InlineData(0, 3, false, VacationError.None)]
    [InlineData(-1, 3, false, VacationError.InPast)]
    [InlineData(3, 1, false, VacationError.EndBeforeStart)]
    [InlineData(0, 13, false, VacationError.None)]
    [InlineData(0, 14, false, VacationError.TooLong)]
    [InlineData(0, 14, true, VacationError.None)]
    [InlineData(0, 60, true, VacationError.TooLong)]
    public void Validate_FollowsTheRulesAndPlanLimits(int fromOffset, int toOffset, bool plus, VacationError expected) =>
        VacationRules.Validate(Today.AddDays(fromOffset), Today.AddDays(toOffset), Today, plus, null).Error.Should().Be(expected);

    [Fact]
    public void Validate_RejectsASecondVacationWhileOneIsActive() =>
        VacationRules.Validate(Today, Today.AddDays(2), Today, true, new VacationInfo(Today, Today.AddDays(5), [])).Error
            .Should().Be(VacationError.AlreadyActive);

    [Fact]
    public async Task Start_MarksScheduledDaysFrozen_ForAllHabits_ButNeverOverwritesDoneDays()
    {
        using var f = new AppFixture();
        var a = await f.AddAsync("A", startDaysAgo: 30);
        var b = await f.AddAsync("B", startDaysAgo: 30);
        await f.AddAsync("Quit", HabitKind.Quit, startDaysAgo: 30);
        await f.LogAsync(a, Today.AddDays(1), 1);
        var service = Create(f);

        var (check, marked) = await service.StartAsync(Today, Today.AddDays(3));

        check.IsValid.Should().BeTrue();
        marked.Should().Be(7, "A: 4 ngày trừ 1 ngày đã Done = 3; B: 4 ngày; thói quen cần bỏ không bị đụng tới");
        (await f.Logs.GetAsync(a.Id, Today.AddDays(1)))!.Status.Should().Be(DayStatus.Done);
        (await f.Logs.GetAsync(a.Id, Today.AddDays(2)))!.Status.Should().Be(DayStatus.Frozen);
        (await f.Logs.GetAsync(b.Id, Today))!.Status.Should().Be(DayStatus.Frozen);
        (await service.CurrentAsync())!.Days.Should().Be(4);
    }

    [Fact]
    public async Task OnlySelectedHabits_AreMarked()
    {
        using var f = new AppFixture();
        var a = await f.AddAsync("A", startDaysAgo: 30);
        var b = await f.AddAsync("B", startDaysAgo: 30);
        var service = Create(f);

        await service.StartAsync(Today, Today.AddDays(1), [a.Id]);

        (await f.Logs.GetAsync(a.Id, Today)).Should().NotBeNull();
        (await f.Logs.GetAsync(b.Id, Today)).Should().BeNull();
    }

    [Fact]
    public async Task OnlyQuitHabits_OrNoHabits_IsNothingToPause()
    {
        using var f = new AppFixture();
        await f.AddAsync("Quit", HabitKind.Quit, startDaysAgo: 5);

        var (check, marked) = await Create(f).StartAsync(Today, Today.AddDays(1));

        check.Error.Should().Be(VacationError.NothingToPause);
        marked.Should().Be(0);
    }

    [Fact]
    public async Task Vacation_KeepsTheStreakAlive_NoRemindersAndNoMissedDays()
    {
        using var f = new AppFixture();
        var h = await f.AddAsync("A", startDaysAgo: 30);
        for (var i = 1; i <= 5; i++) await f.LogAsync(h, Today.AddDays(-i), 1);
        h.Reminders = [new Reminder(new TimeOnly(9, 0))];
        await f.Habits.UpdateAsync(h);
        await Create(f).StartAsync(Today, Today.AddDays(2));

        var logs = await f.Logs.GetAllForHabitAsync(h.Id);
        var streak = new StreakCalculator().Calculate(h, logs, Today.AddDays(3));
        var plan = ReminderPlanner.Plan([h], logs, Today.ToDateTime(new TimeOnly(6, 0)), new ReminderSettings { StreakRescue = false });

        streak.Current.Should().Be(5, "nghỉ không đứt chuỗi và cũng không cộng");
        plan.Should().OnlyContain(p => DateOnly.FromDateTime(p.At) > Today.AddDays(2), "không nhắc trong những ngày nghỉ");
        XpEngine.Compute([h], logs, Today.AddDays(3)).Awards.Should().NotContain(a => a.Day > Today.AddDays(-1) && a.Kind == "done");
    }

    [Fact]
    public async Task QuickCheckInDuringAVacationDay_PreservesFrozenLog()
    {
        using var f = new AppFixture();
        var h = await f.AddAsync("A", startDaysAgo: 30);
        await Create(f).StartAsync(Today, Today.AddDays(2));

        var log = await new CheckInService(f.Logs).ToggleAsync(h.Id, Today);

        log!.Status.Should().Be(DayStatus.Frozen);
        (await f.Logs.GetAsync(h.Id, Today))!.Status.Should().Be(DayStatus.Frozen);
    }

    [Fact]
    public async Task EndEarly_ClearsFromTodayOnward_AndKeepsThePast()
    {
        using var f = new AppFixture();
        var h = await f.AddAsync("A", startDaysAgo: 30);
        var service = Create(f);
        f.Days.Today = Today.AddDays(-2);
        await service.StartAsync(Today.AddDays(-2), Today.AddDays(2));
        f.Days.Today = Today;

        var cleared = await service.EndAsync();

        cleared.Should().Be(3, "hôm nay và 2 ngày sau");
        (await f.Logs.GetAsync(h.Id, Today.AddDays(-2)))!.Status.Should().Be(DayStatus.Frozen, "quá khứ giữ nguyên");
        (await f.Logs.GetAsync(h.Id, Today.AddDays(-1)))!.Status.Should().Be(DayStatus.Frozen);
        (await f.Logs.GetAsync(h.Id, Today)).Should().BeNull();
        (await f.Logs.GetAsync(h.Id, Today.AddDays(2))).Should().BeNull();
        (await service.CurrentAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ExpiredVacation_IsForgotten()
    {
        using var f = new AppFixture();
        await f.AddAsync("A", startDaysAgo: 30);
        var service = Create(f);
        await service.StartAsync(Today, Today.AddDays(1));

        f.Days.Today = Today.AddDays(5);

        (await service.CurrentAsync()).Should().BeNull();
        (await f.Settings.GetAsync(VacationService.Key)).Should().BeNull();
    }

    [Fact]
    public async Task PlusAllowsLongerBreaks_ThanFree()
    {
        using var f = new AppFixture();
        await f.AddAsync("A", startDaysAgo: 30);

        (await Create(f, plus: false).StartAsync(Today, Today.AddDays(20))).Check.Error.Should().Be(VacationError.TooLong);
        (await Create(f, plus: true).StartAsync(Today, Today.AddDays(20))).Check.IsValid.Should().BeTrue();
    }

    [Fact]
    public void HabitsNotScheduledOrNotStarted_GetNoDays()
    {
        var late = new Habit { Name = "Mới", StartDate = Today.AddDays(2), SortKey = "a" };
        var vacation = new VacationInfo(Today, Today.AddDays(4), []);

        VacationRules.DaysToMark(late, vacation, new HashSet<DateOnly>()).Should().Equal(Today.AddDays(2), Today.AddDays(3), Today.AddDays(4));
    }
}
