using System.Security.Cryptography;
using System.Text.Json;
using Erp.Application.LegacyMigration;

namespace Erp.LegacyMigration;

public static class LegacyStoreAttributionPlanCli
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        byte[]? key = null;
        try
        {
            var options = LegacyStoreAttributionPlanOptions.Parse(args);
            key = LegacyExportKey.ReadFromEnvironmentOrConsole(output);
            using var payloadStore = new EncryptedPayloadStore(key);
            var importOptions = new LegacyImportOptions(options.InputDirectories, "B01", false, "store-plan-v1");
            var dataset = await new LegacyImportDatasetLoader(payloadStore).LoadAsync(importOptions, cancellationToken);
            var plan = LegacyStoreAttributionPlanBuilder.Build(dataset);
            SecureOutputDirectory.Prepare(Path.GetDirectoryName(options.OutputFile)!);
            await SecureFile.WriteTextAtomicAsync(
                options.OutputFile,
                JsonSerializer.Serialize(plan, LegacyFieldProfileReport.JsonOptions),
                cancellationToken);
            await output.WriteLineAsync(
                $"门店归属计划完成：门店={plan.Stores.Count}，顾客={plan.Customers.Count}，员工={plan.Employees.Count}，护理={plan.CareRecords.Count}。");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("门店归属计划已取消；没有生成不完整结果。");
            return 130;
        }
        catch (Exception exception) when (exception is LegacyMigrationException or InvalidOperationException)
        {
            await output.WriteLineAsync($"门店归属计划停止：{SensitiveText.Redact(exception.Message)}");
            return 2;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }
}

public sealed record LegacyStoreAttributionPlanOptions(
    IReadOnlyList<string> InputDirectories,
    string OutputFile)
{
    public static LegacyStoreAttributionPlanOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "store-plan")
            throw new LegacyMigrationException("用法：store-plan --input 导出目录 [--input 导出目录] --output 绝对JSON文件");
        var inputs = new List<string>();
        string? output = null;
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input": inputs.Add(Next(args, ref index)); break;
                case "--output": output = Next(args, ref index); break;
                default: throw new LegacyMigrationException($"不支持的门店归属计划参数：{args[index]}");
            }
        }
        if (inputs.Count is 0 or > 10) throw new LegacyMigrationException("门店归属计划必须提供1到10个来源目录。");
        if (string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output) ||
            !string.Equals(Path.GetExtension(output), ".json", StringComparison.OrdinalIgnoreCase))
            throw new LegacyMigrationException("门店归属计划输出必须是绝对JSON文件路径。");
        var fullInputs = inputs.Select(path =>
        {
            if (!Path.IsPathFullyQualified(path)) throw new LegacyMigrationException("门店归属来源目录必须使用绝对路径。");
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) throw new LegacyMigrationException("门店归属来源目录不存在。");
            return full;
        }).Distinct(StringComparer.Ordinal).ToArray();
        return new LegacyStoreAttributionPlanOptions(fullInputs, Path.GetFullPath(output));
    }

    private static string Next(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length) throw new LegacyMigrationException("门店归属计划参数缺少值。");
        return args[index];
    }
}

public sealed record LegacyStoreAttributionPlan(
    string SourceFingerprintSha256,
    IReadOnlyList<LegacyStorePlanItem> Stores,
    IReadOnlyList<LegacyStoreAssignment> Customers,
    IReadOnlyList<LegacyStoreAssignment> Employees,
    IReadOnlyList<LegacyStoreAssignment> CareRecords);

public sealed record LegacyStorePlanItem(string SourceId, string? SourceCode, string SourceName);

public sealed record LegacyStoreAssignment(string SourceId, string SourceStoreId);

public static class LegacyStoreAttributionPlanBuilder
{
    public static LegacyStoreAttributionPlan Build(LegacyImportDataset dataset)
    {
        var rows = dataset.Rows.GroupBy(row => row.Entity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var storeRows = RequireRows(rows, "stores");
        var stores = storeRows.Select(row => new LegacyStorePlanItem(
                row.SourceId,
                Field(row, "shop_code")?.Trim(),
                RequireField(row, "shop_name")))
            .OrderBy(store => store.SourceId, StringComparer.Ordinal)
            .ToArray();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var store in stores)
        {
            AddAlias(aliases, store.SourceId, store.SourceId);
            if (!string.IsNullOrWhiteSpace(store.SourceCode)) AddAlias(aliases, store.SourceCode, store.SourceId);
            AddAlias(aliases, store.SourceName, store.SourceId);
        }

        return new LegacyStoreAttributionPlan(
            dataset.SourceFingerprintSha256,
            stores,
            ResolveAssignments(RequireRows(rows, "customers"), "member_shop", aliases),
            ResolveAssignments(RequireRows(rows, "employees"), "emplee_shop", aliases),
            ResolveAssignments(RequireRows(rows, "care-records"), "bill_shop", aliases));
    }

    private static LegacyStoreAssignment[] ResolveAssignments(
        LegacySourceRow[] rows,
        string field,
        Dictionary<string, string> aliases) =>
        rows.Select(row =>
            {
                var value = RequireField(row, field);
                if (!aliases.TryGetValue(value, out var storeId))
                    throw new LegacyMigrationException($"模块 {row.Entity} 的门店字段无法映射到来源门店。");
                return new LegacyStoreAssignment(row.SourceId, storeId);
            })
            .OrderBy(item => item.SourceId, StringComparer.Ordinal)
            .ToArray();

    private static LegacySourceRow[] RequireRows(
        Dictionary<string, LegacySourceRow[]> rows,
        string entity) => rows.TryGetValue(entity, out var result) && result.Length > 0
        ? result
        : throw new LegacyMigrationException($"门店归属计划缺少模块：{entity}。");

    private static string RequireField(LegacySourceRow row, string field) =>
        Field(row, field)?.Trim() is { Length: > 0 } value
            ? value
            : throw new LegacyMigrationException($"模块 {row.Entity} 存在空门店字段。");

    private static string? Field(LegacySourceRow row, string field) =>
        row.Fields.TryGetValue(field, out var value) ? value : null;

    private static void AddAlias(Dictionary<string, string> aliases, string alias, string sourceId)
    {
        var normalized = alias.Trim();
        if (aliases.TryGetValue(normalized, out var existing) && existing != sourceId)
            throw new LegacyMigrationException("旧系统门店主键、编码或名称存在冲突，无法生成安全归属计划。");
        aliases[normalized] = sourceId;
    }
}
