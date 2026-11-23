using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.ViewModels;
using DayOfWeekMask = HabitGrid.Core.Models.DayOfWeekMask;

namespace HabitGrid.Presentation.Tests;

public class TodayViewModelTests
{
    [Fact]
    public async Task Load_WithNoHabits_IsEmpty()
    {
        using var f = new AppFixture();
        var vm = f.NewToday();

        await vm.LoadAsync();

        vm.IsEmpty.Should().BeTrue();
        vm.Groups.Should().BeEmpty();
        vm.TotalCount.Should().Be(0);
        vm.ProgressFraction.Should().Be(0);
        vm.AllDone.Should().BeFalse();
    }

    [Fact]
    public async Task Load_GroupsPendingHabitsByTimeOfDay_InFixedOrder()
    {
        using var f = new AppFixture();
        await f.AddAsync("Tối", time: TimeOfDay.Evening);
        await f.AddAsync("Sáng", time: TimeOfDay.Morning);
        await f.AddAsync("Bất kỳ", time: TimeOfDay.Anytime);
        await f.AddAsync("Sáng 2", time: TimeOfDay.Morning);
        var vm = f.NewToday();

        await vm.LoadAsync();

        vm.Groups.Select(g => g.TimeOfDay).Should().Equal(TimeOfDay.Morning, TimeOfDay.Evening, TimeOfDay.Anytime);
        vm.Groups[0].Items.Select(i => i.Name).Should().Equal("Sáng", "Sáng 2");
        vm.TotalCount.Should().Be(4);
        vm.DoneCount.Should().Be(0);
    }

    [Fact]
    public async Task Load_PutsUnscheduledHabitsInNotToday_AndExcludesThemFromProgress()
    {
        using var f = new AppFixture();
        await f.AddAsync("Hằng ngày");
        // Hôm nay là thứ Năm; thói quen chỉ thứ Hai.
        await f.AddAsync("Chỉ thứ Hai", frequency: new HabitFrequency.WeekDays(DayOfWeekMask.Monday));
        var vm = f.NewToday();

        await vm.LoadAsync();

        vm.NotToday.Select(i => i.Name).Should().Equal("Chỉ thứ Hai");
        vm.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task CheckIn_Boolean_TogglesAndUpdatesProgressStreakAndLists()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Đọc sách", startDaysAgo: 10);
        await f.LogAsync(habit, AppFixture.Today.AddDays(-1), 1);
        await f.LogAsync(habit, AppFixture.Today.AddDays(-2), 1);
        var vm = f.NewToday();
        await vm.LoadAsync();
        var item = vm.Groups.Single().Items.Single();
        item.Streak.Should().Be(2);
        item.AtRisk.Should().BeTrue();

        await vm.CheckInAsync(item);

        vm.Completed.Should().ContainSingle().Which.Name.Should().Be("Đọc sách");
        vm.Groups.Single().Items.Should().ContainSingle();
        vm.DoneCount.Should().Be(1);
        vm.AllDone.Should().BeTrue();
        vm.Completed[0].Streak.Should().Be(3);
        vm.Completed[0].AtRisk.Should().BeFalse();

        var saved = await f.Logs.GetAsync(habit.Id, AppFixture.Today);
        saved!.Status.Should().Be(DayStatus.Done);
        saved.CheckedAt.Should().NotBeNull();

        // Chạm lần nữa → bỏ check và xóa dòng.
        await vm.CheckInAsync(vm.Completed[0]);
        vm.Completed.Should().BeEmpty();
        (await f.Logs.GetAsync(habit.Id, AppFixture.Today)).Should().BeNull();
    }

    [Fact]
    public async Task CheckIn_Count_IncrementsUntilTarget_ThenMovesToCompleted()
    {
        using var f = new AppFixture();
        await f.AddAsync("Uống nước", HabitKind.Count, target: 3);
        var vm = f.NewToday();
        await vm.LoadAsync();

        var item = vm.Groups.Single().Items.Single();
        await vm.CheckInAsync(item);
        await vm.CheckInAsync(vm.Groups.Single().Items.Single());

        var partial = vm.Groups.Single().Items.Single();
        partial.Value.Should().Be(2);
        partial.Status.Should().Be(DayStatus.Partial);
        partial.Progress.Should().BeApproximately(2 / 3.0, 1e-9);
        vm.DoneCount.Should().Be(0);

        await vm.CheckInAsync(partial);

        vm.Completed.Should().ContainSingle().Which.Value.Should().Be(3);
        vm.DoneCount.Should().Be(1);
    }

    [Fact]
    public async Task Decrement_LowersCount_AndDroppingToZeroRemovesTheRow()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Uống nước", HabitKind.Count, target: 5);
        await f.LogAsync(habit, AppFixture.Today, 1);
        var vm = f.NewToday();
        await vm.LoadAsync();

        await vm.DecrementAsync(vm.Groups.Single().Items.Single());

