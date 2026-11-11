using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Calendar;
using HabitGrid.Core.Services;

namespace HabitGrid.Presentation.Services;

/// <summary>
/// Dữ liệu và thao tác cho trang Lịch: lưới tháng (mỗi ngày đã xong bao nhiêu việc) và xem lại/sửa một ngày bất kỳ trong quá khứ.
/// Dùng lại <see cref="QuickCheckInModel"/> cho danh sách của ngày được chọn nên check/đếm hoạt động y hệt trang Hôm nay.
/// </summary>
public sealed class CalendarModel
{
    private readonly IHabitRepository _habits;
    private readonly IDayLogRepository _logs;
    private readonly IDayProvider _days;
    private readonly HabitGrid.Core.Domain.IScheduleCalculator _schedule;
    private readonly QuickCheckInModel _day;

    public CalendarModel(IHabitRepository habits, IDayLogRepository logs, IDayProvider days, ISettingsStore? settings = null, HabitGrid.Core.Domain.IScheduleCalculator? schedule = null)
    {
        _habits = habits ?? throw new ArgumentNullException(nameof(habits));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        _days = days ?? throw new ArgumentNullException(nameof(days));
        _schedule = schedule ?? new HabitGrid.Core.Domain.ScheduleCalculator();
        _day = new QuickCheckInModel(habits, logs, _schedule, settings);
    }

    public DateOnly Today => _days.GetToday();

    /// <summary>Tuần bắt đầu từ thứ Hai (cùng quy ước với widget).</summary>
    public DayOfWeek WeekStart { get; init; } = DayOfWeek.Monday;

    /// <summary>Tháng đang xem (ngày 1).</summary>
    public DateOnly Month { get; private set; }

    public DateOnly SelectedDay { get; private set; }

    public MonthGrid? Grid { get; private set; }

    /// <summary>Việc của ngày được chọn (đến hạn ngày đó).</summary>
    public IReadOnlyList<QuickItem> DayItems => _day.Items;

    /// <summary>Không cho xem tháng sau tháng hiện tại (chưa có dữ liệu).</summary>
    public bool CanGoNext => Month < new DateOnly(Today.Year, Today.Month, 1);

    public async Task LoadAsync(DateOnly? month = null, DateOnly? selected = null, CancellationToken ct = default)
    {
        var today = Today;
        var target = month ?? Month;
        target = target == default ? today : target;

        if (target > today)
        {
            target = today;
        }

        Month = new DateOnly(target.Year, target.Month, 1);
        var first = Month;
        var last = first.AddMonths(1).AddDays(-1);

        var habits = (await _habits.GetAllActiveAsync(ct)).ToList();
        var logs = new Dictionary<(Guid, DateOnly), DayLog>();

        foreach (var habit in habits)
        {
            foreach (var log in await _logs.GetRangeAsync(habit.Id, first, last, ct))
            {
                logs[(habit.Id, log.Day)] = log;
            }
        }

        Grid = MonthCalendar.Build(first, WeekStart, habits, logs, _schedule, today);

        var pick = selected ?? (SelectedDay == default ? today : SelectedDay);

        // Ngày được chọn phải thuộc tháng đang xem và không ở tương lai.
        if (pick < first || pick > last || pick > today)
        {
            pick = today >= first && today <= last ? today : (last > today ? today : last);
        }

        SelectedDay = pick;
        await _day.LoadAsync(SelectedDay, ct);
    }

    public Task SelectDayAsync(DateOnly day, CancellationToken ct = default) => LoadAsync(Month, day, ct);

    public Task ShiftMonthAsync(int delta, CancellationToken ct = default) => LoadAsync(Month.AddMonths(delta), null, ct);

    public async Task ToggleAsync(QuickItem item, CancellationToken ct = default)
    {
        await _day.ToggleAsync(item, ct);
        await LoadAsync(Month, SelectedDay, ct);
    }

    public async Task AdjustAsync(QuickItem item, int delta, CancellationToken ct = default)
    {
        await _day.AdjustAsync(item, delta, ct);
        await LoadAsync(Month, SelectedDay, ct);
    }
}
