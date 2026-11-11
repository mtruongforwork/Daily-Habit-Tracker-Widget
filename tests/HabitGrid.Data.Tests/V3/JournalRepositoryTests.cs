using FluentAssertions;
using HabitGrid.Core.Domain.Journal;
using HabitGrid.Data.V3;

namespace HabitGrid.Data.Tests.V3;

public class JournalRepositoryTests
{
    private static JournalEntry Entry(string day, string text, int? mood = null) => new(DateOnly.Parse(day), text, mood, DateTime.UtcNow);

    [Fact]
    public async Task UpsertThenGet_RoundTrips_AndReplacesTheSameDay()
    {
        using var f = new V3Fixture();
        var repo = new JournalRepository(f.Database);

        await repo.UpsertAsync(Entry("2026-11-01", "  Hôm nay tốt  ", 4));
        (await repo.GetAsync(new DateOnly(2026, 11, 1)))!.Should().Match<JournalEntry>(e => e.Text == "Hôm nay tốt" && e.Mood == 4);

        await repo.UpsertAsync(Entry("2026-11-01", "Đã sửa", null));
        (await repo.GetAllAsync()).Should().ContainSingle().Which.Text.Should().Be("Đã sửa");
    }

    [Fact]
    public async Task EmptyEntry_DeletesTheDay_AndInvalidMoodIsDropped()
    {
        using var f = new V3Fixture();
        var repo = new JournalRepository(f.Database);

        await repo.UpsertAsync(Entry("2026-11-01", "Có chữ", 9));
        (await repo.GetAsync(new DateOnly(2026, 11, 1)))!.Mood.Should().BeNull();

        await repo.UpsertAsync(Entry("2026-11-01", "   ", null));
        (await repo.GetAsync(new DateOnly(2026, 11, 1))).Should().BeNull();
    }

    [Fact]
    public async Task Text_IsCappedAtTheMaximumLength()
    {
        using var f = new V3Fixture();
        var repo = new JournalRepository(f.Database);

        await repo.UpsertAsync(Entry("2026-11-02", new string('a', JournalRules.MaxLength + 500)));

        (await repo.GetAsync(new DateOnly(2026, 11, 2)))!.Text.Length.Should().Be(JournalRules.MaxLength);
    }

    [Fact]
    public async Task Range_ReturnsNewestFirstAndOnlyInsideTheRange()
    {
        using var f = new V3Fixture();
        var repo = new JournalRepository(f.Database);
        foreach (var d in new[] { "2026-10-30", "2026-11-02", "2026-11-05", "2026-11-09" })
        {
            await repo.UpsertAsync(Entry(d, "x " + d));
        }

        var range = await repo.GetRangeAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 8));

        range.Select(e => e.Day.ToString("yyyy-MM-dd")).Should().Equal("2026-11-05", "2026-11-02");
    }

    [Fact]
    public async Task Search_FindsText_CaseInsensitive_EscapesWildcards_AndHonoursLimit()
    {
        using var f = new V3Fixture();
        var repo = new JournalRepository(f.Database);
        await repo.UpsertAsync(Entry("2026-11-01", "Chạy bộ buổi sáng"));
        await repo.UpsertAsync(Entry("2026-11-02", "Đọc SÁCH"));
        await repo.UpsertAsync(Entry("2026-11-03", "Giảm 50% đường"));
        await repo.UpsertAsync(Entry("2026-11-04", "Một_hai"));

        (await repo.SearchAsync("chạy")).Select(e => e.Day.Day).Should().Equal(1);
        (await repo.SearchAsync("50%")).Should().ContainSingle();
        (await repo.SearchAsync("%")).Should().ContainSingle("% là ký tự thường, không phải ký tự đại diện");
        (await repo.SearchAsync("_")).Should().ContainSingle();
        (await repo.SearchAsync("zzz")).Should().BeEmpty();
        (await repo.SearchAsync("  ")).Should().BeEmpty();
        (await repo.SearchAsync("o", 2)).Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task EraseAll_AlsoClearsTheJournal()
    {
        using var f = new V3Fixture();
        var repo = new JournalRepository(f.Database);
        await repo.UpsertAsync(Entry("2026-11-01", "bí mật"));

        await DataEraser.EraseAllAsync(f.Database);

        (await repo.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_ExportsAndRestoresTheJournal()
    {
        using var f = new V3Fixture();
        var journal = new JournalRepository(f.Database);
        var service = new SnapshotBackupService(f.Database, f.Clock, f.Habits, f.DayLogs, f.Routines, f.Profile, f.Bus, journal);
        await journal.UpsertAsync(Entry("2026-11-01", "Nhật ký cần được giữ", 5));

        var path = Path.Combine(Path.GetTempPath(), $"hg-journal-{Guid.NewGuid():N}{SnapshotBackupService.Extension}");

        try
        {
            await service.ExportAsync(path);
            await journal.DeleteAsync(new DateOnly(2026, 11, 1));
            (await journal.GetAllAsync()).Should().BeEmpty();

            await service.RestoreAsync(path);

            (await journal.GetAsync(new DateOnly(2026, 11, 1)))!.Should().Match<JournalEntry>(e => e.Text == "Nhật ký cần được giữ" && e.Mood == 5);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OldBackupsWithoutAJournal_StillRestore_AndClearTheCurrentOne()
    {
        using var f = new V3Fixture();
        var journal = new JournalRepository(f.Database);
        var withoutJournal = new SnapshotBackupService(f.Database, f.Clock, f.Habits, f.DayLogs, f.Routines, f.Profile, f.Bus);
        var path = Path.Combine(Path.GetTempPath(), $"hg-nojournal-{Guid.NewGuid():N}{SnapshotBackupService.Extension}");

        try
        {
            await withoutJournal.ExportAsync(path);
            await journal.UpsertAsync(Entry("2026-11-01", "mới viết"));

            await new SnapshotBackupService(f.Database, f.Clock, f.Habits, f.DayLogs, f.Routines, f.Profile, f.Bus, journal).RestoreAsync(path);

            (await journal.GetAllAsync()).Should().BeEmpty("khôi phục thay thế toàn bộ dữ liệu");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
