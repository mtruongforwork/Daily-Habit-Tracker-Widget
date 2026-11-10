using FluentAssertions;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Monetization;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;

namespace HabitGrid.Presentation.Tests;

/// <summary>Store giả cho test: đếm số lần mua để chứng minh không có đường nào mua ngoài nút Mua.</summary>
public sealed class FakePlusStore : IPlusStore
{
    public bool IsAvailable { get; set; } = true;

    public List<OwnedAddOn> Owned { get; } = [];

    public int PurchaseCalls { get; private set; }

    public PurchaseOutcome NextOutcome { get; set; } = PurchaseOutcome.Succeeded;

    public bool ThrowOnOwned { get; set; }

    public Task<IReadOnlyList<PlusProduct>> GetProductsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlusProduct>>(
        [
            new(PlusPlan.Monthly, PlusProducts.Monthly, "39.000 ₫", false, false),
            new(PlusPlan.Yearly, PlusProducts.Yearly, "249.000 ₫", true, false),
            new(PlusPlan.Lifetime, PlusProducts.Lifetime, "499.000 ₫", false, false)
        ]);

    public Task<IReadOnlyList<OwnedAddOn>> GetOwnedAsync(CancellationToken ct = default) =>
        ThrowOnOwned ? throw new HttpRequestException("offline") : Task.FromResult<IReadOnlyList<OwnedAddOn>>(Owned.ToList());

    public Task<PurchaseOutcome> PurchaseAsync(PlusPlan plan, IntPtr ownerWindow, CancellationToken ct = default)
    {
        PurchaseCalls++;

        if (NextOutcome == PurchaseOutcome.Succeeded)
        {
            Owned.Add(new OwnedAddOn(PlusProducts.StoreIdFor(plan), true, plan == PlusPlan.Lifetime ? null : DateTime.UtcNow.AddDays(30)));
        }

        return Task.FromResult(NextOutcome);
    }
}

public class PlusServiceTests
{
    private static (PlusService Service, FakePlusStore Store, LiveEntitlements Live, MemoryLicenseCache Cache) Create(AppFixture f, Func<DateTime>? now = null)
    {
        var store = new FakePlusStore();
        var live = new LiveEntitlements();
        var cache = new MemoryLicenseCache();
        return (new PlusService(store, cache, live, f.Settings, f.Habits, now) { PromoEnabled = false }, store, live, cache);
    }

    [Fact]
    public async Task FreshUser_IsFree_WithFiveHabitLimit()
    {
        using var f = new AppFixture();
        var (service, _, live, _) = Create(f);

        await service.InitializeAsync();

        service.Status.IsPlus.Should().BeFalse();
        live.MaxActiveHabits.Should().Be(5);
    }

    [Fact]
    public async Task LegacyProV1_GetsPlusOnFirstLaunch()
    {
        using var f = new AppFixture();
        var (service, store, live, cache) = Create(f);
        store.Owned.Add(new OwnedAddOn(PlusProducts.LegacyPro, true));

        await service.InitializeAsync();

        service.Status.Tier.Should().Be(PlusTier.LegacyPro);
        live.IsPlus.Should().BeTrue();
        cache.Load()!.Tier.Should().Be(PlusTier.LegacyPro);
    }

    [Fact]
    public async Task Purchase_Succeeds_UnlocksPlus_AndRestoreReadsItBack()
    {
        using var f = new AppFixture();
        var (service, store, live, _) = Create(f);
        await service.InitializeAsync();

        (await service.PurchaseAsync(PlusPlan.Yearly, IntPtr.Zero)).Should().Be(PurchaseOutcome.Succeeded);
        live.IsPlus.Should().BeTrue();

        // Thiết bị mới: chỉ có collection trên Store → Khôi phục mua hàng.
        var (fresh, freshStore, freshLive, _) = Create(f);
        freshStore.Owned.AddRange(store.Owned);
        (await fresh.RestoreAsync()).Should().BeTrue();
        freshLive.IsPlus.Should().BeTrue();
    }

    [Fact]
    public async Task Cancelled_Purchase_ChangesNothing()
    {
        using var f = new AppFixture();
        var (service, store, live, _) = Create(f);
        store.NextOutcome = PurchaseOutcome.Cancelled;

        (await service.PurchaseAsync(PlusPlan.Monthly, IntPtr.Zero)).Should().Be(PurchaseOutcome.Cancelled);
        live.IsPlus.Should().BeFalse();
    }

    [Fact]
    public async Task NoStore_NeverGrantsPlus()
    {
        using var f = new AppFixture();
        var (service, store, live, _) = Create(f);
        store.IsAvailable = false;

        (await service.PurchaseAsync(PlusPlan.Lifetime, IntPtr.Zero)).Should().Be(PurchaseOutcome.StoreUnavailable);
        store.PurchaseCalls.Should().Be(0);
        live.IsPlus.Should().BeFalse();
    }

