using HabitGrid.Core.Domain;
using HabitGrid.Presentation.ViewModels;

namespace HabitGrid.Presentation.Templates;

public enum HabitTopic
{
    Study,
    Health,
    Fitness,
    Mind,
    Creative,
    Work,
    Home,
    Quit
}

public enum TemplateFrequency
{
    Daily,
    TimesPerWeek,
    TimesPerMonth,
    EveryNDays
}

/// <summary>Một mẫu thói quen (02-UX-SCREENS §4: thư viện ≥ 60 mẫu, 8 chủ đề).</summary>
public sealed record HabitTemplate(
    string Id,
    HabitTopic Topic,
    string Emoji,
    string NameVi,
    string NameEn,
    HabitKind Kind = HabitKind.Boolean,
    int Target = 1,
    string? UnitVi = null,
    string? UnitEn = null,
    TimeOfDay TimeOfDay = TimeOfDay.Anytime,
    TemplateFrequency Frequency = TemplateFrequency.Daily,
    int FrequencyValue = 1)
{
    public string Name(string language) => language == "vi" ? NameVi : NameEn;

    public string? Unit(string language) => language == "vi" ? UnitVi : UnitEn;

    public HabitFrequency ToFrequency(DateOnly today) => Frequency switch
    {
        TemplateFrequency.TimesPerWeek => new HabitFrequency.TimesPerWeek(FrequencyValue),
        TemplateFrequency.TimesPerMonth => new HabitFrequency.TimesPerMonth(FrequencyValue),
        TemplateFrequency.EveryNDays => new HabitFrequency.EveryNDays(FrequencyValue, today),
        _ => new HabitFrequency.Daily()
    };

    public HabitDraft ToDraft(string language, DateOnly today) => new(
        Name(language),
        HabitIcon.Emoji(Emoji),
        Kind,
        Target,
        Unit(language),
        TimeOfDay,
        ToFrequency(today));
}

public static class HabitTemplates
{
    private const HabitKind Bool = HabitKind.Boolean;
    private const HabitKind Count = HabitKind.Count;
    private const HabitKind Dur = HabitKind.Duration;
    private const HabitKind Quit = HabitKind.Quit;

    public static IReadOnlyList<HabitTemplate> All { get; } = Build();

    public static IEnumerable<HabitTemplate> For(IEnumerable<HabitTopic> topics)
    {
        var set = topics.ToHashSet();
        return All.Where(t => set.Contains(t.Topic));
    }

