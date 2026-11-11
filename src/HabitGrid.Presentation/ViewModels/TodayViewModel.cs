using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using IDayProvider = HabitGrid.Core.Services.IDayProvider;

namespace HabitGrid.Presentation.ViewModels;

/// <summary>Một thẻ thói quen trên trang Hôm nay. Được tái sử dụng giữa các lần tải để giao diện chạy được hiệu ứng khi giá trị đổi.</summary>
public sealed partial class HabitTodayItem : ObservableObject
{
    public HabitTodayItem(Habit habit)
    {
        Habit = habit;
    }

    public Habit Habit { get; private set; }

    public Guid Id => Habit.Id;

    public string Name => Habit.Name;

    public string Icon => Habit.Icon.Value;

    public string ColorHue => Habit.ColorHue;

    public HabitKind Kind => Habit.Kind;

    public int Target => Habit.EffectiveTarget;

    public string? Unit => Habit.Target.Unit;

    [ObservableProperty]
    private int _value;

    [ObservableProperty]
    private DayStatus _status;

    [ObservableProperty]
    private int _streak;

    [ObservableProperty]
    private StreakUnit _streakUnit;

    [ObservableProperty]
    private bool _atRisk;

    /// <summary>Thói quen vượt giới hạn gói Free sau khi hạ cấp: xem được, không check-in được (SPEC-V2-10 §5).</summary>
    [ObservableProperty]
    private bool _isReadOnly;

    /// <summary>Trạng thái 7 ngày kết thúc ở ngày đang xem (cũ → mới), dùng cho mini heatmap.</summary>
    [ObservableProperty]
    private IReadOnlyList<DayStatus> _lastSevenDays = [];

    public bool IsDone => Status == DayStatus.Done;

    public bool IsSkipped => Status is DayStatus.Skipped or DayStatus.Frozen;

    /// <summary>0..1, dùng cho thanh tiến độ của loại đếm/thời lượng.</summary>
    public double Progress => Kind switch
    {
        HabitKind.Boolean => IsDone ? 1 : 0,
        HabitKind.Quit => Value == 0 ? 1 : 0,
        _ => Math.Clamp(Value / (double)Target, 0, 1)
    };

    /// <summary>Thay dữ liệu thói quen (sau khi sửa) mà vẫn giữ cùng đối tượng thẻ.</summary>
    internal void Replace(Habit habit)
    {
        Habit = habit;
        OnPropertyChanged(string.Empty);
    }

    partial void OnValueChanged(int value) => OnPropertyChanged(nameof(Progress));

    partial void OnStatusChanged(DayStatus value)
    {
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(Progress));
    }
}

/// <summary>Nhóm thẻ theo khung giờ. Gồm cả thẻ đã xong: thẻ đổi màu tại chỗ chứ không nhảy sang chỗ khác.</summary>
public sealed record TodayGroup(TimeOfDay TimeOfDay, IReadOnlyList<HabitTodayItem> Items);

/// <summary>Một ngày trong dải tuần.</summary>
public sealed record DayBubble(DateOnly Day, double? Fraction, bool IsToday, bool IsSelected, bool IsFuture);

/// <summary>Thao tác gần nhất để hoàn tác.</summary>
public sealed record UndoEntry(Guid HabitId, string HabitName, DateOnly Day, DayLog? Previous);

public enum BuddyMood
{
    Sleepy,
    Okay,
    Happy,
    Ecstatic
}

/// <summary>
/// Trang "Hôm nay" (03-UI-REBUILD-GLOW2 §5.2): dải tuần, hero (Buddy + tiến độ), streak tổng, lưới thẻ thói quen
/// theo khung giờ, check-in một chạm cho ngày đang xem (kể cả ngày cũ), bỏ qua, hoàn tác.
/// </summary>
public sealed partial class TodayViewModel : ObservableObject, IDisposable
{
    private readonly IHabitRepository _habits;
    private readonly IDayLogRepository _logs;
    private readonly IDayProvider _dayProvider;
    private readonly IStreakCalculator _streaks;
    private readonly IScheduleCalculator _schedule;
    private readonly IDataChangeBus? _bus;
    private readonly IUiDispatcher _dispatcher;
    private readonly IAppLogger _logger;
    private readonly IProfileRepository? _profile;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private IReadOnlySet<Guid> _readOnly = new HashSet<Guid>();

