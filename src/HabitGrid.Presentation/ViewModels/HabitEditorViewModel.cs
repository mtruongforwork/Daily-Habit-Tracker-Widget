using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HabitGrid.Core.Domain;
using HabitGrid.Presentation.Services;
using DayOfWeekMask = HabitGrid.Core.Models.DayOfWeekMask;
using DayOfWeekMaskExtensions = HabitGrid.Core.Models.DayOfWeekMaskExtensions;
using IDayProvider = HabitGrid.Core.Services.IDayProvider;

namespace HabitGrid.Presentation.ViewModels;

public enum FrequencyType
{
    Daily,
    WeekDays,
    TimesPerWeek,
    TimesPerMonth,
    EveryNDays
}

/// <summary>Một dòng nhắc nhở đang chỉnh sửa.</summary>
public sealed partial class ReminderEdit : ObservableObject
{
    public ReminderEdit(Reminder reminder)
    {
        _time = reminder.Time;
        _days = reminder.Days;
        _smart = reminder.Smart;
        _followUp = reminder.FollowUp;
    }

    [ObservableProperty]
    private TimeOnly _time;

    [ObservableProperty]
    private DayOfWeekMask _days;

    [ObservableProperty]
    private bool _smart;

    [ObservableProperty]
    private bool _followUp;

    public Reminder ToReminder() => new(Time, Days, Smart, FollowUp);
}

/// <summary>Kết quả lưu từ trình chỉnh sửa.</summary>
public sealed record SaveHabitResult(Habit? Habit, IReadOnlyList<ValidationError> Errors)
{
    public bool Succeeded => Habit is not null;
}

/// <summary>
/// Trình tạo/sửa thói quen đầy đủ (02-UX-SCREENS §6): cơ bản, loại, tần suất, khung giờ, nhắc nhở, ghi nhận thêm, nâng cao.
/// Dùng chung cho tạo mới và chỉnh sửa; kiểm tra bằng <see cref="HabitValidator"/> của Core.
/// </summary>
public sealed partial class HabitEditorViewModel : ObservableObject
{
    private readonly IHabitRepository _habits;
    private readonly IDayProvider _dayProvider;
    private readonly IEntitlements _entitlements;
    private Habit? _editing;

    public HabitEditorViewModel(
        IHabitRepository habits,
        IDayProvider dayProvider,
        IEntitlements? entitlements = null)
    {
        _habits = habits ?? throw new ArgumentNullException(nameof(habits));
        _dayProvider = dayProvider ?? throw new ArgumentNullException(nameof(dayProvider));
        _entitlements = entitlements ?? new UnlimitedEntitlements();

        _colorHue = HabitCreationService.PresetColors[0];
        _icon = HabitIcon.Emoji("✨").Value;
        _startDate = dayProvider.GetToday();
        _target = 1;
    }

    public bool IsEditing => _editing is not null;

    public Guid? EditingId => _editing?.Id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>Giá trị icon dạng <c>emoji:…</c> hoặc <c>fluent:…</c>.</summary>
    [ObservableProperty]
    private string _icon;

    [ObservableProperty]
    private string _colorHue;

    [ObservableProperty]
    private HabitKind _kind = HabitKind.Boolean;

    [ObservableProperty]
    private int _target;

    [ObservableProperty]
    private string _unit = string.Empty;

    [ObservableProperty]
    private FrequencyType _frequencyType = FrequencyType.Daily;

    [ObservableProperty]
    private DayOfWeekMask _weekDays = DayOfWeekMask.Monday | DayOfWeekMask.Wednesday | DayOfWeekMask.Friday;

    [ObservableProperty]
    private int _timesPerWeek = 3;

    [ObservableProperty]
    private int _timesPerMonth = 8;

    [ObservableProperty]
    private int _intervalDays = 2;

    [ObservableProperty]
    private TimeOfDay _timeOfDay = TimeOfDay.Anytime;

    [ObservableProperty]
    private DateOnly _startDate;

    [ObservableProperty]
    private bool _askMood;

    [ObservableProperty]
    private bool _allowNotes = true;

    [ObservableProperty]
    private bool _allowFreeze = true;

    [ObservableProperty]
    private bool _skipWeekends;

    [ObservableProperty]
    private Guid? _areaId;

    /// <summary>Các tag, phân cách bằng dấu phẩy.</summary>
    [ObservableProperty]
    private string _tagsText = string.Empty;

