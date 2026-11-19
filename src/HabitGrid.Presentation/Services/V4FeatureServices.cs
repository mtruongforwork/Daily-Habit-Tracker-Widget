using System.Globalization;
using HabitGrid.Core.Domain;

namespace HabitGrid.Presentation.Services;

public enum RewardResult
{
    Ok,
    InvalidInput,
    LimitReached,
    NotFound,
    InsufficientCoins
}

public sealed class RewardService
{
    private readonly IRewardRepository _rewards;
    private readonly ISettingsStore _settings;
    private readonly IEntitlements _entitlements;

    public RewardService(IRewardRepository rewards, ISettingsStore settings, IEntitlements entitlements)
    {
        _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
    }

    public event EventHandler? Changed;

    public async Task<int> GetBalanceAsync(CancellationToken ct = default)
    {
        var raw = await _settings.GetAsync(SettingKeys.CoinsBalance, ct);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var coins)
            ? Math.Max(0, coins)
            : 0;
    }

    public async Task<int> AddCoinsAsync(int delta, CancellationToken ct = default)
    {
        var current = await GetBalanceAsync(ct);
        var next = RewardWallet.ApplyDelta(current, delta);
        await _settings.SetAsync(SettingKeys.CoinsBalance, next.ToString(CultureInfo.InvariantCulture), ct);
        Changed?.Invoke(this, EventArgs.Empty);
        return next;
    }

    public Task<IReadOnlyList<CustomReward>> GetRewardsAsync(CancellationToken ct = default) =>
        _rewards.GetAllAsync(ct);

    public Task<IReadOnlyList<RewardRedemption>> GetRecentRedemptionsAsync(int limit = 20, CancellationToken ct = default) =>
        _rewards.GetRecentRedemptionsAsync(limit, ct);

    public async Task<(RewardResult Result, CustomReward? Reward)> SaveRewardAsync(
        Guid? id,
        string title,
        string emoji,
        int costCoins,
        string? note = null,
        CancellationToken ct = default)
    {
        if (!RewardWallet.IsValidReward(title, costCoins))
        {
            return (RewardResult.InvalidInput, null);
        }

        var existing = await _rewards.GetAllAsync(ct);
        var isNew = id is null || existing.All(r => r.Id != id.Value);

        if (isNew && existing.Count >= _entitlements.MaxCustomRewards)
        {
            return (RewardResult.LimitReached, null);
        }

        var reward = new CustomReward(
            id ?? Guid.CreateVersion7(),
            title.Trim(),
            string.IsNullOrWhiteSpace(emoji) ? "🎁" : emoji.Trim(),
            costCoins,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            DateTime.UtcNow);

        await _rewards.UpsertAsync(reward, ct);
        Changed?.Invoke(this, EventArgs.Empty);
        return (RewardResult.Ok, reward);
    }

    public async Task DeleteRewardAsync(Guid id, CancellationToken ct = default)
    {
        await _rewards.DeleteAsync(id, ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<(RewardResult Result, int RemainingCoins)> RedeemAsync(Guid rewardId, CancellationToken ct = default)
    {
        var all = await _rewards.GetAllAsync(ct);
        var reward = all.FirstOrDefault(r => r.Id == rewardId);
        if (reward is null)
        {
            return (RewardResult.NotFound, await GetBalanceAsync(ct));
        }

        var balance = await GetBalanceAsync(ct);
        if (!RewardWallet.CanRedeem(balance, reward.CostCoins))
        {
            return (RewardResult.InsufficientCoins, balance);
        }

        var next = RewardWallet.Redeem(balance, reward.CostCoins);
        await _settings.SetAsync(SettingKeys.CoinsBalance, next.ToString(CultureInfo.InvariantCulture), ct);
        await _rewards.RecordRedemptionAsync(
            new RewardRedemption(Guid.CreateVersion7(), reward.Id, reward.Title, reward.CostCoins, DateTime.UtcNow),
            ct);
        Changed?.Invoke(this, EventArgs.Empty);
        return (RewardResult.Ok, next);
    }
}

public sealed class GoalOfTheDayService
{
    private readonly ISettingsStore _settings;

    public GoalOfTheDayService(ISettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public event EventHandler? Changed;

    public async Task<Guid?> GetGoalHabitIdAsync(DateOnly today, CancellationToken ct = default)
    {
        var dateRaw = await _settings.GetAsync(SettingKeys.GoalOfDayDate, ct);
        if (!DateOnly.TryParseExact(dateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var savedDate)
            || savedDate != today)
        {
            return null;
        }

        var idRaw = await _settings.GetAsync(SettingKeys.GoalOfDayHabitId, ct);
        return Guid.TryParse(idRaw, out var id) ? id : null;
    }

    public async Task SetGoalAsync(Guid? habitId, DateOnly today, CancellationToken ct = default)
    {
        if (habitId is null)
        {
            await _settings.RemoveAsync(SettingKeys.GoalOfDayHabitId, ct);
            await _settings.RemoveAsync(SettingKeys.GoalOfDayDate, ct);
        }
        else
        {
            await _settings.SetAsync(SettingKeys.GoalOfDayDate, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ct);
            await _settings.SetAsync(SettingKeys.GoalOfDayHabitId, habitId.Value.ToString("D"), ct);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class AreaService
{
    private readonly IHabitAreaRepository _areas;
    private readonly IEntitlements _entitlements;

    public AreaService(IHabitAreaRepository areas, IEntitlements entitlements)
    {
        _areas = areas ?? throw new ArgumentNullException(nameof(areas));
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
    }

    public Task<IReadOnlyList<HabitArea>> GetAllAsync(CancellationToken ct = default) =>
        _areas.GetAllAsync(ct);

    public async Task<(bool Ok, string? ErrorKey, HabitArea? Area)> SaveAsync(
        Guid? id,
        string name,
        string emoji,
        string colorHex,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 30)
        {
            return (false, "validation.name.required", null);
        }

        var all = await _areas.GetAllAsync(ct);
        var isNew = id is null || all.All(a => a.Id != id.Value);
        if (isNew && all.Count >= _entitlements.MaxAreas)
        {
            return (false, "area.err.limit", null);
        }

        var area = new HabitArea(
            id ?? Guid.CreateVersion7(),
            name.Trim(),
            string.IsNullOrWhiteSpace(emoji) ? "🏷️" : emoji.Trim(),
            string.IsNullOrWhiteSpace(colorHex) ? "#8B5CF6" : colorHex.Trim());

        await _areas.UpsertAsync(area, ct);
        return (true, null, area);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        _areas.DeleteAsync(id, ct);
}

public sealed class QuickTodoService
{
    private readonly IQuickTodoRepository _todos;
    private readonly RewardService? _rewards;

    public QuickTodoService(IQuickTodoRepository todos, RewardService? rewards = null)
    {
        _todos = todos ?? throw new ArgumentNullException(nameof(todos));
        _rewards = rewards;
    }

    public Task<IReadOnlyList<QuickTodo>> GetForDayAsync(DateOnly day, CancellationToken ct = default) =>
        _todos.GetForDayAsync(day, ct);

    public async Task<QuickTodo?> AddAsync(string title, DateOnly day, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var todo = new QuickTodo(Guid.CreateVersion7(), title.Trim(), day, false, DateTime.UtcNow);
        await _todos.InsertAsync(todo, ct);
        return todo;
    }

    public async Task ToggleAsync(QuickTodo todo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(todo);
        var nextDone = !todo.IsDone;
        await _todos.SetDoneAsync(todo.Id, nextDone, nextDone ? DateTime.UtcNow : null, ct);

        if (_rewards is not null)
        {
            await _rewards.AddCoinsAsync(nextDone ? RewardWallet.QuickTodoCoins : -RewardWallet.QuickTodoCoins, ct);
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        _todos.DeleteAsync(id, ct);
}

public sealed class GentleRepairService
{
    private readonly IHabitRepository _habits;
    private readonly IDayLogRepository _logs;

    public GentleRepairService(IHabitRepository habits, IDayLogRepository logs)
    {
        _habits = habits ?? throw new ArgumentNullException(nameof(habits));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
    }

    public async Task<DateOnly?> RepairLatestAsync(Guid habitId, DateOnly today, CancellationToken ct = default)
    {
        var habit = await _habits.GetByIdAsync(habitId, ct);
        if (habit is null)
        {
            return null;
        }

        var logs = await _logs.GetAllForHabitAsync(habitId, ct);
        var missedDay = GentleStreakRules.FindRepairableMissedDay(habit, logs, today);
        if (missedDay is null)
        {
            return null;
        }

        var repaired = new DayLog
        {
            HabitId = habitId,
            Day = missedDay.Value,
            Value = habit.EffectiveTarget,
            Status = DayStatus.Frozen,
            Note = "Gentle repair",
            CheckedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _logs.UpsertAsync(repaired, ct);
        return missedDay;
    }
}
