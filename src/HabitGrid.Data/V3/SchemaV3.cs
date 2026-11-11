using HabitGrid.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace HabitGrid.Data.V3;

/// <summary>Lược đồ SQLite v3 (SPEC-V2-02 A.2). Áp dụng trên file DB mới, không động vào DB v1/v2.</summary>
public sealed class MigrationV3Schema : IMigration
{
    public const int SchemaVersion = 3;

    public int Version => SchemaVersion;

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE habits (
                id                  TEXT PRIMARY KEY,
                name                TEXT NOT NULL,
                description         TEXT NULL,
                icon                TEXT NOT NULL,
                color_hue           TEXT NOT NULL,
                kind                INTEGER NOT NULL,
                target_value        INTEGER NOT NULL DEFAULT 1,
                target_unit         TEXT NULL,
                frequency_json      TEXT NOT NULL,
                time_of_day         INTEGER NOT NULL DEFAULT 0,
                start_date          TEXT NOT NULL,
                ask_mood            INTEGER NOT NULL DEFAULT 0,
                allow_notes         INTEGER NOT NULL DEFAULT 1,
                allow_freeze        INTEGER NOT NULL DEFAULT 1,
                routine_id          TEXT NULL,
                routine_order       INTEGER NOT NULL DEFAULT 0,
                tags_json           TEXT NOT NULL DEFAULT '[]',
                sort_key            TEXT NOT NULL,
                archived_at         TEXT NULL,
                deleted_at          TEXT NULL,
                created_at          TEXT NOT NULL,
                updated_at          TEXT NOT NULL,
                hlc                 TEXT NOT NULL,
                shared_challenge_id TEXT NULL
            );
            CREATE INDEX idx_habits_status ON habits(deleted_at, archived_at);
            CREATE INDEX idx_habits_sort ON habits(sort_key);

            CREATE TABLE reminders (
                id         TEXT PRIMARY KEY,
                habit_id   TEXT NOT NULL REFERENCES habits(id) ON DELETE CASCADE,
                position   INTEGER NOT NULL DEFAULT 0,
                time       TEXT NOT NULL,
                days_mask  INTEGER NOT NULL DEFAULT 127,
                smart      INTEGER NOT NULL DEFAULT 0,
                follow_up  INTEGER NOT NULL DEFAULT 0,
                hlc        TEXT NOT NULL,
                deleted_at TEXT NULL
            );
            CREATE INDEX idx_reminders_habit ON reminders(habit_id, position);

            CREATE TABLE day_logs (
                habit_id   TEXT NOT NULL REFERENCES habits(id) ON DELETE CASCADE,
                day        TEXT NOT NULL,
                value      INTEGER NOT NULL,
                status     INTEGER NOT NULL,
                note       TEXT NULL,
                mood       INTEGER NULL,
                checked_at TEXT NULL,
                updated_at TEXT NOT NULL,
                hlc        TEXT NOT NULL,
                PRIMARY KEY (habit_id, day)
            );
            CREATE INDEX idx_day_logs_day ON day_logs(day);

            CREATE TABLE routines (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                icon        TEXT NOT NULL,
                time_of_day INTEGER NOT NULL DEFAULT 0,
                sort_key    TEXT NOT NULL,
                hlc         TEXT NOT NULL,
                deleted_at  TEXT NULL
            );

            CREATE TABLE profile (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                hlc   TEXT NOT NULL
            );

            CREATE TABLE xp_events (
                id          TEXT PRIMARY KEY,
                kind        TEXT NOT NULL,
                amount      INTEGER NOT NULL,
                occurred_at TEXT NOT NULL,
                habit_id    TEXT NULL,
                hlc         TEXT NOT NULL
            );

            CREATE TABLE achievements (
                id          TEXT PRIMARY KEY,
                unlocked_at TEXT NOT NULL,
                hlc         TEXT NOT NULL
            );

            -- Dữ liệu theo máy, không đồng bộ.
            CREATE TABLE widgets (
                id            TEXT PRIMARY KEY,
                kind          TEXT NOT NULL,
                habit_ids_json TEXT NOT NULL DEFAULT '[]',
                monitor       TEXT NOT NULL DEFAULT '',
                x             REAL NOT NULL DEFAULT 0,
                y             REAL NOT NULL DEFAULT 0,
                size          INTEGER NOT NULL DEFAULT 1,
                theme_id      TEXT NULL,
                opacity       REAL NOT NULL DEFAULT 1.0,
                visible       INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE sync_outbox (
                seq          INTEGER PRIMARY KEY AUTOINCREMENT,
                entity       TEXT NOT NULL,
                entity_id    TEXT NOT NULL,
                op           TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                hlc          TEXT NOT NULL,
                created_at   TEXT NOT NULL,
                attempts     INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE sync_state (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}

/// <summary>Tạo và khởi tạo CSDL v3.</summary>
public static class HabitGridV3
{
    public const string FileName = "habitgrid-v3.db";

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HabitGrid",
        FileName);

    public static HabitGridDatabase Open(string? path = null)
    {
        var database = HabitGridDatabase.CreateAtPath(path ?? DefaultPath);

        try
        {
            Migrate(database);
            return database;
        }
        catch
        {
            // Không để kết nối rò rỉ giữ khóa file khi mở thất bại.
            database.Dispose();
            throw;
        }
    }

    public static HabitGridDatabase OpenInMemory()
    {
        var database = HabitGridDatabase.CreateInMemory();
        Migrate(database);
        return database;
    }

    /// <summary>Áp dụng lược đồ v3 (an toàn khi gọi lại).</summary>
    public static void Migrate(HabitGridDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        new MigrationRunner(database, [new MigrationV3Schema(), new MigrationV4Journal()]).ApplyMigrations();
    }
}