    [Fact]
    public async Task Offline_SixDaysWithSubscription_StillPlus_DayEight_AsksToRecheck_NoDataLocked()
    {
        using var f = new AppFixture();
        var clock = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        var (service, store, live, cache) = Create(f, () => clock);
        store.Owned.Add(new OwnedAddOn(PlusProducts.Monthly, true, clock.AddDays(30)));
        await service.InitializeAsync();

        // Mất mạng: Store không trả lời, chỉ còn cache.
        store.ThrowOnOwned = true;

        clock = clock.AddDays(6);
        var day6 = new PlusService(store, cache, live, f.Settings, f.Habits, () => clock) { PromoEnabled = false };
        await day6.InitializeAsync();
        day6.Status.Should().Match<LicenseStatus>(s => s.IsPlus && !s.NeedsRecheck);

        clock = clock.AddDays(2);
        var day8 = new PlusService(store, cache, live, f.Settings, f.Habits, () => clock) { PromoEnabled = false };
        await day8.InitializeAsync();
        day8.Status.Should().Match<LicenseStatus>(s => s.IsPlus && s.NeedsRecheck);
    }

    [Fact]
    public async Task Downgrade_With9Habits_UserChooses5_Other4ReadOnly_NoDataLost()
    {
        using var f = new AppFixture();
        var habits = new List<Habit>();
        for (var i = 0; i < 9; i++) habits.Add(await f.AddAsync($"H{i}", startDaysAgo: 10));
        foreach (var h in habits) await f.LogAsync(h, AppFixture.Today.AddDays(-1), 1);
        var (service, _, _, _) = Create(f);
        await service.InitializeAsync();

        var before = await service.GetAllowanceAsync();
        before.NeedsChoice.Should().BeTrue();

        var chosen = habits.Where((_, i) => i % 2 == 0).Select(h => h.Id).ToList();
        chosen.Should().HaveCount(5);
        await service.ChooseRunningAsync(chosen);

        var readOnly = await ReadOnlyHabits.LoadAsync(f.Settings);
        readOnly.Should().BeEquivalentTo(habits.Where((_, i) => i % 2 == 1).Select(h => h.Id));
        (await f.Habits.GetAllActiveAsync()).Should().HaveCount(9);
        foreach (var h in habits) (await f.Logs.GetAsync(h.Id, AppFixture.Today.AddDays(-1))).Should().NotBeNull();
    }

    [Fact]
    public async Task ReadOnlyHabits_CannotBeCheckedIn_Anywhere()
    {
        using var f = new AppFixture();
        var habits = new List<Habit>();
        for (var i = 0; i < 6; i++) habits.Add(await f.AddAsync($"H{i}", startDaysAgo: 10));
        var (service, _, _, _) = Create(f);
        await service.InitializeAsync();
        var locked = habits[5];
        (await ReadOnlyHabits.LoadAsync(f.Settings)).Should().ContainSingle().Which.Should().Be(locked.Id);

        // Widget / palette / toast dùng CheckInService.
        (await new CheckInService(f.Habits, f.Logs, f.Settings).ToggleAsync(locked.Id, AppFixture.Today)).Should().BeNull();
        (await f.Logs.GetAsync(locked.Id, AppFixture.Today)).Should().BeNull();

        // Quick check-in / Today List không liệt kê.
        var quick = new QuickCheckInModel(f.Habits, f.Logs, settings: f.Settings);
        await quick.LoadAsync(AppFixture.Today);
        quick.Items.Should().NotContain(i => i.Id == locked.Id);

        // Trang Hôm nay: thẻ hiện chỉ đọc, chạm không ghi.
        var today = new TodayViewModel(f.Habits, f.Logs, f.Days) { Settings = f.Settings };
        await today.LoadAsync();
        var item = today.Groups.SelectMany(g => g.Items).Single(i => i.Id == locked.Id);
        item.IsReadOnly.Should().BeTrue();
        await today.CheckInAsync(item);
        (await f.Logs.GetAsync(locked.Id, AppFixture.Today)).Should().BeNull();
        today.ErrorMessage.Should().Be("plan.readonly");
    }

    [Fact]
    public async Task UpgradingAgain_ClearsReadOnly()
    {
        using var f = new AppFixture();
        for (var i = 0; i < 7; i++) await f.AddAsync($"H{i}", startDaysAgo: 3);
        var (service, store, _, _) = Create(f);
        await service.InitializeAsync();
        (await ReadOnlyHabits.LoadAsync(f.Settings)).Should().HaveCount(2);

        await service.PurchaseAsync(PlusPlan.Lifetime, IntPtr.Zero);

        (await ReadOnlyHabits.LoadAsync(f.Settings)).Should().BeEmpty();
    }
}

