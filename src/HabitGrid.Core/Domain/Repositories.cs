namespace HabitGrid.Core.Domain;

/// <summary>Loại dữ liệu vừa thay đổi.</summary>
public enum DataEntity
{
    Habit,
    DayLog,
    Routine,
    Profile,
    XpEvent,
    Achievement,
    Everything
}

/// <summary>Thông báo thay đổi dữ liệu. <see cref="Key"/> là Id (hoặc "habitId|yyyy-MM-dd" với DayLog).</summary>
public sealed record DataChange(DataEntity Entity, string Key);

/// <summary>Bus nội bộ để UI/widget làm mới đúng phần bị ảnh hưởng thay vì tải lại toàn bộ.</summary>
public interface IDataChangeBus
{
    event EventHandler<DataChange>? Changed;

    void Publish(DataChange change);
}

public sealed class DataChangeBus : IDataChangeBus
{
    public event EventHandler<DataChange>? Changed;

    public void Publish(DataChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Changed?.Invoke(this, change);
    }
}

public interface IHabitRepository
{
    /// <summary>Thói quen đang chạy (chưa xóa, chưa lưu trữ), theo SortKey tăng dần.</summary>
    Task<IReadOnlyList<Habit>> GetAllActiveAsync(CancellationToken ct = default);

    /// <summary>Mọi thói quen chưa xóa, gồm cả đã lưu trữ.</summary>
    Task<IReadOnlyList<Habit>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Lấy theo Id kể cả thói quen đã xóa mềm (để hoàn tác).</summary>
    Task<Habit?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task InsertAsync(Habit habit, CancellationToken ct = default);

    Task UpdateAsync(Habit habit, CancellationToken ct = default);

    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);

    Task RestoreAsync(Guid id, CancellationToken ct = default);

    Task ArchiveAsync(Guid id, CancellationToken ct = default);

    Task UnarchiveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Đổi vị trí bằng fractional index, chỉ ghi một dòng.</summary>
    Task SetSortKeyAsync(Guid id, string sortKey, CancellationToken ct = default);

    Task<int> CountActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Xóa hẳn các thói quen đã xóa mềm trước <paramref name="olderThan"/> và không còn thay đổi nào chờ đồng bộ.
    /// Trả về số thói quen đã xóa.
    /// </summary>
    Task<int> PurgeDeletedAsync(DateTime olderThan, CancellationToken ct = default);
}

public interface IDayLogRepository
{
    Task<DayLog?> GetAsync(Guid habitId, DateOnly day, CancellationToken ct = default);

    Task<IReadOnlyList<DayLog>> GetRangeAsync(Guid habitId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<IReadOnlyList<DayLog>> GetAllForHabitAsync(Guid habitId, CancellationToken ct = default);

    /// <summary>Mọi ghi nhận của một ngày (cho trang Hôm nay).</summary>
    Task<IReadOnlyList<DayLog>> GetForDayAsync(DateOnly day, CancellationToken ct = default);

    /// <summary>Ghi (thêm hoặc thay) một ngày. Bản ghi nên đã qua <see cref="DayLogRules.Normalize"/>.</summary>
    Task UpsertAsync(DayLog log, CancellationToken ct = default);

    Task DeleteAsync(Guid habitId, DateOnly day, CancellationToken ct = default);

    /// <summary>Read and update the current log atomically. Return the current instance to leave it unchanged, or null to delete.</summary>
    Task<DayLog?> UpdateAsync(Guid habitId, DateOnly day, Func<Habit, DayLog?, DayLog?> update, CancellationToken ct = default);
}

public interface IRoutineRepository
{
    Task<IReadOnlyList<Routine>> GetAllAsync(CancellationToken ct = default);

    Task UpsertAsync(Routine routine, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IFocusRepository
{
    Task<IReadOnlyList<Focus.FocusRecord>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(Focus.FocusRecord record, CancellationToken ct = default);
}

public interface IAchievementRepository
{
    Task<IReadOnlyList<Achievement>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Ghi huy hiệu đã mở khóa; trả về true nếu là lần đầu (đã mở thì giữ ngày cũ, không ghi lại).</summary>
    Task<bool> UnlockAsync(string id, DateTime unlockedAtUtc, CancellationToken ct = default);
}

public interface IProfileRepository
{
    Task<UserProfile> GetAsync(CancellationToken ct = default);

    Task SaveAsync(UserProfile profile, CancellationToken ct = default);
}
