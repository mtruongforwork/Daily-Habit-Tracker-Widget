using System.Text.Json;
using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Data;
using HabitGrid.Data.V3;
using Microsoft.Data.Sqlite;
using Xunit;
using DayOfWeekMask = HabitGrid.Core.Models.DayOfWeekMask;

namespace HabitGrid.Data.Tests.V3;

public class SchemaTests
{
    [Fact]
    public void Schema_CreatesAllTables_AndIsIdempotent()
    {
        using var fixture = new V3Fixture();

        HabitGridV3.Migrate(fixture.Database); // gọi lại không lỗi

        var tables = fixture.Strings("SELECT name FROM sqlite_master WHERE type = 'table';");
        tables.Should().Contain([
            "habits", "reminders", "day_logs", "routines", "profile", "xp_events", "achievements",
            "widgets", "settings", "sync_outbox", "sync_state", "schema_info", "journal_entries"]);

        fixture.Scalar("SELECT MAX(version) FROM schema_info;").Should().Be(4);
    }

    [Fact]
    public void Schema_EnforcesForeignKeys()
    {
        using var fixture = new V3Fixture();

        var act = () => fixture.Execute(
            "INSERT INTO day_logs (habit_id, day, value, status, updated_at, hlc) VALUES ('nope', '2026-01-01', 1, 1, 'x', 'x');");

        act.Should().Throw<SqliteException>();
    }
}

