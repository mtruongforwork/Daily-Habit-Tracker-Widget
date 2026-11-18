namespace HabitGrid.Core.Domain.Monetization;

/// <summary>Gói đang có. <see cref="LegacyPro"/> = người mua Pro v1 (add-on <c>pro_unlock</c>), được Plus trọn đời.</summary>
public enum PlusTier
{
    None,
    Monthly,
    Yearly,
    Lifetime,
    LegacyPro,

    /// <summary>Plus miễn phí có thời hạn do app tặng (chưa bán trên Store). Hết hạn thì về Free, dữ liệu giữ nguyên.</summary>
    Promo
}

/// <summary>
/// Chương trình tặng Plus miễn phí khi chưa mở bán trên Store. Tắt bằng cách đặt <see cref="Enabled"/> = false
/// khi các gói trả phí đã sẵn sàng; người đang trong thời gian tặng vẫn dùng hết số ngày còn lại.
/// </summary>
public static class PlusPromo
{
    /// <summary>
    /// Tắt từ v3: các gói trả phí đã có dùng thử của Store (Tháng 7 ngày, Năm 30 ngày), tặng thêm sẽ thành dùng thử chồng dùng thử.
    /// Người đã bắt đầu thời gian tặng trước đó vẫn dùng hết số ngày còn lại (ngày bắt đầu đã lưu).
    /// </summary>
    public const bool Enabled = false;
    public const int Days = 30;

    /// <summary>Quyền tặng tính từ lần đầu mở app (hoặc lần đầu có chương trình).</summary>
    public static LicenseState StateFor(DateTime startedAtUtc) =>
        new(PlusTier.Promo, startedAtUtc, startedAtUtc.AddDays(Days), IsTrial: true);

    /// <summary>Đổi thời hạn dùng thử của Store (độ dài + đơn vị) ra số ngày để ghi trên paywall.</summary>
    public static int? TrialDays(uint length, string unit) => unit switch
    {
        "Day" => (int)length,
        "Week" => (int)length * 7,
        "Month" => (int)length * 30,
        "Year" => (int)length * 365,
        _ => null
    };

    public static int DaysLeft(LicenseState state, DateTime nowUtc) =>
        state.Tier == PlusTier.Promo && state.ExpiresAtUtc is { } end
            ? Math.Max(0, (int)Math.Ceiling((end - nowUtc).TotalDays))
            : 0;
}

/// <summary>Ba gói bán trên Store (SPEC-V2-10 §2–§3).</summary>
public enum PlusPlan
{
    Monthly,
    Yearly,
    Lifetime
}

public static class PlusProducts
{
    public const string Monthly = "plus_monthly";
    public const string Yearly = "plus_yearly";
    public const string Lifetime = "plus_lifetime";
    public const string LegacyPro = "pro_unlock";

    public static string StoreIdFor(PlusPlan plan) => plan switch
    {
        PlusPlan.Monthly => Monthly,
        PlusPlan.Yearly => Yearly,
        _ => Lifetime
    };

    public static PlusTier TierFor(string storeId) => storeId switch
    {
        Monthly => PlusTier.Monthly,
        Yearly => PlusTier.Yearly,
        Lifetime => PlusTier.Lifetime,
        LegacyPro => PlusTier.LegacyPro,
        _ => PlusTier.None
    };

    public static bool IsSubscription(PlusTier tier) => tier is PlusTier.Monthly or PlusTier.Yearly;
}

/// <summary>
/// Sản phẩm hiển thị trên paywall. Giá lấy từ Store (đúng tiền tệ của người dùng); không có Store thì là giá tham khảo.
/// <paramref name="TrialDays"/>: số ngày dùng thử Store cấu hình cho gói (ví dụ Tháng 7, Năm 30); null nếu không rõ.
/// </summary>
public sealed record PlusProduct(PlusPlan Plan, string StoreId, string Price, bool HasTrial, bool IsReferencePrice, int? TrialDays = null);

/// <summary>Quyền đã xác minh gần nhất (được lưu cache mã hóa để dùng offline).</summary>
public sealed record LicenseState(PlusTier Tier, DateTime VerifiedAtUtc, DateTime? ExpiresAtUtc = null, bool IsTrial = false)
{
    public static LicenseState Free(DateTime verifiedAtUtc) => new(PlusTier.None, verifiedAtUtc);
}

