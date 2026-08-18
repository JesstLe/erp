using Erp.Application.Common;

namespace Erp.Application.Identity;

public sealed record EmployeeStoreDto(Guid Id, string Code, string Name, bool IsPrimary);

public sealed record EmployeeDto(Guid Id, string EmployeeNo, string DisplayName, string PositionCode, string Status,
    Guid? UserId, string? Account, bool? AccountEnabled, bool? MustChangePassword, IReadOnlyList<string> Roles,
    IReadOnlyList<EmployeeStoreDto> Stores, DateTimeOffset CreatedAtUtc);

public sealed record RoleDto(Guid Id, string Code, string Name);

public sealed record CreateEmployeeCommand(string EmployeeNo, string DisplayName, string PositionCode,
    IReadOnlyList<Guid> StoreIds, bool CreateLoginAccount, string? Account, string? InitialPassword,
    IReadOnlyList<string> Roles, Guid OperatorId);

public sealed record SetEmployeeAccountStatusCommand(Guid EmployeeId, bool IsEnabled, Guid OperatorId);

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeDto>> ListAsync(Guid tenantId, string? query, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Result<EmployeeDto>> CreateAsync(Guid tenantId, CreateEmployeeCommand command, CancellationToken cancellationToken);

    Task<Result<EmployeeDto>> SetAccountStatusAsync(Guid tenantId, SetEmployeeAccountStatusCommand command,
        CancellationToken cancellationToken);
}
