using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Cashier;

internal sealed class RefundService(ErpDbContext db, TimeProvider clock,
    IHttpContextAccessor httpContextAccessor, PaymentChannelCredentialResolver credentialResolver,
    PaymentChannelGatewayRegistry gatewayRegistry) : IRefundService
{
    public async Task<IReadOnlyList<RefundDto>> ListAsync(Guid tenantId, Guid storeId, Guid? paymentId,
        CancellationToken cancellationToken)
    {
        var query = db.Refunds.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId);
        if (paymentId.HasValue) query = query.Where(x => x.PaymentId == paymentId.Value);
        var refunds = await query.OrderByDescending(x => x.RequestedAtUtc).Take(100)
            .ToListAsync(cancellationToken);
        var paymentIds = refunds.Select(x => x.PaymentId).Distinct().ToList();
        var refundIds = refunds.Select(x => x.Id).ToList();
        var payments = await db.Payments.AsNoTracking().Where(x => paymentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var channelRefunds = await db.PaymentChannelRefunds.AsNoTracking()
            .Where(x => refundIds.Contains(x.RefundId)).ToDictionaryAsync(x => x.RefundId, cancellationToken);
        return refunds.Where(x => payments.ContainsKey(x.PaymentId))
            .Select(x => ToDto(x, payments[x.PaymentId], channelRefunds.GetValueOrDefault(x.Id))).ToList();
    }

    public async Task<Result<RefundDto>> RequestAsync(Guid tenantId, RequestRefundCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty || command.Lines.Count is 0 or > 20)
            return ResultFactory.Failure<RefundDto>("VALIDATION_FAILED", "退款请求参数不完整");
        var requestHash = Hash(JsonSerializer.Serialize(command with { OperatorId = Guid.Empty }));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, requestHash, cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var payment = await db.Payments.Include(x => x.Allocations).SingleOrDefaultAsync(x =>
                x.Id == command.PaymentId && x.TenantId == tenantId && x.StoreId == command.StoreId,
                cancellationToken);
            if (payment is null)
                return await Fail(transaction, "PAYMENT_NOT_FOUND", "原支付单不存在", cancellationToken);
            if (payment.BusinessType is not (PaymentBusinessType.ServiceOrder or PaymentBusinessType.MemberTopup))
                return await Fail(transaction, "REFUND_SOURCE_NOT_SUPPORTED",
                    "当前仅支持消费退款和会员储值全额冲正", cancellationToken);
            if (payment.Version != command.ExpectedPaymentVersion)
                return await Fail(transaction, "VERSION_CONFLICT", "原支付单已变化，请刷新后重试", cancellationToken);
            if (payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartiallyRefunded))
                return await Fail(transaction, "STATE_TRANSITION_NOT_ALLOWED", "当前支付单不可退款", cancellationToken);

            var requested = command.Lines.GroupBy(x => x.OriginalAllocationId)
                .ToDictionary(x => x.Key, x => checked(x.Sum(y => y.AmountMinor)));
            if (requested.Count != command.Lines.Count || requested.Values.Any(x => x <= 0))
                return await Fail(transaction, "VALIDATION_FAILED", "退款分摊不能重复且金额必须大于0", cancellationToken);
            var allocations = payment.Allocations.Where(x => requested.ContainsKey(x.Id)).ToDictionary(x => x.Id);
            if (allocations.Count != requested.Count)
                return await Fail(transaction, "REFUND_ALLOCATION_NOT_FOUND", "退款分摊不属于原支付单", cancellationToken);
            if (allocations.Values.Any(x => x.Category == PaymentMethodCategory.ManualExternal))
                return await Fail(transaction, "REFUND_ROUTE_UNAVAILABLE",
                    "人工微信/支付宝尚未经过真实渠道确认，不能伪装为原路退款", cancellationToken);

            var reserved = await (from line in db.RefundLines
                                  join parent in db.Refunds on line.RefundId equals parent.Id
                                  where requested.Keys.Contains(line.OriginalAllocationId) &&
                                      (parent.Status == RefundStatus.PendingApproval ||
                                       parent.Status == RefundStatus.Processing ||
                                       parent.Status == RefundStatus.Completed)
                                  group line by line.OriginalAllocationId into item
                                  select new { Id = item.Key, Amount = item.Sum(x => x.AmountMinor) })
                .ToDictionaryAsync(x => x.Id, x => x.Amount, cancellationToken);
            foreach (var (allocationId, amount) in requested)
                if (reserved.GetValueOrDefault(allocationId) + amount > allocations[allocationId].AmountMinor)
                    return await Fail(transaction, "REFUND_AMOUNT_EXCEEDED",
                        "退款累计金额不能超过原支付分摊金额", cancellationToken);
            if (payment.BusinessType == PaymentBusinessType.MemberTopup &&
                (reserved.Values.Sum() != 0 || requested.Count != payment.Allocations.Count ||
                 requested.Sum(x => x.Value) != payment.PaidMinor || requested.Any(x =>
                     x.Value != allocations[x.Key].AmountMinor)))
                return await Fail(transaction, "TOPUP_FULL_REVERSAL_REQUIRED",
                    "会员储值只允许按原支付分摊整单冲正，不能部分退款", cancellationToken);

            var now = clock.GetUtcNow();
            var local = await StoreLocalTime(command.StoreId, tenantId, now, cancellationToken);
            if (local is null)
                return await Fail(transaction, "VALIDATION_FAILED", "门店时区配置无效", cancellationToken);
            var refund = new Refund(tenantId, command.StoreId, payment.Id, CreateRefundNo(local.Value),
                command.Reason, command.OperatorId, requested.Select(item =>
                {
                    var allocation = allocations[item.Key];
                    return new RefundLineDraft(allocation.Id, item.Value, allocation.Category,
                        allocation.MemberAccountId);
                }), now);
            db.Refunds.Add(refund);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, refund.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "refund.request", refund.Id, null,
                refund.Status.ToString(), command.CommandId, command.Reason, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(refund, payment));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<RefundDto>(exception.Code, exception.Message);
        }
        catch (OverflowException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<RefundDto>("VALIDATION_FAILED", "退款金额超过允许范围");
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<RefundDto>("VERSION_CONFLICT", "退款请求发生并发冲突，请刷新后重试");
        }
    }

    public async Task<Result<RefundDto>> ApproveAsync(Guid tenantId, ApproveRefundCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<RefundDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var requestHash = Hash($"REFUND_APPROVE|{command.StoreId}|{command.RefundId}|{command.ExpectedVersion}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, requestHash, cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var refund = await db.Refunds.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.Id == command.RefundId && x.TenantId == tenantId && x.StoreId == command.StoreId,
                cancellationToken);
            if (refund is null)
                return await Fail(transaction, "REFUND_NOT_FOUND", "退款单不存在", cancellationToken);
            if (refund.Version != command.ExpectedVersion)
                return await Fail(transaction, "VERSION_CONFLICT", "退款单已变化，请刷新后重试", cancellationToken);
            var payment = await db.Payments.Include(x => x.Allocations).SingleAsync(x =>
                x.Id == refund.PaymentId && x.TenantId == tenantId, cancellationToken);
            ServiceOrder? order = null;
            MemberTopupOrder? topup = null;
            if (payment.BusinessType == PaymentBusinessType.ServiceOrder)
                order = await db.ServiceOrders.SingleAsync(x => x.Id == payment.BusinessId &&
                    x.TenantId == tenantId, cancellationToken);
            else if (payment.BusinessType == PaymentBusinessType.MemberTopup)
                topup = await db.MemberTopupOrders.SingleAsync(x => x.Id == payment.BusinessId &&
                    x.TenantId == tenantId, cancellationToken);
            else
                return await Fail(transaction, "REFUND_SOURCE_NOT_SUPPORTED",
                    "当前仅支持消费退款和会员储值全额冲正", cancellationToken);
            if (refund.Lines.Any(x => x.Category == PaymentMethodCategory.ChannelExternal))
            {
                if (refund.Lines.Count != 1 ||
                    refund.Lines.Any(x => x.Category != PaymentMethodCategory.ChannelExternal) || order is null)
                    return await Fail(transaction, "CHANNEL_REFUND_MIX_NOT_ALLOWED",
                        "真实渠道退款必须来自一笔消费渠道分摊，不能与其他退款方式混合", cancellationToken);
                var line = refund.Lines.Single();
                var allocation = payment.Allocations.SingleOrDefault(x => x.Id == line.OriginalAllocationId);
                if (allocation is null || allocation.ChannelProvider is null ||
                    allocation.ConfirmationStatus != PaymentConfirmationStatus.ChannelConfirmed ||
                    string.IsNullOrWhiteSpace(allocation.ExternalReference))
                    return await Fail(transaction, "CHANNEL_REFUND_SOURCE_INVALID",
                        "原渠道支付尚未确认或交易号不完整", cancellationToken);
                var originalChannelOrder = await db.PaymentChannelOrders.SingleOrDefaultAsync(x =>
                    x.PaymentAllocationId == allocation.Id && x.Status == PaymentChannelOrderStatus.Paid,
                    cancellationToken);
                if (originalChannelOrder is null)
                    return await Fail(transaction, "CHANNEL_ORDER_NOT_FOUND",
                        "原渠道支付订单不存在", cancellationToken);
                var configuration = await db.PaymentChannelConfigurations.SingleOrDefaultAsync(x =>
                    x.Id == originalChannelOrder.ConfigurationId && x.TenantId == tenantId,
                    cancellationToken);
                if (configuration is null)
                    return await Fail(transaction, "PAYMENT_CHANNEL_NOT_FOUND",
                        "原支付渠道配置不存在", cancellationToken);
                if (!credentialResolver.TryResolve(configuration.Provider, configuration.CredentialProfile,
                        out var profile, out var missing) || profile is null)
                    return await Fail(transaction, "PAYMENT_CHANNEL_CREDENTIALS_INCOMPLETE",
                        $"渠道退款凭据不完整：{string.Join('、', missing)}", cancellationToken);
                if (!PaymentChannelCredentialResolver.IsEnvironmentCompatible(configuration.Environment,
                        profile, out var environmentMessage))
                    return await Fail(transaction, "PAYMENT_CHANNEL_ENVIRONMENT_MISMATCH",
                        environmentMessage, cancellationToken);

                var channelStartedAt = clock.GetUtcNow();
                refund.BeginChannelProcessing(command.ApproverId);
                var channelRefund = new PaymentChannelRefund(tenantId, configuration.Id, refund.Id,
                    originalChannelOrder.Id, configuration.Provider, refund.RefundNo,
                    originalChannelOrder.OutTradeNo, allocation.ExternalReference, refund.AmountMinor);
                db.PaymentChannelRefunds.Add(channelRefund);
                AddReceipt(tenantId, command.CommandId, command.ApproverId, requestHash, refund.Id,
                    channelStartedAt);
                AddAudit(tenantId, command.StoreId, command.ApproverId, "refund.channel.processing", refund.Id,
                    RefundStatus.PendingApproval.ToString(), refund.Status.ToString(), command.CommandId,
                    refund.Reason, channelStartedAt);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                db.ChangeTracker.Clear();
                await transaction.DisposeAsync();
                return await ExecuteChannelRefundAsync(tenantId,
                    new OperateChannelRefundCommand(command.StoreId, refund.Id, command.ApproverId), false,
                    cancellationToken);
            }
            CashierShift? cashShift = null;
            if (refund.Lines.Any(x => x.Category == PaymentMethodCategory.Cash))
            {
                cashShift = await db.CashierShifts.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                    x.StoreId == command.StoreId && x.OperatorId == command.ApproverId &&
                    x.Status == CashierShiftStatus.Open, cancellationToken);
                if (cashShift is null)
                    return await Fail(transaction, "SHIFT_NOT_OPEN", "现金退款前请由审批人先开班", cancellationToken);
                var cashReceipts = await db.PaymentAllocations.Where(x => x.ShiftId == cashShift.Id &&
                    x.Category == PaymentMethodCategory.Cash &&
                    x.ConfirmationStatus == PaymentConfirmationStatus.CashRecorded)
                    .SumAsync(x => (long?)x.AmountMinor, cancellationToken) ?? 0;
                var previousCashRefunds = await db.RefundLines.Where(x => x.CashShiftId == cashShift.Id &&
                    x.CompletedAtUtc != null).SumAsync(x => (long?)x.AmountMinor, cancellationToken) ?? 0;
                var requestedCash = refund.Lines.Where(x => x.Category == PaymentMethodCategory.Cash)
                    .Sum(x => x.AmountMinor);
                if (cashShift.OpeningCashMinor + cashReceipts - previousCashRefunds < requestedCash)
                    return await Fail(transaction, "INSUFFICIENT_SHIFT_CASH",
                        "当前班次可用现金不足，不能完成本次现金退款", cancellationToken);
            }
            var now = clock.GetUtcNow();
            var accountIds = refund.Lines.Where(x => x.MemberAccountId.HasValue)
                .Select(x => x.MemberAccountId!.Value).ToList();
            var accounts = await db.MemberAccounts.Where(x => accountIds.Contains(x.Id) &&
                x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, cancellationToken);
            foreach (var line in refund.Lines.Where(x => x.MemberAccountId.HasValue))
            {
                if (!accounts.TryGetValue(line.MemberAccountId!.Value, out var account))
                    return await Fail(transaction, "MEMBER_ACCOUNT_NOT_FOUND",
                        "原会员支付账户不存在", cancellationToken);
                db.MemberAccountLedgers.Add(account.Credit("PaymentRefund", refund.Id, line.AmountMinor,
                    command.CommandId, now));
                AddAudit(tenantId, command.StoreId, command.ApproverId, "membership.account.refund_credit",
                    account.Id, null, account.BalanceUnits.ToString(CultureInfo.InvariantCulture),
                    command.CommandId, refund.Reason, now);
            }
            if (topup is not null)
            {
                var topupAccounts = await db.MemberAccounts.Where(x => x.TenantId == tenantId &&
                    x.CardId == topup.CardId && (x.AccountType == MemberAccountType.Principal ||
                    x.AccountType == MemberAccountType.Bonus)).ToDictionaryAsync(x => x.AccountType,
                    cancellationToken);
                if (!topupAccounts.TryGetValue(MemberAccountType.Principal, out var principal) ||
                    !topupAccounts.TryGetValue(MemberAccountType.Bonus, out var bonus))
                    return await Fail(transaction, "MEMBER_ACCOUNT_NOT_FOUND",
                        "会员资金账户不完整，不能冲正储值", cancellationToken);
                MemberTopupReversalPolicy.EnsureOriginalBalancesAvailable(principal.BalanceUnits,
                    bonus.BalanceUnits, topup.PrincipalMinor, topup.BonusMinor);
                db.MemberAccountLedgers.Add(principal.Debit("MemberTopupRefund", refund.Id,
                    topup.PrincipalMinor, command.CommandId, now));
                if (topup.BonusMinor > 0)
                    db.MemberAccountLedgers.Add(bonus.Debit("MemberTopupRefund", refund.Id,
                        topup.BonusMinor, command.CommandId, now));
                AddAudit(tenantId, command.StoreId, command.ApproverId,
                    "membership.topup.refund_debit", topup.Id, topup.Status.ToString(),
                    MemberTopupStatus.Refunded.ToString(), command.CommandId, refund.Reason, now);
            }
            payment.ApplyRefund(refund.AmountMinor);
            if (order is not null) order.ApplyRefund(refund.AmountMinor);
            if (topup is not null) topup.ApplyFullRefund();
            refund.Complete(command.ApproverId, cashShift?.Id, now);
            AddReceipt(tenantId, command.CommandId, command.ApproverId, requestHash, refund.Id, now);
            AddAudit(tenantId, command.StoreId, command.ApproverId, "refund.complete", refund.Id,
                RefundStatus.PendingApproval.ToString(), refund.Status.ToString(), command.CommandId,
                refund.Reason, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(refund, payment));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<RefundDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<RefundDto>("VERSION_CONFLICT", "退款审批发生并发冲突，请刷新后重试");
        }
    }

    public async Task<Result<RefundDto>> RejectAsync(Guid tenantId, RejectRefundCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<RefundDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var requestHash = Hash($"REFUND_REJECT|{command.StoreId}|{command.RefundId}|{command.ExpectedVersion}|{command.Reason}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, requestHash, cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var refund = await db.Refunds.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.Id == command.RefundId && x.TenantId == tenantId && x.StoreId == command.StoreId,
                cancellationToken);
            if (refund is null)
                return await Fail(transaction, "REFUND_NOT_FOUND", "退款单不存在", cancellationToken);
            if (refund.Version != command.ExpectedVersion)
                return await Fail(transaction, "VERSION_CONFLICT", "退款单已变化，请刷新后重试", cancellationToken);
            var payment = await db.Payments.SingleAsync(x => x.Id == refund.PaymentId &&
                x.TenantId == tenantId, cancellationToken);
            var now = clock.GetUtcNow();
            refund.Reject(command.ApproverId, command.Reason);
            AddReceipt(tenantId, command.CommandId, command.ApproverId, requestHash, refund.Id, now);
            AddAudit(tenantId, command.StoreId, command.ApproverId, "refund.reject", refund.Id,
                RefundStatus.PendingApproval.ToString(), refund.Status.ToString(), command.CommandId,
                command.Reason, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(refund, payment));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<RefundDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<RefundDto>("VERSION_CONFLICT", "退款拒绝发生并发冲突，请刷新后重试");
        }
    }

    public Task<Result<RefundDto>> QueryChannelAsync(Guid tenantId, OperateChannelRefundCommand command,
        CancellationToken cancellationToken) =>
        ExecuteChannelRefundAsync(tenantId, command, true, cancellationToken);

    public Task<Result<RefundDto>> RetryChannelAsync(Guid tenantId, OperateChannelRefundCommand command,
        CancellationToken cancellationToken) =>
        ExecuteChannelRefundAsync(tenantId, command, false, cancellationToken);

    private async Task<Result<RefundDto>> ExecuteChannelRefundAsync(Guid tenantId,
        OperateChannelRefundCommand command, bool queryOnly, CancellationToken cancellationToken)
    {
        var channelRefund = await db.PaymentChannelRefunds.AsNoTracking().SingleOrDefaultAsync(x =>
            x.RefundId == command.RefundId && x.TenantId == tenantId, cancellationToken);
        var refund = await db.Refunds.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.Id == command.RefundId && x.TenantId == tenantId && x.StoreId == command.StoreId,
            cancellationToken);
        if (channelRefund is null || refund is null)
            return ResultFactory.Failure<RefundDto>("CHANNEL_REFUND_NOT_FOUND", "渠道退款单不存在");
        if (refund.Status == RefundStatus.Completed &&
            channelRefund.Status == PaymentChannelRefundStatus.Succeeded)
        {
            var completedPayment = await db.Payments.AsNoTracking().SingleAsync(x =>
                x.Id == refund.PaymentId, cancellationToken);
            return ResultFactory.Success(ToDto(refund, completedPayment, channelRefund));
        }
        if (refund.Status != RefundStatus.Processing)
            return ResultFactory.Failure<RefundDto>("STATE_TRANSITION_NOT_ALLOWED", "当前退款单不在渠道处理中");

        var configuration = await db.PaymentChannelConfigurations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == channelRefund.ConfigurationId && x.TenantId == tenantId, cancellationToken);
        if (configuration is null)
            return ResultFactory.Failure<RefundDto>("PAYMENT_CHANNEL_NOT_FOUND", "原支付渠道配置不存在");
        if (!credentialResolver.TryResolve(channelRefund.Provider, configuration.CredentialProfile,
                out var profile, out var missing) || profile is null)
            return ResultFactory.Failure<RefundDto>("PAYMENT_CHANNEL_CREDENTIALS_INCOMPLETE",
                $"渠道退款凭据不完整：{string.Join('、', missing)}");
        if (!PaymentChannelCredentialResolver.IsEnvironmentCompatible(configuration.Environment, profile,
                out var environmentMessage))
            return ResultFactory.Failure<RefundDto>("PAYMENT_CHANNEL_ENVIRONMENT_MISMATCH",
                environmentMessage);
        var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == refund.PaymentId,
            cancellationToken);
        var gatewayRequest = new PaymentChannelRefundRequest(channelRefund.OutTradeNo,
            channelRefund.ProviderTradeNo, channelRefund.OutRefundNo, channelRefund.AmountMinor,
            payment.ReceivableMinor, refund.Reason);
        var gateway = gatewayRegistry.Get(channelRefund.Provider);
        var result = queryOnly
            ? await gateway.QueryRefundAsync(profile, gatewayRequest, cancellationToken)
            : await gateway.RefundAsync(profile, gatewayRequest, cancellationToken);

        if (!result.IsSuccess && !IsExplicitChannelRejection(result.ErrorCode))
            return ResultFactory.Failure<RefundDto>(result.ErrorCode ?? "CHANNEL_REFUND_UNAVAILABLE",
                result.ErrorMessage ?? "渠道退款状态暂时不可确认");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var currentChannel = await db.PaymentChannelRefunds.SingleAsync(x =>
                x.Id == channelRefund.Id && x.TenantId == tenantId, cancellationToken);
            var currentRefund = await db.Refunds.Include(x => x.Lines).SingleAsync(x =>
                x.Id == refund.Id && x.TenantId == tenantId, cancellationToken);
            var currentPayment = await db.Payments.Include(x => x.Allocations).SingleAsync(x =>
                x.Id == currentRefund.PaymentId && x.TenantId == tenantId, cancellationToken);
            if (queryOnly) currentChannel.RecordQuery(clock.GetUtcNow());

            if (!result.IsSuccess || result.State == PaymentChannelRefundState.Failed)
            {
                currentChannel.MarkFailed(result.ErrorCode ?? "CHANNEL_REFUND_FAILED");
            }
            else if (result.RefundAmountMinor != currentChannel.AmountMinor)
            {
                currentChannel.MarkFailed("CHANNEL_REFUND_AMOUNT_CONFLICT");
            }
            else if (result.State == PaymentChannelRefundState.Succeeded)
            {
                currentChannel.MarkSucceeded(result.ProviderRefundNo, clock.GetUtcNow());
                if (currentRefund.Status == RefundStatus.Processing)
                {
                    currentPayment.ApplyRefund(currentRefund.AmountMinor);
                    var order = await db.ServiceOrders.SingleAsync(x =>
                        x.Id == currentPayment.BusinessId && x.TenantId == tenantId, cancellationToken);
                    order.ApplyRefund(currentRefund.AmountMinor);
                    currentRefund.CompleteChannel(clock.GetUtcNow());
                    AddAudit(tenantId, command.StoreId, command.OperatorId, "refund.channel.complete",
                        currentRefund.Id, RefundStatus.Processing.ToString(), currentRefund.Status.ToString(),
                        null, currentRefund.Reason, clock.GetUtcNow());
                }
            }
            else if (result.State == PaymentChannelRefundState.Pending)
            {
                currentChannel.MarkProcessing(result.ProviderRefundNo);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
                return ResultFactory.Failure<RefundDto>("CHANNEL_REFUND_STATE_UNKNOWN",
                    "渠道退款状态不明确，本地账务未变更");
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(currentRefund, currentPayment, currentChannel));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<RefundDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<RefundDto>("VERSION_CONFLICT", "渠道退款状态已变化，请重新查询");
        }
    }

    private async Task<Result<RefundDto>?> ReplayAsync(Guid tenantId, Guid commandId, byte[] hash,
        CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, hash))
            return ResultFactory.Failure<RefundDto>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null ? null : JsonSerializer.Deserialize<Receipt>(existing.ResponseBody);
        if (receipt is null) return ResultFactory.Failure<RefundDto>("COMMAND_IN_PROGRESS", "请求正在处理");
        var refund = await db.Refunds.AsNoTracking().Include(x => x.Lines).SingleAsync(x =>
            x.Id == receipt.EntityId && x.TenantId == tenantId, cancellationToken);
        var payment = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == refund.PaymentId &&
            x.TenantId == tenantId, cancellationToken);
        var channelRefund = await db.PaymentChannelRefunds.AsNoTracking().SingleOrDefaultAsync(x =>
            x.RefundId == refund.Id, cancellationToken);
        return ResultFactory.Success(ToDto(refund, payment, channelRefund));
    }

    private static RefundDto ToDto(Refund refund, Payment payment, PaymentChannelRefund? channelRefund = null) =>
        new(refund.Id, refund.PaymentId,
        payment.BusinessType.ToString(), payment.BusinessId, refund.RefundNo,
        refund.Status.ToString(), refund.AmountMinor, refund.Reason, refund.RequestedBy,
        refund.RequestedAtUtc, refund.ApprovedBy, refund.CompletedAtUtc, refund.RejectionReason,
        refund.Version, refund.Lines.Select(x => new RefundLineDto(x.Id, x.OriginalAllocationId,
            x.AmountMinor, x.Category.ToString(), x.MemberAccountId, x.Route.ToString(), x.CashShiftId,
            x.CompletedAtUtc)).ToList(), channelRefund is null ? null : new ChannelRefundDto(channelRefund.Id,
            channelRefund.Provider.ToString(), channelRefund.OutRefundNo, channelRefund.ProviderRefundNo,
            channelRefund.AmountMinor, channelRefund.Status.ToString(), channelRefund.FailureCode,
            channelRefund.LastQueriedAtUtc, channelRefund.SucceededAtUtc, channelRefund.Version));
    private async Task<DateTimeOffset?> StoreLocalTime(Guid storeId, Guid tenantId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var zone = await db.Stores.Where(x => x.Id == storeId && x.TenantId == tenantId)
            .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
        return zone is null ? null : TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone));
    }
    private static string CreateRefundNo(DateTimeOffset local) =>
        $"RF{local:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..40].ToUpperInvariant();
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] hash, Guid id,
        DateTimeOffset now) => db.IdempotencyCommands.Add(new IdempotencyCommandRecord
        {
            CommandId = commandId, TenantId = tenantId, OperatorId = operatorId, RequestHash = hash,
            ResponseStatus = 200, ResponseBody = JsonSerializer.Serialize(new Receipt(id)),
            CreatedAtUtc = now, CompletedAtUtc = now
        });
    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, Guid id,
        string? previous, string? current, Guid? commandId, string? reason, DateTimeOffset now) =>
        db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action,
            EntityType = "Refund", EntityId = id, PreviousState = previous, CurrentState = current,
            RequestId = commandId, Reason = reason,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = now
        });
    private static async Task<Result<RefundDto>> Fail(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string code, string message,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return ResultFactory.Failure<RefundDto>(code, message);
    }
    private static bool IsConflict(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && postgres.SqlState is "23505" or "40001" or "40P01")
                return true;
        return false;
    }
    private static bool IsExplicitChannelRejection(string? errorCode) => errorCode is not null &&
        (errorCode.StartsWith("WECHAT_", StringComparison.Ordinal) ||
         errorCode.StartsWith("ALIPAY_", StringComparison.Ordinal));
    private sealed record Receipt(Guid EntityId);
}
