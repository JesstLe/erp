using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Erp.Application.LegacyMigration;
using Erp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Erp.LegacyMigration;

public static class LegacyImportCli
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        byte[]? key = null;
        LegacyImportDataset? dataset = null;
        try
        {
            var options = LegacyImportOptions.Parse(args);
            key = LegacyExportKey.ReadFromEnvironmentOrConsole(output);
            using var payloadStore = new EncryptedPayloadStore(key);
            dataset = await new LegacyImportDatasetLoader(payloadStore).LoadAsync(options, cancellationToken);
            await output.WriteLineAsync(
                $"导入预检完成：品牌={options.TenantCode}，来源记录={dataset.Rows.Count}，照片={dataset.Photos.Count}，模式={(options.Apply ? "执行" : "干跑")}。");

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddErpInfrastructure(builder.Configuration, builder.Environment);
            await using var provider = builder.Services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ILegacyImportService>();
            var result = await service.ImportAsync(new LegacyImportCommand(dataset, !options.Apply), cancellationToken);
            await output.WriteLineAsync($"迁移运行：{result.RunId}，已完成={result.AlreadyCompleted}，模式={(result.DryRun ? "干跑" : "执行")}。");
            foreach (var item in result.Created.OrderBy(x => x.Key, StringComparer.Ordinal))
                await output.WriteLineAsync($"创建 {item.Key}: {item.Value}");
            foreach (var item in result.Skipped.OrderBy(x => x.Key, StringComparer.Ordinal))
                await output.WriteLineAsync($"跳过 {item.Key}: {item.Value}");
            foreach (var item in result.Exceptions.OrderBy(x => x.Key, StringComparer.Ordinal))
                await output.WriteLineAsync($"异常 {item.Key}: {item.Value}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("导入已取消，数据库事务已回滚。");
            return 130;
        }
        catch (Exception exception) when (exception is LegacyMigrationException or InvalidOperationException)
        {
            await output.WriteLineAsync($"导入停止：{SensitiveText.Redact(exception.Message)}");
            return 2;
        }
        finally
        {
            if (dataset is not null)
                foreach (var photo in dataset.Photos) CryptographicOperations.ZeroMemory(photo.Content);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }
}

public sealed record LegacyImportOptions(
    IReadOnlyList<string> InputDirectories,
    string TenantCode,
    bool Apply,
    string ImportVersion)
{
    public static LegacyImportOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "import")
            throw new LegacyMigrationException("用法：import --input 导出目录 [--input 导出目录] --tenant B01 [--apply]");
        var inputs = new List<string>();
        string? tenant = null;
        var apply = false;
        var version = "legacy-import-v1";
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input": inputs.Add(Next(args, ref index)); break;
                case "--tenant": tenant = Next(args, ref index).ToUpperInvariant(); break;
                case "--version": version = Next(args, ref index); break;
                case "--apply": apply = true; break;
                default: throw new LegacyMigrationException($"不支持的导入参数：{args[index]}");
            }
        }
        if (tenant != "B01") throw new LegacyMigrationException("当前受控迁移只允许目标品牌 B01。");
        if (inputs.Count is 0 or > 10) throw new LegacyMigrationException("导入必须提供1到10个来源目录。");
        var fullInputs = inputs.Select(path =>
        {
            if (!Path.IsPathFullyQualified(path)) throw new LegacyMigrationException("导入目录必须使用绝对路径。");
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) throw new LegacyMigrationException("导入目录不存在。");
            return full;
        }).Distinct(StringComparer.Ordinal).ToArray();
        if (version.Length is < 3 or > 40 || version.Any(char.IsControl))
            throw new LegacyMigrationException("导入版本格式无效。");
        return new LegacyImportOptions(fullInputs, tenant, apply, version);
    }

    private static string Next(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length) throw new LegacyMigrationException("导入参数缺少值。");
        return args[index];
    }
}

