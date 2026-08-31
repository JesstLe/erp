using Erp.Application.Common;

namespace Erp.Application.Organization;

public sealed record BrandProfileDto(Guid Id, string Code, string Name, string Status, uint Version);

public sealed record StoreProfileDto(Guid Id, string Code, string Name, string TimeZoneId, string Status,
    IReadOnlyList<string> ManagerNames, int EmployeeCount, int FacilityGroupCount, int FacilityCount,
    int EnabledFacilityCount, uint Version);

public sealed record OrganizationSettingsDto(BrandProfileDto Brand, IReadOnlyList<StoreProfileDto> Stores);
public sealed record NavigationLabelsDto(IReadOnlyDictionary<string, string> Labels, uint Version);

public sealed record UpdateBrandProfileCommand(string Code, string Name, uint ExpectedVersion, Guid OperatorId);
public sealed record CreateStoreCommand(string Name, string TimeZoneId, Guid OperatorId);
public sealed record UpdateStoreCommand(Guid StoreId, string Code, string Name, string TimeZoneId,
    uint ExpectedVersion, Guid OperatorId);
public sealed record ChangeStoreStatusCommand(Guid StoreId, bool Enable, string Reason, uint ExpectedVersion,
    Guid OperatorId);
public sealed record UpdateNavigationLabelsCommand(IReadOnlyDictionary<string, string> Labels,
    uint ExpectedVersion, Guid OperatorId);

public interface IOrganizationService
{
    Task<OrganizationSettingsDto?> GetSettingsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<BrandProfileDto>> UpdateBrandAsync(Guid tenantId, UpdateBrandProfileCommand command,
        CancellationToken cancellationToken);
    Task<Result<StoreProfileDto>> CreateStoreAsync(Guid tenantId, CreateStoreCommand command,
        CancellationToken cancellationToken);
    Task<Result<StoreProfileDto>> UpdateStoreAsync(Guid tenantId, UpdateStoreCommand command,
        CancellationToken cancellationToken);
    Task<Result<StoreProfileDto>> ChangeStoreStatusAsync(Guid tenantId, ChangeStoreStatusCommand command,
        CancellationToken cancellationToken);
    Task<NavigationLabelsDto?> GetNavigationLabelsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<NavigationLabelsDto>> UpdateNavigationLabelsAsync(Guid tenantId,
        UpdateNavigationLabelsCommand command, CancellationToken cancellationToken);
}
