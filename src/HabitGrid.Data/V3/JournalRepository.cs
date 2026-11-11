using HabitGrid.Core.Domain.Journal;
using HabitGrid.Data.Migrations;
using Microsoft.Data.Sqlite;
using Sql = HabitGrid.Data.Repositories.SqlMapping;

namespace HabitGrid.Data.V3;

/// <summary>Lược đồ v4: bảng nhật ký ngày (dữ liệu theo máy, chưa đồng bộ nên không có HLC/outbox).</summary>
public sealed class MigrationV4Journal : IMigration
{
    public const int SchemaVersion = 4;

    public int Version => SchemaVersion;

    public void Up(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE journal_entries (
                day        TEXT PRIMARY KEY,
                text       TEXT NOT NULL,
                mood       INTEGER NULL,
                updated_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}

public sealed class JournalRepository : IJournalRepository
{
    private const string Columns = "day, text, mood, updated_at";

    private readonly HabitGridDatabase _database;

    public JournalRepository(HabitGridDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<JournalEntry?> GetAsync(DateOnly day, CancellationToken ct = default)
    {
        var list = await QueryAsync("day = $day", c => c.Parameters.AddWithValue("$day", Sql.ToSqlDate(day)), 1, ct);
        return list.Count == 0 ? null : list[0];
    }

    public Task<IReadOnlyList<JournalEntry>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        QueryAsync("day >= $from AND day <= $to", c =>
        {
            c.Parameters.AddWithValue("$from", Sql.ToSqlDate(from));
            c.Parameters.AddWithValue("$to", Sql.ToSqlDate(to));
        }, int.MaxValue, ct);

    public Task<IReadOnlyList<JournalEntry>> GetAllAsync(CancellationToken ct = default) =>
        QueryAsync("1 = 1", _ => { }, int.MaxValue, ct);

    public Task<IReadOnlyList<JournalEntry>> SearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<JournalEntry>>([]);
        }

        // LIKE trong SQLite không phân biệt hoa/thường với ASCII; ký tự đặc biệt của LIKE được thoát để tìm đúng chữ người dùng gõ.
        var pattern = "%" + query.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        return QueryAsync("text LIKE $q ESCAPE '\\'", c => c.Parameters.AddWithValue("$q", pattern), Math.Max(1, limit), ct);
    }

    public async Task UpsertAsync(JournalEntry entry, CancellationToken ct = default)
    {
        var normalized = JournalRules.Normalize(entry);

        if (normalized.IsEmpty)
        {
            await DeleteAsync(entry.Day, ct);
            return;
        }

        await using var connection = await _database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO journal_entries ({Columns}) VALUES ($day, $text, $mood, $updated)
            ON CONFLICT(day) DO UPDATE SET text = excluded.text, mood = excluded.mood, updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$day", Sql.ToSqlDate(normalized.Day));
        command.Parameters.AddWithValue("$text", normalized.Text);
        command.Parameters.AddWithValue("$mood", (object?)normalized.Mood ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", Sql.ToSqlTimestamp(DateTime.UtcNow));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(DateOnly day, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM journal_entries WHERE day = $day;";
        command.Parameters.AddWithValue("$day", Sql.ToSqlDate(day));
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<IReadOnlyList<JournalEntry>> QueryAsync(string where, Action<SqliteCommand> bind, int limit, CancellationToken ct)
    {
        await using var connection = await _database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM journal_entries WHERE {where} ORDER BY day DESC LIMIT $limit;";
        bind(command);
        command.Parameters.AddWithValue("$limit", limit == int.MaxValue ? -1 : limit);

        var list = new List<JournalEntry>();
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            list.Add(new JournalEntry(
                Sql.ToDateOnly(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Sql.ToTimestamp(reader.GetString(3))));
        }

        return list;
    }
}
