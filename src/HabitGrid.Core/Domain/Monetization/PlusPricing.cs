using System.Globalization;

namespace HabitGrid.Core.Domain.Monetization;

/// <summary>
/// Tính "tiết kiệm bao nhiêu %" của gói Năm so với trả theo tháng, từ chính chuỗi giá Store trả về ("39.000 ₫", "$1.99", "9,99 €").
/// Không đọc được giá thì trả null để giao diện KHÔNG ghi con số (không bao giờ ghi một tỷ lệ đoán).
/// </summary>
public static class PlusPricing
{
    public static int? YearlySavePercent(string monthlyPrice, string yearlyPrice)
    {
        if (TryParseAmount(monthlyPrice, out var monthly) && TryParseAmount(yearlyPrice, out var yearly) && monthly > 0 && yearly > 0)
        {
            var save = (int)Math.Round(100 * (1 - (yearly / (monthly * 12))), MidpointRounding.AwayFromZero);
            return save > 0 ? save : null;
        }

        return null;
    }

    /// <summary>Đọc số tiền trong chuỗi giá đã định dạng theo vùng (bỏ ký hiệu tiền tệ, nhận dạng dấu nghìn và dấu thập phân).</summary>
    public static bool TryParseAmount(string? price, out decimal amount)
    {
        amount = 0;

        if (string.IsNullOrWhiteSpace(price))
        {
            return false;
        }

        var raw = new string(price.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray()).Trim('.', ',');

        if (raw.Length == 0 || !raw.Any(char.IsDigit))
        {
            return false;
        }

        var lastDot = raw.LastIndexOf('.');
        var lastComma = raw.LastIndexOf(',');
        string normalized;

        if (lastDot >= 0 && lastComma >= 0)
        {
            // Có cả hai loại dấu: dấu xuất hiện sau cùng là dấu thập phân.
            var decimalSeparator = lastDot > lastComma ? '.' : ',';
            var thousands = decimalSeparator == '.' ? ',' : '.';
            normalized = raw.Replace(thousands.ToString(), string.Empty).Replace(decimalSeparator, '.');
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var separator = lastDot >= 0 ? '.' : ',';
            var count = raw.Count(c => c == separator);
            var digitsAfter = raw.Length - raw.LastIndexOf(separator) - 1;

            // Nhiều dấu ("1.249.000") hoặc một dấu theo sau đúng 3 chữ số ("249.000"): dấu nghìn. Còn lại ("9,99"): dấu thập phân.
            normalized = count > 1 || digitsAfter == 3
                ? raw.Replace(separator.ToString(), string.Empty)
                : raw.Replace(separator, '.');
        }
        else
        {
            normalized = raw;
        }

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    }
}
