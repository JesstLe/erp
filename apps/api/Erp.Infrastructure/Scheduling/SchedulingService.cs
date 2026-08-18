using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Common;
using Erp.Application.Scheduling;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Domain.Facilities;
using Erp.Domain.Organization;
using Erp.Domain.Scheduling;
using Erp.Infrastructure.Customers;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Scheduling;

internal sealed class SchedulingService(ErpDbContext db, TimeProvider clock, CustomerPrivacyService privacy,
    IHttpContextAccessor httpContextAccessor) : ISchedulingService
{
    private static readonly FacilitySessionStatus[] OpenFacilityStatuses =
        [FacilitySessionStatus.Active, FacilitySessionStatus.Paused];

    public async Task<PageResult<AppointmentDto>> ListAppointmentsAsync(Guid tenantId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, string? status, string? query, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var appointments = db.Appointments.AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.StoreId == storeId && x.StartsAtUtc < toUtc && x.EndsAtUtc > fromUtc);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AppointmentStatus>(status, true, out var parsed))
            appointments = appointments.Where(x => x.Status == parsed);
        var term = query?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var upper = term.ToUpperInvariant();
            byte[]? mobileHash = null;
            try
            {
                if (term.Count(char.IsDigit) == 11) mobileHash = privacy.Hash(term);
            }
            catch (ArgumentException)
            {
                // Invalid mobile-shaped input remains a normal name/number query.
            }
            appointments = appointments.Where(x => x.AppointmentNo.Contains(upper) ||
                db.Customers.Any(customer => customer.Id == x.CustomerId && customer.TenantId == tenantId &&
                    (customer.Name.Contains(term) || (mobileHash != null && customer.MobileLookupHash == mobileHash))) ||
                db.ServiceItems.Any(item => item.Id == x.ServiceItemId && item.TenantId == tenantId &&
                    (item.Name.Contains(term) || item.Code.Contains(upper))));
        }
        var total = await appointments.CountAsync(cancellationToken);
        var rows = await appointments.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<AppointmentDto>(await ProjectAppointmentsAsync(tenantId, rows, cancellationToken),
            total, page, pageSize);
    }

    public async Task<PageResult<EmployeeShiftDto>> ListShiftsAsync(Guid tenantId, Guid storeId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var shifts = db.EmployeeShifts.AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.StoreId == storeId && x.StartsAtUtc < toUtc && x.EndsAtUtc > fromUtc);
        var total = await shifts.CountAsync(cancellationToken);
        var rows = await shifts.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var employeeIds = rows.Select(x => x.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && employeeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var items = rows.Select(x =>
        {
            employees.TryGetValue(x.EmployeeId, out var employee);
            return new EmployeeShiftDto(x.Id, x.StoreId, x.EmployeeId, employee?.EmployeeNo ?? "-",
                employee?.DisplayName ?? "已停用员工", x.StartsAtUtc, x.EndsAtUtc, x.Note, x.Status.ToString(),
                x.CancellationReason, x.Version);
        }).ToList();
        return new PageResult<EmployeeShiftDto>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<SchedulingResourceDto>> ListEmployeesAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken) => await (from employee in db.Employees.AsNoTracking()
            join assignment in db.EmployeeStores.AsNoTracking() on employee.Id equals assignment.EmployeeId
            where employee.TenantId == tenantId && assignment.TenantId == tenantId &&
                assignment.StoreId == storeId && employee.Status == EmployeeStatus.Active
            orderby employee.EmployeeNo
            select new SchedulingResourceDto(employee.Id, employee.EmployeeNo, employee.DisplayName))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SchedulingResourceDto>> ListFacilitiesAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken) => await db.Facilities.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId && x.AllowReservation &&
                x.LifecycleStatus == FacilityLifecycleStatus.Enabled)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => new SchedulingResourceDto(x.Id, x.Code, x.DisplayName)).ToListAsync(cancellationToken);

    public async Task<Result<AppointmentDto>> CreateAppointmentAsync(Guid tenantId,
        CreateAppointmentCommand command, CancellationToken cancellationToken)
    {
        var hash = RequestHash($"appointment.create|{command.StoreId}|{command.CustomerId}|{command.ServiceItemId}|" +
            $"{command.EmployeeId}|{command.FacilityId}|{command.StartsAtUtc:O}|{command.EndsAtUtc:O}|{command.Note?.Trim()}");
        var replay = await ReplayAppointmentAsync(tenantId, command.CommandId, hash, cancellationToken);
        if (replay is not null) return replay;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var validation = await ValidateAppointmentResourcesAsync(tenantId, command.StoreId, command.CustomerId,
                command.ServiceItemId, command.EmployeeId, command.FacilityId, command.StartsAtUtc,
                command.EndsAtUtc, null, cancellationToken);
            if (validation is not null) return await RollbackFailure<AppointmentDto>(transaction, validation.Value.Code,
                validation.Value.Message, cancellationToken);
            if (command.StartsAtUtc < clock.GetUtcNow().AddMinutes(-5))
                return await RollbackFailure<AppointmentDto>(transaction, "APPOINTMENT_IN_PAST",
                    "不能新建已经开始的预约", cancellationToken);
            var now = clock.GetUtcNow();
            var appointment = new Appointment(tenantId, command.StoreId, CreateAppointmentNo(now),
                command.CustomerId, command.ServiceItemId, command.EmployeeId, command.FacilityId,
                command.StartsAtUtc, command.EndsAtUtc, command.Note, command.OperatorId, command.CommandId);
            db.Appointments.Add(appointment);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, appointment.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "appointment.create", "Appointment",
                appointment.Id, null, AppointmentStatus.Scheduled.ToString(), command.CommandId, now,
                metadata: JsonSerializer.Serialize(new { command.CustomerId, command.ServiceItemId,
                    command.EmployeeId, command.FacilityId, command.StartsAtUtc, command.EndsAtUtc }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadAppointmentAsync(tenantId, appointment.Id, cancellationToken));
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<AppointmentDto>("APPOINTMENT_RESOURCE_CONFLICT",
                "员工或设施在该时段已被其他预约占用，请刷新后重试");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return await ReplayAppointmentAsync(tenantId, command.CommandId, hash, cancellationToken) ??
                ResultFactory.Failure<AppointmentDto>("APPOINTMENT_CREATE_CONFLICT", "预约创建冲突，请刷新后重试");
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<AppointmentDto>(exception.Code, exception.Message);
        }
    }

    public async Task<Result<AppointmentDto>> UpdateAppointmentAsync(Guid tenantId,
        UpdateAppointmentCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.Id == command.AppointmentId &&
                x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
            if (appointment is null) return await RollbackFailure<AppointmentDto>(transaction,
                "APPOINTMENT_NOT_FOUND", "预约不存在", cancellationToken);
            if (appointment.Version != command.ExpectedVersion) return await RollbackFailure<AppointmentDto>(transaction,
                "VERSION_CONFLICT", "预约已被其他人修改，请刷新后重试", cancellationToken);
            var validation = await ValidateAppointmentResourcesAsync(tenantId, command.StoreId,
                appointment.CustomerId, command.ServiceItemId, command.EmployeeId, command.FacilityId,
                command.StartsAtUtc, command.EndsAtUtc, appointment.Id, cancellationToken);
            if (validation is not null) return await RollbackFailure<AppointmentDto>(transaction,
                validation.Value.Code, validation.Value.Message, cancellationToken);
            if (command.StartsAtUtc < clock.GetUtcNow().AddMinutes(-5))
                return await RollbackFailure<AppointmentDto>(transaction, "APPOINTMENT_IN_PAST",
                    "不能把预约调整到已经开始的时间", cancellationToken);
            var previous = AppointmentState(appointment);
            appointment.Update(command.ServiceItemId, command.EmployeeId, command.FacilityId,
                command.StartsAtUtc, command.EndsAtUtc, command.Note);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "appointment.update", "Appointment",
                appointment.Id, AppointmentStatus.Scheduled.ToString(), AppointmentStatus.Scheduled.ToString(),
                Guid.CreateVersion7(), clock.GetUtcNow(), metadata: JsonSerializer.Serialize(new { previous,
                    current = AppointmentState(appointment) }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadAppointmentAsync(tenantId, appointment.Id, cancellationToken));
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<AppointmentDto>("APPOINTMENT_RESOURCE_CONFLICT",
                "员工或设施在该时段已被其他预约占用，请刷新后重试");
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<AppointmentDto>(exception.Code, exception.Message);
        }
    }

    public Task<Result<AppointmentDto>> CancelAppointmentAsync(Guid tenantId,
        TransitionAppointmentCommand command, CancellationToken cancellationToken) =>
        TransitionAppointmentAsync(tenantId, command, "appointment.cancel", (appointment, now) =>
            appointment.Cancel(now, command.OperatorId, command.Reason ?? string.Empty), cancellationToken);

    public Task<Result<AppointmentDto>> MarkNoShowAsync(Guid tenantId,
        TransitionAppointmentCommand command, CancellationToken cancellationToken) =>
        TransitionAppointmentAsync(tenantId, command, "appointment.no-show", (appointment, now) =>
            appointment.MarkNoShow(now, command.OperatorId, command.Reason), cancellationToken);

    public async Task<Result<AppointmentDto>> ArriveAsync(Guid tenantId, TransitionAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var hash = RequestHash($"appointment.arrive|{command.StoreId}|{command.AppointmentId}|{command.ExpectedVersion}");
        var replay = await ReplayAppointmentAsync(tenantId, command.CommandId, hash, cancellationToken);
        if (replay is not null) return replay;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.Id == command.AppointmentId &&
                x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
            if (appointment is null) return await RollbackFailure<AppointmentDto>(transaction,
                "APPOINTMENT_NOT_FOUND", "预约不存在", cancellationToken);
            if (appointment.Version != command.ExpectedVersion) return await RollbackFailure<AppointmentDto>(transaction,
                "VERSION_CONFLICT", "预约已被其他人处理，请刷新后重试", cancellationToken);
            if (appointment.FacilityId.HasValue && (await db.FacilitySessions.AnyAsync(x =>
                    x.FacilityId == appointment.FacilityId.Value && OpenFacilityStatuses.Contains(x.Status), cancellationToken) ||
                await db.FacilityCleaningTasks.AnyAsync(x => x.FacilityId == appointment.FacilityId.Value &&
                    x.Status == CleaningTaskStatus.Pending, cancellationToken)))
                return await RollbackFailure<AppointmentDto>(transaction, "FACILITY_NOT_AVAILABLE",
                    "预约设施当前正在使用或清洁中，可先修改预约设施或稍后办理到店", cancellationToken);
            var now = clock.GetUtcNow();
            var expectedMinutes = Math.Clamp((int)Math.Ceiling((appointment.EndsAtUtc - appointment.StartsAtUtc).TotalMinutes), 1, 1440);
            var visit = new Visit(tenantId, command.StoreId, CreateVisitNo(now), expectedMinutes,
                $"预约到店 · {appointment.AppointmentNo}", now, appointment.ServiceItemId);
            visit.LinkCustomer(appointment.CustomerId);
            db.Visits.Add(visit);
            await db.SaveChangesAsync(cancellationToken);
            if (appointment.FacilityId.HasValue)
                db.FacilitySessions.Add(new FacilitySession(tenantId, command.StoreId,
                    appointment.FacilityId.Value, visit.Id, now, command.OperatorId, command.CommandId));
            appointment.MarkArrived(now, command.OperatorId, visit.Id);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, appointment.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "appointment.arrive", "Appointment",
                appointment.Id, AppointmentStatus.Scheduled.ToString(), AppointmentStatus.Arrived.ToString(),
                command.CommandId, now, metadata: JsonSerializer.Serialize(new { VisitId = visit.Id,
                    appointment.FacilityId, appointment.EmployeeId }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadAppointmentAsync(tenantId, appointment.Id, cancellationToken));
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception) || IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            var replayed = await ReplayAppointmentAsync(tenantId, command.CommandId, hash, cancellationToken);
            return replayed ?? ResultFactory.Failure<AppointmentDto>("FACILITY_NOT_AVAILABLE",
                "预约资源状态已变化，请刷新后重试");
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<AppointmentDto>(exception.Code, exception.Message);
        }
    }

    public async Task<Result<EmployeeShiftDto>> CreateShiftAsync(Guid tenantId,
        CreateEmployeeShiftCommand command, CancellationToken cancellationToken)
    {
        var existing = await db.EmployeeShifts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CreateCommandId == command.CommandId && x.TenantId == tenantId, cancellationToken);
        if (existing is not null) return ResultFactory.Success(await LoadShiftAsync(tenantId, existing.Id, cancellationToken));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var error = await ValidateShiftAsync(tenantId, command.StoreId, command.EmployeeId,
                command.StartsAtUtc, command.EndsAtUtc, null, cancellationToken);
            if (error is not null) return await RollbackFailure<EmployeeShiftDto>(transaction,
                error.Value.Code, error.Value.Message, cancellationToken);
            var shift = new EmployeeShift(tenantId, command.StoreId, command.EmployeeId, command.StartsAtUtc,
                command.EndsAtUtc, command.Note, command.OperatorId, command.CommandId);
            db.EmployeeShifts.Add(shift);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "employee-shift.create", "EmployeeShift",
                shift.Id, null, EmployeeShiftStatus.Scheduled.ToString(), command.CommandId, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadShiftAsync(tenantId, shift.Id, cancellationToken));
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<EmployeeShiftDto>("EMPLOYEE_SHIFT_CONFLICT", "该员工已有重叠班次");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            var replayed = await db.EmployeeShifts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.CreateCommandId == command.CommandId && x.TenantId == tenantId, cancellationToken);
            return replayed is null ? ResultFactory.Failure<EmployeeShiftDto>("EMPLOYEE_SHIFT_CONFLICT",
                "班次创建冲突，请刷新后重试") :
                ResultFactory.Success(await LoadShiftAsync(tenantId, replayed.Id, cancellationToken));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<EmployeeShiftDto>(exception.Code, exception.Message);
        }
    }

    public async Task<Result<EmployeeShiftDto>> UpdateShiftAsync(Guid tenantId,
        UpdateEmployeeShiftCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var shift = await db.EmployeeShifts.SingleOrDefaultAsync(x => x.Id == command.ShiftId &&
                x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
            if (shift is null) return await RollbackFailure<EmployeeShiftDto>(transaction,
                "EMPLOYEE_SHIFT_NOT_FOUND", "员工班次不存在", cancellationToken);
            if (shift.Version != command.ExpectedVersion) return await RollbackFailure<EmployeeShiftDto>(transaction,
                "VERSION_CONFLICT", "班次已被其他人修改，请刷新后重试", cancellationToken);
            var error = await ValidateShiftAsync(tenantId, command.StoreId, shift.EmployeeId,
                command.StartsAtUtc, command.EndsAtUtc, shift.Id, cancellationToken);
            if (error is not null) return await RollbackFailure<EmployeeShiftDto>(transaction,
                error.Value.Code, error.Value.Message, cancellationToken);
            shift.Update(command.StartsAtUtc, command.EndsAtUtc, command.Note);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "employee-shift.update", "EmployeeShift",
                shift.Id, EmployeeShiftStatus.Scheduled.ToString(), EmployeeShiftStatus.Scheduled.ToString(),
                Guid.CreateVersion7(), clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadShiftAsync(tenantId, shift.Id, cancellationToken));
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<EmployeeShiftDto>("EMPLOYEE_SHIFT_CONFLICT", "该员工已有重叠班次");
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<EmployeeShiftDto>(exception.Code, exception.Message);
        }
    }

    public async Task<Result<EmployeeShiftDto>> CancelShiftAsync(Guid tenantId,
        CancelEmployeeShiftCommand command, CancellationToken cancellationToken)
    {
        var hash = RequestHash($"employee-shift.cancel|{command.StoreId}|{command.ShiftId}|" +
            $"{command.ExpectedVersion}|{command.Reason.Trim()}");
        var replay = await ReplayShiftAsync(tenantId, command.CommandId, hash, cancellationToken);
        if (replay is not null) return replay;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var shift = await db.EmployeeShifts.SingleOrDefaultAsync(x => x.Id == command.ShiftId &&
                x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
            if (shift is null) return await RollbackFailure<EmployeeShiftDto>(transaction,
                "EMPLOYEE_SHIFT_NOT_FOUND", "员工班次不存在", cancellationToken);
            if (shift.Version != command.ExpectedVersion) return await RollbackFailure<EmployeeShiftDto>(transaction,
                "VERSION_CONFLICT", "班次已被其他人修改，请刷新后重试", cancellationToken);
            var now = clock.GetUtcNow();
            shift.Cancel(now, command.OperatorId, command.Reason);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, shift.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "employee-shift.cancel", "EmployeeShift",
                shift.Id, EmployeeShiftStatus.Scheduled.ToString(), EmployeeShiftStatus.Cancelled.ToString(),
                command.CommandId, now, command.Reason);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadShiftAsync(tenantId, shift.Id, cancellationToken));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return await ReplayShiftAsync(tenantId, command.CommandId, hash, cancellationToken) ??
                ResultFactory.Failure<EmployeeShiftDto>("EMPLOYEE_SHIFT_CONFLICT", "班次状态已变化，请刷新后重试");
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<EmployeeShiftDto>("EMPLOYEE_SHIFT_CONFLICT", "班次状态已变化，请刷新后重试");
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<EmployeeShiftDto>(exception.Code, exception.Message);
        }
    }

    private async Task<Result<AppointmentDto>> TransitionAppointmentAsync(Guid tenantId,
        TransitionAppointmentCommand command, string action, Action<Appointment, DateTimeOffset> transition,
        CancellationToken cancellationToken)
    {
        var hash = RequestHash($"{action}|{command.StoreId}|{command.AppointmentId}|{command.ExpectedVersion}|" +
            $"{command.Reason?.Trim()}");
        var replay = await ReplayAppointmentAsync(tenantId, command.CommandId, hash, cancellationToken);
        if (replay is not null) return replay;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.Id == command.AppointmentId &&
                x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
            if (appointment is null) return await RollbackFailure<AppointmentDto>(transaction,
                "APPOINTMENT_NOT_FOUND", "预约不存在", cancellationToken);
            if (appointment.Version != command.ExpectedVersion) return await RollbackFailure<AppointmentDto>(transaction,
                "VERSION_CONFLICT", "预约已被其他人处理，请刷新后重试", cancellationToken);
            var previous = appointment.Status.ToString();
            var now = clock.GetUtcNow();
            transition(appointment, now);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, appointment.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, action, "Appointment", appointment.Id,
                previous, appointment.Status.ToString(), command.CommandId, now, command.Reason);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadAppointmentAsync(tenantId, appointment.Id, cancellationToken));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return await ReplayAppointmentAsync(tenantId, command.CommandId, hash, cancellationToken) ??
                ResultFactory.Failure<AppointmentDto>("APPOINTMENT_RESOURCE_CONFLICT", "预约状态已变化，请刷新后重试");
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<AppointmentDto>("APPOINTMENT_RESOURCE_CONFLICT", "预约状态已变化，请刷新后重试");
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<AppointmentDto>(exception.Code, exception.Message);
        }
    }

    private async Task<(string Code, string Message)?> ValidateAppointmentResourcesAsync(Guid tenantId,
        Guid storeId, Guid customerId, Guid serviceItemId, Guid? employeeId, Guid? facilityId,
        DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, Guid? excludedAppointmentId,
        CancellationToken cancellationToken)
    {
        if (endsAtUtc <= startsAtUtc) return ("VALIDATION_FAILED", "预约结束时间必须晚于开始时间");
        if (!await db.Customers.AnyAsync(x => x.Id == customerId && x.TenantId == tenantId &&
                x.Status == CustomerStatus.Active, cancellationToken))
            return ("CUSTOMER_NOT_FOUND", "顾客不存在或已停用");
        if (!await db.ServiceItems.AnyAsync(x => x.Id == serviceItemId && x.TenantId == tenantId &&
                x.Status == CatalogItemStatus.Enabled, cancellationToken))
            return ("SERVICE_ITEM_NOT_FOUND", "服务项目不存在或已停用");
        if (employeeId.HasValue && !await (from employee in db.Employees
                join assignment in db.EmployeeStores on employee.Id equals assignment.EmployeeId
                where employee.Id == employeeId.Value && employee.TenantId == tenantId &&
                    employee.Status == EmployeeStatus.Active && assignment.StoreId == storeId &&
                    assignment.TenantId == tenantId select employee.Id).AnyAsync(cancellationToken))
            return ("EMPLOYEE_NOT_ELIGIBLE", "预约员工不存在、已离职或不属于当前门店");
        if (facilityId.HasValue && !await db.Facilities.AnyAsync(x => x.Id == facilityId.Value &&
                x.TenantId == tenantId && x.StoreId == storeId && x.AllowReservation &&
                x.LifecycleStatus == FacilityLifecycleStatus.Enabled, cancellationToken))
            return ("FACILITY_RESERVATION_NOT_ALLOWED", "设施不存在、已停用或未启用预约");
        var conflicts = db.Appointments.Where(x => x.TenantId == tenantId && x.StoreId == storeId &&
            x.Status == AppointmentStatus.Scheduled && x.StartsAtUtc < endsAtUtc && x.EndsAtUtc > startsAtUtc);
        if (excludedAppointmentId.HasValue) conflicts = conflicts.Where(x => x.Id != excludedAppointmentId.Value);
        if (employeeId.HasValue && await conflicts.AnyAsync(x => x.EmployeeId == employeeId, cancellationToken))
            return ("APPOINTMENT_EMPLOYEE_CONFLICT", "该员工在所选时段已有预约");
        if (facilityId.HasValue && await conflicts.AnyAsync(x => x.FacilityId == facilityId, cancellationToken))
            return ("APPOINTMENT_FACILITY_CONFLICT", "该设施在所选时段已有预约");
        return null;
    }

    private async Task<(string Code, string Message)?> ValidateShiftAsync(Guid tenantId, Guid storeId,
        Guid employeeId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc, Guid? excludedShiftId,
        CancellationToken cancellationToken)
    {
        if (endsAtUtc <= startsAtUtc) return ("VALIDATION_FAILED", "班次结束时间必须晚于开始时间");
        if (!await (from employee in db.Employees join assignment in db.EmployeeStores on employee.Id equals assignment.EmployeeId
                where employee.Id == employeeId && employee.TenantId == tenantId && employee.Status == EmployeeStatus.Active &&
                    assignment.TenantId == tenantId && assignment.StoreId == storeId select employee.Id)
            .AnyAsync(cancellationToken))
            return ("EMPLOYEE_NOT_ELIGIBLE", "员工不存在、已离职或不属于当前门店");
        var conflicts = db.EmployeeShifts.Where(x => x.TenantId == tenantId && x.StoreId == storeId &&
            x.EmployeeId == employeeId && x.Status == EmployeeShiftStatus.Scheduled &&
            x.StartsAtUtc < endsAtUtc && x.EndsAtUtc > startsAtUtc);
        if (excludedShiftId.HasValue) conflicts = conflicts.Where(x => x.Id != excludedShiftId.Value);
        return await conflicts.AnyAsync(cancellationToken) ?
            ("EMPLOYEE_SHIFT_CONFLICT", "该员工已有重叠班次") : null;
    }

    private async Task<IReadOnlyList<AppointmentDto>> ProjectAppointmentsAsync(Guid tenantId,
        IReadOnlyList<Appointment> appointments, CancellationToken cancellationToken)
    {
        var customerIds = appointments.Select(x => x.CustomerId).Distinct().ToList();
        var serviceIds = appointments.Select(x => x.ServiceItemId).Distinct().ToList();
        var employeeIds = appointments.Where(x => x.EmployeeId.HasValue).Select(x => x.EmployeeId!.Value).Distinct().ToList();
        var facilityIds = appointments.Where(x => x.FacilityId.HasValue).Select(x => x.FacilityId!.Value).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(x => x.TenantId == tenantId && customerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var services = await db.ServiceItems.AsNoTracking().Where(x => x.TenantId == tenantId && serviceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && employeeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var facilities = await db.Facilities.AsNoTracking().Where(x => x.TenantId == tenantId && facilityIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return appointments.Select(x =>
        {
            customers.TryGetValue(x.CustomerId, out var customer);
            services.TryGetValue(x.ServiceItemId, out var service);
            Employee? employee = null;
            if (x.EmployeeId.HasValue) employees.TryGetValue(x.EmployeeId.Value, out employee);
            Facility? facility = null;
            if (x.FacilityId.HasValue) facilities.TryGetValue(x.FacilityId.Value, out facility);
            return new AppointmentDto(x.Id, x.AppointmentNo, x.StoreId, x.CustomerId,
                customer?.Name ?? "已停用顾客", customer is null ? "-" : privacy.MaskProtectedMobile(customer.MobileCiphertext),
                x.ServiceItemId, service?.Name ?? "已停用项目", x.EmployeeId, employee?.DisplayName,
                x.FacilityId, facility?.DisplayName, x.StartsAtUtc, x.EndsAtUtc, x.Note, x.Status.ToString(),
                x.VisitId, x.ArrivedAtUtc, x.CancellationReason, x.NoShowReason, x.Version);
        }).ToList();
    }

    private async Task<AppointmentDto> LoadAppointmentAsync(Guid tenantId, Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == appointmentId &&
            x.TenantId == tenantId, cancellationToken);
        return (await ProjectAppointmentsAsync(tenantId, [appointment], cancellationToken))[0];
    }

    private async Task<EmployeeShiftDto> LoadShiftAsync(Guid tenantId, Guid shiftId,
        CancellationToken cancellationToken)
    {
        var shift = await db.EmployeeShifts.AsNoTracking().SingleAsync(x => x.Id == shiftId && x.TenantId == tenantId,
            cancellationToken);
        var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == shift.EmployeeId &&
            x.TenantId == tenantId, cancellationToken);
        return new EmployeeShiftDto(shift.Id, shift.StoreId, shift.EmployeeId, employee.EmployeeNo,
            employee.DisplayName, shift.StartsAtUtc, shift.EndsAtUtc, shift.Note, shift.Status.ToString(),
            shift.CancellationReason, shift.Version);
    }

    private async Task<Result<AppointmentDto>?> ReplayAppointmentAsync(Guid tenantId, Guid commandId,
        byte[] requestHash, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
            return ResultFactory.Failure<AppointmentDto>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null ? null : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        return receipt is null ? ResultFactory.Failure<AppointmentDto>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新") :
            ResultFactory.Success(await LoadAppointmentAsync(tenantId, receipt.EntityId, cancellationToken));
    }

    private async Task<Result<EmployeeShiftDto>?> ReplayShiftAsync(Guid tenantId, Guid commandId,
        byte[] requestHash, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
            return ResultFactory.Failure<EmployeeShiftDto>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null ? null : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        return receipt is null ? ResultFactory.Failure<EmployeeShiftDto>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新") :
            ResultFactory.Success(await LoadShiftAsync(tenantId, receipt.EntityId, cancellationToken));
    }

    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] requestHash,
        Guid entityId, DateTimeOffset now) => db.IdempotencyCommands.Add(new IdempotencyCommandRecord
        {
            CommandId = commandId, TenantId = tenantId, OperatorId = operatorId, RequestHash = requestHash,
            ResponseStatus = 200, ResponseBody = JsonSerializer.Serialize(new CommandReceipt(entityId)),
            CreatedAtUtc = now, CompletedAtUtc = now,
        });

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, string entityType,
        Guid entityId, string? previous, string? current, Guid requestId, DateTimeOffset now,
        string? reason = null, string? metadata = null) => db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action,
            EntityType = entityType, EntityId = entityId, PreviousState = previous, CurrentState = current,
            RequestId = requestId, Reason = reason, Metadata = metadata ?? "{}",
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = now,
        });

    private static object AppointmentState(Appointment appointment) => new
    {
        appointment.ServiceItemId, appointment.EmployeeId, appointment.FacilityId,
        appointment.StartsAtUtc, appointment.EndsAtUtc, appointment.Note,
    };

    private static async Task<Result<T>> RollbackFailure<T>(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string code, string message, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return ResultFactory.Failure<T>(code, message);
    }

    private static async Task RollbackIfActiveAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        catch (InvalidOperationException) { }
    }

    private static byte[] RequestHash(string identity) => SHA256.HashData(Encoding.UTF8.GetBytes(identity));
    private static bool IsUniqueViolation(Exception exception) => FindPostgres(exception)?.SqlState == PostgresErrorCodes.UniqueViolation;
    private static bool IsDatabaseConcurrencyConflict(Exception exception) => FindPostgres(exception)?.SqlState is
        PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;
    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
    private static string CreateAppointmentNo(DateTimeOffset now) =>
        $"A{now:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..30].ToUpperInvariant();
    private static string CreateVisitNo(DateTimeOffset now) =>
        $"V{now:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..30].ToUpperInvariant();
    private sealed record CommandReceipt(Guid EntityId);
}
