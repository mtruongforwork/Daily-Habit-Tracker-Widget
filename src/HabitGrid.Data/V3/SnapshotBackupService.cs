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
    IReadOnlyList<HabitGrid.Core.Domain.Journal.JournalEntry>? Journal = null);

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

    private readonly HabitGridDatabase _database;
    private readonly IHlcClock _clock;
    private readonly IHabitRepository _habits;
    private readonly IDayLogRepository _dayLogs;
    private readonly IRoutineRepository _routines;
    private readonly IProfileRepository _profile;
    private readonly IDataChangeBus? _bus;
    private readonly HabitGrid.Core.Domain.Journal.IJournalRepository? _journal;

    public SnapshotBackupService(
        HabitGridDatabase database,
        IHlcClock clock,
        IHabitRepository habits,
        IDayLogRepository dayLogs,
        IRoutineRepository routines,
        IProfileRepository profile,
        IDataChangeBus? bus = null,
        HabitGrid.Core.Domain.Journal.IJournalRepository? journal = null)
    {
        _journal = journal;
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _habits = habits ?? throw new ArgumentNullException(nameof(habits));
        _dayLogs = dayLogs ?? throw new ArgumentNullException(nameof(dayLogs));
        _routines = routines ?? throw new ArgumentNullException(nameof(routines));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _bus = bus;
    }

    public async Task<BackupManifest> ExportAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var habits = await _habits.GetAllAsync(ct);
        var logs = new List<DayLog>();
        foreach (var habit in habits)
        {
            logs.AddRange(await _dayLogs.GetAllForHabitAsync(habit.Id, ct));
        }

        var payload = new BackupPayload(habits, logs, await _routines.GetAllAsync(ct), await _profile.GetAsync(ct), _journal is null ? [] : await _journal.GetAllAsync(ct));
        var manifest = new BackupManifest(
            MigrationV3Schema.SchemaVersion,
            DateTime.UtcNow,
            "HabitGrid",
            habits.Count,
            logs.Count);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        // Ghi ra file tạm rồi đổi tên: backup lỗi giữa chừng không bao giờ thay thế một backup tốt.
        var temp = path + ".tmp";
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(zip, ManifestEntry, manifest);
            WriteEntry(zip, DataEntry, payload);
        }

        File.Move(temp, path, overwrite: true);
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
    public async Task<BackupManifest> RestoreAsync(string path, CancellationToken ct = default)
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
            foreach (var table in new[] { "journal_entries", "day_logs", "reminders", "routines", "profile", "habits", "sync_outbox" })
            {
                await using var wipe = connection.CreateCommand();
                wipe.Transaction = transaction;
                wipe.CommandText = $"DELETE FROM {table};";
                await wipe.ExecuteNonQueryAsync(ct);
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

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        await _profile.SaveAsync(payload.Profile, ct);
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

    /// <summary>Giới hạn kích thước một mục sau giải nén (128 MB; dữ liệu thật chỉ vài MB).</summary>
    public const long MaxEntryBytes = 128L * 1024 * 1024;
}
