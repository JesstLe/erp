using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Erp.LegacyMigration;

public static class LegacyProfileCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = LegacyProfileOptions.Parse(args);
            var key = LegacyExportKey.ReadFromEnvironmentOrConsole(output);
            try
            {
                var outputDirectory = Path.GetDirectoryName(options.OutputFile)
                    ?? throw new LegacyMigrationException("字段画像输出文件缺少目录。");
                SecureOutputDirectory.Prepare(outputDirectory);

                using var payloadStore = new EncryptedPayloadStore(key);
                var profiler = new LegacyFieldProfiler(payloadStore, output);
                var report = await profiler.ProfileAsync(options.InputDirectories, cancellationToken);
                var json = JsonSerializer.Serialize(report, LegacyFieldProfileReport.JsonOptions);
                await SecureFile.WriteTextAtomicAsync(options.OutputFile, json, cancellationToken);

                await output.WriteLineAsync(
                    $"字段画像完成：模块={report.EntityCount}，记录={report.TotalRows}，输出={options.OutputFile}");
                return 0;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("字段画像已取消；没有生成不完整报告。");
            return 130;
        }
        catch (LegacyMigrationException exception)
        {
            await output.WriteLineAsync($"字段画像停止：{SensitiveText.Redact(exception.Message)}");
            return 2;
        }
        finally
        {
            Environment.SetEnvironmentVariable("ERP_LEGACY_EXPORT_KEY", null);
        }
    }
}

public sealed record LegacyProfileOptions(IReadOnlyList<string> InputDirectories, string OutputFile)
{
    public static LegacyProfileOptions Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "profile", StringComparison.Ordinal))
        {
            throw new LegacyMigrationException(
                "用法：dotnet run --project tools/Erp.LegacyMigration -- profile --input 绝对导出目录 [--input 绝对导出目录] --output 绝对JSON文件");
        }

        var inputs = new List<string>();
        string? output = null;
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                throw new LegacyMigrationException($"参数 {args[index]} 缺少值。");
            }

            var name = args[index];
            var value = args[index + 1];
            switch (name)
            {
                case "--input":
                    inputs.Add(RequireAbsolutePath(name, value));
                    break;
                case "--output":
                    output = RequireAbsolutePath(name, value);
                    break;
                default:
                    throw new LegacyMigrationException($"不支持的字段画像参数：{name}");
            }
        }

        if (inputs.Count == 0)
        {
            throw new LegacyMigrationException("字段画像至少需要一个 --input 导出目录。");
        }

        if (string.IsNullOrWhiteSpace(output) ||
            !string.Equals(Path.GetExtension(output), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new LegacyMigrationException("字段画像 --output 必须是绝对 JSON 文件路径。");
        }

        var distinctInputs = inputs.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var input in distinctInputs)
        {
            if (!Directory.Exists(input))
            {
                throw new LegacyMigrationException("字段画像输入目录不存在。");
            }
        }

        return new LegacyProfileOptions(distinctInputs, Path.GetFullPath(output));
    }

    private static string RequireAbsolutePath(string name, string value)
    {
        if (!Path.IsPathFullyQualified(value))
        {
            throw new LegacyMigrationException($"参数 {name} 必须使用绝对路径。");
        }

        return Path.GetFullPath(value);
    }
}

public static class LegacyExportKey
{
    public static byte[] ReadFromEnvironmentOrConsole(TextWriter output)
    {
        var encodedKey = Environment.GetEnvironmentVariable("ERP_LEGACY_EXPORT_KEY");
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            if (Console.IsInputRedirected)
            {
                throw new LegacyMigrationException("导出加密密钥未通过安全环境变量提供，且当前终端无法隐藏输入。");
            }

            output.Write("导出加密密钥（32字节Base64，输入不回显）：");
            var buffer = new List<char>();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    output.WriteLine();
                    encodedKey = new string(buffer.ToArray());
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Count > 0)
                    {
                        buffer.RemoveAt(buffer.Count - 1);
                    }

