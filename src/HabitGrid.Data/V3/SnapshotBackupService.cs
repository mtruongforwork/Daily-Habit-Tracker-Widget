using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HabitGrid.Core.Domain;
using Microsoft.Data.Sqlite;
using Sql = HabitGrid.Data.Repositories.SqlMapping;

namespace HabitGrid.Data.V3;

public sealed record BackupManifest(int SchemaVersion, DateTime CreatedAtUtc, string App, int Habits, int DayLogs);

public sealed record BackupPayload(
    IReadOnlyList<Habit> Habits,
    IReadOnlyList<DayLog> DayLogs,
    IReadOnlyList<Routine> Routines,
    UserProfile Profile,
    IReadOnlyList<HabitGrid.Core.Domain.Journal.JournalEntry>? Journal = null,
    IReadOnlyList<HabitArea>? HabitAreas = null,
    IReadOnlyList<QuickTodo>? QuickTodos = null,
    IReadOnlyList<CustomReward>? CustomRewards = null,
    IReadOnlyList<RewardRedemption>? RewardRedemptions = null,
    IReadOnlyDictionary<string, string>? V4Settings = null);

public sealed class UnsupportedBackupException : Exception
{
    public UnsupportedBackupException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Sao lưu/khôi phục snapshot <c>.hgbackup</c> (zip gồm <c>manifest.json</c> và <c>data.json</c>), SPEC-V2-02 A.5.
/// Sao lưu tự động hằng ngày có ở bản Free vì đây là dữ liệu của người dùng.
/// </summary>
public sealed class SnapshotBackupService
{
    public const string Extension = ".hgbackup";
    public const int DefaultRetention = 14;

    private const string ManifestEntry = "manifest.json";
    private const string DataEntry = "data.json";

    private static readonly string[] V4SettingKeys =
    [
        SettingKeys.CoinsBalance,
        SettingKeys.HabitStacks,
        SettingKeys.GoalOfDayHabitId,
        SettingKeys.GoalOfDayDate
    ];

    private static readonly HashSet<string> V4SettingKeySet = new(V4SettingKeys, StringComparer.Ordinal);

    private readonly HabitGridDatabase _database;
    private readonly IHlcClock _clock;
    private readonly IDataChangeBus? _bus;
    private readonly bool _includeJournal;

    public SnapshotBackupService(HabitGridDatabase database, IHlcClock clock,
        IDataChangeBus? bus = null, bool includeJournal = true)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _bus = bus;
        _includeJournal = includeJournal;
    }

    public async Task<BackupManifest> ExportAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        BackupPayload payload;
        await using (var connection = await _database.OpenConnectionAsync())
        await using (var transaction = connection.BeginTransaction(deferred: true))
        {
            payload = await ReadPayloadAsync(connection, transaction, ct);
            await transaction.CommitAsync(ct);
        }