public sealed class LegacyImportDatasetLoader(EncryptedPayloadStore payloadStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, string> SourceIdFields =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customers"] = "member_id",
            ["stores"] = "shop_id",
            ["employees"] = "emplee_id",
            ["services"] = "goods_id",
            ["products"] = "goods_id",
            ["service-passes"] = "goods_id",
            ["member-levels"] = "iclevel_id",
            ["topup-plans"] = "icfull_id",
            ["facilities"] = "room_id",
            ["brands"] = "brand_id",
            ["units"] = "unit_id",
            ["employee-trades"] = "ework_id",
            ["customer-sources"] = "source_id",
            ["care-records"] = "nurse_id",
        };

    public async Task<LegacyImportDataset> LoadAsync(
        LegacyImportOptions options,
        CancellationToken cancellationToken)
    {
        var manifests = options.InputDirectories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "manifest.json", SearchOption.AllDirectories))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (manifests.Length == 0) throw new LegacyMigrationException("导入目录没有找到清单。");
        var rows = new List<LegacySourceRow>();
        var photos = new List<LegacySourcePhoto>();
        var fingerprintParts = new List<string>();
        var entities = new HashSet<string>(StringComparer.Ordinal);

        foreach (var manifestPath in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
            var entity = document.RootElement.GetProperty("entity").GetString()
                ?? throw new LegacyMigrationException("导入清单缺少实体名。");
            if (!entities.Add(entity)) throw new LegacyMigrationException($"导入包含重复模块：{entity}。");
            if (entity == "customer-photos")
            {
                await LoadPhotosAsync(manifestPath, document.RootElement, photos, fingerprintParts, cancellationToken);
                continue;
            }
            await LoadRowsAsync(manifestPath, document.RootElement, rows, fingerprintParts, cancellationToken);
        }

        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', fingerprintParts.Order(StringComparer.Ordinal)))));
        return new LegacyImportDataset(options.TenantCode, "siweicloud-swshop", fingerprint,
            options.ImportVersion, rows, photos);
    }

    private async Task LoadRowsAsync(string manifestPath, JsonElement manifest, List<LegacySourceRow> rows,
        List<string> fingerprintParts, CancellationToken cancellationToken)
    {
        var entity = RequireString(manifest, "entity");
        if (!SourceIdFields.TryGetValue(entity, out var sourceIdField))
            throw new LegacyMigrationException($"未登记来源主键：{entity}。");
        ValidateCommonManifest(manifest);
        var rowsFile = RequireString(manifest, "rowsFile");
        var expectedHash = RequireString(manifest, "rowsSha256");
        var path = ResolveChild(manifestPath, rowsFile);
        var actualHash = await SecureFile.Sha256Async(path, cancellationToken);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new LegacyMigrationException($"模块 {entity} 的逐行摘要不一致。");
        fingerprintParts.Add($"{entity}:{actualHash}");
        var plaintext = await payloadStore.ReadEncryptedTextAsync(path, cancellationToken);
        var count = 0;
        try
        {
            foreach (var line in plaintext.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var rowDocument = JsonDocument.Parse(line);
                var root = rowDocument.RootElement;
                if (!root.TryGetProperty(sourceIdField, out var idElement))
                    throw new LegacyMigrationException($"模块 {entity} 缺少来源主键。");
                var sourceId = Scalar(idElement);
                if (string.IsNullOrWhiteSpace(sourceId))
                    throw new LegacyMigrationException($"模块 {entity} 包含空来源主键。");
                var fields = root.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => ScalarOrNull(property.Value),
                    StringComparer.Ordinal);
                var rowHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(line)));
                rows.Add(new LegacySourceRow(entity, sourceId, rowHash, fields));
                count++;
            }
        }
        finally
        {
            plaintext = string.Empty;
        }
        if (count != manifest.GetProperty("rowCount").GetInt32())
            throw new LegacyMigrationException($"模块 {entity} 的记录数不一致。");
    }

    private async Task LoadPhotosAsync(string manifestPath, JsonElement manifest, List<LegacySourcePhoto> photos,
        List<string> fingerprintParts, CancellationToken cancellationToken)
    {
        ValidateCommonManifest(manifest);
        foreach (var photo in manifest.GetProperty("photos").EnumerateArray())
        {
            var sourceCustomerId = photo.GetProperty("sourceCustomerId").GetInt64().ToString(CultureInfo.InvariantCulture);
            var slot = photo.GetProperty("slot").GetInt32();
            var contentType = RequireString(photo, "contentType");
            var plainHash = RequireString(photo, "plainSha256");
            var encryptedHash = RequireString(photo, "encryptedSha256");
            var path = ResolveChild(manifestPath, RequireString(photo, "file"));
            if (await SecureFile.Sha256Async(path, cancellationToken) != encryptedHash)
                throw new LegacyMigrationException("顾客照片密文摘要不一致。");
            var bytes = await payloadStore.ReadEncryptedBytesAsync(path, cancellationToken);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != plainHash)
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw new LegacyMigrationException("顾客照片明文摘要不一致。");
            }
            photos.Add(new LegacySourcePhoto(sourceCustomerId, slot, contentType, plainHash, bytes));
            fingerprintParts.Add($"customer-photos:{sourceCustomerId}:{slot}:{encryptedHash}");
        }
        if (photos.Count != manifest.GetProperty("photoCount").GetInt32())
            throw new LegacyMigrationException("顾客照片清单数量不一致。");
    }

    private static void ValidateCommonManifest(JsonElement manifest)
    {
        if (manifest.GetProperty("schemaVersion").GetInt32() != 1 ||
            RequireString(manifest, "sourceHost") != LegacyEndpointPolicy.Origin.Host ||
            RequireString(manifest, "encryption") != "AES-256-GCM/ERPLEG1")
            throw new LegacyMigrationException("导入清单未通过安全校验。");
    }

    private static string ResolveChild(string manifestPath, string relative)
    {
        if (relative != Path.GetFileName(relative)) throw new LegacyMigrationException("导入清单包含非法相对路径。");
        var directory = Path.GetFullPath(Path.GetDirectoryName(manifestPath)!);
        var path = Path.GetFullPath(Path.Combine(directory, relative));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
            throw new LegacyMigrationException("导入清单引用文件不存在或越过目录。");
        return path;
    }

    private static string RequireString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new LegacyMigrationException($"导入清单缺少 {property}。");

    private static string Scalar(JsonElement element) => ScalarOrNull(element) ?? string.Empty;

    private static string? ScalarOrNull(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
        _ => throw new LegacyMigrationException("来源字段包含不支持的复合值。"),
    };
}
