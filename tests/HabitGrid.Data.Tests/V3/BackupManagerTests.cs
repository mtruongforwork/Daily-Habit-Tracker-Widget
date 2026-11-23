using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Data.V3;
using Xunit;

namespace HabitGrid.Data.Tests.V3;

public sealed class BackupManagerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "habitgrid-mgr-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private BackupManager Create(V3Fixture f, int retention = 3) =>
        new(new SnapshotBackupService(f.Database, f.Clock, f.Bus, includeJournal: false), _directory, retention);

    private static DateTime Day(int offset) => new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc).AddDays(offset);

    [Fact]
    public async Task RunIfDue_BacksUpOncePerDay_AndSkipsTheSecondCall()
    {
        using var f = new V3Fixture();
        var manager = Create(f);

        var first = await manager.RunIfDueAsync(Day(0));
        var again = await manager.RunIfDueAsync(Day(0).AddHours(1));

        first.Should().NotBeNull();
        first!.IsAutomatic.Should().BeTrue();
        again.Should().BeNull();
        manager.List().Should().ContainSingle();
    }

    [Fact]
    public async Task RunIfDue_NextDay_CreatesANewBackup_AndPrunesOldAutomaticOnes()
    {
        using var f = new V3Fixture();
        var manager = Create(f, retention: 3);

        for (var i = 0; i < 6; i++)
        {
            (await manager.RunIfDueAsync(Day(i))).Should().NotBeNull();
        }

        var kept = manager.List();
        kept.Should().HaveCount(3);
        kept.Select(b => b.CreatedAtUtc).Should().BeInDescendingOrder();
        kept[0].CreatedAtUtc.Should().Be(Day(5).Date.Add(Day(5).TimeOfDay));
    }

    [Fact]
    public async Task ManualBackups_AreNeverPruned()
    {
        using var f = new V3Fixture();
        var manager = Create(f, retention: 1);
        await manager.BackupNowAsync(Day(0));
        await manager.BackupNowAsync(Day(0).AddMinutes(1));

        for (var i = 1; i <= 4; i++)
        {
            await manager.RunIfDueAsync(Day(i));
        }

        manager.List().Count(b => !b.IsAutomatic).Should().Be(2);
        manager.List().Count(b => b.IsAutomatic).Should().Be(1);
    }

    [Fact]
    public async Task Restore_ReplacesData_AndFirstSavesTheCurrentState()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit("Giữ lại");
        await f.Habits.InsertAsync(habit);
        var manager = Create(f);
        var backup = await manager.BackupNowAsync(Day(0));

        var extra = V3Fixture.NewHabit("Thêm sau", "W");
        await f.Habits.InsertAsync(extra);
        (await f.Habits.GetAllAsync()).Should().HaveCount(2);

        // The safety snapshot must never overwrite a manual backup created in the same second.
        await manager.RestoreAsync(backup.Path, Day(0));

        (await f.Habits.GetAllAsync()).Should().ContainSingle().Which.Name.Should().Be("Giữ lại");
        var files = manager.List();
        files.Should().HaveCount(2, "bản đã chọn và bản tự lưu trước khi khôi phục");
        files.Any(b => b.Path.Contains("before-restore")).Should().BeTrue();

        // Quay lại trạng thái trước khi khôi phục bằng bản "before-restore".
        var safety = files.Single(b => b.Path.Contains("before-restore"));
        await manager.RestoreAsync(safety.Path, Day(2));
        (await f.Habits.GetAllAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Restore_OfACorruptFile_ChangesNothing()
    {
        using var f = new V3Fixture();
        await f.Habits.InsertAsync(V3Fixture.NewHabit("A"));
        var manager = Create(f);
        Directory.CreateDirectory(_directory);
        var bad = Path.Combine(_directory, "habitgrid-20261106-120000.hgbackup");
        await File.WriteAllTextAsync(bad, "not a zip");

        var act = async () => await manager.RestoreAsync(bad, Day(0));

        await act.Should().ThrowAsync<Exception>();
        (await f.Habits.GetAllAsync()).Should().ContainSingle();
        manager.List().Should().ContainSingle("không tạo thêm bản nào khi file hỏng");
    }

    [Fact]
    public void List_OnAMissingFolder_IsEmpty_AndIgnoresForeignFiles()
    {
        BackupManager.ListIn(_directory).Should().BeEmpty();

        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "habitgrid-notadate.hgbackup"), "x");
        File.WriteAllText(Path.Combine(_directory, "other.txt"), "x");

        BackupManager.ListIn(_directory).Should().BeEmpty();
    }
}
