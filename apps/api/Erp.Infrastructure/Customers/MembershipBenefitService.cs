using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Common;
using Erp.Application.Customers;
using Erp.Domain.Cashier;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Customers;

internal sealed class MembershipBenefitService(ErpDbContext db, TimeProvider clock,
    IHttpContextAccessor httpContextAccessor) : IMembershipBenefitService
{
    public async Task<Result<MembershipBenefitsDto>> GetAsync(Guid tenantId, Guid storeId, Guid customerId,
        CancellationToken cancellationToken)
    {
        var customerIds = await CustomerIdsAsync(tenantId, customerId, cancellationToken);
        if (customerIds.Count == 0)
            return ResultFactory.Failure<MembershipBenefitsDto>("CUSTOMER_NOT_FOUND", "顾客不存在");
        var passes = await db.ServicePasses.AsNoTracking().Where(x => x.TenantId == tenantId &&
                customerIds.Contains(x.CustomerId))
            .OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
        var pointAccounts = await db.MemberAccounts.AsNoTracking().Where(x => x.TenantId == tenantId &&
                customerIds.Contains(x.CustomerId) && x.AccountType == MemberAccountType.Points)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        return ResultFactory.Success(new MembershipBenefitsDto(
            await ToPassDtosAsync(passes, cancellationToken),
            await ToPointDtosAsync(pointAccounts, cancellationToken)));
    }

    public async Task<Result<ServicePassDto>> IssuePassAsync(Guid tenantId, IssueServicePassCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<ServicePassDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var hash = RequestHash(command with { OperatorId = Guid.Empty });
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var replay = await ReplayPassAsync(tenantId, command.StoreId, command.CommandId, hash,
                cancellationToken);
            if (replay is not null) return replay;
            var card = await LoadCardAsync(tenantId, command.StoreId, command.CustomerId, command.CardId,
                cancellationToken);
            if (card is null) return await PassFailure(transaction, "MEMBER_CARD_NOT_FOUND",
                "会员卡不存在或当前不可用", cancellationToken);
            var service = await db.ServiceItems.AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == command.ServiceItemId && x.TenantId == tenantId && x.Status == CatalogItemStatus.Enabled,
                cancellationToken);
            if (service is null) return await PassFailure(transaction, "SERVICE_ITEM_NOT_FOUND",
                "服务项目不存在或已停用", cancellationToken);
            var now = clock.GetUtcNow();
            var localDate = await LocalDateAsync(tenantId, command.StoreId, now, cancellationToken)
                ?? throw new DomainRuleException("VALIDATION_FAILED", "门店时区配置无效");
            if (card.ValidFrom > localDate || card.ValidTo < localDate || command.ValidTo < localDate)
                throw new DomainRuleException("MEMBER_CARD_NOT_ACTIVE",
                    "会员卡尚未生效、已经到期，或次卡到期日早于当前门店日期");
            var pass = new ServicePass(tenantId, command.StoreId, command.CustomerId, card.Id,
                service.Id, command.PassName, command.PurchasedUses, command.BonusUses,
                command.ValidFrom, command.ValidTo, command.Reason);
            db.ServicePasses.Add(pass);
            db.ServicePassLedgers.Add(pass.CreateIssueLedger(command.CommandId, command.OperatorId, now));
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, pass.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "membership.service_pass.issued",
                "ServicePass", pass.Id, null, pass.Status.ToString(), command.CommandId, command.Reason, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success((await ToPassDtosAsync([pass], cancellationToken)).Single());
        }
        catch (DomainRuleException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServicePassDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServicePassDto>("VERSION_CONFLICT", "次卡数据已变化，请刷新后重试");
        }
    }

    public Task<Result<ServicePassDto>> RedeemPassAsync(Guid tenantId, RedeemServicePassCommand command,
        CancellationToken cancellationToken) => ChangePassAsync(tenantId, command.StoreId, command.PassId,
        command.ExpectedVersion, command.CommandId, command.OperatorId, command.Reason,
        "membership.service_pass.redeemed", async (pass, localDate, now) =>
        {
            if (command.ServiceOrderId.HasValue)
            {
                var order = await db.ServiceOrders.AsNoTracking().Include(x => x.Lines)
                    .SingleOrDefaultAsync(x => x.Id == command.ServiceOrderId.Value &&
                        x.TenantId == tenantId && x.StoreId == command.StoreId,
                        cancellationToken);
                if (order is null || order.CustomerId != pass.CustomerId ||
                    !order.Lines.Any(x => x.ServiceItemId == pass.ServiceItemId))
                    throw new DomainRuleException("SERVICE_PASS_ORDER_MISMATCH",
                        "关联消费单必须属于同一顾客并包含次卡对应服务项目");
            }
            return pass.Redeem(command.StoreId, command.Uses, command.ServiceOrderId, command.Reason,
                command.CommandId, command.OperatorId, localDate, now);
        }, command, cancellationToken);

    public Task<Result<ServicePassDto>> ReversePassAsync(Guid tenantId, ReverseServicePassCommand command,
        CancellationToken cancellationToken) => ChangePassAsync(tenantId, command.StoreId, command.PassId,
        command.ExpectedVersion, command.CommandId, command.OperatorId, command.Reason,
        "membership.service_pass.reversed", async (pass, localDate, now) =>
        {
            var original = await db.ServicePassLedgers.SingleOrDefaultAsync(x => x.Id == command.LedgerId &&
                x.TenantId == tenantId && x.PassId == pass.Id, cancellationToken)
                ?? throw new DomainRuleException("SERVICE_PASS_LEDGER_NOT_FOUND", "次卡流水不存在");
            if (await db.ServicePassLedgers.AnyAsync(x => x.TenantId == tenantId &&
                    x.ReversedLedgerId == original.Id, cancellationToken))
                throw new DomainRuleException("SERVICE_PASS_LEDGER_ALREADY_REVERSED", "该核销流水已经撤销");
            return pass.Reverse(command.StoreId, original, command.Reason, command.CommandId,
                command.OperatorId, localDate, now);
        }, command, cancellationToken);

    public Task<Result<ServicePassDto>> ExpirePassAsync(Guid tenantId, ExpireServicePassCommand command,
        CancellationToken cancellationToken) => ChangePassAsync(tenantId, command.StoreId, command.PassId,
        command.ExpectedVersion, command.CommandId, command.OperatorId, command.Reason,
        "membership.service_pass.expired", (pass, localDate, now) =>
            pass.Expire(command.StoreId, command.Reason, command.CommandId, command.OperatorId, localDate, now),
        command, cancellationToken);

    public async Task<Result<MemberPointSummaryDto>> AdjustPointsAsync(Guid tenantId,
        AdjustMemberPointsCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty || command.Units <= 0 || string.IsNullOrWhiteSpace(command.Reason))
            return ResultFactory.Failure<MemberPointSummaryDto>("VALIDATION_FAILED", "积分数量、原因和幂等请求号必填");
        return await ChangePointsAsync(tenantId, command.StoreId, command.CardId, command.CommandId,
            command.OperatorId, command.Reason, command, async (account, localDate, now) =>
            {
                var customerIds = await CustomerIdsAsync(tenantId, command.CustomerId, cancellationToken);
                if (!customerIds.Contains(account.CustomerId))
                    throw new DomainRuleException("MEMBER_ACCOUNT_TYPE_MISMATCH", "积分账户不属于当前顾客");
                if (command.Credit)
                {
                    if (command.ExpiresOn < localDate)
                        throw new DomainRuleException("VALIDATION_FAILED", "积分到期日不能早于当前门店日期");
                    var businessId = Guid.CreateVersion7();
                    var ledger = account.Credit("PointManualCredit", businessId, command.Units,
                        command.CommandId, now);
                    db.MemberAccountLedgers.Add(ledger);
                    db.MemberPointGrants.Add(new MemberPointGrant(tenantId, command.StoreId,
                        command.CustomerId, command.CardId, account.Id, command.Units, command.ExpiresOn,
                        "PointManualCredit", businessId));
                    return;
                }

                var grants = await db.MemberPointGrants.Where(x => x.TenantId == tenantId &&
                        x.AccountId == account.Id && x.Status == MemberPointGrantStatus.Active &&
                        (x.ExpiresOn == null || x.ExpiresOn >= localDate))
                    .OrderBy(x => x.ExpiresOn == null).ThenBy(x => x.ExpiresOn)
                    .ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken);
                if (grants.Sum(x => x.RemainingUnits) < command.Units)
                    throw new DomainRuleException("INSUFFICIENT_MEMBER_POINTS", "可用积分不足；已到期批次请先执行过期处理");
                var debitLedger = account.Debit("PointManualDebit", Guid.CreateVersion7(), command.Units,
                    command.CommandId, now);
                db.MemberAccountLedgers.Add(debitLedger);
                var remaining = command.Units;
                foreach (var grant in grants)
                {
                    var used = grant.Consume(remaining);
                    if (used > 0) db.MemberPointUseAllocations.Add(
                        new MemberPointUseAllocation(tenantId, debitLedger.Id, grant.Id, used));
                    remaining -= used;
                    if (remaining == 0) break;
                }
            }, command.Credit ? "membership.points.credited" : "membership.points.debited",
            cancellationToken);
    }

    public async Task<Result<MemberPointSummaryDto>> ReversePointsAsync(Guid tenantId,
        ReverseMemberPointsCommand command, CancellationToken cancellationToken) =>
        await ChangePointsAsync(tenantId, command.StoreId, command.CardId, command.CommandId,
            command.OperatorId, command.Reason, command, async (account, localDate, now) =>
            {
                var original = await db.MemberAccountLedgers.SingleOrDefaultAsync(x =>
                    x.Id == command.LedgerId && x.TenantId == tenantId && x.AccountId == account.Id,
                    cancellationToken) ?? throw new DomainRuleException("POINT_LEDGER_NOT_FOUND", "积分流水不存在");
                if (original.Direction != LedgerDirection.Debit || original.BusinessType != "PointManualDebit")
                    throw new DomainRuleException("POINT_LEDGER_NOT_REVERSIBLE", "只有人工扣减积分流水可以撤销");
                if (await db.MemberAccountLedgers.AnyAsync(x => x.TenantId == tenantId &&
                    x.AccountId == account.Id && x.BusinessType == "PointReversal" &&
                    x.BusinessId == original.Id, cancellationToken))
                    throw new DomainRuleException("POINT_LEDGER_ALREADY_REVERSED", "该积分流水已经撤销");
                var allocations = await db.MemberPointUseAllocations.Where(x =>
                    x.TenantId == tenantId && x.DebitLedgerId == original.Id).ToListAsync(cancellationToken);
                var grantIds = allocations.Select(x => x.GrantId).ToList();
                var grants = await db.MemberPointGrants.Where(x => grantIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, cancellationToken);
                foreach (var allocation in allocations)
                    grants[allocation.GrantId].Restore(allocation.Units, localDate);
                db.MemberAccountLedgers.Add(account.Credit("PointReversal", original.Id, original.Units,
                    command.CommandId, now));
            }, "membership.points.reversed", cancellationToken);

    public async Task<Result<MemberPointSummaryDto>> ExpirePointsAsync(Guid tenantId,
        ExpireMemberPointsCommand command, CancellationToken cancellationToken) =>
        await ChangePointsAsync(tenantId, command.StoreId, command.CardId, command.CommandId,
            command.OperatorId, command.Reason, command, async (account, localDate, now) =>
            {
                var grants = await db.MemberPointGrants.Where(x => x.TenantId == tenantId &&
                        x.AccountId == account.Id && x.Status == MemberPointGrantStatus.Active &&
                        x.ExpiresOn != null && x.ExpiresOn < localDate)
                    .OrderBy(x => x.ExpiresOn).ThenBy(x => x.Id).ToListAsync(cancellationToken);
                var expired = grants.Sum(x => x.Expire(localDate));
                if (expired <= 0)
                    throw new DomainRuleException("POINTS_NOT_DUE", "当前没有需要过期处理的积分");
                db.MemberAccountLedgers.Add(account.Debit("PointExpiration", command.CommandId,
                    expired, command.CommandId, now));
            }, "membership.points.expired", cancellationToken);

    private async Task<Result<ServicePassDto>> ChangePassAsync<TCommand>(Guid tenantId, Guid storeId,
        Guid passId, uint expectedVersion, Guid commandId, Guid operatorId, string reason, string action,
        Func<ServicePass, DateOnly, DateTimeOffset, ServicePassLedger> mutate, TCommand hashSource,
        CancellationToken cancellationToken) => await ChangePassAsync(tenantId, storeId, passId,
            expectedVersion, commandId, operatorId, reason, action,
            (pass, localDate, now) => Task.FromResult(mutate(pass, localDate, now)), hashSource,
            cancellationToken);

    private async Task<Result<ServicePassDto>> ChangePassAsync<TCommand>(Guid tenantId, Guid storeId,
        Guid passId, uint expectedVersion, Guid commandId, Guid operatorId, string reason, string action,
        Func<ServicePass, DateOnly, DateTimeOffset, Task<ServicePassLedger>> mutate, TCommand hashSource,
        CancellationToken cancellationToken)
    {
        if (commandId == Guid.Empty || string.IsNullOrWhiteSpace(reason))
            return ResultFactory.Failure<ServicePassDto>("VALIDATION_FAILED", "操作原因和幂等请求号必填");
        var hash = RequestHash(hashSource!);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var replay = await ReplayPassAsync(tenantId, storeId, commandId, hash, cancellationToken);
            if (replay is not null) return replay;
            var pass = await db.ServicePasses.SingleOrDefaultAsync(x => x.Id == passId &&
                x.TenantId == tenantId, cancellationToken);
            if (pass is null) return await PassFailure(transaction, "SERVICE_PASS_NOT_FOUND", "次卡不存在",
                cancellationToken);
            if (pass.Version != expectedVersion) return await PassFailure(transaction, "VERSION_CONFLICT",
                "次卡数据已变化，请刷新后重试", cancellationToken);
            var now = clock.GetUtcNow();
            var localDate = await LocalDateAsync(tenantId, storeId, now, cancellationToken)
                ?? throw new DomainRuleException("VALIDATION_FAILED", "门店时区配置无效");
            var cardActive = await db.MemberCards.AsNoTracking().AnyAsync(x => x.Id == pass.CardId &&
                x.TenantId == tenantId && x.Status == MemberCardStatus.Active &&
                x.ValidFrom <= localDate && (x.ValidTo == null || x.ValidTo >= localDate), cancellationToken);
            if (!cardActive)
                throw new DomainRuleException("MEMBER_CARD_NOT_ACTIVE", "会员卡尚未生效或已经到期");
            var previous = pass.Status.ToString();
            db.ServicePassLedgers.Add(await mutate(pass, localDate, now));
            AddReceipt(tenantId, commandId, operatorId, hash, pass.Id, now);
            AddAudit(tenantId, storeId, operatorId, action, "ServicePass", pass.Id, previous,
                pass.Status.ToString(), commandId, reason, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success((await ToPassDtosAsync([pass], cancellationToken)).Single());
        }
        catch (DomainRuleException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServicePassDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServicePassDto>("VERSION_CONFLICT", "次卡数据已变化，请刷新后重试");
        }
    }

    private async Task<Result<MemberPointSummaryDto>> ChangePointsAsync<TCommand>(Guid tenantId,
        Guid storeId, Guid cardId, Guid commandId, Guid operatorId, string reason, TCommand hashSource,
        Func<MemberAccount, DateOnly, DateTimeOffset, Task> mutate, string action,
        CancellationToken cancellationToken)
    {
        if (commandId == Guid.Empty || string.IsNullOrWhiteSpace(reason))
            return ResultFactory.Failure<MemberPointSummaryDto>("VALIDATION_FAILED", "操作原因和幂等请求号必填");
        var hash = RequestHash(hashSource!);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var replay = await ReplayPointsAsync(tenantId, commandId, hash, cancellationToken);
            if (replay is not null) return replay;
            var account = await db.MemberAccounts.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
                x.CardId == cardId && x.AccountType == MemberAccountType.Points &&
                x.Status == MemberAccountStatus.Active, cancellationToken);
            if (account is null) return await PointFailure(transaction, "MEMBER_ACCOUNT_NOT_FOUND",
                "会员积分账户不存在或当前不可用", cancellationToken);
            var card = await db.MemberCards.AsNoTracking().SingleOrDefaultAsync(x => x.Id == cardId &&
                x.TenantId == tenantId && x.Status == MemberCardStatus.Active,
                cancellationToken);
            if (card is null) return await PointFailure(transaction, "MEMBER_CARD_NOT_FOUND",
                "会员卡不存在或当前不可用", cancellationToken);
            var now = clock.GetUtcNow();
            var localDate = await LocalDateAsync(tenantId, storeId, now, cancellationToken)
                ?? throw new DomainRuleException("VALIDATION_FAILED", "门店时区配置无效");
            if (card.ValidFrom > localDate || card.ValidTo < localDate)
                throw new DomainRuleException("MEMBER_CARD_NOT_ACTIVE", "会员卡尚未生效或已经到期");
            var before = account.BalanceUnits;
            await mutate(account, localDate, now);
            AddReceipt(tenantId, commandId, operatorId, hash, cardId, now);
            AddAudit(tenantId, storeId, operatorId, action, "MemberAccount", account.Id,
                before.ToString(CultureInfo.InvariantCulture),
                account.BalanceUnits.ToString(CultureInfo.InvariantCulture), commandId, reason, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success((await ToPointDtosAsync([account], cancellationToken)).Single());
        }
        catch (DomainRuleException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            return ResultFactory.Failure<MemberPointSummaryDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            return ResultFactory.Failure<MemberPointSummaryDto>("VERSION_CONFLICT", "积分数据已变化，请刷新后重试");
        }
    }

    private async Task<IReadOnlyList<ServicePassDto>> ToPassDtosAsync(List<ServicePass> passes,
        CancellationToken cancellationToken)
    {
        if (passes.Count == 0) return [];
        var passIds = passes.Select(x => x.Id).ToList();
        var serviceIds = passes.Select(x => x.ServiceItemId).Distinct().ToList();
        var names = await db.ServiceItems.AsNoTracking().Where(x => serviceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var ledgers = new Dictionary<Guid, List<ServicePassLedger>>();
        foreach (var passId in passIds)
            ledgers[passId] = await db.ServicePassLedgers.AsNoTracking().Where(x => x.PassId == passId)
                .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).Take(100)
                .ToListAsync(cancellationToken);
        return passes.Select(pass => new ServicePassDto(pass.Id, pass.StoreId, pass.CustomerId,
            pass.CardId, pass.ServiceItemId, names.GetValueOrDefault(pass.ServiceItemId, "已停用项目"),
            pass.PassName, pass.PurchasedUses, pass.BonusUses, pass.RemainingPurchasedUses,
            pass.RemainingBonusUses, pass.RemainingUses, pass.ValidFrom, pass.ValidTo,
            pass.Status.ToString(), pass.Version, ledgers[pass.Id]
                .Select(ToLedgerDto).ToList())).ToList();
    }

    private async Task<IReadOnlyList<MemberPointSummaryDto>> ToPointDtosAsync(
        List<MemberAccount> accounts, CancellationToken cancellationToken)
    {
        if (accounts.Count == 0) return [];
        var ids = accounts.Select(x => x.Id).ToList();
        var grants = new Dictionary<Guid, List<MemberPointGrant>>();
        var ledgers = new Dictionary<Guid, List<MemberAccountLedger>>();
        foreach (var accountId in ids)
        {
            grants[accountId] = await db.MemberPointGrants.AsNoTracking().Where(x => x.AccountId == accountId)
                .OrderBy(x => x.Status != MemberPointGrantStatus.Active).ThenBy(x => x.ExpiresOn == null)
                .ThenBy(x => x.ExpiresOn).ThenByDescending(x => x.CreatedAtUtc).Take(200)
                .ToListAsync(cancellationToken);
            ledgers[accountId] = await db.MemberAccountLedgers.AsNoTracking().Where(x =>
                    x.AccountId == accountId && x.BusinessType.StartsWith("Point"))
                .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).Take(100)
                .ToListAsync(cancellationToken);
        }
        return accounts.Select(account => new MemberPointSummaryDto(account.CardId, account.Id,
            account.BalanceUnits, account.Version, grants[account.Id]
                .Select(x => new PointGrantDto(x.Id, x.OriginalUnits, x.RemainingUnits, x.ExpiresOn,
                    x.SourceType, x.Status.ToString())).ToList(), ledgers[account.Id]
                .Select(x => new PointLedgerDto(x.Id, x.BusinessType, x.BusinessId,
                    x.Direction.ToString(), x.Units, x.BalanceBefore, x.BalanceAfter,
                    x.OccurredAtUtc)).ToList())).ToList();
    }

    private static ServicePassLedgerDto ToLedgerDto(ServicePassLedger ledger) => new(ledger.Id, ledger.StoreId,
        ledger.Action.ToString(), ledger.PurchasedUsesDelta, ledger.BonusUsesDelta,
        ledger.PurchasedUsesAfter, ledger.BonusUsesAfter, ledger.ServiceOrderId,
        ledger.ReversedLedgerId, ledger.Reason, ledger.OccurredAtUtc);

    private async Task<MemberCard?> LoadCardAsync(Guid tenantId, Guid storeId, Guid customerId,
        Guid cardId, CancellationToken cancellationToken)
    {
        var customerIds = await CustomerIdsAsync(tenantId, customerId, cancellationToken);
        return await db.MemberCards.SingleOrDefaultAsync(x => x.Id == cardId && x.TenantId == tenantId &&
            customerIds.Contains(x.CustomerId) && x.Status == MemberCardStatus.Active,
            cancellationToken);
    }

    private async Task<List<Guid>> CustomerIdsAsync(Guid tenantId, Guid customerId,
        CancellationToken cancellationToken) => await db.Customers.AsNoTracking().Where(x =>
            x.TenantId == tenantId && (x.Id == customerId || x.MergedIntoCustomerId == customerId))
            .Select(x => x.Id).ToListAsync(cancellationToken);

    private async Task<DateOnly?> LocalDateAsync(Guid tenantId, Guid storeId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var zone = await db.Stores.AsNoTracking().Where(x => x.Id == storeId && x.TenantId == tenantId)
            .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
        return zone is null ? null : DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
    }

    private async Task<Result<ServicePassDto>?> ReplayPassAsync(Guid tenantId, Guid storeId,
        Guid commandId, byte[] hash, CancellationToken cancellationToken)
    {
        var receipt = await ReplayEntityIdAsync(tenantId, commandId, hash, cancellationToken);
        if (!receipt.Found) return null;
        if (receipt.Error is not null) return ResultFactory.Failure<ServicePassDto>(receipt.Error.Value.Code,
            receipt.Error.Value.Message);
        var pass = await db.ServicePasses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == receipt.EntityId &&
            x.TenantId == tenantId, cancellationToken);
        return pass is null ? ResultFactory.Failure<ServicePassDto>("SERVICE_PASS_NOT_FOUND", "次卡不存在")
            : ResultFactory.Success((await ToPassDtosAsync([pass], cancellationToken)).Single());
    }

    private async Task<Result<MemberPointSummaryDto>?> ReplayPointsAsync(Guid tenantId, Guid commandId,
        byte[] hash, CancellationToken cancellationToken)
    {
        var receipt = await ReplayEntityIdAsync(tenantId, commandId, hash, cancellationToken);
        if (!receipt.Found) return null;
        if (receipt.Error is not null) return ResultFactory.Failure<MemberPointSummaryDto>(
            receipt.Error.Value.Code, receipt.Error.Value.Message);
        var account = await db.MemberAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == tenantId && x.CardId == receipt.EntityId &&
            x.AccountType == MemberAccountType.Points, cancellationToken);
        return account is null ? ResultFactory.Failure<MemberPointSummaryDto>("MEMBER_ACCOUNT_NOT_FOUND",
            "积分账户不存在") : ResultFactory.Success((await ToPointDtosAsync([account], cancellationToken)).Single());
    }

    private async Task<ReplayReceipt> ReplayEntityIdAsync(Guid tenantId, Guid commandId, byte[] hash,
        CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
        if (existing is null) return new ReplayReceipt(false, Guid.Empty, null);
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, hash))
            return new ReplayReceipt(true, Guid.Empty, ("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用"));
        var body = existing.ResponseBody is null ? null : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        return body is null
            ? new ReplayReceipt(true, Guid.Empty, ("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新"))
            : new ReplayReceipt(true, body.EntityId, null);
    }

    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] hash, Guid entityId,
        DateTimeOffset now) => db.IdempotencyCommands.Add(new IdempotencyCommandRecord
        {
            CommandId = commandId, TenantId = tenantId, OperatorId = operatorId, RequestHash = hash,
            ResponseStatus = 200, ResponseBody = JsonSerializer.Serialize(new CommandReceipt(entityId)),
            CreatedAtUtc = now, CompletedAtUtc = now,
        });

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, string entityType,
        Guid entityId, string? previous, string? current, Guid commandId, string reason,
        DateTimeOffset now) => db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action,
            EntityType = entityType, EntityId = entityId, PreviousState = previous, CurrentState = current,
            Reason = reason.Trim(), RequestId = commandId,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = now,
        });

    private static byte[] RequestHash<T>(T value) => SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));

    private static async Task<Result<ServicePassDto>> PassFailure(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string code,
        string message, CancellationToken cancellationToken)
    {
        await RollbackAsync(transaction, cancellationToken);
        return ResultFactory.Failure<ServicePassDto>(code, message);
    }

    private static async Task<Result<MemberPointSummaryDto>> PointFailure(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string code,
        string message, CancellationToken cancellationToken)
    {
        await RollbackAsync(transaction, cancellationToken);
        return ResultFactory.Failure<MemberPointSummaryDto>(code, message);
    }

    private static async Task RollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        catch (InvalidOperationException) { }
    }

    private static bool IsConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && postgres.SqlState is
                PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected) return true;
        return exception is DbUpdateConcurrencyException;
    }

    private sealed record CommandReceipt(Guid EntityId);
    private sealed record ReplayReceipt(bool Found, Guid EntityId, (string Code, string Message)? Error);
}
