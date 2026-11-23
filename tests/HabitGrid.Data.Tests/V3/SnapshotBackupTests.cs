using System.IO.Compression;
using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Data.V3;
using Xunit;

namespace HabitGrid.Data.Tests.V3;

public sealed class SnapshotBackupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "habitgrid-bak-" + Guid.NewGuid().ToString("N"));

    private SnapshotBackupService CreateService(V3Fixture f) =>
        new(f.Database, f.Clock, f.Bus);

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
    public async Task Restore_ProfileWriteFails_RollsBackDataAndOutbox()
    {
        using var target = new V3Fixture();
        var habit = V3Fixture.NewHabit("Original");
        await target.Habits.InsertAsync(habit);
        await target.Profile.SaveAsync(new UserProfile { DisplayName = "Original profile" });
        var outbox = target.OutboxCount;
        target.Changes.Clear();
        target.Execute("""
            CREATE TRIGGER reject_profile BEFORE INSERT ON profile
            BEGIN SELECT RAISE(ABORT, 'profile write failed'); END;
            """);

        using var source = new V3Fixture();
        await SeedAsync(source);
        var path = Path.Combine(_directory, "restore.hgbackup");
        await CreateService(source).ExportAsync(path);

        await FluentActions.Awaiting(() => CreateService(target).RestoreAsync(path))
            .Should().ThrowAsync<Microsoft.Data.Sqlite.SqliteException>().WithMessage("*profile write failed*");

        (await target.Habits.GetAllAsync()).Should().ContainSingle().Which.Id.Should().Be(habit.Id);
        (await target.Profile.GetAsync()).DisplayName.Should().Be("Original profile");
        target.OutboxCount.Should().Be(outbox);
        target.Changes.Should().BeEmpty();
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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