                    continue;
                }

                if (!char.IsControl(key.KeyChar) && buffer.Count < 512)
                {
                    buffer.Add(key.KeyChar);
                }
            }
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException exception)
        {
            throw new LegacyMigrationException("导出加密密钥必须是有效的 Base64。", exception);
        }

        if (decoded.Length != 32)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new LegacyMigrationException("导出加密密钥解码后必须恰好为 32 字节。");
        }

        return decoded;
    }
}

public sealed class LegacyFieldProfiler
{
    private const long MaxManifestBytes = 1024 * 1024;
    private const long MaxEncryptedRowsBytes = 512L * 1024 * 1024;
    private const int MaxRowsPerEntity = 500_000;
    private const int MaxFieldsPerEntity = 512;
    private const int MaxPropertyNameLength = 128;
    private const int MaxRowCharacters = 2 * 1024 * 1024;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    private readonly EncryptedPayloadStore _payloadStore;
    private readonly TextWriter _output;

    public LegacyFieldProfiler(EncryptedPayloadStore payloadStore, TextWriter output)
    {
        _payloadStore = payloadStore;
        _output = output;
    }

    public async Task<LegacyFieldProfileReport> ProfileAsync(
        IReadOnlyList<string> inputDirectories,
        CancellationToken cancellationToken)
    {
        var manifestPaths = inputDirectories
            .SelectMany(input => Directory.EnumerateFiles(input, "manifest.json", SearchOption.AllDirectories))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (manifestPaths.Length == 0)
        {
            throw new LegacyMigrationException("字段画像输入中没有找到导出清单。");
        }

        var profiles = new List<LegacyEntityFieldProfile>();
        var entities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var manifestPath in manifestPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = await ProfileEntityAsync(manifestPath, cancellationToken);
            if (!entities.Add(profile.Entity))
            {
                throw new LegacyMigrationException("字段画像输入包含重复模块。");
            }

            profiles.Add(profile);
            await _output.WriteLineAsync(
                $"字段画像：模块={profile.Entity}，记录={profile.RowCount}，字段={profile.Fields.Count}，完整性=通过");
        }

