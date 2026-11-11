using System.Globalization;
using CommunityToolkit.WinUI.Notifications;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Reminders;
using HabitGrid.Presentation.Services;
using Windows.UI.Notifications;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Lên lịch toast nhắc bằng ScheduledToastNotification của Windows (SPEC-V2-09 §1.3): Windows tự hiện đúng giờ kể cả khi
/// app và WidgetHost đều tắt. Lập lại khi dữ liệu đổi, qua ngày, khởi động; chỉ thay khi kế hoạch thật sự khác (tránh xóa/thêm liên tục).
/// Nút trên toast (Xong, +1, Nhắc lại 30 phút, Bỏ qua hôm nay) ghi thẳng vào CSDL qua cùng repository.
/// </summary>
public sealed class ToastScheduler
{
    public const string Group = "reminders";
    public const string SnoozeGroup = "snooze";

    private readonly IHabitRepository _habits;
    private readonly IDayLogRepository _logs;
    private readonly ISettingsStore _settings;
    private string _lastSignature = string.Empty;

    public ToastScheduler(IHabitRepository habits, IDayLogRepository logs, ISettingsStore settings)
    {
        _habits = habits;
        _logs = logs;
        _settings = settings;
    }

    /// <summary>Lỗi gần nhất của nền tảng thông báo (máy không hỗ trợ, bị chính sách chặn…); null nếu ổn.</summary>
    public string? LastError { get; private set; }

    public IReadOnlyList<PlannedToast> LastPlan { get; private set; } = [];

