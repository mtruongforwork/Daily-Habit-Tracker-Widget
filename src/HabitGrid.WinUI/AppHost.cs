using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HabitGrid.Core.Domain;
using HabitGrid.Data;
using HabitGrid.Data.V3;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using DayProvider = HabitGrid.Core.Services.DayProvider;
using IDayProvider = HabitGrid.Core.Services.IDayProvider;

namespace HabitGrid.WinUI;

/// <summary>Đường dẫn dữ liệu. Biến môi trường <c>HABITGRID_DATA_DIR</c> cho phép chạy thử trên thư mục riêng.</summary>
public static class AppPaths
{
    public const string DataDirVariable = "HABITGRID_DATA_DIR";

    public static bool IsCustomDataDir => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataDirVariable));

    public static string DataDir => IsCustomDataDir
        ? Path.GetFullPath(Environment.GetEnvironmentVariable(DataDirVariable)!)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HabitGrid");

    public static string LegacyDatabase => Path.Combine(DataDir, "habitgrid.db");

    public static string Database => Path.Combine(DataDir, HabitGridV3.FileName);

    public static string LogDir => Path.Combine(DataDir, "logs");

    /// <summary>Khóa single-instance; thư mục dữ liệu riêng có khóa riêng để không đụng bản đang chạy thật.</summary>
    public static string InstanceKey => IsCustomDataDir
        ? "HabitGrid.Main." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDir)))[..12]
        : "HabitGrid.Main";
}

/// <summary>Log ra file xoay theo ngày, giữ 7 ngày. Không bao giờ ném lỗi.</summary>
public sealed class FileLogger : IAppLogger
{
    private readonly string _directory;
    private readonly object _gate = new();

    public FileLogger(string directory)
    {
        _directory = directory;

        try
        {
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.GetFiles(directory, "habitgrid-*.log")
                         .OrderByDescending(f => f, StringComparer.Ordinal)
                         .Skip(7))
            {
                File.Delete(old);
            }
        }
        catch
        {
            // Log không được làm app lỗi.
        }
    }

    public string CurrentFile => Path.Combine(_directory, $"habitgrid-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(CurrentFile, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // nuốt lỗi ghi log
        }

        Debug.WriteLine($"[{level}] {message}");
    }
}

/// <summary>Đưa việc về luồng UI của WinUI.</summary>
public sealed class WinUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public WinUiDispatcher(DispatcherQueue queue) => _queue = queue;

    public void Post(Action action)
    {
        if (_queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _queue.TryEnqueue(() => action());
        }
    }
}

/// <summary>Composition root: dựng DI, mở CSDL v3, nhập dữ liệu v1 ở lần chạy đầu.</summary>
public sealed class AppHost : IDisposable
{
    private AppHost(ServiceProvider services, string? importNote)
    {
        Services = services;
        ImportNote = importNote;
    }

    public ServiceProvider Services { get; }

    /// <summary>Ghi chú về lần nhập dữ liệu cũ (nếu có), để hiển thị/ghi log.</summary>
    public string? ImportNote { get; }

    public static async Task<AppHost> CreateAsync(DispatcherQueue dispatcherQueue)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var logger = new FileLogger(AppPaths.LogDir);
        logger.Info($"Khởi động. Dữ liệu: {AppPaths.DataDir}");

        var deviceId = ReadOrCreateDeviceId();
        var clock = new HlcClock(deviceId);

        // Lần chạy đầu: dựng v3 từ CSDL v1/v2 (chỉ đọc, không sửa file cũ).
        string? importNote = null;
        if (!File.Exists(AppPaths.Database))
        {
            var result = await LegacyImporter.ImportAsync(AppPaths.LegacyDatabase, AppPaths.Database, clock);
            importNote = result.Imported
                ? $"Đã nhập {result.Habits} thói quen, {result.DayLogs} ghi nhận từ phiên bản cũ."
                : $"Không nhập dữ liệu cũ ({result.SkippedReason}).";
            logger.Info(importNote);
        }

        var database = HabitGridV3.Open(AppPaths.Database);
        var bus = new DataChangeBus();
        var localizer = new Localizer();
        var settings = new SettingsStore(database);