        (await f.Logs.GetAsync(habit.Id, AppFixture.Today)).Should().BeNull();
        vm.Groups.Single().Items.Single().Value.Should().Be(0);
    }

    [Fact]
    public async Task CheckIn_Duration_TogglesBetweenZeroAndTargetMinutes()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Thiền", HabitKind.Duration, target: 20);
        var vm = f.NewToday();
        await vm.LoadAsync();

        await vm.CheckInAsync(vm.Groups.Single().Items.Single());

        (await f.Logs.GetAsync(habit.Id, AppFixture.Today))!.Value.Should().Be(20);
        vm.Completed.Should().ContainSingle();
    }

    [Fact]
    public async Task Quit_StaysInList_IsNotCountedInProgress_AndSlipResetsStreak()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Lướt điện thoại khuya", HabitKind.Quit);
        var vm = f.NewToday();
        await vm.LoadAsync();

        vm.TotalCount.Should().Be(0, "thói quen cần bỏ không tính vào tiến độ ngày");
        var item = vm.Groups.Single().Items.Single();
        item.Streak.Should().Be(1, "hôm nay là ngày đầu tiên giữ được");

        await vm.CheckInAsync(item); // ghi một lần lỡ

        var after = vm.Groups.Single().Items.Single();
        after.Value.Should().Be(1);
        after.Streak.Should().Be(0);
        (await f.Logs.GetAsync(habit.Id, AppFixture.Today))!.Status.Should().Be(DayStatus.Failed);

        await vm.DecrementAsync(after);

        vm.Groups.Single().Items.Single().Streak.Should().Be(1);
        (await f.Logs.GetAsync(habit.Id, AppFixture.Today)).Should().BeNull();
    }

    [Fact]
    public async Task Undo_RestoresPreviousState_IncludingNote()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Viết nhật ký");
        await f.Logs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = AppFixture.Today, Value = 0, Note = "ghi chú cũ", Status = DayStatus.None });
        var vm = f.NewToday();
        await vm.LoadAsync();

        await vm.CheckInAsync(vm.Groups.Single().Items.Single());
        vm.CanUndo.Should().BeTrue();
        (await f.Logs.GetAsync(habit.Id, AppFixture.Today))!.Note.Should().Be("ghi chú cũ", "ghi chú được giữ khi check-in");

        await vm.UndoAsync();

        var restored = await f.Logs.GetAsync(habit.Id, AppFixture.Today);
        restored!.Value.Should().Be(0);
        restored.Note.Should().Be("ghi chú cũ");
        vm.CanUndo.Should().BeFalse();
        vm.Completed.Should().BeEmpty();
    }

    [Fact]
    public async Task Undo_OfFirstCheckIn_DeletesTheRow()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Chạy bộ");
        var vm = f.NewToday();
        await vm.LoadAsync();

        await vm.CheckInAsync(vm.Groups.Single().Items.Single());
        await vm.UndoAsync();

        (await f.Logs.GetAsync(habit.Id, AppFixture.Today)).Should().BeNull();
    }

    [Fact]
    public async Task LastSevenDays_ReflectsRecentHistory()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Tập", startDaysAgo: 10);
        await f.LogAsync(habit, AppFixture.Today.AddDays(-1), 1);
        await f.LogAsync(habit, AppFixture.Today.AddDays(-3), 1);
        var vm = f.NewToday();

        await vm.LoadAsync();

        vm.Groups.Single().Items.Single().LastSevenDays.Should().Equal(
            DayStatus.None, DayStatus.None, DayStatus.None, DayStatus.Done, DayStatus.None, DayStatus.Done, DayStatus.None);
    }

    [Fact]
    public async Task NewDay_RefreshesWhenLoadedAgain()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Thiền", startDaysAgo: 5);
        await f.LogAsync(habit, AppFixture.Today, 1);
        var vm = f.NewToday();
        await vm.LoadAsync();
        vm.Completed.Should().HaveCount(1);

        f.Days.Today = AppFixture.Today.AddDays(1);
        await vm.LoadAsync();

        vm.Today.Should().Be(AppFixture.Today.AddDays(1));
        vm.Completed.Should().BeEmpty();
        vm.Groups.Single().Items.Single().Streak.Should().Be(1, "hôm qua đã xong, hôm nay chưa xong nhưng chuỗi vẫn sống");
    }

    [Fact]
    public async Task ExternalChange_OnBus_ReloadsTheList()
    {
        using var f = new AppFixture();
        var vm = f.NewToday(withBus: true);
        await vm.LoadAsync();
        vm.IsEmpty.Should().BeTrue();

        // Thay đổi do nơi khác (widget, sync) gây ra, đi qua cùng repository nên phát bus.
        await f.AddAsync("Từ widget");
        await WaitUntilAsync(() => vm.Groups.Count == 1);

        vm.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task OwnWrites_DoNotCauseDuplicateLoads()
    {
        using var f = new AppFixture();
        await f.AddAsync("A");
        var loads = 0;
        var vm = f.NewToday(new CountingDispatcher(() => loads++), withBus: true);
        await vm.LoadAsync();
        loads = 0;

        await vm.CheckInAsync(vm.Groups.Single().Items.Single());
        await Task.Delay(100); // chờ mọi tải nền (nếu có) hoàn tất

        loads.Should().Be(1, "chỉ một lần tải sau thao tác, không tải lại vì sự kiện bus của chính mình");
    }

    [Fact]
    public async Task Failure_DuringCheckIn_IsReportedNotThrown()
    {
        using var f = new AppFixture();
        await f.AddAsync("A");
        var vm = f.NewToday();
        await vm.LoadAsync();
        var item = vm.Groups.Single().Items.Single();

        // Simulate a write failure after the transaction has read the habit.
        using (var connection = f.Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER reject_checkin BEFORE INSERT ON day_logs
                BEGIN SELECT RAISE(ABORT, 'check-in write failed'); END;
                """;
            command.ExecuteNonQuery();
        }

        await vm.CheckInAsync(item);

        vm.ErrorMessage.Should().NotBeNullOrEmpty();
        vm.CanUndo.Should().BeFalse();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }
    }

    private sealed class CountingDispatcher : HabitGrid.Presentation.Services.IUiDispatcher
    {
        private readonly Action _onPost;

        public CountingDispatcher(Action onPost) => _onPost = onPost;

        public void Post(Action action)
        {
            _onPost();
            action();
        }
    }
}
