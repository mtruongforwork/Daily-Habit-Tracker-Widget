using System.Text.Json;
using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Focus;
using HabitGrid.Presentation.Services;

namespace HabitGrid.Presentation.Tests;

public class FocusPageModelTests
{
    private sealed class FakeCommands : IFocusCommands
    {
        public List<string> Log { get; } = [];

        public Task StartAsync(Guid? habitId, int workMinutes, FocusMode mode, int breakMinutes)
        {
            Log.Add($"start {workMinutes}/{breakMinutes}/{mode}");
            return Task.CompletedTask;
        }

        public Task ToggleAsync()
        {
            Log.Add("toggle");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Log.Add("stop");
            return Task.CompletedTask;
        }
    }

    private static FocusPageModel Model(AppFixture f, FakeCommands commands) =>
        new(new FocusService(f.Habits, f.Logs, f.Focus, f.Days, f.Settings), f.Settings, f.Days, commands);

    private static readonly DateTime Now = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NoSavedState_IsIdle()
    {
        using var f = new AppFixture();
        var model = Model(f, new FakeCommands());

        (await model.DisplayAsync(Now)).Should().Be(FocusDisplay.Idle);
    }

    [Fact]
    public async Task SavedWorkState_ShowsRemainingAndProgress_AtAnyTime()
    {
        using var f = new AppFixture();
        var timer = new FocusTimer(FocusMode.Pomodoro, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), null);
        timer.Start(Now);
        await f.Settings.SetAsync("focus.state", JsonSerializer.Serialize(timer.State));
        var model = Model(f, new FakeCommands());

        var display = await model.DisplayAsync(Now.AddMinutes(10));

        display.Phase.Should().Be(FocusPhase.Work);
        display.Running.Should().BeTrue();
        display.Remaining!.Value.TotalMinutes.Should().BeApproximately(15, 0.01);
        display.Fraction.Should().BeApproximately(0.4, 0.001);
    }

    [Fact]
    public async Task Paused_IsReportedAsPaused()
    {
        using var f = new AppFixture();
        var timer = new FocusTimer(FocusMode.Countdown, TimeSpan.FromMinutes(30), TimeSpan.Zero, null);
        timer.Start(Now);
        timer.Pause(Now.AddMinutes(5));
        await f.Settings.SetAsync("focus.state", JsonSerializer.Serialize(timer.State));

        var display = await Model(f, new FakeCommands()).DisplayAsync(Now.AddMinutes(20));

        display.IsPaused.Should().BeTrue();
        display.Elapsed.TotalMinutes.Should().BeApproximately(5, 0.01);
    }

    [Fact]
    public async Task Stopwatch_HasNoRemainingAndNoFraction()
    {
        using var f = new AppFixture();
        var timer = new FocusTimer(FocusMode.Stopwatch, TimeSpan.Zero, TimeSpan.Zero, null);
        timer.Start(Now);
        await f.Settings.SetAsync("focus.state", JsonSerializer.Serialize(timer.State));

        var display = await Model(f, new FakeCommands()).DisplayAsync(Now.AddMinutes(7));

        display.Remaining.Should().BeNull();
        display.Fraction.Should().Be(0);
        display.Elapsed.TotalMinutes.Should().BeApproximately(7, 0.01);
    }

    [Fact]
    public async Task Start_SendsTheCommand_AndShowsAnOptimisticTimerUntilTheHostSavesState()
    {
        using var f = new AppFixture();
        var commands = new FakeCommands();
        var model = Model(f, commands);

        await model.StartAsync(new FocusChoice(FocusMode.Pomodoro, 25, 5, null), Now);

        commands.Log.Should().Equal("start 25/5/Pomodoro");
        var early = await model.DisplayAsync(Now.AddSeconds(2));
        early.Phase.Should().Be(FocusPhase.Work);
        early.Remaining!.Value.TotalSeconds.Should().BeApproximately(25 * 60 - 2, 0.5);

        // Quá cửa sổ chờ mà host vẫn chưa ghi trạng thái: coi như chưa chạy.
        (await model.DisplayAsync(Now.AddMinutes(1))).Phase.Should().Be(FocusPhase.Idle);
    }

    [Fact]
    public async Task Start_ClampsOutOfRangeValues()
    {
        using var f = new AppFixture();
        var commands = new FakeCommands();
        var model = Model(f, commands);

        await model.StartAsync(new FocusChoice(FocusMode.Countdown, 0, 999, null), Now);

        commands.Log.Should().Equal("start 1/120/Countdown");
    }

    [Fact]
    public async Task ToggleAndStop_ForwardTheCommands()
    {
        using var f = new AppFixture();
        var commands = new FakeCommands();
        var model = Model(f, commands);

        await model.ToggleAsync(Now);
        await model.StopAsync();

        commands.Log.Should().Equal("toggle", "stop");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void ParseChoice_FallsBackToDefaults(string? json)
    {
        FocusPageModel.ParseChoice(json).Should().Be(FocusChoice.Default);
    }

    [Fact]
    public void ParseChoice_ReadsTheWidgetPreferences_AndRejectsBadValues()
    {
        var habit = Guid.NewGuid();
        var good = FocusPageModel.ParseChoice($"{{\"Mode\":1,\"WorkMinutes\":50,\"BreakMinutes\":10,\"HabitId\":\"{habit}\"}}");
        good.Should().Be(new FocusChoice(FocusMode.Countdown, 50, 10, habit));

        FocusPageModel.ParseChoice("{\"Mode\":9,\"WorkMinutes\":0,\"BreakMinutes\":-3}").Should().Be(FocusChoice.Default);
    }

    [Fact]
    public async Task Load_ComputesStatsFromHistory_AndListsCandidates()
    {
        using var f = new AppFixture();
        await f.AddAsync("Đọc sách", HabitKind.Duration, target: 30);
        await f.Focus.AddAsync(new FocusRecord(Guid.NewGuid(), null, Now.AddHours(-2), Now.AddHours(-1), 25, FocusMode.Pomodoro));
        var model = Model(f, new FakeCommands());

        await model.LoadAsync();

        model.Candidates.Should().ContainSingle(h => h.Name == "Đọc sách");
        model.Stats.Sessions.Should().Be(1);
        model.Stats.TotalMinutes.Should().Be(25);
        model.Recent.Should().ContainSingle();
    }
}
