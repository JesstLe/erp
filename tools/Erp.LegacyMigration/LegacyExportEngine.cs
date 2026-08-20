using System.Text;
using System.Text.Json;

namespace Erp.LegacyMigration;

public sealed class LegacyExportEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly LegacySessionClient _session;
    private readonly EncryptedPayloadStore _payloadStore;
    private readonly TextWriter _output;

    public LegacyExportEngine(
        LegacySessionClient session,
        EncryptedPayloadStore payloadStore,
        TextWriter output)
    {
        _session = session;
        _payloadStore = payloadStore;
        _output = output;
    }

    public async Task<LegacyExportResult> ExportAsync(
        LegacyCliOptions options,
        LegacyEntityDefinition entity,
        CancellationToken cancellationToken)
    {
        var entityDirectory = Path.Combine(options.OutputDirectory, entity.Name);
        Directory.CreateDirectory(entityDirectory);
        SecureOutputDirectory.Restrict(entityDirectory);
        var checkpointPath = Path.Combine(entityDirectory, "checkpoint.json");
        var manifestPath = Path.Combine(entityDirectory, "manifest.json");

        var checkpoint = await LoadOrCreateCheckpointAsync(
            checkpointPath,
            entity,
            options.PageSize,
            cancellationToken);
        await VerifyCompletedPagesAsync(entityDirectory, checkpoint, cancellationToken);

        var nextPage = checkpoint.Pages.Count == 0 ? 1 : checkpoint.Pages.Max(page => page.Page) + 1;
        var finished = checkpoint.CompletedAtUtc is not null;
        while (!finished)
        {
            if (nextPage > options.MaxPages)
            {
                throw new LegacyMigrationException("分页数量超过安全上限，导出已停止。");
            }

            var json = await _session.GetGridPageAsync(entity, nextPage, options.PageSize, cancellationToken);
            var page = JqGridPage.Parse(json, nextPage);
            if (page.Page != nextPage)
            {
                throw new LegacyMigrationException("旧系统返回的页码与请求不一致。");
            }

            var relativeFile = $"page-{nextPage:D6}.json.enc";
            var pagePath = Path.Combine(entityDirectory, relativeFile);
            await _payloadStore.WriteEncryptedTextAsync(pagePath, json, cancellationToken);
            var sha256 = await SecureFile.Sha256Async(pagePath, cancellationToken);

            checkpoint.Pages.Add(new LegacyPageArtifact(nextPage, page.RowCount, sha256, relativeFile));
            checkpoint = checkpoint with
            {
                SourceRecords = Math.Max(checkpoint.SourceRecords, page.Records),
                TotalPages = Math.Max(checkpoint.TotalPages, page.TotalPages)
            };

            finished = page.RowCount == 0 ||
                (page.TotalPages > 0 && nextPage >= page.TotalPages) ||
                (page.TotalPages == 0 && page.RowCount < options.PageSize);
            if (finished)
            {
                checkpoint = checkpoint with { CompletedAtUtc = DateTimeOffset.UtcNow };
            }

            await SaveCheckpointAsync(checkpointPath, checkpoint, cancellationToken);
            await _output.WriteLineAsync($"{entity.Name}：第 {nextPage} 页完成，共 {page.RowCount} 条。");
            nextPage++;

            if (!finished && options.DelayMilliseconds > 0)
            {
                await Task.Delay(options.DelayMilliseconds, cancellationToken);
            }
        }

        var rowsPath = Path.Combine(entityDirectory, "rows.jsonl.enc");
        var rowCount = await BuildRowsFileAsync(entityDirectory, rowsPath, checkpoint, cancellationToken);
        var rowsSha256 = await SecureFile.Sha256Async(rowsPath, cancellationToken);
        var manifest = new LegacyExportManifest(
            SchemaVersion: 1,
            RunId: checkpoint.RunId,
            Entity: entity.Name,
            SourceHost: LegacyEndpointPolicy.Origin.Host,
            Endpoint: $"{entity.Path}?act={entity.Action}",
            StartedAtUtc: checkpoint.StartedAtUtc,
            CompletedAtUtc: checkpoint.CompletedAtUtc ?? DateTimeOffset.UtcNow,
            PageSize: checkpoint.PageSize,
            PageCount: checkpoint.Pages.Count,
            RowCount: rowCount,
            SourceRecords: checkpoint.SourceRecords,
            Encryption: "AES-256-GCM/ERPLEG1",
            RowsFile: Path.GetFileName(rowsPath),
            RowsSha256: rowsSha256,
            Pages: checkpoint.Pages);
        await SecureFile.WriteTextAtomicAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonOptions),
            cancellationToken);

        return new LegacyExportResult(entity.Name, checkpoint.Pages.Count, rowCount, manifestPath);
    }

    private static async Task<LegacyExportCheckpoint> LoadOrCreateCheckpointAsync(
        string path,
        LegacyEntityDefinition entity,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            var created = new LegacyExportCheckpoint(
                SchemaVersion: 1,
                RunId: Guid.NewGuid(),
                Entity: entity.Name,
                PageSize: pageSize,
                StartedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: null,
                TotalPages: 0,
                SourceRecords: 0,
                Pages: []);
            await SaveCheckpointAsync(path, created, cancellationToken);
            return created;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var checkpoint = JsonSerializer.Deserialize<LegacyExportCheckpoint>(json, JsonOptions)
                ?? throw new LegacyMigrationException("导出检查点为空。");
            if (checkpoint.SchemaVersion != 1 ||
                !string.Equals(checkpoint.Entity, entity.Name, StringComparison.Ordinal) ||
                checkpoint.PageSize != pageSize)
            {
                throw new LegacyMigrationException("已有检查点与当前导出参数不一致。");
            }

            return checkpoint;
        }
        catch (JsonException exception)
        {
            throw new LegacyMigrationException("导出检查点不是有效 JSON。", exception);
        }
    }

    private static async Task SaveCheckpointAsync(
        string path,
        LegacyExportCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await SecureFile.WriteTextAtomicAsync(
            path,
            JsonSerializer.Serialize(checkpoint, JsonOptions),
            cancellationToken);
    }

    private static async Task VerifyCompletedPagesAsync(
        string entityDirectory,
        LegacyExportCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var expectedPage = 1;
        foreach (var page in checkpoint.Pages.OrderBy(page => page.Page))
        {
            if (page.Page != expectedPage)
            {
                throw new LegacyMigrationException("导出检查点页码不连续。");
            }

            var path = Path.Combine(entityDirectory, page.File);
            if (!File.Exists(path))
            {
                throw new LegacyMigrationException("导出检查点引用的加密页面不存在。");
            }

            var actualHash = await SecureFile.Sha256Async(path, cancellationToken);
            if (!string.Equals(actualHash, page.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new LegacyMigrationException("导出页面校验值不一致，恢复已停止。");
            }

            expectedPage++;
        }
    }

    private async Task<int> BuildRowsFileAsync(
        string entityDirectory,
        string rowsPath,
        LegacyExportCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var rowCount = 0;
        foreach (var page in checkpoint.Pages.OrderBy(page => page.Page))
        {
            var json = await _payloadStore.ReadEncryptedTextAsync(
                Path.Combine(entityDirectory, page.File),
                cancellationToken);
            foreach (var row in JqGridPage.EnumerateRows(json))
            {
                builder.AppendLine(row);
                rowCount++;
            }
        }

        await _payloadStore.WriteEncryptedTextAsync(rowsPath, builder.ToString(), cancellationToken);
        return rowCount;
    }
}

public sealed record LegacyExportCheckpoint(
    int SchemaVersion,
    Guid RunId,
    string Entity,
    int PageSize,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalPages,
    int SourceRecords,
    List<LegacyPageArtifact> Pages);

public sealed record LegacyPageArtifact(int Page, int RowCount, string Sha256, string File);

public sealed record LegacyExportManifest(
    int SchemaVersion,
    Guid RunId,
    string Entity,
    string SourceHost,
    string Endpoint,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int PageSize,
    int PageCount,
    int RowCount,
    int SourceRecords,
    string Encryption,
    string RowsFile,
    string RowsSha256,
    IReadOnlyList<LegacyPageArtifact> Pages);

public sealed record LegacyExportResult(string Entity, int PageCount, int RowCount, string ManifestPath);
