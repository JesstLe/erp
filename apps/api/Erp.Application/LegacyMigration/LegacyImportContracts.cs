namespace Erp.Application.LegacyMigration;

public sealed record LegacySourceRow(
    string Entity,
    string SourceId,
    string SourceSha256,
    IReadOnlyDictionary<string, string?> Fields);

public sealed record LegacySourcePhoto(
    string SourceCustomerId,
    int Slot,
    string ContentType,
    string PlainSha256,
    byte[] Content);

public sealed record LegacySourceCarePhoto(
    string SourceCareRecordId,
    int Slot,
    string ContentType,
    string PlainSha256,
    byte[] Content);

public sealed record LegacyImportDataset(
    string TenantCode,
    string SourceSystem,
    string SourceFingerprintSha256,
    string ImportVersion,
    IReadOnlyList<LegacySourceRow> Rows,
    IReadOnlyList<LegacySourcePhoto> Photos,
    IReadOnlyList<LegacySourceCarePhoto>? CarePhotos = null,
    IReadOnlyDictionary<string, string>? StoreSourceToTargetCodes = null);

public sealed record LegacyImportCommand(
    LegacyImportDataset Dataset,
    bool DryRun,
    string? ConfirmedTargetTenantCode = null,
    bool SyncMappedStores = false,
    bool ReconcileExistingCustomers = false,
    bool FinancialIncrementalSync = false,
    bool FinancialRebaseline = false,
    long? ExpectedCurrentPrincipalMinor = null,
    long? ExpectedCurrentBonusMinor = null,
    int? ExpectedMappedCustomers = null);

public sealed record LegacyImportResult(
    Guid RunId,
    bool DryRun,
    bool AlreadyCompleted,
    IReadOnlyDictionary<string, int> Created,
    IReadOnlyDictionary<string, int> Skipped,
    IReadOnlyDictionary<string, int> Exceptions);

public interface ILegacyImportService
{
    Task<LegacyImportResult> ImportAsync(LegacyImportCommand command, CancellationToken cancellationToken);
}