    /// <summary>Số toast nhắc đang chờ trong lịch của Windows (để tự kiểm tra).</summary>
    public int ScheduledCount(string group = Group)
    {
        try
        {
            return ToastNotificationManagerCompat.CreateToastNotifier().GetScheduledToastNotifications().Count(t => t.Group == group);
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}" + (ex.InnerException is { } inner ? $" / inner {inner.GetType().Name} 0x{inner.HResult:X8}: {inner.Message}" : string.Empty);
            return -1;
        }
    }

    public async Task ReplanAsync(DateTime nowLocal)
    {
        try
        {
            var habits = await _habits.GetAllAsync();
            var logs = new List<DayLog>();

            foreach (var habit in habits.Where(h => h.IsActive && h.Kind != HabitKind.Quit))
            {
                logs.AddRange(await _logs.GetAllForHabitAsync(habit.Id));
            }

            // Thói quen chỉ đọc (vượt giới hạn Free) không được nhắc.
            var readOnly = await ReadOnlyHabits.LoadAsync(_settings);
            habits = habits.Where(h => !readOnly.Contains(h.Id)).ToList();
            var enabled = await ReminderSettingsStore.RemindersEnabledAsync(_settings);
            var settings = await ReminderSettingsStore.LoadAsync(_settings);
            var plan = enabled ? ReminderPlanner.Plan(habits, logs, nowLocal, settings) : [];
            LastPlan = plan;

            var signature = string.Join(';', plan.Select(p => $"{p.Id}:{string.Join(',', p.HabitIds)}"));

            if (signature == _lastSignature && ScheduledCount() == plan.Count)
            {
                return;
            }

            var names = habits.ToDictionary(h => h.Id);
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();

            foreach (var old in notifier.GetScheduledToastNotifications().Where(t => t.Group == Group).ToList())
            {
                notifier.RemoveFromSchedule(old);
            }

            foreach (var toast in plan)
            {
                var content = Build(toast.Kind, toast.HabitIds.Where(names.ContainsKey).Select(id => names[id]).ToList(), DateOnly.FromDateTime(toast.At));

                if (content is null)
                {
                    continue;
                }

                notifier.AddToSchedule(new ScheduledToastNotification(content.GetXml(), new DateTimeOffset(toast.At))
                {
                    Tag = toast.Id,
                    Group = Group
                });
            }

            _lastSignature = signature;
            LastError = null;
        }
        catch (Exception ex)
        {
            // Máy không có nền tảng thông báo (Windows Server lõi, chính sách công ty…): widget vẫn chạy bình thường.
            LastError = $"{ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}" + (ex.InnerException is { } inner ? $" / inner {inner.GetType().Name} 0x{inner.HResult:X8}: {inner.Message}" : string.Empty);
        }
    }

    /// <summary>Xử lý nút bấm trên toast. Trả về true nếu dữ liệu đã đổi.</summary>
    public async Task<bool> HandleAsync(ToastArguments args, Action? openMainApp = null)
    {
        if (!args.TryGetValue("action", out var action))
        {
            openMainApp?.Invoke();
            return false;
        }

        var ids = args.TryGetValue("ids", out var raw)
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList()
            : [];
        var day = args.TryGetValue("day", out var dayText) && DateOnly.TryParseExact(dayText, "yyyy-MM-dd", out var parsed)
            ? parsed
            : DateOnly.FromDateTime(DateTime.Now);

        switch (action)
        {
            case "done":
                foreach (var id in ids)
                {
                    await MarkDoneAsync(id, day);
                }

                return ids.Count > 0;

            case "plus":
                foreach (var id in ids)
                {
                    await AddOneAsync(id, day);
                }

                return ids.Count > 0;

            case "skip":
                var changed = false;

                foreach (var id in ids)
                {
                    changed |= await SkipAsync(id, day);
                }

                return changed;

            case "snooze":
                await SnoozeAsync(ids, day, args.TryGetValue("kind", out var kind) && Enum.TryParse<ToastKind>(kind, out var k) ? k : ToastKind.Reminder);
                return false;

            default:
                openMainApp?.Invoke();
                return false;
        }
    }

    private async Task MarkDoneAsync(Guid id, DateOnly day)
    {
        if (await _habits.GetByIdAsync(id) is not { DeletedAt: null } habit || habit.Kind == HabitKind.Quit
            || (await ReadOnlyHabits.LoadAsync(_settings)).Contains(id))
        {
            return;
        }

        var existing = await _logs.GetAsync(id, day);

        if (existing is { Status: DayStatus.Done })
        {
            return;
        }

        await _logs.UpsertAsync(DayLogRules.Normalize(habit, new DayLog
        {
            HabitId = id,
            Day = day,
            Value = Math.Max(habit.EffectiveTarget, existing?.Value ?? 0),
            Note = existing?.Note,
            Mood = existing?.Mood,
            CheckedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }));
    }

    private async Task AddOneAsync(Guid id, DateOnly day)
    {
        if (await _habits.GetByIdAsync(id) is not { DeletedAt: null } habit || habit.Kind == HabitKind.Quit
            || (await ReadOnlyHabits.LoadAsync(_settings)).Contains(id))
        {
            return;
        }

        var existing = await _logs.GetAsync(id, day);
        var current = existing is { Status: DayStatus.Skipped or DayStatus.Frozen } ? 0 : existing?.Value ?? 0;

        await _logs.UpsertAsync(DayLogRules.Normalize(habit, new DayLog
        {
            HabitId = id,
            Day = day,
            Value = current + 1,
            Note = existing?.Note,
            Mood = existing?.Mood,
            CheckedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }));
    }

    private async Task<bool> SkipAsync(Guid id, DateOnly day)
    {
        if (await _habits.GetByIdAsync(id) is not { DeletedAt: null } habit)
        {
            return false;
        }

        var all = await _logs.GetAllForHabitAsync(id);

        if (all.Any(l => l.Day == day && l.Status is DayStatus.Done or DayStatus.Skipped) || !DayLogRules.CanSkip(all, day))
        {
            return false;
        }

        await _logs.UpsertAsync(DayLogRules.Normalize(habit, new DayLog
        {
            HabitId = id,
            Day = day,
            Status = DayStatus.Skipped,
            UpdatedAt = DateTime.UtcNow
        }));
        return true;
    }

    private async Task SnoozeAsync(IReadOnlyList<Guid> ids, DateOnly day, ToastKind kind)
    {
        var habits = new List<Habit>();

        foreach (var id in ids)
        {
            if (await _habits.GetByIdAsync(id) is { DeletedAt: null } habit)
            {
                habits.Add(habit);
            }
        }

        if (Build(kind, habits, day) is { } content)
        {
            ToastNotificationManagerCompat.CreateToastNotifier().AddToSchedule(
                new ScheduledToastNotification(content.GetXml(), DateTimeOffset.Now.AddMinutes(30))
                {
                    Tag = "z" + DateTime.Now.ToString("ddHHmmss", CultureInfo.InvariantCulture),
                    Group = SnoozeGroup
                });
        }
    }

    /// <summary>Tiêu đề nhắc nhở theo loại; dùng chung cho toast Windows và thẻ nhắc trên viên thuốc.</summary>
    public static string TitleFor(ToastKind kind, IReadOnlyList<string> names) => kind switch
    {
        ToastKind.FollowUp => HostText.ReminderFollowUp,
        ToastKind.StreakRescue => HostText.ReminderRescue,
        ToastKind.MorningSummary => HostText.ReminderMorning,
        _ => names.Count == 1 ? HostText.ReminderSingle(names[0]) : HostText.ReminderMany(names.Count)
    };

    /// <summary>Nội dung toast: tiêu đề theo loại, tối đa 3 tên rồi "và N thói quen khác", nút hành động.</summary>
    public static ToastContentBuilder? Build(ToastKind kind, IReadOnlyList<Habit> habits, DateOnly day)
    {
        if (habits.Count == 0)
        {
            return null;
        }

        var ids = string.Join(',', habits.Select(h => h.Id));
        var dayText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var title = TitleFor(kind, habits.Select(h => h.Name).ToList());

        var lines = habits.Take(3).Select(h => "• " + h.Name).ToList();

        if (habits.Count > 3)
        {
            lines.Add(HostText.AndMore(habits.Count - 3));
        }

        var builder = new ToastContentBuilder()
            .AddArgument("action", "open")
            .AddText(title)
            .AddText(string.Join('\n', lines));

        if (kind == ToastKind.MorningSummary)
        {
            return builder.AddButton(new ToastButton().SetContent(HostText.Open).AddArgument("action", "open"));
        }

        builder.AddButton(new ToastButton()
            .SetContent(habits.Count == 1 ? HostText.Done : HostText.DoneAll)
            .AddArgument("action", "done").AddArgument("ids", ids).AddArgument("day", dayText)
            .SetBackgroundActivation());

        if (habits.Count == 1 && habits[0].Kind == HabitKind.Count)
        {
            builder.AddButton(new ToastButton()
                .SetContent("+1")
                .AddArgument("action", "plus").AddArgument("ids", ids).AddArgument("day", dayText)
                .SetBackgroundActivation());
        }

        builder.AddButton(new ToastButton()
            .SetContent(HostText.Snooze)
            .AddArgument("action", "snooze").AddArgument("ids", ids).AddArgument("day", dayText).AddArgument("kind", kind.ToString())
            .SetBackgroundActivation());

        if (habits.Count == 1)
        {
            builder.AddButton(new ToastButton()
                .SetContent(HostText.SkipToday)
                .AddArgument("action", "skip").AddArgument("ids", ids).AddArgument("day", dayText)
                .SetBackgroundActivation());
        }

        return builder;
    }
}
