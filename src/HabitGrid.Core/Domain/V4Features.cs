namespace HabitGrid.Core.Domain;

/// <summary>Lĩnh vực (Area) để phân loại thói quen (Sức khỏe, Công việc, Học tập...).</summary>
public sealed record HabitArea(
    Guid Id,
    string Name,
    string Emoji,
    string ColorHex,
    string SortKey = "V");

/// <summary>Việc cần làm 1 lần trong ngày (Quick To-Do), tách biệt với thói quen lặp lại.</summary>
public sealed record QuickTodo(
    Guid Id,
    string Title,
    DateOnly Day,
    bool IsDone,
    DateTime CreatedAt,
    DateTime? CompletedAt = null);

/// <summary>Phần thưởng tự định nghĩa ngoài đời do người dùng tạo và đổi bằng xu kiếm từ check-in.</summary>
public sealed record CustomReward(
    Guid Id,
    string Title,
    string Emoji,
    int CostCoins,
    string? Note = null,
    DateTime? CreatedAt = null);

/// <summary>Lịch sử một lần đổi phần thưởng.</summary>
public sealed record RewardRedemption(
    Guid Id,
    Guid RewardId,
    string RewardTitle,
    int CostCoins,
    DateTime RedeemedAt);

/// <summary>Hàm thuần tính xu thưởng và đổi quà (không phụ thuộc CSDL).</summary>
public static class RewardWallet
{
    public const int CheckInCoins = 10;
    public const int QuickTodoCoins = 5;
    public const int FocusSessionCoins = 15;
    public const int DualNegativePenalty = 3;
    public const int MinCostCoins = 5;
    public const int MaxCostCoins = 10_000;

    public static int EarnForCheckIn(bool isGoalOfDay) =>
        CheckInCoins * GoalOfTheDayRules.Multiplier(isGoalOfDay);

    public static int ApplyDelta(int currentCoins, int delta) =>
        Math.Max(0, currentCoins + delta);

    public static bool CanRedeem(int currentCoins, int costCoins) =>
        costCoins > 0 && currentCoins >= costCoins;

    public static int Redeem(int currentCoins, int costCoins)
    {
        if (!CanRedeem(currentCoins, costCoins))
        {
            throw new InvalidOperationException("Không đủ xu để đổi phần thưởng.");
        }

        return currentCoins - costCoins;
    }

    public static bool IsValidReward(string? title, int costCoins) =>
        !string.IsNullOrWhiteSpace(title)
        && title.Trim().Length <= 60
        && costCoins is >= MinCostCoins and <= MaxCostCoins;
}

/// <summary>Quy tắc Mục tiêu trọng tâm trong ngày (Goal of the Day): ×2 XP và ×2 xu.</summary>
public static class GoalOfTheDayRules
{
    public const int BonusMultiplier = 2;

    public static bool IsGoal(Guid? goalHabitId, Guid habitId) =>
        goalHabitId is { } id && id == habitId;

    public static int Multiplier(bool isGoal) => isGoal ? BonusMultiplier : 1;

    public static int ApplyXpBonus(int baseXp, bool isGoal) =>
        Math.Max(0, baseXp) * Multiplier(isGoal);
}