/// <summary>Kết luận về quyền tại một thời điểm.</summary>
/// <param name="NeedsRecheck">Nên hỏi người dùng kết nối mạng để xác minh lại (không bao giờ khóa dữ liệu).</param>
public sealed record LicenseStatus(bool IsPlus, PlusTier Tier, bool NeedsRecheck, string Reason);

/// <summary>
/// Chính sách quyền offline (SPEC-V2-10 §3, tiêu chí 3). Gói thuê bao: xác minh trong 7 ngày → Plus; 7–14 ngày → vẫn Plus
/// nhưng hỏi kiểm tra lại; quá 14 ngày → về Free (dữ liệu giữ nguyên, thói quen vượt giới hạn chỉ thành chỉ đọc).
/// Gói vĩnh viễn (trọn đời, Pro v1): luôn Plus; quá 30 ngày chưa xác minh chỉ nhắc kiểm tra lại.
/// Thuê bao có ngày hết hạn đã biết và đã qua thì hết quyền ngay, không chờ ân hạn.
/// </summary>
public static class LicensePolicy
{
    public static readonly TimeSpan SubscriptionCache = TimeSpan.FromDays(7);
    public static readonly TimeSpan SubscriptionGrace = TimeSpan.FromDays(14);
    public static readonly TimeSpan DurableRecheck = TimeSpan.FromDays(30);

    public static LicenseStatus Evaluate(LicenseState? state, DateTime nowUtc)
    {
        if (state is null || state.Tier == PlusTier.None)
        {
            return new LicenseStatus(false, PlusTier.None, false, "free");
        }

        if (state.Tier == PlusTier.Promo)
        {
            // Quyền tặng cục bộ: chỉ phụ thuộc ngày hết hạn, không cần xác minh Store.
            return state.ExpiresAtUtc is { } end && nowUtc <= end
                ? new LicenseStatus(true, PlusTier.Promo, false, "promo")
                : new LicenseStatus(false, PlusTier.None, false, "promo-ended");
        }

        var age = nowUtc - state.VerifiedAtUtc;

        if (!PlusProducts.IsSubscription(state.Tier))
        {
            return new LicenseStatus(true, state.Tier, age > DurableRecheck, age > DurableRecheck ? "durable-recheck" : "durable");
        }

        if (state.ExpiresAtUtc is { } expires && nowUtc > expires)
        {
            return new LicenseStatus(false, PlusTier.None, true, "expired");
        }

        if (age <= SubscriptionCache)
        {
            return new LicenseStatus(true, state.Tier, false, "subscription");
        }

        if (age <= SubscriptionGrace)
        {
            return new LicenseStatus(true, state.Tier, true, "subscription-grace");
        }

        return new LicenseStatus(false, PlusTier.None, true, "subscription-unverified");
    }

    /// <summary>Gộp các add-on đang có: ưu tiên quyền vĩnh viễn, rồi gói năm, rồi gói tháng.</summary>
    public static LicenseState FromOwned(IEnumerable<OwnedAddOn> owned, DateTime nowUtc)
    {
        var best = owned
            .Where(o => o.IsActive)
            .Select(o => (Tier: PlusProducts.TierFor(o.StoreId), o.ExpiresAtUtc, o.IsTrial))
            .Where(o => o.Tier != PlusTier.None)
            .OrderBy(o => o.Tier switch { PlusTier.Lifetime => 0, PlusTier.LegacyPro => 1, PlusTier.Yearly => 2, _ => 3 })
            .FirstOrDefault();

        return best.Tier == PlusTier.None
            ? LicenseState.Free(nowUtc)
            : new LicenseState(best.Tier, nowUtc, PlusProducts.IsSubscription(best.Tier) ? best.ExpiresAtUtc : null, best.IsTrial);
    }
}

/// <summary>Một add-on Store người dùng đang sở hữu.</summary>
public sealed record OwnedAddOn(string StoreId, bool IsActive, DateTime? ExpiresAtUtc = null, bool IsTrial = false);

/// <summary>Quyền theo gói Plus (SPEC-V2-10 §1).</summary>
public sealed class PlusEntitlements : IEntitlements
{
    public int MaxActiveHabits => int.MaxValue;

    public int MaxRemindersPerHabit => HabitValidator.MaxReminders;

    public bool CanUseAiCoach => true;

    public bool CanUseTheme(string themeId) => true;
}

/// <summary>
/// Quyền "sống": đổi giữa Free và Plus khi giấy phép đổi, nên các dịch vụ đã giữ tham chiếu (singleton) luôn thấy quyền mới.
/// </summary>
public sealed class LiveEntitlements : IEntitlements
{
    private static readonly IEntitlements Free = new FreeEntitlements();
    private static readonly IEntitlements Plus = new PlusEntitlements();