    /// <summary>Nguồn danh sách thói quen chỉ đọc (settings dùng chung); null = không giới hạn.</summary>
    public ISettingsStore? Settings { get; init; }
    private readonly Dictionary<Guid, HabitTodayItem> _items = [];
    private string _structure = string.Empty;
    private int _writes;
    private bool _disposed;

    public TodayViewModel(
        IHabitRepository habits,
        IDayLogRepository logs,
        IDayProvider dayProvider,
        IStreakCalculator? streaks = null,
        IScheduleCalculator? schedule = null,
        IDataChangeBus? bus = null,
        IUiDispatcher? dispatcher = null,
        IAppLogger? logger = null,
        IProfileRepository? profile = null)
    {
        _habits = habits ?? throw new ArgumentNullException(nameof(habits));
        _logs = logs ?? throw new ArgumentNullException(nameof(logs));
        _dayProvider = dayProvider ?? throw new ArgumentNullException(nameof(dayProvider));
        _streaks = streaks ?? new StreakCalculator();
        _schedule = schedule ?? new ScheduleCalculator();
        _bus = bus;
        _dispatcher = dispatcher ?? new ImmediateDispatcher();
        _logger = logger ?? NullLogger.Instance;
        _profile = profile;

        if (_bus is not null)
        {
            _bus.Changed += OnDataChanged;
        }
    }

    /// <summary>Thẻ cần làm ở ngày đang xem, nhóm theo khung giờ (gồm cả thẻ đã xong).</summary>
    public ObservableCollection<TodayGroup> Groups { get; } = [];

    /// <summary>Các thẻ đã xong (tập con của <see cref="Groups"/>, tiện cho đếm và kiểm thử).</summary>
    public ObservableCollection<HabitTodayItem> Completed { get; } = [];

    /// <summary>Không có lịch ở ngày đang xem.</summary>
    public ObservableCollection<HabitTodayItem> NotToday { get; } = [];

    /// <summary>Dải tuần hiện tại (Thứ Hai → Chủ Nhật).</summary>
    public ObservableCollection<DayBubble> Week { get; } = [];

    /// <summary>Hôm nay thật (theo mốc chuyển ngày).</summary>
    public DateOnly Today { get; private set; }

    /// <summary>Ngày đang xem/sửa. Mặc định là hôm nay.</summary>
    public DateOnly SelectedDay { get; private set; }

    public bool IsViewingPast => SelectedDay != default && SelectedDay < Today;

    [ObservableProperty]
    private int _doneCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private UndoEntry? _lastAction;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Số ngày liên tiếp có ít nhất một thói quen xong (hôm nay chưa xong thì chuỗi vẫn sống).</summary>
    [ObservableProperty]
    private int _overallStreak;

    [ObservableProperty]
    private int _freezeTokens;

    [ObservableProperty]
    private string _buddyName = string.Empty;

    [ObservableProperty]
    private long _xp;

    [ObservableProperty]
    private int _level = 1;

    public double ProgressFraction => TotalCount == 0 ? 0 : DoneCount / (double)TotalCount;

    public bool AllDone => TotalCount > 0 && DoneCount == TotalCount;

    public bool CanUndo => LastAction is not null;

    public BuddyMood BuddyMood => !AllDone && TotalCount == 0
        ? BuddyMood.Sleepy
        : AllDone
            ? BuddyMood.Ecstatic
            : DoneCount == 0
                ? BuddyMood.Sleepy
                : ProgressFraction < 0.5 ? BuddyMood.Okay : BuddyMood.Happy;

    partial void OnDoneCountChanged(int value) => RaiseProgress();

    partial void OnTotalCountChanged(int value) => RaiseProgress();

    partial void OnLastActionChanged(UndoEntry? value) => OnPropertyChanged(nameof(CanUndo));

