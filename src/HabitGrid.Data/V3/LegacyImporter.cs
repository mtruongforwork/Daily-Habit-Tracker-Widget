using System.Text.Json;
using HabitGrid.Core.Domain;
using Microsoft.Data.Sqlite;
using V1 = HabitGrid.Core.Models;
using Sql = HabitGrid.Data.Repositories.SqlMapping;

namespace HabitGrid.Data.V3;

public sealed record LegacyImportResult(
    bool Imported,
    int Habits,
    int DayLogs,
    int Widgets,
    int Settings,
    string? SkippedReason)
{
    public static LegacyImportResult Skipped(string reason) => new(false, 0, 0, 0, 0, reason);
}

/// <summary>
/// Nhập dữ liệu từ CSDL v1/v2 sang file v3 mới (SPEC-V2-02 A.3).
/// An toàn theo thiết kế: file cũ chỉ được MỞ ĐỌC, không bao giờ bị sửa; dữ liệu mới được dựng ở file tạm rồi đổi tên
/// nguyên tử, nên tại mọi thời điểm chỉ có "chưa có v3" hoặc "v3 hoàn chỉnh", không có trạng thái nửa vời.
/// </summary>
public static class LegacyImporter
{
    public const string ImportMarkerKey = "import.legacy_at";

    public static async Task<LegacyImportResult> ImportAsync(
        string legacyPath,
        string targetPath,
        IHlcClock clock,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(clock);

        if (!File.Exists(legacyPath))
        {
            return LegacyImportResult.Skipped("legacy.missing");
        }

        if (File.Exists(targetPath) && !await IsEmptyAndUnimportedAsync(targetPath))
        {
            return LegacyImportResult.Skipped("target.initialized");
        }

        var temp = targetPath + ".importing";
        DeleteDatabaseFiles(temp);

        try
        {
            var snapshot = ReadLegacy(legacyPath);

            if (snapshot.Habits.Count == 0 && snapshot.Settings.Count == 0)
            {
                return LegacyImportResult.Skipped("legacy.empty");
            }

            var habits = V1Mapper.ToV2(snapshot.Habits);
            var counts = Write(temp, legacyPath, habits, snapshot, clock);

            ct.ThrowIfCancellationRequested();

            // Giải phóng mọi kết nối tới file tạm rồi gộp WAL vào file chính trước khi đổi tên.
            await CheckpointAsync(temp);

            if (File.Exists(targetPath))
            {
                DeleteDatabaseFiles(targetPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
            File.Move(temp, targetPath);
            CleanSidecars(temp);

            return new LegacyImportResult(true, counts.Habits, counts.DayLogs, counts.Widgets, counts.Settings, null);
        }
        catch
        {
            DeleteDatabaseFiles(temp);
            throw;
        }
    }

    // ------------------------------------------------------------------

    private sealed record LegacySnapshot(
        List<V1.Habit> Habits,
        List<V1.Entry> Entries,
        List<LegacyWidget> Widgets,
        Dictionary<string, string> Settings);

    private sealed record LegacyWidget(Guid HabitId, string Monitor, double X, double Y, int Size, bool Visible);

    private sealed record Counts(int Habits, int DayLogs, int Widgets, int Settings);

    private static LegacySnapshot ReadLegacy(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var tables = ReadStrings(connection, "SELECT name FROM sqlite_master WHERE type = 'table';").ToHashSet();
        var habits = new List<V1.Habit>();
        var entries = new List<V1.Entry>();
        var widgets = new List<LegacyWidget>();
        var settings = new Dictionary<string, string>();

        if (tables.Contains("habits"))
        {
            var hasReadOnly = ReadStrings(connection, "SELECT name FROM pragma_table_info('habits');").Contains("read_only");

            using var command = connection.CreateCommand();
            command.CommandText = hasReadOnly
                ? """
                  SELECT id, name, icon, kind, target, schedule_mask, palette_id, custom_color, start_date, reminder_time,
                         reminder_mask, sort_order, archived_at, deleted_at, created_at, read_only FROM habits;
                  """
                : """
                  SELECT id, name, icon, kind, target, schedule_mask, palette_id, custom_color, start_date, reminder_time,
                         reminder_mask, sort_order, archived_at, deleted_at, created_at, 0 FROM habits;
                  """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                habits.Add(Repositories.SqlMapping.ToHabit(reader));
            }
        }

        if (tables.Contains("entries"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT habit_id, day, value, updated_at FROM entries;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(Repositories.SqlMapping.ToEntry(reader));
            }
        }

        if (tables.Contains("widget_state"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT habit_id, monitor, x, y, size, visible FROM widget_state;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                widgets.Add(new LegacyWidget(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetDouble(2),
                    reader.GetDouble(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5) != 0));
            }
        }

        if (tables.Contains("settings"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT key, value FROM settings;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                settings[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return new LegacySnapshot(habits, entries, widgets, settings);
    }

    private static Counts Write(
        string tempPath,
        string legacyPath,
        IReadOnlyList<Habit> habits,
        LegacySnapshot snapshot,
        IHlcClock clock)
    {
        using var database = HabitGridV3.Open(tempPath);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var byId = habits.ToDictionary(h => h.Id);
        var dayLogs = 0;

        foreach (var habit in habits)
        {
            habit.Version = clock.Now();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"INSERT INTO habits ({HabitRepository.Columns}) VALUES ({HabitRepository.Placeholders()});";
                HabitRepository.Bind(command, habit);
                command.ExecuteNonQuery();
            }

            var position = 0;
            foreach (var reminder in habit.Reminders)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO reminders (id, habit_id, position, time, days_mask, smart, follow_up, hlc)
                    VALUES ($id, $habit, $pos, $time, $days, 0, 0, $hlc);
                    """;
                command.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("D"));
                command.Parameters.AddWithValue("$habit", habit.Id.ToString("D"));
                command.Parameters.AddWithValue("$pos", position++);
                command.Parameters.AddWithValue("$time", reminder.Time.ToString(Sql.TimeFormat));
                command.Parameters.AddWithValue("$days", (int)reminder.Days);
                command.Parameters.AddWithValue("$hlc", habit.Version.ToString());
                command.ExecuteNonQuery();
            }

            Outbox(connection, transaction, "habit", habit.Id.ToString("D"), habit, habit.Version);
        }

        foreach (var entry in snapshot.Entries)
        {
            // Bỏ qua ghi nhận mồ côi (khóa ngoại sẽ từ chối).
            if (!byId.TryGetValue(entry.HabitId, out var habit))
            {
                continue;
            }

            var log = V1Mapper.ToV2(entry, habit) with { Version = clock.Now(), CheckedAt = null };

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT OR REPLACE INTO day_logs ({DayLogRepository.Columns})
                    VALUES ($habit, $day, $value, $status, $note, $mood, $checked_at, $updated_at, $hlc);
                    """;
                DayLogRepository.Bind(command, log);
                command.ExecuteNonQuery();
            }

            Outbox(connection, transaction, "dayLog", DayLogRepository.Key(log.HabitId, log.Day), log, log.Version);
            dayLogs++;
        }

        var widgets = 0;
        foreach (var widget in snapshot.Widgets)
        {
            if (!byId.ContainsKey(widget.HabitId))
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR REPLACE INTO widgets (id, kind, habit_ids_json, monitor, x, y, size, theme_id, opacity, visible)
                VALUES ($id, 'habitGrid', $ids, $monitor, $x, $y, $size, NULL, 1.0, $visible);
                """;
            command.Parameters.AddWithValue("$id", widget.HabitId.ToString("D"));
            command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(new[] { widget.HabitId }, DomainJson.Options));
            command.Parameters.AddWithValue("$monitor", widget.Monitor);
            command.Parameters.AddWithValue("$x", widget.X);
            command.Parameters.AddWithValue("$y", widget.Y);
            command.Parameters.AddWithValue("$size", widget.Size);
            command.Parameters.AddWithValue("$visible", widget.Visible ? 1 : 0);
            command.ExecuteNonQuery();
            widgets++;
        }

        foreach (var (key, value) in snapshot.Settings)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES ($key, $value);";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        SetState(connection, transaction, ImportMarkerKey, DateTime.UtcNow.ToString("O"));
        SetState(connection, transaction, "import.legacy_path", legacyPath);
        SetState(connection, transaction, "device_id", clock.DeviceId);

        transaction.Commit();
        return new Counts(habits.Count, dayLogs, widgets, snapshot.Settings.Count);
    }

    private static void Outbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string entity,
        string id,
        object payload,
        Hlc hlc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_outbox (entity, entity_id, op, payload_json, hlc, created_at)
            VALUES ($entity, $id, 'upsert', $payload, $hlc, $createdAt);
            """;
        command.Parameters.AddWithValue("$entity", entity);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(payload, DomainJson.Options));
        command.Parameters.AddWithValue("$hlc", hlc.ToString());
        command.Parameters.AddWithValue("$createdAt", Sql.ToSqlTimestamp(DateTime.UtcNow));
        command.ExecuteNonQuery();
    }

    private static void SetState(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO sync_state (key, value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// True chỉ khi <paramref name="targetPath"/> chắc chắn là CSDL v3 của HabitGrid còn trống và chưa nhập.
    /// Kiểm tra bằng kết nối CHỈ ĐỌC, không bao giờ tạo bảng; file lạ (kể cả SQLite của app khác) luôn bị coi là "có dữ liệu".
    /// </summary>
    private static async Task<bool> IsEmptyAndUnimportedAsync(string targetPath)
    {
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = targetPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var tables = ReadStrings(connection, "SELECT name FROM sqlite_master WHERE type = 'table';").ToHashSet();
            string[] required = ["schema_info", "habits", "day_logs", "sync_state"];
            if (!required.All(tables.Contains))
            {
                return false;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT (SELECT COUNT(*) FROM habits) + (SELECT COUNT(*) FROM day_logs)
                     + (SELECT COUNT(*) FROM sync_state WHERE key = '{ImportMarkerKey}')
                     + (SELECT COUNT(*) FROM schema_info WHERE version NOT IN ({MigrationV3Schema.SchemaVersion}, {MigrationV4Journal.SchemaVersion}));
                """;
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 0;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static async Task CheckpointAsync(string path)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode = DELETE;";
        await command.ExecuteNonQueryAsync();
        SqliteConnection.ClearAllPools();
    }

    private static IEnumerable<string> ReadStrings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var list = new List<string>();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    private static void DeleteDatabaseFiles(string path)
    {
        SqliteConnection.ClearAllPools();

        foreach (var file in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private static void CleanSidecars(string path)
    {
        foreach (var file in new[] { path + "-wal", path + "-shm", path + "-journal" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
