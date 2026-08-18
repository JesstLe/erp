using Erp.Application.Common;

namespace Erp.Application.Scheduling;

public sealed record AppointmentDto(Guid Id, string AppointmentNo, Guid StoreId, Guid CustomerId,
    string CustomerName, string MaskedMobile, Guid ServiceItemId, string ServiceItemName, Guid? EmployeeId,
    string? EmployeeName, Guid? FacilityId, string? FacilityName, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, string? Note, string Status, Guid? VisitId, DateTimeOffset? ArrivedAtUtc,
    string? CancellationReason, string? NoShowReason, uint Version);

public sealed record EmployeeShiftDto(Guid Id, Guid StoreId, Guid EmployeeId, string EmployeeNo,
    string EmployeeName, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, string? Note,
    string Status, string? CancellationReason, uint Version);

public sealed record SchedulingResourceDto(Guid Id, string Code, string Name);

public sealed record CreateAppointmentCommand(Guid StoreId, Guid CustomerId, Guid ServiceItemId,
    Guid? EmployeeId, Guid? FacilityId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
    string? Note, Guid CommandId, Guid OperatorId);
public sealed record UpdateAppointmentCommand(Guid StoreId, Guid AppointmentId, Guid ServiceItemId,
    Guid? EmployeeId, Guid? FacilityId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc,
    string? Note, uint ExpectedVersion, Guid OperatorId);
public sealed record TransitionAppointmentCommand(Guid StoreId, Guid AppointmentId, string? Reason,
    uint ExpectedVersion, Guid CommandId, Guid OperatorId);
public sealed record CreateEmployeeShiftCommand(Guid StoreId, Guid EmployeeId, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, string? Note, Guid CommandId, Guid OperatorId);
public sealed record UpdateEmployeeShiftCommand(Guid StoreId, Guid ShiftId, DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc, string? Note, uint ExpectedVersion, Guid OperatorId);
public sealed record CancelEmployeeShiftCommand(Guid StoreId, Guid ShiftId, string Reason,
    uint ExpectedVersion, Guid CommandId, Guid OperatorId);

public interface ISchedulingService
{
    Task<PageResult<AppointmentDto>> ListAppointmentsAsync(Guid tenantId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, string? status, string? query, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<PageResult<EmployeeShiftDto>> ListShiftsAsync(Guid tenantId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SchedulingResourceDto>> ListEmployeesAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SchedulingResourceDto>> ListFacilitiesAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> CreateAppointmentAsync(Guid tenantId, CreateAppointmentCommand command,
        CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> UpdateAppointmentAsync(Guid tenantId, UpdateAppointmentCommand command,
        CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> CancelAppointmentAsync(Guid tenantId, TransitionAppointmentCommand command,
        CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> MarkNoShowAsync(Guid tenantId, TransitionAppointmentCommand command,
        CancellationToken cancellationToken);
    Task<Result<AppointmentDto>> ArriveAsync(Guid tenantId, TransitionAppointmentCommand command,
        CancellationToken cancellationToken);
    Task<Result<EmployeeShiftDto>> CreateShiftAsync(Guid tenantId, CreateEmployeeShiftCommand command,
        CancellationToken cancellationToken);
    Task<Result<EmployeeShiftDto>> UpdateShiftAsync(Guid tenantId, UpdateEmployeeShiftCommand command,
        CancellationToken cancellationToken);
    Task<Result<EmployeeShiftDto>> CancelShiftAsync(Guid tenantId, CancelEmployeeShiftCommand command,
        CancellationToken cancellationToken);
}
