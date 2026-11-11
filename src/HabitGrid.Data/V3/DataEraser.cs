using Microsoft.Data.Sqlite;

namespace HabitGrid.Data.V3;

/// <summary>
/// Xóa toàn bộ dữ liệu người dùng trong CSDL (quyền yêu cầu xóa dữ liệu): mọi bảng dữ liệu bị dọn sạch rồi <c>VACUUM</c> để nội dung
/// không còn nằm lại trong các trang rỗng của file. Cấu trúc bảng và phiên bản lược đồ được giữ nên app mở lại như vừa cài (hiện màn hình chào).
/// </summary>
public static class DataEraser
{
    /// <summary>Danh sách bảng cố định trong mã (không bao giờ ghép từ dữ liệu bên ngoài). Thứ tự: bảng con trước bảng cha.</summary>
    private static readonly string[] Tables =
    [
        "day_logs", "reminders", "routines", "xp_events", "achievements", "widgets", "profile", "settings", "sync_outbox", "sync_state", "habits"
    ];

    /// <summary>Số dòng đã xóa (tổng các bảng).</summary>
    public static async Task<int> EraseAllAsync(HabitGridDatabase database, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        var removed = 0;

        await using (var connection = await database.OpenConnectionAsync())
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);

            foreach (var table in Tables)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"DELETE FROM {table};";
                removed += await command.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }

        // VACUUM không chạy được trong giao dịch; bộ nhớ trong (test) bỏ qua checkpoint WAL.
        await using (var connection = await database.OpenConnectionAsync())
        {
            foreach (var sql in new[] { "VACUUM;", "PRAGMA wal_checkpoint(TRUNCATE);" })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(ct);
            }
        }

        return removed;
    }
}
