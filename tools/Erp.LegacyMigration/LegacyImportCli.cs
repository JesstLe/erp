using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Erp.Application.LegacyMigration;
using Erp.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
                $"导入预检完成：品牌={options.TenantCode}，来源记录={dataset.Rows.Count}，顾客照片={dataset.Photos.Count}，护理照片={dataset.CarePhotos?.Count ?? 0}，模式={(options.Apply ? "执行" : "干跑")}。");

            var builder = Host.CreateApplicationBuilder();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddErpInfrastructure(builder.Configuration, builder.Environment);
            await using var provider = builder.Services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ILegacyImportService>();
            var result = await service.ImportAsync(new LegacyImportCommand(
                dataset,
                !options.Apply,
                options.ConfirmedTargetTenantCode,
                options.SyncMappedStores,
                options.ReconcileExistingCustomers,
                options.FinancialIncrementalSync,
                options.FinancialRebaseline,
                options.ExpectedCurrentPrincipalMinor,
                options.ExpectedCurrentBonusMinor,
                options.ExpectedMappedCustomers), cancellationToken);
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
            {
                foreach (var photo in dataset.Photos) CryptographicOperations.ZeroMemory(photo.Content);
                foreach (var photo in dataset.CarePhotos ?? []) CryptographicOperations.ZeroMemory(photo.Content);
            }
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }
}

