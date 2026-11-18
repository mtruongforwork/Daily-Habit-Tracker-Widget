using CommunityToolkit.Mvvm.ComponentModel;
using HabitGrid.Core.Domain.Monetization;
using HabitGrid.Presentation.Services;

namespace HabitGrid.Presentation.ViewModels;

/// <summary>Nguồn mở paywall (để đo, và để nội dung nói đúng lý do). Paywall chỉ mở khi người dùng chạm giới hạn hoặc tự mở.</summary>
public enum PaywallSource
{
    HabitLimit,
    Theme,
    Settings,
    Menu
}

/// <summary>Một gói trên paywall.</summary>
public sealed record PaywallPlan(PlusPlan Plan, string Price, bool HasTrial, bool IsBestValue, int? SavePercent);

/// <summary>
/// Paywall Plus (02-UX-SCREENS §12, SPEC-V2-10 §6): 3 gói, giá đầy đủ, ghi rõ tự gia hạn, nút đóng rõ ràng.
/// Mở paywall, xem giá hay khôi phục KHÔNG bao giờ gọi mua; chỉ <see cref="BuyAsync"/> (nút Mua) mới gọi.
/// </summary>
public sealed partial class PaywallViewModel : ObservableObject
{
    /// <summary>Giá tham khảo khi Store không trả giá; hiển thị kèm nhãn "tham khảo". Theo bảng giá v3 ($1.99 / $9.99 / $19.99); giá thật đặt trong Partner Center.</summary>
    public static readonly IReadOnlyDictionary<PlusPlan, string> ReferencePrices = new Dictionary<PlusPlan, string>
    {
        [PlusPlan.Monthly] = "$1.99",
        [PlusPlan.Yearly] = "$9.99",
        [PlusPlan.Lifetime] = "$19.99"
    };

    private readonly PlusService _plus;

    public PaywallViewModel(PlusService plus, PaywallSource source)
    {
        _plus = plus ?? throw new ArgumentNullException(nameof(plus));
        Source = source;
    }

    public PaywallSource Source { get; }

    public IReadOnlyList<PaywallPlan> Plans { get; private set; } = [];

    public bool StoreAvailable => _plus.StoreAvailable;

    /// <summary>Store có trả về sản phẩm Plus thật chưa. Chưa có (chưa tạo add-on trên Partner Center) thì không mở bán: tránh hiện nút mua cho thứ không mua được.</summary>
    public bool HasStoreProducts { get; private set; }

    /// <summary>Nút Mua chỉ hoạt động khi chạy trong Store và Store có sản phẩm.</summary>
    public bool PurchasesOpen => StoreAvailable && HasStoreProducts;

    public bool IsPlus => _plus.Status.IsPlus;

    public bool PricesAreReference { get; private set; }

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Khóa chuỗi thông báo kết quả (để giao diện dịch).</summary>
    [ObservableProperty]
    private string? _messageKey;

    public async Task LoadAsync()
    {
        IReadOnlyList<PlusProduct> products;

        try
        {
            products = await _plus.GetProductsAsync();
        }
        catch (Exception)
        {
            products = [];
        }

        var byPlan = products.ToDictionary(p => p.Plan);
        PricesAreReference = products.Count == 0 || products.Any(p => p.IsReferencePrice);
        HasStoreProducts = products.Count > 0;

        var monthly = Make(PlusPlan.Monthly, byPlan, false, null);
        var yearlyPrice = byPlan.TryGetValue(PlusPlan.Yearly, out var yearlyProduct) ? yearlyProduct.Price : ReferencePrices[PlusPlan.Yearly];

        // "Tiết kiệm x%" tính từ chính giá đang hiện (trước đây cố định 44%, sai khi đổi giá); không đọc được giá thì không ghi.
        Plans =
        [
            monthly,
            Make(PlusPlan.Yearly, byPlan, true, HabitGrid.Core.Domain.Monetization.PlusPricing.YearlySavePercent(monthly.Price, yearlyPrice)),
            Make(PlusPlan.Lifetime, byPlan, false, null)
        ];
        OnPropertyChanged(nameof(Plans));
        OnPropertyChanged(nameof(StoreAvailable));
        OnPropertyChanged(nameof(PurchasesOpen));
    }

    /// <summary>Nút "Mua": đường duy nhất trong UI gọi mua hàng.</summary>
    public async Task<PurchaseOutcome> BuyAsync(PlusPlan plan, IntPtr ownerWindow)
    {
        if (IsBusy)
        {
            return PurchaseOutcome.Cancelled;
        }

        IsBusy = true;

        try
        {
            var outcome = await _plus.PurchaseAsync(plan, ownerWindow);
            MessageKey = outcome switch
            {
                PurchaseOutcome.Succeeded or PurchaseOutcome.AlreadyOwned => "plus.msg.thanks",
                PurchaseOutcome.Cancelled => null,
                PurchaseOutcome.NetworkError => "plus.msg.network",
                PurchaseOutcome.StoreUnavailable => "plus.msg.nostore",
                _ => "plus.msg.failed"
            };
            OnPropertyChanged(nameof(IsPlus));
            return outcome;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RestoreAsync()
    {
        IsBusy = true;

        try
        {
            var ok = await _plus.RestoreAsync();
            MessageKey = !ok ? (StoreAvailable ? "plus.msg.network" : "plus.msg.nostore")
                : _plus.Status.IsPlus ? "plus.msg.restored" : "plus.msg.nothing";
            OnPropertyChanged(nameof(IsPlus));
            return ok;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static PaywallPlan Make(PlusPlan plan, IReadOnlyDictionary<PlusPlan, PlusProduct> byPlan, bool best, int? save) =>
        byPlan.TryGetValue(plan, out var product)
            ? new PaywallPlan(plan, product.Price, product.HasTrial, best, save)
            // Giá tham khảo không bao giờ kèm "dùng thử": chỉ Store mới biết add-on có bật dùng thử hay không
            // (ghi dùng thử mà không có là mô tả sai, vi phạm chính sách 10.8.4 của Store).
            : new PaywallPlan(plan, ReferencePrices[plan], HasTrial: false, best, save);
}