        return WriteArchive(path, payload, ct);
    }

    private async Task<BackupPayload> ReadPayloadAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        var habits = HabitRepository.Query(connection, transaction, "deleted_at IS NULL");
        var logs = new List<DayLog>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                SELECT {DayLogRepository.Columns} FROM day_logs
                WHERE habit_id IN (SELECT id FROM habits WHERE deleted_at IS NULL)
                ORDER BY day;
                """;
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                logs.Add(DayLogRepository.Read(reader));
            }
        }

        return new BackupPayload(habits, logs,
            await RoutineRepository.ReadAllAsync(connection, transaction, ct),
            await ProfileRepository.ReadAsync(connection, transaction, ct),
            _includeJournal ? await JournalRepository.ReadAllAsync(connection, transaction, ct) : [],
            await ReadHabitAreasAsync(connection, transaction, ct),
            await ReadQuickTodosAsync(connection, transaction, ct),
            await ReadCustomRewardsAsync(connection, transaction, ct),
            await ReadRewardRedemptionsAsync(connection, transaction, ct),
            await ReadV4SettingsAsync(connection, transaction, ct));
    }

    private static BackupManifest WriteArchive(string path, BackupPayload payload, CancellationToken ct)
    {
        var manifest = new BackupManifest(
            MigrationV3Schema.SchemaVersion,
            DateTime.UtcNow,
            "HabitGrid",
            payload.Habits.Count,
            payload.DayLogs.Count);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Ghi ra file tạm rồi đổi tên: backup lỗi giữa chừng không bao giờ thay thế một backup tốt.
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(zip, ManifestEntry, manifest);
                WriteEntry(zip, DataEntry, payload);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }

        return manifest;
    }

    public static async Task<BackupManifest> ReadManifestAsync(string path, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(path);
        return await ReadEntryAsync<BackupManifest>(zip, ManifestEntry, ct)
               ?? throw new UnsupportedBackupException("Thiếu manifest.");
    }

    /// <summary>
    /// Khôi phục theo kiểu THAY THẾ toàn bộ dữ liệu đồng bộ. Mọi bản ghi được đưa vào outbox để lần sync kế tiếp đẩy lại.
    /// Từ chối bản sao lưu từ phiên bản lược đồ mới hơn.
    /// </summary>
    public Task<BackupManifest> RestoreAsync(string path, CancellationToken ct = default) =>
        RestoreAsync(path, null, ct);

    internal async Task<BackupManifest> RestoreAsync(string path, string? safetyPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        BackupManifest manifest;
        BackupPayload payload;

        try
        {
            using var zip = ZipFile.OpenRead(path);
            manifest = await ReadEntryAsync<BackupManifest>(zip, ManifestEntry, ct)
                       ?? throw new UnsupportedBackupException("Thiếu manifest.");

            if (manifest.SchemaVersion > MigrationV3Schema.SchemaVersion)
            {
                throw new UnsupportedBackupException(
                    $"Bản sao lưu dùng lược đồ v{manifest.SchemaVersion}, mới hơn v{MigrationV3Schema.SchemaVersion} mà app này hỗ trợ.");
            }

            payload = await ReadEntryAsync<BackupPayload>(zip, DataEntry, ct)
                      ?? throw new UnsupportedBackupException("Thiếu dữ liệu.");
        }
        catch (InvalidDataException ex)
        {
            throw new UnsupportedBackupException($"File sao lưu không hợp lệ: {ex.Message}");
        }
        catch (JsonException ex)
        {
            throw new UnsupportedBackupException($"Nội dung sao lưu không đọc được: {ex.Message}");
        }

        await using var connection = await _database.OpenConnectionAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            // The write transaction also excludes WidgetHost writers between the safety backup and restore.
            if (safetyPath is not null)
            {
                var previous = await ReadPayloadAsync(connection, transaction, ct);
                WriteArchive(safetyPath, previous, ct);
            }

            foreach (var table in new[]
            {
                "reward_redemptions", "custom_rewards", "quick_todos",
                "journal_entries", "day_logs", "reminders", "routines",
                "profile", "habits", "habit_areas", "sync_outbox"
            })
            {
                await using var wipe = connection.CreateCommand();
                wipe.Transaction = transaction;
                wipe.CommandText = $"DELETE FROM {table};";
                await wipe.ExecuteNonQueryAsync(ct);
            }

            await ClearV4SettingsAsync(connection, transaction, ct);

            foreach (var area in payload.HabitAreas ?? [])
            {
                await InsertHabitAreaAsync(connection, transaction, area, ct);
            }

            foreach (var habit in payload.Habits)
            {
                habit.Version = _clock.Now();
                await InsertHabitAsync(connection, transaction, habit, ct);
            }

            var known = payload.Habits.Select(h => h.Id).ToHashSet();
            foreach (var log in payload.DayLogs.Where(l => known.Contains(l.HabitId)))
            {
                var stamped = log with { Version = _clock.Now() };
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO day_logs ({DayLogRepository.Columns})
                    VALUES ($habit, $day, $value, $status, $note, $mood, $checked_at, $updated_at, $hlc);
                    """;
                DayLogRepository.Bind(command, stamped);
                await command.ExecuteNonQueryAsync(ct);
                await EnqueueAsync(connection, transaction, "dayLog", DayLogRepository.Key(stamped.HabitId, stamped.Day), stamped, stamped.Version, ct);
            }

            foreach (var routine in payload.Routines)
            {
                var hlc = _clock.Now();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO routines (id, name, icon, time_of_day, sort_key, hlc)
                    VALUES ($id, $name, $icon, $tod, $sort, $hlc);
                    """;
                command.Parameters.AddWithValue("$id", routine.Id.ToString("D"));
                command.Parameters.AddWithValue("$name", routine.Name);
                command.Parameters.AddWithValue("$icon", routine.Icon.Value);
                command.Parameters.AddWithValue("$tod", (int)routine.TimeOfDay);
                command.Parameters.AddWithValue("$sort", routine.SortKey);
                command.Parameters.AddWithValue("$hlc", hlc.ToString());
                await command.ExecuteNonQueryAsync(ct);
                await EnqueueAsync(connection, transaction, "routine", routine.Id.ToString("D"), routine with { HabitIds = [] }, hlc, ct);
            }

            foreach (var entry in (payload.Journal ?? []).Select(HabitGrid.Core.Domain.Journal.JournalRules.Normalize).Where(e => !e.IsEmpty))
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT OR REPLACE INTO journal_entries (day, text, mood, updated_at) VALUES ($day, $text, $mood, $updated);";
                command.Parameters.AddWithValue("$day", Sql.ToSqlDate(entry.Day));
                command.Parameters.AddWithValue("$text", entry.Text);
                command.Parameters.AddWithValue("$mood", (object?)entry.Mood ?? DBNull.Value);
                command.Parameters.AddWithValue("$updated", Sql.ToSqlTimestamp(entry.UpdatedAt));
                await command.ExecuteNonQueryAsync(ct);
            }

            foreach (var todo in payload.QuickTodos ?? [])
            {
                await InsertQuickTodoAsync(connection, transaction, todo, ct);
            }

            foreach (var reward in payload.CustomRewards ?? [])
            {
                await InsertCustomRewardAsync(connection, transaction, reward, ct);
            }

            foreach (var redemption in payload.RewardRedemptions ?? [])
            {
                await InsertRewardRedemptionAsync(connection, transaction, redemption, ct);
            }

            if (payload.V4Settings is { Count: > 0 } v4Settings)
            {
                await InsertV4SettingsAsync(connection, transaction, v4Settings, ct);
            }

            ProfileRepository.Write(connection, transaction, payload.Profile, _clock, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        _bus?.Publish(new DataChange(DataEntity.Everything, string.Empty));
        return manifest;
    }

    /// <summary>Tạo snapshot của ngày <paramref name="nowLocal"/> nếu chưa có, rồi dọn bớt bản cũ. Trả về đường dẫn file mới (hoặc null nếu đã có).</summary>
    public async Task<string?> CreateDailySnapshotAsync(
        string directory,
        DateTime nowLocal,
        int keep = DefaultRetention,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"habitgrid-{nowLocal:yyyyMMdd}{Extension}");

        if (File.Exists(path))
        {
            return null;
        }

        await ExportAsync(path, ct);
        Prune(directory, keep);
        return path;
    }

    public static void Prune(string directory, int keep = DefaultRetention)
    {
        var files = Directory.GetFiles(directory, $"habitgrid-*{Extension}")
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Skip(keep);

        foreach (var file in files)
        {
            File.Delete(file);
        }
    }

    // ------------------------------------------------------------------

    private static async Task InsertHabitAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Habit habit,
        CancellationToken ct)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO habits ({HabitRepository.Columns}) VALUES ({HabitRepository.Placeholders()});";
            HabitRepository.Bind(command, habit);
            await command.ExecuteNonQueryAsync(ct);
        }

        var position = 0;
        foreach (var reminder in habit.Reminders)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO reminders (id, habit_id, position, time, days_mask, smart, follow_up, hlc)
                VALUES ($id, $habit, $pos, $time, $days, $smart, $followUp, $hlc);
                """;
            command.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("D"));
            command.Parameters.AddWithValue("$habit", habit.Id.ToString("D"));
            command.Parameters.AddWithValue("$pos", position++);
            command.Parameters.AddWithValue("$time", reminder.Time.ToString(Sql.TimeFormat));
            command.Parameters.AddWithValue("$days", (int)reminder.Days);
            command.Parameters.AddWithValue("$smart", reminder.Smart ? 1 : 0);
            command.Parameters.AddWithValue("$followUp", reminder.FollowUp ? 1 : 0);
            command.Parameters.AddWithValue("$hlc", habit.Version.ToString());
            await command.ExecuteNonQueryAsync(ct);
        }

        await EnqueueAsync(connection, transaction, "habit", habit.Id.ToString("D"), habit, habit.Version, ct);
    }

    private static async Task EnqueueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string entity,
        string id,
        object payload,
        Hlc hlc,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
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
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void WriteEntry(ZipArchive zip, string name, object value)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(value, DomainJson.Options));
    }

    private static async Task<T?> ReadEntryAsync<T>(ZipArchive zip, string name, CancellationToken ct)
    {
        var entry = zip.GetEntry(name);
        if (entry is null)
        {
            return default;
        }

        // File sao lưu do người dùng chọn có thể bị hỏng hoặc độc hại (zip bomb): từ chối mục quá lớn trước khi giải nén.
        if (entry.Length > MaxEntryBytes)
        {
            throw new UnsupportedBackupException("File sao lưu quá lớn hoặc không hợp lệ.");
        }

        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, DomainJson.Options, ct);
    }

    private static async Task<IReadOnlyList<HabitArea>> ReadHabitAreasAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, name, emoji, color_hex, sort_key FROM habit_areas ORDER BY sort_key, name;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<HabitArea>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new HabitArea(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }
        return list;
    }

    private static async Task<IReadOnlyList<QuickTodo>> ReadQuickTodosAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, title, day, is_done, created_at, completed_at FROM quick_todos ORDER BY day ASC, created_at ASC;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<QuickTodo>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new QuickTodo(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                Sql.ToDateOnly(reader.GetString(2)),
                reader.GetInt32(3) != 0,
                Sql.ToTimestamp(reader.GetString(4)),
                Sql.ToTimestampOrNull(reader.IsDBNull(5) ? null : reader.GetString(5))));
        }
        return list;
    }

    private static async Task<IReadOnlyList<CustomReward>> ReadCustomRewardsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, title, emoji, cost_coins, note, created_at FROM custom_rewards ORDER BY cost_coins ASC, created_at ASC;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<CustomReward>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CustomReward(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                Sql.ToTimestamp(reader.GetString(5))));
        }
        return list;
    }

    private static async Task<IReadOnlyList<RewardRedemption>> ReadRewardRedemptionsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, reward_id, reward_title, cost_coins, redeemed_at FROM reward_redemptions ORDER BY redeemed_at DESC;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<RewardRedemption>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RewardRedemption(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt32(3),
                Sql.ToTimestamp(reader.GetString(4))));
        }
        return list;
    }

    private static async Task<Dictionary<string, string>> ReadV4SettingsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = BuildV4SettingsInClause("SELECT key, value FROM settings WHERE key IN");
        AddV4SettingKeyParameters(command);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }
        return dict;
    }

    private static async Task ClearV4SettingsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = BuildV4SettingsInClause("DELETE FROM settings WHERE key IN");
        AddV4SettingKeyParameters(command);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string BuildV4SettingsInClause(string prefix)
    {
        var placeholders = string.Join(", ", V4SettingKeys.Select((_, i) => $"$k{i}"));
        return $"{prefix} ({placeholders});";
    }

    private static void AddV4SettingKeyParameters(SqliteCommand command)
    {
        for (var i = 0; i < V4SettingKeys.Length; i++)
        {
            command.Parameters.AddWithValue($"$k{i}", V4SettingKeys[i]);
        }
    }

    private static async Task InsertHabitAreaAsync(SqliteConnection connection, SqliteTransaction transaction, HabitArea area, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO habit_areas (id, name, emoji, color_hex, sort_key)
            VALUES ($id, $name, $emoji, $color, $sort);
            """;
        command.Parameters.AddWithValue("$id", area.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", area.Name);
        command.Parameters.AddWithValue("$emoji", area.Emoji);
        command.Parameters.AddWithValue("$color", area.ColorHex);
        command.Parameters.AddWithValue("$sort", area.SortKey);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertQuickTodoAsync(SqliteConnection connection, SqliteTransaction transaction, QuickTodo todo, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO quick_todos (id, title, day, is_done, created_at, completed_at)
            VALUES ($id, $title, $day, $done, $created, $completed);
            """;
        command.Parameters.AddWithValue("$id", todo.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", todo.Title);
        command.Parameters.AddWithValue("$day", Sql.ToSqlDate(todo.Day));
        command.Parameters.AddWithValue("$done", todo.IsDone ? 1 : 0);
        command.Parameters.AddWithValue("$created", Sql.ToSqlTimestamp(todo.CreatedAt));
        command.Parameters.AddWithValue("$completed", (object?)Sql.ToSqlTimestamp(todo.CompletedAt) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertCustomRewardAsync(SqliteConnection connection, SqliteTransaction transaction, CustomReward reward, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO custom_rewards (id, title, emoji, cost_coins, note, created_at)
            VALUES ($id, $title, $emoji, $cost, $note, $created);
            """;
        command.Parameters.AddWithValue("$id", reward.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", reward.Title);
        command.Parameters.AddWithValue("$emoji", reward.Emoji);
        command.Parameters.AddWithValue("$cost", reward.CostCoins);
        command.Parameters.AddWithValue("$note", (object?)reward.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", Sql.ToSqlTimestamp(reward.CreatedAt ?? DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertRewardRedemptionAsync(SqliteConnection connection, SqliteTransaction transaction, RewardRedemption redemption, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO reward_redemptions (id, reward_id, reward_title, cost_coins, redeemed_at)
            VALUES ($id, $rid, $title, $cost, $at);
            """;
        command.Parameters.AddWithValue("$id", redemption.Id.ToString("D"));
        command.Parameters.AddWithValue("$rid", redemption.RewardId.ToString("D"));
        command.Parameters.AddWithValue("$title", redemption.RewardTitle);
        command.Parameters.AddWithValue("$cost", redemption.CostCoins);
        command.Parameters.AddWithValue("$at", Sql.ToSqlTimestamp(redemption.RedeemedAt));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertV4SettingsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyDictionary<string, string> settings,
        CancellationToken ct)
    {
        foreach (var (key, value) in settings)
        {
            if (!V4SettingKeySet.Contains(key) || value is null)
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO settings (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>Giới hạn kích thước một mục sau giải nén (128 MB; dữ liệu thật chỉ vài MB).</summary>
    public const long MaxEntryBytes = 128L * 1024 * 1024;
}