        // Ngôn ngữ: theo cài đặt đã lưu, nếu chưa có thì theo ngôn ngữ giao diện của Windows.
        var savedLanguage = await settings.GetAsync(SettingKeys.Language);
        localizer.SetLanguage(savedLanguage ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi" ? "vi" : "en"));
        // Quyền "sống": Free cho tới khi PlusService xác minh được Plus (từ cache offline hoặc Store).
        var entitlements = new HabitGrid.Core.Domain.Monetization.LiveEntitlements();
        var dayStartHour = int.TryParse(await settings.GetAsync(SettingKeys.DayStartHour), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour)
            ? Math.Clamp(hour, 0, 5)
            : 0;

        var services = new ServiceCollection();
        services.AddSingleton<IAppLogger>(logger);
        services.AddSingleton(database);
        services.AddSingleton<IHlcClock>(clock);
        services.AddSingleton<IDataChangeBus>(bus);
        services.AddSingleton<ILocalizer>(localizer);
        services.AddSingleton<IUiDispatcher>(new WinUiDispatcher(dispatcherQueue));
        services.AddSingleton<IHabitRepository>(_ => new HabitRepository(database, clock, bus));
        services.AddSingleton<IDayLogRepository>(_ => new DayLogRepository(database, clock, bus));
        services.AddSingleton<IRoutineRepository>(_ => new RoutineRepository(database, clock, bus));
        services.AddSingleton<VacationService>(sp => new VacationService(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IEntitlements>()));
        services.AddSingleton<ChallengeService>(sp => new ChallengeService(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IEntitlements>(),
            sp.GetRequiredService<IDayProvider>()));
        services.AddSingleton<WeeklyRecapService>(sp => new WeeklyRecapService(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IDayProvider>()));
        services.AddSingleton<DailyMoodService>(sp => new DailyMoodService(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IDayProvider>()));
        services.AddSingleton<RoutineService>(sp => new RoutineService(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IRoutineRepository>(),
            sp.GetRequiredService<IEntitlements>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<ISettingsStore>()));
        services.AddSingleton<RoutineTimerService>(sp => new RoutineTimerService(
            sp.GetRequiredService<RoutineService>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IEntitlements>()));
        services.AddSingleton<BuddyWardrobeService>(sp => new BuddyWardrobeService(
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IProfileRepository>(),
            sp.GetRequiredService<IAchievementRepository>(),
            sp.GetRequiredService<IEntitlements>(),
            sp.GetRequiredService<IDayProvider>()));
        services.AddSingleton<WrappedService>(sp => new WrappedService(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IEntitlements>(),
            sp.GetRequiredService<ISettingsStore>()));
        services.AddSingleton<BackupManager>(sp =>
        {
            var manager = new BackupManager(
                new SnapshotBackupService(
                    database,
                    clock,
                    sp.GetRequiredService<IHabitRepository>(),
                    sp.GetRequiredService<IDayLogRepository>(),
                    sp.GetRequiredService<IRoutineRepository>(),
                    sp.GetRequiredService<IProfileRepository>(),
                    bus,
                    sp.GetRequiredService<HabitGrid.Core.Domain.Journal.IJournalRepository>()),
                Path.Combine(AppPaths.DataDir, "backups"));

            // P3 (Plus): nhân bản sang thư mục cloud người dùng chọn. Đọc lại mỗi lần nên bật/tắt/đổi thư mục có hiệu lực ngay;
            // hết Plus thì ngừng nhân bản (file đã chép vẫn ở đó, không xóa gì).
            var live = sp.GetRequiredService<IEntitlements>();
            var store = sp.GetRequiredService<ISettingsStore>();
            manager.MirrorTarget = async () => live.IsPlus ? await store.GetAsync(BackupMirrorKey) : null;
            return manager;
        });
        services.AddSingleton<IProfileRepository>(_ => new ProfileRepository(database, clock, bus));
        services.AddSingleton<IAchievementRepository>(_ => new AchievementRepository(database, clock, bus));
        services.AddSingleton<IFocusRepository>(_ => new FocusRepository(database, clock, bus));
        services.AddSingleton<ISettingsStore>(settings);
        services.AddSingleton(new ThemeService(settings));
        services.AddSingleton<IEntitlements>(entitlements);
        services.AddSingleton(entitlements);
        services.AddSingleton<HabitGrid.Core.Domain.Monetization.IPlusStore>(_ => CreatePlusStore());
        services.AddSingleton<ILicenseCache>(_ => new Plus.DpapiLicenseCache(Path.Combine(AppPaths.DataDir, ".plus.dat")));
        services.AddSingleton<PlusService>(sp => new PlusService(
            sp.GetRequiredService<HabitGrid.Core.Domain.Monetization.IPlusStore>(),
            sp.GetRequiredService<ILicenseCache>(),
            entitlements,
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IHabitRepository>()));
        services.AddSingleton<IDayProvider>(_ => new DayProvider(TimeSpan.FromHours(dayStartHour)));
        services.AddSingleton<IStreakCalculator>(new StreakCalculator());
        services.AddSingleton<IStatsCalculator>(new StatsCalculator());
        services.AddSingleton<GamificationService>(sp => new GamificationService(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IProfileRepository>(),
            sp.GetRequiredService<IAchievementRepository>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IEntitlements>(),
            sp.GetRequiredService<IFocusRepository>()));
        services.AddSingleton(_ => new MomentStore(Path.Combine(AppPaths.DataDir, "moments")));
        services.AddSingleton<MomentService>(sp => new MomentService(
            sp.GetRequiredService<MomentStore>(),
            sp.GetRequiredService<IEntitlements>(),
            sp.GetRequiredService<IDayProvider>()));
        services.AddSingleton<ShareCardService>(sp => new ShareCardService(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IProfileRepository>(),
            sp.GetRequiredService<IEntitlements>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<MomentStore>()));
        services.AddSingleton<HabitGrid.Core.Domain.Journal.IJournalRepository>(_ => new JournalRepository(database));
        services.AddTransient<JournalModel>(sp => new JournalModel(
            sp.GetRequiredService<HabitGrid.Core.Domain.Journal.IJournalRepository>(),
            sp.GetRequiredService<IDayProvider>()));
        services.AddSingleton<FocusService>(sp => new FocusService(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IFocusRepository>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<ISettingsStore>()));
        services.AddSingleton<IFocusCommands, HostFocusCommands>();
        services.AddTransient<FocusPageModel>(sp => new FocusPageModel(
            sp.GetRequiredService<FocusService>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IFocusCommands>()));
        services.AddTransient<CalendarModel>(sp => new CalendarModel(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<ISettingsStore>()));
        services.AddTransient<InsightsViewModel>(sp => new InsightsViewModel(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IEntitlements>(),
            sp.GetRequiredService<ISettingsStore>()));
        services.AddTransient<BuddyViewModel>(sp => new BuddyViewModel(
            sp.GetRequiredService<GamificationService>(),
            sp.GetRequiredService<IProfileRepository>(),
            sp.GetRequiredService<IAchievementRepository>()));
        services.AddSingleton<HabitCreationService>(sp => new HabitCreationService(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IEntitlements>()));
        services.AddTransient<HabitEditorViewModel>(sp => new HabitEditorViewModel(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IEntitlements>()));
        services.AddTransient<OnboardingViewModel>(sp => new OnboardingViewModel(
            sp.GetRequiredService<HabitCreationService>(),
            sp.GetRequiredService<IProfileRepository>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<ILocalizer>()));
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<TodayViewModel>(sp => new TodayViewModel(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IStreakCalculator>(),
            null,
            sp.GetRequiredService<IDataChangeBus>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IAppLogger>(),
            sp.GetRequiredService<IProfileRepository>())
        {
            Settings = sp.GetRequiredService<ISettingsStore>()
        });
        services.AddSingleton<HabitsViewModel>(sp => new HabitsViewModel(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IStreakCalculator>(),
            sp.GetRequiredService<IStatsCalculator>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IAppLogger>()));
        services.AddTransient<HabitDetailViewModel>(sp => new HabitDetailViewModel(
            sp.GetRequiredService<IHabitRepository>(),
            sp.GetRequiredService<IDayLogRepository>(),
            sp.GetRequiredService<IDayProvider>(),
            sp.GetRequiredService<IStreakCalculator>(),
            sp.GetRequiredService<IStatsCalculator>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IAppLogger>()));

        return new AppHost(services.BuildServiceProvider(), importNote);
    }

