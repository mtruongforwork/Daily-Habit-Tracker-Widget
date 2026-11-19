using System.ComponentModel;
using System.Globalization;
using System.Resources;
using HabitGrid.Core.Settings;

namespace HabitGrid.UI.Localization;

/// <summary>
/// Quản lý ngôn ngữ hiện tại và tra cứu chuỗi từ tài nguyên <c>.resx</c>.
/// <para>
/// Khi đổi ngôn ngữ, mọi binding dùng markup extension <c>{loc:Translate ...}</c>
/// được cập nhật ngay lập tức mà không cần khởi động lại ứng dụng.
/// </para>
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly ResourceManager ResourceManagerInstance =
        new("HabitGrid.UI.Resources.Resources", typeof(LocalizationManager).Assembly);

    private CultureInfo _culture = new("vi-VN");

    private LocalizationManager()
    {
    }

    /// <summary>Quản lý ngôn ngữ dùng chung cho toàn ứng dụng.</summary>
    public static LocalizationManager Instance { get; } = new();

    /// <summary>Được gọi khi ngôn ngữ thay đổi.</summary>
    public event EventHandler? LanguageChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Culture hiện tại.</summary>
    public CultureInfo Culture => _culture;

    /// <summary>Đặt ngôn ngữ hiện tại và thông báo cho toàn bộ binding cập nhật.</summary>
    public void SetLanguage(AppLanguage language)
    {
        var culture = language.ToCultureInfo();

        if (string.Equals(culture.Name, _culture.Name, StringComparison.Ordinal))
        {
            return;
        }

        _culture = culture;

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        // "Item[]" báo cho mọi binding theo chỉ mục cập nhật giá trị.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Đặt ngôn ngữ theo mã culture (ví dụ "vi-VN").</summary>
    public void SetLanguage(string cultureName)
    {
        var language = cultureName switch
        {
            _ when cultureName.StartsWith("vi", StringComparison.OrdinalIgnoreCase) => AppLanguage.Vietnamese,
            _ when cultureName.StartsWith("ja", StringComparison.OrdinalIgnoreCase) => AppLanguage.Japanese,
            _ when cultureName.StartsWith("ko", StringComparison.OrdinalIgnoreCase) => AppLanguage.Korean,
            _ => AppLanguage.English
        };

        SetLanguage(language);
    }

    /// <summary>Lấy chuỗi theo khóa. Trả về chính khóa nếu không tìm thấy.</summary>
    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        try
        {
            return ResourceManagerInstance.GetString(key, _culture) ?? key;
        }
        catch (MissingManifestResourceException)
        {
            return key;
        }
    }

    /// <summary>Lấy chuỗi có tham số thay thế, ví dụ "Đã xóa thói quen “{0}”".</summary>
    public string Format(string key, params object?[] args)
    {
        var format = Get(key);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    /// <summary>Chỉ số để binding theo khóa.</summary>
    public string this[string key] => Get(key);

    /// <summary>Lấy chuỗi theo ngôn ngữ hiện tại.</summary>
    public static string Translate(string key) => Instance.Get(key);

    /// <summary>Lấy chuỗi có tham số theo ngôn ngữ hiện tại.</summary>
    public static string TranslateFormat(string key, params object?[] args) => Instance.Format(key, args);
}
