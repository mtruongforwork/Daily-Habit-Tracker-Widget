namespace HabitGrid.Core.Domain.Streaks;

/// <summary>
/// Chuỗi tổng của người dùng: số ngày liên tiếp có ít nhất một thói quen xong. Ngày đóng băng nối chuỗi nhưng không được đếm.
/// Chưa check hôm nay thì chuỗi tính tới hôm qua (không bị "gãy" cho tới hết ngày). Dùng chung cho trang Hôm nay và widget.
/// </summary>
public static class OverallStreak
{
    private const int MaxDays = 3700;

    public static int Compute(IEnumerable<DayLog> logs, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(logs);

        var all = logs as IReadOnlyCollection<DayLog> ?? logs.ToList();
        var done = all.Where(l => l.Status == DayStatus.Done).Select(l => l.Day).ToHashSet();
        var frozen = all.Where(l => l.Status == DayStatus.Frozen).Select(l => l.Day).ToHashSet();

        var day = done.Contains(today) ? today : today.AddDays(-1);
        var count = 0;

        for (var guard = 0; guard < MaxDays; guard++)
        {
            if (done.Contains(day))
            {
                count++;
            }
            else if (!frozen.Contains(day))
            {
                break;
            }

            day = day.AddDays(-1);
        }

        return count;
    }
}
