using System.Globalization;
using System.Resources;

namespace HabitGrid.Presentation.Services;

/// <summary>Tra cứu chuỗi đa ngôn ngữ (VI mặc định, EN, JA, KO). Đổi ngôn ngữ áp dụng ngay, không cần khởi động lại.</summary>
public interface ILocalizer
{
    string Language { get; }

    string this[string key] { get; }

    string Format(string key, params object?[] args);

    void SetLanguage(string language);

    event EventHandler? LanguageChanged;
}

public sealed class Localizer : ILocalizer
{
    private static readonly ResourceManager Resources =
        new("HabitGrid.Presentation.Resources.Strings", typeof(Localizer).Assembly);

    private CultureInfo _culture = CultureInfo.GetCultureInfo("vi");

    public event EventHandler? LanguageChanged;

    public string Language => _culture.TwoLetterISOLanguageName;

    public string this[string key]
    {
        get
        {
            var value = Resources.GetString(key, _culture);

            // Thiếu khóa thì trả về chính khóa để lỗi lộ ra trong UI thay vì im lặng.
            return value ?? $"[{key}]";
        }
    }

    public string Format(string key, params object?[] args) =>
        string.Format(_culture, this[key], args);

    public void SetLanguage(string language)
    {
        var code = (language ?? string.Empty).Trim().ToLowerInvariant();
        var culture = code switch
        {
            _ when code.StartsWith("en") => CultureInfo.GetCultureInfo("en"),
            _ when code.StartsWith("ja") => CultureInfo.GetCultureInfo("ja"),
            _ when code.StartsWith("ko") => CultureInfo.GetCultureInfo("ko"),
            _ => CultureInfo.GetCultureInfo("vi")
        };

        if (Equals(culture, _culture))
        {
            return;
        }

        _culture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
