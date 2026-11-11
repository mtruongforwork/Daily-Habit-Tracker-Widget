namespace HabitGrid.Core.Domain.Widgets;

/// <summary>Kiểu ghim widget lên màn hình (Cài đặt → Widget).</summary>
public enum PinChoice
{
    /// <summary>Luôn nằm trên các cửa sổ khác. Mặc định: chắc chắn hiện hình trên mọi bản Windows (widget nhỏ nên ít phiền).</summary>
    Top,

    /// <summary>Cửa sổ cấp cao ngay trên lớp desktop, dưới mọi cửa sổ ứng dụng.</summary>
    AboveDesktop,

    /// <summary>Con của lớp desktop (cách cũ). Không hiện hình trên một số bản Windows 11 mới.</summary>
    Desktop
}

/// <summary>Cách hiển thị widget: một widget gọn duy nhất (mặc định) hay nhiều widget riêng.</summary>
public enum WidgetLayoutMode
{
    /// <summary>Một widget nhỏ gọn duy nhất, rê chuột/bấm để mở ra đủ mọi mục (Buddy, Hôm nay, Mục tiêu, lưới thói quen).</summary>
    Hub,

    /// <summary>Các widget riêng lẻ (lưới thói quen, Today List, Khoảnh khắc, Buddy, Mục tiêu, Multi Grid).</summary>
    Separate
}

/// <summary>Vị trí của widget gộp trên màn hình.</summary>
public enum WidgetPlacementMode
{
    /// <summary>Giữ vị trí hiện tại (góc phải mặc định hoặc nơi người dùng đã kéo tới).</summary>
    KeepPosition,

    /// <summary>"Dynamic Island": viên thuốc ghim giữa mép trên màn hình chính, luôn nằm trên cùng.</summary>
    TopCenter
}

public static class WidgetPlacements
{
    public const string SettingKey = "widgets.placement";

    public static WidgetPlacementMode Parse(string? stored) =>
        string.Equals(stored?.Trim(), "topcenter", StringComparison.OrdinalIgnoreCase) ? WidgetPlacementMode.TopCenter : WidgetPlacementMode.KeepPosition;

    public static string ToStored(WidgetPlacementMode placement) => placement == WidgetPlacementMode.TopCenter ? "topcenter" : "keep";
}

/// <summary>Các mục trong widget gộp.</summary>
[Flags]
public enum HubSection
{
    None = 0,
    Buddy = 1,
    Today = 2,
    Goals = 4,
    Grid = 8
}

public static class WidgetLayoutModes
{
    public const string SettingKey = "widgets.layout";

    public static WidgetLayoutMode Parse(string? stored) =>
        string.Equals(stored?.Trim(), "separate", StringComparison.OrdinalIgnoreCase) ? WidgetLayoutMode.Separate : WidgetLayoutMode.Hub;

    public static string ToStored(WidgetLayoutMode mode) => mode == WidgetLayoutMode.Separate ? "separate" : "hub";

    /// <summary>Free: Buddy và danh sách Hôm nay (check được ngay). Plus: thêm Mục tiêu và lưới thói quen.</summary>
    public static HubSection Sections(bool isPlus) =>
        isPlus ? HubSection.Buddy | HubSection.Today | HubSection.Goals | HubSection.Grid : HubSection.Buddy | HubSection.Today;
}

public static class PinChoices
{
    public const string SettingKey = "widgets.pinmode";

    public static PinChoice Parse(string? stored) => stored?.Trim().ToLowerInvariant() switch
    {
        "above" => PinChoice.AboveDesktop,
        "desktop" => PinChoice.Desktop,
        _ => PinChoice.Top
    };

    public static string ToStored(PinChoice choice) => choice switch
    {
        PinChoice.AboveDesktop => "above",
        PinChoice.Desktop => "desktop",
        _ => "top"
    };
}

/// <summary>
/// Xếp vị trí mặc định cho widget để không chồng lên nhau: lấp cột sát mép phải từ trên xuống, hết chỗ thì sang cột bên trái.
/// Hàm thuần theo pixel nên kiểm thử được; widget đã có vị trí người dùng lưu thì không dùng đến.
/// </summary>
public static class WidgetLayout
{
    public const int Margin = 24;
    public const int HubWidth = 288;
    public const int HubHeight = 64;
    public const int Gap = 12;

    // Chế độ "Dynamic Island" (giữa mép trên).
    // Trạng thái nghỉ: vòng Buddy + tên việc + dòng phụ + các chip (+1, ✓ n/m, 🔥 chuỗi).
    public const int IslandRestWidth = 380;
    public const int IslandRestHeight = 56;

    /// <summary>Xong hết / không có việc: bỏ nút +1 nên ngắn hơn.</summary>
    public const int IslandDoneWidth = 330;

    /// <summary>Nghỉ khi đang Focus: rộng cố định để đồng hồ đổi số mỗi giây không làm viên thuốc co giãn.</summary>
    public const int IslandFocusWidth = 380;

    /// <summary>Kiểu "siêu gọn": chỉ Buddy và các chip.</summary>
    public const int IslandMiniWidth = 170;
    public const int IslandMiniHeight = 46;

    public const int IslandListWidth = 380;
    public const int IslandPanelWidth = 384;

    /// <summary>Khoảng cách từ mép trên của màn hình tới viên thuốc, pixel vật lý.</summary>
    public const int IslandTopGap = 6;

    public readonly record struct Item(string Key, int Width, int Height);

    public readonly record struct Slot(int Left, int Top);

    /// <param name="workLeft">Vùng làm việc (không tính thanh tác vụ), pixel.</param>
    public static IReadOnlyDictionary<string, Slot> Plan(int workLeft, int workTop, int workRight, int workBottom, IEnumerable<Item> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var result = new Dictionary<string, Slot>();
        var columnRight = workRight - Margin;
        var y = workTop + Margin;
        var columnWidth = 0;
        var column = new List<Item>();

        void Place(Item item, int right, int top)
        {
            result[item.Key] = new Slot(Math.Max(workLeft, right - item.Width), top);
        }

        foreach (var item in items)
        {
            // Hết chỗ theo chiều dọc (và cột đã có phần tử): sang cột bên trái.
            if (column.Count > 0 && y + item.Height > workBottom - Margin)
            {
                columnRight -= columnWidth + Gap;
                y = workTop + Margin;
                columnWidth = 0;
                column.Clear();
            }

            Place(item, columnRight, y);
            column.Add(item);
            columnWidth = Math.Max(columnWidth, item.Width);
            y += item.Height + Gap;
        }

        return result;
    }

    /// <summary>
    /// Vị trí viên thuốc rộng <paramref name="width"/> pixel nằm giữa cạnh trên của màn hình có toạ độ (<paramref name="monitorLeft"/>, <paramref name="monitorTop"/>)
    /// và bề rộng <paramref name="monitorWidth"/> (toàn màn hình, không trừ taskbar), cách mép trên <see cref="IslandTopGap"/> pixel.
    /// Chạy đúng với toạ độ âm của màn hình phụ đặt bên trái/trên màn hình chính.
    /// </summary>
    public static Slot TopCenter(int monitorLeft, int monitorTop, int monitorWidth, int width) =>
        new(monitorLeft + ((monitorWidth - width) / 2), monitorTop + IslandTopGap);
}
