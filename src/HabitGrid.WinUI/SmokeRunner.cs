using IDayProvider = HabitGrid.Core.Services.IDayProvider;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Challenges;
using HabitGrid.Core.Domain.Insights;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.Templates;
using HabitGrid.Presentation.Theming;
using HabitGrid.Presentation.ViewModels;
using HabitGrid.WinUI.Controls;
using HabitGrid.WinUI.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HabitGrid.WinUI;

/// <summary>
/// Chế độ <c>--smoke</c>: chạy toàn bộ hành trình người dùng mới trên giao diện thật rồi xuất ảnh chụp từng bước:
/// onboarding → Hôm nay → check-in/hoàn tác → tạo thói quen → sửa thói quen → mọi trang.
/// Đây là bài kiểm tra "dựng XAML thật" mà bản v1 đã thiếu (SPEC-V2-11). Thoát 0 nếu đạt, khác 0 nếu có lỗi.
/// Từ chối chạy khi không đặt <c>HABITGRID_DATA_DIR</c> vì smoke test ghi dữ liệu.
/// </summary>
public static class SmokeRunner
{
    private static int _shotNo;

    public static async Task RunAsync(MainWindow window)
    {
        var logger = App.Host.Get<IAppLogger>();

        try
        {
            if (!AppPaths.IsCustomDataDir)
            {
                throw new InvalidOperationException(
                    $"Smoke test ghi dữ liệu nên bắt buộc đặt {AppPaths.DataDirVariable}; từ chối chạy trên dữ liệu thật.");
            }

            await Task.Delay(800);

            // Chụp ảnh cho hồ sơ Store: HABITGRID_SMOKE_SIZE=1600x1000 đổi cỡ cửa sổ, HABITGRID_SMOKE_LANG=vi|en đổi ngôn ngữ trước khi chụp.
            if (Environment.GetEnvironmentVariable("HABITGRID_SMOKE_SIZE")?.Split('x') is [var width, var height]
                && int.TryParse(width, out var w) && int.TryParse(height, out var h))
            {
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
                await Task.Delay(500);
            }

            if (Environment.GetEnvironmentVariable("HABITGRID_SMOKE_LANG") is { Length: > 0 } language)
            {
                await window.SetLanguageAsync(language);
            }

            GamificationCoordinator.DialogsEnabled = false;
            var celebrations = 0;
            App.Host.Get<GamificationService>().Celebrated += (_, _) => celebrations++;

            await OnboardingJourneyAsync(window, logger);
            await CheckInRoundTripAsync(window, logger);
            await EditorJourneyAsync(window, logger);

            foreach (var key in new[] { PageKeys.Habits, PageKeys.Insights, PageKeys.Buddy, PageKeys.Settings, PageKeys.Today })
            {
                await GoToAsync(window, key);
                await SnapAsync(window, $"page-{key}");
                logger.Info($"SMOKE page {key} OK");
            }

            await DetailJourneyAsync(window, logger);
            await BuddyJourneyAsync(window, logger, () => celebrations);
            await ShortcutsJourneyAsync(window, logger);
            await PlusJourneyAsync(window, logger);
            await InsightsJourneyAsync(window, logger);
            await MomentsJourneyAsync(window, logger);
            await BatchOneJourneyAsync(window, logger);
            await ChallengeJourneyAsync(window, logger);
            await WeeklyJourneyAsync(window, logger);
            await WrappedJourneyAsync(window, logger);
            await AccessibilityAuditAsync(window, logger);

            // Mọi theme × sáng/tối phải dựng được (ảnh để duyệt giao diện).
            var variants = new (ThemeDefinition Theme, ThemeMode Mode, string Name)[]
            {
                (Themes.CandyBento, ThemeMode.Dark, "candy-dark"),
                (Themes.CandyBento, ThemeMode.Light, "candy-light"),
                (Themes.NeonNight, ThemeMode.Dark, "neon"),
                (Themes.SoftPastel, ThemeMode.Light, "pastel")
            };

            foreach (var (theme, mode, name) in variants)
            {
                await window.SetThemeAsync(theme, mode);
                await GoToAsync(window, PageKeys.Today);
                await Task.Delay(500);
                await SnapAsync(window, $"theme-{name}");
                await GoToAsync(window, PageKeys.Habits);
                await Task.Delay(400);
                await SnapAsync(window, $"theme-{name}-habits");
                await GoToAsync(window, PageKeys.Settings);
                await Task.Delay(300);
                await SnapAsync(window, $"theme-{name}-settings");
            }

            await window.SetThemeAsync(Themes.CandyBento, ThemeMode.System);
            logger.Info("SMOKE themes OK");

            await SettingsJourneyAsync(window, logger);

            logger.Info("SMOKE OK");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            logger.Error("SMOKE FAILED", ex);
            Environment.Exit(2);
        }
    }

    // ------------------------------------------------------------------ onboarding

    private static async Task OnboardingJourneyAsync(MainWindow window, IAppLogger logger)
    {
        Require(window.IsOnboarding, "Dữ liệu trống nhưng không thấy onboarding.");
        var page = window.CurrentOnboardingPage ?? throw new InvalidOperationException("Không có trang onboarding.");
        var vm = page.ViewModel;
        await Task.Delay(500);
        await SnapAsync(window, "onboarding-1-welcome");

        await page.GoNextAsync();
        Require(vm.Step == OnboardingStep.Goals && page.IsPanelVisible(OnboardingStep.Goals), "Không sang được bước chủ đề.");
        Require(!vm.CanGoNext, "Chưa chọn chủ đề mà vẫn đi tiếp được.");
        vm.ToggleTopic(HabitTopic.Fitness);
        vm.ToggleTopic(HabitTopic.Mind);
        page.RefreshForSmoke();
        await Task.Delay(400);
        await SnapAsync(window, "onboarding-2-goals");

        await page.GoNextAsync();
        Require(vm.Step == OnboardingStep.Habits && page.IsPanelVisible(OnboardingStep.Habits), "Không sang được bước thói quen.");
        Require(vm.Suggestions.Count >= 16, "Thiếu gợi ý thói quen.");
        vm.TogglePick(HabitTemplates.Find("fit.gym")!);
        vm.TogglePick(HabitTemplates.Find("mind.meditate")!);
        page.RefreshForSmoke();
        await Task.Delay(700);
        await SnapAsync(window, "onboarding-3-habits");

        await page.GoNextAsync();
        Require(vm.Step == OnboardingStep.Buddy && page.IsPanelVisible(OnboardingStep.Buddy), "Không sang được bước Buddy.");
        vm.BuddyName = "Mochi";
        page.RefreshForSmoke();
        await Task.Delay(400);
        await SnapAsync(window, "onboarding-4-buddy");

        await page.GoNextAsync();
        Require(vm.Step == OnboardingStep.Reminder && page.IsPanelVisible(OnboardingStep.Reminder), "Không sang được bước nhắc nhở.");
        await Task.Delay(400);
        await SnapAsync(window, "onboarding-5-reminder");

        await page.FinishAsync();
        await Task.Delay(1200);

        Require(!window.IsOnboarding, "Onboarding xong nhưng vẫn ở chế độ onboarding.");
        var habits = await App.Host.Get<IHabitRepository>().GetAllActiveAsync();
        Require(habits.Count == 2, $"Kỳ vọng 2 thói quen sau onboarding, thực tế {habits.Count}.");
        Require((await App.Host.Get<IProfileRepository>().GetAsync()).BuddyName == "Mochi", "Tên Buddy không được lưu.");
        Require(await App.Host.Get<ISettingsStore>().GetAsync(SettingKeys.OnboardingDone) == "1", "Chưa đánh dấu onboarding xong.");
        Require(window.CurrentPageKey == PageKeys.Today, "Sau onboarding không vào trang Hôm nay.");
        await SnapAsync(window, "today-after-onboarding");
        logger.Info("SMOKE onboarding OK");
    }

