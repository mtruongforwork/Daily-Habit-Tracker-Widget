using System.IO.Compression;
using System.Text;
using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Data.V3;
using Xunit;

namespace HabitGrid.Data.Tests.V3;

public sealed class SnapshotBackupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "habitgrid-bak-" + Guid.NewGuid().ToString("N"));

    private SnapshotBackupService CreateService(V3Fixture f) =>
        new(f.Database, f.Clock, f.Habits, f.DayLogs, f.Routines, f.Profile, f.Bus);

    private static async Task SeedAsync(V3Fixture f)
    {
        var a = V3Fixture.NewHabit("A", "V");
        a.Reminders = [new Reminder(new TimeOnly(7, 0))];
        a.Frequency = new HabitFrequency.TimesPerWeek(3);
        var b = V3Fixture.NewHabit("B", "W");
        b.Icon = HabitIcon.Emoji("📚");
        await f.Habits.InsertAsync(a);
        await f.Habits.InsertAsync(b);

        for (var i = 0; i < 5; i++)
        {
            await f.DayLogs.UpsertAsync(new DayLog
            {
                HabitId = a.Id,
                Day = new DateOnly(2026, 2, 1).AddDays(i),
                Value = 1,
                Status = DayStatus.Done,
                Note = i == 0 ? "bắt đầu" : null,
                Mood = i == 0 ? 5 : null
            });
        }

        await f.Routines.UpsertAsync(new Routine(Guid.NewGuid(), "Sáng", HabitIcon.Emoji("🌅"), TimeOfDay.Morning, "V", []));
        await f.Profile.SaveAsync((await f.Profile.GetAsync()) with { DisplayName = "Linh", Xp = 500, Level = 4 });
    }

    [Fact]
    public async Task ExportThenRestore_IntoFreshDatabase_ReproducesAllData()
    {
        using var source = new V3Fixture();
        await SeedAsync(source);
        var path = Path.Combine(_directory, "test.hgbackup");

        var manifest = await CreateService(source).ExportAsync(path);

        manifest.Habits.Should().Be(2);
        manifest.DayLogs.Should().Be(5);
        manifest.SchemaVersion.Should().Be(3);

        using var target = new V3Fixture();
        await CreateService(target).RestoreAsync(path);

        (await target.Habits.GetAllAsync()).Should().BeEquivalentTo(
            await source.Habits.GetAllAsync(),
            o => o.Excluding(h => h.Version).Excluding(h => h.UpdatedAt));
        target.Scalar("SELECT COUNT(*) FROM day_logs;").Should().Be(5);
        (await target.Routines.GetAllAsync()).Should().ContainSingle().Which.Name.Should().Be("Sáng");

        var profile = await target.Profile.GetAsync();
        profile.DisplayName.Should().Be("Linh");
        profile.Xp.Should().Be(500);

        var firstLog = (await target.DayLogs.GetAllForHabitAsync((await source.Habits.GetAllAsync())[0].Id)).First();
        firstLog.Note.Should().Be("bắt đầu");
        firstLog.Mood.Should().Be(5);
    }

    [Fact]
    public async Task Restore_ReplacesExistingData_AndQueuesEverythingForSync()
    {
        using var source = new V3Fixture();
        await SeedAsync(source);
        var path = Path.Combine(_directory, "test.hgbackup");
        await CreateService(source).ExportAsync(path);

        using var target = new V3Fixture();
        await target.Habits.InsertAsync(V3Fixture.NewHabit("Sẽ bị thay", "V"));

        await CreateService(target).RestoreAsync(path);

        (await target.Habits.GetAllAsync()).Select(h => h.Name).Should().BeEquivalentTo("A", "B");
        target.Strings("SELECT DISTINCT entity FROM sync_outbox;").Should().Contain(["habit", "dayLog", "routine"]);
        target.Scalar("SELECT COUNT(*) FROM sync_outbox WHERE entity = 'dayLog';").Should().Be(5);
        target.Changes.Should().Contain(c => c.Entity == DataEntity.Everything);
    }

    [Fact]
    public async Task Restore_NewerSchema_IsRejected_AndLeavesDataUntouched()
    {
        using var f = new V3Fixture();
        await SeedAsync(f);
        var path = Path.Combine(_directory, "future.hgbackup");
        Directory.CreateDirectory(_directory);

        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("{\"schemaVersion\":99,\"createdAtUtc\":\"2030-01-01T00:00:00Z\",\"app\":\"HabitGrid\",\"habits\":0,\"dayLogs\":0}");
        }

        await FluentActions.Awaiting(() => CreateService(f).RestoreAsync(path))
            .Should().ThrowAsync<UnsupportedBackupException>().WithMessage("*v99*");

        (await f.Habits.GetAllAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Restore_CorruptFile_IsRejected_AndLeavesDataUntouched()
    {
        using var f = new V3Fixture();
        await SeedAsync(f);
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "broken.hgbackup");
        await File.WriteAllTextAsync(path, "this is not a zip");

        await FluentActions.Awaiting(() => CreateService(f).RestoreAsync(path))
            .Should().ThrowAsync<UnsupportedBackupException>();

        (await f.Habits.GetAllAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Export_LeavesNoTempFile_AndNeverReplacesGoodBackupWithPartialOne()
    {
        using var f = new V3Fixture();
        await SeedAsync(f);
        var path = Path.Combine(_directory, "x.hgbackup");

        await CreateService(f).ExportAsync(path);
        await CreateService(f).ExportAsync(path); // ghi đè bản trước

        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
        (await SnapshotBackupService.ReadManifestAsync(path)).Habits.Should().Be(2);
    }

    [Fact]
    public async Task DailySnapshot_CreatesOncePerDay_AndKeepsOnlyRecentFiles()
    {
        using var f = new V3Fixture();
        await SeedAsync(f);
        var service = CreateService(f);
        var start = new DateTime(2026, 10, 1, 9, 0, 0);

        (await service.CreateDailySnapshotAsync(_directory, start, keep: 3)).Should().NotBeNull();
        (await service.CreateDailySnapshotAsync(_directory, start.AddHours(5), keep: 3)).Should().BeNull("đã có bản của ngày hôm nay");

        for (var day = 1; day <= 5; day++)
        {
            await service.CreateDailySnapshotAsync(_directory, start.AddDays(day), keep: 3);
        }

        var files = Directory.GetFiles(_directory, "*.hgbackup").Select(Path.GetFileName).Order().ToList();
        files.Should().Equal("habitgrid-20261004.hgbackup", "habitgrid-20261005.hgbackup", "habitgrid-20261006.hgbackup");
    }

    [Fact]
    public async Task ExportThenRestore_V4Data_RoundTripsAreasTodosRewardsRedemptionsAndSettings()
    {
        using var source = new V3Fixture();
        await SeedAsync(source);

        var areas = new HabitAreaRepository(source.Database, source.Clock, source.Bus);
        var todos = new QuickTodoRepository(source.Database, source.Clock, source.Bus);
        var rewards = new RewardRepository(source.Database, source.Clock, source.Bus);
        var sourceSettings = new SettingsStore(source.Database);

        var areaId = Guid.NewGuid();
        await areas.UpsertAsync(new HabitArea(areaId, "Sức khỏe", "💪", "#22C55E", "A"));

        var habit = V3Fixture.NewHabit("Chạy bộ", "Z");
        habit.AreaId = areaId;
        habit.SkipWeekends = true;
        await source.Habits.InsertAsync(habit);

        var day1 = new DateOnly(2026, 5, 1);
        var day2 = new DateOnly(2026, 5, 2);
        var todo1 = new QuickTodo(Guid.NewGuid(), "Mua sách", day1, false, new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc));
        var todo2 = new QuickTodo(Guid.NewGuid(), "Gọi điện", day2, true, new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 2, 10, 0, 0, DateTimeKind.Utc));
        await todos.InsertAsync(todo1);
        await todos.InsertAsync(todo2);

        var rewardId = Guid.NewGuid();
        var reward = new CustomReward(rewardId, "Trà sữa", "🧋", 50, "Ít đường", new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));
        await rewards.UpsertAsync(reward);

        var redemption = new RewardRedemption(Guid.NewGuid(), rewardId, "Trà sữa", 50, new DateTime(2026, 5, 2, 15, 30, 0, DateTimeKind.Utc));
        await rewards.RecordRedemptionAsync(redemption);

        await sourceSettings.SetAsync(SettingKeys.CoinsBalance, "120");
        await sourceSettings.SetAsync(SettingKeys.HabitStacks, "[{\"triggerId\":\"a\",\"nextId\":\"b\"}]");
        await sourceSettings.SetAsync(SettingKeys.GoalOfDayHabitId, habit.Id.ToString("D"));
        await sourceSettings.SetAsync(SettingKeys.GoalOfDayDate, "2026-05-02");

        var path = Path.Combine(_directory, "v4-roundtrip.hgbackup");
        await CreateService(source).ExportAsync(path);

        using var target = new V3Fixture();
        var targetSettings = new SettingsStore(target.Database);
        await targetSettings.SetAsync(SettingKeys.Language, "vi");

        await CreateService(target).RestoreAsync(path);

        var targetAreas = new HabitAreaRepository(target.Database, target.Clock, target.Bus);
        var targetTodos = new QuickTodoRepository(target.Database, target.Clock, target.Bus);
        var targetRewards = new RewardRepository(target.Database, target.Clock, target.Bus);

        (await targetAreas.GetAllAsync()).Should().ContainSingle().Which.Should().Be(new HabitArea(areaId, "Sức khỏe", "💪", "#22C55E", "A"));

        var restoredHabit = (await target.Habits.GetAllAsync()).Single(h => h.Id == habit.Id);
        restoredHabit.AreaId.Should().Be(areaId);
        restoredHabit.SkipWeekends.Should().BeTrue();

        (await targetTodos.GetForDayAsync(day1)).Should().ContainSingle().Which.Should().Be(todo1);
        (await targetTodos.GetForDayAsync(day2)).Should().ContainSingle().Which.Should().Be(todo2);

        (await targetRewards.GetAllAsync()).Should().ContainSingle().Which.Should().Be(reward);
        (await targetRewards.GetRecentRedemptionsAsync()).Should().ContainSingle().Which.Should().Be(redemption);

        (await targetSettings.GetAsync(SettingKeys.CoinsBalance)).Should().Be("120");
        (await targetSettings.GetAsync(SettingKeys.HabitStacks)).Should().Be("[{\"triggerId\":\"a\",\"nextId\":\"b\"}]");
        (await targetSettings.GetAsync(SettingKeys.GoalOfDayHabitId)).Should().Be(habit.Id.ToString("D"));
        (await targetSettings.GetAsync(SettingKeys.GoalOfDayDate)).Should().Be("2026-05-02");
        (await targetSettings.GetAsync(SettingKeys.Language)).Should().Be("vi");
    }

    [Fact]
    public async Task Restore_LegacyV3BackupWithoutV4Fields_SucceedsAndClearsExistingV4Data()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "legacy-v3.hgbackup");

        var legacyHabit = V3Fixture.NewHabit("Thiền", "V");
        legacyHabit.Icon = HabitIcon.Emoji("🧘");
        var legacyProfile = new UserProfile { DisplayName = "Minh", Xp = 100, Level = 2 };

        var manifestJson = """{"schemaVersion":3,"createdAtUtc":"2026-03-01T00:00:00Z","app":"HabitGrid","habits":1,"dayLogs":0}""";
        var habitsJson = System.Text.Json.JsonSerializer.Serialize(new[] { legacyHabit }, DomainJson.Options);
        var profileJson = System.Text.Json.JsonSerializer.Serialize(legacyProfile, DomainJson.Options);
        var dataJson = $$"""
            {
              "habits": {{habitsJson}},
              "dayLogs": [],
              "routines": [],
              "profile": {{profileJson}}
            }
            """;

        using (var stream = File.Create(path))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteRawEntry(zip, "manifest.json", manifestJson);
            WriteRawEntry(zip, "data.json", dataJson);
        }

        using var target = new V3Fixture();
        var targetAreas = new HabitAreaRepository(target.Database, target.Clock, target.Bus);
        var targetTodos = new QuickTodoRepository(target.Database, target.Clock, target.Bus);
        var targetRewards = new RewardRepository(target.Database, target.Clock, target.Bus);
        var targetSettings = new SettingsStore(target.Database);

        await targetAreas.UpsertAsync(new HabitArea(Guid.NewGuid(), "Cũ", "📁", "#000000", "V"));
        await targetTodos.InsertAsync(new QuickTodo(Guid.NewGuid(), "Việc cũ", new DateOnly(2026, 4, 1), false, DateTime.UtcNow));
        var oldRewardId = Guid.NewGuid();
        await targetRewards.UpsertAsync(new CustomReward(oldRewardId, "Quà cũ", "🎁", 10, null, DateTime.UtcNow));
        await targetRewards.RecordRedemptionAsync(new RewardRedemption(Guid.NewGuid(), oldRewardId, "Quà cũ", 10, DateTime.UtcNow));
        await targetSettings.SetAsync(SettingKeys.CoinsBalance, "999");
        await targetSettings.SetAsync(SettingKeys.HabitStacks, "[]");
        await targetSettings.SetAsync(SettingKeys.GoalOfDayHabitId, Guid.NewGuid().ToString("D"));
        await targetSettings.SetAsync(SettingKeys.GoalOfDayDate, "2026-04-01");
        await targetSettings.SetAsync(SettingKeys.Language, "en");

        var manifest = await CreateService(target).RestoreAsync(path);

        manifest.SchemaVersion.Should().Be(3);
        (await target.Habits.GetAllAsync()).Should().ContainSingle().Which.Name.Should().Be("Thiền");
        (await target.Profile.GetAsync()).DisplayName.Should().Be("Minh");

        (await targetAreas.GetAllAsync()).Should().BeEmpty();
        (await targetTodos.GetForDayAsync(new DateOnly(2026, 4, 1))).Should().BeEmpty();
        (await targetRewards.GetAllAsync()).Should().BeEmpty();
        (await targetRewards.GetRecentRedemptionsAsync()).Should().BeEmpty();
        (await targetSettings.GetAsync(SettingKeys.CoinsBalance)).Should().BeNull();
        (await targetSettings.GetAsync(SettingKeys.HabitStacks)).Should().BeNull();
        (await targetSettings.GetAsync(SettingKeys.GoalOfDayHabitId)).Should().BeNull();
        (await targetSettings.GetAsync(SettingKeys.GoalOfDayDate)).Should().BeNull();
        (await targetSettings.GetAsync(SettingKeys.Language)).Should().Be("en");
    }

    private static void WriteRawEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
