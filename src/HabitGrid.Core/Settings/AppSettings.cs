using System.Globalization;
using HabitGrid.Core.Repositories;

namespace HabitGrid.Core.Settings;

/// <summary>
/// Toán tử mở rộng cho ngôn ngữ ứng dụng.
/// </summary>
public static class AppLanguageExtensions
{
    /// <summary>Chuyển ngôn ngữ sang CultureInfo tương ứng.</summary>
    public static CultureInfo ToCultureInfo(this AppLanguage language) => language switch
    {
        AppLanguage.English => new CultureInfo("en-US"),
        AppLanguage.Japanese => new CultureInfo("ja-JP"),
        AppLanguage.Korean => new CultureInfo("ko-KR"),
        _ => new CultureInfo("vi-VN")
    };

    /// <summary>Mã ngôn ngữ dạng chuẩn, ví dụ "vi-VN".</summary>
    public static string ToCultureName(this AppLanguage language) =>
        language.ToCultureInfo().Name;
}

/// <summary>
/// Toàn bộ cài đặt của ứng dụng.
/// <para>
/// Mọi thay đổi đều được ghi ngay xuống CSDL (không có nút "Lưu") và phát tín hiệu
/// <see cref="SettingChanged"/> để các cửa sổ/widget đang mở cập nhật tức thì.
/// </para>
/// </summary>
public sealed class AppSettings
{
    private readonly ISettingsRepository _repository;
    private readonly List<Task> _pendingWrites = new();

    private AppThemeMode _theme = AppThemeMode.System;
    private AppLanguage _language = AppLanguage.Vietnamese;
    private DayOfWeek _weekStart = DayOfWeek.Monday;
    private int _dayStartHour;
    private bool _runAtStartup;
    private int _pinningMode;
    private int _defaultSize = 1;
    private bool _editMode;
    private WidgetBackgroundStyle _backgroundStyle = WidgetBackgroundStyle.Acrylic;
    private double _backgroundOpacity = 85;
    private double _cornerRadius = 12;

    public AppSettings(ISettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// Tạo và nạp cài đặt từ CSDL.
    /// <para>
    /// Phải là hàm bất đồng bộ: việc đọc CSDL dùng API async của SQLite, nếu chặn luồng UI
    /// bằng <c>GetAwaiter().GetResult()</c> sẽ gây deadlock (tiếp tục của tác vụ async cần
    /// chính luồng UI để chạy).
    /// </para>
    /// </summary>
    public static async Task<AppSettings> CreateAsync(ISettingsRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var settings = new AppSettings(repository);
        await settings.LoadAsync().ConfigureAwait(false);

        return settings;
    }

    /// <summary>Được gọi khi một cài đặt thay đổi (tham số là khóa của cài đặt).</summary>
    public event EventHandler<string>? SettingChanged;

    /// <summary>Chế độ màu: theo hệ thống / sáng / tối.</summary>
    public AppThemeMode Theme
    {
        get => _theme;
        set => Set(ref _theme, value, SettingsKeys.Theme);
    }

    /// <summary>Ngôn ngữ giao diện.</summary>
    public AppLanguage Language
    {
        get => _language;
        set => Set(ref _language, value, SettingsKeys.Language);
    }

    /// <summary>Ngày bắt đầu tuần (mặc định ISO là thứ Hai).</summary>
    public DayOfWeek WeekStart
    {
        get => _weekStart;
        set => Set(ref _weekStart, value, SettingsKeys.WeekStart);
    }

    /// <summary>Mốc chuyển ngày mới (0 – 5 giờ).</summary>
    public int DayStartHour
    {
        get => _dayStartHour;
        set => Set(ref _dayStartHour, Math.Clamp(value, 0, 5), SettingsKeys.DayStartHour);
    }

    /// <summary>True nếu ứng dụng tự chạy khi khởi động Windows.</summary>
    public bool RunAtStartup
    {
        get => _runAtStartup;
        set => Set(ref _runAtStartup, value, SettingsKeys.RunAtStartup);
    }

    /// <summary>Chế độ ghim widget (0 = Desktop-level, 1 = Bottom-most, 2 = Always-on-top).</summary>
    public int PinningMode
    {
        get => _pinningMode;
        set => Set(ref _pinningMode, Math.Clamp(value, 0, 2), SettingsKeys.PinnedMode);
    }

    /// <summary>Cỡ widget mặc định (0 = Nhỏ, 1 = Vừa, 2 = Lớn).</summary>
    public int DefaultWidgetSize
    {
        get => _defaultSize;
        set => Set(ref _defaultSize, Math.Clamp(value, 0, 2), SettingsKeys.DefaultSize);
    }

    /// <summary>True nếu đang bật chế độ chỉnh sửa widget.</summary>
    public bool EditMode
    {
        get => _editMode;
        set => Set(ref _editMode, value, SettingsKeys.EditMode);
    }

    /// <summary>Kiểu nền widget.</summary>
    public WidgetBackgroundStyle BackgroundStyle
    {
        get => _backgroundStyle;
        set => Set(ref _backgroundStyle, value, SettingsKeys.BackgroundStyle);
    }

    /// <summary>Độ mờ nền widget (0 – 100).</summary>
    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set => Set(ref _backgroundOpacity, Math.Clamp(value, 0, 100), SettingsKeys.BackgroundOpacity);
    }