    public ObservableCollection<ReminderEdit> Reminders { get; } = [];

    /// <summary>Loại có mục tiêu số (đếm, thời lượng).</summary>
    public bool HasNumericTarget => Kind is HabitKind.Count or HabitKind.Duration;

    public bool CanAddReminder => Reminders.Count < _entitlements.MaxRemindersPerHabit;

    partial void OnKindChanged(HabitKind value)
    {
        OnPropertyChanged(nameof(HasNumericTarget));

        // Đổi loại thì mục tiêu về mặc định hợp lý để khỏi giữ 8 ly cho thói quen "có/không".
        Target = value switch
        {
            HabitKind.Duration when Target <= 1 => 20,
            HabitKind.Count when Target <= 0 => 1,
            HabitKind.Boolean or HabitKind.Quit or HabitKind.Dual => 1,
            _ => Target
        };
    }

    // ------------------------------------------------------------------ nạp / dựng

    /// <summary>Nạp thói quen có sẵn để sửa.</summary>
    public void LoadFrom(Habit habit)
    {
        ArgumentNullException.ThrowIfNull(habit);
        _editing = habit;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditingId));

        Name = habit.Name;
        Description = habit.Description ?? string.Empty;
        Icon = habit.Icon.Value;
        ColorHue = habit.ColorHue;
        Kind = habit.Kind;
        Target = habit.Target.Value;
        Unit = habit.Target.Unit ?? string.Empty;
        TimeOfDay = habit.TimeOfDay;
        StartDate = habit.StartDate;
        AskMood = habit.AskMood;
        AllowNotes = habit.AllowNotes;
        AllowFreeze = habit.AllowFreeze;
        SkipWeekends = habit.SkipWeekends;
        AreaId = habit.AreaId;
        TagsText = string.Join(", ", habit.Tags);

        switch (habit.Frequency)
        {
            case HabitFrequency.WeekDays w:
                FrequencyType = FrequencyType.WeekDays;
                WeekDays = w.Days;
                break;
            case HabitFrequency.TimesPerWeek w:
                FrequencyType = FrequencyType.TimesPerWeek;
                TimesPerWeek = w.Times;
                break;
            case HabitFrequency.TimesPerMonth m:
                FrequencyType = FrequencyType.TimesPerMonth;
                TimesPerMonth = m.Times;
                break;
            case HabitFrequency.EveryNDays e:
                FrequencyType = FrequencyType.EveryNDays;
                IntervalDays = e.N;
                break;
            default:
                FrequencyType = FrequencyType.Daily;
                break;
        }

        Reminders.Clear();
        foreach (var reminder in habit.Reminders)
        {
            Reminders.Add(new ReminderEdit(reminder));
        }

        OnPropertyChanged(nameof(CanAddReminder));
    }

    public HabitFrequency BuildFrequency() => FrequencyType switch
    {
        FrequencyType.WeekDays => new HabitFrequency.WeekDays(WeekDays),
        FrequencyType.TimesPerWeek => new HabitFrequency.TimesPerWeek(TimesPerWeek),
        FrequencyType.TimesPerMonth => new HabitFrequency.TimesPerMonth(TimesPerMonth),
        FrequencyType.EveryNDays => new HabitFrequency.EveryNDays(IntervalDays, _editing?.StartDate ?? StartDate),
        _ => new HabitFrequency.Daily()
    };

    /// <summary>Áp dụng trạng thái hiện tại lên một <see cref="Habit"/> (mới hoặc đang sửa).</summary>
    private Habit Apply(Habit habit, string sortKey)
    {
        habit.Name = Name.Trim();
        habit.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        habit.Icon = new HabitIcon(Icon);
        habit.ColorHue = ColorHue;
        habit.Kind = Kind;
        habit.Target = new HabitTarget(
            Kind is HabitKind.Boolean or HabitKind.Quit or HabitKind.Dual ? 1 : Target,
            string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim());
        habit.Frequency = BuildFrequency();
        habit.TimeOfDay = TimeOfDay;
        habit.StartDate = StartDate;
        habit.AskMood = AskMood;
        habit.AllowNotes = AllowNotes;
        habit.AllowFreeze = AllowFreeze;
        habit.SkipWeekends = SkipWeekends;
        habit.AreaId = AreaId;
        habit.Reminders = Reminders.Select(r => r.ToReminder()).ToList();
        habit.Tags = TagsText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        habit.SortKey = sortKey;
        return habit;
    }

    /// <summary>Bản sao đầy đủ của thói quen (kể cả các trường mà trình chỉnh sửa không đụng tới).</summary>
    private static Habit CloneForEdit(Habit source) => new()
    {
        Id = source.Id,
        CreatedAt = source.CreatedAt,
        Name = source.Name,
        Description = source.Description,
        Icon = source.Icon,
        ColorHue = source.ColorHue,
        Kind = source.Kind,
        Target = source.Target,
        Frequency = source.Frequency,
        TimeOfDay = source.TimeOfDay,
        StartDate = source.StartDate,
        Reminders = source.Reminders.ToList(),
        AskMood = source.AskMood,
        AllowNotes = source.AllowNotes,
        AllowFreeze = source.AllowFreeze,
        SkipWeekends = source.SkipWeekends,
        AreaId = source.AreaId,
        RoutineId = source.RoutineId,
        RoutineOrder = source.RoutineOrder,
        Tags = source.Tags.ToList(),
        SortKey = source.SortKey,
        ArchivedAt = source.ArchivedAt,
        DeletedAt = source.DeletedAt,
        UpdatedAt = source.UpdatedAt,
        Version = source.Version,
        SharedChallengeId = source.SharedChallengeId
    };

    /// <summary>Kiểm tra mà không lưu (dùng để bật/tắt nút Lưu và hiện lỗi).</summary>
    public IReadOnlyList<ValidationError> Validate() =>
        HabitValidator.Validate(Apply(new Habit(), _editing?.SortKey ?? "V"));

    // ------------------------------------------------------------------ nhắc nhở

    public ReminderEdit? AddReminder(TimeOnly? time = null)
    {
        if (!CanAddReminder)
        {
            return null;
        }

        var defaultTime = time ?? TimeOfDay switch
        {
            TimeOfDay.Morning => new TimeOnly(8, 0),
            TimeOfDay.Afternoon => new TimeOnly(14, 0),
            TimeOfDay.Evening => new TimeOnly(20, 0),
            _ => new TimeOnly(19, 0)
        };

        var edit = new ReminderEdit(new Reminder(defaultTime));
        Reminders.Add(edit);
        OnPropertyChanged(nameof(CanAddReminder));
        return edit;
    }

    public void RemoveReminder(ReminderEdit reminder)
    {
        if (Reminders.Remove(reminder))
        {
            OnPropertyChanged(nameof(CanAddReminder));
        }
    }

    /// <summary>Bật/tắt một ngày trong tuần của tần suất "các thứ cố định".</summary>
    public void ToggleWeekDay(DayOfWeek day)
    {
        var flag = DayOfWeekMaskExtensions.ToMask(day);
        WeekDays = WeekDays.HasFlag(flag) ? WeekDays & ~flag : WeekDays | flag;
    }

    // ------------------------------------------------------------------ lưu

    public async Task<SaveHabitResult> SaveAsync(CancellationToken ct = default)
    {
        if (_editing is { } existing)
        {
            // Làm việc trên bản sao: nếu không hợp lệ thì đối tượng đang hiển thị trong danh sách không bị đổi.
            var updated = Apply(CloneForEdit(existing), existing.SortKey);
            var errors = HabitValidator.Validate(updated);
            if (errors.Count > 0)
            {
                return new SaveHabitResult(null, errors);
            }

            await _habits.UpdateAsync(updated, ct);
            _editing = updated;
            return new SaveHabitResult(updated, []);
        }

        var all = await _habits.GetAllAsync(ct);

        if (all.Count(h => h.IsActive) >= _entitlements.MaxActiveHabits)
        {
            return new SaveHabitResult(null, [new ValidationError("limit.habits", nameof(Habit))]);
        }

        var last = all.Select(h => h.SortKey).OrderBy(k => k, StringComparer.Ordinal).LastOrDefault();
        var created = Apply(new Habit(), FractionalIndex.After(last));
        var createErrors = HabitValidator.Validate(created);

        if (createErrors.Count > 0)
        {
            return new SaveHabitResult(null, createErrors);
        }

        await _habits.InsertAsync(created, ct);
        return new SaveHabitResult(created, []);
    }
}
