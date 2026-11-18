using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HabitGrid.Core.Domain.Monetization;
using HabitGrid.Presentation.Services;
using Windows.Services.Store;

namespace HabitGrid.WinUI.Plus;

/// <summary>
/// Cửa hàng Plus qua Microsoft Store (SPEC-V2-10 §3). Chỉ hoạt động khi app chạy với danh tính gói (MSIX từ Store);
/// bản không đóng gói thì <see cref="IsAvailable"/> = false và mọi lần mua trả về StoreUnavailable — không tự cấp quyền.
/// </summary>
public sealed class WindowsPlusStore : IPlusStore
{
    private static readonly string[] ProductKinds = ["Durable", "Subscription"];
    private readonly Dictionary<string, string> _storeIdByToken = new(StringComparer.OrdinalIgnoreCase);
    private StoreContext? _context;

    public WindowsPlusStore() => IsAvailable = HasPackageIdentity();

    public bool IsAvailable { get; }

    public async Task<IReadOnlyList<PlusProduct>> GetProductsAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return [];
        }

        var result = await Context().GetAssociatedStoreProductsAsync(ProductKinds);

        if (result.ExtendedError is not null)
        {
            return [];
        }

        var list = new List<PlusProduct>();

        foreach (var product in result.Products.Values)
        {
            var token = product.InAppOfferToken;
            var plan = PlusProducts.TierFor(token) switch
            {
                PlusTier.Monthly => PlusPlan.Monthly,
                PlusTier.Yearly => PlusPlan.Yearly,
                PlusTier.Lifetime => PlusPlan.Lifetime,
                _ => (PlusPlan?)null
            };

            if (plan is null)
            {
                continue;
            }

            _storeIdByToken[token] = product.StoreId;
            var trialSku = product.Skus.FirstOrDefault(s => s.IsSubscription && s.SubscriptionInfo?.HasTrialPeriod == true);
            var trialDays = trialSku?.SubscriptionInfo is { } info ? PlusPromo.TrialDays(info.TrialPeriod, info.TrialPeriodUnit.ToString()) : null;
            list.Add(new PlusProduct(plan.Value, token, product.Price.FormattedPrice, trialSku is not null, false, trialDays));
        }

        return list;
    }

    public async Task<IReadOnlyList<OwnedAddOn>> GetOwnedAsync(CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return [];
        }

        var license = await Context().GetAppLicenseAsync();

        return license.AddOnLicenses.Values
            .Select(l => new OwnedAddOn(
                l.InAppOfferToken,
                l.IsActive,
                l.ExpirationDate.Year < 9000 ? l.ExpirationDate.UtcDateTime : null))
            .ToList();
    }

    public async Task<PurchaseOutcome> PurchaseAsync(PlusPlan plan, IntPtr ownerWindow, CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            return PurchaseOutcome.StoreUnavailable;
        }

        var token = PlusProducts.StoreIdFor(plan);

        if (!_storeIdByToken.ContainsKey(token))
        {
            await GetProductsAsync(ct);
        }

        if (!_storeIdByToken.TryGetValue(token, out var storeId))
        {
            return PurchaseOutcome.Failed;
        }

        var context = Context();

        if (ownerWindow != IntPtr.Zero)
        {
            // App desktop: hộp thoại mua của Store cần cửa sổ chủ.
            WinRT.Interop.InitializeWithWindow.Initialize(context, ownerWindow);
        }

        var result = await context.RequestPurchaseAsync(storeId);

        return result.Status switch
        {
            StorePurchaseStatus.Succeeded => PurchaseOutcome.Succeeded,
            StorePurchaseStatus.AlreadyPurchased => PurchaseOutcome.AlreadyOwned,
            StorePurchaseStatus.NotPurchased => PurchaseOutcome.Cancelled,
            StorePurchaseStatus.NetworkError => PurchaseOutcome.NetworkError,
            _ => PurchaseOutcome.Failed
        };
    }

    private StoreContext Context() => _context ??= StoreContext.GetDefault();

    private static bool HasPackageIdentity()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}

#if DEBUG
/// <summary>
/// Store giả CHỈ có trong bản Debug, bật bằng biến môi trường <c>HABITGRID_FAKE_STORE=1</c>, để thử luồng mua trên máy dev.
/// Không tồn tại trong bản Release nên không thể dùng để mở Plus miễn phí.
/// </summary>
public sealed class DebugFakePlusStore : IPlusStore
{
    private readonly List<OwnedAddOn> _owned = [];

    public bool IsAvailable => true;

    public Task<IReadOnlyList<PlusProduct>> GetProductsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlusProduct>>(
        [
            new(PlusPlan.Monthly, PlusProducts.Monthly, "39.000 ₫ (debug)", false, false),
            new(PlusPlan.Yearly, PlusProducts.Yearly, "249.000 ₫ (debug)", true, false),
            new(PlusPlan.Lifetime, PlusProducts.Lifetime, "499.000 ₫ (debug)", false, false)
        ]);

    public Task<IReadOnlyList<OwnedAddOn>> GetOwnedAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OwnedAddOn>>(_owned.ToList());

    public Task<PurchaseOutcome> PurchaseAsync(PlusPlan plan, IntPtr ownerWindow, CancellationToken ct = default)
    {
        _owned.Add(new OwnedAddOn(PlusProducts.StoreIdFor(plan), true, plan == PlusPlan.Lifetime ? null : DateTime.UtcNow.AddDays(30)));
        return Task.FromResult(PurchaseOutcome.Succeeded);
    }
}
#endif

/// <summary>Cache giấy phép mã hóa DPAPI theo người dùng Windows (file khác máy/người dùng khác không giải mã được).</summary>
public sealed class DpapiLicenseCache : ILicenseCache
{
    private static readonly byte[] Entropy = "HabitGrid.Plus.v2"u8.ToArray();
    private readonly string _path;

    public DpapiLicenseCache(string path) => _path = path;

    public LicenseState? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<LicenseState>(Encoding.UTF8.GetString(plain));
        }
        catch (Exception)
        {
            // Hỏng hoặc của người dùng khác: coi như chưa có, sẽ hỏi lại Store.
            return null;
        }
    }

    public void Save(LicenseState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state));
            File.WriteAllBytes(_path, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception)
        {
            // Không ghi được cache thì lần sau hỏi lại Store; không làm hỏng app.
        }
    }
}
