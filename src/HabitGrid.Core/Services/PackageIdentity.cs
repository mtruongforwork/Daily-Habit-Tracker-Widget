using System.Runtime.InteropServices;
using System.Text;

namespace HabitGrid.Core.Services;

/// <summary>
/// Tiến trình có danh tính gói MSIX hay không. Khi có: ghi vào %LocalAppData% bị ảo hóa theo gói (mọi tiến trình cùng gói thấy chung một vùng),
/// khởi động cùng Windows phải dùng StartupTask thay vì khóa Run, và tiến trình phải được mở qua alias/AUMID để giữ danh tính.
/// </summary>
public static class PackageIdentity
{
    private const int ErrorNoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);

    /// <summary>Nhẹ và không ném lỗi (khác <c>Package.Current</c>), dùng được ở mọi nơi kể cả trước khi giao diện dựng.</summary>
    public static bool IsPackaged
    {
        get
        {
            var length = 0;

            try
            {
                return GetCurrentPackageFullName(ref length, null) != ErrorNoPackage;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>Tên đầy đủ của gói (để log/chẩn đoán), null nếu không đóng gói.</summary>
    public static string? FullName
    {
        get
        {
            var length = 0;

            if (GetCurrentPackageFullName(ref length, null) == ErrorNoPackage)
            {
                return null;
            }

            var name = new StringBuilder(length);
            return GetCurrentPackageFullName(ref length, name) == 0 ? name.ToString() : null;
        }
    }
}
