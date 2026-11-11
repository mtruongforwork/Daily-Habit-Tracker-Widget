using System.IO;
using System.Windows.Media.Imaging;
using HabitGrid.Presentation.ViewModels;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Hình màu của Buddy và phụ kiện cho widget WPF (WPF chỉ vẽ emoji đơn sắc). Hình lấy từ Microsoft Fluent Emoji (MIT, 3D, 256 px),
/// nằm trong <c>Assets/Buddy</c> dưới dạng Resource; thiếu hình thì widget quay về emoji đơn sắc. Có tải lại bằng <c>build/fetch-buddy-sprites.ps1</c>.
/// </summary>
public static class BuddySprites
{
    private static readonly Dictionary<string, BitmapImage?> Cache = [];

    public static string StageId(BuddyStage stage) => "stage-" + stage.ToString().ToLowerInvariant();

    /// <summary>Hình đã nạp (đóng băng, dùng chung giữa luồng); null nếu không có hình đó.</summary>
    public static BitmapImage? TryLoad(string id)
    {
        if (Cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        BitmapImage? image = null;

        try
        {
            image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Assets/Buddy/{id}.png", UriKind.Absolute);
            image.DecodePixelWidth = 160;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException or InvalidOperationException)
        {
            image = null;
        }

        Cache[id] = image;
        return image;
    }
}