public class HabitRepositoryTests
{
    [Fact]
    public async Task InsertThenGet_RoundTripsEveryField()
    {
        using var f = new V3Fixture();
        var habit = new Habit
        {
            Name = "Uống nước",
            Description = "8 ly mỗi ngày",
            Icon = HabitIcon.Emoji("💧"),
            ColorHue = "#3B82F6",
            Kind = HabitKind.Count,
            Target = new HabitTarget(8, "ly"),
            Frequency = new HabitFrequency.WeekDays(DayOfWeekMask.Monday | DayOfWeekMask.Friday),
            TimeOfDay = TimeOfDay.Morning,
            StartDate = new DateOnly(2026, 3, 9),
            Reminders = [new Reminder(new TimeOnly(7, 30), DayOfWeekMask.Monday, Smart: true, FollowUp: true), new Reminder(new TimeOnly(20, 0))],
            AskMood = true,
            AllowNotes = false,
            AllowFreeze = false,
            RoutineId = Guid.NewGuid(),
            RoutineOrder = 3,
            Tags = ["sức khỏe", "nước"],
            SortKey = "Vz",
            SharedChallengeId = Guid.NewGuid()
        };

        await f.Habits.InsertAsync(habit);
        var loaded = await f.Habits.GetByIdAsync(habit.Id);

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(habit, o => o.Excluding(h => h.Version).Excluding(h => h.UpdatedAt));
        loaded.Version.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Frequency_AllVariants_RoundTrip()
    {
        using var f = new V3Fixture();
        var anchor = new DateOnly(2026, 2, 1);
        HabitFrequency[] variants =
        [
            new HabitFrequency.Daily(),
            new HabitFrequency.WeekDays(DayOfWeekMask.Saturday | DayOfWeekMask.Sunday),
            new HabitFrequency.TimesPerWeek(4),
            new HabitFrequency.TimesPerMonth(10),
            new HabitFrequency.EveryNDays(5, anchor)
        ];

        var ids = new List<Guid>();
        foreach (var (frequency, i) in variants.Select((v, i) => (v, i)))
        {
            var habit = V3Fixture.NewHabit($"H{i}", $"V{i + 1}");
            habit.Frequency = frequency;
            await f.Habits.InsertAsync(habit);
            ids.Add(habit.Id);
        }

        for (var i = 0; i < variants.Length; i++)
        {
            (await f.Habits.GetByIdAsync(ids[i]))!.Frequency.Should().Be(variants[i]);
        }
    }

    [Fact]
    public async Task GetAllActive_OrdersBySortKey_AndExcludesArchivedAndDeleted()
    {
        using var f = new V3Fixture();
        var c = V3Fixture.NewHabit("C", "W");
        var a = V3Fixture.NewHabit("A", "V");
        var b = V3Fixture.NewHabit("B", "VV");
        var archived = V3Fixture.NewHabit("Archived", "X");
        var deleted = V3Fixture.NewHabit("Deleted", "Y");

        foreach (var habit in new[] { c, a, b, archived, deleted })
        {
            await f.Habits.InsertAsync(habit);
        }

        await f.Habits.ArchiveAsync(archived.Id);
        await f.Habits.SoftDeleteAsync(deleted.Id);

        (await f.Habits.GetAllActiveAsync()).Select(h => h.Name).Should().Equal("A", "B", "C");
        (await f.Habits.GetAllAsync()).Select(h => h.Name).Should().Contain("Archived").And.NotContain("Deleted");
        (await f.Habits.CountActiveAsync()).Should().Be(3);
    }

    [Fact]
    public async Task SoftDelete_ThenRestore_KeepsDataAndAllowsUndo()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        await f.Habits.InsertAsync(habit);

        await f.Habits.SoftDeleteAsync(habit.Id);
        (await f.Habits.GetByIdAsync(habit.Id))!.DeletedAt.Should().NotBeNull();
        (await f.Habits.GetAllAsync()).Should().BeEmpty();

        await f.Habits.RestoreAsync(habit.Id);
        (await f.Habits.GetByIdAsync(habit.Id))!.DeletedAt.Should().BeNull();
        (await f.Habits.GetAllActiveAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ArchiveAndUnarchive()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        await f.Habits.InsertAsync(habit);

        await f.Habits.ArchiveAsync(habit.Id);
        (await f.Habits.GetByIdAsync(habit.Id))!.IsArchived.Should().BeTrue();

        await f.Habits.UnarchiveAsync(habit.Id);
        (await f.Habits.GetByIdAsync(habit.Id))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Update_ReplacesFieldsAndReminders_AndBumpsVersion()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        habit.Reminders = [new Reminder(new TimeOnly(8, 0)), new Reminder(new TimeOnly(9, 0))];
        await f.Habits.InsertAsync(habit);
        var firstVersion = habit.Version;

        habit.Name = "Đọc sách 30 phút";
        habit.Reminders = [new Reminder(new TimeOnly(21, 15))];
        await f.Habits.UpdateAsync(habit);

        var loaded = (await f.Habits.GetByIdAsync(habit.Id))!;
        loaded.Name.Should().Be("Đọc sách 30 phút");
        loaded.Reminders.Should().Equal(new Reminder(new TimeOnly(21, 15)));
        loaded.Version.Should().BeGreaterThan(firstVersion);
    }

    [Fact]
    public async Task Mutations_OnMissingHabit_ThrowNotFound()
    {
        using var f = new V3Fixture();

        await FluentActions.Awaiting(() => f.Habits.UpdateAsync(V3Fixture.NewHabit()))
            .Should().ThrowAsync<HabitNotFoundException>();
        await FluentActions.Awaiting(() => f.Habits.ArchiveAsync(Guid.NewGuid()))
            .Should().ThrowAsync<HabitNotFoundException>();
        await FluentActions.Awaiting(() => f.Habits.SoftDeleteAsync(Guid.NewGuid()))
            .Should().ThrowAsync<HabitNotFoundException>();
    }

    [Fact]
    public async Task SetSortKey_ChangesOnlyOneRow_AndRejectsInvalidKey()
    {
        using var f = new V3Fixture();
        var a = V3Fixture.NewHabit("A", "V");
        var b = V3Fixture.NewHabit("B", "W");
        await f.Habits.InsertAsync(a);
        await f.Habits.InsertAsync(b);
        var outboxBefore = f.OutboxCount;

        await f.Habits.SetSortKeyAsync(b.Id, FractionalIndex.Before("V"));

        (await f.Habits.GetAllActiveAsync()).Select(h => h.Name).Should().Equal("B", "A");
        (f.OutboxCount - outboxBefore).Should().Be(1, "chỉ một dòng được ghi khi kéo-thả");
        await FluentActions.Awaiting(() => f.Habits.SetSortKeyAsync(a.Id, "bad!")).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Purge_RemovesOnlyOldDeletedHabitsWithNoPendingSync()
    {
        using var f = new V3Fixture();
        var gone = V3Fixture.NewHabit("gone", "V");
        var keep = V3Fixture.NewHabit("keep", "W");
        await f.Habits.InsertAsync(gone);
        await f.Habits.InsertAsync(keep);
        await f.Habits.SoftDeleteAsync(gone.Id);

        // Còn thay đổi chờ đồng bộ → chưa được xóa hẳn.
        (await f.Habits.PurgeDeletedAsync(DateTime.UtcNow.AddDays(1))).Should().Be(0);

        f.Execute("DELETE FROM sync_outbox;");

        (await f.Habits.PurgeDeletedAsync(DateTime.UtcNow.AddDays(-1))).Should().Be(0, "chưa đủ 30 ngày");
        (await f.Habits.PurgeDeletedAsync(DateTime.UtcNow.AddDays(1))).Should().Be(1);
        (await f.Habits.GetByIdAsync(gone.Id)).Should().BeNull();
        (await f.Habits.GetByIdAsync(keep.Id)).Should().NotBeNull();
    }
}

public class OutboxAndAtomicityTests
{
    [Fact]
    public async Task EveryWrite_EnqueuesExactlyOneOutboxRow_WithHlcAndPayload()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();

        await f.Habits.InsertAsync(habit);
        await f.Habits.ArchiveAsync(habit.Id);
        await f.DayLogs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = new DateOnly(2026, 2, 1), Value = 1, Status = DayStatus.Done });

