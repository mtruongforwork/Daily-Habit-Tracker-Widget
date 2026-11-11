namespace HabitGrid.Core.Domain.Journal;

/// <summary>Nhật ký của một ngày: đoạn văn tự do kèm tâm trạng tùy chọn (1–5). Mỗi ngày tối đa một bản ghi.</summary>
public sealed record JournalEntry(DateOnly Day, string Text, int? Mood, DateTime UpdatedAt)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && Mood is null;

    /// <summary>Một dòng ngắn để hiển thị trong danh sách: dòng đầu có chữ, cắt ở <paramref name="max"/> ký tự.</summary>
    public string Preview(int max = 90)
    {
        var line = Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }
}

public static class JournalRules
{
    public const int MaxLength = 5000;

    /// <summary>Chuẩn hóa trước khi lưu: cắt khoảng trắng đầu/cuối, giới hạn độ dài, tâm trạng ngoài 1–5 bị bỏ.</summary>
    public static JournalEntry Normalize(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var text = (entry.Text ?? string.Empty).Replace("\r\n", "\n").Trim();

        if (text.Length > MaxLength)
        {
            text = text[..MaxLength];
        }

        var mood = entry.Mood is >= 1 and <= 5 ? entry.Mood : null;
        return entry with { Text = text, Mood = mood };
    }
}

public interface IJournalRepository
{
    Task<JournalEntry?> GetAsync(DateOnly day, CancellationToken ct = default);

    /// <summary>Các ngày có nhật ký trong khoảng (gồm cả hai đầu), mới nhất trước.</summary>
    Task<IReadOnlyList<JournalEntry>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>Tìm trong nội dung (không phân biệt hoa/thường), mới nhất trước.</summary>
    Task<IReadOnlyList<JournalEntry>> SearchAsync(string query, int limit = 50, CancellationToken ct = default);

    Task<IReadOnlyList<JournalEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Lưu (thêm hoặc thay). Bản ghi rỗng (không chữ, không tâm trạng) thì xóa.</summary>
    Task UpsertAsync(JournalEntry entry, CancellationToken ct = default);

    Task DeleteAsync(DateOnly day, CancellationToken ct = default);
}