    public static HabitTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    private static List<HabitTemplate> Build()
    {
        const TimeOfDay am = TimeOfDay.Morning;
        const TimeOfDay pm = TimeOfDay.Afternoon;
        const TimeOfDay eve = TimeOfDay.Evening;
        const TemplateFrequency perWeek = TemplateFrequency.TimesPerWeek;
        const TemplateFrequency perMonth = TemplateFrequency.TimesPerMonth;
        const TemplateFrequency everyN = TemplateFrequency.EveryNDays;

        return
        [
            // ---- Học tập
            new("study.read", HabitTopic.Study, "📚", "Đọc sách", "Read a book", Dur, 20, "phút", "min"),
            new("study.vocab", HabitTopic.Study, "🔤", "Học từ vựng", "Learn vocabulary", Count, 10, "từ", "words"),
            new("study.homework", HabitTopic.Study, "📝", "Làm bài tập", "Do homework"),
            new("study.review", HabitTopic.Study, "🧠", "Ôn bài", "Review notes", Dur, 30, "phút", "min", eve),
            new("study.listen", HabitTopic.Study, "🎧", "Luyện nghe tiếng Anh", "English listening", Dur, 15, "phút", "min"),
            new("study.journal", HabitTopic.Study, "📓", "Ghi chép học tập", "Study journal", Bool, 1, null, null, eve),
            new("study.code", HabitTopic.Study, "💻", "Giải một bài code", "Solve one coding problem"),
            new("study.video", HabitTopic.Study, "🎓", "Xem bài giảng", "Watch a lecture", Dur, 30, "phút", "min"),

            // ---- Sức khỏe
            new("health.water", HabitTopic.Health, "💧", "Uống nước", "Drink water", Count, 8, "ly", "glasses"),
            new("health.sleep", HabitTopic.Health, "😴", "Đi ngủ trước 23h", "In bed before 11pm", Bool, 1, null, null, eve),
            new("health.veg", HabitTopic.Health, "🥦", "Ăn rau", "Eat vegetables", Count, 3, "phần", "servings"),
            new("health.vitamin", HabitTopic.Health, "💊", "Uống vitamin", "Take vitamins", Bool, 1, null, null, am),
            new("health.breakfast", HabitTopic.Health, "🍳", "Ăn sáng", "Eat breakfast", Bool, 1, null, null, am),
            new("health.skin", HabitTopic.Health, "🧴", "Chăm sóc da buổi tối", "Evening skincare", Bool, 1, null, null, eve),
            new("health.sleep7", HabitTopic.Health, "🛌", "Ngủ đủ 7 tiếng", "Sleep 7 hours", Bool),
            new("health.sunlight", HabitTopic.Health, "☀️", "Ra nắng 10 phút", "10 minutes of sunlight", Dur, 10, "phút", "min", am),

            // ---- Thể dục
            new("fit.gym", HabitTopic.Fitness, "🏋️", "Tập gym", "Hit the gym", Bool, 1, null, null, TimeOfDay.Anytime, perWeek, 3),
            new("fit.run", HabitTopic.Fitness, "🏃", "Chạy bộ", "Go running", Dur, 30, "phút", "min", am, perWeek, 3),
            new("fit.steps", HabitTopic.Fitness, "👟", "Đi bộ", "Walk", Count, 8000, "bước", "steps"),
            new("fit.pushup", HabitTopic.Fitness, "💪", "Hít đất", "Push-ups", Count, 30, "cái", "reps"),
            new("fit.stretch", HabitTopic.Fitness, "🤸", "Giãn cơ", "Stretch", Dur, 10, "phút", "min", am),
            new("fit.yoga", HabitTopic.Fitness, "🧘‍♀️", "Yoga", "Yoga", Dur, 30, "phút", "min", TimeOfDay.Anytime, perWeek, 3),
            new("fit.bike", HabitTopic.Fitness, "🚴", "Đạp xe", "Cycling", Bool, 1, null, null, TimeOfDay.Anytime, perWeek, 2),
            new("fit.plank", HabitTopic.Fitness, "🪵", "Plank", "Plank", Dur, 2, "phút", "min"),

            // ---- Tinh thần
            new("mind.meditate", HabitTopic.Mind, "🧘", "Thiền", "Meditate", Dur, 10, "phút", "min", am),
            new("mind.gratitude", HabitTopic.Mind, "🙏", "Viết điều biết ơn", "Write gratitude", Bool, 1, null, null, eve),
            new("mind.breathe", HabitTopic.Mind, "🌬️", "Hít thở sâu", "Deep breathing", Count, 3, "lần", "times"),
            new("mind.nophone", HabitTopic.Mind, "📵", "Không điện thoại trước khi ngủ", "No phone before bed", Bool, 1, null, null, eve),
            new("mind.call", HabitTopic.Mind, "📞", "Gọi cho người thân", "Call family", Bool, 1, null, null, TimeOfDay.Anytime, perWeek, 2),
            new("mind.walk", HabitTopic.Mind, "🌳", "Đi dạo ngoài trời", "Walk outside", Dur, 20, "phút", "min"),
            new("mind.journal", HabitTopic.Mind, "✍️", "Viết nhật ký", "Journal", Bool, 1, null, null, eve),
            new("mind.read", HabitTopic.Mind, "📖", "Đọc sách nhẹ nhàng", "Light reading", Dur, 15, "phút", "min", eve),

            // ---- Sáng tạo
            new("cre.draw", HabitTopic.Creative, "🎨", "Vẽ", "Draw", Dur, 20, "phút", "min"),
            new("cre.write", HabitTopic.Creative, "🖋️", "Viết lách", "Write", Dur, 30, "phút", "min"),
            new("cre.music", HabitTopic.Creative, "🎸", "Tập nhạc cụ", "Practice an instrument", Dur, 30, "phút", "min"),
            new("cre.photo", HabitTopic.Creative, "📷", "Chụp một bức ảnh", "Take a photo"),
            new("cre.beat", HabitTopic.Creative, "🎹", "Làm nhạc", "Make music", Dur, 30, "phút", "min"),
            new("cre.vlog", HabitTopic.Creative, "🎬", "Quay vlog", "Film a vlog", Bool, 1, null, null, TimeOfDay.Anytime, perWeek, 2),
            new("cre.design", HabitTopic.Creative, "🖌️", "Luyện thiết kế", "Design practice", Dur, 30, "phút", "min"),
            new("cre.sideproject", HabitTopic.Creative, "🚀", "Làm dự án riêng", "Work on a side project", Dur, 45, "phút", "min", eve),

            // ---- Công việc
            new("work.plan", HabitTopic.Work, "🗓️", "Lên kế hoạch cho ngày", "Plan the day", Bool, 1, null, null, am),
            new("work.inbox", HabitTopic.Work, "📥", "Dọn hộp thư", "Clear inbox", Bool, 1, null, null, pm),
            new("work.focus", HabitTopic.Work, "🎯", "Làm việc sâu", "Deep work", Dur, 120, "phút", "min"),
            new("work.news", HabitTopic.Work, "📰", "Đọc tin ngành", "Read industry news", Dur, 15, "phút", "min"),
            new("work.review", HabitTopic.Work, "✅", "Tổng kết cuối ngày", "End-of-day review", Bool, 1, null, null, eve),
            new("work.skill", HabitTopic.Work, "🛠️", "Học kỹ năng mới", "Learn a new skill", Dur, 30, "phút", "min"),
            new("work.weekly", HabitTopic.Work, "📊", "Báo cáo tuần", "Weekly report", Bool, 1, null, null, TimeOfDay.Anytime, perWeek, 1),
            new("work.network", HabitTopic.Work, "🤝", "Kết nối người mới", "Network", Bool, 1, null, null, TimeOfDay.Anytime, perMonth, 4),

            // ---- Nhà cửa
            new("home.bed", HabitTopic.Home, "🛏️", "Dọn giường", "Make the bed", Bool, 1, null, null, am),
            new("home.dishes", HabitTopic.Home, "🍽️", "Rửa bát", "Do the dishes", Bool, 1, null, null, eve),
            new("home.laundry", HabitTopic.Home, "🧺", "Giặt đồ", "Laundry", Bool, 1, null, null, TimeOfDay.Anytime, perWeek, 2),
            new("home.clean", HabitTopic.Home, "🧹", "Dọn phòng", "Clean the room", Bool, 1, null, null, TimeOfDay.Anytime, perWeek, 2),
            new("home.plants", HabitTopic.Home, "🪴", "Tưới cây", "Water plants", Bool, 1, null, null, TimeOfDay.Anytime, everyN, 2),
            new("home.cook", HabitTopic.Home, "🍲", "Nấu ăn ở nhà", "Cook at home", Bool, 1, null, null, eve, perWeek, 4),
            new("home.trash", HabitTopic.Home, "🗑️", "Đổ rác", "Take out the trash", Bool, 1, null, null, TimeOfDay.Anytime, everyN, 3),
            new("home.budget", HabitTopic.Home, "💰", "Ghi chép chi tiêu", "Track spending", Bool, 1, null, null, eve),

            // ---- Bỏ thói quen xấu
            new("quit.latephone", HabitTopic.Quit, "📱", "Không lướt điện thoại khuya", "No late-night scrolling", Quit),
            new("quit.bubbletea", HabitTopic.Quit, "🧋", "Không uống trà sữa", "No bubble tea", Quit),
            new("quit.smoke", HabitTopic.Quit, "🚭", "Không hút thuốc", "No smoking", Quit),
            new("quit.alcohol", HabitTopic.Quit, "🍺", "Không rượu bia", "No alcohol", Quit),
            new("quit.caffeine", HabitTopic.Quit, "☕", "Không cà phê sau 2 giờ chiều", "No coffee after 2pm", Quit),
            new("quit.impulse", HabitTopic.Quit, "🛍️", "Không mua sắm bốc đồng", "No impulse shopping", Quit),
            new("quit.nails", HabitTopic.Quit, "💅", "Không cắn móng tay", "No nail biting", Quit),
            new("quit.stayup", HabitTopic.Quit, "🌙", "Không thức khuya", "No staying up late", Quit)
        ];
    }
}
