using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;

namespace HabitGrid.Presentation.Tests;

public class CheckInServiceTests
{
    private static readonly DateOnly Day = AppFixture.Today;

    [Fact]
    public async Task ConcurrentCheckInsFromSeparateRepositories_KeepEveryIncrement()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Count", kind: HabitKind.Count, target: 20);
        var second = new HabitGrid.Data.V3.DayLogRepository(f.Database, new HlcClock("widget"));
        var app = new CheckInService(f.Logs);
        var widget = new CheckInService(second);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var updates = Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
        {
            await start.Task;
            await (i % 2 == 0 ? app : widget).ToggleAsync(habit.Id, Day);
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(updates);

        (await f.Logs.GetAsync(habit.Id, Day))!.Value.Should().Be(12);
    }

    [Fact]
    public async Task Boolean_TogglesOnAndOff()
    {
        using var f = new AppFixture();
        var h = await f.AddAsync("A");
        var svc = new CheckInService(f.Logs);

        (await svc.ToggleAsync(h.Id, Day))!.Status.Should().Be(DayStatus.Done);
        (await f.Logs.GetAsync(h.Id, Day)).Should().NotBeNull();

        (await svc.ToggleAsync(h.Id, Day)).Should().BeNull();
        (await f.Logs.GetAsync(h.Id, Day)).Should().BeNull();
    }

    [Fact]
    public async Task Count_IncrementsThenResetsWhenTargetReached()
    {
        using var f = new AppFixture();
        var h = await f.AddAsync("Water", HabitKind.Count, target: 3);
        var svc = new CheckInService(f.Logs);

        (await svc.ToggleAsync(h.Id, Day))!.Value.Should().Be(1);
        (await svc.ToggleAsync(h.Id, Day))!.Value.Should().Be(2);
        (await svc.ToggleAsync(h.Id, Day))!.Should().Match<DayLog>(l => l.Value == 3 && l.Status == DayStatus.Done);
        (await svc.ToggleAsync(h.Id, Day)).Should().BeNull("đã đủ mục tiêu, bấm nữa là xóa");
    }

    [Fact]
    public async Task Duration_OneClickCompletes_SecondClears()
    {
        using var f = new AppFixture();
        var h = await f.AddAsync("Read", HabitKind.Duration, target: 25);
        var svc = new CheckInService(f.Logs);

        (await svc.ToggleAsync(h.Id, Day))!.Value.Should().Be(25);
        (await svc.ToggleAsync(h.Id, Day)).Should().BeNull();
    }

    [Fact]
    public async Task Skipped_BecomesChecked_AndKeepsNote()
    {
        using var f = new AppFixture();
        var h = await f.AddAsync("A");
        await f.Logs.UpsertAsync(DayLogRules.Normalize(h, new DayLog { HabitId = h.Id, Day = Day, Status = DayStatus.Skipped, Note = "ốm" }));
        var svc = new CheckInService(f.Logs);

        var log = await svc.ToggleAsync(h.Id, Day);

        log!.Status.Should().Be(DayStatus.Done);
        log.Note.Should().Be("ốm");
    }

    [Fact]
    public async Task Quit_UnknownAndBeforeStart_AreIgnored()
    {
        using var f = new AppFixture();
        var quit = await f.AddAsync("No sugar", HabitKind.Quit);
        var normal = await f.AddAsync("A");
        var svc = new CheckInService(f.Logs);

        (await svc.ToggleAsync(quit.Id, Day)).Should().BeNull();
        (await svc.ToggleAsync(Guid.NewGuid(), Day)).Should().BeNull();
        (await svc.ToggleAsync(normal.Id, Day.AddDays(-5))).Should().BeNull();
        (await f.Logs.GetAsync(normal.Id, Day.AddDays(-5))).Should().BeNull();
    }
}