    /// <summary>Bo góc widget (4 – 16 px).</summary>
    public double CornerRadius
    {
        get => _cornerRadius;
        set => Set(ref _cornerRadius, Math.Clamp(value, 4, 16), SettingsKeys.CornerRadius);
    }

    /// <summary>Đợi mọi thao tác ghi đang chạy xong (dùng trong kiểm thử).</summary>
    public async Task FlushAsync()
    {
        Task[] pending;

        lock (_pendingWrites)
        {
            pending = _pendingWrites.ToArray();
        }

        foreach (var task in pending)
        {
            await task.ConfigureAwait(false);
        }
    }

    /// <summary>Nạp toàn bộ giá trị cài đặt từ CSDL.</summary>
    public async Task LoadAsync()
    {
        _theme = await ReadEnumAsync(SettingsKeys.Theme, AppThemeMode.System).ConfigureAwait(false);
        _language = await ReadEnumAsync(SettingsKeys.Language, AppLanguage.Vietnamese).ConfigureAwait(false);
        _weekStart = await ReadEnumAsync(SettingsKeys.WeekStart, DayOfWeek.Monday).ConfigureAwait(false);
        _dayStartHour = await ReadIntAsync(SettingsKeys.DayStartHour, 0).ConfigureAwait(false);
        _runAtStartup = await ReadBoolAsync(SettingsKeys.RunAtStartup, false).ConfigureAwait(false);
        _pinningMode = await ReadIntAsync(SettingsKeys.PinnedMode, 0).ConfigureAwait(false);
        _defaultSize = await ReadIntAsync(SettingsKeys.DefaultSize, 1).ConfigureAwait(false);
        _editMode = await ReadBoolAsync(SettingsKeys.EditMode, false).ConfigureAwait(false);
        _backgroundStyle = await ReadEnumAsync(SettingsKeys.BackgroundStyle, WidgetBackgroundStyle.Acrylic).ConfigureAwait(false);
        _backgroundOpacity = await ReadDoubleAsync(SettingsKeys.BackgroundOpacity, 85).ConfigureAwait(false);
        _cornerRadius = await ReadDoubleAsync(SettingsKeys.CornerRadius, 12).ConfigureAwait(false);
    }

    private async Task<T> ReadEnumAsync<T>(string key, T fallback) where T : struct, Enum
    {
        var raw = await _repository.GetAsync(key, string.Empty).ConfigureAwait(false);

        return Enum.TryParse<T>(raw, ignoreCase: true, out var value) ? value : fallback;
    }

    private async Task<int> ReadIntAsync(string key, int fallback)
    {
        var raw = await _repository.GetAsync(key, string.Empty).ConfigureAwait(false);

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private async Task<double> ReadDoubleAsync(string key, double fallback)
    {
        var raw = await _repository.GetAsync(key, string.Empty).ConfigureAwait(false);

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private async Task<bool> ReadBoolAsync(string key, bool fallback)
    {
        var raw = await _repository.GetAsync(key, string.Empty).ConfigureAwait(false);

        return bool.TryParse(raw, out var value) ? value : fallback;
    }

    private void Set<T>(ref T field, T value, string key)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;

        var write = _repository.SetAsync(key, value);

        lock (_pendingWrites)
        {
            _pendingWrites.Add(write);
        }

        SettingChanged?.Invoke(this, key);
    }
}
