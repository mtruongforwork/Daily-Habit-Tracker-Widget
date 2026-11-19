using System.Text.Json;
using HabitGrid.Core.Domain;
using DayOfWeekMask = HabitGrid.Core.Models.DayOfWeekMask;
using Microsoft.Data.Sqlite;
using Sql = HabitGrid.Data.Repositories.SqlMapping;

namespace HabitGrid.Data.V3;

public sealed class HabitRepository : RepositoryBase, IHabitRepository
{
    internal const string Columns = """
        id, name, description, icon, color_hue, kind, target_value, target_unit, frequency_json, time_of_day,
        start_date, ask_mood, allow_notes, allow_freeze, routine_id, routine_order, tags_json, sort_key,
        archived_at, deleted_at, created_at, updated_at, hlc, shared_challenge_id, skip_weekends, area_id
        """;

    public HabitRepository(HabitGridDatabase database, IHlcClock clock, IDataChangeBus? bus = null)
        : base(database, clock, bus)
    {
    }

    public async Task<IReadOnlyList<Habit>> GetAllActiveAsync(CancellationToken ct = default) =>
        await QueryAsync("deleted_at IS NULL AND archived_at IS NULL");

    public async Task<IReadOnlyList<Habit>> GetAllAsync(CancellationToken ct = default) =>
        await QueryAsync("deleted_at IS NULL");

