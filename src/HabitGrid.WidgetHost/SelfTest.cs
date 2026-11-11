using System.IO;
using HabitGrid.Core.Domain;
using HabitGrid.Data;
using HabitGrid.Data.V3;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Tự kiểm tra đầu-cuối (CI): có widget dựng từ CSDL, bấm ô hôm nay ghi vào CSDL, và một ghi từ kết nối khác
/// (như app chính) hiện lên widget trong vài giây. Trả 0 nếu đạt, 2 nếu hỏng.
/// </summary>
public static class SelfTest
{
    private static void SaveShot(System.Windows.Window window, string path)
    {
        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window.Content as System.Windows.Media.Visual ?? window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static async Task<int> RunAsync(WidgetController controller, string dbPath)
    {
        try
        {
            await Task.Delay(1500);
            Require(controller.Windows.Count > 0, "Không dựng được widget nào từ CSDL.");
            Require(controller.Windows.All(w => w.IsVisible), "Có widget không hiển thị.");

            var model = controller.Models.First();
            var habitId = model.Habit.Id;
            var today = controller.Today;
            var before = model.Values.GetValueOrDefault(today);

            // 1) Bấm ô hôm nay trên widget: CSDL phải đổi và widget phải cập nhật.
            model.RequestMark(today);
            await Task.Delay(1500);

            using (var db = HabitGridV3.Open(dbPath))
            {
                var logs = new DayLogRepository(db, new HlcClock("selftest"), null);
                var log = await logs.GetAsync(habitId, today);
                Require((log?.Value ?? 0) != before, "Bấm widget nhưng CSDL không đổi.");
            }

            Require(model.Values.GetValueOrDefault(today) != before, "Widget không cập nhật sau khi check-in.");

            // 2) Ghi từ kết nối khác (như app chính): widget phải thấy qua data_version.
            var yesterday = today.AddDays(-1);

            using (var db = HabitGridV3.Open(dbPath))
            {
                var clock = new HlcClock("selftest2");
                var habit = await new HabitRepository(db, clock, null).GetByIdAsync(habitId);
                var logs = new DayLogRepository(db, clock, null);
                await logs.UpsertAsync(DayLogRules.Normalize(habit!, new DayLog { HabitId = habitId, Day = yesterday, Value = 1 }));
            }

            await Task.Delay(4500);
            Require(model.Values.ContainsKey(yesterday), "Ghi từ kết nối khác không hiện lên widget (làm mới theo data_version hỏng).");

            // 3) Today List: hiển thị cùng số thói quen đến hạn.
            Require(controller.TodayList is { IsVisible: true }, "Widget Today List không hiển thị.");

            // 4) Quick check-in: lọc không dấu rồi Enter check đúng thói quen đó.
            var quick = controller.QuickWindow ?? throw new InvalidOperationException("Không có cửa sổ Quick check-in.");
            var all = quick.SetFilterForTest(string.Empty);
            Require(all.Count > 0, "Quick check-in không liệt kê thói quen nào.");
            var target = all.First(i => !i.IsDone && !i.IsSkipped);
            var typed = HabitGrid.Presentation.Services.QuickCheckInModel.Fold(target.Name);
            var shown = quick.SetFilterForTest(typed);
            Require(shown.Count > 0 && shown[0].Id == target.Id, "Lọc không dấu không trả về đúng thói quen.");

            await quick.EnterForTestAsync();
            await Task.Delay(1000);

            using (var db = HabitGridV3.Open(dbPath))
            {
                var logs = new DayLogRepository(db, new HlcClock("selftest3"), null);
                var log = await logs.GetAsync(target.Id, today);
                Require((log?.Value ?? 0) > target.Value, "Enter trong Quick check-in không ghi vào CSDL.");
            }

            // 5) Nhắc nhở: gắn giờ nhắc 10:00 hằng ngày cho một thói quen, lập lịch, kiểm tra Windows nhận toast.
            var reminderHabitId = Guid.Empty;

            using (var db = HabitGridV3.Open(dbPath))
            {
                var clock = new HlcClock("selftest5");
                var habits = new HabitRepository(db, clock, null);
                var habit = (await habits.GetAllActiveAsync()).First(h => h.Kind != HabitKind.Quit);
                habit.Reminders = [new Reminder(new TimeOnly(10, 0))];
                await habits.UpdateAsync(habit);
                reminderHabitId = habit.Id;
            }

            await controller.Toasts.ReplanAsync(DateTime.Now);
            var plan = controller.Toasts.LastPlan;
            Require(plan.Any(p => p.HabitIds.Contains(reminderHabitId)), "Lập lịch không tạo toast cho thói quen có giờ nhắc.");
            var scheduled = controller.Toasts.ScheduledCount();
            Note(dbPath, $"toasts planned={plan.Count} scheduled={scheduled} error={controller.Toasts.LastError ?? "none"}");
            Require(scheduled == plan.Count, $"Windows không nhận đủ toast đã lên lịch ({scheduled}/{plan.Count}): {controller.Toasts.LastError}");

            // 6) Nút "Xong" trên toast ghi đúng; đã xong thì toast hôm nay của thói quen đó bị gỡ.
            var args = CommunityToolkit.WinUI.Notifications.ToastArguments.Parse(
                $"action=done;ids={reminderHabitId};day={today:yyyy-MM-dd}");
            await controller.HandleToastAsync(args);

            using (var db = HabitGridV3.Open(dbPath))
            {
                var log = await new DayLogRepository(db, new HlcClock("selftest6"), null).GetAsync(reminderHabitId, today);
                Require(log?.Status == DayStatus.Done, "Nút Xong trên toast không ghi được check-in.");
            }

            Require(!controller.Toasts.LastPlan.Any(p => DateOnly.FromDateTime(p.At) == today && p.HabitIds.Contains(reminderHabitId)),
                "Đã xong nhưng toast nhắc hôm nay vẫn còn trong lịch.");
            Require(controller.Toasts.ScheduledCount() == controller.Toasts.LastPlan.Count, "Lịch của Windows không khớp kế hoạch sau khi check-in.");

            // 6b) Widget khoảnh khắc (Plus tặng + smoke đã thêm ảnh): phải hiện.
            // Chỉ kiểm khi dữ liệu có ảnh (smoke của app thêm ảnh; dữ liệu tối thiểu của job kiểm gói MSIX thì không).
            var momentsDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "moments");

            if (Directory.Exists(momentsDir) && Directory.EnumerateFiles(momentsDir, "*", SearchOption.AllDirectories).Any())
            {
                await controller.SyncMomentWidgetAsync();
                Require(controller.MomentWidget is { IsVisible: true }, "Widget khoảnh khắc không hiện dù đang Plus và có ảnh.");
                Note(dbPath, "moment widget visible");
            }
            else
            {
                Note(dbPath, "moment widget skipped (no photos)");
            }

            // 6c) Widget Plus (M1): Free không thấy; Plus bật thì cả ba hiện đúng và có nội dung; hết Plus lại ẩn.
            await controller.Settings_SetPlusForTestAsync(false);
            await controller.SyncExtraWidgetsAsync();
            Require(controller.Extras.Count == 0, "Free mà vẫn thấy widget Plus.");

            await controller.EnableExtrasForTestAsync();
            await Task.Delay(800);
            Require(controller.Extras.Count == 3 && controller.Extras.Values.All(w => w.IsVisible), "Plus bật đủ ba widget nhưng không hiện đủ.");
            Require(controller.Extras.ContainsKey(HabitGrid.Core.Domain.Widgets.ExtraWidget.MultiGrid), "Thiếu widget Multi Grid.");
            var shots = Path.Combine(Path.GetDirectoryName(dbPath)!, "shots");
            Directory.CreateDirectory(shots);

            foreach (var (kind, window) in controller.Extras)
            {
                var name = kind.ToString().ToLowerInvariant();
                Require(!window.IsExpanded, $"Widget {name} phải khởi đầu ở dạng nhỏ gọn.");
                window.UpdateLayout();
                SaveShot(window, Path.Combine(shots, $"widget-{name}-compact.png"));
                var compact = (window.Width, window.Height);

                window.SetExpanded(true, animate: false);
                await Task.Delay(300);
                window.UpdateLayout();
                Require(window.IsExpanded && window.Width > compact.Width && window.Height >= compact.Height, $"Widget {name} không mở rộng được (expanded={window.IsExpanded}, {window.Width}x{window.Height} so với nhỏ gọn {compact.Width}x{compact.Height}).");
                SaveShot(window, Path.Combine(shots, $"widget-{name}-expanded.png"));

                window.SetExpanded(false, animate: false);
                await Task.Delay(200);
                Require(!window.IsExpanded && Math.Abs(window.Width - compact.Width) < 1, $"Widget {name} không thu gọn lại được.");
            }

            // Ngữ cảnh động: Buddy đổi trạng thái theo giờ và tiến độ; Mục tiêu luân phiên; Multi Grid thu gọn còn tối đa 13 ngày.
            var sample = new HabitGrid.Presentation.Services.BuddyWidgetData("B", 5, HabitGrid.Presentation.ViewModels.BuddyStage.Baby, [], 1, 3, 0);
            var morning = new DateTime(2026, 11, 11, 9, 0, 0);
            var evening = new DateTime(2026, 11, 11, 20, 0, 0);
            Require(HabitGrid.WidgetHost.BuddyWidgetWindow.MoodFor(sample, morning) == HabitGrid.WidgetHost.BuddyWidgetWindow.Mood.Normal, "Sáng còn việc phải là Normal.");
            Require(HabitGrid.WidgetHost.BuddyWidgetWindow.MoodFor(sample, evening) == HabitGrid.WidgetHost.BuddyWidgetWindow.Mood.AtRisk, "Tối còn việc phải là AtRisk (sắp mất chuỗi).");
            Require(HabitGrid.WidgetHost.BuddyWidgetWindow.MoodFor(sample with { TodayDone = 3 }, evening) == HabitGrid.WidgetHost.BuddyWidgetWindow.Mood.AllDone, "Xong hết phải là AllDone.");
            Require(HabitGrid.WidgetHost.BuddyWidgetWindow.MoodFor(sample with { TodayDue = 0, TodayDone = 0 }, evening) == HabitGrid.WidgetHost.BuddyWidgetWindow.Mood.Normal, "Không có việc đến hạn thì không báo nguy cơ.");

            var rotation = HabitGrid.WidgetHost.GoalsWidgetWindow.Rotation(new HabitGrid.Presentation.Services.GoalsWidgetData(new HabitGrid.Presentation.Services.IntentProgress(Guid.NewGuid(), "A", "emoji:🏃", "#10B981", 1, 3), []));
            Require(rotation.Count == 1 && rotation[0].Fraction > 0.3, "Luân phiên Mục tiêu sai.");

            var longRow = new HabitGrid.Presentation.Services.MultiGridRow(Guid.NewGuid(), "H", "emoji:🏃", "#10B981", 3, HabitGrid.Core.Domain.StreakUnit.Day, Enumerable.Range(0, 91).Select(i => i < 85 ? 1 : -1).ToList());
            var last = HabitGrid.WidgetHost.MultiGridWidgetWindow.LastDays(longRow);
            Require(last.Count == 13 && last.All(v => v == 1), "Dạng nhỏ gọn của Multi Grid phải lấy 13 ngày gần nhất đến hôm nay.");

            // Lò xo biến hình (Dynamic Island): phải tới đích, vượt đích một chút (nảy) rồi lắng lại, không dao động vô hạn.
            var spring = new HabitGrid.WidgetHost.SpringValue();
            spring.Snap(92);
            spring.Target = 320;
            double peak = 0;
            var steps = 0;

            while (spring.Step(1.0 / 60) && steps++ < 600)
            {
                peak = Math.Max(peak, spring.Value);
            }

            Require(steps < 600 && Math.Abs(spring.Value - 320) < 0.1, "Lò xo biến hình không lắng lại ở đích.");
            Require(peak > 320 && peak < 320 * 1.12, $"Lò xo phải vượt đích nhẹ (nảy), đỉnh = {peak:0.0}.");

            // Hình màu của Buddy: đủ 5 giai đoạn và mọi phụ kiện trong danh mục phải nạp được (thiếu thì widget rơi về emoji đơn sắc).
            var spriteIds = Enum.GetValues<HabitGrid.Presentation.ViewModels.BuddyStage>().Select(HabitGrid.WidgetHost.BuddySprites.StageId)
                .Concat(HabitGrid.Core.Domain.Gamification.BuddyAccessories.All.Select(a => a.Id)).ToList();
            var missing = spriteIds.Where(id => HabitGrid.WidgetHost.BuddySprites.TryLoad(id) is null).ToList();
            Require(missing.Count == 0, "Thiếu hình Buddy: " + string.Join(", ", missing));

            await controller.Settings_SetPlusForTestAsync(false);
            await controller.SyncExtraWidgetsAsync();
            Require(controller.Extras.Count == 0, "Hết Plus mà widget Plus vẫn hiện.");
            Note(dbPath, "extra widgets ok (hidden for Free, 3 shown for Plus, hidden again)");

            // 6d) Một widget gộp (chế độ Hub): chỉ có đúng một cửa sổ; Free thấy Buddy + Hôm nay, Plus thêm Mục tiêu + lưới; bấm một việc thì check.
            await controller.Settings_SetPlusForTestAsync(true);
            await controller.SetLayoutForTestAsync(HabitGrid.Core.Domain.Widgets.WidgetLayoutMode.Hub);
            await Task.Delay(1500);
            var hub = controller.Hub ?? throw new InvalidOperationException("Chế độ Hub không dựng widget gộp.");
            Require(hub.IsVisible && controller.Windows.Count == 0 && controller.Extras.Count == 0 && controller.TodayList is null && controller.MomentWidget is null,
                "Chế độ Hub phải chỉ còn một widget gộp (không còn widget lưới/Today/Khoảnh khắc/Plus riêng).");
            Require(hub.Sections.HasFlag(HabitGrid.Core.Domain.Widgets.HubSection.Goals) && hub.Sections.HasFlag(HabitGrid.Core.Domain.Widgets.HubSection.Grid), "Plus phải thấy Mục tiêu và lưới trong widget gộp.");
            Require(!hub.IsExpanded && Math.Abs(hub.Width - HabitGrid.Core.Domain.Widgets.WidgetLayout.HubWidth) < 1, "Widget gộp phải khởi đầu ở dạng nhỏ gọn.");

            hub.UpdateLayout();
            SaveShot(hub, Path.Combine(shots, "hub-compact.png"));
            hub.SetExpanded(true, animate: false);
            await Task.Delay(400);
            hub.UpdateLayout();
            Require(hub.IsExpanded && hub.Height > 200, "Widget gộp không mở rộng được.");
            SaveShot(hub, Path.Combine(shots, "hub-expanded.png"));

            hub.SetExpanded(false, animate: false);

            await controller.Settings_SetPlusForTestAsync(false);
            await controller.SyncExtraWidgetsAsync();
            await Task.Delay(500);
            Require(controller.Hub is { } freeHub && !freeHub.Sections.HasFlag(HabitGrid.Core.Domain.Widgets.HubSection.Goals) && !freeHub.Sections.HasFlag(HabitGrid.Core.Domain.Widgets.HubSection.Grid), "Free không được thấy Mục tiêu/lưới trong widget gộp.");

            // Quay lại các widget riêng cho phần còn lại của bài kiểm tra.
            await controller.SetLayoutForTestAsync(HabitGrid.Core.Domain.Widgets.WidgetLayoutMode.Separate);
            await Task.Delay(1500);
            Require(controller.Hub is null && controller.Windows.Count > 0 && controller.TodayList is not null, "Chuyển về widget riêng phải dựng lại widget lưới và Today List.");
            Note(dbPath, "hub widget ok (single window, Free/Plus sections, expand, back to separate)");

            // 7) Focus: phiên đếm ngược 1 phút tự kết thúc và được ghi vào nhật ký.
            var before2 = (await controller.FocusService.HistoryAsync()).Count;
            await controller.Focus.StartAsync(HabitGrid.Core.Domain.Focus.FocusMode.Countdown, 1, null, keepHabit: false);
            await controller.Focus.TickAsync(DateTime.UtcNow.AddMinutes(1).AddSeconds(2));
            var after2 = (await controller.FocusService.HistoryAsync()).Count;
            Require(after2 == before2 + 1, "Phiên Focus hết giờ nhưng không được ghi.");
            Note(dbPath, $"focus sessions={after2} focusHotkey={controller.FocusHotKeyDescription ?? "none"}");

            File.AppendAllText(Path.Combine(Path.GetDirectoryName(dbPath)!, "widgethost-selftest.log"),
                $"hotkey={controller.HotKeyDescription ?? "none"}{Environment.NewLine}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SELFTEST FAILED: " + ex.Message);
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(dbPath)!, "widgethost-selftest.log"), ex + "\n");
            return 2;
        }
    }

    /// <summary>
    /// Kết thúc Explorer thật rồi chờ widget tự dựng lại và ghim lại. Trả 0 nếu đạt, 2 nếu hỏng, 5 nếu điều kiện đầu
    /// không có (máy không có desktop shell nên widget không ghim được ngay từ đầu, không đánh giá được).
    /// </summary>
    public static async Task<int> RunExplorerRestartAsync(WidgetController controller, string dbPath)
    {
        var log = Path.Combine(Path.GetDirectoryName(dbPath)!, "widgethost-selftest.log");

        void Note(string text) => File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}");

        try
        {
            await Task.Delay(2000);
            var before = controller.WindowHealth();
            Note($"before: alive={before.Alive}/{before.Total}, pinned={controller.AllPinnedToDesktop()}");

            if (before.Total == 0 || !controller.AllPinnedToDesktop())
            {
                Note("precondition failed: widgets not pinned to the desktop layer before the test");
                return 5;
            }

            Run("taskkill", "/f /im explorer.exe");
            Note("explorer killed");
            await Task.Delay(4000);

            if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0)
            {
                Note("shell did not restart by itself; starting explorer.exe");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }

            var recovered = false;

            for (var second = 0; second < 40 && !recovered; second++)
            {
                await Task.Delay(1000);
                var (alive, total) = controller.WindowHealth();
                recovered = total > 0 && alive == total && controller.AllPinnedToDesktop();

                if (second % 5 == 0)
                {
                    Note($"t+{second}s alive={alive}/{total} pinned={controller.AllPinnedToDesktop()}");
                }
            }

            Note(recovered ? "RECOVERED" : "NOT RECOVERED");
            return recovered ? 0 : 2;
        }
        catch (Exception ex)
        {
            Note("FAILED: " + ex);
            return 2;
        }
    }

    private static void Run(string file, string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
        process?.WaitForExit(10000);
    }

    private static void Note(string dbPath, string text) =>
        File.AppendAllText(Path.Combine(Path.GetDirectoryName(dbPath)!, "widgethost-selftest.log"), $"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
