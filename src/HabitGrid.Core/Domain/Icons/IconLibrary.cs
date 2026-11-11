using System.Globalization;
using System.Text;

namespace HabitGrid.Core.Domain.Icons;

public enum IconCategory
{
    Health,
    Sport,
    Mind,
    Work,
    Home,
    Food,
    Money,
    Nature,
    Fun,
    Social,
    Travel,
    Symbols
}

/// <summary>Một biểu tượng chọn được: emoji, danh mục và từ khóa tìm kiếm (tiếng Việt không dấu + tiếng Anh).</summary>
public sealed record IconEntry(string Emoji, IconCategory Category, string Keywords);

/// <summary>
/// Thư viện biểu tượng cho thói quen: gom theo danh mục, tìm theo từ khóa (không phân biệt hoa/thường và dấu) và gợi ý từ tên thói quen.
/// Thuần logic nên kiểm thử được; giao diện chỉ hiển thị.
/// </summary>
public static class IconLibrary
{
    public static IReadOnlyList<IconEntry> All { get; } =
    [
        // Sức khỏe
        new("💧", IconCategory.Health, "nuoc uong water drink hydrate"),
        new("💊", IconCategory.Health, "thuoc vitamin pill medicine"),
        new("😴", IconCategory.Health, "ngu giac sleep rest"),
        new("🛌", IconCategory.Health, "giuong ngu di ngu bed sleep"),
        new("🦷", IconCategory.Health, "danh rang rang mieng teeth brush dental"),
        new("🧴", IconCategory.Health, "duong da kem chong nang skincare lotion"),
        new("🚿", IconCategory.Health, "tam shower wash"),
        new("🧼", IconCategory.Health, "rua tay xa phong soap wash hands"),
        new("🩺", IconCategory.Health, "kham suc khoe bac si doctor checkup"),
        new("❤️", IconCategory.Health, "tim suc khoe heart health"),
        new("🧘", IconCategory.Health, "thien yoga meditate"),
        new("🌡️", IconCategory.Health, "nhiet do sot temperature"),
        new("⚖️", IconCategory.Health, "can nang weight scale"),
        new("👁️", IconCategory.Health, "mat nghi mat eyes rest"),
        // Thể thao
        new("🏃", IconCategory.Sport, "chay bo run running jog"),
        new("🚴", IconCategory.Sport, "dap xe bike cycling"),
        new("🏋️", IconCategory.Sport, "gym ta tap luc weights lift"),
        new("💪", IconCategory.Sport, "co bap suc manh muscle strength workout"),
        new("🏊", IconCategory.Sport, "boi swim"),
        new("🧗", IconCategory.Sport, "leo nui climb"),
        new("⚽", IconCategory.Sport, "bong da football soccer"),
        new("🏀", IconCategory.Sport, "bong ro basketball"),
        new("🎾", IconCategory.Sport, "tennis"),
        new("🏸", IconCategory.Sport, "cau long badminton"),
        new("🥊", IconCategory.Sport, "boxing dam bokc"),
        new("🤸", IconCategory.Sport, "gian co stretch"),
        new("🚶", IconCategory.Sport, "di bo walk steps"),
        new("🛹", IconCategory.Sport, "truot skate"),
        new("⛷️", IconCategory.Sport, "tuyet ski"),
        new("🏓", IconCategory.Sport, "bong ban ping pong"),
        // Tinh thần / học tập
        new("📚", IconCategory.Mind, "doc sach book read"),
        new("📖", IconCategory.Mind, "sach book open read"),
        new("✍️", IconCategory.Mind, "viet write journal"),
        new("📝", IconCategory.Mind, "ghi chu nhat ky note journal"),
        new("🧠", IconCategory.Mind, "hoc brain learn study"),
        new("🎓", IconCategory.Mind, "tot nghiep hoc graduate study"),
        new("🌐", IconCategory.Mind, "ngoai ngu tieng anh language english"),
        new("🗣️", IconCategory.Mind, "noi giao tiep speak talk"),
        new("🔬", IconCategory.Mind, "khoa hoc science"),
        new("🧩", IconCategory.Mind, "giai do puzzle"),
        new("♟️", IconCategory.Mind, "co vua chess"),
        new("🙏", IconCategory.Mind, "cau nguyen biet on gratitude pray"),
        new("🧘‍♀️", IconCategory.Mind, "thien tinh tam meditate mindful"),
        new("📵", IconCategory.Mind, "bo dien thoai digital detox phone"),
        // Công việc
        new("💻", IconCategory.Work, "may tinh lap trinh code laptop"),
        new("📧", IconCategory.Work, "email thu mail"),
        new("📞", IconCategory.Work, "goi dien call phone"),
        new("📅", IconCategory.Work, "lich ke hoach calendar plan"),
        new("🎯", IconCategory.Work, "muc tieu target goal"),
        new("💼", IconCategory.Work, "cong viec briefcase work"),
        new("📊", IconCategory.Work, "bao cao chart report"),
        new("🗂️", IconCategory.Work, "tai lieu file organize"),
        new("⏰", IconCategory.Work, "bao thuc day som alarm wake early"),
        new("⏱️", IconCategory.Work, "tap trung focus timer pomodoro"),
        new("🖊️", IconCategory.Work, "but viet pen"),
        new("📈", IconCategory.Work, "tang truong growth"),
        // Nhà cửa
        new("🧹", IconCategory.Home, "don dep quet nha clean sweep"),
        new("🧺", IconCategory.Home, "giat do laundry"),
        new("🍽️", IconCategory.Home, "rua bat dishes"),
        new("🛏️", IconCategory.Home, "don giuong make bed"),
        new("🗑️", IconCategory.Home, "do rac trash"),
        new("🪴", IconCategory.Home, "cay canh tuoi plant water"),
        new("🏠", IconCategory.Home, "nha home"),
        new("🔧", IconCategory.Home, "sua chua repair fix"),
        new("🐕", IconCategory.Home, "cho dat di dao walk dog pet"),
        new("🐈", IconCategory.Home, "meo cat pet"),
        new("🐟", IconCategory.Home, "ca cho an fish feed"),
        new("🧸", IconCategory.Home, "tre em con kids"),
        // Ăn uống
        new("🥗", IconCategory.Food, "rau salad an lanh healthy eat"),
        new("🍎", IconCategory.Food, "trai cay tao fruit apple"),
        new("🥦", IconCategory.Food, "rau xanh bong cai vegetables"),
        new("🍳", IconCategory.Food, "nau an cook breakfast"),
        new("🥛", IconCategory.Food, "sua milk"),
        new("🍵", IconCategory.Food, "tra tea"),
        new("☕", IconCategory.Food, "ca phe coffee"),
        new("🍚", IconCategory.Food, "com rice meal"),
        new("🚫", IconCategory.Food, "kieng bo cai quit avoid"),
        new("🍬", IconCategory.Food, "duong ngot sugar sweet"),
        new("🚭", IconCategory.Food, "thuoc la hut thuoc smoke quit"),
        new("🍺", IconCategory.Food, "ruou bia beer alcohol"),
        // Tài chính
        new("💰", IconCategory.Money, "tiet kiem tien money save"),
        new("💵", IconCategory.Money, "thu nhap tien cash income"),
        new("💳", IconCategory.Money, "the thanh toan card pay"),
        new("🏦", IconCategory.Money, "ngan hang bank"),
        new("📉", IconCategory.Money, "chi tieu giam spending expense"),
        new("🧾", IconCategory.Money, "hoa don receipt bill"),
        new("🪙", IconCategory.Money, "dau tu coin invest"),
        new("🛒", IconCategory.Money, "mua sam cart shopping"),
        // Thiên nhiên
        new("🌱", IconCategory.Nature, "cay mam trong plant grow seedling"),
        new("🌳", IconCategory.Nature, "cay tree"),
        new("☀️", IconCategory.Nature, "nang mat troi sun morning"),
        new("🌙", IconCategory.Nature, "trang dem moon night"),
        new("⭐", IconCategory.Nature, "sao star"),
        new("🌈", IconCategory.Nature, "cau vong rainbow"),
        new("🌸", IconCategory.Nature, "hoa flower"),
        new("🌊", IconCategory.Nature, "bien song wave sea"),
        new("⛰️", IconCategory.Nature, "nui mountain hike"),
        new("🔥", IconCategory.Nature, "lua fire streak"),
        new("🌍", IconCategory.Nature, "trai dat earth"),
        new("♻️", IconCategory.Nature, "tai che recycle"),
        // Giải trí
        new("🎸", IconCategory.Fun, "guitar nhac cu music instrument"),
        new("🎹", IconCategory.Fun, "piano dan keyboard music"),
        new("🎧", IconCategory.Fun, "nghe nhac podcast music listen"),
        new("🎨", IconCategory.Fun, "ve tranh paint draw art"),
        new("📷", IconCategory.Fun, "chup anh photo camera"),
        new("🎮", IconCategory.Fun, "choi game gaming"),
        new("🎬", IconCategory.Fun, "phim movie film"),
        new("🎤", IconCategory.Fun, "hat sing karaoke"),
        new("🧶", IconCategory.Fun, "dan len knit craft"),
        new("🪡", IconCategory.Fun, "may vo sew craft"),
        new("🎲", IconCategory.Fun, "co board game dice"),
        new("📺", IconCategory.Fun, "xem tv watch"),
        // Xã hội
        new("👨‍👩‍👧", IconCategory.Social, "gia dinh family"),
        new("👫", IconCategory.Social, "ban be friends couple"),
        new("☎️", IconCategory.Social, "goi ba me call parents"),
        new("💬", IconCategory.Social, "nhan tin chat message"),
        new("🤝", IconCategory.Social, "gap go networking meet"),
        new("🎁", IconCategory.Social, "qua tang gift"),
        new("😊", IconCategory.Social, "cuoi vui smile happy"),
        new("🫶", IconCategory.Social, "yeu thuong love care"),
        new("🙌", IconCategory.Social, "tinh nguyen volunteer help"),
        new("💌", IconCategory.Social, "thu love letter"),
        // Du lịch
        new("✈️", IconCategory.Travel, "may bay du lich plane travel"),
        new("🚗", IconCategory.Travel, "o to lai xe car drive"),
        new("🚌", IconCategory.Travel, "xe buyt bus transit"),
        new("🧳", IconCategory.Travel, "hanh ly luggage trip"),
        new("🗺️", IconCategory.Travel, "ban do map explore"),
        new("⛺", IconCategory.Travel, "cam trai camping"),
        new("🏖️", IconCategory.Travel, "bien beach vacation"),
        new("🚲", IconCategory.Travel, "xe dap bicycle commute"),
        // Biểu tượng
        new("✨", IconCategory.Symbols, "lap lanh sparkle magic"),
        new("✅", IconCategory.Symbols, "xong tick done check"),
        new("🏆", IconCategory.Symbols, "cup thanh tich trophy win"),
        new("🚀", IconCategory.Symbols, "khoi nghiep rocket launch"),
        new("💡", IconCategory.Symbols, "y tuong idea"),
        new("🔔", IconCategory.Symbols, "nhac nho bell reminder"),
        new("🔒", IconCategory.Symbols, "khoa bao mat lock secure"),
        new("🎉", IconCategory.Symbols, "an mung party celebrate"),
        new("🧿", IconCategory.Symbols, "may man luck"),
        new("♾️", IconCategory.Symbols, "vo han infinity"),
    ];

