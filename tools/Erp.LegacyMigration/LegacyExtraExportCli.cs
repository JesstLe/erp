using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Erp.LegacyMigration;

public static class LegacyExtraExportCli
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            var options = LegacyExtraExportOptions.Parse(args);
            using var credentials = LegacyCredentials.ReadFromEnvironmentOrConsole(output);
            SecureOutputDirectory.Prepare(options.OutputDirectory);
            using var payloadStore = new EncryptedPayloadStore(credentials.ExportKey);
            using var session = new LegacySessionClient(new LegacyEndpointPolicy());

            var captchaPath = Path.Combine(options.OutputDirectory, "captcha.png");
            await session.DownloadCaptchaAsync(captchaPath, cancellationToken);
            var captcha = options.Captcha;
            if (string.IsNullOrWhiteSpace(captcha))
            {
                await output.WriteLineAsync($"验证码图片：{captchaPath}");
                await output.WriteAsync("请输入图片中的四位验证码：");
                captcha = Console.ReadLine();
            }

            LegacyMigrationCli.ValidateCaptcha(captcha);
            await session.LoginAsync(credentials.Account, credentials.Password, captcha!, cancellationToken);
            SecureFile.TryDelete(captchaPath);
            await output.WriteLineAsync("旧系统登录成功，开始只读导出护理列表和顾客照片。");

            var gridOptions = new LegacyCliOptions(
                LegacyEntityDefinition.CareRecords.Name,
                options.OutputDirectory,
                options.PageSize,
                options.MaxPages,
                options.DelayMilliseconds,
                captcha);
            var gridEngine = new LegacyExportEngine(session, payloadStore, output);
            var careResult = await gridEngine.ExportAsync(gridOptions, LegacyEntityDefinition.CareRecords, cancellationToken);
            await output.WriteLineAsync($"护理列表导出完成：记录数={careResult.RowCount}。");

            var photoEngine = new LegacyCustomerPhotoExportEngine(session, payloadStore, output);
            var photoResult = await photoEngine.ExportAsync(options, cancellationToken);
            await output.WriteLineAsync(
                $"顾客照片导出完成：档案={photoResult.CustomerCount}，照片={photoResult.PhotoCount}，缺失={photoResult.MissingCount}。");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("操作已取消；已完成照片索引可从检查点恢复。");
            return 130;
        }
        catch (LegacyMigrationException exception)
        {
            await output.WriteLineAsync($"迁移工具停止：{SensitiveText.Redact(exception.Message)}");
            return 2;
        }
        finally
        {
            LegacyCredentials.ClearProcessSecrets();
        }
    }
}

public sealed record LegacyExtraExportOptions(
    string CustomerExportDirectory,
    string OutputDirectory,
    int PageSize,
    int MaxPages,
    int DelayMilliseconds,
    string? Captcha,
    long? ProbeCustomerId)
{
    public static LegacyExtraExportOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "extras")
            throw new LegacyMigrationException("用法：extras --input 顾客导出目录 --output 安全目录 [--captcha 1234]");
        string? input = null;
        string? output = null;
        string? captcha = null;
        var pageSize = 100;
        var maxPages = 10_000;
        var delay = 100;
        long? probeCustomerId = null;
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length) throw new LegacyMigrationException($"参数 {args[index]} 缺少值。");
            var name = args[index];
            var value = args[index + 1];
            switch (name)
            {
                case "--input": input = value; break;
                case "--output": output = value; break;
                case "--captcha": captcha = value; break;
                case "--page-size": pageSize = ParseInt(name, value, 1, 200); break;
                case "--max-pages": maxPages = ParseInt(name, value, 1, 10_000); break;
                case "--delay-ms": delay = ParseInt(name, value, 0, 5_000); break;
                case "--probe-id":
                    probeCustomerId = long.TryParse(value, out var parsedId) && parsedId > 0
                        ? parsedId : throw new LegacyMigrationException("--probe-id 必须是正整数。");
                    break;
                default: throw new LegacyMigrationException($"不支持的参数：{name}");
            }
        }

        if (input is null || output is null || !Path.IsPathFullyQualified(input) || !Path.IsPathFullyQualified(output))
            throw new LegacyMigrationException("extras 的 --input 和 --output 必须是绝对路径。");
        var fullInput = Path.GetFullPath(input);
        if (!Directory.Exists(fullInput)) throw new LegacyMigrationException("顾客导出目录不存在。");
        return new LegacyExtraExportOptions(fullInput, Path.GetFullPath(output), pageSize, maxPages, delay, captcha,
            probeCustomerId);
    }

    private static int ParseInt(string name, string value, int min, int max) =>
        int.TryParse(value, out var parsed) && parsed >= min && parsed <= max
            ? parsed
            : throw new LegacyMigrationException($"参数 {name} 必须在 {min} 到 {max} 之间。");
}

