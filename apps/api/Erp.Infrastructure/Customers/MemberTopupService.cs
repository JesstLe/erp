using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Application.Customers;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Erp.Infrastructure.Customers;

internal sealed class MemberTopupService(ErpDbContext db, TimeProvider clock,
    IHttpContextAccessor httpContextAccessor) : IMemberTopupService
{
    public async Task<IReadOnlyList<MemberTopupDto>> ListAsync(Guid tenantId, Guid storeId, Guid? customerId,
        CancellationToken cancellationToken)
    {
        var query = db.MemberTopupOrders.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId);
        if (customerId.HasValue) query = query.Where(x => x.CustomerId == customerId.Value);
        var orders = await query.OrderByDescending(x => x.PaidAtUtc).Take(100).ToListAsync(cancellationToken);
        var ids = orders.Select(x => x.Id).ToList();
        var payments = await db.Payments.AsNoTracking().Include(x => x.Allocations)
            .Where(x => x.TenantId == tenantId && x.BusinessType == PaymentBusinessType.MemberTopup &&
                ids.Contains(x.BusinessId)).ToDictionaryAsync(x => x.BusinessId, cancellationToken);
        return orders.Where(x => payments.ContainsKey(x.Id)).Select(x => ToDto(x, payments[x.Id])).ToList();
    }

    public async Task<Result<MemberTopupDto>> CreateAndSettleAsync(Guid tenantId,
        CreateMemberTopupCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<MemberTopupDto>("VALIDATION_FAILED", "缺少幂等请求号");
        if (command.Allocations.Count is 0 or > 20)
            return ResultFactory.Failure<MemberTopupDto>("PAYMENT_ALLOCATION_UNBALANCED", "支付分摊需要1到20行");
        if (command.BonusMinor > 0 && !command.CanGrantBonus)
            return ResultFactory.Failure<MemberTopupDto>("FORBIDDEN_ACTION", "赠送奖励金只能由最高权限账号确认");
        var hash = RequestHash(JsonSerializer.Serialize(command with
        {
            OperatorId = Guid.Empty,
            CanGrantBonus = false,
        }));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, hash,
            id => GetAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;

        try
        {
            var customerExists = await db.Customers.AnyAsync(x => x.Id == command.CustomerId &&
                x.TenantId == tenantId && x.HomeStoreId == command.StoreId && x.Status == CustomerStatus.Active,
                cancellationToken);
            if (!customerExists)
                return await FailureAndRollback(transaction, "CUSTOMER_NOT_FOUND", "顾客不存在或当前不可储值", cancellationToken);

            var card = await db.MemberCards.SingleOrDefaultAsync(x => x.Id == command.CardId &&
                x.TenantId == tenantId && x.CustomerId == command.CustomerId && x.StoreId == command.StoreId &&
                x.Status == MemberCardStatus.Active, cancellationToken);
            if (card is null)
                return await FailureAndRollback(transaction, "MEMBER_CARD_NOT_FOUND", "会员卡不存在或当前不可储值", cancellationToken);

            var now = clock.GetUtcNow();
            var localTime = await StoreLocalTimeAsync(tenantId, command.StoreId, now, cancellationToken);
            if (localTime is null)
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "门店时区配置无效", cancellationToken);
            var localDate = DateOnly.FromDateTime(localTime.Value.DateTime);
            if (card.ValidFrom > localDate || card.ValidTo < localDate)
                return await FailureAndRollback(transaction, "MEMBER_CARD_NOT_ACTIVE", "会员卡尚未生效或已经到期", cancellationToken);

            var methodIds = command.Allocations.Select(x => x.MethodId).Distinct().ToList();
            if (methodIds.Count != command.Allocations.Count)
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "同一支付方式不能重复分摊", cancellationToken);
            var methods = await db.PaymentMethods.Where(x => x.TenantId == tenantId && x.IsEnabled &&
                methodIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            if (methods.Count != methodIds.Count)
                return await FailureAndRollback(transaction, "PAYMENT_METHOD_NOT_FOUND", "支付方式不存在或已停用", cancellationToken);
            if (methods.Values.Any(x => x.Category == PaymentMethodCategory.InternalAccount))
                return await FailureAndRollback(transaction, "PAYMENT_METHOD_NOT_ALLOWED", "会员账户余额不能用于购买自身储值", cancellationToken);
            if (methods.Values.Any(x => x.Category == PaymentMethodCategory.ChannelExternal))
                return await FailureAndRollback(transaction, "CHANNEL_TOPUP_NOT_AVAILABLE",
                    "微信或支付宝储值必须等待独立异步入账流程开放，不能先增加会员余额", cancellationToken);

            CashierShift? shift = null;
            if (methods.Values.Any(x => x.RequiresOpenShift))
            {
                shift = await db.CashierShifts.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                    x.StoreId == command.StoreId && x.OperatorId == command.OperatorId &&
                    x.Status == CashierShiftStatus.Open, cancellationToken);
                if (shift is null)
                    return await FailureAndRollback(transaction, "SHIFT_NOT_OPEN", "请先开班，再办理会员储值", cancellationToken);
            }

            var accounts = await db.MemberAccounts.Where(x => x.TenantId == tenantId && x.CardId == card.Id &&
                (x.AccountType == MemberAccountType.Principal || x.AccountType == MemberAccountType.Bonus))
                .ToDictionaryAsync(x => x.AccountType, cancellationToken);
            if (!accounts.TryGetValue(MemberAccountType.Principal, out var principalAccount) ||
                !accounts.TryGetValue(MemberAccountType.Bonus, out var bonusAccount))
                return await FailureAndRollback(transaction, "MEMBER_ACCOUNT_NOT_FOUND", "会员资金账户不完整，请联系管理员", cancellationToken);

            var topup = new MemberTopupOrder(tenantId, command.StoreId, command.CustomerId, command.CardId,
                CreateTopupNo(localTime.Value), command.PrincipalMinor, command.BonusMinor, command.Note, now);
            var drafts = command.Allocations.Select(line =>
            {
                var method = methods[line.MethodId];
                return new PaymentAllocationDraft(method.Id, method.Code, method.Name, method.Category,
                    line.AmountMinor, line.ExternalReference, method.RequiresOpenShift ? shift?.Id : null,
                    ChannelProvider: method.ChannelProvider);
            }).ToList();
            var payment = new Payment(tenantId, command.StoreId, PaymentBusinessType.MemberTopup, topup.Id,
                CreatePaymentNo(localTime.Value), topup.ReceivableMinor, drafts, now);

            db.MemberTopupOrders.Add(topup);
            db.Payments.Add(payment);
            db.MemberAccountLedgers.Add(principalAccount.Credit("MemberTopup", topup.Id,
                topup.PrincipalMinor, command.CommandId, now));
            if (topup.BonusMinor > 0)
                db.MemberAccountLedgers.Add(bonusAccount.Credit("MemberTopup", topup.Id,
                    topup.BonusMinor, command.CommandId, now));
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, topup.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "membership.topup.paid",
                "MemberTopupOrder", topup.Id, null, topup.Status.ToString(), command.CommandId,
                command.Note, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(topup, payment));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<MemberTopupDto>(exception.Code, exception.Message);
        }
        catch (OverflowException)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<MemberTopupDto>("VALIDATION_FAILED", "储值金额超过允许范围");
        }
        catch (Exception exception) when (IsUniqueOrConcurrency(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<MemberTopupDto>("VERSION_CONFLICT", "会员余额或班次已变化，请刷新后重试");
        }
    }

    private async Task<Result<MemberTopupDto>> GetAsync(Guid tenantId, Guid storeId, Guid id,
        CancellationToken cancellationToken)
    {
        var order = await db.MemberTopupOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id &&
            x.TenantId == tenantId && x.StoreId == storeId, cancellationToken);
        if (order is null) return ResultFactory.Failure<MemberTopupDto>("MEMBER_TOPUP_NOT_FOUND", "储值单不存在");
        var payment = await db.Payments.AsNoTracking().Include(x => x.Allocations).SingleOrDefaultAsync(x =>
            x.TenantId == tenantId && x.BusinessType == PaymentBusinessType.MemberTopup &&
            x.BusinessId == order.Id, cancellationToken);
        return payment is null
            ? ResultFactory.Failure<MemberTopupDto>("PAYMENT_NOT_FOUND", "储值单支付记录不存在")
            : ResultFactory.Success(ToDto(order, payment));
    }

    private static MemberTopupDto ToDto(MemberTopupOrder order, Payment payment) => new(order.Id,
        order.TopupNo, order.StoreId, order.CustomerId, order.CardId, order.PrincipalMinor,
        order.BonusMinor, order.ReceivableMinor, order.Status.ToString(), order.Note, order.PaidAtUtc,
        payment.Id, payment.PaymentNo, payment.Status.ToString(), payment.RefundedMinor, payment.Version,
        payment.Allocations.OrderBy(x => x.CreatedAtUtc)
            .Select(x => new PaymentAllocationDto(x.Id, x.MethodId, x.MethodCodeSnapshot,
                x.MethodNameSnapshot, x.Category.ToString(), x.AmountMinor, x.ExternalReference,
                x.ConfirmationStatus.ToString(), x.ReconciliationStatus.ToString(), x.ShiftId,
                x.MemberAccountId, x.ChannelProvider?.ToString())).ToList());

    private async Task<Result<MemberTopupDto>?> ReplayAsync(Guid tenantId, Guid commandId, byte[] hash,
        Func<Guid, Task<Result<MemberTopupDto>>> load, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, hash))
            return ResultFactory.Failure<MemberTopupDto>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null ? null : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        return receipt is null
            ? ResultFactory.Failure<MemberTopupDto>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新")
            : await load(receipt.EntityId);
    }

    private async Task<DateTimeOffset?> StoreLocalTimeAsync(Guid tenantId, Guid storeId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var zone = await db.Stores.Where(x => x.Id == storeId && x.TenantId == tenantId)
            .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
        return zone is null ? null : TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone));
    }

    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] hash, Guid entityId,
        DateTimeOffset now) => db.IdempotencyCommands.Add(new IdempotencyCommandRecord
        {
            CommandId = commandId,
            TenantId = tenantId,
            OperatorId = operatorId,
            RequestHash = hash,
            ResponseStatus = 200,
            ResponseBody = JsonSerializer.Serialize(new CommandReceipt(entityId)),
            CreatedAtUtc = now,
            CompletedAtUtc = now,
        });

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, string entityType,
        Guid entityId, string? previous, string? current, Guid commandId, string? reason,
        DateTimeOffset now) => db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId,
            StoreId = storeId,
            OperatorId = operatorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            PreviousState = previous,
            CurrentState = current,
            Reason = reason,
            RequestId = commandId,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background",
            OccurredAtUtc = now,
        });

    private static async Task<Result<MemberTopupDto>> FailureAndRollback(IDbContextTransaction transaction,
        string code, string message, CancellationToken cancellationToken)
    {
        await RollbackIfActiveAsync(transaction, cancellationToken);
        return ResultFactory.Failure<MemberTopupDto>(code, message);
    }

    private static async Task RollbackIfActiveAsync(IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        catch (InvalidOperationException) { }
    }

    private static byte[] RequestHash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static bool IsUniqueOrConcurrency(Exception exception)
    {
        var state = FindPostgres(exception)?.SqlState;
        return state is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.DeadlockDetected;
    }
    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
    private static string CreateTopupNo(DateTimeOffset localTime) =>
        $"TU{localTime:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..34].ToUpperInvariant();
    private static string CreatePaymentNo(DateTimeOffset localTime) =>
        $"PAY{localTime:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..36].ToUpperInvariant();
    private sealed record CommandReceipt(Guid EntityId);
}
