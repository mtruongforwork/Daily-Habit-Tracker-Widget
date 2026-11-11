using HabitGrid.Data;
using HabitGrid.Data.V3;

namespace HabitGrid.WinUI;

/// <summary>
/// Xóa toàn bộ dữ liệu người dùng trên máy: dòng trong CSDL (rồi VACUUM), ảnh khoảnh khắc, bản sao lưu và nhật ký.
/// Tách khỏi giao diện để smoke test chạy đúng luồng thật trên thư mục dữ liệu tạm.
/// </summary>
public static class AppDataEraser
{
    /// <summary>Trả về số file đã xóa. File đang bị giữ (ví dụ nhật ký của lần chạy này) được bỏ qua, không làm hỏng cả quá trình.</summary>
    public static async Task<int> EraseAsync(HabitGridDatabase database, string dataDir, string backupsDir, string logDir, CancellationToken ct = default)
    {
        await DataEraser.EraseAllAsync(database, ct);

        var deleted = 0;

        foreach (var folder in new[] { Path.Combine(dataDir, "moments"), backupsDir, logDir })
        {
            deleted += DeleteFolderContents(folder);
        }

        return deleted;
    }

    private static int DeleteFolderContents(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var deleted = 0;

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (IOException)
            {
                // Đang được dùng: để lại, không chặn các file còn lại.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }
}