    public bool IsPlus { get; private set; }

    public event EventHandler? Changed;

    public int MaxActiveHabits => Current.MaxActiveHabits;

    public int MaxRemindersPerHabit => Current.MaxRemindersPerHabit;

    public bool CanUseAiCoach => Current.CanUseAiCoach;

    private IEntitlements Current => IsPlus ? Plus : Free;

    public bool CanUseTheme(string themeId) => Current.CanUseTheme(themeId);

    public void Set(bool isPlus)
    {
        if (IsPlus == isPlus)
        {
            return;
        }

        IsPlus = isPlus;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Quyền dùng theme: theme miễn phí, theme mở bằng level, còn lại cần Plus.</summary>
public static class ThemeAccess
{
    public const string CandyBento = "candy-bento";
    public const string NeonNight = "neon-night";
    public const string SoftPastel = "soft-pastel";

    public static IReadOnlySet<string> FreeThemes { get; } = new HashSet<string> { CandyBento, NeonNight };

    /// <summary>Theme mở bằng level cho người dùng Free (người không trả tiền vẫn có thứ để đạt).</summary>
    public static IReadOnlyDictionary<string, int> LevelUnlocks { get; } = new Dictionary<string, int> { [SoftPastel] = 10 };

    public static bool IsUnlocked(string themeId, bool isPlus, int level) =>
        isPlus || FreeThemes.Contains(themeId) || (LevelUnlocks.TryGetValue(themeId, out var needed) && level >= needed);

    public static int? UnlockLevel(string themeId) => LevelUnlocks.TryGetValue(themeId, out var level) ? level : null;
}

/// <summary>
/// Hạ cấp không mất dữ liệu (SPEC-V2-10 §5): khi số thói quen đang chạy vượt giới hạn, chỉ những thói quen người dùng chọn
/// (tối đa <c>limit</c>) được check-in; còn lại là "chỉ đọc" (vẫn xem được, không xóa gì). Chưa chọn thì tạm giữ
/// các thói quen đầu tiên theo thứ tự người dùng sắp và báo cần chọn.
/// </summary>
public static class HabitAllowance
{
    public sealed record Result(IReadOnlySet<Guid> ReadOnly, IReadOnlyList<Guid> Running, bool NeedsChoice);

    public static Result Evaluate(IReadOnlyList<Habit> habits, IReadOnlyCollection<Guid>? chosen, int limit)
    {
        ArgumentNullException.ThrowIfNull(habits);

        var active = habits.Where(h => h.IsActive).OrderBy(h => h.SortKey, StringComparer.Ordinal).ToList();

        if (active.Count <= limit)
        {
            return new Result(new HashSet<Guid>(), active.Select(h => h.Id).ToList(), false);
        }

        var picked = (chosen ?? [])
            .Where(id => active.Any(h => h.Id == id))
            .Distinct()
            .Take(limit)
            .ToList();

        var needsChoice = picked.Count < limit;

        // Bù cho đủ giới hạn bằng thói quen đầu danh sách (tạm thời, tới khi người dùng chọn).
        foreach (var habit in active)
        {
            if (picked.Count >= limit)
            {
                break;
            }

            if (!picked.Contains(habit.Id))
            {
                picked.Add(habit.Id);
            }
        }

        var readOnly = active.Select(h => h.Id).Where(id => !picked.Contains(id)).ToHashSet();
        return new Result(readOnly, picked, needsChoice);
    }
}

/// <summary>Kết quả mua hàng.</summary>
public enum PurchaseOutcome
{
    Succeeded,
    AlreadyOwned,
    Cancelled,
    NetworkError,
    StoreUnavailable,
    Failed
}

/// <summary>
/// Cửa hàng Plus. Hiện thực thật dùng Microsoft Store (chỉ chạy khi app được đóng gói MSIX); không có Store thì mọi lần mua
/// trả về <see cref="PurchaseOutcome.StoreUnavailable"/> — không bao giờ tự cấp quyền.
/// </summary>
public interface IPlusStore
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<PlusProduct>> GetProductsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<OwnedAddOn>> GetOwnedAsync(CancellationToken ct = default);

    /// <summary>Chỉ được gọi từ nút "Mua" người dùng bấm (SPEC-V2-10 tiêu chí 5).</summary>
    Task<PurchaseOutcome> PurchaseAsync(PlusPlan plan, IntPtr ownerWindow, CancellationToken ct = default);
}
