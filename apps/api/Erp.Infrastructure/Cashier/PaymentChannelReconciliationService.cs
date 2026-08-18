using System.Data;
using System.Text.Json;
using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Cashier;

internal sealed class PaymentChannelReconciliationService(ErpDbContext db,
    PaymentChannelCredentialResolver credentialResolver, PaymentChannelGatewayRegistry gateways,
    TimeProvider clock, IHttpContextAccessor httpContextAccessor) : IPaymentChannelReconciliationService
{
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public async Task<PageResult<PaymentChannelReconciliationRunDto>> ListAsync(Guid tenantId,
        Guid storeId, DateOnly? fromDate, DateOnly? toDate, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var today = ChinaDate(clock.GetUtcNow());
        var from = fromDate ?? today.AddDays(-30);
        var to = toDate ?? today;
        if (from > to) return new PageResult<PaymentChannelReconciliationRunDto>([], 0, page, pageSize);
        if (to.DayNumber - from.DayNumber > 90) from = to.AddDays(-90);
        var query = db.PaymentChannelReconciliationRuns.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId &&
                        x.BusinessDate >= from && x.BusinessDate <= to);
        var total = await query.CountAsync(cancellationToken);
        var runs = await query
            .OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.AttemptNo)
            .ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        if (runs.Count == 0) return new PageResult<PaymentChannelReconciliationRunDto>([], total, page, pageSize);
        var runIds = runs.Select(x => x.Id).ToList();
        var items = await db.PaymentChannelReconciliationItems.AsNoTracking()
            .Where(x => x.TenantId == tenantId && runIds.Contains(x.RunId) &&
                        x.Status != PaymentChannelReconciliationItemStatus.Matched)
            .OrderBy(x => x.Status).ThenBy(x => x.MatchKey).ToListAsync(cancellationToken);
        var grouped = items.GroupBy(x => x.RunId).ToDictionary(x => x.Key, x => x.Select(Map).ToList());
        var mapped = runs.Select(run => Map(run, grouped.GetValueOrDefault(run.Id) ?? [])).ToList();
        return new PageResult<PaymentChannelReconciliationRunDto>(mapped, total, page, pageSize);
    }

    public async Task<Result<PaymentChannelReconciliationRunDto>> StartAsync(Guid tenantId,
        StartPaymentChannelReconciliationCommand command, CancellationToken cancellationToken)
    {
        var today = ChinaDate(clock.GetUtcNow());
        if (command.BusinessDate >= today || command.BusinessDate < today.AddDays(-90))
            return FailureRun("VALIDATION_FAILED", "只能对账最近90天内且早于今天的账单");

        PaymentChannelConfiguration configuration;
        PaymentChannelReconciliationRun run;
        PaymentChannelCredentialProfile credentialProfile;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
                         cancellationToken))
        {
            try
            {
                configuration = await db.PaymentChannelConfigurations.SingleOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.StoreId == command.StoreId &&
                    x.Provider == command.Provider, cancellationToken)
                    ?? throw new DomainRuleException("PAYMENT_CHANNEL_NOT_FOUND", "当前门店没有该支付渠道配置");
                if (!credentialResolver.TryResolve(configuration.Provider, configuration.CredentialProfile,
                        out var resolved, out var missing) || resolved is null)
                    return await FailRun(transaction, "PAYMENT_CHANNEL_CREDENTIALS_INCOMPLETE",
                        $"渠道凭据不完整：{string.Join('、', missing)}", cancellationToken);
                credentialProfile = resolved;
                if (!PaymentChannelCredentialResolver.IsEnvironmentCompatible(configuration.Environment,
                        credentialProfile, out var environmentMessage))
                    return await FailRun(transaction, "PAYMENT_CHANNEL_ENVIRONMENT_MISMATCH",
                        environmentMessage, cancellationToken);
                if (await db.PaymentChannelReconciliationRuns.AnyAsync(x =>
                        x.ConfigurationId == configuration.Id && x.BusinessDate == command.BusinessDate &&
                        x.Status == PaymentChannelReconciliationRunStatus.Running, cancellationToken))
                    return await FailRun(transaction, "RECONCILIATION_ALREADY_RUNNING",
                        "该渠道当天的账单正在对账，请稍后刷新", cancellationToken);
                var previousAttempt = await db.PaymentChannelReconciliationRuns
                    .Where(x => x.ConfigurationId == configuration.Id && x.BusinessDate == command.BusinessDate)
                    .Select(x => (int?)x.AttemptNo).MaxAsync(cancellationToken) ?? 0;
                run = new PaymentChannelReconciliationRun(tenantId, command.StoreId, configuration.Id,
                    command.Provider, command.BusinessDate, checked(previousAttempt + 1), command.OperatorId,
                    clock.GetUtcNow());
                db.PaymentChannelReconciliationRuns.Add(run);
                AddAudit(tenantId, command.StoreId, command.OperatorId, "payment_channel.reconciliation.start",
                    "PaymentChannelReconciliationRun", run.Id, null, run.Status.ToString(), null,
                    new { provider = command.Provider.ToString(), businessDate = command.BusinessDate },
                    clock.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DomainRuleException exception)
            {
                await RollbackQuietly(transaction, cancellationToken);
                return FailureRun(exception.Code, exception.Message);
            }
            catch (Exception exception) when (IsConcurrency(exception))
            {
                await RollbackQuietly(transaction, cancellationToken);
                return FailureRun("RECONCILIATION_ALREADY_RUNNING", "该渠道当天的账单正在对账，请稍后刷新");
            }
        }

        var bill = await gateways.Get(command.Provider).DownloadBillAsync(credentialProfile,
            command.BusinessDate, cancellationToken);
        db.ChangeTracker.Clear();
        if (!bill.IsSuccess || bill.SourceSha256 is not { Length: 32 })
        {
            await MarkFailedAsync(tenantId, command.StoreId, command.OperatorId, run.Id,
                bill.ErrorCode ?? "CHANNEL_BILL_INVALID_RESPONSE", cancellationToken);
            return FailureRun(bill.ErrorCode ?? "CHANNEL_BILL_INVALID_RESPONSE",
                bill.ErrorMessage ?? "渠道账单下载或解析失败");
        }

        return await ApplyBillAsync(tenantId, command, run.Id, bill, cancellationToken);
    }

    public async Task<Result<PaymentChannelReconciliationItemDto>> ResolveAsync(Guid tenantId,
        ResolvePaymentChannelReconciliationItemCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var item = await db.PaymentChannelReconciliationItems.SingleOrDefaultAsync(x =>
                x.Id == command.ItemId && x.TenantId == tenantId, cancellationToken);
            if (item is null)
                return await FailItem(transaction, "RECONCILIATION_ITEM_NOT_FOUND", "对账差异不存在",
                    cancellationToken);
            var run = await db.PaymentChannelReconciliationRuns.SingleAsync(x =>
                x.Id == item.RunId && x.TenantId == tenantId, cancellationToken);
            if (run.StoreId != command.StoreId)
                return await FailItem(transaction, "RECONCILIATION_ITEM_NOT_FOUND", "对账差异不存在",
                    cancellationToken);
            if (item.Version != command.ExpectedVersion)
                return await FailItem(transaction, "VERSION_CONFLICT", "对账差异已变化，请刷新后重试",
                    cancellationToken);
            var previous = item.Status.ToString();
            item.Resolve(command.OperatorId, command.Reason, clock.GetUtcNow());
            if (item.PaymentAllocationId is { } allocationId)
            {
                var allocation = await db.PaymentAllocations.SingleAsync(x => x.Id == allocationId &&
                    x.TenantId == tenantId, cancellationToken);
                allocation.MarkReconciled(ReconciliationStatus.Resolved);
            }
            if (item.ChannelRefundId is { } channelRefundId)
            {
                var channelRefund = await db.PaymentChannelRefunds.SingleAsync(x => x.Id == channelRefundId &&
                    x.TenantId == tenantId, cancellationToken);
                channelRefund.MarkReconciled(ReconciliationStatus.Resolved);
            }
            AddAudit(tenantId, command.StoreId, command.OperatorId,
                "payment_channel.reconciliation.resolve", "PaymentChannelReconciliationItem", item.Id,
                previous, item.Status.ToString(), command.Reason,
                new { item.MatchKey, run.Provider, run.BusinessDate }, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(Map(item));
        }
        catch (DomainRuleException exception)
        {
            await RollbackQuietly(transaction, cancellationToken);
            return ResultFactory.Failure<PaymentChannelReconciliationItemDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConcurrency(exception))
        {
            await RollbackQuietly(transaction, cancellationToken);
            return ResultFactory.Failure<PaymentChannelReconciliationItemDto>("VERSION_CONFLICT",
                "对账差异已变化，请刷新后重试");
        }
    }

    private async Task<Result<PaymentChannelReconciliationRunDto>> ApplyBillAsync(Guid tenantId,
        StartPaymentChannelReconciliationCommand command, Guid runId, PaymentChannelBillResult bill,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var run = await db.PaymentChannelReconciliationRuns.SingleAsync(x => x.Id == runId &&
                x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
            if (run.Status != PaymentChannelReconciliationRunStatus.Running)
                return await FailRun(transaction, "STATE_TRANSITION_NOT_ALLOWED", "对账任务已经结束",
                    cancellationToken);
            var channelByKey = bill.Entries.ToDictionary(x => x.MatchKey, StringComparer.Ordinal);
            var tradeNumbers = bill.Entries.Where(x => x.ItemType == PaymentChannelReconciliationItemType.Payment)
                .Select(x => x.OutTradeNo!).Distinct().ToList();
            var refundNumbers = bill.Entries.Where(x => x.ItemType == PaymentChannelReconciliationItemType.Refund)
                .Select(x => x.OutRefundNo!).Distinct().ToList();
            var (startUtc, endUtc) = ChinaDay(command.BusinessDate);

            var channelOrders = await db.PaymentChannelOrders.Where(x =>
                x.TenantId == tenantId && x.ConfigurationId == run.ConfigurationId &&
                ((x.Status == PaymentChannelOrderStatus.Paid && x.PaidAtUtc >= startUtc &&
                  x.PaidAtUtc < endUtc) || tradeNumbers.Contains(x.OutTradeNo)))
                .ToListAsync(cancellationToken);
            var allocationIds = channelOrders.Select(x => x.PaymentAllocationId).Distinct().ToList();
            var allocations = await db.PaymentAllocations.Where(x => x.TenantId == tenantId &&
                allocationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
            var channelRefunds = await db.PaymentChannelRefunds.Where(x =>
                x.TenantId == tenantId && x.ConfigurationId == run.ConfigurationId &&
                (((x.Status == PaymentChannelRefundStatus.Processing ||
                   x.Status == PaymentChannelRefundStatus.Succeeded) && x.CreatedAtUtc >= startUtc &&
                  x.CreatedAtUtc < endUtc) || refundNumbers.Contains(x.OutRefundNo)))
                .ToListAsync(cancellationToken);

            var paymentsByKey = channelOrders.ToDictionary(x => $"PAY:{x.OutTradeNo}", StringComparer.Ordinal);
            var refundsByKey = channelRefunds.ToDictionary(x => $"REFUND:{x.OutRefundNo}",
                StringComparer.Ordinal);
            var keys = channelByKey.Keys.Concat(paymentsByKey.Keys).Concat(refundsByKey.Keys)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var items = new List<PaymentChannelReconciliationItem>(keys.Count);
            foreach (var key in keys)
            {
                channelByKey.TryGetValue(key, out var channel);
                PaymentChannelReconciliationItem item;
                if (key.StartsWith("PAY:", StringComparison.Ordinal))
                {
                    paymentsByKey.TryGetValue(key, out var local);
                    allocations.TryGetValue(local?.PaymentAllocationId ?? Guid.Empty, out var allocation);
                    var status = MatchPayment(local, allocation, channel);
                    item = new PaymentChannelReconciliationItem(tenantId, run.Id,
                        PaymentChannelReconciliationItemType.Payment, status, key,
                        channel?.OutTradeNo ?? local?.OutTradeNo, null,
                        channel?.ProviderTradeNo ?? local?.ProviderTradeNo, local?.PaymentAllocationId, null,
                        local?.AmountMinor, channel?.AmountMinor, channel?.FeeMinor ?? 0,
                        local is null ? null : $"{local.Status}/{allocation?.ConfirmationStatus}",
                        channel?.ChannelStatus);
                    allocation?.MarkReconciled(status == PaymentChannelReconciliationItemStatus.Matched
                        ? ReconciliationStatus.Matched : ReconciliationStatus.Difference);
                }
                else
                {
                    refundsByKey.TryGetValue(key, out var local);
                    var status = MatchRefund(local, channel);
                    item = new PaymentChannelReconciliationItem(tenantId, run.Id,
                        PaymentChannelReconciliationItemType.Refund, status, key,
                        channel?.OutTradeNo ?? local?.OutTradeNo, channel?.OutRefundNo ?? local?.OutRefundNo,
                        channel?.ProviderTradeNo ?? local?.ProviderRefundNo, null, local?.Id,
                        local?.AmountMinor, channel?.AmountMinor, channel?.FeeMinor ?? 0,
                        local?.Status.ToString(), channel?.ChannelStatus);
                    local?.MarkReconciled(status == PaymentChannelReconciliationItemStatus.Matched
                        ? ReconciliationStatus.Matched : ReconciliationStatus.Difference);
                }
                items.Add(item);
            }
            db.PaymentChannelReconciliationItems.AddRange(items);
            var matched = items.Count(x => x.Status == PaymentChannelReconciliationItemStatus.Matched);
            run.Complete(bill.Entries.Count, matched, items.Count - matched, bill.SourceSha256!,
                clock.GetUtcNow());
            AddAudit(tenantId, command.StoreId, command.OperatorId,
                "payment_channel.reconciliation.complete", "PaymentChannelReconciliationRun", run.Id,
                PaymentChannelReconciliationRunStatus.Running.ToString(), run.Status.ToString(), null,
                new { run.Provider, run.BusinessDate, run.ChannelEntryCount, run.MatchedCount,
                    run.DifferenceCount }, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(Map(run, items.Where(x =>
                    x.Status != PaymentChannelReconciliationItemStatus.Matched).Select(Map).ToList()));
        }
        catch (DomainRuleException exception)
        {
            await RollbackQuietly(transaction, cancellationToken);
            await MarkFailedAsync(tenantId, command.StoreId, command.OperatorId, runId, exception.Code,
                cancellationToken);
            return FailureRun(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConcurrency(exception))
        {
            await RollbackQuietly(transaction, cancellationToken);
            await MarkFailedAsync(tenantId, command.StoreId, command.OperatorId, runId,
                "VERSION_CONFLICT", cancellationToken);
            return FailureRun("VERSION_CONFLICT", "对账期间本地账务发生变化，请重新执行");
        }
    }

    private async Task MarkFailedAsync(Guid tenantId, Guid storeId, Guid operatorId, Guid runId,
        string failureCode, CancellationToken cancellationToken)
    {
        // ApplyBillAsync may call this from a rolled-back catch block before its transaction scope exits.
        // Dispose that transaction first so the failure state is persisted in a fresh transaction.
        if (db.Database.CurrentTransaction is { } currentTransaction)
            await currentTransaction.DisposeAsync();
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var run = await db.PaymentChannelReconciliationRuns.SingleOrDefaultAsync(x => x.Id == runId &&
            x.TenantId == tenantId && x.Status == PaymentChannelReconciliationRunStatus.Running,
            cancellationToken);
        if (run is null) return;
        run.Fail(failureCode, clock.GetUtcNow());
        AddAudit(tenantId, storeId, operatorId, "payment_channel.reconciliation.fail",
            "PaymentChannelReconciliationRun", run.Id,
            PaymentChannelReconciliationRunStatus.Running.ToString(), run.Status.ToString(), null,
            new { run.Provider, run.BusinessDate, failureCode }, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static PaymentChannelReconciliationItemStatus MatchPayment(PaymentChannelOrder? local,
        PaymentAllocation? allocation, PaymentChannelBillEntry? channel)
    {
        if (local is null) return PaymentChannelReconciliationItemStatus.ChannelOnly;
        if (channel is null) return PaymentChannelReconciliationItemStatus.LocalOnly;
        if (local.AmountMinor != channel.AmountMinor)
            return PaymentChannelReconciliationItemStatus.AmountMismatch;
        if (local.Status != PaymentChannelOrderStatus.Paid || allocation is null ||
            allocation.ConfirmationStatus != PaymentConfirmationStatus.ChannelConfirmed)
            return PaymentChannelReconciliationItemStatus.StateMismatch;
        return PaymentChannelReconciliationItemStatus.Matched;
    }

    private static PaymentChannelReconciliationItemStatus MatchRefund(PaymentChannelRefund? local,
        PaymentChannelBillEntry? channel)
    {
        if (local is null) return PaymentChannelReconciliationItemStatus.ChannelOnly;
        if (channel is null) return PaymentChannelReconciliationItemStatus.LocalOnly;
        if (local.AmountMinor != channel.AmountMinor)
            return PaymentChannelReconciliationItemStatus.AmountMismatch;
        // Provider bills record refund acceptance snapshots; final success remains query-result authoritative.
        if (local.Status is PaymentChannelRefundStatus.Created or PaymentChannelRefundStatus.Failed)
            return PaymentChannelReconciliationItemStatus.StateMismatch;
        return PaymentChannelReconciliationItemStatus.Matched;
    }

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action,
        string entityType, Guid entityId, string? previous, string? current, string? reason,
        object metadata, DateTimeOffset now) => db.AuditEvents.Add(new AuditEventRecord
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
        TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background",
        OccurredAtUtc = now,
        Metadata = JsonSerializer.Serialize(metadata),
    });

    private static DateOnly ChinaDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(ChinaOffset).DateTime);

    private static (DateTimeOffset Start, DateTimeOffset End) ChinaDay(DateOnly date)
    {
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), ChinaOffset).ToUniversalTime();
        return (start, start.AddDays(1));
    }

    private static PaymentChannelReconciliationRunDto Map(PaymentChannelReconciliationRun run,
        IReadOnlyList<PaymentChannelReconciliationItemDto> items) => new(run.Id, run.ConfigurationId,
        run.Provider.ToString(), run.BusinessDate, run.AttemptNo, run.Status.ToString(),
        run.ChannelEntryCount, run.MatchedCount, run.DifferenceCount,
        run.SourceSha256 is null ? null : Convert.ToHexString(run.SourceSha256).ToLowerInvariant(),
        run.FailureCode, run.StartedBy, run.StartedAtUtc, run.CompletedAtUtc, run.Version, items);

    private static PaymentChannelReconciliationItemDto Map(PaymentChannelReconciliationItem item) =>
        new(item.Id, item.ItemType.ToString(), item.Status.ToString(), item.MatchKey, item.OutTradeNo,
            item.OutRefundNo, item.ProviderTradeNo, item.PaymentAllocationId, item.ChannelRefundId,
            item.LocalAmountMinor, item.ChannelAmountMinor, item.ChannelFeeMinor, item.LocalStatus,
            item.ChannelStatus, item.ResolvedBy, item.ResolvedAtUtc, item.ResolutionReason, item.Version);

    private static Result<PaymentChannelReconciliationRunDto> FailureRun(string code, string message) =>
        ResultFactory.Failure<PaymentChannelReconciliationRunDto>(code, message);

    private static async Task<Result<PaymentChannelReconciliationRunDto>> FailRun(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string code, string message,
        CancellationToken cancellationToken)
    {
        await RollbackQuietly(transaction, cancellationToken);
        return FailureRun(code, message);
    }

    private static async Task<Result<PaymentChannelReconciliationItemDto>> FailItem(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string code, string message,
        CancellationToken cancellationToken)
    {
        await RollbackQuietly(transaction, cancellationToken);
        return ResultFactory.Failure<PaymentChannelReconciliationItemDto>(code, message);
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
}