    /// <summary>Các biểu tượng trong một danh mục, giữ thứ tự khai báo.</summary>
    public static IReadOnlyList<IconEntry> InCategory(IconCategory category) => All.Where(i => i.Category == category).ToList();

    /// <summary>Bỏ dấu, hạ chữ thường (đ → d).</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>Tìm theo từ khóa: mọi từ của truy vấn phải khớp đầu một từ khóa; rỗng thì trả về tất cả.</summary>
    public static IReadOnlyList<IconEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return All;
        }

        var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return All.Where(i => words.All(w => i.Keywords.Split(' ').Any(k => k.StartsWith(w, StringComparison.Ordinal)))).ToList();
    }

    /// <summary>
    /// Gợi ý biểu tượng từ tên thói quen ("Chạy bộ buổi sáng" → 🏃…): chấm điểm theo số từ trong tên khớp từ khóa, trả về tối đa <paramref name="max"/>.
    /// Tên rỗng hoặc không khớp gì thì không gợi ý.
    /// </summary>
    public static IReadOnlyList<IconEntry> Suggest(string? habitName, int max = 6)
    {
        if (string.IsNullOrWhiteSpace(habitName))
        {
            return [];
        }

        var words = Fold(habitName).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2).Distinct().ToList();

        // Cụm hai từ liền nhau ("chay bo") khớp mạnh hơn từng từ rời.
        var phrases = words.Zip(words.Skip(1), (a, b) => a + " " + b).ToList();

        return All
            .Select(i =>
            {
                var folded = i.Keywords;
                var score = phrases.Count(p => folded.Contains(p, StringComparison.Ordinal)) * 3
                            + words.Count(w => folded.Split(' ').Any(k => k == w || (w.Length >= 3 && k.StartsWith(w, StringComparison.Ordinal))));
                return (Entry: i, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(max)
            .Select(x => x.Entry)
            .ToList();
    }
}
