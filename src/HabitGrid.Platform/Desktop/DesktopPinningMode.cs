namespace HabitGrid.Platform.Desktop;

/// <summary>
/// Chế độ ghim cửa sổ widget lên desktop.
/// </summary>
public enum DesktopPinningMode
{
    /// <summary>Chế độ 1: nằm trên hình nền, dưới mọi cửa sổ (WorkerW).</summary>
    DesktopLevel = 0,

    /// <summary>Chế độ 2: cửa sổ trôi xuống đáy z-order (dự phòng ổn định).</summary>
    BottomMost = 1,

    /// <summary>Chế độ 3: luôn nằm trên cùng.</summary>
    AlwaysOnTop = 2,

    /// <summary>
    /// Chế độ 4: cửa sổ cấp cao đặt NGAY TRÊN lớp desktop (Progman) trong z-order, không đổi cha. Dùng khi ghim làm con của Progman
    /// không hiện hình (gặp trên một số bản Windows 11 mới); vẫn nằm dưới mọi cửa sổ ứng dụng.
    /// </summary>
    AboveDesktop = 3
}
