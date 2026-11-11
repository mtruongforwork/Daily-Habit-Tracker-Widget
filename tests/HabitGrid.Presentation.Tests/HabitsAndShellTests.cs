using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;

namespace HabitGrid.Presentation.Tests;

public class HabitCreationTests
{
    [Fact]
    public async Task Create_Valid_SetsColorStartDateAndAppendsSortKey()
    {
        using var f = new AppFixture();

        var first = await f.Creation.CreateAsync(new HabitDraft("  Đọc sách  ", HabitIcon.Emoji("📚")));
        var second = await f.Creation.CreateAsync(new HabitDraft("Chạy bộ", HabitIcon.Default));

        first.Succeeded.Should().BeTrue();
        first.Habit!.Name.Should().Be("Đọc sách", "tên được cắt khoảng trắng");
        first.Habit.StartDate.Should().Be(AppFixture.Today);
        first.Habit.ColorHue.Should().Be(HabitCreationService.PresetColors[0]);
        second.Habit!.ColorHue.Should().Be(HabitCreationService.PresetColors[1], "màu xoay vòng theo preset");
        string.CompareOrdinal(second.Habit.SortKey, first.Habit.SortKey).Should().BePositive();
        (await f.Habits.CountActiveAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("", "name.required")]
    [InlineData("   ", "name.required")]
    public async Task Create_InvalidName_ReturnsErrorsAndSavesNothing(string name, string code)
    {
        using var f = new AppFixture();

        var result = await f.Creation.CreateAsync(new HabitDraft(name, HabitIcon.Default));

        result.Succeeded.Should().BeFalse();
        result.Errors.Select(e => e.Code).Should().Contain(code);
        (await f.Habits.CountActiveAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_CountWithoutTarget_IsRejected_AndBooleanTargetIsForcedToOne()
    {
        using var f = new AppFixture();

        (await f.Creation.CreateAsync(new HabitDraft("A", HabitIcon.Default, HabitKind.Count, Target: 0)))
            .Errors.Select(e => e.Code).Should().Contain("target.range");

        var boolean = await f.Creation.CreateAsync(new HabitDraft("B", HabitIcon.Default, HabitKind.Boolean, Target: 99));
        boolean.Habit!.Target.Value.Should().Be(1);
    }

    [Fact]
    public void ValidationText_TranslatesKnownCodes_AndFallsBackForUnknown()
    {
        var localizer = new Localizer();

        ValidationText.For(localizer, new ValidationError("name.required", "Name")).Should().Be("Hãy đặt tên cho thói quen.");
        ValidationText.For(localizer, new ValidationError("totally.unknown", "X")).Should().Be(localizer["validation.generic"]);
    }
}

public class HabitsViewModelTests
{
    private static HabitsViewModel NewVm(AppFixture f) => new(f.Habits, f.Logs, f.Days);

    [Fact]
    public async Task Load_ListsHabitsWithStreakAndRate()
    {
        using var f = new AppFixture();
        var habit = await f.AddAsync("Thiền", startDaysAgo: 30);
        for (var i = 0; i < 6; i++)
        {
            await f.LogAsync(habit, AppFixture.Today.AddDays(-i), 1);
        }

        var vm = NewVm(f);
        await vm.LoadAsync();

        var item = vm.Items.Single();
        item.Streak.Should().Be(6);
        item.Rate30.Should().NotBeNull();
        vm.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Archive_RemovesFromList()
    {
        using var f = new AppFixture();
        await f.AddAsync("A");
        var vm = NewVm(f);
        await vm.LoadAsync();

        await vm.ArchiveAsync(vm.Items.Single());

        vm.Items.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
        (await f.Habits.GetAllAsync()).Should().ContainSingle("lưu trữ không xóa dữ liệu");
    }

    [Fact]
    public async Task Delete_ThenUndo_BringsTheHabitBack()
    {
        using var f = new AppFixture();
        await f.AddAsync("A");
        var vm = NewVm(f);
        await vm.LoadAsync();

        await vm.DeleteAsync(vm.Items.Single());
        vm.Items.Should().BeEmpty();
        vm.LastDeletedId.Should().NotBeNull();

        await vm.UndoDeleteAsync();

        vm.Items.Should().ContainSingle();
        vm.LastDeletedId.Should().BeNull();
    }

    [Fact]
    public async Task Move_ReordersWithSingleRowWrite()
    {
        using var f = new AppFixture();
        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            await f.AddAsync(name);
        }

        var vm = NewVm(f);
        await vm.LoadAsync();
        var outboxBefore = Count(f, "sync_outbox");

        await vm.MoveAsync(vm.Items.Single(i => i.Name == "D"), 1);
        vm.Items.Select(i => i.Name).Should().Equal("A", "D", "B", "C");

        await vm.MoveAsync(vm.Items.Single(i => i.Name == "A"), 3);
        vm.Items.Select(i => i.Name).Should().Equal("D", "B", "C", "A");

        await vm.MoveAsync(vm.Items.Single(i => i.Name == "C"), 0);
        vm.Items.Select(i => i.Name).Should().Equal("C", "D", "B", "A");

        (Count(f, "sync_outbox") - outboxBefore).Should().Be(3, "mỗi lần kéo-thả chỉ ghi một dòng");
    }

    [Fact]
    public async Task Move_WithDuplicateSortKeys_RenumbersAndStillWorks()
    {
        using var f = new AppFixture();
        var a = await f.AddAsync("A");
        var b = await f.AddAsync("B");
        var c = await f.AddAsync("C");
        // Làm hỏng dữ liệu: hai khóa bằng nhau.
        using (var connection = f.Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"UPDATE habits SET sort_key = 'V' WHERE id IN ('{a.Id}', '{b.Id}', '{c.Id}');";
            command.ExecuteNonQuery();
        }

        var vm = NewVm(f);
        await vm.LoadAsync();
        var first = vm.Items[0];

        await vm.MoveAsync(first, 2);

        vm.Items.Select(i => i.Habit.SortKey).Should().BeInAscendingOrder(StringComparer.Ordinal);
        vm.Items.Select(i => i.Habit.SortKey).Distinct().Should().HaveCount(3);
        vm.Items[2].Id.Should().Be(first.Id);
    }

    private static long Count(AppFixture f, string table)
    {
        using var connection = f.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(command.ExecuteScalar());
    }
}

public class ShellAndLocalizationTests
{
    [Fact]
    public void Shell_HasEightNavigationTargets_WithUniqueKeys()
    {
        var shell = new ShellViewModel(new Localizer());

        shell.AllItems.Select(i => i.Key).Should().Equal("today", "calendar", "journal", "focus", "habits", "insights", "buddy", "settings");
        shell.SelectedKey.Should().Be(PageKeys.Today);
        shell.TitleOf(PageKeys.Habits).Should().Be("Thói quen");
        shell.TitleOf("nope").Should().BeEmpty();
    }

    [Fact]
    public void Localizer_SwitchesLanguageLive_AndRaisesEvent()
    {
        var localizer = new Localizer();
        var raised = 0;
        localizer.LanguageChanged += (_, _) => raised++;

        localizer["nav.today"].Should().Be("Hôm nay");

        localizer.SetLanguage("en");
        localizer["nav.today"].Should().Be("Today");
        localizer.Format("today.progress", 2, 5).Should().Be("2/5 done today");

        localizer.SetLanguage("en"); // không đổi → không phát sự kiện
        localizer.SetLanguage("vi");
        localizer["nav.today"].Should().Be("Hôm nay");
        raised.Should().Be(2);
    }

    [Fact]
    public void Localizer_MissingKey_IsVisibleNotSilent()
    {
        new Localizer()["no.such.key"].Should().Be("[no.such.key]");
    }

    [Fact]
    public void EveryKey_ExistsInBothLanguages_WithMatchingPlaceholders()
    {
        var manager = new ResourceManager("HabitGrid.Presentation.Resources.Strings", typeof(Localizer).Assembly);

        var vi = Keys(manager, "vi");
        var en = Keys(manager, "en");

        vi.Keys.Should().BeEquivalentTo(en.Keys, "mọi khóa phải có đủ VI và EN");

        foreach (var (key, viText) in vi)
        {
            viText.Should().NotBeNullOrWhiteSpace(key);
            en[key].Should().NotBeNullOrWhiteSpace(key);
            Placeholders(viText).Should().BeEquivalentTo(Placeholders(en[key]), $"khóa '{key}' phải dùng cùng tham số ở hai ngôn ngữ");
        }
    }

    [Fact]
    public void EveryNavigationAndStreakKey_Resolves()
    {
        var localizer = new Localizer();
        var shell = new ShellViewModel(localizer);

        foreach (var language in new[] { "vi", "en" })
        {
            localizer.SetLanguage(language);

            foreach (var item in shell.AllItems)
            {
                localizer[item.TitleKey].Should().NotStartWith("[");
            }

            foreach (var key in new[] { "streak.days", "streak.weeks", "streak.months", "streak.windows" })
            {
                localizer.Format(key, 3).Should().Contain("3");
            }
        }
    }

    private static Dictionary<string, string> Keys(ResourceManager manager, string culture)
    {
        var set = manager.GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: true)!;
        var result = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in set)
        {
            result[(string)entry.Key] = (string)entry.Value!;
        }

        return result;
    }

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Distinct().Order().ToArray();
}
