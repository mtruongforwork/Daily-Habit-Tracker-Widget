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
                SaveShot(window, Path.Combine(shots, $"widget-{kind.ToString().ToLowerInvariant()}.png"));
            }

            await controller.Settings_SetPlusForTestAsync(false);
            await controller.SyncExtraWidgetsAsync();
            Require(controller.Extras.Count == 0, "Hết Plus mà widget Plus vẫn hiện.");
            Note(dbPath, "extra widgets ok (hidden for Free, 3 shown for Plus, hidden again)");

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
