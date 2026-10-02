using Erp.Application.Common;

namespace Erp.Application.Identity;

public sealed record EmployeeStoreDto(Guid Id, string Code, string Name, bool IsPrimary);

public sealed record EmployeeDto(Guid Id, string EmployeeNo, string DisplayName, string PositionCode, string Status,
    Guid? UserId, string? Account, bool? AccountEnabled, bool? MustChangePassword, IReadOnlyList<string> Roles,
    IReadOnlyList<EmployeeStoreDto> Stores, DateTimeOffset CreatedAtUtc, uint Version);

public sealed record RoleDto(Guid Id, string Code, string Name);

public sealed record EmployeePositionDto(Guid Id, string Code, string Name, int SortOrder, string Status,
    uint Version, int? DefaultCommissionRateBasisPoints = null);

public sealed record PositionServiceCommissionDto(Guid ServiceItemId, string Code, string Name, string Status,
    int? RateBasisPoints, string FallbackMode, int? FallbackRateBasisPoints, long? FallbackFixedMinor);
public sealed record PositionCommissionsDto(EmployeePositionDto Position,
    IReadOnlyList<PositionServiceCommissionDto> Services);
public sealed record PositionServiceCommissionInput(Guid ServiceItemId, int RateBasisPoints);
public sealed record SetPositionCommissionsCommand(Guid PositionId, int? DefaultRateBasisPoints,
    IReadOnlyList<PositionServiceCommissionInput> Services, uint ExpectedVersion, Guid OperatorId);

public sealed record CreateEmployeePositionCommand(string Name, int SortOrder, Guid OperatorId);
public sealed record UpdateEmployeePositionCommand(Guid Id, string Name, int SortOrder, bool IsEnabled,
    uint ExpectedVersion, Guid OperatorId);
public sealed record DeleteEmployeePositionCommand(Guid Id, uint ExpectedVersion, Guid OperatorId);

public sealed record CreateEmployeeCommand(string DisplayName, string PositionCode,
    IReadOnlyList<Guid> StoreIds, bool CreateLoginAccount, string? Account, string? InitialPassword,
    IReadOnlyList<string> Roles, Guid OperatorId);

public sealed record SetEmployeeAccountStatusCommand(Guid EmployeeId, bool IsEnabled, Guid OperatorId);
public sealed record UpdateEmployeeCommand(Guid EmployeeId, string DisplayName, string PositionCode,
    IReadOnlyList<Guid> StoreIds, IReadOnlyList<string> Roles, uint ExpectedVersion, Guid OperatorId);
public sealed record ChangeEmploymentStatusCommand(Guid EmployeeId, bool Reactivate, string Reason,
    uint ExpectedVersion, Guid OperatorId);
public sealed record ResetEmployeePasswordCommand(Guid EmployeeId, string NewInitialPassword, string Reason,
    Guid OperatorId);

public interface IEmployeeService
{
    Task<PageResult<EmployeeDto>> ListAsync(Guid tenantId, string? query, int page, int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<EmployeePositionDto>> ListPositionsAsync(Guid tenantId,
        CancellationToken cancellationToken);
    Task<Result<EmployeePositionDto>> CreatePositionAsync(Guid tenantId, CreateEmployeePositionCommand command,
        CancellationToken cancellationToken);
    Task<Result<EmployeePositionDto>> UpdatePositionAsync(Guid tenantId, UpdateEmployeePositionCommand command,
        CancellationToken cancellationToken);
    Task<Result<bool>> DeletePositionAsync(Guid tenantId, DeleteEmployeePositionCommand command,
        CancellationToken cancellationToken);
    Task<Result<PositionCommissionsDto>> GetPositionCommissionsAsync(Guid tenantId, Guid positionId,
        CancellationToken cancellationToken);
    Task<Result<PositionCommissionsDto>> SetPositionCommissionsAsync(Guid tenantId,
        SetPositionCommissionsCommand command, CancellationToken cancellationToken);

    Task<Result<EmployeeDto>> CreateAsync(Guid tenantId, CreateEmployeeCommand command, CancellationToken cancellationToken);

    Task<Result<EmployeeDto>> SetAccountStatusAsync(Guid tenantId, SetEmployeeAccountStatusCommand command,
        CancellationToken cancellationToken);
    Task<Result<EmployeeDto>> UpdateAsync(Guid tenantId, UpdateEmployeeCommand command,
        CancellationToken cancellationToken);
    Task<Result<EmployeeDto>> ChangeEmploymentStatusAsync(Guid tenantId, ChangeEmploymentStatusCommand command,
        CancellationToken cancellationToken);
    Task<Result<EmployeeDto>> ResetPasswordAsync(Guid tenantId, ResetEmployeePasswordCommand command,
        CancellationToken cancellationToken);
}
