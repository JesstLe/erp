using System.Text.Json;

namespace Erp.LegacyMigration;

public sealed record JqGridPage(int Page, int TotalPages, int Records, int RowCount)
{
    public static JqGridPage Parse(string json, int requestedPage)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var rows = ExtractRows(root);
            if (rows is null)
            {
                throw new LegacyMigrationException($"旧系统列表响应缺少可识别数组，结构={DescribeShape(root)}。");
            }

            var rowCount = rows.Length;
            var page = root.ValueKind == JsonValueKind.Object
                ? ReadInteger(root, "page") ?? requestedPage
                : requestedPage;
            var totalPages = root.ValueKind == JsonValueKind.Object
                ? ReadInteger(root, "total") ?? (rowCount == 0 ? Math.Max(0, page - 1) : page)
                : page;
            var records = root.ValueKind == JsonValueKind.Object
                ? ReadInteger(root, "records") ?? rowCount
                : rowCount;

            if (page < 0 || totalPages < 0 || records < 0 || page > Math.Max(totalPages, requestedPage) + 1)
            {
                throw new LegacyMigrationException("旧系统列表分页元数据无效。");
            }

            return new JqGridPage(page, totalPages, records, rowCount);
        }
        catch (JsonException exception)
        {
            throw new LegacyMigrationException("旧系统列表响应不是有效 JSON。", exception);
        }
    }

    public static IEnumerable<string> EnumerateRows(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ExtractRows(document.RootElement)
            ?? throw new LegacyMigrationException("旧系统列表响应缺少可识别数组。");
    }

    private static string[]? ExtractRows(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(row => row.GetRawText()).ToArray();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "rows", "data", "items", "list" })
            {
                if (!root.TryGetProperty(propertyName, out var rows))
                {
                    continue;
                }

                if (rows.ValueKind == JsonValueKind.Array)
                {
                    return rows.EnumerateArray().Select(row => row.GetRawText()).ToArray();
                }

                if (rows.ValueKind == JsonValueKind.String)
                {
                    var encodedRows = rows.GetString();
                    if (string.IsNullOrWhiteSpace(encodedRows) && ReadInteger(root, "records") == 0)
                    {
                        return [];
                    }

                    if (!string.IsNullOrWhiteSpace(encodedRows))
                    {
                        try
                        {
                            using var nested = JsonDocument.Parse(encodedRows);
                            if (nested.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                return nested.RootElement
                                    .EnumerateArray()
                                    .Select(row => row.GetRawText())
                                    .ToArray();
                            }
                        }
                        catch (JsonException)
                        {
                            return null;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static string DescribeShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return root.ValueKind.ToString();
        }

        return string.Join(
            ',',
            root.EnumerateObject()
                .Take(20)
                .Select(property => $"{property.Name}:{property.Value.ValueKind}"));
    }

    private static int? ReadInteger(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numericValue))
        {
            return numericValue;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var stringValue))
        {
            return stringValue;
        }

        throw new LegacyMigrationException($"旧系统列表字段 {propertyName} 不是有效整数。");
    }
}