    // ------------------------------------------------------------------ check-in

    private static async Task CheckInRoundTripAsync(MainWindow window, IAppLogger logger)
    {
        var logs = App.Host.Get<IDayLogRepository>();
        var today = App.Host.Get<TodayViewModel>();

        await today.LoadAsync();
        await Task.Delay(500);

        var pending = today.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Kind == HabitKind.Boolean)
                      ?? throw new InvalidOperationException("Không có thói quen kiểu có/không để check-in.");

        var doneBefore = today.DoneCount;
        var button = FindCards(window.Content).FirstOrDefault(c => c.Item?.Id == pending.Id)
                     ?? throw new InvalidOperationException("Không tìm thấy nút check trên giao diện.");

        await button.PerformClickAsync();
        await Task.Delay(900);

        var saved = await logs.GetAsync(pending.Id, today.Today)
                    ?? throw new InvalidOperationException("Bấm nút nhưng DB không có ghi nhận.");

        Require(saved.Status == DayStatus.Done && today.DoneCount == doneBefore + 1,
            $"Sau check-in kỳ vọng Done và DoneCount {doneBefore + 1}, thực tế {saved.Status} / {today.DoneCount}.");
        await SnapAsync(window, "today-checked");
        logger.Info("SMOKE check-in OK");

        await today.UndoAsync();
        await Task.Delay(500);
        Require(await logs.GetAsync(pending.Id, today.Today) is null && today.DoneCount == doneBefore, "Hoàn tác không đưa dữ liệu về trạng thái trước.");
        logger.Info("SMOKE undo OK");