public sealed record LegacyImportOptions(
    IReadOnlyList<string> InputDirectories,
    string TenantCode,
    bool Apply,
    string ImportVersion,
    IReadOnlyDictionary<string, string>? StoreMappings = null,
    string? ConfirmedTargetTenantCode = null,
    bool SyncMappedStores = false,
    bool ReconcileExistingCustomers = false,
    bool FinancialIncrementalSync = false,
    bool FinancialRebaseline = false,
    long? ExpectedCurrentPrincipalMinor = null,
    long? ExpectedCurrentBonusMinor = null,
    int? ExpectedMappedCustomers = null)
{
    public static LegacyImportOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "import")
            throw new LegacyMigrationException("用法：import --input 导出目录 [--input 导出目录] --tenant 品牌编码 [--confirm-target 品牌编码] [--apply]");
        var inputs = new List<string>();
        string? tenant = null;
        var apply = false;
        var version = "legacy-import-v1";
        string? confirmedTarget = null;
        var syncMappedStores = false;
        var reconcileExistingCustomers = false;
        var financialIncrementalSync = false;
        var financialRebaseline = false;
        long? expectedCurrentPrincipalMinor = null;
        long? expectedCurrentBonusMinor = null;
        int? expectedMappedCustomers = null;
        var storeMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input": inputs.Add(Next(args, ref index)); break;
                case "--tenant": tenant = Next(args, ref index).ToUpperInvariant(); break;
                case "--confirm-target": confirmedTarget = Next(args, ref index).ToUpperInvariant(); break;
                case "--version": version = Next(args, ref index); break;
                case "--store-map":
                {
                    var mapping = Next(args, ref index).Split('=', 2, StringSplitOptions.TrimEntries);
                    if (mapping.Length != 2 || mapping[0].Length is < 1 or > 160 ||
                        mapping[1].Length is < 1 or > 32 || mapping[0].Any(char.IsControl) ||
                        mapping[1].Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')) ||
                        !storeMappings.TryAdd(mapping[0], mapping[1].ToUpperInvariant()) ||
                        storeMappings.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != storeMappings.Count)
                        throw new LegacyMigrationException("门店映射必须使用唯一的 来源门店ID=目标门店编码。");
                    break;
                }
                case "--sync-mapped-stores": syncMappedStores = true; break;
                case "--reconcile-existing-customers": reconcileExistingCustomers = true; break;
                case "--financial-incremental": financialIncrementalSync = true; break;
                case "--financial-rebaseline": financialRebaseline = true; break;
                case "--expected-current-principal-minor":
                    expectedCurrentPrincipalMinor = ParseNonNegativeLong(Next(args, ref index));
                    break;
                case "--expected-current-bonus-minor":
                    expectedCurrentBonusMinor = ParseNonNegativeLong(Next(args, ref index));
                    break;
                case "--expected-mapped-customers":
                    expectedMappedCustomers = ParsePositiveInt(Next(args, ref index));
                    break;
                case "--apply": apply = true; break;
                default: throw new LegacyMigrationException($"不支持的导入参数：{args[index]}");
            }
        }
        if (tenant is null || tenant.Length is < 3 or > 32 ||
            tenant.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new LegacyMigrationException("目标品牌编码格式无效。");
        if (!string.Equals(tenant, "B01", StringComparison.Ordinal) &&
            !string.Equals(confirmedTarget, tenant, StringComparison.Ordinal))
            throw new LegacyMigrationException("非测试品牌必须使用 --confirm-target 重复确认精确的目标品牌编码。");
        if (syncMappedStores && storeMappings.Count == 0)
            throw new LegacyMigrationException("同步映射门店时必须提供门店映射。");
        if (financialIncrementalSync && (syncMappedStores || reconcileExistingCustomers))
            throw new LegacyMigrationException("金额增量同步不能与首次迁移开关同时使用。");
        if (financialRebaseline && (financialIncrementalSync || syncMappedStores || reconcileExistingCustomers))
            throw new LegacyMigrationException("金额重建不能与增量同步或首次迁移开关同时使用。");
        if (financialRebaseline && (expectedCurrentPrincipalMinor is null ||
                expectedCurrentBonusMinor is null || expectedMappedCustomers is null))
            throw new LegacyMigrationException(
                "金额重建必须同时提供当前本金、赠送金和已映射顾客数量护栏。");
        if (!financialRebaseline && (expectedCurrentPrincipalMinor is not null ||
                expectedCurrentBonusMinor is not null || expectedMappedCustomers is not null))
            throw new LegacyMigrationException("金额护栏参数只能用于金额重建。");
        if (inputs.Count is 0 or > 20) throw new LegacyMigrationException("导入必须提供1到20个来源目录。");
        var fullInputs = inputs.Select(path =>
        {
            if (!Path.IsPathFullyQualified(path)) throw new LegacyMigrationException("导入目录必须使用绝对路径。");
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) throw new LegacyMigrationException("导入目录不存在。");
            return full;
        }).Distinct(StringComparer.Ordinal).ToArray();
        if (version.Length is < 3 or > 40 || version.Any(char.IsControl))
            throw new LegacyMigrationException("导入版本格式无效。");
        return new LegacyImportOptions(fullInputs, tenant, apply, version, storeMappings,
            confirmedTarget, syncMappedStores, reconcileExistingCustomers, financialIncrementalSync,
            financialRebaseline, expectedCurrentPrincipalMinor, expectedCurrentBonusMinor,
            expectedMappedCustomers);
    }

    private static string Next(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length) throw new LegacyMigrationException("导入参数缺少值。");
        return args[index];
    }

    private static long ParseNonNegativeLong(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : throw new LegacyMigrationException("金额护栏必须是非负的最小货币单位整数。");

    private static int ParsePositiveInt(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new LegacyMigrationException("已映射顾客数量护栏必须是正整数。");
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
            ["care-records"] = "bill_id",
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
        var carePhotos = new List<LegacySourceCarePhoto>();
        var fingerprintParts = new List<string>();
        var entities = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (var manifestPath in manifests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
                var entity = document.RootElement.GetProperty("entity").GetString()
                    ?? throw new LegacyMigrationException("导入清单缺少实体名。");
                if (!entities.Add(entity)) throw new LegacyMigrationException($"导入包含重复模块：{entity}。");
                if (entity == "customer-photos")
                {
                    await LoadPhotosAsync(
                        manifestPath,
                        document.RootElement,
                        photos,
                        fingerprintParts,
                        cancellationToken);
                    continue;
                }
                if (entity == "care-record-photos")
                {
                    await LoadCarePhotosAsync(
                        manifestPath,
                        document.RootElement,
                        carePhotos,
                        fingerprintParts,
                        cancellationToken);
                    continue;
                }
                await LoadRowsAsync(manifestPath, document.RootElement, rows, fingerprintParts, cancellationToken);
            }

            ValidatePhotoRelationships(rows, photos, carePhotos);
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join('\n', fingerprintParts
                    .Concat((options.StoreMappings ?? new Dictionary<string, string>())
                        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(item => $"store-map:{item.Key}:{item.Value}"))
                    .Append($"import-version:{options.ImportVersion}")
                    .Append($"financial-incremental:{options.FinancialIncrementalSync}")
                    .Append($"financial-rebaseline:{options.FinancialRebaseline}")
                    .Append($"expected-current-principal-minor:{options.ExpectedCurrentPrincipalMinor}")
                    .Append($"expected-current-bonus-minor:{options.ExpectedCurrentBonusMinor}")
                    .Append($"expected-mapped-customers:{options.ExpectedMappedCustomers}")
                    .Order(StringComparer.Ordinal)))));
            return new LegacyImportDataset(options.TenantCode, "siweicloud-swshop", fingerprint,
                options.ImportVersion, rows, photos, carePhotos, options.StoreMappings);
        }
        catch
        {
            foreach (var photo in photos)
            {
                CryptographicOperations.ZeroMemory(photo.Content);
            }
            foreach (var photo in carePhotos)
            {
                CryptographicOperations.ZeroMemory(photo.Content);
            }
            throw;
        }
    }

    private async Task LoadCarePhotosAsync(
        string manifestPath,
        JsonElement manifest,
        List<LegacySourceCarePhoto> photos,
        List<string> fingerprintParts,
        CancellationToken cancellationToken)
    {
        ValidateCommonManifest(manifest);
        EnsureNoFailedIds(manifest, "failedCareRecordIds", "护理照片清单仍含失败记录，拒绝不完整导入。");
        foreach (var photo in manifest.GetProperty("photos").EnumerateArray())
        {
            var sourceId = photo.GetProperty("sourceCareRecordId").GetInt64()
                .ToString(CultureInfo.InvariantCulture);
            var slot = photo.GetProperty("slot").GetInt32();
            var contentType = RequireString(photo, "contentType");
            var plainHash = RequireString(photo, "plainSha256");
            var encryptedHash = RequireString(photo, "encryptedSha256");
            ValidatePhotoMetadata(sourceId, slot, contentType, plainHash, encryptedHash);
            var path = ResolveChild(manifestPath, RequireString(photo, "file"));
            if (await SecureFile.Sha256Async(path, cancellationToken) != encryptedHash)
                throw new LegacyMigrationException("护理照片密文摘要不一致。");
            var bytes = await payloadStore.ReadEncryptedBytesAsync(path, cancellationToken);
            if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != plainHash)
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw new LegacyMigrationException("护理照片明文摘要不一致。");
            }
            photos.Add(new LegacySourceCarePhoto(sourceId, slot, contentType, plainHash, bytes));
            fingerprintParts.Add($"care-record-photos:{sourceId}:{slot}:{encryptedHash}");
        }
        if (photos.Count != manifest.GetProperty("photoCount").GetInt32())
            throw new LegacyMigrationException("护理照片清单数量不一致。");
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
        EnsureNoFailedIds(manifest, "failedCustomerIds", "顾客照片清单仍含失败记录，拒绝不完整导入。");
        foreach (var photo in manifest.GetProperty("photos").EnumerateArray())
        {
            var sourceCustomerId = photo.GetProperty("sourceCustomerId").GetInt64().ToString(CultureInfo.InvariantCulture);
            var slot = photo.GetProperty("slot").GetInt32();
            var contentType = RequireString(photo, "contentType");
            var plainHash = RequireString(photo, "plainSha256");
            var encryptedHash = RequireString(photo, "encryptedSha256");
            ValidatePhotoMetadata(sourceCustomerId, slot, contentType, plainHash, encryptedHash);
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

    private static void EnsureNoFailedIds(JsonElement manifest, string property, string message)
    {
        if (manifest.TryGetProperty(property, out var failed) &&
            failed.ValueKind == JsonValueKind.Array &&
            failed.GetArrayLength() > 0)
        {
            throw new LegacyMigrationException(message);
        }
    }

    private static void ValidatePhotoMetadata(
        string sourceId,
        int slot,
        string contentType,
        string plainHash,
        string encryptedHash)
    {
        if (!long.TryParse(sourceId, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId) ||
            numericId <= 0 || slot is < 1 or > 2)
            throw new LegacyMigrationException("照片清单包含无效来源主键或槽位。");
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new LegacyMigrationException("照片清单包含不允许的图片格式。");
        if (!IsSha256(plainHash) || !IsSha256(encryptedHash))
            throw new LegacyMigrationException("照片清单包含无效摘要。");
    }

    private static void ValidatePhotoRelationships(
        IReadOnlyList<LegacySourceRow> rows,
        List<LegacySourcePhoto> customerPhotos,
        List<LegacySourceCarePhoto> carePhotos)
    {
        var customerIds = rows.Where(row => row.Entity == "customers")
            .Select(row => row.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        var careIds = rows.Where(row => row.Entity == "care-records")
            .Select(row => row.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        if (customerPhotos.Any(photo => !customerIds.Contains(photo.SourceCustomerId)) ||
            carePhotos.Any(photo => !careIds.Contains(photo.SourceCareRecordId)))
            throw new LegacyMigrationException("照片清单包含无法关联的来源记录。");
        if (customerPhotos.Select(photo => (photo.SourceCustomerId, photo.Slot)).Distinct().Count() !=
            customerPhotos.Count ||
            carePhotos.Select(photo => (photo.SourceCareRecordId, photo.Slot)).Distinct().Count() !=
            carePhotos.Count)
            throw new LegacyMigrationException("照片清单包含重复来源槽位。");
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => Uri.IsHexDigit(character));

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
