namespace HabitGrid.Core.Domain.Focus;

/// <summary>Số liệu Focus cho trang Focus: phút hôm nay/tuần này/tổng, số phiên và biểu đồ 7 ngày gần nhất.</summary>
public sealed record FocusStatsData(int TodayMinutes, int WeekMinutes, int TotalMinutes, int Sessions, IReadOnlyList<(DateOnly Day, int Minutes)> Last7Days, int BestDayMinutes)
{
    public double TotalHours => TotalMinutes / 60.0;
}

/// <summary>Tính số liệu Focus từ nhật ký phiên. Thuần logic; ngày tính theo giờ địa phương của lúc kết thúc phiên.</summary>
public static class FocusStats
{
    public static FocusStatsData Compute(IEnumerable<FocusRecord> records, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(records);
        var list = records.ToList();

        var byDay = list.GroupBy(r => r.Day).ToDictionary(g => g.Key, g => g.Sum(r => r.Minutes));
        var last7 = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).Select(d => (d, byDay.GetValueOrDefault(d))).ToList();
        var weekStart = PeriodCalendar.StartOfWeek(today, DayOfWeek.Monday);
        var week = byDay.Where(kv => kv.Key >= weekStart && kv.Key <= today).Sum(kv => kv.Value);

        return new FocusStatsData(
            byDay.GetValueOrDefault(today),
            week,
            list.Sum(r => r.Minutes),
            list.Count,
            last7,
            byDay.Count == 0 ? 0 : byDay.Values.Max());
    }

    /// <summary>"1 giờ 05" / "45 phút" / "0 phút" dạng ngắn dùng chung cho hiển thị (chữ "giờ"/"phút" do giao diện thêm).</summary>
    public static (int Hours, int Minutes) Split(int totalMinutes) => (totalMinutes / 60, totalMinutes % 60);
}