        f.OutboxCount.Should().Be(3);
        f.Strings("SELECT entity FROM sync_outbox ORDER BY seq;").Should().Equal("habit", "habit", "dayLog");

        foreach (var hlc in f.Strings("SELECT hlc FROM sync_outbox ORDER BY seq;"))
        {
            Hlc.TryParse(hlc, out var parsed).Should().BeTrue();
            parsed.IsEmpty.Should().BeFalse();
        }

        var payload = JsonDocument.Parse(f.Strings("SELECT payload_json FROM sync_outbox WHERE seq = 1;")[0]);
        payload.RootElement.GetProperty("id").GetGuid().Should().Be(habit.Id);
        payload.RootElement.GetProperty("frequency").GetProperty("type").GetString().Should().Be("daily");
    }

    [Fact]
    public async Task OutboxHlcs_AreStrictlyIncreasingInOrderOfWrites()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        await f.Habits.InsertAsync(habit);

        for (var i = 0; i < 20; i++)
        {
            await f.DayLogs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = new DateOnly(2026, 2, 1).AddDays(i), Value = 1, Status = DayStatus.Done });
        }

        var hlcs = f.Strings("SELECT hlc FROM sync_outbox ORDER BY seq;");
        hlcs.Should().BeInAscendingOrder(StringComparer.Ordinal);
        hlcs.Distinct().Should().HaveCount(hlcs.Count);
    }

    [Fact]
    public async Task FailedWrite_LeavesNoRowAndNoOutbox_AndPublishesNothing()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        await f.Habits.InsertAsync(habit);
        var outbox = f.OutboxCount;
        f.Changes.Clear();

        // Trùng khóa chính → lỗi.
        await FluentActions.Awaiting(() => f.Habits.InsertAsync(habit)).Should().ThrowAsync<SqliteException>();

        // Vi phạm khóa ngoại → lỗi.
        await FluentActions.Awaiting(() => f.DayLogs.UpsertAsync(
                new DayLog { HabitId = Guid.NewGuid(), Day = new DateOnly(2026, 2, 1), Value = 1, Status = DayStatus.Done }))
            .Should().ThrowAsync<SqliteException>();

        f.OutboxCount.Should().Be(outbox);
        f.Scalar("SELECT COUNT(*) FROM day_logs;").Should().Be(0);
        f.Changes.Should().BeEmpty("bus chỉ phát sau khi commit thành công");
    }

    [Fact]
    public async Task Writes_PublishChangeNotifications()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        var day = new DateOnly(2026, 2, 1);

        await f.Habits.InsertAsync(habit);
        await f.DayLogs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = day, Value = 1, Status = DayStatus.Done });

        f.Changes.Should().Contain(new DataChange(DataEntity.Habit, habit.Id.ToString("D")));
        f.Changes.Should().Contain(new DataChange(DataEntity.DayLog, $"{habit.Id:D}|2026-02-01"));
    }
}

public class DayLogRepositoryTests
{
    private static readonly DateOnly D1 = new(2026, 2, 1);

    [Fact]
    public async Task UpsertTwice_ReplacesRow_AndKeepsNoteMoodAndCheckedAt()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        await f.Habits.InsertAsync(habit);
        var checkedAt = new DateTime(2026, 2, 1, 14, 30, 0, DateTimeKind.Utc);

