namespace HabitGrid.Core.Domain.Dashboard;

/// <summary>Các khối có thể sắp xếp/ẩn của trang Hôm nay. Danh sách thói quen luôn hiện và không nằm ở đây.</summary>
public static class DashboardSections
{
    public const string Week = "week";
    public const string Mood = "mood";
    public const string Recap = "recap";
    public const string Routines = "routines";
    public const string Challenges = "challenges";
    public const string Hero = "hero";

    /// <summary>Thứ tự mặc định (khớp bố cục trước khi có tùy biến).</summary>
    public static IReadOnlyList<string> Default { get; } = [Week, Mood, Recap, Routines, Challenges, Hero];
}

/// <summary>
/// Bố cục tùy biến của trang Hôm nay: thứ tự và khối nào bị ẩn. Thuần logic, lưu thành một chuỗi trong cài đặt
/// (<c>week,mood,-recap,…</c>: dấu <c>-</c> trước tên là khối đã ẩn). Đọc chuỗi hỏng/cũ vẫn ra bố cục hợp lệ:
/// bỏ khối lạ, bỏ trùng, khối còn thiếu (ví dụ bản mới thêm khối) được thêm vào cuối.
/// </summary>
public sealed class DashboardLayout
{
    public const string SettingKey = "today.layout";

    private readonly List<(string Id, bool Visible)> _items;

    private DashboardLayout(List<(string Id, bool Visible)> items)
    {
        _items = items;
    }

    /// <summary>Các khối theo thứ tự hiện tại kèm trạng thái hiện/ẩn.</summary>
    public IReadOnlyList<(string Id, bool Visible)> Items => _items;

    /// <summary>Id các khối đang hiện, theo thứ tự.</summary>
    public IEnumerable<string> VisibleIds => _items.Where(i => i.Visible).Select(i => i.Id);

    public bool IsVisible(string id) => _items.Any(i => i.Id == id && i.Visible);

    public static DashboardLayout Default() => new(DashboardSections.Default.Select(id => (id, true)).ToList());

    public static DashboardLayout Parse(string? text)
    {
        var items = new List<(string Id, bool Visible)>();
        var known = DashboardSections.Default.ToHashSet();

        foreach (var raw in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var hidden = raw.StartsWith('-');
            var id = hidden ? raw[1..] : raw;

            if (known.Contains(id) && items.All(i => i.Id != id))
            {
                items.Add((id, !hidden));
            }
        }

        foreach (var id in DashboardSections.Default.Where(id => items.All(i => i.Id != id)))
        {
            items.Add((id, true));
        }

        return new DashboardLayout(items);
    }

    public string Serialize() => string.Join(',', _items.Select(i => (i.Visible ? string.Empty : "-") + i.Id));

    /// <summary>Đổi hiện/ẩn một khối; id không có thì bỏ qua.</summary>
    public DashboardLayout WithVisible(string id, bool visible) =>
        new(_items.Select(i => i.Id == id ? (i.Id, visible) : i).ToList());

    /// <summary>Dời một khối lên (<paramref name="delta"/> âm) hoặc xuống (dương), giữ trong giới hạn.</summary>
    public DashboardLayout Move(string id, int delta)
    {
        var list = _items.ToList();
        var from = list.FindIndex(i => i.Id == id);

        if (from < 0)
        {
            return this;
        }

        var to = Math.Clamp(from + delta, 0, list.Count - 1);

        if (to == from)
        {
            return this;
        }

        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        return new DashboardLayout(list);
    }
}