        profiles.Sort((left, right) => StringComparer.Ordinal.Compare(left.Entity, right.Entity));
        return new LegacyFieldProfileReport(
            SchemaVersion: 1,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            EntityCount: profiles.Count,
            TotalRows: profiles.Sum(profile => profile.RowCount),
            Privacy: "No source values, hashes, paths, credentials, cookies or encryption keys are included.",
            Entities: profiles);
    }

    private async Task<LegacyEntityFieldProfile> ProfileEntityAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        EnsureRegularFile(manifestPath, MaxManifestBytes, "导出清单");
        LegacyExportManifest manifest;
        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            manifest = JsonSerializer.Deserialize<LegacyExportManifest>(
                manifestJson,
                LegacyFieldProfileReport.JsonOptions)
                ?? throw new LegacyMigrationException("导出清单为空。");
        }
        catch (JsonException exception)
        {
            throw new LegacyMigrationException("导出清单不是有效 JSON。", exception);
        }

        ValidateManifest(manifest);
        var entityDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new LegacyMigrationException("导出清单缺少目录。");
        var rowsPath = Path.GetFullPath(Path.Combine(entityDirectory, manifest.RowsFile));
        var expectedDirectory = Path.GetFullPath(entityDirectory) + Path.DirectorySeparatorChar;
        if (!rowsPath.StartsWith(expectedDirectory, StringComparison.Ordinal))
        {
            throw new LegacyMigrationException("导出清单的逐行文件越过模块目录。");
        }

        EnsureRegularFile(rowsPath, MaxEncryptedRowsBytes, "加密逐行文件");
        var actualHash = await SecureFile.Sha256Async(rowsPath, cancellationToken);
        if (!string.Equals(actualHash, manifest.RowsSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new LegacyMigrationException("加密逐行文件摘要与清单不一致。");
        }

        var plaintext = await _payloadStore.ReadEncryptedTextAsync(rowsPath, cancellationToken);
        try
        {
            return ProfileRows(manifest, plaintext);
        }
        finally
        {
            plaintext = string.Empty;
        }
    }

    private static LegacyEntityFieldProfile ProfileRows(LegacyExportManifest manifest, string plaintext)
    {
        var builders = new Dictionary<string, FieldProfileBuilder>(StringComparer.Ordinal);
        var rowCount = 0;
        using var reader = new StringReader(plaintext);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            rowCount++;
            if (rowCount > MaxRowsPerEntity || line.Length > MaxRowCharacters)
            {
                throw new LegacyMigrationException("字段画像记录数或单行大小超过安全上限。");
            }

            try
            {
                using var document = JsonDocument.Parse(line, DocumentOptions);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new LegacyMigrationException("字段画像只接受对象形式的逐行记录。");
                }

                var namesInRow = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    ValidatePropertyName(property.Name);
                    if (!namesInRow.Add(property.Name))
                    {
                        throw new LegacyMigrationException("字段画像记录包含重复字段名。");
                    }

                    if (!builders.TryGetValue(property.Name, out var builder))
                    {
                        if (builders.Count >= MaxFieldsPerEntity)
                        {
                            throw new LegacyMigrationException("字段画像字段数量超过安全上限。");
                        }

                        builder = new FieldProfileBuilder(property.Name);
                        builders.Add(property.Name, builder);
                    }

                    builder.Observe(property.Value);
                }
            }
            catch (JsonException exception)
            {
                throw new LegacyMigrationException("字段画像逐行记录不是有效 JSON。", exception);
            }
        }

        if (rowCount != manifest.RowCount)
        {
            throw new LegacyMigrationException("字段画像逐行数量与导出清单不一致。");
        }

        var fields = builders.Values
            .Select(builder => builder.Build(rowCount))
            .OrderBy(profile => profile.Field, StringComparer.Ordinal)
            .ToArray();
        return new LegacyEntityFieldProfile(
            manifest.Entity,
            rowCount,
            fields.Length,
            IntegrityVerified: true,
            fields);
    }

    private static void ValidateManifest(LegacyExportManifest manifest)
    {
        var rowCountMatchesSource = manifest.RowCount == manifest.SourceRecords ||
            string.Equals(manifest.Entity, LegacyEntityDefinition.PayrollData.Name, StringComparison.Ordinal) &&
            manifest.SourceRecords <= manifest.RowCount;
        if (manifest.SchemaVersion != 1 || manifest.RowCount < 0 || manifest.SourceRecords < 0 ||
            manifest.RowCount > MaxRowsPerEntity || !rowCountMatchesSource ||
            !string.Equals(manifest.SourceHost, LegacyEndpointPolicy.Origin.Host, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifest.Encryption, "AES-256-GCM/ERPLEG1", StringComparison.Ordinal) ||
            manifest.RowsSha256.Length != 64 || !manifest.RowsSha256.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(manifest.RowsFile) ||
            !string.Equals(manifest.RowsFile, Path.GetFileName(manifest.RowsFile), StringComparison.Ordinal))
        {
            throw new LegacyMigrationException("导出清单未通过字段画像安全校验。");
        }

        var entity = LegacyEntityCatalog.All.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, manifest.Entity, StringComparison.Ordinal));
        if (entity is null ||
            !string.Equals(manifest.Endpoint, $"{entity.Path}?act={entity.Action}", StringComparison.Ordinal))
        {
            throw new LegacyMigrationException("导出清单实体或端点未登记。");
        }
    }

    private static void ValidatePropertyName(string name)
    {
        if (name.Length is 0 or > MaxPropertyNameLength || name.Any(char.IsControl))
        {
            throw new LegacyMigrationException("字段画像发现无效字段名。");
        }
    }

    private static void EnsureRegularFile(string path, long maximumBytes, string label)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || file.Length > maximumBytes)
        {
            throw new LegacyMigrationException($"{label}不存在、是符号链接或超过安全大小上限。");
        }
    }
}

internal sealed partial class FieldProfileBuilder
{
    private readonly HashSet<string> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _stringShapes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _distinctDigests = new(StringComparer.Ordinal);
    private readonly byte[] _deduplicationKey = RandomNumberGenerator.GetBytes(32);

    public FieldProfileBuilder(string field)
    {
        Field = field;
    }

    private string Field { get; }
    private int PresenceCount { get; set; }
    private int NullOrBlankCount { get; set; }
    private int MaxStringLength { get; set; }

