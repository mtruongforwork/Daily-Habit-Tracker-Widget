using System.Security.Cryptography;
using System.Text;

namespace HabitGrid.Core.Domain.Widgets;

/// <summary>
/// Chữ ký cấu hình màn hình (tập màn hình + vị trí + độ phân giải). Vị trí widget được nhớ theo chữ ký này:
/// rút màn hình phụ rồi cắm lại thì widget về đúng chỗ cũ, thay vì chỗ vừa bị đẩy về màn hình chính.
/// </summary>
public static class ScreenLayout
{
    public readonly record struct Monitor(string Device, int Left, int Top, int Width, int Height);

    /// <summary>Không phụ thuộc thứ tự liệt kê; đổi độ phân giải hoặc số màn hình thì chữ ký đổi. 8 ký tự hex.</summary>
    public static string Signature(IEnumerable<Monitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        var text = string.Join(
            ";",
            monitors.OrderBy(m => m.Device, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Left).ThenBy(m => m.Top)
                .Select(m => $"{m.Device.ToLowerInvariant()}:{m.Left},{m.Top},{m.Width},{m.Height}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();
    }

    /// <summary>Khóa lưu vị trí theo cấu hình màn hình hiện tại.</summary>
    public static string KeyFor(string baseKey, string signature) => $"{baseKey}@{signature}";
}
