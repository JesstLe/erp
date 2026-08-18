using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Domain.Facilities;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Cashier;

internal sealed class PaymentChannelPaymentService(ErpDbContext db, PaymentChannelCredentialResolver credentials,
    PaymentChannelGatewayRegistry gateways, TimeProvider clock, IHttpContextAccessor httpContextAccessor)
    : IPaymentChannelPaymentService
{
    public async Task<Result<PaymentChannelOrderDto>> GetByServiceOrderAsync(Guid tenantId, Guid storeId,
        Guid orderId, CancellationToken cancellationToken)
    {
        var paymentId = await db.Payments.AsNoTracking().Where(x => x.TenantId == tenantId &&
                x.StoreId == storeId && x.BusinessType == PaymentBusinessType.ServiceOrder &&
                x.BusinessId == orderId && x.Status != PaymentStatus.Cancelled)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (paymentId is null) return Failure("CHANNEL_ORDER_NOT_FOUND", "消费单没有进行中的渠道支付");
        var channelOrderId = await (from channel in db.PaymentChannelOrders.AsNoTracking()
                join allocation in db.PaymentAllocations.AsNoTracking()
                    on channel.PaymentAllocationId equals allocation.Id
                where allocation.PaymentId == paymentId.Value
                orderby channel.AttemptNo descending
                select (Guid?)channel.Id).FirstOrDefaultAsync(cancellationToken);
        return channelOrderId is null
            ? Failure("CHANNEL_ORDER_NOT_FOUND", "消费单没有渠道支付记录")
            : await LoadDtoAsync(tenantId, storeId, channelOrderId.Value, cancellationToken);
    }

    public async Task<Result<PaymentChannelOrderDto>> InitiateAsync(Guid tenantId,
        InitiatePaymentChannelCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return Failure("VALIDATION_FAILED", "缺少幂等请求号");
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"CHANNEL_INIT|{command.StoreId}|{command.OrderId}|{command.ExpectedOrderVersion}|{command.MethodId}"));

        PaymentChannelConfiguration configuration;
        PaymentChannelOrder channelOrder;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
                         cancellationToken))
        {
            try
            {
                var replay = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.CommandId == command.CommandId, cancellationToken);
                if (replay is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    if (replay.TenantId != tenantId ||
                        !CryptographicOperations.FixedTimeEquals(replay.RequestHash, requestHash))
                        return Failure("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
                    var receipt = replay.ResponseBody is null
                        ? null : JsonSerializer.Deserialize<CommandReceipt>(replay.ResponseBody);
                    return receipt is null
                        ? Failure("COMMAND_IN_PROGRESS", "请求正在处理，请稍后查询")
                        : await LoadDtoAsync(tenantId, command.StoreId, receipt.EntityId, cancellationToken);
                }

                var order = await db.ServiceOrders.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                    x.Id == command.OrderId && x.TenantId == tenantId && x.StoreId == command.StoreId,
                    cancellationToken);
                if (order is null) return await RollbackFailure(transaction, "SERVICE_ORDER_NOT_FOUND",
                    "消费单不存在", cancellationToken);
                if (order.Version != command.ExpectedOrderVersion)
                    return await RollbackFailure(transaction, "VERSION_CONFLICT", "消费单已变化，请刷新后重试",
                        cancellationToken);
                if (order.Status != ServiceOrderStatus.PendingPayment)
                    return await RollbackFailure(transaction, "STATE_TRANSITION_NOT_ALLOWED",
                        "只有待支付消费单可以发起渠道支付", cancellationToken);
                if (order.ReceivableMinor <= 0)
                    return await RollbackFailure(transaction, "VALIDATION_FAILED", "零金额消费单不需要渠道支付",
                        cancellationToken);
                if (await db.Payments.AnyAsync(x => x.TenantId == tenantId &&
                        x.BusinessType == PaymentBusinessType.ServiceOrder && x.BusinessId == order.Id &&
                        x.Status != PaymentStatus.Cancelled, cancellationToken))
                    return await RollbackFailure(transaction, "PAYMENT_ALREADY_EXISTS",
                        "该消费单已有进行中或已完成的支付", cancellationToken);

                var method = await db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == command.MethodId &&
                    x.TenantId == tenantId && x.IsEnabled, cancellationToken);
                if (method is null || method.Category != PaymentMethodCategory.ChannelExternal ||
                    method.ChannelProvider is null)
                    return await RollbackFailure(transaction, "PAYMENT_METHOD_NOT_FOUND",
                        "真实渠道支付方式不存在或尚未启用", cancellationToken);
                configuration = await db.PaymentChannelConfigurations.SingleOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.StoreId == command.StoreId &&
                    x.Provider == method.ChannelProvider && x.IsEnabled, cancellationToken)
                    ?? throw new DomainRuleException("PAYMENT_CHANNEL_NOT_ENABLED", "当前门店尚未启用该支付渠道");
                if (!credentials.TryResolve(configuration.Provider, configuration.CredentialProfile,
                        out var resolvedProfile, out var missing))
                    return await RollbackFailure(transaction, "PAYMENT_CHANNEL_CREDENTIALS_INCOMPLETE",
                        $"渠道凭据不完整：{string.Join('、', missing)}", cancellationToken);
                if (resolvedProfile is null)
                    return await RollbackFailure(transaction, "PAYMENT_CHANNEL_CREDENTIALS_INCOMPLETE",
                        "渠道凭据解析失败", cancellationToken);
                if (!PaymentChannelCredentialResolver.IsEnvironmentCompatible(configuration.Environment,
                        resolvedProfile, out var environmentMessage))
                    return await RollbackFailure(transaction, "PAYMENT_CHANNEL_ENVIRONMENT_MISMATCH",
                        environmentMessage, cancellationToken);

                var shift = await db.CashierShifts.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                    x.StoreId == command.StoreId && x.OperatorId == command.OperatorId &&
                    x.Status == CashierShiftStatus.Open, cancellationToken);
                if (shift is null)
                    return await RollbackFailure(transaction, "SHIFT_NOT_OPEN", "请先开班，再发起渠道支付",
                        cancellationToken);

                var now = clock.GetUtcNow();
                var paymentNo = CreatePaymentNo(now);
                order.BeginCheckout();
                var payment = new Payment(tenantId, command.StoreId, order.Id, paymentNo, order.ReceivableMinor,
                    [new PaymentAllocationDraft(method.Id, method.Code, method.Name, method.Category,
                        order.ReceivableMinor, null, shift.Id, null, method.ChannelProvider)], now);
                var allocation = payment.Allocations.Single();
                channelOrder = new PaymentChannelOrder(tenantId, configuration.Id, allocation.Id,
                    method.ChannelProvider.Value, $"{paymentNo}-A1", 1, order.ReceivableMinor,
                    $"消费单 {order.OrderNo}", now.AddMinutes(15));
                db.Payments.Add(payment);
                db.PaymentChannelOrders.Add(channelOrder);
                db.IdempotencyCommands.Add(new IdempotencyCommandRecord
                {
                    CommandId = command.CommandId,
                    TenantId = tenantId,
                    OperatorId = command.OperatorId,
                    RequestHash = requestHash,
                    ResponseStatus = StatusCodes.Status200OK,
                    ResponseBody = JsonSerializer.Serialize(new CommandReceipt(channelOrder.Id)),
                    CreatedAtUtc = now,
                    CompletedAtUtc = now,
                });
                AddAudit(tenantId, command.StoreId, command.OperatorId, "payment_channel.order.initiate",
                    "PaymentChannelOrder", channelOrder.Id, null, channelOrder.Status.ToString(),
                    command.CommandId, now);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DomainRuleException exception)
            {
                await RollbackQuietly(transaction, cancellationToken);
                return Failure(exception.Code, exception.Message);
            }
            catch (Exception exception) when (IsConcurrency(exception))
            {
                await RollbackQuietly(transaction, cancellationToken);
                return Failure("VERSION_CONFLICT", "消费单或支付状态已变化，请刷新后重试");
            }
        }

        if (!credentials.TryResolve(configuration.Provider, configuration.CredentialProfile,
                out var credentialProfile, out var missingRequirements) || credentialProfile is null)
            return Failure("PAYMENT_CHANNEL_CREDENTIALS_INCOMPLETE",
                $"渠道凭据不完整：{string.Join('、', missingRequirements)}");
        credentialProfile = WithNotificationEndpoint(credentialProfile, configuration.Id);
        var createResult = await gateways.Get(configuration.Provider).CreateQrAsync(credentialProfile,
            new PaymentChannelCreateRequest(channelOrder.OutTradeNo, channelOrder.AmountMinor,
                channelOrder.Subject, channelOrder.ExpiresAtUtc), cancellationToken);

        db.ChangeTracker.Clear();
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
                         cancellationToken))
        {
            try
            {
                var current = await db.PaymentChannelOrders.SingleAsync(x => x.Id == channelOrder.Id &&
                    x.TenantId == tenantId, cancellationToken);
                if (createResult.IsSuccess && current.Status == PaymentChannelOrderStatus.Created)
                {
                    current.MarkQrReady(createResult.QrPayload!, clock.GetUtcNow());
                    AddAudit(tenantId, command.StoreId, command.OperatorId, "payment_channel.qr.ready",
                        "PaymentChannelOrder", current.Id, PaymentChannelOrderStatus.Created.ToString(),
                        current.Status.ToString(), command.CommandId, clock.GetUtcNow());
                }
                else if (!createResult.IsSuccess && IsExplicitRejection(createResult.ErrorCode) &&
                         current.Status == PaymentChannelOrderStatus.Created)
                {
                    var allocation = await db.PaymentAllocations.SingleAsync(x =>
                        x.Id == current.PaymentAllocationId, cancellationToken);
                    var payment = await db.Payments.Include(x => x.Allocations).SingleAsync(x =>
                        x.Id == allocation.PaymentId, cancellationToken);
                    var order = await db.ServiceOrders.SingleAsync(x => x.Id == payment.BusinessId,
                        cancellationToken);
                    current.Fail(createResult.ErrorCode!);
                    payment.CancelPendingChannelPayment();
                    order.CancelCheckout();
                    AddAudit(tenantId, command.StoreId, command.OperatorId, "payment_channel.order.rejected",
                        "PaymentChannelOrder", current.Id, PaymentChannelOrderStatus.Created.ToString(),
                        current.Status.ToString(), command.CommandId, clock.GetUtcNow());
                }
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception exception) when (IsConcurrency(exception))
            {
                await RollbackQuietly(transaction, cancellationToken);
            }
        }
        db.ChangeTracker.Clear();
        return await LoadDtoAsync(tenantId, command.StoreId, channelOrder.Id, cancellationToken);
    }

    public async Task<Result<PaymentChannelOrderDto>> QueryAsync(Guid tenantId,
        OperatePaymentChannelCommand command, CancellationToken cancellationToken)
    {
        var contextResult = await LoadGatewayContextAsync(tenantId, command.StoreId, command.ChannelOrderId,
            cancellationToken);
        if (!contextResult.IsSuccess || contextResult.Value is null)
            return Failure(contextResult.Error!.Code, contextResult.Error.Message);
        var context = contextResult.Value;
        var query = await context.Gateway.QueryAsync(context.Credentials, context.ChannelOrder.OutTradeNo,
            cancellationToken);
        if (!query.IsSuccess)
            return Failure(query.ErrorCode ?? "CHANNEL_QUERY_FAILED", query.ErrorMessage ?? "渠道查单失败");
        return await ApplyProviderQueryAsync(tenantId, command, query, cancellationToken);
    }

    public async Task<Result<PaymentChannelOrderDto>> CloseAsync(Guid tenantId,
        OperatePaymentChannelCommand command, CancellationToken cancellationToken)
    {
        var contextResult = await LoadGatewayContextAsync(tenantId, command.StoreId, command.ChannelOrderId,
            cancellationToken);
        if (!contextResult.IsSuccess || contextResult.Value is null)
            return Failure(contextResult.Error!.Code, contextResult.Error.Message);
        var context = contextResult.Value;
        var query = await context.Gateway.QueryAsync(context.Credentials, context.ChannelOrder.OutTradeNo,
            cancellationToken);
        if (!query.IsSuccess)
            return Failure(query.ErrorCode ?? "CHANNEL_QUERY_FAILED", "关单前查单失败，系统不会冒险关闭本地支付");
        if (query.State == PaymentChannelTradeState.Paid)
            return await ApplyProviderQueryAsync(tenantId, command, query, cancellationToken);
        if (query.State is PaymentChannelTradeState.Closed or PaymentChannelTradeState.Failed)
            return await CloseLocalAsync(tenantId, command, cancellationToken);
        if (query.State != PaymentChannelTradeState.Pending)
            return Failure("CHANNEL_STATE_UNKNOWN", "渠道订单状态不明确，暂不能关闭");

        var close = await context.Gateway.CloseAsync(context.Credentials, context.ChannelOrder.OutTradeNo,
            cancellationToken);
        if (!close.IsSuccess || close.State != PaymentChannelTradeState.Closed)
            return Failure(close.ErrorCode ?? "CHANNEL_CLOSE_FAILED",
                close.ErrorMessage ?? "渠道未确认关单，本地支付保持进行中");
        return await CloseLocalAsync(tenantId, command, cancellationToken);
    }

    public async Task<PaymentChannelNotificationResult> ProcessNotificationAsync(
        PaymentChannelNotificationCommand command, CancellationToken cancellationToken)
    {
        var configuration = await db.PaymentChannelConfigurations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == command.ConfigurationId && x.Provider == command.Provider, cancellationToken);
        if (configuration is null || !credentials.TryResolve(command.Provider, configuration.CredentialProfile,
                out var profile, out _) || profile is null)
            return new PaymentChannelNotificationResult(false, "PAYMENT_CHANNEL_CONFIGURATION_NOT_FOUND");
        if (!PaymentChannelCredentialResolver.IsEnvironmentCompatible(configuration.Environment, profile, out _))
            return new PaymentChannelNotificationResult(false, "PAYMENT_CHANNEL_ENVIRONMENT_MISMATCH");

        var notification = gateways.Get(command.Provider).VerifyNotification(profile,
            new PaymentChannelNotificationEnvelope(command.Headers, command.Body, command.Form));
        if (!notification.IsVerified || string.IsNullOrWhiteSpace(notification.ProviderEventId) ||
            string.IsNullOrWhiteSpace(notification.OutTradeNo))
            return new PaymentChannelNotificationResult(false,
                notification.ErrorCode ?? "CHANNEL_SIGNATURE_INVALID");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var duplicate = await db.PaymentChannelEvents.AsNoTracking().SingleOrDefaultAsync(x =>
                x.ConfigurationId == configuration.Id &&
                x.ProviderEventId == notification.ProviderEventId, cancellationToken);
            if (duplicate is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                var samePayload = CryptographicOperations.FixedTimeEquals(duplicate.PayloadSha256,
                    notification.PayloadSha256);
                return new PaymentChannelNotificationResult(samePayload && duplicate.Status is
                    PaymentChannelEventStatus.Processed or PaymentChannelEventStatus.Ignored,
                    samePayload ? duplicate.ErrorCode : "CHANNEL_EVENT_REPLAY_CONFLICT");
            }

            var channelOrder = await db.PaymentChannelOrders.SingleOrDefaultAsync(x =>
                x.ConfigurationId == configuration.Id && x.Provider == command.Provider &&
                x.OutTradeNo == notification.OutTradeNo, cancellationToken);
            if (channelOrder is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new PaymentChannelNotificationResult(false, "CHANNEL_ORDER_NOT_FOUND");
            }
            var channelEvent = new PaymentChannelEvent(configuration.TenantId, configuration.Id, channelOrder.Id,
                command.Provider, notification.ProviderEventId, notification.EventType ?? "UNKNOWN",
                notification.PayloadSha256, clock.GetUtcNow());
            db.PaymentChannelEvents.Add(channelEvent);
            if (notification.State != PaymentChannelTradeState.Paid)
            {
                channelEvent.Complete(PaymentChannelEventStatus.Ignored, clock.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new PaymentChannelNotificationResult(true, null);
            }

            var outcome = await ApplyPaidInsideTransactionAsync(channelOrder, channelEvent,
                notification.ProviderTradeNo, notification.AmountMinor, notification.PaidAtUtc,
                operatorId: null, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PaymentChannelNotificationResult(outcome is null, outcome);
        }
        catch (Exception exception) when (IsConcurrency(exception))
        {
            await RollbackQuietly(transaction, cancellationToken);
            return new PaymentChannelNotificationResult(false, "CHANNEL_NOTIFICATION_CONFLICT");
        }
        catch (DomainRuleException exception)
        {
            await RollbackQuietly(transaction, cancellationToken);
            return new PaymentChannelNotificationResult(false, exception.Code);
        }
    }

    private async Task<Result<PaymentChannelOrderDto>> ApplyProviderQueryAsync(Guid tenantId,
        OperatePaymentChannelCommand command, PaymentChannelQueryResult query,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var channelOrder = await db.PaymentChannelOrders.SingleOrDefaultAsync(x =>
                x.Id == command.ChannelOrderId && x.TenantId == tenantId, cancellationToken);
            if (channelOrder is null)
                return await RollbackFailure(transaction, "CHANNEL_ORDER_NOT_FOUND", "渠道订单不存在",
                    cancellationToken);
            channelOrder.RecordQuery(clock.GetUtcNow());
            if (query.State == PaymentChannelTradeState.Paid)
            {
                var evidence = $"{channelOrder.OutTradeNo}|{query.ProviderTradeNo}|{query.AmountMinor}|{query.PaidAtUtc:O}";
                var digest = SHA256.HashData(Encoding.UTF8.GetBytes(evidence));
                var eventId = $"QUERY:{Convert.ToHexString(digest)[..32]}";
                var channelEvent = await db.PaymentChannelEvents.SingleOrDefaultAsync(x =>
                    x.ConfigurationId == channelOrder.ConfigurationId && x.ProviderEventId == eventId,
                    cancellationToken);
                if (channelEvent is null)
                {
                    channelEvent = new PaymentChannelEvent(tenantId, channelOrder.ConfigurationId,
                        channelOrder.Id, channelOrder.Provider, eventId, "ACTIVE_QUERY_PAID", digest,
                        clock.GetUtcNow());
                    db.PaymentChannelEvents.Add(channelEvent);
                }
                if (channelEvent.Status == PaymentChannelEventStatus.Received)
                {
                    var error = await ApplyPaidInsideTransactionAsync(channelOrder, channelEvent,
                        query.ProviderTradeNo, query.AmountMinor, query.PaidAtUtc, command.OperatorId,
                        cancellationToken);
                    if (error is not null)
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        return Failure(error, "渠道支付结果需要人工处理");
                    }
                }
            }
            else if (query.State is PaymentChannelTradeState.Closed or PaymentChannelTradeState.Failed)
            {
                await CancelPendingInsideTransactionAsync(channelOrder, command.OperatorId, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DomainRuleException exception)
        {
            await RollbackQuietly(transaction, cancellationToken);
            return Failure(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConcurrency(exception))
        {
            await RollbackQuietly(transaction, cancellationToken);
            return Failure("VERSION_CONFLICT", "渠道支付状态已变化，请重新查询");
        }
        db.ChangeTracker.Clear();
        return await LoadDtoAsync(tenantId, command.StoreId, command.ChannelOrderId, cancellationToken);
    }

    private async Task<string?> ApplyPaidInsideTransactionAsync(PaymentChannelOrder channelOrder,
        PaymentChannelEvent channelEvent, string? providerTradeNo, long? amountMinor, DateTimeOffset? paidAtUtc,
        Guid? operatorId, CancellationToken cancellationToken)
    {
        if (amountMinor != channelOrder.AmountMinor || string.IsNullOrWhiteSpace(providerTradeNo))
        {
            channelEvent.Complete(PaymentChannelEventStatus.Failed, clock.GetUtcNow(),
                "CHANNEL_AMOUNT_OR_TRADE_CONFLICT");
            return "CHANNEL_AMOUNT_OR_TRADE_CONFLICT";
        }
        var allocation = await db.PaymentAllocations.SingleAsync(x =>
            x.Id == channelOrder.PaymentAllocationId, cancellationToken);
        var payment = await db.Payments.Include(x => x.Allocations).SingleAsync(x =>
            x.Id == allocation.PaymentId, cancellationToken);
        var order = await db.ServiceOrders.SingleAsync(x => x.Id == payment.BusinessId,
            cancellationToken);

        if (channelOrder.Status is PaymentChannelOrderStatus.Closed or PaymentChannelOrderStatus.Failed or
            PaymentChannelOrderStatus.Expired || payment.Status == PaymentStatus.Cancelled)
        {
            payment.MarkReversalRequired();
            channelEvent.Complete(PaymentChannelEventStatus.Failed, clock.GetUtcNow(),
                "CHANNEL_LATE_PAYMENT_REQUIRES_REVERSAL");
            AddAudit(payment.TenantId, payment.StoreId, operatorId, "payment_channel.late_payment",
                "Payment", payment.Id, PaymentStatus.Cancelled.ToString(), payment.Status.ToString(), null,
                clock.GetUtcNow());
            return "CHANNEL_LATE_PAYMENT_REQUIRES_REVERSAL";
        }

        var paidAt = paidAtUtc ?? clock.GetUtcNow();
        channelOrder.MarkPaid(providerTradeNo, paidAt);
        payment.ConfirmChannelAllocation(allocation.Id, providerTradeNo, paidAt);
        if (order.Status == ServiceOrderStatus.PaymentProcessing)
            order.Settle(paidAt);
        else if (order.Status != ServiceOrderStatus.Settled)
            throw new DomainRuleException("CHANNEL_RESULT_CONFLICT", "消费单状态与已支付结果不一致");
        var visit = await db.Visits.SingleAsync(x => x.Id == order.VisitId, cancellationToken);
        if (visit.Status == VisitStatus.ServiceEnded) visit.Complete();
        else if (visit.Status != VisitStatus.Completed)
            throw new DomainRuleException("CHANNEL_RESULT_CONFLICT", "接待状态与已支付结果不一致");
        channelEvent.Complete(PaymentChannelEventStatus.Processed, clock.GetUtcNow());
        AddAudit(payment.TenantId, payment.StoreId, operatorId, "payment_channel.payment.confirmed",
            "Payment", payment.Id, PaymentStatus.Processing.ToString(), payment.Status.ToString(), null,
            clock.GetUtcNow());
        return null;
    }

    private async Task<Result<PaymentChannelOrderDto>> CloseLocalAsync(Guid tenantId,
        OperatePaymentChannelCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var channelOrder = await db.PaymentChannelOrders.SingleOrDefaultAsync(x =>
                x.Id == command.ChannelOrderId && x.TenantId == tenantId, cancellationToken);
            if (channelOrder is null)
                return await RollbackFailure(transaction, "CHANNEL_ORDER_NOT_FOUND", "渠道订单不存在",
                    cancellationToken);
            await CancelPendingInsideTransactionAsync(channelOrder, command.OperatorId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DomainRuleException exception)
        {
            await RollbackQuietly(transaction, cancellationToken);
            return Failure(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConcurrency(exception))
        {
            await RollbackQuietly(transaction, cancellationToken);
            return Failure("VERSION_CONFLICT", "渠道支付状态已变化，请重新查询");
        }
        db.ChangeTracker.Clear();
        return await LoadDtoAsync(tenantId, command.StoreId, command.ChannelOrderId, cancellationToken);
    }

    private async Task CancelPendingInsideTransactionAsync(PaymentChannelOrder channelOrder, Guid operatorId,
        CancellationToken cancellationToken)
    {
        if (channelOrder.Status == PaymentChannelOrderStatus.Paid)
            throw new DomainRuleException("CHANNEL_ALREADY_PAID", "渠道订单已经支付，不能关闭");
        if (channelOrder.Status is PaymentChannelOrderStatus.Created or PaymentChannelOrderStatus.QrReady)
            channelOrder.Close(clock.GetUtcNow());
        var allocation = await db.PaymentAllocations.SingleAsync(x =>
            x.Id == channelOrder.PaymentAllocationId, cancellationToken);
        var payment = await db.Payments.Include(x => x.Allocations).SingleAsync(x =>
            x.Id == allocation.PaymentId, cancellationToken);
        var order = await db.ServiceOrders.SingleAsync(x => x.Id == payment.BusinessId,
            cancellationToken);
        if (payment.Status == PaymentStatus.Processing) payment.CancelPendingChannelPayment();
        if (order.Status == ServiceOrderStatus.PaymentProcessing) order.CancelCheckout();
        AddAudit(payment.TenantId, payment.StoreId, operatorId, "payment_channel.order.closed",
            "PaymentChannelOrder", channelOrder.Id, null, channelOrder.Status.ToString(), null,
            clock.GetUtcNow());
    }

    private async Task<Result<GatewayContext>> LoadGatewayContextAsync(Guid tenantId, Guid storeId,
        Guid channelOrderId, CancellationToken cancellationToken)
    {
        var channelOrder = await db.PaymentChannelOrders.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == channelOrderId && x.TenantId == tenantId, cancellationToken);
        if (channelOrder is null) return ResultFactory.Failure<GatewayContext>("CHANNEL_ORDER_NOT_FOUND",
            "渠道订单不存在");
        var configuration = await db.PaymentChannelConfigurations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == channelOrder.ConfigurationId && x.TenantId == tenantId && x.StoreId == storeId,
            cancellationToken);
        if (configuration is null) return ResultFactory.Failure<GatewayContext>("PAYMENT_CHANNEL_NOT_FOUND",
            "支付渠道配置不存在");
        if (!credentials.TryResolve(configuration.Provider, configuration.CredentialProfile,
                out var profile, out var missing) || profile is null)
            return ResultFactory.Failure<GatewayContext>("PAYMENT_CHANNEL_CREDENTIALS_INCOMPLETE",
                $"渠道凭据不完整：{string.Join('、', missing)}");
        if (!PaymentChannelCredentialResolver.IsEnvironmentCompatible(configuration.Environment, profile,
                out var environmentMessage))
            return ResultFactory.Failure<GatewayContext>("PAYMENT_CHANNEL_ENVIRONMENT_MISMATCH",
                environmentMessage);
        return ResultFactory.Success(new GatewayContext(channelOrder, configuration,
            WithNotificationEndpoint(profile, configuration.Id), gateways.Get(configuration.Provider)));
    }

    private async Task<Result<PaymentChannelOrderDto>> LoadDtoAsync(Guid tenantId, Guid storeId,
        Guid channelOrderId, CancellationToken cancellationToken)
    {
        var channelOrder = await db.PaymentChannelOrders.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == channelOrderId && x.TenantId == tenantId, cancellationToken);
        if (channelOrder is null) return Failure("CHANNEL_ORDER_NOT_FOUND", "渠道订单不存在");
        var allocation = await db.PaymentAllocations.AsNoTracking().SingleAsync(x =>
            x.Id == channelOrder.PaymentAllocationId, cancellationToken);
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == allocation.PaymentId &&
            x.StoreId == storeId, cancellationToken);
        if (payment is null) return Failure("PAYMENT_NOT_FOUND", "支付单不存在");
        var order = await db.ServiceOrders.AsNoTracking().SingleAsync(x => x.Id == payment.BusinessId,
            cancellationToken);
        return ResultFactory.Success(new PaymentChannelOrderDto(channelOrder.Id, channelOrder.ConfigurationId,
            payment.Id, channelOrder.PaymentAllocationId, channelOrder.Provider.ToString(),
            channelOrder.OutTradeNo, channelOrder.AmountMinor, channelOrder.Status.ToString(),
            channelOrder.QrPayload, channelOrder.ProviderTradeNo, channelOrder.FailureCode,
            channelOrder.ExpiresAtUtc, channelOrder.PaidAtUtc, channelOrder.ClosedAtUtc,
            channelOrder.LastQueriedAtUtc, payment.Status.ToString(), order.Status.ToString(),
            channelOrder.Version));
    }

    private void AddAudit(Guid tenantId, Guid storeId, Guid? operatorId, string action, string entityType,
        Guid entityId, string? previous, string? current, Guid? requestId, DateTimeOffset now) =>
        db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId,
            StoreId = storeId,
            OperatorId = operatorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            PreviousState = previous,
            CurrentState = current,
            RequestId = requestId,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "payment-channel-callback",
            OccurredAtUtc = now,
        });

    private static PaymentChannelCredentialProfile WithNotificationEndpoint(
        PaymentChannelCredentialProfile profile, Guid configurationId) =>
        profile with { NotifyUrl = $"{profile.NotifyUrl.TrimEnd('/')}/{configurationId:D}" };

    private static bool IsExplicitRejection(string? errorCode) => errorCode is not null &&
        (errorCode.StartsWith("WECHAT_", StringComparison.Ordinal) ||
         errorCode.StartsWith("ALIPAY_", StringComparison.Ordinal));

    private static string CreatePaymentNo(DateTimeOffset now) =>
        $"PAY{now:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..36].ToUpperInvariant();

    private static Result<PaymentChannelOrderDto> Failure(string code, string message) =>
        ResultFactory.Failure<PaymentChannelOrderDto>(code, message);

    private static async Task<Result<PaymentChannelOrderDto>> RollbackFailure(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string code, string message,
        CancellationToken cancellationToken)
    {
        await RollbackQuietly(transaction, cancellationToken);
        return Failure(code, message);
    }

    private static async Task RollbackQuietly(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        catch (InvalidOperationException) { }
    }

    private static bool IsConcurrency(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && postgres.SqlState is
                PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected) return true;
        return exception is DbUpdateConcurrencyException;
    }

    private sealed record CommandReceipt(Guid EntityId);
    private sealed record GatewayContext(PaymentChannelOrder ChannelOrder,
        PaymentChannelConfiguration Configuration, PaymentChannelCredentialProfile Credentials,
        IPaymentChannelGateway Gateway);
}
