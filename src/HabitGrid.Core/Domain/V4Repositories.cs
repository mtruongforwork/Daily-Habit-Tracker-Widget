namespace HabitGrid.Core.Domain;

public interface IHabitAreaRepository
{
    Task<IReadOnlyList<HabitArea>> GetAllAsync(CancellationToken ct = default);

    Task UpsertAsync(HabitArea area, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IQuickTodoRepository
{
    Task<IReadOnlyList<QuickTodo>> GetForDayAsync(DateOnly day, CancellationToken ct = default);

    Task InsertAsync(QuickTodo todo, CancellationToken ct = default);

    Task SetDoneAsync(Guid id, bool isDone, DateTime? completedAt, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface IRewardRepository
{
    Task<IReadOnlyList<CustomReward>> GetAllAsync(CancellationToken ct = default);

    Task UpsertAsync(CustomReward reward, CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<RewardRedemption>> GetRecentRedemptionsAsync(int limit = 20, CancellationToken ct = default);

    Task RecordRedemptionAsync(RewardRedemption redemption, CancellationToken ct = default);
}