/// <summary>
/// Luật nhắc nhẹ "Đừng bỏ lỡ 2 ngày liên tiếp" (Never Miss Twice) và "Sửa chuỗi thân thiện" (Gentle Streak Repair).
/// </summary>
public static class GentleStreakRules
{
    /// <summary>
    /// Trả về true nếu thói quen đang có nguy cơ lỡ 2 ngày liên tiếp:
    /// ngày có lịch gần nhất trước <paramref name="today"/> bị lỡ (không Done/Skipped/Frozen),
    /// ngay trước ngày lỡ đó đang có chuỗi (&gt;= 1 ngày Done), và <paramref name="today"/> chưa Done.
    /// </summary>
    public static bool IsAtRiskForSecondMiss(
        Habit habit,
        IReadOnlyList<DayLog> logs,
        DateOnly today,
        IScheduleCalculator? schedule = null)
    {
        ArgumentNullException.ThrowIfNull(habit);
        ArgumentNullException.ThrowIfNull(logs);

        if (habit.Kind == HabitKind.Quit || today <= habit.StartDate)
        {
            return false;
        }

        schedule ??= new ScheduleCalculator();
        if (!schedule.IsScheduled(habit, today))
        {
            return false;
        }

        var byDay = logs.ToDictionary(l => l.Day);
        if (byDay.TryGetValue(today, out var todayLog) && todayLog.Status is DayStatus.Done or DayStatus.Skipped or DayStatus.Frozen)
        {
            return false;
        }

        // Tìm ngày có lịch gần nhất trước today.
        DateOnly? prevScheduled = null;
        for (var d = today.AddDays(-1); d >= habit.StartDate && d >= today.AddDays(-14); d = d.AddDays(-1))
        {
            if (schedule.IsScheduled(habit, d))
            {
                prevScheduled = d;
                break;
            }
        }

        if (prevScheduled is null)
        {
            return false;
        }

        // Nếu ngày có lịch gần nhất đã Done/Skipped/Frozen thì chưa lỡ.
        if (byDay.TryGetValue(prevScheduled.Value, out var prevLog)
            && prevLog.Status is DayStatus.Done or DayStatus.Skipped or DayStatus.Frozen)
        {
            return false;
        }

        // Kiểm tra xem ngay trước ngày lỡ đó có ít nhất 1 ngày Done không (tức là từng có chuỗi).
        for (var d = prevScheduled.Value.AddDays(-1); d >= habit.StartDate && d >= prevScheduled.Value.AddDays(-14); d = d.AddDays(-1))
        {
            if (!schedule.IsScheduled(habit, d))
            {
                continue;
            }

            return byDay.TryGetValue(d, out var beforeMiss) && beforeMiss.Status == DayStatus.Done;
        }

        return false;
    }

    /// <summary>
    /// Tìm ngày có lịch vừa lỡ trong vòng 48h (tối đa 2 ngày trước <paramref name="today"/>) để Sửa chuỗi (Gentle Repair).
    /// Trả về null nếu không đủ điều kiện (không có ngày lỡ gần, hoặc trước đó chưa có chuỗi).
    /// </summary>
    public static DateOnly? FindRepairableMissedDay(
        Habit habit,
        IReadOnlyList<DayLog> logs,
        DateOnly today,
        IScheduleCalculator? schedule = null)
    {
        ArgumentNullException.ThrowIfNull(habit);
        ArgumentNullException.ThrowIfNull(logs);

        if (habit.Kind == HabitKind.Quit || today <= habit.StartDate)
        {
            return null;
        }

        schedule ??= new ScheduleCalculator();
        var byDay = logs.ToDictionary(l => l.Day);

        // Chỉ xét 2 ngày gần nhất (hôm qua và hôm kia = cửa sổ 48h).
        for (var offset = 1; offset <= 2; offset++)
        {
            var candidate = today.AddDays(-offset);
            if (candidate < habit.StartDate || !schedule.IsScheduled(habit, candidate))
            {
                continue;
            }

            if (byDay.TryGetValue(candidate, out var log)
                && log.Status is DayStatus.Done or DayStatus.Skipped or DayStatus.Frozen)
            {
                continue;
            }

            // Phải có ít nhất 1 ngày Done ngay trước ngày lỡ đó.
            for (var d = candidate.AddDays(-1); d >= habit.StartDate && d >= candidate.AddDays(-14); d = d.AddDays(-1))
            {
                if (!schedule.IsScheduled(habit, d))
                {
                    continue;
                }

                if (byDay.TryGetValue(d, out var before) && before.Status == DayStatus.Done)
                {
                    return candidate;
                }

                break;
            }
        }

        return null;
    }
}
