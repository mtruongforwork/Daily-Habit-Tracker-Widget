using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.WinUI.Notifications;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Focus;
using HabitGrid.Presentation.Services;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Điều khiển Focus trong WidgetHost (SPEC-V2-09 §2): giữ <see cref="FocusTimer"/>, lưu trạng thái để sống qua khởi động lại,
/// tự ghi phiên khi hết giờ (cộng phút vào thói quen Thời lượng), tạm dừng khi máy ngủ. Phiên tiếp tục kể cả khi đóng app chính.
/// </summary>
public sealed class FocusController : IDisposable
{
    private const string StateKey = "focus.state";
    private const string PrefsKey = "focus.prefs";

    private readonly FocusService _service;
    private readonly ISettingsStore _settings;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private FocusTimer? _timer;

    public FocusController(FocusService service, ISettingsStore settings)
    {
        _service = service;
        _settings = settings;
        _tick.Tick += async (_, _) => await TickAsync(DateTime.UtcNow);
    }

    /// <summary>Có thay đổi hiển thị (mỗi giây khi chạy, khi đổi pha).</summary>
    public event EventHandler? Changed;

    /// <summary>Một phiên vừa được ghi (để làm mới widget và báo người dùng).</summary>
    public event EventHandler<FocusRecord>? Completed;

    public FocusTimer? Timer => _timer;

    public bool PausedBySleep { get; private set; }

    public FocusPrefs Prefs { get; private set; } = new();

    public async Task LoadAsync()
    {
        Prefs = Deserialize<FocusPrefs>(await _settings.GetAsync(PrefsKey)) ?? new FocusPrefs();

        if (Deserialize<FocusState>(await _settings.GetAsync(StateKey)) is { } state && state.Phase is FocusPhase.Work or FocusPhase.Break)
        {
            _timer = FocusTimer.Restore(state);
            await TickAsync(DateTime.UtcNow);
            _tick.Start();
        }
    }

    public async Task SavePrefsAsync(FocusPrefs prefs)
    {
        Prefs = prefs;
        await _settings.SetAsync(PrefsKey, JsonSerializer.Serialize(prefs));
    }

    /// <summary>Bắt đầu phiên mới theo lựa chọn đã lưu (hoặc tham số).</summary>
    public async Task StartAsync(FocusMode? mode = null, int? minutes = null, Guid? habitId = null, bool keepHabit = true, int? breakMinutes = null)
    {
        var prefs = Prefs with
        {
            Mode = mode ?? Prefs.Mode,
            WorkMinutes = minutes ?? Prefs.WorkMinutes,
            BreakMinutes = breakMinutes ?? Prefs.BreakMinutes,
            HabitId = keepHabit ? habitId ?? Prefs.HabitId : habitId
        };
        await SavePrefsAsync(prefs);

        _timer = new FocusTimer(prefs.Mode, TimeSpan.FromMinutes(Math.Max(1, prefs.WorkMinutes)), TimeSpan.FromMinutes(prefs.BreakMinutes), prefs.HabitId);
        _timer.Start(DateTime.UtcNow);
        PausedBySleep = false;
        _tick.Start();
        await PersistAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Phím tắt Win+Alt+F: chưa có phiên thì bắt đầu, đang chạy thì tạm dừng, đang dừng thì chạy tiếp.</summary>
    public async Task ToggleAsync()
    {
        if (_timer is null || _timer.Phase is FocusPhase.Idle or FocusPhase.Finished or FocusPhase.Break)
        {
            await StartAsync();
            return;
        }

        _timer.Toggle(DateTime.UtcNow);
        PausedBySleep = false;
        await PersistAsync();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync()
    {
        if (_timer is null)
        {
            return;
        }

        var result = _timer.Stop(DateTime.UtcNow);
        _tick.Stop();
        await PersistAsync();

        if (result is not null)
        {
            await RecordAsync(result);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Máy sắp ngủ: tạm dừng để không tính thời gian ngủ là thời gian tập trung.</summary>
    public async Task OnSuspendAsync()
    {
        if (_timer is { IsRunning: true })
        {
            _timer.Pause(DateTime.UtcNow);
            PausedBySleep = true;
            await PersistAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => _tick.Stop();

    /// <summary>Dùng cho tự kiểm tra và nhịp đồng hồ: đưa đồng hồ tới thời điểm cho trước.</summary>
    public async Task TickAsync(DateTime nowUtc)
    {
        if (_timer is null)
        {
            return;
        }

        var phaseBefore = _timer.Phase;
        var result = _timer.Tick(nowUtc);

        if (result is not null)
        {
            await RecordAsync(result);
        }

        if (result is not null || phaseBefore != _timer.Phase)
        {
            await PersistAsync();
        }

        if (_timer.Phase is FocusPhase.Idle or FocusPhase.Finished)
        {
            _tick.Stop();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RecordAsync(FocusResult result)
    {
        if (await _service.CompleteAsync(result) is { } record)
        {
            Completed?.Invoke(this, record);

            try
            {
                new ToastContentBuilder().AddText(HostText.FocusDone(record.Minutes)).Show();
            }
            catch (Exception)
            {
                // Không hiện được toast thì thôi; phiên đã được ghi.
            }
        }
    }

    private Task PersistAsync() =>
        _settings.SetAsync(StateKey, _timer is null ? string.Empty : JsonSerializer.Serialize(_timer.State));

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Lựa chọn Focus nhớ giữa các phiên.</summary>
public sealed record FocusPrefs(FocusMode Mode = FocusMode.Pomodoro, int WorkMinutes = 25, int BreakMinutes = 5, Guid? HabitId = null);