        await f.DayLogs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = D1, Value = 1, Status = DayStatus.Done });
        await f.DayLogs.UpsertAsync(new DayLog
        {
            HabitId = habit.Id,
            Day = D1,
            Value = 2,
            Status = DayStatus.Done,
            Note = "ngon",
            Mood = 4,
            CheckedAt = checkedAt
        });

        var log = (await f.DayLogs.GetAsync(habit.Id, D1))!;
        log.Value.Should().Be(2);
        log.Note.Should().Be("ngon");
        log.Mood.Should().Be(4);
        log.CheckedAt.Should().Be(checkedAt);
        f.Scalar("SELECT COUNT(*) FROM day_logs;").Should().Be(1);
    }

    [Fact]
    public async Task Range_And_ForDay_Queries()
    {
        using var f = new V3Fixture();
        var a = V3Fixture.NewHabit("A", "V");
        var b = V3Fixture.NewHabit("B", "W");
        await f.Habits.InsertAsync(a);
        await f.Habits.InsertAsync(b);

        foreach (var offset in Enumerable.Range(0, 10))
        {
            await f.DayLogs.UpsertAsync(new DayLog { HabitId = a.Id, Day = D1.AddDays(offset), Value = 1, Status = DayStatus.Done });
        }

        await f.DayLogs.UpsertAsync(new DayLog { HabitId = b.Id, Day = D1.AddDays(3), Value = 1, Status = DayStatus.Done });

        (await f.DayLogs.GetRangeAsync(a.Id, D1.AddDays(2), D1.AddDays(4))).Select(l => l.Day)
            .Should().Equal(D1.AddDays(2), D1.AddDays(3), D1.AddDays(4));
        (await f.DayLogs.GetAllForHabitAsync(a.Id)).Should().HaveCount(10);
        (await f.DayLogs.GetForDayAsync(D1.AddDays(3))).Select(l => l.HabitId).Should().BeEquivalentTo([a.Id, b.Id]);
    }

    [Fact]
    public async Task Delete_RemovesRow_AndEmitsDeleteOp()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        await f.Habits.InsertAsync(habit);
        await f.DayLogs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = D1, Value = 1, Status = DayStatus.Done });

        await f.DayLogs.DeleteAsync(habit.Id, D1);
        await f.DayLogs.DeleteAsync(habit.Id, D1); // lần hai không có gì để xóa

        (await f.DayLogs.GetAsync(habit.Id, D1)).Should().BeNull();
        f.Strings("SELECT op FROM sync_outbox WHERE entity = 'dayLog' ORDER BY seq;").Should().Equal("upsert", "delete");
    }

    [Fact]
    public async Task DeletingHabit_Hard_CascadesToLogs()
    {
        using var f = new V3Fixture();
        var habit = V3Fixture.NewHabit();
        await f.Habits.InsertAsync(habit);
        await f.DayLogs.UpsertAsync(new DayLog { HabitId = habit.Id, Day = D1, Value = 1, Status = DayStatus.Done });
        await f.Habits.SoftDeleteAsync(habit.Id);
        f.Execute("DELETE FROM sync_outbox;");

        await f.Habits.PurgeDeletedAsync(DateTime.UtcNow.AddDays(1));

        f.Scalar("SELECT COUNT(*) FROM day_logs;").Should().Be(0);
    }
}

public class RoutineAndProfileRepositoryTests
{
    [Fact]
    public async Task Routine_MembersComeFromHabits_AndDeleteDetachesThem()
    {
        using var f = new V3Fixture();
        var routine = new Routine(Guid.NewGuid(), "Buổi sáng", HabitIcon.Emoji("🌅"), TimeOfDay.Morning, "V", []);
        await f.Routines.UpsertAsync(routine);

        var first = V3Fixture.NewHabit("Thiền", "V");
        first.RoutineId = routine.Id;
        first.RoutineOrder = 2;
        var second = V3Fixture.NewHabit("Giãn cơ", "W");
        second.RoutineId = routine.Id;
        second.RoutineOrder = 1;
        await f.Habits.InsertAsync(first);
        await f.Habits.InsertAsync(second);

        var loaded = (await f.Routines.GetAllAsync()).Should().ContainSingle().Subject;
        loaded.HabitIds.Should().Equal(second.Id, first.Id);

        await f.Routines.DeleteAsync(routine.Id);

        (await f.Routines.GetAllAsync()).Should().BeEmpty();
        (await f.Habits.GetByIdAsync(first.Id))!.RoutineId.Should().BeNull();
        (await f.Habits.GetAllActiveAsync()).Should().HaveCount(2, "xóa nhóm không xóa thói quen");
    }

    [Fact]
    public async Task Profile_DefaultsThenRoundTrips_AndEnqueuesOnlyChangedKeys()
    {
        using var f = new V3Fixture();

        (await f.Profile.GetAsync()).Level.Should().Be(1);

        var profile = (await f.Profile.GetAsync()) with { DisplayName = "Linh", Xp = 1234, Level = 7, FreezeTokens = 2 };
        await f.Profile.SaveAsync(profile);
        var afterFirst = f.OutboxCount;

        (await f.Profile.GetAsync()).Should().BeEquivalentTo(profile);

        // Lưu lại y hệt → không có gì thay đổi → không thêm outbox.
        await f.Profile.SaveAsync(profile);
        f.OutboxCount.Should().Be(afterFirst);

        await f.Profile.SaveAsync(profile with { Xp = 1300 });
        f.OutboxCount.Should().Be(afterFirst + 1, "chỉ khóa xp thay đổi");
    }
}