public sealed partial class LegacyCustomerPhotoExportEngine(
    LegacySessionClient session,
    EncryptedPayloadStore payloadStore,
    TextWriter output)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<LegacyPhotoExportResult> ExportAsync(
        LegacyExtraExportOptions options,
        CancellationToken cancellationToken)
    {
        var customers = await ReadCustomerIdsAsync(options.CustomerExportDirectory, cancellationToken);
        if (options.ProbeCustomerId.HasValue)
        {
            if (!customers.Contains(options.ProbeCustomerId.Value))
                throw new LegacyMigrationException("照片探测主键不在顾客导出中。");
            customers = [options.ProbeCustomerId.Value];
        }
        var directory = Path.Combine(options.OutputDirectory, "customer-photos");
        Directory.CreateDirectory(directory);
        SecureOutputDirectory.Restrict(directory);
        var checkpointPath = Path.Combine(directory, "checkpoint.json");
        var checkpoint = await LoadCheckpointAsync(checkpointPath, customers.Count, cancellationToken);
        await VerifyArtifactsAsync(directory, checkpoint, cancellationToken);

        while (checkpoint.ProcessedCustomerIds.Count < customers.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = checkpoint.ProcessedCustomerIds.Count;
            // The legacy PHP session serializes requests and can stall when the same session is read concurrently.
            // Keep one reviewed GET in flight and rely on the durable checkpoint for throughput and recovery.
            var batch = customers.Skip(start).Take(1).ToArray();
            var results = await Task.WhenAll(batch.Select(sourceCustomerId =>
                ExportCustomerPhotosAsync(sourceCustomerId, directory, options.ProbeCustomerId.HasValue,
                    cancellationToken)));
            foreach (var result in results)
            {
                checkpoint.Photos.AddRange(result.Photos);
                checkpoint.ProcessedCustomerIds.Add(result.SourceCustomerId);
                if (result.Photos.Count == 0)
                    checkpoint = checkpoint with { MissingCount = checkpoint.MissingCount + 1 };
            }
            await SaveCheckpointAsync(checkpointPath, checkpoint, cancellationToken);
            if (start / 50 != checkpoint.ProcessedCustomerIds.Count / 50 ||
                checkpoint.ProcessedCustomerIds.Count == customers.Count)
                await output.WriteLineAsync(
                    $"顾客照片索引：已检查 {checkpoint.ProcessedCustomerIds.Count}/{customers.Count}，发现 {checkpoint.Photos.Count} 张。");
            if (options.DelayMilliseconds > 0)
                await Task.Delay(options.DelayMilliseconds, cancellationToken);
        }

        checkpoint = checkpoint with { CompletedAtUtc = DateTimeOffset.UtcNow };
        await SaveCheckpointAsync(checkpointPath, checkpoint, cancellationToken);
        var manifest = new LegacyPhotoManifest(
            1,
            checkpoint.RunId,
            "customer-photos",
            LegacyEndpointPolicy.Origin.Host,
            checkpoint.StartedAtUtc,
            checkpoint.CompletedAtUtc.Value,
            customers.Count,
            checkpoint.Photos.Count,
            checkpoint.MissingCount,
            "AES-256-GCM/ERPLEG1",
            checkpoint.Photos);
        await SecureFile.WriteTextAtomicAsync(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            cancellationToken);
        return new LegacyPhotoExportResult(customers.Count, checkpoint.Photos.Count, checkpoint.MissingCount);
    }

    private async Task<LegacyCustomerPhotoExportResult> ExportCustomerPhotosAsync(
        long sourceCustomerId,
        string directory,
        bool saveProbePage,
        CancellationToken cancellationToken)
    {
        var html = await session.GetCustomerEditPageAsync(sourceCustomerId, cancellationToken);
        if (saveProbePage)
            await payloadStore.WriteEncryptedTextAsync(
                Path.Combine(directory, "probe-page.html.enc"), html, cancellationToken);
        var photos = new List<LegacyPhotoArtifact>();
        foreach (var item in ParsePhotoUris(html))
        {
            var image = await session.GetCustomerPhotoAsync(item.Uri, cancellationToken);
            try
            {
                var relativeFile = $"{sourceCustomerId:D10}-slot-{item.Slot}.bin.enc";
                var path = Path.Combine(directory, relativeFile);
                await payloadStore.WriteEncryptedBytesAsync(path, image.Bytes, cancellationToken);
                photos.Add(new LegacyPhotoArtifact(
                    sourceCustomerId,
                    item.Slot,
                    image.ContentType,
                    image.Bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(image.Bytes)),
                    relativeFile,
                    await SecureFile.Sha256Async(path, cancellationToken)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(image.Bytes);
            }
        }
        return new LegacyCustomerPhotoExportResult(sourceCustomerId, photos);
    }

    private async Task<List<long>> ReadCustomerIdsAsync(string inputDirectory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(inputDirectory, "customers", "manifest.json");
        if (!File.Exists(manifestPath)) manifestPath = Path.Combine(inputDirectory, "manifest.json");
        if (!File.Exists(manifestPath)) throw new LegacyMigrationException("顾客导出清单不存在。");
        var manifest = JsonSerializer.Deserialize<LegacyExportManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new LegacyMigrationException("顾客导出清单为空。");
        if (manifest.SchemaVersion != 1 || manifest.Entity != "customers" ||
            manifest.SourceHost != LegacyEndpointPolicy.Origin.Host || manifest.Encryption != "AES-256-GCM/ERPLEG1")
            throw new LegacyMigrationException("顾客导出清单未通过照片索引安全校验。");
        var directory = Path.GetDirectoryName(manifestPath)!;
        var rowsPath = Path.GetFullPath(Path.Combine(directory, manifest.RowsFile));
        if (!rowsPath.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new LegacyMigrationException("顾客逐行文件越过导出目录。");
        if (await SecureFile.Sha256Async(rowsPath, cancellationToken) != manifest.RowsSha256)
            throw new LegacyMigrationException("顾客逐行文件摘要不一致。");
        var plaintext = await payloadStore.ReadEncryptedTextAsync(rowsPath, cancellationToken);
        var result = new List<long>(manifest.RowCount);
        try
        {
            foreach (var line in plaintext.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("member_id", out var id) || !id.TryGetInt64(out var numericId) || numericId <= 0)
                    throw new LegacyMigrationException("顾客导出包含无效来源主键。");
                result.Add(numericId);
            }
        }
        finally
        {
            plaintext = string.Empty;
        }

        if (result.Count != manifest.RowCount || result.Distinct().Count() != result.Count)
            throw new LegacyMigrationException("顾客来源主键数量或唯一性校验失败。");
        return result;
    }

    internal static List<LegacyPhotoReference> ParsePhotoUris(string html)
    {
        var result = new List<LegacyPhotoReference>();
        var usedUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match image in PicturePathPattern().Matches(html))
        {
            var decoded = WebUtility.HtmlDecode(image.Groups["src"].Value);
            var uri = new Uri(new Uri(LegacyEndpointPolicy.Origin, "/swshop/base/member.php"), decoded);
            if (!uri.AbsolutePath.StartsWith("/swshop/picture/", StringComparison.Ordinal)) continue;
            if (!usedUris.Add(uri.AbsoluteUri)) continue;
            new LegacyEndpointPolicy().EnsureAllowed(HttpMethod.Get, uri);
            result.Add(new LegacyPhotoReference(result.Count + 1, uri));
            if (result.Count == 2) break;
        }

        return result;
    }

    private static async Task<LegacyPhotoCheckpoint> LoadCheckpointAsync(
        string path, int customerCount, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new LegacyPhotoCheckpoint(1, Guid.NewGuid(), customerCount, DateTimeOffset.UtcNow, null, [], [], 0);
        var checkpoint = JsonSerializer.Deserialize<LegacyPhotoCheckpoint>(
            await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
            ?? throw new LegacyMigrationException("照片检查点为空。");
        if (checkpoint.SchemaVersion != 1 || checkpoint.CustomerCount != customerCount ||
            checkpoint.ProcessedCustomerIds.Distinct().Count() != checkpoint.ProcessedCustomerIds.Count)
            throw new LegacyMigrationException("照片检查点与顾客导出不一致。");
        return checkpoint;
    }

    private static async Task VerifyArtifactsAsync(
        string directory, LegacyPhotoCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        foreach (var photo in checkpoint.Photos)
        {
            var path = Path.GetFullPath(Path.Combine(directory, photo.File));
            if (!path.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                !File.Exists(path) || await SecureFile.Sha256Async(path, cancellationToken) != photo.EncryptedSha256)
                throw new LegacyMigrationException("照片检查点文件完整性校验失败。");
        }
    }

    private static Task SaveCheckpointAsync(
        string path, LegacyPhotoCheckpoint checkpoint, CancellationToken cancellationToken) =>
        SecureFile.WriteTextAtomicAsync(path, JsonSerializer.Serialize(checkpoint, JsonOptions), cancellationToken);

    [GeneratedRegex("(?<src>(?:https://app5\\.siweicloud\\.com)?(?:\\.\\./|/swshop/)?picture/[A-Za-z0-9_./-]+\\.(?:jpe?g|png|webp))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PicturePathPattern();
}

public sealed record LegacyPhotoReference(int Slot, Uri Uri);
public sealed record LegacyPhotoArtifact(long SourceCustomerId, int Slot, string ContentType, int PlainBytes,
    string PlainSha256, string File, string EncryptedSha256);
public sealed record LegacyPhotoCheckpoint(int SchemaVersion, Guid RunId, int CustomerCount,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, List<long> ProcessedCustomerIds,
    List<LegacyPhotoArtifact> Photos, int MissingCount);
public sealed record LegacyPhotoManifest(int SchemaVersion, Guid RunId, string Entity, string SourceHost,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, int CustomerCount, int PhotoCount,
    int MissingCount, string Encryption, IReadOnlyList<LegacyPhotoArtifact> Photos);
public sealed record LegacyPhotoExportResult(int CustomerCount, int PhotoCount, int MissingCount);
public sealed record LegacyCustomerPhotoExportResult(long SourceCustomerId, IReadOnlyList<LegacyPhotoArtifact> Photos);
