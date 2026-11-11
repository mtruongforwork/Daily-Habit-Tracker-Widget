using System.Text.Json;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Focus;
using HabitGrid.Core.Services;

namespace HabitGrid.Presentation.Services;

/// <summary>Lệnh điều khiển phiên Focus. Phiên chạy trong WidgetHost (sống qua việc đóng app chính), app chỉ gửi lệnh.</summary>
public interface IFocusCommands
{
    Task StartAsync(Guid? habitId, int workMinutes, FocusMode mode, int breakMinutes);

    Task ToggleAsync();

    Task StopAsync();
}

/// <summary>Ảnh chụp hiển thị của phiên Focus tại một thời điểm.</summary>
public sealed record FocusDisplay(FocusPhase Phase, FocusMode Mode, TimeSpan Elapsed, TimeSpan? Remaining, double Fraction, bool Running, Guid? HabitId)
{
    public bool IsActive => Phase is FocusPhase.Work or FocusPhase.Break;

    public bool IsPaused => Phase == FocusPhase.Work && !Running;

    public static FocusDisplay Idle { get; } = new(FocusPhase.Idle, FocusMode.Pomodoro, TimeSpan.Zero, null, 0, false, null);
}

/// <summary>Lựa chọn Focus gần nhất (đọc từ cài đặt chung với widget để hai nơi cùng mặc định).</summary>
public sealed record FocusChoice(FocusMode Mode, int WorkMinutes, int BreakMinutes, Guid? HabitId)
{
    public static FocusChoice Default { get; } = new(FocusMode.Pomodoro, 25, 5, null);
}

/// <summary>
/// Dữ liệu và thao tác cho trang Focus trong app: hiển thị phiên đang chạy (đọc trạng thái do WidgetHost lưu, tính lại theo giờ hiện tại),
/// chọn chế độ/thời lượng/thói quen để bắt đầu, và thống kê từ lịch sử phiên. Không phụ thuộc giao diện.
/// </summary>
public sealed class FocusPageModel
{
    private const string StateKey = "focus.state";
    private const string PrefsKey = "focus.prefs";

    /// <summary>Sau khi gửi lệnh bắt đầu, WidgetHost có thể mất vài giây mới ghi trạng thái; trong lúc đó dùng bản đồng hồ cục bộ.</summary>
    private static readonly TimeSpan OptimisticWindow = TimeSpan.FromSeconds(6);

    private readonly FocusService _service;
    private readonly ISettingsStore _settings;
    private readonly IDayProvider _days;
    private readonly IFocusCommands _commands;
    private FocusTimer? _optimistic;
    private DateTime _optimisticSince;

    public FocusPageModel(FocusService service, ISettingsStore settings, IDayProvider days, IFocusCommands commands)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _days = days ?? throw new ArgumentNullException(nameof(days));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public IReadOnlyList<Habit> Candidates { get; private set; } = [];

    public FocusStatsData Stats { get; private set; } = FocusStats.Compute([], DateOnly.FromDateTime(DateTime.Today));

    /// <summary>Phiên gần đây, mới nhất trước.</summary>
    public IReadOnlyList<FocusRecord> Recent { get; private set; } = [];

    public FocusChoice Choice { get; private set; } = FocusChoice.Default;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        Candidates = await _service.CandidatesAsync(ct);
        var history = await _service.HistoryAsync(ct);
        Stats = FocusStats.Compute(history, _days.GetToday());
        Recent = history.OrderByDescending(r => r.EndedAtUtc).Take(8).ToList();
        Choice = ParseChoice(await _settings.GetAsync(PrefsKey));
    }

    /// <summary>Hiển thị của phiên tại <paramref name="nowUtc"/>: lấy từ trạng thái WidgetHost đã lưu; chưa có thì dùng đồng hồ cục bộ vừa bắt đầu.</summary>
    public async Task<FocusDisplay> DisplayAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var timer = ParseState(await _settings.GetAsync(StateKey)) is { Phase: FocusPhase.Work or FocusPhase.Break } state
            ? FocusTimer.Restore(state)
            : null;

        if (timer is null && _optimistic is not null && nowUtc - _optimisticSince < OptimisticWindow)
        {
            timer = _optimistic;
        }
        else if (timer is not null)
        {
            _optimistic = null;
        }

        return timer is null ? FocusDisplay.Idle : Describe(timer, nowUtc);
    }

    public static FocusDisplay Describe(FocusTimer timer, DateTime nowUtc)
    {
        var state = timer.State;
        var elapsed = timer.Elapsed(nowUtc);
        var remaining = timer.Remaining(nowUtc);

        var fraction = state.Phase switch
        {
            FocusPhase.Work when state.Mode != FocusMode.Stopwatch && state.WorkSeconds > 0 => Math.Clamp(elapsed.TotalSeconds / state.WorkSeconds, 0, 1),
            FocusPhase.Break when state.BreakSeconds > 0 && remaining is { } left => Math.Clamp(1 - (left.TotalSeconds / state.BreakSeconds), 0, 1),
            _ => 0
        };

        return new FocusDisplay(state.Phase, state.Mode, elapsed, remaining, fraction, timer.IsRunning, state.HabitId);
    }

    public async Task StartAsync(FocusChoice choice, DateTime nowUtc)
    {
        var work = Math.Clamp(choice.WorkMinutes, 1, 600);
        var rest = Math.Clamp(choice.BreakMinutes, 0, 120);
        Choice = choice with { WorkMinutes = work, BreakMinutes = rest };

        _optimistic = new FocusTimer(choice.Mode, TimeSpan.FromMinutes(work), TimeSpan.FromMinutes(rest), choice.HabitId);
        _optimistic.Start(nowUtc);
        _optimisticSince = nowUtc;

        await _commands.StartAsync(choice.HabitId, work, choice.Mode, rest);
    }

    public async Task ToggleAsync(DateTime nowUtc)
    {
        _optimistic?.Toggle(nowUtc);
        await _commands.ToggleAsync();
    }

    public async Task StopAsync()
    {
        _optimistic = null;
        await _commands.StopAsync();
    }

    public static FocusChoice ParseChoice(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return FocusChoice.Default;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var mode = root.TryGetProperty("Mode", out var m) && m.TryGetInt32(out var mi) && Enum.IsDefined((FocusMode)mi) ? (FocusMode)mi : FocusMode.Pomodoro;
            var work = root.TryGetProperty("WorkMinutes", out var w) && w.TryGetInt32(out var wi) && wi is >= 1 and <= 600 ? wi : 25;
            var rest = root.TryGetProperty("BreakMinutes", out var b) && b.TryGetInt32(out var bi) && bi is >= 0 and <= 120 ? bi : 5;
            Guid? habit = root.TryGetProperty("HabitId", out var h) && h.ValueKind == JsonValueKind.String && Guid.TryParse(h.GetString(), out var g) ? g : null;
            return new FocusChoice(mode, work, rest, habit);
        }
        catch (JsonException)
        {
            return FocusChoice.Default;
        }
    }

    private static FocusState? ParseState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FocusState>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
