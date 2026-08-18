using Erp.Application.Common;
using Erp.Domain.Facilities;

namespace Erp.Application.Facilities;

public sealed record FacilityGroupDto(Guid Id, string DisplayName, int SortOrder);
public sealed record FacilityTypeDto(Guid Id, string DisplayName);
public sealed record FacilityConfigurationStoreDto(Guid Id, string Code, string Name, string Status,
    IReadOnlyList<string> ManagerNames, int GroupCount, int FacilityCount, int EnabledFacilityCount);
public sealed record FacilityConfigurationDto(Guid StoreId, string StoreCode, string StoreName,
    IReadOnlyList<string> ManagerNames, IReadOnlyList<FacilityConfigurationGroupDto> Groups);
public sealed record FacilityConfigurationGroupDto(Guid Id, string DisplayName, int SortOrder, uint Version,
    IReadOnlyList<FacilityConfigurationItemDto> Facilities);
public sealed record FacilityConfigurationItemDto(Guid Id, Guid GroupId, Guid FacilityTypeId, string TypeName,
    string Code, string DisplayName, string? ServiceName, string? EquipmentName, long? ReferencePriceMinor,
    int SortOrder, int DefaultCleaningMinutes, bool AllowReservation, string LifecycleStatus, uint Version,
    bool HasOpenSession);

public sealed record FacilityBoardDto(DateTimeOffset ServerNowUtc, IReadOnlyList<FacilityBoardGroupDto> Groups);
public sealed record FacilityBoardGroupDto(Guid Id, string DisplayName, IReadOnlyList<FacilityBoardItemDto> Facilities);
public sealed record FacilityBoardItemDto(
    Guid Id,
    string Code,
    string DisplayName,
    string TypeName,
    string Status,
    uint Version,
    Guid? SessionId,
    Guid? VisitId,
    string? VisitNo,
    string? SessionStatus,
    DateTimeOffset? StartedAtUtc,
    long ActiveSeconds,
    long PausedSeconds,
    int? ExpectedDurationMinutes,
    string? Note,
    string? ServiceName,
    string? EquipmentName,
    long? ReferencePriceMinor);

public sealed record CreateFacilityGroupCommand(Guid StoreId, string DisplayName, int SortOrder, Guid OperatorId);
public sealed record UpdateFacilityGroupCommand(Guid StoreId, Guid GroupId, string DisplayName, int SortOrder,
    uint ExpectedVersion, Guid OperatorId);
public sealed record CreateFacilityTypeCommand(string DisplayName, Guid OperatorId);
public sealed record CreateFacilityCommand(Guid StoreId, Guid GroupId, Guid? FacilityTypeId, string? Code,
    string DisplayName, string? ServiceName, string? EquipmentName, long? ReferencePriceMinor,
    int SortOrder, int DefaultCleaningMinutes, bool AllowReservation, Guid OperatorId);
public sealed record UpdateFacilityCommand(Guid StoreId, Guid FacilityId, Guid GroupId, Guid? FacilityTypeId,
    string? Code, string DisplayName, string? ServiceName, string? EquipmentName, long? ReferencePriceMinor,
    int SortOrder, int DefaultCleaningMinutes, bool AllowReservation, FacilityLifecycleStatus LifecycleStatus,
    uint ExpectedVersion, Guid OperatorId);
public sealed record StartFacilitySessionCommand(Guid StoreId, Guid FacilityId, int? ExpectedDurationMinutes, string? Note,
    Guid CommandId, Guid OperatorId);
public sealed record OperateFacilitySessionCommand(Guid StoreId, Guid SessionId, Guid CommandId, Guid OperatorId);
public sealed record SwitchFacilityCommand(Guid StoreId, Guid SessionId, Guid TargetFacilityId, string? Reason,
    Guid CommandId, Guid OperatorId);

public interface IFacilityService
{
    Task<Result<FacilityBoardDto>> GetBoardAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FacilityConfigurationStoreDto>> ListConfigurationStoresAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<FacilityConfigurationDto>> GetConfigurationAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FacilityGroupDto>> ListGroupsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FacilityTypeDto>> ListTypesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<FacilityGroupDto>> CreateGroupAsync(Guid tenantId, CreateFacilityGroupCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityGroupDto>> UpdateGroupAsync(Guid tenantId, UpdateFacilityGroupCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityTypeDto>> CreateTypeAsync(Guid tenantId, CreateFacilityTypeCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> CreateFacilityAsync(Guid tenantId, CreateFacilityCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityConfigurationItemDto>> UpdateFacilityAsync(Guid tenantId, UpdateFacilityCommand command,
        CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> StartAsync(Guid tenantId, StartFacilitySessionCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> PauseAsync(Guid tenantId, OperateFacilitySessionCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> ResumeAsync(Guid tenantId, OperateFacilitySessionCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> EndAsync(Guid tenantId, OperateFacilitySessionCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> SwitchAsync(Guid tenantId, SwitchFacilityCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> CompleteCleaningAsync(Guid tenantId, Guid storeId, Guid facilityId, Guid commandId,
        Guid operatorId, CancellationToken cancellationToken);
}
