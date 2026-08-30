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
                $"顾客照片导出完成：档案={photoResult.CustomerCount}，照片={photoResult.PhotoCount}，缺失={photoResult.MissingCount}，失败={photoResult.FailedCount}。");

            if (options.SkipCarePhotos)
            {
                await output.WriteLineAsync("护理照片已按本次迁移范围明确跳过。");
            }
            else
            {
                var carePhotoEngine = new LegacyCarePhotoExportEngine(session, payloadStore, output);
                var carePhotoResult = await carePhotoEngine.ExportAsync(options, cancellationToken);
                await output.WriteLineAsync(
                    $"护理照片导出完成：记录={carePhotoResult.CareRecordCount}，照片={carePhotoResult.PhotoCount}，缺失={carePhotoResult.MissingCount}，失败={carePhotoResult.FailedCount}。");
            }
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
    long? ProbeCustomerId,
    bool SkipCarePhotos)
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
        var skipCarePhotos = false;
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
                case "--skip-care-photos":
                    skipCarePhotos = bool.TryParse(value, out var parsedSkipCarePhotos)
                        ? parsedSkipCarePhotos
                        : throw new LegacyMigrationException("--skip-care-photos 必须是 true 或 false。");
                    break;
                default: throw new LegacyMigrationException($"不支持的参数：{name}");
            }
        }

        if (input is null || output is null || !Path.IsPathFullyQualified(input) || !Path.IsPathFullyQualified(output))
            throw new LegacyMigrationException("extras 的 --input 和 --output 必须是绝对路径。");
        var fullInput = Path.GetFullPath(input);
        if (!Directory.Exists(fullInput)) throw new LegacyMigrationException("顾客导出目录不存在。");
        return new LegacyExtraExportOptions(fullInput, Path.GetFullPath(output), pageSize, maxPages, delay, captcha,
            probeCustomerId, skipCarePhotos);
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
        checkpoint = checkpoint with { FailedCustomerIds = checkpoint.FailedCustomerIds ?? [] };
        await VerifyArtifactsAsync(directory, checkpoint, cancellationToken);

        foreach (var sourceCustomerId in checkpoint.FailedCustomerIds.ToArray())
        {
            try
            {
                var retry = await ExportCustomerPhotosAsync(
                    sourceCustomerId,
                    directory,
                    options.ProbeCustomerId.HasValue,
                    cancellationToken);
                checkpoint.Photos.AddRange(retry.Photos);
                if (retry.Photos.Count == 0)
                    checkpoint = checkpoint with { MissingCount = checkpoint.MissingCount + 1 };
                checkpoint.FailedCustomerIds!.Remove(sourceCustomerId);
                await SaveCheckpointAsync(checkpointPath, checkpoint, cancellationToken);
                await output.WriteLineAsync($"顾客照片索引：来源主键 {sourceCustomerId} 重试成功。");
            }
            catch (LegacyMigrationException exception) when (
                IsRetryablePhotoFailure(exception))
            {
                await output.WriteLineAsync($"顾客照片索引：来源主键 {sourceCustomerId} 重试仍失败，保留失败登记。");
            }
        }

        while (checkpoint.ProcessedCustomerIds.Count < customers.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = checkpoint.ProcessedCustomerIds.Count;
            // The legacy PHP session serializes requests and can stall when the same session is read concurrently.
            // Keep one reviewed GET in flight and rely on the durable checkpoint for throughput and recovery.
            var sourceCustomerId = customers[start];
            LegacyCustomerPhotoExportResult[] results;
            try
            {
                results =
                [
                    await ExportCustomerPhotosAsync(sourceCustomerId, directory, options.ProbeCustomerId.HasValue,
                        cancellationToken)
                ];
            }
            catch (LegacyMigrationException exception) when (
                IsRetryablePhotoFailure(exception))
            {
                checkpoint.FailedCustomerIds!.Add(sourceCustomerId);
                checkpoint.ProcessedCustomerIds.Add(sourceCustomerId);
                await SaveCheckpointAsync(checkpointPath, checkpoint, cancellationToken);
                await output.WriteLineAsync($"顾客照片索引：来源主键 {sourceCustomerId} 读取失败，已登记并继续。");
                continue;
            }
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
            checkpoint.Photos,
            checkpoint.FailedCustomerIds);
        await SecureFile.WriteTextAtomicAsync(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            cancellationToken);
        return new LegacyPhotoExportResult(customers.Count, checkpoint.Photos.Count, checkpoint.MissingCount,
            checkpoint.FailedCustomerIds!.Count);
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
        => ParsePhotoUris(html, "member", "/swshop/base/member.php");

    internal static List<LegacyPhotoReference> ParseCarePhotoUris(string html)
        => ParsePhotoUris(html, "nurse", "/swshop/vip/nurse.php");

    private static List<LegacyPhotoReference> ParsePhotoUris(string html, string folder, string basePath)
    {
        var result = new List<LegacyPhotoReference>();
        var usedUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match image in PicturePathPattern().Matches(html))
        {
            var decoded = WebUtility.HtmlDecode(image.Groups["src"].Value);
            var uri = new Uri(new Uri(LegacyEndpointPolicy.Origin, basePath), decoded);
            if (!uri.AbsolutePath.Contains($"/{folder}/", StringComparison.Ordinal)) continue;
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
            checkpoint.ProcessedCustomerIds.Distinct().Count() != checkpoint.ProcessedCustomerIds.Count ||
            (checkpoint.FailedCustomerIds?.Except(checkpoint.ProcessedCustomerIds).Any() ?? false))
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

    private static bool IsRetryablePhotoFailure(LegacyMigrationException exception) =>
        exception.Message.Contains("照片读取超时", StringComparison.Ordinal) ||
        exception.Message.Contains("照片读取暂时失败", StringComparison.Ordinal);

    [GeneratedRegex("(?<src>(?:https://app5\\.siweicloud\\.com)?(?:\\.\\./|/swshop/)?picture/[A-Za-z0-9_./-]+\\.(?:jpe?g|png|webp))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex PicturePathPattern();
}

public sealed record LegacyPhotoReference(int Slot, Uri Uri);
public sealed record LegacyPhotoArtifact(long SourceCustomerId, int Slot, string ContentType, int PlainBytes,
    string PlainSha256, string File, string EncryptedSha256);
public sealed record LegacyPhotoCheckpoint(int SchemaVersion, Guid RunId, int CustomerCount,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, List<long> ProcessedCustomerIds,
    List<LegacyPhotoArtifact> Photos, int MissingCount, List<long>? FailedCustomerIds = null);
public sealed record LegacyPhotoManifest(int SchemaVersion, Guid RunId, string Entity, string SourceHost,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, int CustomerCount, int PhotoCount,
    int MissingCount, string Encryption, IReadOnlyList<LegacyPhotoArtifact> Photos,
    IReadOnlyList<long>? FailedCustomerIds = null);
public sealed record LegacyPhotoExportResult(int CustomerCount, int PhotoCount, int MissingCount, int FailedCount);
public sealed record LegacyCustomerPhotoExportResult(long SourceCustomerId, IReadOnlyList<LegacyPhotoArtifact> Photos);

public sealed class LegacyCarePhotoExportEngine(
    LegacySessionClient session,
    EncryptedPayloadStore payloadStore,
    TextWriter output)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<LegacyCarePhotoExportResult> ExportAsync(
        LegacyExtraExportOptions options,
        CancellationToken cancellationToken)
    {
        var sourceIds = await ReadCareRecordIdsAsync(options.OutputDirectory, cancellationToken);
        var directory = Path.Combine(options.OutputDirectory, "care-record-photos");
        Directory.CreateDirectory(directory);
        SecureOutputDirectory.Restrict(directory);
        var checkpointPath = Path.Combine(directory, "checkpoint.json");
        var checkpoint = await LoadCheckpointAsync(checkpointPath, sourceIds.Count, cancellationToken);
        checkpoint = checkpoint with { FailedCareRecordIds = checkpoint.FailedCareRecordIds ?? [] };
        await VerifyArtifactsAsync(directory, checkpoint, cancellationToken);

        foreach (var sourceId in checkpoint.FailedCareRecordIds!.ToArray())
        {
            try
            {
                var artifacts = await ExportCarePhotosAsync(sourceId, directory, cancellationToken);
                checkpoint.Photos.AddRange(artifacts);
                if (artifacts.Count == 0)
                    checkpoint = checkpoint with { MissingCount = checkpoint.MissingCount + 1 };
                checkpoint.FailedCareRecordIds!.Remove(sourceId);
                await SaveCareCheckpointAsync(checkpointPath, checkpoint, cancellationToken);
                await output.WriteLineAsync($"护理照片索引：来源主键 {sourceId} 重试成功。");
            }
            catch (LegacyMigrationException exception) when (
                IsRetryablePhotoFailure(exception))
            {
                await output.WriteLineAsync($"护理照片索引：来源主键 {sourceId} 重试仍失败，保留失败登记。");
            }
        }

        while (checkpoint.ProcessedCareRecordIds.Count < sourceIds.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = checkpoint.ProcessedCareRecordIds.Count;
            var batch = sourceIds.Skip(start).Take(20).ToArray();
            var discoveries = new List<LegacyCarePhotoDiscovery>(batch.Length);
            var timedOutIds = new HashSet<long>();
            foreach (var sourceId in batch)
            {
                try
                {
                    discoveries.Add(await DiscoverCarePhotosAsync(sourceId, cancellationToken));
                }
                catch (LegacyMigrationException exception) when (
                    IsRetryablePhotoFailure(exception))
                {
                    timedOutIds.Add(sourceId);
                }
            }

            using var imageConcurrency = new SemaphoreSlim(8, 8);
            var downloaded = await Task.WhenAll(discoveries.Select(async discovery =>
            {
                try
                {
                    return new LegacyCarePhotoBatchResult(
                        discovery.SourceId,
                        await ExportCarePhotoItemsAsync(
                            discovery.SourceId,
                            discovery.Items,
                            directory,
                            imageConcurrency,
                            cancellationToken),
                        TimedOut: false);
                }
                catch (LegacyMigrationException exception) when (
                    IsRetryablePhotoFailure(exception))
                {
                    return new LegacyCarePhotoBatchResult(discovery.SourceId, [], TimedOut: true);
                }
            }));
            var results = downloaded
                .Concat(timedOutIds.Select(sourceId => new LegacyCarePhotoBatchResult(sourceId, [], TimedOut: true)))
                .OrderBy(result => Array.IndexOf(batch, result.SourceId))
                .ToArray();

            foreach (var result in results)
            {
                checkpoint.ProcessedCareRecordIds.Add(result.SourceId);
                if (result.TimedOut)
                {
                    checkpoint.FailedCareRecordIds!.Add(result.SourceId);
                    await output.WriteLineAsync(
                        $"护理照片索引：来源主键 {result.SourceId} 读取失败，已登记并继续。");
                    continue;
                }
                checkpoint.Photos.AddRange(result.Artifacts);
                if (result.Artifacts.Count == 0)
                    checkpoint = checkpoint with { MissingCount = checkpoint.MissingCount + 1 };
            }
            await SaveCareCheckpointAsync(checkpointPath, checkpoint, cancellationToken);
            if (start / 50 != checkpoint.ProcessedCareRecordIds.Count / 50 ||
                checkpoint.ProcessedCareRecordIds.Count == sourceIds.Count)
            {
                await output.WriteLineAsync(
                    $"护理照片索引：已检查 {checkpoint.ProcessedCareRecordIds.Count}/{sourceIds.Count}，发现 {checkpoint.Photos.Count} 张。");
            }
            if (options.DelayMilliseconds > 0)
                await Task.Delay(options.DelayMilliseconds, cancellationToken);
        }

        // Retry transient failures once more after the full scan. This keeps one valid
        // authenticated session useful through completion while still retaining any
        // persistent failures in the final manifest for the importer to reject.
        foreach (var sourceId in checkpoint.FailedCareRecordIds!.ToArray())
        {
            try
            {
                var artifacts = await ExportCarePhotosAsync(sourceId, directory, cancellationToken);
                checkpoint.Photos.AddRange(artifacts);
                if (artifacts.Count == 0)
                    checkpoint = checkpoint with { MissingCount = checkpoint.MissingCount + 1 };
                checkpoint.FailedCareRecordIds!.Remove(sourceId);
                await SaveCareCheckpointAsync(checkpointPath, checkpoint, cancellationToken);
                await output.WriteLineAsync($"护理照片索引：来源主键 {sourceId} 末轮重试成功。");
            }
            catch (LegacyMigrationException exception) when (IsRetryablePhotoFailure(exception))
            {
                await output.WriteLineAsync($"护理照片索引：来源主键 {sourceId} 末轮重试仍失败。");
            }
        }

        checkpoint = checkpoint with { CompletedAtUtc = DateTimeOffset.UtcNow };
        await SecureFile.WriteTextAtomicAsync(
            checkpointPath,
            JsonSerializer.Serialize(checkpoint, JsonOptions),
            cancellationToken);
        var manifest = new LegacyCarePhotoManifest(
            1,
            checkpoint.RunId,
            "care-record-photos",
            LegacyEndpointPolicy.Origin.Host,
            checkpoint.StartedAtUtc,
            checkpoint.CompletedAtUtc.Value,
            sourceIds.Count,
            checkpoint.Photos.Count,
            checkpoint.MissingCount,
            "AES-256-GCM/ERPLEG1",
            checkpoint.Photos,
            checkpoint.FailedCareRecordIds);
        await SecureFile.WriteTextAtomicAsync(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            cancellationToken);
        return new LegacyCarePhotoExportResult(sourceIds.Count, checkpoint.Photos.Count, checkpoint.MissingCount,
            checkpoint.FailedCareRecordIds!.Count);
    }

    private async Task<List<LegacyCarePhotoArtifact>> ExportCarePhotosAsync(
        long sourceId,
        string directory,
        CancellationToken cancellationToken)
    {
        var discovery = await DiscoverCarePhotosAsync(sourceId, cancellationToken);
        using var imageConcurrency = new SemaphoreSlim(2, 2);
        return await ExportCarePhotoItemsAsync(
            sourceId,
            discovery.Items,
            directory,
            imageConcurrency,
            cancellationToken);
    }

    private async Task<LegacyCarePhotoDiscovery> DiscoverCarePhotosAsync(
        long sourceId,
        CancellationToken cancellationToken)
    {
        var html = await session.GetCareRecordEditPageAsync(sourceId, cancellationToken);
        return new LegacyCarePhotoDiscovery(
            sourceId,
            LegacyCustomerPhotoExportEngine.ParseCarePhotoUris(html));
    }

    private async Task<List<LegacyCarePhotoArtifact>> ExportCarePhotoItemsAsync(
        long sourceId,
        IReadOnlyList<LegacyPhotoReference> items,
        string directory,
        SemaphoreSlim imageConcurrency,
        CancellationToken cancellationToken)
    {
        var artifacts = await Task.WhenAll(items.Select(async item =>
        {
            await imageConcurrency.WaitAsync(cancellationToken);
            try
            {
                var image = await session.GetCustomerPhotoAsync(item.Uri, cancellationToken);
                try
                {
                    var relativeFile = $"{sourceId:D10}-slot-{item.Slot}.bin.enc";
                    var path = Path.Combine(directory, relativeFile);
                    await payloadStore.WriteEncryptedBytesAsync(path, image.Bytes, cancellationToken);
                    return new LegacyCarePhotoArtifact(
                        sourceId,
                        item.Slot,
                        image.ContentType,
                        image.Bytes.Length,
                        Convert.ToHexStringLower(SHA256.HashData(image.Bytes)),
                        relativeFile,
                        await SecureFile.Sha256Async(path, cancellationToken));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(image.Bytes);
                }
            }
            finally
            {
                imageConcurrency.Release();
            }
        }));
        return artifacts.ToList();
    }

    private static Task SaveCareCheckpointAsync(
        string path,
        LegacyCarePhotoCheckpoint checkpoint,
        CancellationToken cancellationToken) =>
        SecureFile.WriteTextAtomicAsync(path, JsonSerializer.Serialize(checkpoint, JsonOptions), cancellationToken);

    private static bool IsRetryablePhotoFailure(LegacyMigrationException exception) =>
        exception.Message.Contains("照片读取超时", StringComparison.Ordinal) ||
        exception.Message.Contains("照片读取暂时失败", StringComparison.Ordinal);

    private sealed record LegacyCarePhotoBatchResult(
        long SourceId,
        IReadOnlyList<LegacyCarePhotoArtifact> Artifacts,
        bool TimedOut);

    private sealed record LegacyCarePhotoDiscovery(
        long SourceId,
        IReadOnlyList<LegacyPhotoReference> Items);

    private async Task<List<long>> ReadCareRecordIdsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(outputDirectory, "care-records", "manifest.json");
        if (!File.Exists(manifestPath)) throw new LegacyMigrationException("护理记录导出清单不存在。");
        var manifest = JsonSerializer.Deserialize<LegacyExportManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions)
            ?? throw new LegacyMigrationException("护理记录导出清单为空。");
        if (manifest.SchemaVersion != 1 || manifest.Entity != "care-records" ||
            manifest.SourceHost != LegacyEndpointPolicy.Origin.Host || manifest.Encryption != "AES-256-GCM/ERPLEG1")
            throw new LegacyMigrationException("护理记录清单未通过照片索引安全校验。");
        var rowsPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.RowsFile);
        if (await SecureFile.Sha256Async(rowsPath, cancellationToken) != manifest.RowsSha256)
            throw new LegacyMigrationException("护理记录逐行文件摘要不一致。");
        var plaintext = await payloadStore.ReadEncryptedTextAsync(rowsPath, cancellationToken);
        var result = new List<long>(manifest.RowCount);
        try
        {
            foreach (var line in plaintext.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("bill_id", out var id) ||
                    !id.TryGetInt64(out var numericId) || numericId <= 0)
                    throw new LegacyMigrationException("护理记录导出包含无效来源主键。");
                result.Add(numericId);
            }
        }
        finally
        {
            plaintext = string.Empty;
        }
        if (result.Count != manifest.RowCount || result.Distinct().Count() != result.Count)
            throw new LegacyMigrationException("护理记录来源主键数量或唯一性校验失败。");
        return result;
    }

    private static async Task<LegacyCarePhotoCheckpoint> LoadCheckpointAsync(
        string path,
        int careRecordCount,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return new LegacyCarePhotoCheckpoint(1, Guid.NewGuid(), careRecordCount, DateTimeOffset.UtcNow, null,
                [], [], 0);
        var checkpoint = JsonSerializer.Deserialize<LegacyCarePhotoCheckpoint>(
            await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
            ?? throw new LegacyMigrationException("护理照片检查点为空。");
        if (checkpoint.SchemaVersion != 1 || checkpoint.CareRecordCount != careRecordCount ||
            checkpoint.ProcessedCareRecordIds.Distinct().Count() != checkpoint.ProcessedCareRecordIds.Count ||
            (checkpoint.FailedCareRecordIds?.Except(checkpoint.ProcessedCareRecordIds).Any() ?? false))
            throw new LegacyMigrationException("护理照片检查点与护理记录导出不一致。");
        return checkpoint;
    }

    private static async Task VerifyArtifactsAsync(
        string directory,
        LegacyCarePhotoCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        foreach (var photo in checkpoint.Photos)
        {
            var path = Path.GetFullPath(Path.Combine(directory, photo.File));
            if (!path.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                !File.Exists(path) || await SecureFile.Sha256Async(path, cancellationToken) != photo.EncryptedSha256)
                throw new LegacyMigrationException("护理照片检查点文件完整性校验失败。");
        }
    }
}

public sealed record LegacyCarePhotoArtifact(long SourceCareRecordId, int Slot, string ContentType, int PlainBytes,
    string PlainSha256, string File, string EncryptedSha256);
public sealed record LegacyCarePhotoCheckpoint(int SchemaVersion, Guid RunId, int CareRecordCount,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, List<long> ProcessedCareRecordIds,
    List<LegacyCarePhotoArtifact> Photos, int MissingCount, List<long>? FailedCareRecordIds = null);
public sealed record LegacyCarePhotoManifest(int SchemaVersion, Guid RunId, string Entity, string SourceHost,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, int CareRecordCount, int PhotoCount,
    int MissingCount, string Encryption, IReadOnlyList<LegacyCarePhotoArtifact> Photos,
    IReadOnlyList<long>? FailedCareRecordIds = null);
public sealed record LegacyCarePhotoExportResult(int CareRecordCount, int PhotoCount, int MissingCount,
    int FailedCount);
