using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Challenges;
using HabitGrid.Core.Domain.Gamification;
using HabitGrid.Core.Domain.Monetization;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;

namespace HabitGrid.Presentation.Tests;

public class WidgetDataTests
{
    private static readonly DateOnly Today = AppFixture.Today; // thứ Năm 22/10/2026

    private static (WidgetDataService Data, BuddyWardrobeService Wardrobe, ChallengeService Challenges, WeeklyRecapService Weekly) Create(AppFixture f, bool plus = true)
    {
        var live = new LiveEntitlements();
        live.Set(plus);
        var wardrobe = new BuddyWardrobeService(f.Settings, f.Profile, f.Achievements, live, f.Days);
        var challenges = new ChallengeService(f.Settings, f.Habits, f.Logs, live, f.Days);
        var weekly = new WeeklyRecapService(f.Habits, f.Logs, f.Settings, f.Days);
        return (new WidgetDataService(f.Habits, f.Logs, f.Profile, wardrobe, challenges, weekly, f.Days), wardrobe, challenges, weekly);
    }

    [Fact]
    public async Task Buddy_ShowsProfile_WornAccessories_AndTodaysProgress()
    {
        using var f = new AppFixture();
        await f.Profile.SaveAsync((await f.Profile.GetAsync()) with { BuddyName = "Mochi", Level = 16, FreezeTokens = 2 });
        var done = await f.AddAsync("Đọc sách", startDaysAgo: 10);
        await f.AddAsync("Tập gym", startDaysAgo: 10);
        await f.LogAsync(done, Today, 1);
        var (data, wardrobe, _, _) = Create(f);
        await wardrobe.ToggleAsync("cap");
        await wardrobe.ToggleAsync("shades");

        var buddy = await data.BuddyAsync();

        buddy.Name.Should().Be("Mochi");
        buddy.Level.Should().Be(16);
        buddy.Stage.Should().Be(BuddyStage.Teen);
        buddy.FreezeTokens.Should().Be(2);
        buddy.TodayDone.Should().Be(1);
        buddy.TodayDue.Should().Be(2);
        buddy.TodayFraction.Should().Be(0.5);
        buddy.Worn.Select(w => w.Slot).Should().Equal(AccessorySlot.Head, AccessorySlot.Face);
    }

    [Fact]
    public async Task Buddy_IgnoresFrozenAndQuitHabits_InTodaysProgress()
    {
        using var f = new AppFixture();
        var frozen = await f.AddAsync("Nghỉ", startDaysAgo: 10);
        await f.AddAsync("Bỏ trà sữa", kind: HabitKind.Quit, startDaysAgo: 10);
        var normal = await f.AddAsync("Chạy bộ", startDaysAgo: 10);
        await f.Logs.UpsertAsync(new DayLog { HabitId = frozen.Id, Day = Today, Status = DayStatus.Frozen });
        await f.LogAsync(normal, Today, 1);
        var (data, _, _, _) = Create(f);

        var buddy = await data.BuddyAsync();

        buddy.TodayDue.Should().Be(1);
        buddy.TodayDone.Should().Be(1);
    }

    [Fact]
    public async Task Goals_CombineTheWeeklyIntention_AndOnlyActiveChallenges()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Thiền", startDaysAgo: 30);
        var (data, _, challenges, weekly) = Create(f);
        f.Days.Today = new DateOnly(2026, 10, 19); // thứ Hai
        await weekly.SetIntentAsync(habit.Id);
        await challenges.StartAsync("journal", "Nhật ký", 30);

        var goals = await data.GoalsAsync();

        goals.Intent!.Name.Should().Be("Thiền");
        goals.Challenges.Should().ContainSingle().Which.State.Should().Be(ChallengeState.Active);
        goals.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Goals_AreEmpty_WhenNothingIsSet()
    {
        using var f = new AppFixture();
        await f.AddAsync("A", startDaysAgo: 10);

        (await Create(f).Data.GoalsAsync()).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task MultiGrid_ShowsAtMostFourHabits_WithThirteenWeeksOfCells()
    {
        using var f = new AppFixture();
        for (var i = 0; i < 4; i++) await f.AddAsync($"H{i}", startDaysAgo: 100);
        await f.AddAsync("Bỏ rượu", kind: HabitKind.Quit, startDaysAgo: 100);
        var (data, _, _, _) = Create(f);

        var grid = await data.MultiGridAsync();

        grid.Rows.Should().HaveCount(4);
        grid.Rows.Should().OnlyContain(r => r.Cells.Count == WidgetDataService.MultiGridWeeks * 7);
        grid.Rows.Select(r => r.Name).Should().NotContain("Bỏ rượu");
    }

    [Fact]
    public async Task MultiGrid_Cells_MarkDone_Frozen_Missed_AndNotYetTracked()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Đọc", startDaysAgo: 5);
        await f.LogAsync(habit, Today, 1);
        await f.LogAsync(habit, Today.AddDays(-1), 1);
        await f.Logs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = Today.AddDays(-2), Status = DayStatus.Frozen });
        var (data, _, _, _) = Create(f);

        var row = (await data.MultiGridAsync()).Rows.Single();

        // Ô cuối cùng của lưới là Chủ nhật tuần này (26/10), tức 4 ngày sau hôm nay (thứ Năm).
        var todayIndex = row.Cells.Count - 1 - 3;
        row.Cells[todayIndex].Should().Be(1, "hôm nay đã xong");
        row.Cells[todayIndex - 1].Should().Be(1);
        row.Cells[todayIndex - 2].Should().Be(2, "ngày tạm nghỉ");
        row.Cells[todayIndex - 3].Should().Be(0, "đã tới hạn nhưng chưa làm");
        row.Cells[todayIndex + 1].Should().Be(-1, "ngày tương lai");
        row.Cells[0].Should().Be(-1, "trước ngày bắt đầu theo dõi");
        row.Streak.Should().Be(2);
    }
}