    public async Task<Habit?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await Database.OpenConnectionAsync();
        return LoadById(connection, null, id);
    }

    public async Task InsertAsync(Habit habit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(habit);

        await InTransactionAsync((connection, transaction) =>
        {
            Stamp(habit);
            using (var command = Command(connection, transaction, $"INSERT INTO habits ({Columns}) VALUES ({Placeholders()});"))
            {
                Bind(command, habit);
                command.ExecuteNonQuery();
            }

            WriteReminders(connection, transaction, habit);
            Enqueue(connection, transaction, "habit", habit.Id.ToString("D"), "upsert", habit, habit.Version);
            return 0;
        });

        Publish(DataEntity.Habit, habit.Id.ToString("D"));
    }

    public async Task UpdateAsync(Habit habit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(habit);

        await InTransactionAsync((connection, transaction) =>
        {
            if (LoadById(connection, transaction, habit.Id) is null)
            {
                throw new HabitNotFoundException(habit.Id);
            }

            Stamp(habit);
            Write(connection, transaction, habit);
            return 0;
        });

        Publish(DataEntity.Habit, habit.Id.ToString("D"));
    }

    public Task SoftDeleteAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, habit => habit.DeletedAt = DateTime.UtcNow);

    public Task RestoreAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, habit => habit.DeletedAt = null);

    public Task ArchiveAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, habit => habit.ArchivedAt ??= DateTime.UtcNow);

    public Task UnarchiveAsync(Guid id, CancellationToken ct = default) =>
        MutateAsync(id, habit => habit.ArchivedAt = null);

    public Task SetSortKeyAsync(Guid id, string sortKey, CancellationToken ct = default)
    {
        if (!FractionalIndex.IsValid(sortKey))
        {
            throw new ArgumentException($"Khóa sắp xếp không hợp lệ: '{sortKey}'.", nameof(sortKey));
        }

        return MutateAsync(id, habit => habit.SortKey = sortKey);
    }

    public async Task<int> CountActiveAsync(CancellationToken ct = default)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = Command(
            connection,
            null,
            "SELECT COUNT(*) FROM habits WHERE deleted_at IS NULL AND archived_at IS NULL;");

        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<int> PurgeDeletedAsync(DateTime olderThan, CancellationToken ct = default)
    {
        var purged = await InTransactionAsync((connection, transaction) =>
        {
            using var command = Command(connection, transaction, """
                DELETE FROM habits
                WHERE deleted_at IS NOT NULL
                  AND deleted_at < $cutoff
                  AND NOT EXISTS (
                      SELECT 1 FROM sync_outbox o
                      WHERE o.entity IN ('habit', 'dayLog') AND o.entity_id LIKE habits.id || '%');
                """);
            command.Parameters.AddWithValue("$cutoff", Sql.ToSqlTimestamp(olderThan));
            return command.ExecuteNonQuery();
        });

        if (purged > 0)
        {
            Publish(DataEntity.Everything, string.Empty);
        }

        return purged;
    }

    // ------------------------------------------------------------------

    private async Task MutateAsync(Guid id, Action<Habit> mutation)
    {
        await InTransactionAsync((connection, transaction) =>
        {
            var habit = LoadById(connection, transaction, id) ?? throw new HabitNotFoundException(id);

            mutation(habit);
            Stamp(habit);
            Write(connection, transaction, habit);
            return 0;
        });

        Publish(DataEntity.Habit, id.ToString("D"));
    }

    private void Stamp(Habit habit)
    {
        habit.UpdatedAt = DateTime.UtcNow;
        habit.Version = Clock.Now();
    }

    private static void Write(SqliteConnection connection, SqliteTransaction transaction, Habit habit)
    {
        using (var command = Command(connection, transaction, """
            UPDATE habits SET
                name = $name, description = $description, icon = $icon, color_hue = $color_hue, kind = $kind,
                target_value = $target_value, target_unit = $target_unit, frequency_json = $frequency_json,
                time_of_day = $time_of_day, start_date = $start_date, ask_mood = $ask_mood, allow_notes = $allow_notes,
                allow_freeze = $allow_freeze, routine_id = $routine_id, routine_order = $routine_order,
                tags_json = $tags_json, sort_key = $sort_key, archived_at = $archived_at, deleted_at = $deleted_at,
                updated_at = $updated_at, hlc = $hlc, shared_challenge_id = $shared_challenge_id,
                skip_weekends = $skip_weekends, area_id = $area_id
            WHERE id = $id;
            """))
        {
            Bind(command, habit);
            command.ExecuteNonQuery();
        }

        WriteReminders(connection, transaction, habit);
        Enqueue(connection, transaction, "habit", habit.Id.ToString("D"), "upsert", habit, habit.Version);
    }

    private static void WriteReminders(SqliteConnection connection, SqliteTransaction transaction, Habit habit)
    {
        using (var delete = Command(connection, transaction, "DELETE FROM reminders WHERE habit_id = $id;"))
        {
            delete.Parameters.AddWithValue("$id", habit.Id.ToString("D"));
            delete.ExecuteNonQuery();
        }

        var position = 0;
        foreach (var reminder in habit.Reminders)
        {
            using var insert = Command(connection, transaction, """
                INSERT INTO reminders (id, habit_id, position, time, days_mask, smart, follow_up, hlc)
                VALUES ($rid, $habit, $position, $time, $days, $smart, $followUp, $hlc);
                """);
            insert.Parameters.AddWithValue("$rid", Guid.CreateVersion7().ToString("D"));
            insert.Parameters.AddWithValue("$habit", habit.Id.ToString("D"));
            insert.Parameters.AddWithValue("$position", position++);
            insert.Parameters.AddWithValue("$time", reminder.Time.ToString(Sql.TimeFormat));
            insert.Parameters.AddWithValue("$days", (int)reminder.Days);
            insert.Parameters.AddWithValue("$smart", reminder.Smart ? 1 : 0);
            insert.Parameters.AddWithValue("$followUp", reminder.FollowUp ? 1 : 0);
            insert.Parameters.AddWithValue("$hlc", habit.Version.ToString());
            insert.ExecuteNonQuery();
        }
    }

    private async Task<IReadOnlyList<Habit>> QueryAsync(string where)
    {
        await using var connection = await Database.OpenConnectionAsync();
        return Query(connection, null, where);
    }

    internal static List<Habit> Query(SqliteConnection connection, SqliteTransaction? transaction, string where, Guid? id = null)
    {
        var habits = new List<Habit>();

        using (var command = Command(connection, transaction, $"SELECT {Columns} FROM habits WHERE {where} ORDER BY sort_key, created_at;"))
        {
            if (id is not null)
            {
                command.Parameters.AddWithValue("$id", id.Value.ToString("D"));
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                habits.Add(Read(reader));
            }
        }

        if (habits.Count > 0)
        {
            AttachReminders(connection, transaction, habits);
        }

        return habits;
    }

    internal static Habit? LoadById(SqliteConnection connection, SqliteTransaction? transaction, Guid id) =>
        Query(connection, transaction, "id = $id", id).FirstOrDefault();

    private static void AttachReminders(SqliteConnection connection, SqliteTransaction? transaction, List<Habit> habits)
    {
        var byId = habits.ToDictionary(h => h.Id.ToString("D"));

        using var command = Command(
            connection,
            transaction,
            "SELECT habit_id, time, days_mask, smart, follow_up FROM reminders ORDER BY habit_id, position;");
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (!byId.TryGetValue(reader.GetString(0), out var habit))
            {
                continue;
            }

            habit.Reminders.Add(new Reminder(
                Sql.ToTimeOnly(reader.GetString(1))!.Value,
                (DayOfWeekMask)reader.GetInt32(2),
                reader.GetInt32(3) != 0,
                reader.GetInt32(4) != 0));
        }
    }

    internal static Habit Read(SqliteDataReader r)
    {
        var frequency = JsonSerializer.Deserialize<HabitFrequency>(r.GetString(8), DomainJson.Options)
                        ?? new HabitFrequency.Daily();

        return new Habit
        {
            Id = Guid.Parse(r.GetString(0)),
            Name = r.GetString(1),
            Description = r.IsDBNull(2) ? null : r.GetString(2),
            Icon = new HabitIcon(r.GetString(3)),
            ColorHue = r.GetString(4),
            Kind = (Core.Domain.HabitKind)r.GetInt32(5),
            Target = new HabitTarget(r.GetInt32(6), r.IsDBNull(7) ? null : r.GetString(7)),
            Frequency = frequency,
            TimeOfDay = (TimeOfDay)r.GetInt32(9),
            StartDate = Sql.ToDateOnly(r.GetString(10)),
            AskMood = r.GetInt32(11) != 0,
            AllowNotes = r.GetInt32(12) != 0,
            AllowFreeze = r.GetInt32(13) != 0,
            RoutineId = r.IsDBNull(14) ? null : Guid.Parse(r.GetString(14)),
            RoutineOrder = r.GetInt32(15),
            Tags = JsonSerializer.Deserialize<List<string>>(r.GetString(16), DomainJson.Options) ?? [],
            SortKey = r.GetString(17),
            ArchivedAt = Sql.ToTimestampOrNull(r.IsDBNull(18) ? null : r.GetString(18)),
            DeletedAt = Sql.ToTimestampOrNull(r.IsDBNull(19) ? null : r.GetString(19)),
            CreatedAt = Sql.ToTimestamp(r.GetString(20)),
            UpdatedAt = Sql.ToTimestamp(r.GetString(21)),
            Version = Hlc.Parse(r.GetString(22)),
            SharedChallengeId = r.IsDBNull(23) ? null : Guid.Parse(r.GetString(23)),
            SkipWeekends = r.GetInt32(24) != 0,
            AreaId = r.IsDBNull(25) ? null : Guid.Parse(r.GetString(25))
        };
    }

    internal static string Placeholders() =>
        "$id, $name, $description, $icon, $color_hue, $kind, $target_value, $target_unit, $frequency_json, $time_of_day, " +
        "$start_date, $ask_mood, $allow_notes, $allow_freeze, $routine_id, $routine_order, $tags_json, $sort_key, " +
        "$archived_at, $deleted_at, $created_at, $updated_at, $hlc, $shared_challenge_id, $skip_weekends, $area_id";

    internal static void Bind(SqliteCommand c, Habit h)
    {
        c.Parameters.AddWithValue("$id", h.Id.ToString("D"));
        c.Parameters.AddWithValue("$name", h.Name);
        c.Parameters.AddWithValue("$description", (object?)h.Description ?? DBNull.Value);
        c.Parameters.AddWithValue("$icon", h.Icon.Value);
        c.Parameters.AddWithValue("$color_hue", h.ColorHue);
        c.Parameters.AddWithValue("$kind", (int)h.Kind);
        c.Parameters.AddWithValue("$target_value", h.Target.Value);
        c.Parameters.AddWithValue("$target_unit", (object?)h.Target.Unit ?? DBNull.Value);
        c.Parameters.AddWithValue("$frequency_json", JsonSerializer.Serialize(h.Frequency, DomainJson.Options));
        c.Parameters.AddWithValue("$time_of_day", (int)h.TimeOfDay);
        c.Parameters.AddWithValue("$start_date", Sql.ToSqlDate(h.StartDate));
        c.Parameters.AddWithValue("$ask_mood", h.AskMood ? 1 : 0);
        c.Parameters.AddWithValue("$allow_notes", h.AllowNotes ? 1 : 0);
        c.Parameters.AddWithValue("$allow_freeze", h.AllowFreeze ? 1 : 0);
        c.Parameters.AddWithValue("$routine_id", h.RoutineId is { } rid ? rid.ToString("D") : DBNull.Value);
        c.Parameters.AddWithValue("$routine_order", h.RoutineOrder);
        c.Parameters.AddWithValue("$tags_json", JsonSerializer.Serialize(h.Tags, DomainJson.Options));
        c.Parameters.AddWithValue("$sort_key", h.SortKey);
        c.Parameters.AddWithValue("$archived_at", (object?)Sql.ToSqlTimestamp(h.ArchivedAt) ?? DBNull.Value);
        c.Parameters.AddWithValue("$deleted_at", (object?)Sql.ToSqlTimestamp(h.DeletedAt) ?? DBNull.Value);
        c.Parameters.AddWithValue("$created_at", Sql.ToSqlTimestamp(h.CreatedAt));
        c.Parameters.AddWithValue("$updated_at", Sql.ToSqlTimestamp(h.UpdatedAt));
        c.Parameters.AddWithValue("$hlc", h.Version.ToString());
        c.Parameters.AddWithValue("$shared_challenge_id", h.SharedChallengeId is { } sid ? sid.ToString("D") : DBNull.Value);
        c.Parameters.AddWithValue("$skip_weekends", h.SkipWeekends ? 1 : 0);
        c.Parameters.AddWithValue("$area_id", h.AreaId is { } aid ? aid.ToString("D") : DBNull.Value);
    }
}
