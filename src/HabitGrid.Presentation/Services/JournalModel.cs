using HabitGrid.Core.Domain.Journal;
using HabitGrid.Core.Services;

namespace HabitGrid.Presentation.Services;

/// <summary>
/// Trang Nhật ký: chọn một ngày (không sau hôm nay), viết và lưu nhật ký ngày đó kèm tâm trạng, xem danh sách gần đây hoặc tìm kiếm.
/// Không phụ thuộc giao diện.
/// </summary>
public sealed class JournalModel
{
    /// <summary>Số ngày gần nhất hiện trong danh sách khi không tìm kiếm.</summary>
    public const int RecentDays = 120;

    private readonly IJournalRepository _repository;
    private readonly IDayProvider _days;

    public JournalModel(IJournalRepository repository, IDayProvider days)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _days = days ?? throw new ArgumentNullException(nameof(days));
    }

    public DateOnly Today => _days.GetToday();

    public DateOnly SelectedDay { get; private set; }

    /// <summary>Bản ghi của ngày đang chọn; null nếu chưa viết gì.</summary>
    public JournalEntry? Current { get; private set; }

    /// <summary>Danh sách bên trái: kết quả tìm kiếm nếu có truy vấn, ngược lại các ngày gần đây (mới nhất trước).</summary>
    public IReadOnlyList<JournalEntry> Entries { get; private set; } = [];

    public string Query { get; private set; } = string.Empty;

    public bool CanGoNext => SelectedDay < Today;

    public async Task LoadAsync(DateOnly? day = null, CancellationToken ct = default)
    {
        var target = day ?? (SelectedDay == default ? Today : SelectedDay);
        SelectedDay = target > Today ? Today : target;
        Current = await _repository.GetAsync(SelectedDay, ct);
        await RefreshListAsync(ct);
    }

    public Task SelectDayAsync(DateOnly day, CancellationToken ct = default) => LoadAsync(day, ct);

    public Task ShiftDayAsync(int delta, CancellationToken ct = default) => LoadAsync(SelectedDay.AddDays(delta), ct);

    public async Task SearchAsync(string? query, CancellationToken ct = default)
    {
        Query = query?.Trim() ?? string.Empty;
        await RefreshListAsync(ct);
    }

    /// <summary>Lưu ngày đang chọn. Rỗng (không chữ, không tâm trạng) thì xóa bản ghi.</summary>
    public async Task SaveAsync(string text, int? mood, CancellationToken ct = default)
    {
        await _repository.UpsertAsync(new JournalEntry(SelectedDay, text ?? string.Empty, mood, DateTime.UtcNow), ct);
        Current = await _repository.GetAsync(SelectedDay, ct);
        await RefreshListAsync(ct);
    }

    private async Task RefreshListAsync(CancellationToken ct)
    {
        Entries = Query.Length > 0
            ? await _repository.SearchAsync(Query, 100, ct)
            : await _repository.GetRangeAsync(Today.AddDays(-RecentDays), Today, ct);
    }
}