        // Trạng thái dở dang của thẻ số (thanh tiến độ), rồi hoàn thành cả ngày (hero ăn mừng), rồi xem ngày cũ.
        var numeric = today.Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Kind is HabitKind.Duration or HabitKind.Count);
        if (numeric is not null)
        {
            await today.SetValueAsync(numeric, Math.Max(1, numeric.Target / 3));
            await Task.Delay(700);
            AssertCardsMatchViewModel(window, today);
        await SnapAsync(window, "today-partial");
            Require(numeric.Status != DayStatus.Done && numeric.Progress > 0, "Thẻ số không hiện tiến độ dở dang.");
        }

        foreach (var item in today.Groups.SelectMany(g => g.Items).Where(i => !i.IsDone && i.Kind != HabitKind.Quit).ToList())
        {
            var card = FindCards(window.Content).First(c => c.Item?.Id == item.Id);
            await card.PerformClickAsync();
            await Task.Delay(500);
        }

        await Task.Delay(700);
        Require(today.AllDone, "Check hết mà chưa AllDone.");
        Require(today.BuddyMood == BuddyMood.Ecstatic, "Buddy không ăn mừng khi xong hết.");
        AssertCardsMatchViewModel(window, today);
        await SnapAsync(window, "today-all-done");
        logger.Info("SMOKE all-done OK");

        await today.SelectDayAsync(today.Today.AddDays(-1));
        await Task.Delay(700);
        Require(today.IsViewingPast, "Không chuyển sang xem ngày cũ.");
        await SnapAsync(window, "today-past-day");
        await today.SelectDayAsync(today.Today);
        await Task.Delay(400);
        logger.Info("SMOKE past-day OK");
    }

    // ------------------------------------------------------------------ tạo / sửa thói quen

    private static async Task EditorJourneyAsync(MainWindow window, IAppLogger logger)
    {
        var habits = App.Host.Get<IHabitRepository>();
        var root = window.Content as FrameworkElement ?? throw new InvalidOperationException("Cửa sổ chưa có nội dung.");

        // --- tạo mới ---
        var createTask = HabitEditorDialog.ShowAsync(root.XamlRoot);
        await Task.Delay(700);
        var view = FindEditorView(root) ?? throw new InvalidOperationException("Hộp thoại tạo thói quen không mở được.");

        view.ViewModel.Name = "Đọc sách (smoke)";
        view.ViewModel.Kind = HabitKind.Duration;
        view.ViewModel.Target = 25;
        view.ViewModel.FrequencyType = FrequencyType.WeekDays;
        view.ViewModel.WeekDays = Core.Models.DayOfWeekMask.Monday | Core.Models.DayOfWeekMask.Wednesday | Core.Models.DayOfWeekMask.Friday;
        view.ViewModel.TimeOfDay = TimeOfDay.Evening;
        view.ViewModel.ColorHue = "#EC4899";
        view.ViewModel.AddReminder();
        await Task.Delay(600);
        await SnapElementAsync(view.FullContent, "editor-new");

        Require(await view.TrySaveAsync(), "Lưu thói quen mới thất bại.");
        CloseDialogs(root);
        Require(await createTask is not null, "Hộp thoại không trả về thói quen đã lưu.");

        var created = (await habits.GetAllActiveAsync()).FirstOrDefault(h => h.Name == "Đọc sách (smoke)")
                      ?? throw new InvalidOperationException("Thói quen mới không có trong DB.");
        Require(created.Kind == HabitKind.Duration && created.Target.Value == 25, "Loại/mục tiêu không được lưu.");
        Require(created.Frequency is HabitFrequency.WeekDays, "Tần suất không được lưu.");
        Require(created.Reminders.Count == 1, "Nhắc nhở không được lưu.");
        logger.Info("SMOKE editor create OK");

        // --- dữ liệu không hợp lệ phải được chặn ---
        var invalidTask = HabitEditorDialog.ShowAsync(root.XamlRoot);
        await Task.Delay(600);
        var invalidView = FindEditorView(root) ?? throw new InvalidOperationException("Không mở được hộp thoại lần hai.");
        Require(!await invalidView.TrySaveAsync(), "Lưu thói quen không tên lại thành công.");
        await Task.Delay(300);
        await SnapElementAsync(invalidView.FullContent, "editor-error");
        CloseDialogs(root);
        await invalidTask;
        logger.Info("SMOKE editor validation OK");

        // --- sửa ---
        var editTask = HabitEditorDialog.ShowAsync(root.XamlRoot, created);
        await Task.Delay(700);
        var editView = FindEditorView(root) ?? throw new InvalidOperationException("Hộp thoại sửa không mở được.");
        Require(editView.ViewModel.IsEditing && editView.ViewModel.Name == "Đọc sách (smoke)", "Hộp thoại sửa không nạp đúng dữ liệu.");
        editView.ViewModel.Name = "Đọc sách (đã sửa)";
        editView.ViewModel.FrequencyType = FrequencyType.TimesPerWeek;
        editView.ViewModel.TimesPerWeek = 4;
        await Task.Delay(400);
        await SnapElementAsync(editView.FullContent, "editor-edit");

        Require(await editView.TrySaveAsync(), "Lưu chỉnh sửa thất bại.");
        CloseDialogs(root);
        await editTask;

        var edited = await habits.GetByIdAsync(created.Id);
        Require(edited!.Name == "Đọc sách (đã sửa)" && edited.Frequency is HabitFrequency.TimesPerWeek { Times: 4 }, "Chỉnh sửa không được lưu.");
        Require(edited.Id == created.Id, "Sửa không được đổi Id.");
        logger.Info("SMOKE editor edit OK");
    }

    // ------------------------------------------------------------------ trang Thói quen + chi tiết

    private static async Task DetailJourneyAsync(MainWindow window, IAppLogger logger)
    {
        await SeedHistoryAsync();
        await GoToAsync(window, PageKeys.Habits);
        await Task.Delay(500);

        var cards = FindAll<HabitCollectionCard>(window.Content).Where(c => c.Item is not null).ToList();
        var habits = App.Host.Get<HabitsViewModel>();
        Require(cards.Count > 0 && cards.Count == habits.Items.Count,
            $"Trang Thói quen hiển thị {cards.Count} thẻ nhưng view model có {habits.Items.Count}.");
        Require(cards.All(c => c.Item!.Heatmap is { Weeks.Count: HabitsViewModel.HeatmapWeeks }), "Thẻ thiếu heatmap 12 tuần.");
        await SnapAsync(window, "habits-collection");

        cards[0].Open();
        await Task.Delay(900);

        var page = window.CurrentPage as HabitDetailPage
                   ?? throw new InvalidOperationException("Bấm thẻ thói quen không mở trang chi tiết.");
        await page.Loaded2;
        await Task.Delay(300);

        var vm = page.ViewModel;
        Require(vm.Habit?.Id == cards[0].Item!.Id, "Trang chi tiết mở sai thói quen.");
        Require(vm.Heatmap is { Weeks.Count: HabitDetailViewModel.HeatmapWeeks }, "Chi tiết thiếu heatmap 52 tuần.");
        Require(FindAll<HeatmapGrid>(window.Content).Any(h => h.IsInteractive && h.Data is not null), "Heatmap chi tiết chưa được vẽ.");

        var day = vm.Today.AddDays(-1);

        if (day >= vm.Habit!.StartDate)
        {
            var ok = await vm.SaveDayAsync(day, Math.Max(1, vm.Habit.EffectiveTarget), false, "Ghi chú từ smoke test", 4);
            Require(ok, "Không lưu được ngày trong trang chi tiết: " + vm.ErrorMessage);
            page.Render();
            Require(vm.Journal.Any(j => j.Note == "Ghi chú từ smoke test"), "Nhật ký không hiện ghi chú vừa lưu.");
            await Task.Delay(300);
        }

        await SnapAsync(window, "habit-detail");
        logger.Info("SMOKE habit detail OK");
    }

    private static async Task SettingsJourneyAsync(MainWindow window, IAppLogger logger)
    {
        var theme = App.Host.Get<ThemeService>();
        var loc = App.Host.Get<ILocalizer>();
        var originalLanguage = loc.Language;

        await GoToAsync(window, PageKeys.Settings);
        Require(window.CurrentPage is SettingsPage, "Trang Cài đặt không dựng được.");

        // Giảm chuyển động: áp dụng ngay và nhớ lựa chọn.
        await theme.SetReduceMotionAsync(true);
        Require(!theme.AnimationsEnabled, "Bật giảm chuyển động nhưng hiệu ứng vẫn chạy.");
        await theme.SetReduceMotionAsync(false);

        // Đổi ngôn ngữ lúc chạy: chữ đổi ngay, rồi trả về như cũ.
        var other = originalLanguage == "en" ? "vi" : "en";
        var before = Ui.T("settings.title");
        await window.SetLanguageAsync(other);
        await Task.Delay(500);
        Require(Ui.T("settings.title") != before, "Đổi ngôn ngữ nhưng chữ không đổi.");
        await SnapAsync(window, "settings-other-language");
        await window.SetLanguageAsync(originalLanguage);
        await Task.Delay(300);
        Require(Ui.T("settings.title") == before, "Không trả lại được ngôn ngữ ban đầu.");

        logger.Info("SMOKE settings OK");
    }

    /// <summary>Đợt 1: sao lưu, tạm nghỉ, cảm xúc, routine — chạy thật trên dữ liệu smoke rồi dọn lại.</summary>
    private static async Task BatchOneJourneyAsync(MainWindow window, IAppLogger logger)
    {
        // F5 Sao lưu: sao lưu ngay phải tạo file đọc lại được; sao lưu tự động chỉ một lần mỗi ngày.
        var backups = App.Host.Get<HabitGrid.Data.V3.BackupManager>();
        var file = await backups.BackupNowAsync();
        Require(File.Exists(file.Path) && file.SizeBytes > 100, "Sao lưu ngay không tạo được file.");
        var manifest = await HabitGrid.Data.V3.SnapshotBackupService.ReadManifestAsync(file.Path);
        Require(manifest.Habits > 0 && manifest.DayLogs > 0, "File sao lưu không chứa dữ liệu.");
        Require(await backups.RunIfDueAsync() is not null && await backups.RunIfDueAsync() is null, "Tự sao lưu phải chạy đúng một lần mỗi ngày.");

        // F3 Tạm nghỉ: đánh dấu rồi kết thúc sớm phải trả dữ liệu về như cũ.
        var vacation = App.Host.Get<VacationService>();
        var habits = (await App.Host.Get<IHabitRepository>().GetAllActiveAsync()).Where(h => h.Kind != HabitKind.Quit).ToList();
        Require(habits.Count >= 3, $"Cần ít nhất 3 thói quen để thử routine và tạm nghỉ, có {habits.Count}.");
        var paused = habits.Last();
        var today = App.Host.Get<IDayProvider>().GetToday();
        var logs = App.Host.Get<IDayLogRepository>();
        var before = await logs.GetAsync(paused.Id, today.AddDays(1));
        var (check, marked) = await vacation.StartAsync(today.AddDays(1), today.AddDays(2), [paused.Id]);
        Require(check.IsValid && marked > 0, $"Không bắt đầu được tạm nghỉ: {check.Error}");
        Require((await logs.GetAsync(paused.Id, today.AddDays(1)))?.Status == DayStatus.Frozen, "Ngày nghỉ không được đánh dấu.");
        Require(await vacation.EndAsync() == marked, "Kết thúc tạm nghỉ không gỡ hết ngày đã đánh dấu.");
        Require((await logs.GetAsync(paused.Id, today.AddDays(1)))?.Status == before?.Status, "Dữ liệu không trở về như cũ sau khi kết thúc tạm nghỉ.");

        // F1 Routine: tạo từ hai thói quen, dải routine hiện trên Hôm nay, "xong cả routine" hoàn thành mọi bước.
        var routines = App.Host.Get<RoutineService>();
        var members = habits.Where(h => h.RoutineId is null).Take(2).ToList();
        Require(members.Count == 2, "Không đủ hai thói quen để tạo routine.");
        var created = await routines.CreateAsync(new RoutineDraft("Sáng", HabitIcon.Emoji("🌅"), TimeOfDay.Morning, members.Select(m => m.Id).ToList()));
        Require(created.Succeeded, $"Không tạo được routine: {created.Error}");

        await GoToAsync(window, PageKeys.Today);
        await Task.Delay(900);
        var strip = FindAll<RoutineStrip>(window.Content).FirstOrDefault() ?? throw new InvalidOperationException("Không thấy dải routine.");
        Require(strip.HasItems, "Dải routine trống dù đã có routine.");

        // P4 Hẹn giờ: đang Plus tặng nên dựng được bộ chạy cho hai bước; chạy hết giờ thì cả hai bước hoàn thành.
        var runner = await App.Host.Get<RoutineTimerService>().BuildRunnerAsync(created.Routine!.Id)
            ?? throw new InvalidOperationException("Không dựng được bộ chạy routine hẹn giờ.");
        Require(runner.Steps.Count == 2, "Bộ chạy routine phải có đúng hai bước chưa xong.");
        var t0 = DateTime.UtcNow;
        runner.Start(t0);
        Require(runner.Tick(t0.AddHours(1)).Count == 2 && runner.IsFinished, "Hết giờ phải hoàn thành cả hai bước.");

        // F4 Cảm xúc: chọn một mức, kiểm tra đã lưu, rồi bỏ chọn.
        var mood = FindAll<MoodBar>(window.Content).FirstOrDefault() ?? throw new InvalidOperationException("Không thấy thanh cảm xúc.");
        await mood.PickForTestAsync(4);
        Require((await App.Host.Get<DailyMoodService>().GetAsync(today))?.Mood == 4, "Cảm xúc không được lưu.");
        await SnapAsync(window, "today-routine-mood");
        await mood.PickForTestAsync(4);
        Require(await App.Host.Get<DailyMoodService>().GetAsync(today) is null, "Bỏ chọn cảm xúc không xóa dữ liệu.");

        var done = await routines.CompleteAllAsync(created.Routine!.Id);
        Require((await routines.ProgressAsync()).Single(p => p.Routine.Id == created.Routine.Id).IsComplete, "Xong cả routine nhưng chưa hoàn thành mọi bước.");
        await routines.DeleteAsync(created.Routine.Id);
        Require(!(await routines.ListAsync()).Any(), "Xóa routine không thành công.");
        logger.Info($"SMOKE batch1 OK (backup {file.SizeBytes} bytes, vacation {marked} days, routine steps done {done}, mood saved)");
    }

    /// <summary>F2: bắt đầu thử thách, dải thử thách hiện trên Hôm nay với tiến độ "Ngày 1/N"; rồi dọn để không ảnh hưởng các bước sau.</summary>
    private static async Task ChallengeJourneyAsync(MainWindow window, IAppLogger logger)
    {
        var service = App.Host.Get<ChallengeService>();
        var started = await service.StartAsync("read10", Ui.T("challenge.read10"), 30);
        Require(started.Succeeded, $"Không bắt đầu được thử thách: {started.Error}");

        await GoToAsync(window, PageKeys.Today);
        await Task.Delay(900);
        var strip = FindAll<ChallengeStrip>(window.Content).FirstOrDefault() ?? throw new InvalidOperationException("Không thấy dải thử thách.");
        Require(strip.HasItems, "Dải thử thách trống dù đã bắt đầu thử thách.");
        await SnapAsync(window, "today-challenge");

        var board = await service.RefreshAsync();
        Require(board.Items.Single().State == ChallengeState.Active && board.Items.Single().DayNumber == 1, "Thử thách mới phải ở ngày 1 và đang chạy.");

        // Dọn: xóa thói quen của thử thách để không lẫn vào các bước kiểm sau.
        await App.Host.Get<IHabitRepository>().SoftDeleteAsync(started.Challenge!.HabitId);
        Require((await service.RefreshAsync()).Items.Count == 0, "Xóa thói quen nhưng thử thách vẫn còn chạy.");
        logger.Info("SMOKE challenge OK");
    }

    /// <summary>F6: chọn ý định tuần (chạy mọi ngày), rồi dựng thẻ tổng kết tuần trước từ dữ liệu mẫu để chụp ảnh.</summary>
    private static async Task WeeklyJourneyAsync(MainWindow window, IAppLogger logger)
    {
        var service = App.Host.Get<WeeklyRecapService>();
        var repo = App.Host.Get<IHabitRepository>();
        var habits = (await repo.GetAllActiveAsync()).Where(h => h.Kind != HabitKind.Quit).ToList();
        Require(await service.SetIntentAsync(habits[0].Id), "Không chọn được ý định tuần.");

        await GoToAsync(window, PageKeys.Today);
        await Task.Delay(900);
        var strip = FindAll<WeeklyCardStrip>(window.Content).FirstOrDefault() ?? throw new InvalidOperationException("Không thấy thẻ tuần.");
        Require(strip.HasItems, "Ý định tuần đã chọn nhưng thẻ không hiện.");
        Require((await service.GetAsync()).Intent?.HabitId == habits[0].Id, "Ý định tuần không được lưu.");

        var today = App.Host.Get<IDayProvider>().GetToday();
        var logsRepo = App.Host.Get<IDayLogRepository>();
        var logs = new List<DayLog>();
        foreach (var habit in habits)
        {
            logs.AddRange(await logsRepo.GetRangeAsync(habit.Id, today.AddDays(-28), today));
        }

        var monday = HabitGrid.Core.Domain.PeriodCalendar.StartOfWeek(today, DayOfWeek.Monday);
        var recap = HabitGrid.Core.Domain.Insights.WeeklyRecapBuilder.Build(habits, logs, monday);
        Require(recap.CheckIns > 0, "Dữ liệu mẫu không có check-in tuần trước để tổng kết.");
        strip.Render(new WeeklyCard(recap, (await service.GetAsync()).Intent, habits.Take(4).ToList()));
        await Task.Delay(300);
        await SnapAsync(window, "today-weekly");

        await service.SetIntentAsync(null);
        await strip.ReloadAsync();
        logger.Info($"SMOKE weekly OK (rate {recap.Rate:P0}, checkins {recap.CheckIns}, tip {recap.Tip})");
    }

    /// <summary>P1: số liệu tháng này từ dữ liệu mẫu, rồi render từng slide thành PNG 1080×1920 (lưu vài ảnh để duyệt).</summary>
    private static async Task WrappedJourneyAsync(MainWindow window, IAppLogger logger)
    {
        var service = App.Host.Get<WrappedService>();
        var data = await service.BuildAsync(0);
        Require(!data.IsEmpty && data.TopHabit is not null, "Wrapped tháng này trống dù dữ liệu mẫu có check-in.");
        Require(WrappedSlides.CanView(5, service.IsPlus), "Đang Plus tặng mà không xem được đủ slide.");

        Directory.CreateDirectory(Shot.Directory);
        var sizes = new List<int>();

        foreach (var kind in WrappedSlides.All)
        {
            var png = await HabitGrid.WinUI.Moments.WrappedDialog.RenderSlidePngForTestAsync(FindAll<Panel>(window.Content).First(), kind, data, withPhoto: false);
            Require(png.Length > 8_000, $"Ảnh Wrapped slide {kind} quá nhỏ ({png.Length} byte).");
            sizes.Add(png.Length);

            if (kind is WrappedSlideKind.Total or WrappedSlideKind.TopHabit or WrappedSlideKind.GoldenHour)
            {
                await File.WriteAllBytesAsync(Path.Combine(Shot.Directory, $"{++_shotNo:00}-wrapped-{kind}.png"), png);
            }
        }

        // Mở trang Insights để chắc nút Wrapped có tên cho trình đọc màn hình (kiểm trợ năng chạy sau đó).
        await GoToAsync(window, PageKeys.Insights);
        logger.Info($"SMOKE wrapped OK (checkins {data.TotalCheckIns}, top {data.TopHabit!.Name}, perfect {data.PerfectDays}, png {string.Join('/', sizes)})");
    }

    private static async Task MomentsJourneyAsync(MainWindow window, IAppLogger logger)
    {
        var habit = App.Host.Get<HabitsViewModel>().Items.First();
        await window.RunCommandAsync(new PaletteCommand(PaletteKind.OpenHabit, habit.Name, habit.Id.ToString()));
        await Task.Delay(900);
        var page = window.CurrentPage as HabitDetailPage ?? throw new InvalidOperationException("Không mở được trang chi tiết để thêm ảnh.");

        // Ảnh JPEG thật (dải màu) tạo bằng bộ mã hóa của Windows.
        var jpeg = await MakeJpegAsync();
        Require(page.AddMomentForTest(jpeg) == MomentAddResult.Added, "Không thêm được ảnh khoảnh khắc.");
        Require(page.AddMomentForTest(jpeg) == MomentAddResult.Added, "Đang Plus tặng mà vẫn bị giới hạn 1 ảnh/ngày.");
        Require(page.AddMomentForTest(jpeg, 1) == MomentAddResult.Added && page.AddMomentForTest(jpeg, 2) == MomentAddResult.Added && page.AddMomentForTest(jpeg, 3) == MomentAddResult.Added, "Không thêm được ảnh các ngày trước.");
        Require(App.Host.Get<MomentService>().Store.ForHabit(habit.Id).Count == 5, "Kho ảnh không có đủ 5 ảnh.");
        await Task.Delay(600);
        await SnapAsync(window, "habit-moments");

        // Thẻ chia sẻ: mở hộp thoại, render PNG 1080×1920 như nút Lưu ảnh.
        var root = window.Content.XamlRoot;
        var showing = page.OpenShareForTestAsync();
        await Task.Delay(1500);
        var card = HabitGrid.WinUI.Moments.ShareCardDialog.CurrentCard ?? throw new InvalidOperationException("Thẻ chia sẻ không dựng được.");
        var png = await HabitGrid.WinUI.Moments.MomentCapture.RenderPngAsync(card, 1080, 1920);
        Require(png.Length > 10_000, $"Ảnh thẻ chia sẻ quá nhỏ ({png.Length} byte).");
        Directory.CreateDirectory(Shot.Directory);
        await File.WriteAllBytesAsync(Path.Combine(Shot.Directory, $"{++_shotNo:00}-share-card.png"), png);

        // P7: dải 4 ảnh (đang Plus tặng) phải dựng được và khác thẻ một ảnh.
        var select = HabitGrid.WinUI.Moments.ShareCardDialog.SelectLayoutForTest ?? throw new InvalidOperationException("Hộp thoại thẻ chia sẻ không cho chọn bố cục.");
        await select(HabitGrid.Core.Domain.Moments.ShareLayout.Strip4);
        await Task.Delay(1200);
        var stripCard = HabitGrid.WinUI.Moments.ShareCardDialog.CurrentCard ?? throw new InvalidOperationException("Dải 4 ảnh không dựng được.");
        var stripPng = await HabitGrid.WinUI.Moments.MomentCapture.RenderPngAsync(stripCard, 1080, 1920);
        Require(stripPng.Length > 10_000 && stripPng.Length != png.Length, "Dải 4 ảnh giống hệt thẻ một ảnh.");
        await File.WriteAllBytesAsync(Path.Combine(Shot.Directory, $"{++_shotNo:00}-share-strip.png"), stripPng);

        var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Select(p => p.Child).OfType<ContentDialog>().FirstOrDefault();
        dialog?.Hide();
        await showing;
        logger.Info($"SMOKE moments OK (5 photos, share card {png.Length} bytes, strip {stripPng.Length} bytes)");
    }

    private static async Task<byte[]> MakeJpegAsync()
    {
        const int size = 320;
        var pixels = new byte[size * size * 4];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                pixels[i] = (byte)(120 + x / 3);
                pixels[i + 1] = (byte)(60 + y / 4);
                pixels[i + 2] = (byte)(200 - x / 4);
                pixels[i + 3] = 255;
            }
        }

        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, size, size, 96, 96, pixels);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes), (uint)bytes.Length, Windows.Storage.Streams.InputStreamOptions.None);
        return bytes;
    }

    private static async Task InsightsJourneyAsync(MainWindow window, IAppLogger logger)
    {
        await GoToAsync(window, PageKeys.Insights);
        var page = window.CurrentPage as InsightsPage ?? throw new InvalidOperationException("Không mở được trang Insights.");
        await page.Ready;
        await Task.Delay(400);

        var vm = page.ViewModel;
        Require(vm.ErrorMessage is null, "Insights báo lỗi: " + vm.ErrorMessage);
        Require(vm.Report is { IsEmpty: false, CheckIns: > 0 }, "Insights trống dù đã có check-in.");
        Require(vm.Insights.Count <= 3, "Insights hiện quá 3 gợi ý.");
        Require(vm.Insights.All(i => !InsightsPage.Describe(i).Contains('{')), "Có gợi ý còn tham số chưa thay.");
        await SnapAsync(window, "insights");

        // Đang Plus tặng: xem được toàn bộ lịch sử và xuất CSV.
        Require(await vm.SetRangeAsync(InsightsRange.All), "Plus mà không xem được toàn bộ lịch sử.");
        var csv = await vm.ExportCsvAsync();
        Require(csv is not null && csv.StartsWith("date,habit,kind", StringComparison.Ordinal), "Xuất CSV không đúng định dạng.");
        Require(vm.Advanced is { YearHeatmap.Weeks.Count: 52 }, "Plus mà Insights nâng cao trống.");
        Require(vm.Habits.Count >= 2 && vm.Compare(vm.Habits[0].Id, vm.Habits[1].Id) is not null, "So sánh hai thói quen không chạy.");
        await Task.Delay(200);
        page.ScrollToAdvancedForTest(toEnd: true);
        await Task.Delay(400);
        await SnapAsync(window, "insights-advanced");
        page.ScrollToAdvancedForTest(toEnd: false);

        // P5: màu nhấn tùy chỉnh áp ngay khi đang Plus, bỏ khi đặt lại.
        var themeService = App.Host.Get<ThemeService>();
        themeService.AccentAllowed = true;
        Require(await themeService.SetAccentAsync("#22C55E", window.Content as FrameworkElement), "Không đặt được màu nhấn.");
        // P5 phông chữ: lựa chọn được ghi ra file và từ điển phông dựng ở lần mở sau lấy đúng phông.
        FontResources.WriteChoice("serif");
        Require(FontResources.ReadChoice() == "serif" && ((Microsoft.UI.Xaml.Media.FontFamily)new FontResources()["DisplayFont"]).Source == "Cambria", "Lựa chọn phông không được áp vào từ điển phông.");
        FontResources.WriteChoice(HabitGrid.Core.Domain.AppFonts.DefaultId);
        Require(FontResources.ReadChoice() == HabitGrid.Core.Domain.AppFonts.DefaultId, "Chọn phông mặc định không xóa lựa chọn cũ.");
        Require(!await themeService.SetAccentAsync("xyz", window.Content as FrameworkElement), "Mã màu sai mà vẫn được nhận.");
        var accentBrush = (Microsoft.UI.Xaml.Media.LinearGradientBrush)Application.Current.Resources["AccentGradientBrush"];
        Require(accentBrush.GradientStops[0].Color == Ui.ParseColor("#22C55E"), "Màu nhấn tùy chỉnh chưa được áp vào gradient.");
        await GoToAsync(window, PageKeys.Settings);
        await Task.Delay(300);
        await SnapAsync(window, "settings-accent");
        Require(await themeService.SetAccentAsync(null, window.Content as FrameworkElement), "Không đặt lại được màu nhấn.");
        Require(accentBrush.GradientStops[0].Color != Ui.ParseColor("#22C55E"), "Đặt lại màu nhấn chưa trả về màu theme.");
        await GoToAsync(window, PageKeys.Insights);
        logger.Info($"SMOKE insights OK (check-ins {vm.Report!.CheckIns}, tips {vm.Insights.Count}, csv {csv!.Length} bytes)");
    }

    /// <summary>
    /// Kiểm tra trợ năng tự động: mọi control tương tác đang hiện trên các trang chính phải có tên đọc được (Narrator đọc tên này).
    /// Bỏ qua phần bên trong khuôn mẫu của control hệ thống (thanh cuộn, nút xóa trong ô nhập…), vốn do WinUI đặt tên.
    /// </summary>
    private static async Task AccessibilityAuditAsync(MainWindow window, IAppLogger logger)
    {
        var problems = new List<string>();

        foreach (var key in new[] { PageKeys.Today, PageKeys.Habits, PageKeys.Insights, PageKeys.Buddy, PageKeys.Settings })
        {
            await GoToAsync(window, key);
            await Task.Delay(700);

            if (window.CurrentPage is InsightsPage insights)
            {
                await insights.Ready;
            }
            else if (window.CurrentPage is BuddyPage buddy)
            {
                await buddy.Ready;
            }

            await Task.Delay(300);
            problems.AddRange(UnnamedControls(window.Content, key));
        }

        // Trang chi tiết thói quen.
        var first = App.Host.Get<HabitsViewModel>().Items.FirstOrDefault();

        if (first is not null)
        {
            await window.RunCommandAsync(new PaletteCommand(PaletteKind.OpenHabit, first.Name, first.Id.ToString()));
            await Task.Delay(900);
            problems.AddRange(UnnamedControls(window.Content, "detail"));
        }

        foreach (var problem in problems)
        {
            logger.Info("A11Y " + problem);
        }

        Require(problems.Count == 0, $"{problems.Count} control không có tên cho trình đọc màn hình (xem log A11Y).");
        logger.Info("SMOKE accessibility OK");
    }

    private static IEnumerable<string> UnnamedControls(DependencyObject? root, string page)
    {
        foreach (var element in FindAll<FrameworkElement>(root))
        {
            if (element is not (Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or ToggleSwitch or TextBox or ComboBox or Slider or NumberBox or TimePicker or CalendarDatePicker or AutoSuggestBox or PasswordBox))
            {
                continue;
            }

            if (element is Microsoft.UI.Xaml.Controls.Primitives.RepeatButton || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || IsTemplatePart(element))
            {
                continue;
            }

            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(element)
                       ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(element);
            var name = peer?.GetName();

            if (string.IsNullOrWhiteSpace(name))
            {
                yield return $"{page}: {element.GetType().Name} '{element.Name}' in {Describe(VisualTreeHelper.GetParent(element))}";
            }
        }
    }

    private static bool IsTemplatePart(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar or TextBox or NumberBox or ComboBox or TimePicker or CalendarDatePicker
                or AutoSuggestBox or Slider or ToggleSwitch or CalendarView or InfoBar or Expander)
            {
                return true;
            }

            if (parent is Page or ContentDialog)
            {
                return false;
            }
        }

        return false;
    }

    private static string Describe(DependencyObject? parent) => parent switch
    {
        null => "?",
        FrameworkElement { Name.Length: > 0 } named => $"{named.GetType().Name} '{named.Name}'",
        _ => parent.GetType().Name
    };

    private static async Task PlusJourneyAsync(MainWindow window, IAppLogger logger)
    {
        var plus = App.Host.Get<PlusService>();
        Require(!plus.StoreAvailable, "Bản không đóng gói không được coi là có Microsoft Store.");
        // Chưa mở bán trên Store: người dùng mới được tặng Plus 30 ngày (quyền cục bộ, không phải mua).
        Require(plus.Status.Tier == HabitGrid.Core.Domain.Monetization.PlusTier.Promo, $"Người dùng mới phải được tặng Plus, đang là {plus.Status.Tier}.");
        Require(plus.PromoDaysLeft is 29 or 30, $"Thời gian tặng phải còn 30 ngày, đang còn {plus.PromoDaysLeft}.");
        Require(App.Host.Get<IEntitlements>().MaxActiveHabits == int.MaxValue, "Đang Plus tặng thì không giới hạn thói quen.");

        var root = window.Content.XamlRoot;
        var showing = HabitGrid.WinUI.Plus.PaywallDialog.ShowAsync(root, PaywallSource.Settings);
        await Task.Delay(1000);

        var dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Select(p => p.Child).OfType<ContentDialog>().FirstOrDefault()
                     ?? throw new InvalidOperationException("Paywall không mở được.");
        var buyButtons = FindAll<Button>(dialog).Where(b => Equals(b.Content, Ui.T("plus.buy"))).ToList();
        Require(buyButtons.Count == 3, $"Paywall phải có 3 gói, thấy {buyButtons.Count}.");
        Require(buyButtons.All(b => !b.IsEnabled), "Không có Store mà nút Mua vẫn bấm được.");

        dialog.Hide();
        await showing;
        logger.Info($"SMOKE plus OK (promo {plus.PromoDaysLeft} days left, no store, paywall builds, buy disabled)");
    }

    private static async Task ShortcutsJourneyAsync(MainWindow window, IAppLogger logger)
    {
        // Lệnh palette: chuyển trang.
        await window.RunCommandAsync(new PaletteCommand(PaletteKind.Page, "settings", PageKeys.Settings));
        await Task.Delay(500);
        Require(window.CurrentPage is SettingsPage, "Lệnh palette không mở được Cài đặt.");

        // Phím 1 trên trang Hôm nay: check dòng đầu tiên.
        await GoToAsync(window, PageKeys.Today);
        var today = App.Host.Get<TodayViewModel>();
        await today.LoadAsync();
        var first = today.Groups.SelectMany(g => g.Items).First();
        var wasDone = first.IsDone;
        var value = first.Value;
        await ((TodayPage)window.CurrentPage!).CheckRowAsync(0);
        await Task.Delay(400);
        var after = today.Groups.SelectMany(g => g.Items).First(i => i.Id == first.Id);
        Require(after.IsDone != wasDone || after.Value != value, "Phím 1 không check được dòng đầu tiên.");

        // Lệnh palette: check một thói quen theo tên.
        var target = today.Groups.SelectMany(g => g.Items).First(i => i.Kind == HabitKind.Boolean && i.Id != first.Id);
        var before = target.IsDone;
        await window.RunCommandAsync(new PaletteCommand(PaletteKind.CheckHabit, target.Name, target.Id.ToString()));
        await Task.Delay(400);
        Require(today.Groups.SelectMany(g => g.Items).First(i => i.Id == target.Id).IsDone != before, "Lệnh palette Check không đổi trạng thái thói quen.");

        // Lệnh palette: mở chi tiết thói quen.
        await window.RunCommandAsync(new PaletteCommand(PaletteKind.OpenHabit, target.Name, target.Id.ToString()));
        await Task.Delay(700);
        Require(window.CurrentPage is HabitDetailPage, "Lệnh palette không mở được trang chi tiết.");
        logger.Info("SMOKE shortcuts OK");
    }

    private static async Task BuddyJourneyAsync(MainWindow window, IAppLogger logger, Func<int> celebrations)
    {
        await GoToAsync(window, PageKeys.Buddy);
        var page = window.CurrentPage as BuddyPage ?? throw new InvalidOperationException("Không mở được trang Buddy.");
        await page.Ready;
        await Task.Delay(500);

        var vm = page.ViewModel;
        Require(vm.ErrorMessage is null, "Trang Buddy báo lỗi: " + vm.ErrorMessage);
        Require(vm.Badges.Count == HabitGrid.Core.Domain.Gamification.AchievementCatalog.Default.Count, "Trang Buddy thiếu huy hiệu.");
        Require(vm.Level >= 1 && vm.TotalXp > 0, "Buddy không có XP dù đã check-in nhiều lần.");
        Require(vm.Badges.Any(b => b.Id == "first_checkin" && b.Unlocked), "Huy hiệu 'first_checkin' chưa mở dù đã check-in.");
        Require(vm.Badges.Where(b => b.Hidden).All(b => b.Group == "secret"), "Có huy hiệu thường bị ẩn.");
        Require(celebrations() >= 1, "Không có lần ăn mừng nào dù đã mở khóa huy hiệu sau lần tính đầu.");
        await SnapAsync(window, "buddy");

        // P6: đang Plus tặng nên đeo được món Plus (mũ chóp cao) và món mở bằng huy hiệu/cấp; chụp ảnh Buddy đã đeo đồ rồi tháo hết.
        Require(await page.ToggleAccessoryForTestAsync("tophat") == HabitGrid.Presentation.Services.EquipResult.Equipped, "Plus mà không đeo được món Plus.");
        Require(await page.ToggleAccessoryForTestAsync("tophat") == HabitGrid.Presentation.Services.EquipResult.Removed, "Không tháo được phụ kiện.");
        Require(await page.ToggleAccessoryForTestAsync("tophat") == HabitGrid.Presentation.Services.EquipResult.Equipped, "Không đeo lại được phụ kiện.");
        await page.ToggleAccessoryForTestAsync("shades");
        await page.ToggleAccessoryForTestAsync("ribbon");
        await Task.Delay(300);
        await SnapAsync(window, "buddy-accessories");
        await page.ToggleAccessoryForTestAsync("tophat");
        await page.ToggleAccessoryForTestAsync("shades");
        await page.ToggleAccessoryForTestAsync("ribbon");
        logger.Info($"SMOKE buddy OK (level {vm.Level}, xp {vm.TotalXp}, badges {vm.UnlockedCount}/{vm.TotalCount}, celebrations {celebrations()})");
    }

    /// <summary>Lùi ngày bắt đầu của các thói quen có/không và ghi lịch sử mẫu để ảnh chụp có heatmap thật.</summary>
    private static async Task SeedHistoryAsync()
    {
        var habitsRepo = App.Host.Get<IHabitRepository>();
        var logs = App.Host.Get<IDayLogRepository>();
        var today = App.Host.Get<IDayProvider>().GetToday();
        var seed = 7;

        foreach (var habit in (await habitsRepo.GetAllActiveAsync()).Where(h => h.Kind == HabitKind.Boolean).Take(3))
        {
            habit.StartDate = today.AddDays(-120);
            await habitsRepo.UpdateAsync(habit);

            for (var back = 1; back <= 110; back++)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;

                if (seed % 100 < 68)
                {
                    var day = today.AddDays(-back);
                    await logs.UpsertAsync(DayLogRules.Normalize(habit, new DayLog { HabitId = habit.Id, Day = day, Value = 1 }));
                }
            }
        }

        await App.Host.Get<HabitsViewModel>().LoadAsync();
    }

    // ------------------------------------------------------------------ tiện ích

    private static IEnumerable<T> FindAll<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null)
        {
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static async Task GoToAsync(MainWindow window, string key)
    {
        window.NavigateTo(key);
        await Task.Delay(700);
        Require(window.CurrentPageKey == key, $"Không chuyển được tới trang '{key}'.");
    }

    private static async Task SnapAsync(MainWindow window, string name)
    {
        if (window.Content is UIElement content)
        {
            await Shot.SaveAsync(content, $"{++_shotNo:00}-{name}");
        }
    }

    private static async Task SnapElementAsync(UIElement element, string name) =>
        await Shot.SaveAsync(element, $"{++_shotNo:00}-{name}");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static HabitEditorView? FindEditorView(FrameworkElement root)
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
        {
            if (popup.Child is ContentDialog { Content: HabitEditorView view })
            {
                return view;
            }
        }

        return null;
    }

    private static void CloseDialogs(FrameworkElement root)
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
        {
            if (popup.Child is ContentDialog dialog)
            {
                dialog.Hide();
            }
        }
    }

    /// <summary>Mỗi thẻ trên màn hình phải khớp trạng thái ViewModel (bắt lỗi thẻ "mất kết nối" với dữ liệu).</summary>
    private static void AssertCardsMatchViewModel(MainWindow window, TodayViewModel today)
    {
        var cards = FindCards(window.Content).Where(c => c.Item is not null).ToList();
        Require(cards.Count >= today.Groups.Sum(g => g.Items.Count), "Số thẻ trên màn hình ít hơn ViewModel.");

        foreach (var card in cards)
        {
            var item = card.Item!;
            Require(
                card.IsShowingDone == item.IsDone,
                $"Thẻ '{item.Name}' hiển thị {(card.IsShowingDone ? "xong" : "chưa xong")} nhưng ViewModel là {(item.IsDone ? "xong" : "chưa xong")}.");

            if (item.Kind is HabitKind.Count or HabitKind.Duration && item.Value > 0)
            {
                var vmItem = today.Groups.SelectMany(g => g.Items).First(i => i.Id == item.Id);
                Require(
                    card.DisplayedSubText.Contains(item.Value.ToString()),
                    $"Thẻ '{item.Name}' không hiển thị giá trị {item.Value}. Thẻ: [{card.Diagnostics}]; cùng đối tượng với VM: {ReferenceEquals(vmItem, item)} (vmHash={vmItem.GetHashCode()} vmValue={vmItem.Value}).");
            }
        }
    }

    private static IEnumerable<HabitCard> FindCards(DependencyObject? parent)
    {
        if (parent is null)
        {
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is HabitCard card)
            {
                yield return card;
            }

            foreach (var nested in FindCards(child))
            {
                yield return nested;
            }
        }
    }
}