    public void Observe(JsonElement value)
    {
        PresenceCount++;
        _types.Add(value.ValueKind.ToString());

        string? normalized = null;
        if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
        {
            NullOrBlankCount++;
            return;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            normalized = value.GetString()?.Trim();
            MaxStringLength = Math.Max(MaxStringLength, normalized?.Length ?? 0);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                NullOrBlankCount++;
                IncrementShape("blank");
                return;
            }

            IncrementShape(ClassifyString(normalized));
        }
        else
        {
            normalized = value.GetRawText();
        }

        var digest = HMACSHA256.HashData(_deduplicationKey, Encoding.UTF8.GetBytes(normalized));
        _distinctDigests.Add(Convert.ToHexString(digest));
        CryptographicOperations.ZeroMemory(digest);
    }

    public LegacyFieldProfile Build(int rowCount)
    {
        var nonBlank = PresenceCount - NullOrBlankCount;
        var sensitivity = InferSensitivity(Field, _stringShapes.Keys);
        CryptographicOperations.ZeroMemory(_deduplicationKey);
        return new LegacyFieldProfile(
            Field,
            PresenceCount,
            rowCount - PresenceCount,
            NullOrBlankCount,
            nonBlank,
            _distinctDigests.Count,
            MaxStringLength,
            RequiredInSource: rowCount > 0 && nonBlank == rowCount,
            CandidateKey: rowCount > 0 && nonBlank == rowCount && _distinctDigests.Count == rowCount,
            sensitivity,
            _types.Order(StringComparer.Ordinal).ToArray(),
            _stringShapes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private void IncrementShape(string shape)
    {
        _stringShapes.TryGetValue(shape, out var count);
        _stringShapes[shape] = count + 1;
    }

    private static string ClassifyString(string value)
    {
        if (PhonePattern().IsMatch(value)) return "phone-like";
        if (EmailPattern().IsMatch(value)) return "email-like";
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return "integer-like";
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) return "decimal-like";
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
            return "date-time-like";
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase) || value is "是" or "否")
            return "boolean-like";
        if (value.Contains('<', StringComparison.Ordinal) && value.Contains('>', StringComparison.Ordinal))
            return "html-like";
        return "text";
    }

    private static string InferSensitivity(string field, IEnumerable<string> stringShapes)
    {
        var normalized = field.ToLowerInvariant();
        if (ContainsAny(normalized, "password", "passwd", "pwd", "secret", "token"))
            return "credential-risk";
        if (ContainsAny(normalized, "money", "amount", "balance", "price", "cash", "credit", "debit", "fee",
                "arrear", "bonus", "deposit", "deduct", "cmoney", "zmoney", "opmoney", "remoney") ||
            normalized is "member_store" or "emplee_pay" or "goods_buy" or "goods_sale" or "goods_ship" or
                "goods_smin" or "goods_vip")
            return "financial";
        if (ContainsAny(normalized, "mobile", "phone", "tel", "email", "address", "birth", "gender", "sex",
                "idcard", "identity", "cardno", "card_no", "membername", "customername", "username", "_addr") ||
            normalized is "member_name" or "member_code" or "emplee_name" or "emplee_code" or "shop_man" ||
            stringShapes.Any(shape => shape is "phone-like" or "email-like"))
            return "personal";
        if (normalized.EndsWith("_memo", StringComparison.Ordinal))
            return "free-text-risk";
        return "none";
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));

    [GeneratedRegex(@"^1\d{10}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}

public sealed record LegacyFieldProfileReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    int EntityCount,
    int TotalRows,
    string Privacy,
    IReadOnlyList<LegacyEntityFieldProfile> Entities)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

public sealed record LegacyEntityFieldProfile(
    string Entity,
    int RowCount,
    int FieldCount,
    bool IntegrityVerified,
    IReadOnlyList<LegacyFieldProfile> Fields);

public sealed record LegacyFieldProfile(
    string Field,
    int PresenceCount,
    int MissingCount,
    int NullOrBlankCount,
    int NonBlankCount,
    int DistinctCount,
    int MaxStringLength,
    bool RequiredInSource,
    bool CandidateKey,
    string Sensitivity,
    IReadOnlyList<string> JsonTypes,
    IReadOnlyDictionary<string, int> StringShapes);