    private void RaiseProgress()
    {
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(AllDone));
        OnPropertyChanged(nameof(BuddyMood));
    }

    // ------------------------------------------------------------------ chọn ngày

    /// <summary>Xem (và sửa) một ngày trong tuần. Ngày tương lai bị bỏ qua.</summary>
    public async Task SelectDayAsync(DateOnly day)
    {
        var today = _dayProvider.GetToday();
        if (day > today)
        {
            return;
        }

        SelectedDay = day;
        OnPropertyChanged(nameof(SelectedDay));
        await LoadAsync();
    }

    // ------------------------------------------------------------------ nạp dữ liệu

    [RelayCommand]
    public async Task LoadAsync()
    {
        await _loadGate.WaitAsync();

        try
        {
            _readOnly = Settings is null ? new HashSet<Guid>() : await ReadOnlyHabits.LoadAsync(Settings);
            IsLoading = true;
            ErrorMessage = null;

            var today = _dayProvider.GetToday();
            var rolledOver = Today != default && today != Today;
            var selected = SelectedDay == default || rolledOver && SelectedDay == Today || SelectedDay > today ? today : SelectedDay;

            var habits = await _habits.GetAllActiveAsync();
            var logsByHabit = new Dictionary<Guid, IReadOnlyList<DayLog>>();

            foreach (var habit in habits)
            {
                logsByHabit[habit.Id] = await _logs.GetAllForHabitAsync(habit.Id);
            }

            var profile = _profile is null ? null : await _profile.GetAsync();
            var week = BuildWeek(habits, logsByHabit, today, selected);
            var streak = ComputeOverallStreak(logsByHabit, today);

            _dispatcher.Post(() =>
            {
                Today = today;
                SelectedDay = selected;
                OnPropertyChanged(nameof(SelectedDay));
                OnPropertyChanged(nameof(IsViewingPast));
                Apply(habits, logsByHabit, today, selected);

                Week.Clear();
                foreach (var bubble in week)
                {
                    Week.Add(bubble);
                }

                OverallStreak = streak;

                if (profile is not null)
                {
                    BuddyName = profile.BuddyName;
                    Xp = profile.Xp;
                    Level = profile.Level;
                    FreezeTokens = profile.FreezeTokens;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Tải trang Hôm nay thất bại", ex);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
            _loadGate.Release();
        }
    }

    private void Apply(
        IReadOnlyList<Habit> habits,
        Dictionary<Guid, IReadOnlyList<DayLog>> logsByHabit,
        DateOnly today,
        DateOnly selected)
    {
        var scheduled = new List<HabitTodayItem>();
        var unscheduled = new List<HabitTodayItem>();
        var seen = new HashSet<Guid>();

        foreach (var habit in habits)
        {
            seen.Add(habit.Id);

            // Tái sử dụng thẻ khi thói quen không đổi để giao diện chạy hiệu ứng thay vì dựng lại.
            if (!_items.TryGetValue(habit.Id, out var item))
            {
                item = new HabitTodayItem(habit);
                _items[habit.Id] = item;
            }
            else if (item.Habit.Version != habit.Version)
            {
                item.Replace(habit);
            }

            FillItem(item, habit, logsByHabit[habit.Id], today, selected);
            item.IsReadOnly = _readOnly.Contains(habit.Id);
            (_schedule.IsScheduled(habit, selected) ? scheduled : unscheduled).Add(item);
        }

        foreach (var gone in _items.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _items.Remove(gone);
        }

        // Chỉ dựng lại cấu trúc nhóm khi tập thẻ/thứ tự đổi; còn lại thẻ tự cập nhật qua PropertyChanged.
        var groups = new List<TodayGroup>();
        foreach (var time in new[] { TimeOfDay.Morning, TimeOfDay.Afternoon, TimeOfDay.Evening, TimeOfDay.Anytime })
        {
            var inGroup = scheduled.Where(i => i.Habit.TimeOfDay == time).ToList();
            if (inGroup.Count > 0)
            {
                groups.Add(new TodayGroup(time, inGroup));
            }
        }

        var structure = string.Join(
            ";",
            groups.Select(g => $"{g.TimeOfDay}:{string.Join(",", g.Items.Select(i => i.Id))}"))
            + "|" + string.Join(",", unscheduled.Select(i => i.Id));

        if (structure != _structure)
        {
            _structure = structure;
            Groups.Clear();
            foreach (var group in groups)
            {
                Groups.Add(group);
            }
        }

        // Thói quen "cần bỏ" luôn hiển thị (để ghi nhận lần lỡ) nhưng không tính vào tiến độ ngày.
        var trackable = scheduled.Where(i => i.Kind != HabitKind.Quit).ToList();

        Completed.Clear();
        foreach (var item in trackable.Where(i => i.IsDone))
        {
            Completed.Add(item);
        }

        NotToday.Clear();
        foreach (var item in unscheduled)
        {
            NotToday.Add(item);
        }

        TotalCount = trackable.Count;
        DoneCount = Completed.Count;
        IsEmpty = habits.Count == 0;
    }

    private void FillItem(HabitTodayItem item, Habit habit, IReadOnlyList<DayLog> logs, DateOnly today, DateOnly selected)
    {
        var selectedLog = logs.FirstOrDefault(l => l.Day == selected);

        // Streak luôn tính theo hôm nay thật; chỉ giá trị/trạng thái theo ngày đang xem.
        var streak = _streaks.Calculate(habit, logs, today);

        item.Value = selectedLog?.Value ?? 0;
        item.Status = selectedLog?.Status ?? DayStatus.None;
        item.Streak = streak.Current;
        item.StreakUnit = streak.Unit;
        item.AtRisk = streak.AtRiskToday && selected == today;

        var byDay = logs.ToDictionary(l => l.Day, l => l.Status);
        item.LastSevenDays = Enumerable.Range(-6, 7)
            .Select(offset => byDay.GetValueOrDefault(selected.AddDays(offset), DayStatus.None))
            .ToList();
    }

    private List<DayBubble> BuildWeek(
        IReadOnlyList<Habit> habits,
        Dictionary<Guid, IReadOnlyList<DayLog>> logsByHabit,
        DateOnly today,
        DateOnly selected)
    {
        var start = PeriodCalendar.StartOfWeek(today, DayOfWeek.Monday);
        var bubbles = new List<DayBubble>();

        for (var i = 0; i < 7; i++)
        {
            var day = start.AddDays(i);
            var future = day > today;
            double? fraction = null;

            if (!future)
            {
                var total = 0;
                var done = 0;

                foreach (var habit in habits.Where(h => h.Kind != HabitKind.Quit && _schedule.IsScheduled(h, day)))
                {
                    total++;
                    if (logsByHabit[habit.Id].Any(l => l.Day == day && l.Status == DayStatus.Done))
                    {
                        done++;
                    }
                }

                fraction = total == 0 ? null : done / (double)total;
            }

            bubbles.Add(new DayBubble(day, fraction, day == today, day == selected, future));
        }

        return bubbles;
    }

    /// <summary>Chuỗi tổng (công thức dùng chung với widget, xem <see cref="HabitGrid.Core.Domain.Streaks.OverallStreak"/>).</summary>
    private static int ComputeOverallStreak(Dictionary<Guid, IReadOnlyList<DayLog>> logsByHabit, DateOnly today) =>
        HabitGrid.Core.Domain.Streaks.OverallStreak.Compute(logsByHabit.Values.SelectMany(l => l), today);

    // ------------------------------------------------------------------ check-in

    /// <summary>Chạm vào thẻ: có/không → đảo; đếm → +1; thời lượng → đảo giữa 0 và mục tiêu; cần bỏ → ghi một lần lỡ.</summary>
    [RelayCommand]
    public async Task CheckInAsync(HabitTodayItem? item)
    {
        if (item is null)
        {
            return;
        }

        var newValue = item.Kind switch
        {
            HabitKind.Boolean => item.IsDone ? 0 : 1,
            HabitKind.Count => item.Value + 1,
            HabitKind.Duration => item.IsDone ? 0 : item.Target,
            HabitKind.Quit => item.Value + 1,
            _ => item.Value
        };

        await SetValueAsync(item, newValue);
    }

    /// <summary>Giảm một đơn vị (menu của loại đếm, hoặc xóa một lần lỡ của loại cần bỏ).</summary>
    [RelayCommand]
    public async Task DecrementAsync(HabitTodayItem? item)
    {
        if (item is null || item.Value <= 0)
        {
            return;
        }

        await SetValueAsync(item, item.Value - 1);
    }

    /// <summary>Đặt giá trị cụ thể cho ngày đang xem.</summary>
    public async Task SetValueAsync(HabitTodayItem item, int newValue)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsReadOnly)
        {
            ErrorMessage = "plan.readonly";
            return;
        }

        var day = SelectedDay == default ? Today : SelectedDay;
        Interlocked.Increment(ref _writes);

        try
        {
            var previous = await _logs.GetAsync(item.Id, day);

            // Ngày đã đánh dấu bỏ qua/đóng băng giữ nguyên trạng thái đó khi chưa có giá trị.
            var keepManualStatus = previous is { Status: DayStatus.Skipped or DayStatus.Frozen } && newValue <= 0;

            var log = DayLogRules.Normalize(item.Habit, new DayLog
            {
                HabitId = item.Id,
                Day = day,
                Value = Math.Max(0, newValue),
                Note = previous?.Note,
                Mood = previous?.Mood,
                Status = keepManualStatus ? previous!.Status : DayStatus.None,
                CheckedAt = newValue > 0 ? DateTime.UtcNow : null
            });

            // Về 0 và không có ghi chú/mood thì xóa dòng thay vì lưu một dòng trống.
            var isBlank = log.Value == 0 && log.Note is null && log.Mood is null &&
                          log.Status is DayStatus.None or DayStatus.Done;

            if (isBlank)
            {
                if (previous is not null)
                {
                    await _logs.DeleteAsync(item.Id, day);
                }
            }
            else
            {
                await _logs.UpsertAsync(log);
            }

            LastAction = new UndoEntry(item.Id, item.Name, day, previous);
        }
        catch (Exception ex)
        {
            _logger.Error($"Check-in thất bại cho {item.Name}", ex);
            ErrorMessage = ex.Message;
            return;
        }
        finally
        {
            Interlocked.Decrement(ref _writes);
        }

        await LoadAsync();
    }

    /// <summary>Bỏ qua (hoặc bỏ đánh dấu bỏ qua) ngày đang xem. Tối đa 2 lần/7 ngày cho mỗi thói quen.</summary>
    [RelayCommand]
    public async Task SkipAsync(HabitTodayItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsReadOnly)
        {
            ErrorMessage = "plan.readonly";
            return;
        }

        var day = SelectedDay == default ? Today : SelectedDay;
        Interlocked.Increment(ref _writes);

        try
        {
            var previous = await _logs.GetAsync(item.Id, day);

            if (previous is { Status: DayStatus.Skipped })
            {
                await _logs.DeleteAsync(item.Id, day);
            }
            else
            {
                var all = await _logs.GetAllForHabitAsync(item.Id);

                if (!DayLogRules.CanSkip(all, day))
                {
                    ErrorMessage = "skip.limit";
                    return;
                }

                await _logs.UpsertAsync(new DayLog
                {
                    HabitId = item.Id,
                    Day = day,
                    Value = 0,
                    Status = DayStatus.Skipped,
                    Note = previous?.Note,
                    Mood = previous?.Mood
                });
            }

            LastAction = new UndoEntry(item.Id, item.Name, day, previous);
        }
        catch (Exception ex)
        {
            _logger.Error($"Bỏ qua thất bại cho {item.Name}", ex);
            ErrorMessage = ex.Message;
            return;
        }
        finally
        {
            Interlocked.Decrement(ref _writes);
        }

        await LoadAsync();
    }

    /// <summary>Hoàn tác thao tác gần nhất.</summary>
    [RelayCommand]
    public async Task UndoAsync()
    {
        var action = LastAction;
        if (action is null)
        {
            return;
        }

        Interlocked.Increment(ref _writes);

        try
        {
            if (action.Previous is null)
            {
                await _logs.DeleteAsync(action.HabitId, action.Day);
            }
            else
            {
                await _logs.UpsertAsync(action.Previous);
            }

            LastAction = null;
        }
        catch (Exception ex)
        {
            _logger.Error("Hoàn tác thất bại", ex);
            ErrorMessage = ex.Message;
            return;
        }
        finally
        {
            Interlocked.Decrement(ref _writes);
        }

        await LoadAsync();
    }

    // ------------------------------------------------------------------ làm mới theo dữ liệu

    private void OnDataChanged(object? sender, DataChange change)
    {
        // Thay đổi do chính mình gây ra đã được nạp lại ở cuối thao tác.
        if (_disposed || Volatile.Read(ref _writes) > 0)
        {
            return;
        }

        if (change.Entity is DataEntity.Habit or DataEntity.DayLog or DataEntity.Profile or DataEntity.Everything)
        {
            _ = LoadAsync();
        }
    }

    public void Dispose()
    {
        _disposed = true;

        if (_bus is not null)
        {
            _bus.Changed -= OnDataChanged;
        }

        _loadGate.Dispose();
    }
}
