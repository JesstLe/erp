using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Erp.Infrastructure.Cashier;

internal sealed class PaymentService(ErpDbContext db, TimeProvider clock, IHttpContextAccessor httpContextAccessor) : IPaymentService
{
    public async Task<IReadOnlyList<PaymentMethodDto>> ListMethodsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.PaymentMethods.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsEnabled)
            .OrderBy(x => x.Code).Select(x => new PaymentMethodDto(x.Id, x.Code, x.Name, x.Category.ToString(),
                x.RequiresOpenShift)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentDto>> ListPaymentsAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken)
    {
        var payments = await db.Payments.AsNoTracking().Include(x => x.Allocations)
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
        return payments.Select(ToDto).ToList();
    }

    public async Task<CashierShiftDto?> GetCurrentShiftAsync(Guid tenantId, Guid storeId, Guid operatorId,
        CancellationToken cancellationToken)
    {
        var shift = await db.CashierShifts.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId &&
                x.OperatorId == operatorId && x.Status != CashierShiftStatus.Closed)
            .OrderByDescending(x => x.OpenedAtUtc).FirstOrDefaultAsync(cancellationToken);
        return shift is null ? null : ToDto(shift);
    }

    public async Task<IReadOnlyList<CashierShiftReviewDto>> ListShiftsAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken)
    {
        var shifts = await db.CashierShifts.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .OrderByDescending(x => x.OpenedAtUtc).Take(100).ToListAsync(cancellationToken);
        var operatorIds = shifts.Select(x => x.OperatorId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(x => x.TenantId == tenantId && operatorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        return shifts.Select(x => new CashierShiftReviewDto(ToDto(x), names.GetValueOrDefault(x.OperatorId, "未知员工"))).ToList();
    }

    public async Task<Result<CashierShiftDto>> OpenShiftAsync(Guid tenantId, OpenShiftCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<CashierShiftDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var hash = RequestHash($"SHIFT_OPEN|{command.StoreId}|{command.OpeningCashMinor}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, hash,
            async id => await GetShiftAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;
        try
        {
            if (await db.CashierShifts.AnyAsync(x => x.TenantId == tenantId && x.StoreId == command.StoreId &&
                    x.OperatorId == command.OperatorId && x.Status == CashierShiftStatus.Open, cancellationToken))
                return await FailureAndRollback<CashierShiftDto>(transaction, "SHIFT_ALREADY_OPEN", "当前账号已经有进行中的班次", cancellationToken);
            var now = clock.GetUtcNow();
            var localTime = await StoreLocalTimeAsync(tenantId, command.StoreId, now, cancellationToken);
            if (localTime is null) return await FailureAndRollback<CashierShiftDto>(transaction, "VALIDATION_FAILED", "门店时区配置无效", cancellationToken);
            var shift = new CashierShift(tenantId, command.StoreId, command.OperatorId, CreateShiftNo(localTime.Value),
                command.OpeningCashMinor, now);
            db.CashierShifts.Add(shift);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, shift.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "cashier_shift.open", "CashierShift", shift.Id,
                null, shift.Status.ToString(), command.CommandId, null, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(shift));
        }
        catch (DomainRuleException exception) { return await DomainFailure<CashierShiftDto>(transaction, exception, cancellationToken); }
        catch (Exception exception) when (IsUniqueOrConcurrency(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CashierShiftDto>("SHIFT_ALREADY_OPEN", "当前账号已经有进行中的班次");
        }
    }

    public async Task<Result<CashierShiftDto>> SubmitShiftAsync(Guid tenantId, SubmitShiftCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<CashierShiftDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var hash = RequestHash($"SHIFT_SUBMIT|{command.StoreId}|{command.ShiftId}|{command.ExpectedVersion}|{command.SubmittedCashMinor}|{command.Note}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, hash,
            async id => await GetShiftAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var shift = await db.CashierShifts.SingleOrDefaultAsync(x => x.Id == command.ShiftId && x.TenantId == tenantId &&
                x.StoreId == command.StoreId && x.OperatorId == command.OperatorId, cancellationToken);
            if (shift is null) return await FailureAndRollback<CashierShiftDto>(transaction, "SHIFT_NOT_FOUND", "班次不存在", cancellationToken);
            if (shift.Version != command.ExpectedVersion) return await FailureAndRollback<CashierShiftDto>(transaction, "VERSION_CONFLICT", "班次已变化，请刷新后重试", cancellationToken);
            var cashReceipts = await db.PaymentAllocations.Where(x => x.ShiftId == shift.Id &&
                x.Category == PaymentMethodCategory.Cash && x.ConfirmationStatus == PaymentConfirmationStatus.CashRecorded)
                .SumAsync(x => (long?)x.AmountMinor, cancellationToken) ?? 0;
            var pendingExternal = await db.PaymentAllocations.Where(x => x.ShiftId == shift.Id &&
                x.Category == PaymentMethodCategory.ManualExternal &&
                x.ConfirmationStatus == PaymentConfirmationStatus.ManualPendingReconciliation)
                .SumAsync(x => (long?)x.AmountMinor, cancellationToken) ?? 0;
            var now = clock.GetUtcNow();
            var previous = shift.Status.ToString();
            shift.Submit(cashReceipts, pendingExternal, command.SubmittedCashMinor, command.Note, now);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, shift.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "cashier_shift.submit", "CashierShift", shift.Id,
                previous, shift.Status.ToString(), command.CommandId, command.Note, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(shift));
        }
        catch (DomainRuleException exception) { return await DomainFailure<CashierShiftDto>(transaction, exception, cancellationToken); }
        catch (Exception exception) when (IsUniqueOrConcurrency(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CashierShiftDto>("VERSION_CONFLICT", "班次状态已变化，请刷新后重试");
        }
    }

    public async Task<Result<CashierShiftDto>> ReviewShiftAsync(Guid tenantId, ReviewShiftCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<CashierShiftDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var hash = RequestHash($"SHIFT_REVIEW|{command.StoreId}|{command.ShiftId}|{command.ExpectedVersion}|{command.Reason}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, hash,
            async id => await GetShiftAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var shift = await db.CashierShifts.SingleOrDefaultAsync(x => x.Id == command.ShiftId && x.TenantId == tenantId &&
                x.StoreId == command.StoreId, cancellationToken);
            if (shift is null) return await FailureAndRollback<CashierShiftDto>(transaction, "SHIFT_NOT_FOUND", "班次不存在", cancellationToken);
            if (shift.Version != command.ExpectedVersion) return await FailureAndRollback<CashierShiftDto>(transaction, "VERSION_CONFLICT", "班次已变化，请刷新后重试", cancellationToken);
            var requiresOwner = Math.Abs(shift.CashDifferenceMinor ?? 0) > 1_000 || shift.PendingReconciliationMinor > 0;
            if (requiresOwner && !command.IsOwner)
                return await FailureAndRollback<CashierShiftDto>(transaction, "FORBIDDEN_ACTION", "较大现金差额或外部待核对金额必须由最高权限复核", cancellationToken);
            var now = clock.GetUtcNow();
            var previous = shift.Status.ToString();
            shift.Review(command.ReviewerId, command.Reason, now);
            AddReceipt(tenantId, command.CommandId, command.ReviewerId, hash, shift.Id, now);
            AddAudit(tenantId, command.StoreId, command.ReviewerId, "cashier_shift.review", "CashierShift", shift.Id,
                previous, shift.Status.ToString(), command.CommandId, command.Reason, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(shift));
        }
        catch (DomainRuleException exception) { return await DomainFailure<CashierShiftDto>(transaction, exception, cancellationToken); }
        catch (Exception exception) when (IsUniqueOrConcurrency(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CashierShiftDto>("VERSION_CONFLICT", "班次状态已变化，请刷新后重试");
        }
    }

    public async Task<Result<PaymentDto>> SettleOrderAsync(Guid tenantId, SettleOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<PaymentDto>("VALIDATION_FAILED", "缺少幂等请求号");
        if (command.Allocations.Count is 0 or > 20) return ResultFactory.Failure<PaymentDto>("PAYMENT_ALLOCATION_UNBALANCED", "支付分摊需要1到20行");
        var hash = RequestHash(JsonSerializer.Serialize(command with { OperatorId = Guid.Empty }));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, hash,
            async id => await GetPaymentAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var order = await db.ServiceOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == command.OrderId &&
                x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
            if (order is null) return await FailureAndRollback<PaymentDto>(transaction, "SERVICE_ORDER_NOT_FOUND", "消费单不存在", cancellationToken);
            if (order.Version != command.ExpectedVersion) return await FailureAndRollback<PaymentDto>(transaction, "VERSION_CONFLICT", "消费单已变化，请刷新后重试", cancellationToken);
            if (order.Status != ServiceOrderStatus.PendingPayment)
                return await FailureAndRollback<PaymentDto>(transaction, "STATE_TRANSITION_NOT_ALLOWED", "只有待支付消费单可以结算", cancellationToken);
            if (await db.Payments.AnyAsync(x => x.OrderId == order.Id && x.Status != PaymentStatus.Cancelled, cancellationToken))
                return await FailureAndRollback<PaymentDto>(transaction, "PAYMENT_ALREADY_EXISTS", "该消费单已经存在支付结果", cancellationToken);
            var methodIds = command.Allocations.Select(x => x.MethodId).Distinct().ToList();
            if (methodIds.Count != command.Allocations.Count)
                return await FailureAndRollback<PaymentDto>(transaction, "VALIDATION_FAILED", "同一支付方式不能重复分摊", cancellationToken);
            var methods = await db.PaymentMethods.Where(x => x.TenantId == tenantId && x.IsEnabled && methodIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            if (methods.Count != methodIds.Count)
                return await FailureAndRollback<PaymentDto>(transaction, "PAYMENT_METHOD_NOT_FOUND", "支付方式不存在或已停用", cancellationToken);
            CashierShift? shift = null;
            if (methods.Values.Any(x => x.RequiresOpenShift))
            {
                shift = await db.CashierShifts.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.StoreId == command.StoreId &&
                    x.OperatorId == command.OperatorId && x.Status == CashierShiftStatus.Open, cancellationToken);
                if (shift is null) return await FailureAndRollback<PaymentDto>(transaction, "SHIFT_NOT_OPEN", "请先开班，再登记现金或人工外部收款", cancellationToken);
            }
            var now = clock.GetUtcNow();
            var localTime = await StoreLocalTimeAsync(tenantId, command.StoreId, now, cancellationToken);
            if (localTime is null) return await FailureAndRollback<PaymentDto>(transaction, "VALIDATION_FAILED", "门店时区配置无效", cancellationToken);
            var drafts = command.Allocations.Select(line =>
            {
                var method = methods[line.MethodId];
                return new PaymentAllocationDraft(method.Id, method.Code, method.Name, method.Category, line.AmountMinor,
                    line.ExternalReference, method.RequiresOpenShift ? shift?.Id : null);
            }).ToList();
            order.BeginCheckout();
            var payment = new Payment(tenantId, command.StoreId, order.Id, CreatePaymentNo(localTime.Value),
                order.ReceivableMinor, drafts, now);
            order.Settle(now);
            var visit = await db.Visits.SingleAsync(x => x.Id == order.VisitId && x.TenantId == tenantId, cancellationToken);
            visit.Complete();
            db.Payments.Add(payment);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, payment.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "payment.complete", "Payment", payment.Id,
                PaymentStatus.Processing.ToString(), payment.Status.ToString(), command.CommandId, null, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "service_order.settle", "ServiceOrder", order.Id,
                ServiceOrderStatus.PendingPayment.ToString(), order.Status.ToString(), command.CommandId, null, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(payment));
        }
        catch (DomainRuleException exception) { return await DomainFailure<PaymentDto>(transaction, exception, cancellationToken); }
        catch (Exception exception) when (IsUniqueOrConcurrency(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<PaymentDto>("VERSION_CONFLICT", "消费单或班次状态已变化，请刷新后重试");
        }
    }

    private async Task<Result<CashierShiftDto>> GetShiftAsync(Guid tenantId, Guid storeId, Guid id,
        CancellationToken cancellationToken)
    {
        var shift = await db.CashierShifts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId &&
            x.StoreId == storeId, cancellationToken);
        return shift is null ? ResultFactory.Failure<CashierShiftDto>("SHIFT_NOT_FOUND", "班次不存在") : ResultFactory.Success(ToDto(shift));
    }

    private async Task<Result<PaymentDto>> GetPaymentAsync(Guid tenantId, Guid storeId, Guid id,
        CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking().Include(x => x.Allocations).SingleOrDefaultAsync(x => x.Id == id &&
            x.TenantId == tenantId && x.StoreId == storeId, cancellationToken);
        return payment is null ? ResultFactory.Failure<PaymentDto>("PAYMENT_NOT_FOUND", "支付单不存在") : ResultFactory.Success(ToDto(payment));
    }

    private async Task<Result<T>?> ReplayAsync<T>(Guid tenantId, Guid commandId, byte[] requestHash,
        Func<Guid, Task<Result<T>>> load, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
            return ResultFactory.Failure<T>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null ? null : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        return receipt is null ? ResultFactory.Failure<T>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新") : await load(receipt.EntityId);
    }

    private async Task<DateTimeOffset?> StoreLocalTimeAsync(Guid tenantId, Guid storeId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var zone = await db.Stores.Where(x => x.Id == storeId && x.TenantId == tenantId).Select(x => x.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);
        return zone is null ? null : TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone));
    }

    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] requestHash, Guid entityId, DateTimeOffset now) =>
        db.IdempotencyCommands.Add(new IdempotencyCommandRecord { CommandId = commandId, TenantId = tenantId,
            OperatorId = operatorId, RequestHash = requestHash, ResponseStatus = 200,
            ResponseBody = JsonSerializer.Serialize(new CommandReceipt(entityId)), CreatedAtUtc = now, CompletedAtUtc = now });

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, string entityType, Guid entityId,
        string? previous, string? current, Guid commandId, string? reason, DateTimeOffset now) => db.AuditEvents.Add(new AuditEventRecord
        { TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action, EntityType = entityType,
            EntityId = entityId, PreviousState = previous, CurrentState = current, Reason = reason, RequestId = commandId,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = now });

    private static PaymentDto ToDto(Payment payment) => new(payment.Id, payment.PaymentNo, payment.OrderId,
        payment.Status.ToString(), payment.Currency, payment.ReceivableMinor, payment.PaidMinor, payment.PaidAtUtc,
        payment.Allocations.OrderBy(x => x.CreatedAtUtc).Select(x => new PaymentAllocationDto(x.Id, x.MethodId,
            x.MethodCodeSnapshot, x.MethodNameSnapshot, x.Category.ToString(), x.AmountMinor, x.ExternalReference,
            x.ConfirmationStatus.ToString(), x.ReconciliationStatus.ToString(), x.ShiftId)).ToList());

    private static CashierShiftDto ToDto(CashierShift shift) => new(shift.Id, shift.ShiftNo, shift.OperatorId,
        shift.Status.ToString(), shift.OpeningCashMinor, shift.ExpectedCashMinor, shift.SubmittedCashMinor,
        shift.CashDifferenceMinor, shift.PendingReconciliationMinor, shift.HandoverNote, shift.OpenedAtUtc,
        shift.SubmittedAtUtc, shift.ReviewedBy, shift.ReviewReason, shift.ClosedAtUtc, shift.Version);

    private static async Task<Result<T>> FailureAndRollback<T>(IDbContextTransaction transaction, string code,
        string message, CancellationToken cancellationToken)
    { await RollbackIfActiveAsync(transaction, cancellationToken); return ResultFactory.Failure<T>(code, message); }
    private static async Task<Result<T>> DomainFailure<T>(IDbContextTransaction transaction, DomainRuleException exception,
        CancellationToken cancellationToken)
    { await RollbackIfActiveAsync(transaction, cancellationToken); return ResultFactory.Failure<T>(exception.Code, exception.Message); }
    private static async Task RollbackIfActiveAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    { try { await transaction.RollbackAsync(cancellationToken); } catch (InvalidOperationException) { } }
    private static byte[] RequestHash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static bool IsUniqueOrConcurrency(Exception exception)
    { var state = FindPostgres(exception)?.SqlState; return state is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected; }
    private static PostgresException? FindPostgres(Exception exception)
    { for (Exception? current = exception; current is not null; current = current.InnerException) if (current is PostgresException postgres) return postgres; return null; }
    private static string CreateShiftNo(DateTimeOffset localTime) => $"SH{localTime:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..32].ToUpperInvariant();
    private static string CreatePaymentNo(DateTimeOffset localTime) => $"PAY{localTime:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..36].ToUpperInvariant();
    private sealed record CommandReceipt(Guid EntityId);
}
