using FluentAssertions;
using HabitGrid.Data.V3;
using HabitGrid.Presentation.Services;

namespace HabitGrid.Presentation.Tests;

public class JournalModelTests
{
    private static JournalModel Model(AppFixture f) => new(new JournalRepository(f.Database), f.Days);

    [Fact]
    public async Task Load_StartsOnToday_WithNoEntry()
    {
        using var f = new AppFixture();
        var model = Model(f);

        await model.LoadAsync();

        model.SelectedDay.Should().Be(AppFixture.Today);
        model.Current.Should().BeNull();
        model.Entries.Should().BeEmpty();
        model.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public async Task Save_StoresTheEntry_AndShowsItInTheRecentList()
    {
        using var f = new AppFixture();
        var model = Model(f);
        await model.LoadAsync();

        await model.SaveAsync("Một ngày dài", 4);

        model.Current.Should().Match<HabitGrid.Core.Domain.Journal.JournalEntry>(e => e.Text == "Một ngày dài" && e.Mood == 4);
        model.Entries.Should().ContainSingle().Which.Day.Should().Be(AppFixture.Today);
    }

    [Fact]
    public async Task ShiftDay_MovesBackButNeverIntoTheFuture()
    {
        using var f = new AppFixture();
        var model = Model(f);
        await model.LoadAsync();

        await model.ShiftDayAsync(-3);
        model.SelectedDay.Should().Be(AppFixture.Today.AddDays(-3));
        model.CanGoNext.Should().BeTrue();

        await model.ShiftDayAsync(10);
        model.SelectedDay.Should().Be(AppFixture.Today);

        await model.LoadAsync(AppFixture.Today.AddDays(5));
        model.SelectedDay.Should().Be(AppFixture.Today);
    }

    [Fact]
    public async Task EditingAPastDay_KeepsTheOtherDaysIntact()
    {
        using var f = new AppFixture();
        var model = Model(f);
        await model.LoadAsync();
        await model.SaveAsync("hôm nay", null);

        await model.SelectDayAsync(AppFixture.Today.AddDays(-2));
        model.Current.Should().BeNull();
        await model.SaveAsync("hôm kia", 2);

        model.Entries.Select(e => e.Text).Should().Equal("hôm nay", "hôm kia");
    }

    [Fact]
    public async Task Search_FiltersTheList_AndClearingRestoresTheRecentList()
    {
        using var f = new AppFixture();
        var model = Model(f);
        await model.LoadAsync();
        await model.SaveAsync("chạy bộ", null);
        await model.SelectDayAsync(AppFixture.Today.AddDays(-1));
        await model.SaveAsync("đọc sách", null);

        await model.SearchAsync("sách");
        model.Entries.Should().ContainSingle().Which.Text.Should().Be("đọc sách");

        await model.SearchAsync("");
        model.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task SavingEmptyText_RemovesTheEntry()
    {
        using var f = new AppFixture();
        var model = Model(f);
        await model.LoadAsync();
        await model.SaveAsync("tạm", 3);

        await model.SaveAsync("   ", null);

        model.Current.Should().BeNull();
        model.Entries.Should().BeEmpty();
    }
}
