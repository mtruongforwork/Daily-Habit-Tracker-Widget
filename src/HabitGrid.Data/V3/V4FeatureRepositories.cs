using HabitGrid.Core.Domain;
using Microsoft.Data.Sqlite;
using Sql = HabitGrid.Data.Repositories.SqlMapping;

namespace HabitGrid.Data.V3;

public sealed class HabitAreaRepository : RepositoryBase, IHabitAreaRepository
{
    public HabitAreaRepository(HabitGridDatabase database, IHlcClock clock, IDataChangeBus? bus = null)
        : base(database, clock, bus)
    {
    }

    public async Task<IReadOnlyList<HabitArea>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = Command(connection, null, "SELECT id, name, emoji, color_hex, sort_key FROM habit_areas ORDER BY sort_key, name;");
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

    public async Task UpsertAsync(HabitArea area, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(area);

        await InTransactionAsync((connection, transaction) =>
        {
            using var command = Command(connection, transaction, """
                INSERT INTO habit_areas (id, name, emoji, color_hex, sort_key)
                VALUES ($id, $name, $emoji, $color, $sort)
                ON CONFLICT(id) DO UPDATE SET name = $name, emoji = $emoji, color_hex = $color, sort_key = $sort;
                """);
            command.Parameters.AddWithValue("$id", area.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", area.Name);
            command.Parameters.AddWithValue("$emoji", area.Emoji);
            command.Parameters.AddWithValue("$color", area.ColorHex);
            command.Parameters.AddWithValue("$sort", area.SortKey);
            command.ExecuteNonQuery();
            return 0;
        });

        Publish(DataEntity.Everything, area.Id.ToString("D"));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await InTransactionAsync((connection, transaction) =>
        {
            using (var clearHabits = Command(connection, transaction, "UPDATE habits SET area_id = NULL WHERE area_id = $id;"))
            {
                clearHabits.Parameters.AddWithValue("$id", id.ToString("D"));
                clearHabits.ExecuteNonQuery();
            }

            using var del = Command(connection, transaction, "DELETE FROM habit_areas WHERE id = $id;");
            del.Parameters.AddWithValue("$id", id.ToString("D"));
            del.ExecuteNonQuery();
            return 0;
        });

        Publish(DataEntity.Everything, id.ToString("D"));
    }
}

public sealed class QuickTodoRepository : RepositoryBase, IQuickTodoRepository
{
    public QuickTodoRepository(HabitGridDatabase database, IHlcClock clock, IDataChangeBus? bus = null)
        : base(database, clock, bus)
    {
    }

    public async Task<IReadOnlyList<QuickTodo>> GetForDayAsync(DateOnly day, CancellationToken ct = default)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = Command(connection, null, """
            SELECT id, title, day, is_done, created_at, completed_at
            FROM quick_todos
            WHERE day = $day
            ORDER BY is_done ASC, created_at ASC;
            """);
        command.Parameters.AddWithValue("$day", Sql.ToSqlDate(day));
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

    public async Task InsertAsync(QuickTodo todo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(todo);

        await InTransactionAsync((connection, transaction) =>
        {
            using var command = Command(connection, transaction, """
                INSERT INTO quick_todos (id, title, day, is_done, created_at, completed_at)
                VALUES ($id, $title, $day, $done, $created, $completed);
                """);
            command.Parameters.AddWithValue("$id", todo.Id.ToString("D"));
            command.Parameters.AddWithValue("$title", todo.Title);
            command.Parameters.AddWithValue("$day", Sql.ToSqlDate(todo.Day));
            command.Parameters.AddWithValue("$done", todo.IsDone ? 1 : 0);
            command.Parameters.AddWithValue("$created", Sql.ToSqlTimestamp(todo.CreatedAt));
            command.Parameters.AddWithValue("$completed", (object?)Sql.ToSqlTimestamp(todo.CompletedAt) ?? DBNull.Value);
            command.ExecuteNonQuery();
            return 0;
        });

        Publish(DataEntity.Everything, todo.Id.ToString("D"));
    }