    /// <summary>Khóa cài đặt: thư mục cloud nhận bản sao lưu nhân bản (Plus).</summary>
    public const string BackupMirrorKey = "backup.mirror.path";

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Store thật (chỉ chạy khi app có danh tính gói MSIX). Bản Debug có Store giả nếu đặt HABITGRID_FAKE_STORE=1.</summary>
    private static HabitGrid.Core.Domain.Monetization.IPlusStore CreatePlusStore()
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("HABITGRID_FAKE_STORE") == "1")
        {
            return new Plus.DebugFakePlusStore();
        }
#endif
        return new Plus.WindowsPlusStore();
    }

    public void Dispose() => Services.Dispose();

    // ------------------------------------------------------------------

    private static string ReadOrCreateDeviceId()
    {
        // Thiết bị chưa có CSDL v3 thì tạo id mới; importer sẽ ghi lại id này vào sync_state.
        if (File.Exists(AppPaths.Database))
        {
            try
            {
                using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = AppPaths.Database,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString());
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM sync_state WHERE key = 'device_id';";
                if (command.ExecuteScalar() is string id && id.Length > 0)
                {
                    return id;
                }
            }
            catch (SqliteException)
            {
                // CSDL chưa có sync_state: dùng id mới bên dưới.
            }
        }

        var idFile = Path.Combine(AppPaths.DataDir, "device-id");
        if (File.Exists(idFile))
        {
            var existing = File.ReadAllText(idFile).Trim();
            if (existing.Length > 0)
            {
                return existing;
            }
        }

        var created = $"win-{Guid.NewGuid():N}"[..16];
        File.WriteAllText(idFile, created);
        return created;
    }
}
