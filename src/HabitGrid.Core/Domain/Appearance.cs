using System.Globalization;

namespace HabitGrid.Core.Domain;

/// <summary>Màu nhấn tùy chỉnh (P5, Plus): kiểm tra mã hex và sinh màu cuối của dải gradient từ màu đầu.</summary>
public static class AccentColors
{
    public const string SettingKey = "appearance.accent";

    /// <summary>Các màu gợi ý (không phải màu của theme nào để người dùng thấy khác biệt rõ).</summary>
    public static readonly IReadOnlyList<string> Presets =
    [
        "#FF5C8A", "#FF8A4C", "#F5B800", "#22C55E", "#14B8A6", "#38BDF8", "#6366F1", "#A855F7"
    ];

    /// <summary>Chuẩn hóa "#RRGGBB" (chấp nhận thiếu dấu # và chữ thường); null nếu không hợp lệ.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var hex = input.Trim().TrimStart('#');

        return hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? "#" + hex.ToUpperInvariant()
            : null;
    }

    /// <summary>Màu cuối của gradient: xoay sắc độ +35° để dải có chiều sâu nhưng vẫn cùng tông.</summary>
    public static string DeriveEnd(string startHex)
    {
        var normalized = Normalize(startHex) ?? throw new ArgumentException("Mã màu không hợp lệ.", nameof(startHex));
        var r = Convert.ToInt32(normalized.Substring(1, 2), 16) / 255.0;
        var g = Convert.ToInt32(normalized.Substring(3, 2), 16) / 255.0;
        var b = Convert.ToInt32(normalized.Substring(5, 2), 16) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        double h = 0, s = 0;

        if (max != min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? ((g - b) / d + (g < b ? 6 : 0)) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
        }

        h = (h + 35) % 360;
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        double Channel(double t)
        {
            t = (t % 1 + 1) % 1;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }

        var hk = h / 360;
        var (nr, ng, nb) = s == 0 ? (l, l, l) : (Channel(hk + 1.0 / 3), Channel(hk), Channel(hk - 1.0 / 3));
        return $"#{(int)Math.Round(nr * 255):X2}{(int)Math.Round(ng * 255):X2}{(int)Math.Round(nb * 255):X2}";
    }
}

/// <summary>Bộ phông chữ gợi ý (P5, Plus). Mặc định là Be Vietnam Pro đóng gói sẵn; các bộ khác dùng phông có sẵn trong Windows và đều hỗ trợ tiếng Việt.</summary>
public sealed record FontChoice(string Id, string? Family);

public static class AppFonts
{
    public const string DefaultId = "default";
    public const string SettingKey = "appearance.font";

    /// <summary>Tên file nhỏ trong thư mục dữ liệu: phông phải biết trước khi giao diện được dựng nên không đọc từ CSDL được.</summary>
    public const string FileName = "appearance-font.txt";

    public static readonly IReadOnlyList<FontChoice> All =
    [
        new(DefaultId, null),
        new("system", "Segoe UI Variable"),
        new("serif", "Cambria"),
        new("mono", "Consolas")
    ];

    /// <summary>Id hợp lệ, không thì về mặc định (file hỏng hoặc phông bị gỡ khỏi danh sách).</summary>
    public static FontChoice Resolve(string? id) =>
        All.FirstOrDefault(f => string.Equals(f.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

/// <summary>
/// Hiệu ứng kính mờ (Acrylic) của cửa sổ chính. Cường độ 0–100: 0 = nền đặc; càng cao nền càng trong và hình nền Windows phía sau càng lộ.
/// Hàm thuần nên kiểm thử được; việc áp lên cửa sổ nằm ở WinUI.
/// </summary>
public static class GlassStyle
{
    public const string SettingKey = "appearance.glass";
    public const int Default = 65;

    /// <summary>Độ đục thấp nhất của nền khi cường độ 100 (tối: 96/255, sáng: 130/255 để chữ tối vẫn đọc được).</summary>
    private const int MinAlphaDark = 96;
    private const int MinAlphaLight = 130;

    public static int Clamp(int strength) => Math.Clamp(strength, 0, 100);

    /// <summary>Đọc cường độ đã lưu; không có hoặc sai định dạng thì dùng mặc định.</summary>
    public static int Parse(string? stored) =>
        int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Clamp(value) : Default;

    /// <summary>Độ đục (0–255) của lớp nền canvas phủ lên kính ở một cường độ.</summary>
    public static byte CanvasAlpha(bool isDark, int strength)
    {
        var min = isDark ? MinAlphaDark : MinAlphaLight;
        return (byte)Math.Round(255 - (255 - min) * Clamp(strength) / 100.0);
    }
}