public class PaywallTests
{
    private sealed class NoProductsStore : IPlusStore
    {
        public bool IsAvailable => true;

        public Task<IReadOnlyList<PlusProduct>> GetProductsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PlusProduct>>([]);

        public Task<IReadOnlyList<OwnedAddOn>> GetOwnedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OwnedAddOn>>([]);

        public Task<PurchaseOutcome> PurchaseAsync(PlusPlan plan, IntPtr ownerWindow, CancellationToken ct = default) => Task.FromResult(PurchaseOutcome.Failed);
    }

    [Fact]
    public async Task WhenTheStoreHasNoPlusProductsYet_PurchasesAreNotOpened()
    {
        using var f = new AppFixture();
        var service = new PlusService(new NoProductsStore(), new MemoryLicenseCache(), new LiveEntitlements(), f.Settings, f.Habits) { PromoEnabled = false };
        var vm = new PaywallViewModel(service, PaywallSource.Settings);

        await service.InitializeAsync();
        await vm.LoadAsync();

        vm.StoreAvailable.Should().BeTrue();
        vm.HasStoreProducts.Should().BeFalse();
        vm.PurchasesOpen.Should().BeFalse("không hiện nút mua cho sản phẩm chưa tồn tại");
    }

    [Fact]
    public async Task WhenTheStoreHasProducts_PurchasesAreOpened()
    {
        using var f = new AppFixture();
        var service = new PlusService(new FakePlusStore(), new MemoryLicenseCache(), new LiveEntitlements(), f.Settings, f.Habits) { PromoEnabled = false };
        var vm = new PaywallViewModel(service, PaywallSource.Settings);

        await service.InitializeAsync();
        await vm.LoadAsync();

        vm.PurchasesOpen.Should().BeTrue();
    }

    [Fact]
    public async Task OpeningPaywall_LoadingPrices_AndRestoring_NeverPurchase()
    {
        using var f = new AppFixture();
        var store = new FakePlusStore();
        var service = new PlusService(store, new MemoryLicenseCache(), new LiveEntitlements(), f.Settings, f.Habits) { PromoEnabled = false };
        var vm = new PaywallViewModel(service, PaywallSource.HabitLimit);

        await service.InitializeAsync();
        await vm.LoadAsync();
        await vm.RestoreAsync();

        store.PurchaseCalls.Should().Be(0);
        vm.Plans.Select(p => p.Plan).Should().Equal(PlusPlan.Monthly, PlusPlan.Yearly, PlusPlan.Lifetime);
        vm.Plans.Single(p => p.Plan == PlusPlan.Yearly).Should().Match<PaywallPlan>(p => p.HasTrial && p.IsBestValue && p.Price == "249.000 ₫");
        vm.PricesAreReference.Should().BeFalse();
        vm.MessageKey.Should().Be("plus.msg.nothing");
    }

    [Fact]
    public async Task Buy_IsTheOnlyPathThatPurchases()
    {
        using var f = new AppFixture();
        var store = new FakePlusStore();
        var service = new PlusService(store, new MemoryLicenseCache(), new LiveEntitlements(), f.Settings, f.Habits) { PromoEnabled = false };
        var vm = new PaywallViewModel(service, PaywallSource.Settings);
        await vm.LoadAsync();

        var outcome = await vm.BuyAsync(PlusPlan.Yearly, IntPtr.Zero);

        outcome.Should().Be(PurchaseOutcome.Succeeded);
        store.PurchaseCalls.Should().Be(1);
        vm.IsPlus.Should().BeTrue();
        vm.MessageKey.Should().Be("plus.msg.thanks");
    }

    [Fact]
    public async Task WithoutStore_ShowsReferencePrices_AndBuyExplainsWhy()
    {
        using var f = new AppFixture();
        var store = new FakePlusStore { IsAvailable = false };
        var service = new PlusService(store, new MemoryLicenseCache(), new LiveEntitlements(), f.Settings, f.Habits) { PromoEnabled = false };
        var vm = new PaywallViewModel(service, PaywallSource.Theme);

        await vm.LoadAsync();
        var outcome = await vm.BuyAsync(PlusPlan.Monthly, IntPtr.Zero);

        vm.StoreAvailable.Should().BeFalse();
        outcome.Should().Be(PurchaseOutcome.StoreUnavailable);
        vm.MessageKey.Should().Be("plus.msg.nostore");
        vm.IsPlus.Should().BeFalse();
    }