    public async Task SetDoneAsync(Guid id, bool isDone, DateTime? completedAt, CancellationToken ct = default)
    {
        await InTransactionAsync((connection, transaction) =>
        {
            using var command = Command(connection, transaction, """
                UPDATE quick_todos SET is_done = $done, completed_at = $completed WHERE id = $id;
                """);
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.Parameters.AddWithValue("$done", isDone ? 1 : 0);
            command.Parameters.AddWithValue("$completed", (object?)Sql.ToSqlTimestamp(completedAt) ?? DBNull.Value);
            command.ExecuteNonQuery();
            return 0;
        });

        Publish(DataEntity.Everything, id.ToString("D"));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await InTransactionAsync((connection, transaction) =>
        {
            using var command = Command(connection, transaction, "DELETE FROM quick_todos WHERE id = $id;");
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.ExecuteNonQuery();
            return 0;
        });

        Publish(DataEntity.Everything, id.ToString("D"));
    }
}

public sealed class RewardRepository : RepositoryBase, IRewardRepository
{
    public RewardRepository(HabitGridDatabase database, IHlcClock clock, IDataChangeBus? bus = null)
        : base(database, clock, bus)
    {
    }

    public async Task<IReadOnlyList<CustomReward>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = Command(connection, null, "SELECT id, title, emoji, cost_coins, note, created_at FROM custom_rewards ORDER BY cost_coins ASC, created_at ASC;");
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

    public async Task UpsertAsync(CustomReward reward, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reward);

        await InTransactionAsync((connection, transaction) =>
        {
            using var command = Command(connection, transaction, """
                INSERT INTO custom_rewards (id, title, emoji, cost_coins, note, created_at)
                VALUES ($id, $title, $emoji, $cost, $note, $created)
                ON CONFLICT(id) DO UPDATE SET title = $title, emoji = $emoji, cost_coins = $cost, note = $note;
                """);
            command.Parameters.AddWithValue("$id", reward.Id.ToString("D"));
            command.Parameters.AddWithValue("$title", reward.Title);
            command.Parameters.AddWithValue("$emoji", reward.Emoji);
            command.Parameters.AddWithValue("$cost", reward.CostCoins);
            command.Parameters.AddWithValue("$note", (object?)reward.Note ?? DBNull.Value);
            command.Parameters.AddWithValue("$created", Sql.ToSqlTimestamp(reward.CreatedAt ?? DateTime.UtcNow));
            command.ExecuteNonQuery();
            return 0;
        });

        Publish(DataEntity.Everything, reward.Id.ToString("D"));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await InTransactionAsync((connection, transaction) =>
        {
            using var command = Command(connection, transaction, "DELETE FROM custom_rewards WHERE id = $id;");
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.ExecuteNonQuery();
            return 0;
        });

        Publish(DataEntity.Everything, id.ToString("D"));
    }

    public async Task<IReadOnlyList<RewardRedemption>> GetRecentRedemptionsAsync(int limit = 20, CancellationToken ct = default)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = Command(connection, null, "SELECT id, reward_id, reward_title, cost_coins, redeemed_at FROM reward_redemptions ORDER BY redeemed_at DESC LIMIT $limit;");
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
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

    public async Task RecordRedemptionAsync(RewardRedemption redemption, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(redemption);

        await InTransactionAsync((connection, transaction) =>
        {
            using var command = Command(connection, transaction, """
                INSERT INTO reward_redemptions (id, reward_id, reward_title, cost_coins, redeemed_at)
                VALUES ($id, $rid, $title, $cost, $at);
                """);
            command.Parameters.AddWithValue("$id", redemption.Id.ToString("D"));
            command.Parameters.AddWithValue("$rid", redemption.RewardId.ToString("D"));
            command.Parameters.AddWithValue("$title", redemption.RewardTitle);
            command.Parameters.AddWithValue("$cost", redemption.CostCoins);
            command.Parameters.AddWithValue("$at", Sql.ToSqlTimestamp(redemption.RedeemedAt));
            command.ExecuteNonQuery();
            return 0;
        });

        Publish(DataEntity.Everything, redemption.Id.ToString("D"));
    }
}
