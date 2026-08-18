using System.Globalization;
using System.Text;

namespace Erp.Infrastructure.Customers;

internal sealed record CustomerExportRow(string Name, string Mobile, string Status, int ActiveCardCount,
    DateTimeOffset CreatedAtUtc);

internal static class CustomerExportFormatter
{
    public static byte[] ToCsv(IReadOnlyList<CustomerExportRow> rows)
    {
        var csv = new StringBuilder("姓名,手机号,状态,有效会员卡数,建档时间\r\n");
        foreach (var row in rows)
        {
            csv.Append(Cell(row.Name)).Append(',')
                .Append(Cell(row.Mobile)).Append(',')
                .Append(Cell(row.Status)).Append(',')
                .Append(row.ActiveCardCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Cell(row.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)))
                .Append("\r\n");
        }

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = encoding.GetPreamble();
        var content = encoding.GetBytes(csv.ToString());
        var result = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, result, preamble.Length, content.Length);
        return result;
    }

    private static string Cell(string value)
    {
        var safe = value;
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            safe = $"'{safe}";
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }
}
