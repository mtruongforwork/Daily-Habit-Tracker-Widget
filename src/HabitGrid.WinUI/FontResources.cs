using HabitGrid.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace HabitGrid.WinUI;

/// <summary>
/// Từ điển phông chữ của app (P5). Được App.xaml dựng trước Tokens.xaml nên các style dùng <c>{StaticResource DisplayFont}</c> nhận đúng phông ngay từ đầu;
/// đổi phông vì thế có hiệu lực ở lần mở app kế tiếp. Lựa chọn đọc từ file nhỏ trong thư mục dữ liệu (CSDL chưa mở ở thời điểm này).
/// </summary>
public sealed class FontResources : ResourceDictionary
{
    private const string CjkFallback = ", Segoe UI, Yu Gothic UI, Malgun Gothic, Microsoft YaHei UI";
    private const string BundledDisplay = "ms-appx:///Assets/Fonts/BeVietnamPro-ExtraBold.ttf#Be Vietnam Pro" + CjkFallback;
    private const string BundledBold = "ms-appx:///Assets/Fonts/BeVietnamPro-Bold.ttf#Be Vietnam Pro" + CjkFallback;
    private const string BundledSemiBold = "ms-appx:///Assets/Fonts/BeVietnamPro-SemiBold.ttf#Be Vietnam Pro" + CjkFallback;

    public FontResources()
    {
        var family = AppFonts.Resolve(ReadChoice()).Family;
        var withFallback = family is null ? null : family + CjkFallback;
        this["DisplayFont"] = new FontFamily(withFallback ?? BundledDisplay);
        this["DisplayBoldFont"] = new FontFamily(withFallback ?? BundledBold);
        this["DisplaySemiBoldFont"] = new FontFamily(withFallback ?? BundledSemiBold);
    }

    public static string FilePath => Path.Combine(AppPaths.DataDir, AppFonts.FileName);

    /// <summary>Ghi lựa chọn cho lần khởi động sau; chọn mặc định thì xóa file.</summary>
    public static void WriteChoice(string id)
    {
        try
        {
            if (AppFonts.Resolve(id).Family is null)
            {
                File.Delete(FilePath);
                return;
            }

            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(FilePath, AppFonts.Resolve(id).Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Không ghi được thì giữ phông cũ; không được làm app lỗi vì chuyện thẩm mỹ.
        }
    }

    public static string ReadChoice()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : AppFonts.DefaultId;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppFonts.DefaultId;
        }
    }
}
