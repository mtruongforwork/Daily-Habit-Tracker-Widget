using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Dựng ảnh widget cho trang Store (<c>--shots=THƯ_MỤC [--lang=vi|en]</c>): render chính cửa sổ widget bằng WPF với dữ liệu tạm
/// (không chụp màn hình người dùng), rồi ghép lên một nền hình desktop 1920×1080 ở đúng vị trí giữa mép trên.
/// Chỉ phần giao diện widget là thật; nền là hình minh hoạ.
/// </summary>
public static class ShotMaker
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const double Zoom = 1.5;

    public static async Task<int> RunAsync(WidgetController controller, string outDir, string language)
    {
        Directory.CreateDirectory(outDir);
        await controller.SetPlacementForTestAsync(HabitGrid.Core.Domain.Widgets.WidgetPlacementMode.TopCenter);
        await Task.Delay(1800);
        var hub = controller.Hub ?? throw new InvalidOperationException("Không dựng được widget gộp.");

        // Dữ liệu minh hoạ sạch (không dùng dữ liệu người dùng), giờ cố định giữa chiều để dòng "còn … hôm nay" ổn định.
        var demo = DemoData(language == "vi");
        hub.Now = () => DateTime.Today.AddHours(14).AddMinutes(20);
        hub.Update(demo);
        await Task.Delay(400);

        // 1) Viên thuốc nghỉ.
        hub.SetLevel(ViewLevel.Rest, animate: false);
        hub.Update(demo);
        await Task.Delay(500);
        Save(hub, Path.Combine(outDir, $"widget-{language}-1-pill.png"));

        // 2) Danh sách khi rê chuột.
        hub.SetLevel(ViewLevel.Hover, animate: false);
        await Task.Delay(500);
        Save(hub, Path.Combine(outDir, $"widget-{language}-2-hover.png"));

        // 3) Bảng đầy đủ.
        hub.SetLevel(ViewLevel.Panel, animate: false);
        await Task.Delay(500);
        Save(hub, Path.Combine(outDir, $"widget-{language}-3-panel.png"));
        return 0;
    }

    private static HubData DemoData(bool vi)
    {
        HabitGrid.Core.Domain.Habit Habit(string name, string emoji, bool count, int target)
        {
            return new HabitGrid.Core.Domain.Habit
            {
                Name = name,
                Icon = HabitGrid.Core.Domain.HabitIcon.Emoji(emoji),
                Kind = count ? HabitGrid.Core.Domain.HabitKind.Count : HabitGrid.Core.Domain.HabitKind.Boolean,
                Target = new HabitGrid.Core.Domain.HabitTarget(target, null),
                StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-60))
            };
        }

        var water = Habit(vi ? "Uống đủ 2L nước" : "Drink 2L of water", "💧", true, 8);
        var read = Habit(vi ? "Đọc 20 trang sách" : "Read 20 pages", "📚", false, 1);
        var meditate = Habit(vi ? "Thiền 10 phút" : "Meditate 10 minutes", "🧘", false, 1);
        var stretch = Habit(vi ? "Giãn cơ buổi tối" : "Evening stretch", "🤸", false, 1);

        var today = new List<HabitGrid.Presentation.Services.QuickItem>
        {
            new(water, 4, HabitGrid.Core.Domain.DayStatus.None),
            new(read, 1, HabitGrid.Core.Domain.DayStatus.Done),
            new(meditate, 1, HabitGrid.Core.Domain.DayStatus.Done),
            new(stretch, 0, HabitGrid.Core.Domain.DayStatus.None)
        };

        var buddy = new HabitGrid.Presentation.Services.BuddyWidgetData(
            "Mochi", 8, HabitGrid.Presentation.ViewModels.BuddyViewModel.StageFor(8), [], 2, 4, 1, 120, 289, 12);
        var streaks = new Dictionary<Guid, int> { [water.Id] = 5, [read.Id] = 12, [meditate.Id] = 3, [stretch.Id] = 0 };
        var stats = new HabitGrid.Presentation.Services.WidgetStatsData(21, 82, streaks);

        // Lưới tuần (13 tuần × 7 ngày, xếp theo cột) để bảng có thêm tab "Tuần này".
        static IReadOnlyList<int> Cells(int seed)
        {
            var random = new Random(seed);
            return Enumerable.Range(0, 91).Select(i => i > 88 ? -1 : (random.NextDouble() < 0.72 ? 1 : 0)).ToList();
        }

        var grid = new HabitGrid.Presentation.Services.MultiGridData(13,
        [
            new(water.Id, water.Name, water.Icon.Value, "#3B82F6", 5, HabitGrid.Core.Domain.StreakUnit.Day, Cells(1)),
            new(read.Id, read.Name, read.Icon.Value, "#EC4899", 12, HabitGrid.Core.Domain.StreakUnit.Day, Cells(2)),
            new(meditate.Id, meditate.Name, meditate.Icon.Value, "#8B5CF6", 3, HabitGrid.Core.Domain.StreakUnit.Day, Cells(3))
        ]);

        return new HubData(buddy, today, null, grid, stats);
    }

    private static void Save(HubWidgetWindow hub, string path)
    {
        hub.UpdateLayout();

        if (hub.Content is not FrameworkElement card)
        {
            throw new InvalidOperationException("Widget chưa có nội dung.");
        }

        card.UpdateLayout();
        var w = card.ActualWidth;
        var h = card.ActualHeight;
        var cardPx = new RenderTargetBitmap((int)Math.Ceiling(w * Zoom), (int)Math.Ceiling(h * Zoom), 96 * Zoom, 96 * Zoom, PixelFormats.Pbgra32);
        cardPx.Render(card);

        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            DrawBackground(dc);

            // Cách mép trên 22 px trong ảnh (tỷ lệ với phóng đại), căn giữa ngang.
            var x = (Width - (w * Zoom)) / 2;
            dc.DrawImage(cardPx, new Rect(x, 22, w * Zoom, h * Zoom));
        }

        var final = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        final.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Nền hình desktop minh hoạ: gradient tím-xanh với vài quầng sáng mờ.</summary>
    private static void DrawBackground(DrawingContext dc)
    {
        var sky = new LinearGradientBrush(Color.FromRgb(15, 23, 42), Color.FromRgb(56, 26, 104), new Point(0, 0), new Point(1, 1));
        dc.DrawRectangle(sky, null, new Rect(0, 0, Width, Height));

        void Glow(double cx, double cy, double r, Color color)
        {
            var brush = new RadialGradientBrush(Color.FromArgb(150, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
            dc.DrawEllipse(brush, null, new Point(cx, cy), r, r);
        }

        Glow(1500, 820, 620, Color.FromRgb(236, 72, 153));
        Glow(380, 700, 560, Color.FromRgb(59, 130, 246));
        Glow(1000, 1000, 520, Color.FromRgb(139, 92, 246));

        // Thanh tác vụ mờ ở đáy cho giống màn hình thật.
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 12, 12, 20)), null, new Rect(0, Height - 56, Width, 56));
    }
}
