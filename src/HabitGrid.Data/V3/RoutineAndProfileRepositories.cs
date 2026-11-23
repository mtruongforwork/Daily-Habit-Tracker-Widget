using System.Globalization;
using Microsoft.Data.Sqlite;
using HabitGrid.Core.Domain;
using Sql = HabitGrid.Data.Repositories.SqlMapping;

namespace HabitGrid.Data.V3;

public sealed class RoutineRepository : RepositoryBase, IRoutineRepository
{
    public RoutineRepository(HabitGridDatabase database, IHlcClock clock, IDataChangeBus? bus = null)
        : base(database, clock, bus)
    {
    }

    public async Task<IReadOnlyList<Routine>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await Database.OpenConnectionAsync();
        return await ReadAllAsync(connection, null, ct);
    }

    internal static async Task<IReadOnlyList<Routine>> ReadAllAsync(
        SqliteConnection connection, SqliteTransaction? transaction, CancellationToken ct)
    {

        var members = new Dictionary<Guid, List<Guid>>();
        await using (var memberCommand = Command(connection, transaction, """
            SELECT routine_id, id FROM habits
            WHERE routine_id IS NOT NULL AND deleted_at IS NULL
            ORDER BY routine_id, routine_order, sort_key;
            """))
        await using (var memberReader = await memberCommand.ExecuteReaderAsync(ct))
        {
            while (await memberReader.ReadAsync(ct))
            {
                var routineId = Guid.Parse(memberReader.GetString(0));
                if (!members.TryGetValue(routineId, out var list))
                {
                    members[routineId] = list = [];
                }

                list.Add(Guid.Parse(memberReader.GetString(1)));
            }
        }

        var routines = new List<Routine>();
        await using var command = Command(connection, transaction, """
            SELECT id, name, icon, time_of_day, sort_key FROM routines
            WHERE deleted_at IS NULL ORDER BY sort_key;
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var id = Guid.Parse(reader.GetString(0));
            routines.Add(new Routine(
                id,
                reader.GetString(1),
                new HabitIcon(reader.GetString(2)),
                (TimeOfDay)reader.GetInt32(3),
                reader.GetString(4),
                members.TryGetValue(id, out var ids) ? ids : []));
        }

        return routines;
    }

    /// <summary>Ghi thông tin nhóm. Thành viên được quy định bởi <c>Habit.RoutineId</c>, không ghi ở đây.</summary>
    public async Task UpsertAsync(Routine routine, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(routine);
        var hlc = Clock.Now();

        await InTransactionAsync((connection, transaction) =>
        {
            using (var command = Command(connection, transaction, """
                INSERT INTO routines (id, name, icon, time_of_day, sort_key, hlc, deleted_at)
                VALUES ($id, $name, $icon, $tod, $sort, $hlc, NULL)
                ON CONFLICT(id) DO UPDATE SET name = excluded.name, icon = excluded.icon,
                    time_of_day = excluded.time_of_day, sort_key = excluded.sort_key,
                    hlc = excluded.hlc, deleted_at = NULL;
                """))
            {
                command.Parameters.AddWithValue("$id", routine.Id.ToString("D"));
                command.Parameters.AddWithValue("$name", routine.Name);
                command.Parameters.AddWithValue("$icon", routine.Icon.Value);
                command.Parameters.AddWithValue("$tod", (int)routine.TimeOfDay);
                command.Parameters.AddWithValue("$sort", routine.SortKey);
                command.Parameters.AddWithValue("$hlc", hlc.ToString());
                command.ExecuteNonQuery();
            }

            Enqueue(connection, transaction, "routine", routine.Id.ToString("D"), "upsert", routine with { HabitIds = [] }, hlc);
            return 0;
        });

        Publish(DataEntity.Routine, routine.Id.ToString("D"));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var hlc = Clock.Now();

        var rows = await InTransactionAsync((connection, transaction) =>
        {
            int affected;
            using (var command = Command(connection, transaction, "UPDATE routines SET deleted_at = $now, hlc = $hlc WHERE id = $id AND deleted_at IS NULL;"))
            {
                command.Parameters.AddWithValue("$now", Sql.ToSqlTimestamp(DateTime.UtcNow));
                command.Parameters.AddWithValue("$hlc", hlc.ToString());
                command.Parameters.AddWithValue("$id", id.ToString("D"));
                affected = command.ExecuteNonQuery();
            }

            if (affected > 0)
            {
                // Thói quen trong nhóm được tách khỏi nhóm, không bị xóa.
                using var detach = Command(connection, transaction, "UPDATE habits SET routine_id = NULL WHERE routine_id = $id;");
                detach.Parameters.AddWithValue("$id", id.ToString("D"));
                detach.ExecuteNonQuery();

                Enqueue(connection, transaction, "routine", id.ToString("D"), "delete", new { id, version = hlc }, hlc);
            }

            return affected;
        });

        if (rows > 0)
        {
            Publish(DataEntity.Routine, id.ToString("D"));
        }
    }
}

public sealed class ProfileRepository : RepositoryBase, IProfileRepository
{
    private const string DisplayName = "display_name";
    private const string BuddyName = "buddy_name";
    private const string BuddyColor = "buddy_color";
    private const string Xp = "xp";
    private const string Level = "level";
    private const string Freeze = "freeze_tokens";
    private const string TimeZone = "timezone_id";

    public ProfileRepository(HabitGridDatabase database, IHlcClock clock, IDataChangeBus? bus = null)
        : base(database, clock, bus)
    {
    }

    public async Task<UserProfile> GetAsync(CancellationToken ct = default)
    {
        await using var connection = await Database.OpenConnectionAsync();
        return await ReadAsync(connection, null, ct);
    }

    internal static async Task<UserProfile> ReadAsync(
        SqliteConnection connection, SqliteTransaction? transaction, CancellationToken ct)
    {
        await using var command = Command(connection, transaction, "SELECT key, value FROM profile;");
        await using var reader = await command.ExecuteReaderAsync(ct);

        var values = new Dictionary<string, string>();
        while (await reader.ReadAsync(ct))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        var defaults = new UserProfile();

        return new UserProfile
        {
            DisplayName = values.GetValueOrDefault(DisplayName, defaults.DisplayName),
            BuddyName = values.GetValueOrDefault(BuddyName, defaults.BuddyName),
            BuddyColor = values.GetValueOrDefault(BuddyColor, defaults.BuddyColor),
            Xp = values.TryGetValue(Xp, out var xp) ? long.Parse(xp, CultureInfo.InvariantCulture) : defaults.Xp,
            Level = values.TryGetValue(Level, out var level) ? int.Parse(level, CultureInfo.InvariantCulture) : defaults.Level,
            FreezeTokens = values.TryGetValue(Freeze, out var freeze) ? int.Parse(freeze, CultureInfo.InvariantCulture) : defaults.FreezeTokens,
            TimeZoneId = values.GetValueOrDefault(TimeZone, defaults.TimeZoneId)
        };
    }

    /// <summary>Chỉ ghi (và đưa vào outbox) những khóa thực sự thay đổi so với giá trị đang lưu.</summary>
    public async Task SaveAsync(UserProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var changed = await InTransactionAsync((connection, transaction) =>
            Write(connection, transaction, profile, Clock, ct));

        if (changed > 0)
        {
            Publish(DataEntity.Profile, string.Empty);
        }
    }

    internal static int Write(SqliteConnection connection, SqliteTransaction transaction,
        UserProfile profile, IHlcClock clock, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var desired = new Dictionary<string, string>
        {
            [DisplayName] = profile.DisplayName,
            [BuddyName] = profile.BuddyName,
            [BuddyColor] = profile.BuddyColor,
            [Xp] = profile.Xp.ToString(CultureInfo.InvariantCulture),
            [Level] = profile.Level.ToString(CultureInfo.InvariantCulture),
            [Freeze] = profile.FreezeTokens.ToString(CultureInfo.InvariantCulture),
            [TimeZone] = profile.TimeZoneId
        };

        var existing = new Dictionary<string, string>();
        using (var read = Command(connection, transaction, "SELECT key, value FROM profile;"))
        using (var reader = read.ExecuteReader())
        {
            while (reader.Read())
            {
                existing[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var count = 0;
        foreach (var (key, value) in desired)
        {
            if (existing.TryGetValue(key, out var current) && current == value)
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();
            var hlc = clock.Now();
            using var write = Command(connection, transaction, """
                INSERT INTO profile (key, value, hlc) VALUES ($key, $value, $hlc)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value, hlc = excluded.hlc;
                """);
            write.Parameters.AddWithValue("$key", key);
            write.Parameters.AddWithValue("$value", value);
            write.Parameters.AddWithValue("$hlc", hlc.ToString());
            write.ExecuteNonQuery();

            Enqueue(connection, transaction, "profile", key, "upsert", new { key, value, version = hlc }, hlc);
            count++;
        }

        return count;
    }
}
