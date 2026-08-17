using Erp.Application.Common;

namespace Erp.Application.Facilities;

public sealed record FacilityGroupDto(Guid Id, string DisplayName, int SortOrder);
public sealed record FacilityTypeDto(Guid Id, string DisplayName);

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
    string? Note);

public sealed record CreateFacilityGroupCommand(Guid StoreId, string DisplayName, int SortOrder);
public sealed record CreateFacilityTypeCommand(string DisplayName);
public sealed record CreateFacilityCommand(Guid StoreId, Guid GroupId, Guid FacilityTypeId, string Code, string DisplayName,
    int SortOrder, int DefaultCleaningMinutes, bool AllowReservation);
public sealed record StartFacilitySessionCommand(Guid StoreId, Guid FacilityId, int? ExpectedDurationMinutes, string? Note,
    Guid CommandId, Guid OperatorId);
public sealed record OperateFacilitySessionCommand(Guid StoreId, Guid SessionId, Guid CommandId, Guid OperatorId);
public sealed record SwitchFacilityCommand(Guid StoreId, Guid SessionId, Guid TargetFacilityId, string? Reason,
    Guid CommandId, Guid OperatorId);

public interface IFacilityService
{
    Task<Result<FacilityBoardDto>> GetBoardAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FacilityGroupDto>> ListGroupsAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FacilityTypeDto>> ListTypesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Result<FacilityGroupDto>> CreateGroupAsync(Guid tenantId, CreateFacilityGroupCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityTypeDto>> CreateTypeAsync(Guid tenantId, CreateFacilityTypeCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> CreateFacilityAsync(Guid tenantId, CreateFacilityCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> StartAsync(Guid tenantId, StartFacilitySessionCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> PauseAsync(Guid tenantId, OperateFacilitySessionCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> ResumeAsync(Guid tenantId, OperateFacilitySessionCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> EndAsync(Guid tenantId, OperateFacilitySessionCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> SwitchAsync(Guid tenantId, SwitchFacilityCommand command, CancellationToken cancellationToken);
    Task<Result<FacilityBoardItemDto>> CompleteCleaningAsync(Guid tenantId, Guid storeId, Guid facilityId, Guid commandId,
        Guid operatorId, CancellationToken cancellationToken);
}