    /// <summary>
    /// SPEC-V2-10 tiêu chí 5: quét mã nguồn — chỉ nút Mua của paywall (PaywallViewModel.BuyAsync, gọi từ PaywallDialog)
    /// được gọi mua hàng; hiện thực Store là nơi duy nhất gọi API mua của Windows.
    /// </summary>
    [Fact]
    public void Source_OnlyThePaywallBuyButtonCanStartAPurchase()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "HabitGrid.sln")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("cần thư mục gốc repo để quét mã");
        var files = Directory.EnumerateFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(p => !p.Contains("HabitGrid.Platform") && !p.Contains("HabitGrid.UI") && !p.Contains("HabitGrid.App"))
            .ToList();

        static string Name(string path) => Path.GetFileName(path);

        var storeCalls = files.Where(p => File.ReadAllText(p).Contains("RequestPurchaseAsync(")).Select(Name).ToList();
        storeCalls.Should().BeEquivalentTo(["WindowsPlusStore.cs"], "chỉ hiện thực Store gọi API mua của Windows");

        var serviceCalls = files.Where(p => File.ReadAllText(p).Contains("_plus.PurchaseAsync(")).Select(Name).ToList();
        serviceCalls.Should().BeEquivalentTo(["PaywallViewModel.cs"], "chỉ PaywallViewModel.BuyAsync gọi mua qua PlusService");

        var buyCalls = files.Where(p => File.ReadAllText(p).Contains(".BuyAsync(")).Select(Name).ToList();
        buyCalls.Should().BeEquivalentTo(["PaywallDialog.cs"], "chỉ nút Mua trên paywall gọi BuyAsync");
    }
}

public class PlusPromoTests
{
    private static PlusService Create(AppFixture f, FakePlusStore store, Func<DateTime> now, LiveEntitlements? live = null) =>
        new(store, new MemoryLicenseCache(), live ?? new LiveEntitlements(), f.Settings, f.Habits, now) { PromoEnabled = true };

    [Fact]
    public async Task NewUser_Gets30DaysOfPlus_FromFirstLaunch()
    {
        using var f = new AppFixture();
        var now = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
        var live = new LiveEntitlements();
        var service = Create(f, new FakePlusStore { IsAvailable = false }, () => now, live);

        await service.InitializeAsync();

        service.Status.Tier.Should().Be(PlusTier.Promo);
        live.IsPlus.Should().BeTrue();
        service.PromoDaysLeft.Should().Be(30);
    }

    [Fact]
    public async Task Promo_DoesNotRestart_OnLaterLaunches_AndEndsAfter30Days_WithoutLosingData()
    {
        using var f = new AppFixture();
        for (var i = 0; i < 7; i++) await f.AddAsync($"H{i}", startDaysAgo: 3);
        var now = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
        var store = new FakePlusStore { IsAvailable = false };

        await Create(f, store, () => now).InitializeAsync();
        (await ReadOnlyHabits.LoadAsync(f.Settings)).Should().BeEmpty("đang Plus tặng thì không giới hạn");

        now = now.AddDays(20);
        var day20 = Create(f, store, () => now);
        await day20.InitializeAsync();
        day20.PromoDaysLeft.Should().Be(10);
        day20.Status.IsPlus.Should().BeTrue();

        now = now.AddDays(11);
        var day31 = Create(f, store, () => now);
        await day31.InitializeAsync();
        day31.Status.IsPlus.Should().BeFalse();
        (await ReadOnlyHabits.LoadAsync(f.Settings)).Should().HaveCount(2, "hết tặng: 7 thói quen → 2 chỉ đọc, không xóa gì");
        (await f.Habits.GetAllActiveAsync()).Should().HaveCount(7);
    }

    [Fact]
    public async Task RealStorePurchase_TakesPrecedenceOverThePromo()
    {
        using var f = new AppFixture();
        var now = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
        var store = new FakePlusStore();
        store.Owned.Add(new OwnedAddOn(PlusProducts.Lifetime, true));

        var service = Create(f, store, () => now);
        await service.InitializeAsync();

        service.Status.Tier.Should().Be(PlusTier.Lifetime);
    }

    [Fact]
    public async Task Restore_WithNothingOnTheStore_KeepsThePromo()
    {
        using var f = new AppFixture();
        var now = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
        var service = Create(f, new FakePlusStore(), () => now);
        await service.InitializeAsync();

        await service.RestoreAsync();

        service.Status.Tier.Should().Be(PlusTier.Promo);
    }

    [Fact]
    public void Policy_PromoEndsExactlyAtItsExpiry()
    {
        var start = new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
        var state = PlusPromo.StateFor(start);

        LicensePolicy.Evaluate(state, start.AddDays(30)).IsPlus.Should().BeTrue();
        LicensePolicy.Evaluate(state, start.AddDays(30).AddSeconds(1)).IsPlus.Should().BeFalse();
        LicensePolicy.Evaluate(state, start.AddDays(29)).NeedsRecheck.Should().BeFalse();
    }
}
