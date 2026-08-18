using System.Globalization;
using System.Text;
using Erp.Domain.Cashier;

namespace Erp.Infrastructure.Cashier;

internal static class PaymentChannelBillCsv
{
    public static IReadOnlyList<PaymentChannelBillEntry> ParseWechat(string content)
    {
        var rows = ParseRows(content);
        var headerIndex = rows.FindIndex(row => row.Any(cell => Clean(cell) == "商户订单号"));
        if (headerIndex < 0) throw new FormatException("微信交易账单缺少商户订单号表头");
        var headers = HeaderMap(rows[headerIndex]);
        var entries = new Dictionary<string, PaymentChannelBillEntry>(StringComparer.Ordinal);
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            if (row.Count == 0 || Clean(row[0]).StartsWith("总交易单数", StringComparison.Ordinal)) break;
            var outTradeNo = Field(row, headers, "商户订单号");
            if (string.IsNullOrWhiteSpace(outTradeNo)) continue;
            var outRefundNo = Field(row, headers, "商户退款单号");
            if (!string.IsNullOrWhiteSpace(outRefundNo) &&
                TryMinor(Field(row, headers, "申请退款金额") ?? Field(row, headers, "退款金额"), out var refunded))
            {
                var key = $"REFUND:{outRefundNo}";
                entries.TryAdd(key, new PaymentChannelBillEntry(PaymentChannelReconciliationItemType.Refund,
                    key, outTradeNo, outRefundNo, Field(row, headers, "微信退款单号"), refunded, 0,
                    Field(row, headers, "退款状态") ?? "UNKNOWN"));
            }
            else if (TryMinor(Field(row, headers, "订单金额") ?? Field(row, headers, "应结订单金额"),
                         out var paid))
            {
                var key = $"PAY:{outTradeNo}";
                entries.TryAdd(key, new PaymentChannelBillEntry(PaymentChannelReconciliationItemType.Payment,
                    key, outTradeNo, null, Field(row, headers, "微信订单号"), paid,
                    MinorOrZero(Field(row, headers, "手续费")),
                    Field(row, headers, "交易状态") ?? "UNKNOWN"));
            }
        }
        return entries.Values.ToList();
    }

    public static IReadOnlyList<PaymentChannelBillEntry> ParseAlipay(string content)
    {
        var rows = ParseRows(content);
        var headerIndex = rows.FindIndex(row => row.Any(cell => Clean(cell) == "商户订单号"));
        if (headerIndex < 0) throw new FormatException("支付宝交易账单缺少商户订单号表头");
        var headers = HeaderMap(rows[headerIndex]);
        var entries = new Dictionary<string, PaymentChannelBillEntry>(StringComparer.Ordinal);
        foreach (var row in rows.Skip(headerIndex + 1))
        {
            var outTradeNo = Field(row, headers, "商户订单号");
            if (string.IsNullOrWhiteSpace(outTradeNo)) continue;
            var outRefundNo = Field(row, headers, "退款批次号/请求号") ??
                Field(row, headers, "退款请求号");
            var providerTradeNo = Field(row, headers, "支付宝交易号");
            var businessType = Field(row, headers, "业务类型") ?? "UNKNOWN";
            var amountText = !string.IsNullOrWhiteSpace(outRefundNo)
                ? Field(row, headers, "退款金额（元）") ?? Field(row, headers, "退款金额(元)") ??
                  Field(row, headers, "商家实收（元）") ?? Field(row, headers, "商家实收(元)") ??
                  Field(row, headers, "订单金额（元）") ?? Field(row, headers, "订单金额(元)")
                : Field(row, headers, "订单金额（元）") ?? Field(row, headers, "订单金额(元)") ??
                  Field(row, headers, "商家实收（元）") ?? Field(row, headers, "商家实收(元)");
            if (!TryMinor(amountText, out var amount)) continue;
            var fee = MinorOrZero(Field(row, headers, "服务费（元）") ?? Field(row, headers, "服务费(元)"));
            if (!string.IsNullOrWhiteSpace(outRefundNo))
            {
                var key = $"REFUND:{outRefundNo}";
                entries.TryAdd(key, new PaymentChannelBillEntry(PaymentChannelReconciliationItemType.Refund,
                    key, outTradeNo, outRefundNo, providerTradeNo, amount, fee, businessType));
            }
            else
            {
                var key = $"PAY:{outTradeNo}";
                entries.TryAdd(key, new PaymentChannelBillEntry(PaymentChannelReconciliationItemType.Payment,
                    key, outTradeNo, null, providerTradeNo, amount, fee, businessType));
            }
        }
        return entries.Values.ToList();
    }

    private static List<List<string>> ParseRows(string content)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '"')
            {
                if (quoted && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if ((character == '\r' || character == '\n') && !quoted)
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(cell => !string.IsNullOrWhiteSpace(cell))) rows.Add(row);
                row = [];
            }
            else field.Append(character);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Any(cell => !string.IsNullOrWhiteSpace(cell))) rows.Add(row);
        }
        return rows;
    }

    private static Dictionary<string, int> HeaderMap(IReadOnlyList<string> row) => row
        .Select((value, index) => new { Value = Clean(value), Index = index })
        .Where(x => x.Value.Length > 0)
        .GroupBy(x => x.Value, StringComparer.Ordinal)
        .ToDictionary(x => x.Key, x => x.First().Index, StringComparer.Ordinal);

    private static string? Field(List<string> row, Dictionary<string, int> headers,
        string name) => headers.TryGetValue(name, out var index) && index < row.Count
            ? NullIfEmpty(Clean(row[index])) : null;

    private static string Clean(string value) => value.Trim().TrimStart('\uFEFF', '`').Trim();
    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static long MinorOrZero(string? value) => TryMinor(value, out var result) ? result : 0;

    private static bool TryMinor(string? value, out long minor)
    {
        minor = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = Clean(value).Replace("¥", string.Empty, StringComparison.Ordinal)
            .Replace("￥", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var amount) || amount != decimal.Round(amount, 2) ||
            Math.Abs(amount) > 100_000_000m)
            return false;
        try
        {
            minor = checked((long)(Math.Abs(amount) * 100m));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
