using HabitGrid.Core.Domain;
using Microsoft.Data.Sqlite;
using Sql = HabitGrid.Data.Repositories.SqlMapping;

namespace HabitGrid.Data.V3;

public sealed class DayLogRepository : RepositoryBase, IDayLogRepository
{
    internal const string Columns = "habit_id, day, value, status, note, mood, checked_at, updated_at, hlc";

    public DayLogRepository(HabitGridDatabase database, IHlcClock clock, IDataChangeBus? bus = null)
        : base(database, clock, bus)
    {
    }

    public async Task<DayLog?> GetAsync(Guid habitId, DateOnly day, CancellationToken ct = default)
    {
        await using var connection = await Database.OpenConnectionAsync();
        ct.ThrowIfCancellationRequested();
        return Load(connection, null, habitId, day);
    }

    private static DayLog? Load(SqliteConnection connection, SqliteTransaction? transaction, Guid habitId, DateOnly day)
    {
        using var command = Command(
            connection,
            transaction,
            $"SELECT {Columns} FROM day_logs WHERE habit_id = $habit AND day = $day;");
        command.Parameters.AddWithValue("$habit", habitId.ToString("D"));
        command.Parameters.AddWithValue("$day", Sql.ToSqlDate(day));

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<DayLog>> GetRangeAsync(
        Guid habitId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default) =>
        await ListAsync(
            "habit_id = $habit AND day >= $from AND day <= $to",
            c =>
            {
                c.Parameters.AddWithValue("$habit", habitId.ToString("D"));
                c.Parameters.AddWithValue("$from", Sql.ToSqlDate(from));
                c.Parameters.AddWithValue("$to", Sql.ToSqlDate(to));
            },
            ct);

    public async Task<IReadOnlyList<DayLog>> GetAllForHabitAsync(Guid habitId, CancellationToken ct = default) =>
        await ListAsync("habit_id = $habit", c => c.Parameters.AddWithValue("$habit", habitId.ToString("D")), ct);

    public async Task<IReadOnlyList<DayLog>> GetForDayAsync(DateOnly day, CancellationToken ct = default) =>
        await ListAsync("day = $day", c => c.Parameters.AddWithValue("$day", Sql.ToSqlDate(day)), ct);

    public async Task UpsertAsync(DayLog log, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(log);

        await InTransactionAsync((connection, transaction) => Write(connection, transaction, log));
        Publish(DataEntity.DayLog, Key(log.HabitId, log.Day));
    }

    public async Task DeleteAsync(Guid habitId, DateOnly day, CancellationToken ct = default)
    {
        var removed = await InTransactionAsync((connection, transaction) => Delete(connection, transaction, habitId, day));
        if (removed)
        {
            Publish(DataEntity.DayLog, Key(habitId, day));
        }
    }

    public async Task<DayLog?> UpdateAsync(Guid habitId, DateOnly day,
        Func<Habit, DayLog?, DayLog?> update, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (log, changed) = await InTransactionAsync<(DayLog?, bool)>((connection, transaction) =>
        {
            ct.ThrowIfCancellationRequested();
            var habit = HabitRepository.LoadById(connection, transaction, habitId);
            if (habit is null || habit.DeletedAt is not null)
            {
                return (null, false);
            }

            var previous = Load(connection, transaction, habitId, day);
            var next = update(habit, previous);
            if (ReferenceEquals(previous, next))
            {
                return (previous, false);
            }

            ct.ThrowIfCancellationRequested();
            if (next is null)
            {
                return (null, Delete(connection, transaction, habitId, day));
            }

            return (Write(connection, transaction, next with { HabitId = habitId, Day = day }), true);
        });

        if (changed)
        {
            Publish(DataEntity.DayLog, Key(habitId, day));
        }

        return log;
    }

    private DayLog Write(SqliteConnection connection, SqliteTransaction transaction, DayLog log)
    {
        var stamped = log with { UpdatedAt = DateTime.UtcNow, Version = Clock.Now() };
        using (var command = Command(connection, transaction, $"""
            INSERT INTO day_logs ({Columns})
            VALUES ($habit, $day, $value, $status, $note, $mood, $checked_at, $updated_at, $hlc)
            ON CONFLICT(habit_id, day) DO UPDATE SET
                value = excluded.value, status = excluded.status, note = excluded.note, mood = excluded.mood,
                checked_at = excluded.checked_at, updated_at = excluded.updated_at, hlc = excluded.hlc;
            """))
        {
            Bind(command, stamped);
            command.ExecuteNonQuery();
        }

        Enqueue(connection, transaction, "dayLog", Key(stamped.HabitId, stamped.Day), "upsert", stamped, stamped.Version);
        return stamped;
    }

    private bool Delete(SqliteConnection connection, SqliteTransaction transaction, Guid habitId, DateOnly day)
    {
        using var command = Command(connection, transaction, "DELETE FROM day_logs WHERE habit_id = $habit AND day = $day;");
        command.Parameters.AddWithValue("$habit", habitId.ToString("D"));
        command.Parameters.AddWithValue("$day", Sql.ToSqlDate(day));
        if (command.ExecuteNonQuery() == 0)
        {
            return false;
        }

        var hlc = Clock.Now();
        Enqueue(connection, transaction, "dayLog", Key(habitId, day), "delete", new { habitId, day, version = hlc }, hlc);
        return true;
    }

    internal static string Key(Guid habitId, DateOnly day) => $"{habitId:D}|{Sql.ToSqlDate(day)}";

    private async Task<IReadOnlyList<DayLog>> ListAsync(string where, Action<SqliteCommand> bind, CancellationToken ct)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = Command(
            connection,
            null,
            $"SELECT {Columns} FROM day_logs WHERE {where} ORDER BY day;");
        bind(command);

        var list = new List<DayLog>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    internal static DayLog Read(SqliteDataReader r) => new()
    {
        HabitId = Guid.Parse(r.GetString(0)),
        Day = Sql.ToDateOnly(r.GetString(1)),
        Value = r.GetInt32(2),
        Status = (DayStatus)r.GetInt32(3),
        Note = r.IsDBNull(4) ? null : r.GetString(4),
        Mood = r.IsDBNull(5) ? null : r.GetInt32(5),
        CheckedAt = Sql.ToTimestampOrNull(r.IsDBNull(6) ? null : r.GetString(6)),
        UpdatedAt = Sql.ToTimestamp(r.GetString(7)),
        Version = Hlc.Parse(r.GetString(8))
    };

    internal static void Bind(SqliteCommand c, DayLog l)
    {
        c.Parameters.AddWithValue("$habit", l.HabitId.ToString("D"));
        c.Parameters.AddWithValue("$day", Sql.ToSqlDate(l.Day));
        c.Parameters.AddWithValue("$value", l.Value);
        c.Parameters.AddWithValue("$status", (int)l.Status);
        c.Parameters.AddWithValue("$note", (object?)l.Note ?? DBNull.Value);
        c.Parameters.AddWithValue("$mood", (object?)l.Mood ?? DBNull.Value);
        c.Parameters.AddWithValue("$checked_at", (object?)Sql.ToSqlTimestamp(l.CheckedAt) ?? DBNull.Value);
        c.Parameters.AddWithValue("$updated_at", Sql.ToSqlTimestamp(l.UpdatedAt));
        c.Parameters.AddWithValue("$hlc", l.Version.ToString());
    }
}
