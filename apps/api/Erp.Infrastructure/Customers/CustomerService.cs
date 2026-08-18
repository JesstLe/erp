using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Common;
using Erp.Application.Customers;
using Erp.Domain.Common;
using Erp.Domain.Cashier;
using Erp.Domain.Customers;
using Erp.Domain.Facilities;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Erp.Infrastructure.Customers;

internal sealed class CustomerService(ErpDbContext db, CustomerPrivacyService privacy, TimeProvider clock,
    IHttpContextAccessor httpContextAccessor) : ICustomerService
{
    public async Task<PageResult<CustomerSummaryDto>> SearchAsync(Guid tenantId, Guid storeId, string? query,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var customers = BuildCustomerQuery(tenantId, storeId, query);
        if (customers is null) return new PageResult<CustomerSummaryDto>([], 0, page, pageSize);

        var total = await customers.CountAsync(cancellationToken);
        var rows = await customers.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { Customer = x, ActiveCards = db.MemberCards.Count(card => card.CustomerId == x.Id && card.Status == MemberCardStatus.Active) })
            .ToListAsync(cancellationToken);
        var items = rows.Select(x => new CustomerSummaryDto(x.Customer.Id, x.Customer.Name,
            privacy.MaskProtectedMobile(x.Customer.MobileCiphertext), x.Customer.Status.ToString(), x.Customer.HomeStoreId,
            x.ActiveCards, x.Customer.CreatedAtUtc)).ToList();
        return new PageResult<CustomerSummaryDto>(items, total, page, pageSize);
    }

    public async Task<Result<CustomerDetailDto>> GetAsync(Guid tenantId, Guid storeId, Guid customerId,
        bool includeFinancialDetails, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId &&
            x.TenantId == tenantId && x.HomeStoreId == storeId, cancellationToken);
        if (customer?.Status == CustomerStatus.Merged && customer.MergedIntoCustomerId is Guid targetId)
            customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == targetId &&
                x.TenantId == tenantId && x.HomeStoreId == storeId, cancellationToken);
        return customer is null
            ? ResultFactory.Failure<CustomerDetailDto>("CUSTOMER_NOT_FOUND", "顾客不存在")
            : ResultFactory.Success(await ToDetailAsync(customer, includeFinancialDetails, cancellationToken));
    }

    public async Task<Result<CustomerMobileRevealDto>> RevealMobileAsync(Guid tenantId,
        RevealCustomerMobileCommand command, CancellationToken cancellationToken)
    {
        var purpose = NormalizePurpose(command.Purpose);
        if (command.CommandId == Guid.Empty || purpose is null)
            return ResultFactory.Failure<CustomerMobileRevealDto>("VALIDATION_FAILED", "查看完整手机号必须填写2到200字业务目的");
        var requestHash = RequestHash($"CUSTOMER_MOBILE_REVEAL|{command.StoreId}|{command.CustomerId}|{purpose}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,
            cancellationToken);
        var replay = await ReplayAsync<CustomerMobileRevealDto>(tenantId, command.CommandId, requestHash,
            _ => LoadMobileAsync(tenantId, command.StoreId, command.CustomerId, cancellationToken),
            cancellationToken);
        if (replay is not null) return replay;

        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.CustomerId &&
            x.TenantId == tenantId && x.HomeStoreId == command.StoreId, cancellationToken);
        if (customer is null)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerMobileRevealDto>("CUSTOMER_NOT_FOUND", "顾客不存在");
        }

        var now = clock.GetUtcNow();
        AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, customer.Id, now);
        AddAudit(tenantId, command.StoreId, command.OperatorId, "customer.mobile.reveal", "Customer", customer.Id,
            null, "Revealed", command.CommandId, now, reason: purpose,
            metadata: JsonSerializer.Serialize(new { field = "mobile" }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ResultFactory.Success(new CustomerMobileRevealDto(customer.Id,
            privacy.RevealProtectedMobile(customer.MobileCiphertext), now));
    }

    public async Task<Result<CustomerExportDto>> ExportAsync(Guid tenantId, ExportCustomersCommand command,
        CancellationToken cancellationToken)
    {
        var purpose = NormalizePurpose(command.Purpose);
        if (command.CommandId == Guid.Empty || purpose is null)
            return ResultFactory.Failure<CustomerExportDto>("VALIDATION_FAILED", "导出顾客名单必须填写2到200字业务目的");
        if (command.IncludeFullMobile && !command.CanExportFullMobile)
            return ResultFactory.Failure<CustomerExportDto>("SENSITIVE_EXPORT_FORBIDDEN", "只有最高权限可以导出完整手机号");
        var customers = BuildCustomerQuery(tenantId, command.StoreId, command.Query);
        if (customers is null)
            return ResultFactory.Failure<CustomerExportDto>("VALIDATION_FAILED", "查询条件不能超过100字");

        var rows = await customers.OrderBy(x => x.Name).ThenBy(x => x.CreatedAtUtc).Take(5_001)
            .Select(x => new
            {
                Customer = x,
                ActiveCards = db.MemberCards.Count(card => card.CustomerId == x.Id &&
                    card.Status == MemberCardStatus.Active),
            }).ToListAsync(cancellationToken);
        if (rows.Count > 5_000)
            return ResultFactory.Failure<CustomerExportDto>("EXPORT_TOO_LARGE", "单次最多导出5000位顾客，请先缩小查询范围");

        var exportRows = rows.Select(x => new CustomerExportRow(x.Customer.Name,
            command.IncludeFullMobile
                ? privacy.RevealProtectedMobile(x.Customer.MobileCiphertext)
                : privacy.MaskProtectedMobile(x.Customer.MobileCiphertext),
            x.Customer.Status.ToString(), x.ActiveCards, x.Customer.CreatedAtUtc)).ToList();
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,
            cancellationToken);
        AddAudit(tenantId, command.StoreId, command.OperatorId, "customer.export", "CustomerExport",
            command.StoreId, null, "Exported", command.CommandId, now, reason: purpose,
            metadata: JsonSerializer.Serialize(new
            {
                includesFullMobile = command.IncludeFullMobile,
                rowCount = exportRows.Count,
                queryApplied = !string.IsNullOrWhiteSpace(command.Query),
            }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var suffix = command.IncludeFullMobile ? "full-mobile" : "masked-mobile";
        return ResultFactory.Success(new CustomerExportDto(CustomerExportFormatter.ToCsv(exportRows),
            $"customers-{suffix}-{now:yyyyMMdd-HHmmss}.csv", exportRows.Count, command.IncludeFullMobile));
    }

    public async Task<Result<CustomerDetailDto>> CreateAsync(Guid tenantId, CreateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "缺少幂等请求号");
        ProtectedMobile mobile;
        CustomerGender gender;
        try
        {
            mobile = privacy.Protect(command.Mobile);
            if (string.IsNullOrWhiteSpace(command.Gender)) gender = CustomerGender.Unknown;
            else if (!Enum.TryParse<CustomerGender>(command.Gender, true, out gender) || !Enum.IsDefined(gender))
                throw new ArgumentException("性别值无效");
        }
        catch (ArgumentException exception) { return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", exception.Message); }

        var requestHash = RequestHash($"CUSTOMER_CREATE|{command.StoreId}|{command.Name}|{Convert.ToHexString(mobile.LookupHash)}|{gender}|{command.BirthDate}|{command.SourceCode}|{command.ServiceNotificationConsent}|{command.MarketingConsent}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync<CustomerDetailDto>(tenantId, command.CommandId, requestHash,
            id => GetAsync(tenantId, command.StoreId, id, includeFinancialDetails: false, cancellationToken),
            cancellationToken);
        if (replay is not null) return replay;

        try
        {
            if (await db.Customers.AnyAsync(x => x.TenantId == tenantId &&
                x.MobileLookupHash == mobile.LookupHash, cancellationToken))
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("DUPLICATE_CUSTOMER", "该手机号已存在顾客档案，请先查询并核对");
            }

            var now = clock.GetUtcNow();
            var timeZoneId = await db.Stores.Where(x => x.Id == command.StoreId && x.TenantId == tenantId)
                .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
            if (timeZoneId is null)
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "门店时区配置无效");
            }
            var customer = new Customer(tenantId, command.StoreId, command.Name, mobile.Ciphertext, mobile.LookupHash,
                mobile.LastFour, gender, command.BirthDate, command.SourceCode, command.ServiceNotificationConsent,
                command.MarketingConsent, StoreDate(now, timeZoneId));
            db.Customers.Add(customer);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, customer.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "customer.create", "Customer", customer.Id,
                null, customer.Status.ToString(), command.CommandId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await ToDetailAsync(customer, includeFinancialDetails: false,
                cancellationToken));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("DUPLICATE_CUSTOMER", "顾客档案已被其他终端创建，请重新查询");
        }
    }

    public async Task<Result<CustomerDetailDto>> UpdateAsync(Guid tenantId, UpdateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "缺少幂等请求号");
        ProtectedMobile mobile;
        CustomerGender gender;
        try
        {
            mobile = privacy.Protect(command.Mobile);
            if (string.IsNullOrWhiteSpace(command.Gender)) gender = CustomerGender.Unknown;
            else if (!Enum.TryParse<CustomerGender>(command.Gender, true, out gender) || !Enum.IsDefined(gender))
                throw new ArgumentException("性别值无效");
        }
        catch (ArgumentException exception)
        {
            return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", exception.Message);
        }

        var requestHash = RequestHash($"CUSTOMER_UPDATE|{command.StoreId}|{command.CustomerId}|{command.Name}|" +
            $"{Convert.ToHexString(mobile.LookupHash)}|{gender}|{command.BirthDate}|{command.SourceCode}|" +
            $"{command.ServiceNotificationConsent}|{command.MarketingConsent}|{command.ExpectedVersion}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayAsync<CustomerDetailDto>(tenantId, command.CommandId, requestHash,
            id => GetAsync(tenantId, command.StoreId, id, includeFinancialDetails: true, cancellationToken),
            cancellationToken);
        if (replay is not null) return replay;

        try
        {
            var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == command.CustomerId &&
                x.TenantId == tenantId && x.HomeStoreId == command.StoreId, cancellationToken);
            if (customer is null)
                return await RollbackFailure<CustomerDetailDto>(transaction, "CUSTOMER_NOT_FOUND", "顾客不存在",
                    cancellationToken);
            if (customer.Version != command.ExpectedVersion)
                return await RollbackFailure<CustomerDetailDto>(transaction, "VERSION_CONFLICT",
                    "顾客资料已被其他终端修改，请刷新后重试", cancellationToken);
            if (await db.Customers.AnyAsync(x => x.TenantId == tenantId && x.Id != customer.Id &&
                x.MobileLookupHash == mobile.LookupHash && (x.Status != CustomerStatus.Merged ||
                    x.MergedIntoCustomerId != customer.Id), cancellationToken))
                return await RollbackFailure<CustomerDetailDto>(transaction, "DUPLICATE_CUSTOMER",
                    "该手机号已属于其他顾客档案", cancellationToken);
            var timeZoneId = await db.Stores.Where(x => x.Id == command.StoreId && x.TenantId == tenantId)
                .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
            if (timeZoneId is null)
                return await RollbackFailure<CustomerDetailDto>(transaction, "VALIDATION_FAILED", "门店时区配置无效",
                    cancellationToken);
            var before = new
            {
                customer.Name, customer.Gender, customer.BirthDate, customer.SourceCode,
                customer.ServiceNotificationConsent, customer.MarketingConsent,
            };
            customer.UpdateProfile(command.Name, mobile.Ciphertext, mobile.LookupHash, mobile.LastFour, gender,
                command.BirthDate, command.SourceCode, command.ServiceNotificationConsent,
                command.MarketingConsent, StoreDate(clock.GetUtcNow(), timeZoneId));
            var now = clock.GetUtcNow();
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, customer.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "customer.update", "Customer", customer.Id,
                customer.Status.ToString(), customer.Status.ToString(), command.CommandId, now,
                metadata: JsonSerializer.Serialize(new { before, after = new
                {
                    customer.Name, customer.Gender, customer.BirthDate, customer.SourceCode,
                    customer.ServiceNotificationConsent, customer.MarketingConsent,
                } }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await ToDetailAsync(customer, includeFinancialDetails: true,
                cancellationToken));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("VERSION_CONFLICT", "顾客资料已变化，请刷新后重试");
        }
    }

    public async Task<Result<CustomerDetailDto>> ChangeStatusAsync(Guid tenantId,
        ChangeCustomerStatusCommand command, CancellationToken cancellationToken)
    {
        var reason = NormalizePurpose(command.Reason);
        if (command.CommandId == Guid.Empty || reason is null)
            return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "停用或恢复必须填写2到200字原因");
        var action = command.Restore ? "RESTORE" : "DISABLE";
        var requestHash = RequestHash($"CUSTOMER_{action}|{command.StoreId}|{command.CustomerId}|" +
            $"{command.ExpectedVersion}|{reason}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayAsync<CustomerDetailDto>(tenantId, command.CommandId, requestHash,
            id => GetAsync(tenantId, command.StoreId, id, includeFinancialDetails: true, cancellationToken),
            cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == command.CustomerId &&
                x.TenantId == tenantId && x.HomeStoreId == command.StoreId, cancellationToken);
            if (customer is null)
                return await RollbackFailure<CustomerDetailDto>(transaction, "CUSTOMER_NOT_FOUND", "顾客不存在",
                    cancellationToken);
            if (customer.Version != command.ExpectedVersion)
                return await RollbackFailure<CustomerDetailDto>(transaction, "VERSION_CONFLICT",
                    "顾客状态已被其他终端修改，请刷新后重试", cancellationToken);
            var before = customer.Status.ToString();
            if (command.Restore) customer.Restore(); else customer.Disable();
            var now = clock.GetUtcNow();
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, customer.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId,
                command.Restore ? "customer.restore" : "customer.disable", "Customer", customer.Id,
                before, customer.Status.ToString(), command.CommandId, now, reason: reason);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await ToDetailAsync(customer, includeFinancialDetails: true,
                cancellationToken));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("VERSION_CONFLICT", "顾客状态已变化，请刷新后重试");
        }
    }

    public async Task<Result<CustomerMergePreviewDto>> PreviewMergeAsync(Guid tenantId,
        PreviewCustomerMergeCommand command, CancellationToken cancellationToken)
    {
        if (command.SourceCustomerId == Guid.Empty || command.TargetCustomerId == Guid.Empty ||
            command.SourceCustomerId == command.TargetCustomerId)
            return ResultFactory.Failure<CustomerMergePreviewDto>("VALIDATION_FAILED", "请选择两个不同的顾客档案");
        var customers = await db.Customers.AsNoTracking().Where(x => x.TenantId == tenantId &&
                (x.Id == command.SourceCustomerId || x.Id == command.TargetCustomerId))
            .ToListAsync(cancellationToken);
        var source = customers.SingleOrDefault(x => x.Id == command.SourceCustomerId);
        var target = customers.SingleOrDefault(x => x.Id == command.TargetCustomerId);
        if (source is null || target is null)
            return ResultFactory.Failure<CustomerMergePreviewDto>("CUSTOMER_NOT_FOUND", "源档案或保留档案不存在");
        if (source.HomeStoreId != command.StoreId || target.HomeStoreId != command.StoreId)
            return ResultFactory.Failure<CustomerMergePreviewDto>("FORBIDDEN_DATA_SCOPE",
                "只能预览当前授权门店内的顾客合并");
        return ResultFactory.Success(await BuildMergePreviewAsync(source, target, command.StoreId,
            cancellationToken));
    }

    public async Task<Result<CustomerDetailDto>> MergeAsync(Guid tenantId, MergeCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var reason = NormalizePurpose(command.Reason);
        if (command.CommandId == Guid.Empty || reason is null || command.SourceCustomerId == Guid.Empty ||
            command.TargetCustomerId == Guid.Empty || command.SourceCustomerId == command.TargetCustomerId)
            return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED",
                "请选择两个不同顾客并填写2到200字合并原因");
        var requestHash = RequestHash($"CUSTOMER_MERGE|{command.StoreId}|{command.SourceCustomerId}|" +
            $"{command.TargetCustomerId}|{command.ExpectedSourceVersion}|{command.ExpectedTargetVersion}|{reason}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayAsync<CustomerDetailDto>(tenantId, command.CommandId, requestHash,
            _ => GetAsync(tenantId, command.StoreId, command.TargetCustomerId, includeFinancialDetails: true,
                cancellationToken), cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var customers = await db.Customers.Where(x => x.TenantId == tenantId &&
                    (x.Id == command.SourceCustomerId || x.Id == command.TargetCustomerId))
                .ToListAsync(cancellationToken);
            var source = customers.SingleOrDefault(x => x.Id == command.SourceCustomerId);
            var target = customers.SingleOrDefault(x => x.Id == command.TargetCustomerId);
            if (source is null || target is null)
                return await RollbackFailure<CustomerDetailDto>(transaction, "CUSTOMER_NOT_FOUND",
                    "源档案或保留档案不存在", cancellationToken);
            if (source.HomeStoreId != command.StoreId || target.HomeStoreId != command.StoreId)
                return await RollbackFailure<CustomerDetailDto>(transaction, "FORBIDDEN_DATA_SCOPE",
                    "只能合并当前授权门店内的顾客档案", cancellationToken);
            if (source.Version != command.ExpectedSourceVersion || target.Version != command.ExpectedTargetVersion)
                return await RollbackFailure<CustomerDetailDto>(transaction, "VERSION_CONFLICT",
                    "顾客档案已被其他终端修改，请重新预览", cancellationToken);
            var preview = await BuildMergePreviewAsync(source, target, command.StoreId, cancellationToken);
            if (!preview.CanMerge)
                return await RollbackFailure<CustomerDetailDto>(transaction, "CUSTOMER_MERGE_BLOCKED",
                    string.Join('；', preview.Blockers), cancellationToken);
            var now = clock.GetUtcNow();
            source.MergeInto(target.Id, command.OperatorId, reason, now);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, target.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "customer.merge", "Customer", source.Id,
                "SelectedAsSource", "Merged", command.CommandId, now, reason: reason,
                metadata: JsonSerializer.Serialize(new
                {
                    sourceCustomerId = source.Id,
                    targetCustomerId = target.Id,
                    preview.SourceCardCount,
                    preview.SourcePrincipalBalanceMinor,
                    preview.SourceBonusBalanceMinor,
                    preview.SourcePointsBalance,
                    preview.SourceOrderCount,
                    preview.SourceServiceRecordCount,
                    strategy = "LogicalLineagePreservesHistoricalForeignKeys",
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(tenantId, command.StoreId, target.Id, includeFinancialDetails: true,
                cancellationToken);
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("VERSION_CONFLICT", "顾客合并发生并发冲突，请重新预览");
        }
    }

    private async Task<CustomerMergePreviewDto> BuildMergePreviewAsync(Customer source, Customer target,
        Guid storeId, CancellationToken cancellationToken)
    {
        var blockers = new List<string>();
        if (source.Status == CustomerStatus.Merged) blockers.Add("源档案已经合并");
        if (target.Status != CustomerStatus.Active) blockers.Add("保留档案必须为正常状态");
        if (source.HomeStoreId != storeId || target.HomeStoreId != storeId)
            blockers.Add("当前只允许合并同一归属门店的顾客档案");
        if (await db.Customers.AsNoTracking().AnyAsync(x => x.TenantId == source.TenantId &&
            x.MergedIntoCustomerId == source.Id, cancellationToken))
            blockers.Add("源档案已经承接其他合并档案，不能再次作为源档案");
        if (await db.Visits.AsNoTracking().AnyAsync(x => x.TenantId == source.TenantId &&
            x.CustomerId == source.Id && (x.Status == VisitStatus.Arrived || x.Status == VisitStatus.InService ||
                x.Status == VisitStatus.ServiceEnded), cancellationToken))
            blockers.Add("源档案仍有关联的进行中或待录单接待");
        if (await db.ServiceOrders.AsNoTracking().AnyAsync(x => x.TenantId == source.TenantId &&
            x.CustomerId == source.Id && (x.Status == ServiceOrderStatus.Draft ||
                x.Status == ServiceOrderStatus.PendingPayment || x.Status == ServiceOrderStatus.PaymentProcessing),
            cancellationToken))
            blockers.Add("源档案仍有未完成消费单");
        if (await db.MemberVerificationChallenges.AsNoTracking().AnyAsync(x => x.TenantId == source.TenantId &&
            x.CustomerId == source.Id && (x.Status == MemberVerificationStatus.Active ||
                x.Status == MemberVerificationStatus.Verified), cancellationToken))
            blockers.Add("源档案仍有有效的会员验证码挑战");
        var accounts = await db.MemberAccounts.AsNoTracking().Where(x => x.TenantId == source.TenantId &&
                x.CustomerId == source.Id).ToListAsync(cancellationToken);
        var cards = await db.MemberCards.AsNoTracking().CountAsync(x => x.TenantId == source.TenantId &&
            x.CustomerId == source.Id, cancellationToken);
        var orders = await db.ServiceOrders.AsNoTracking().CountAsync(x => x.TenantId == source.TenantId &&
            x.CustomerId == source.Id, cancellationToken);
        var records = await db.ServiceRecords.AsNoTracking().CountAsync(x => x.TenantId == source.TenantId &&
            x.CustomerId == source.Id, cancellationToken);
        return new CustomerMergePreviewDto(source.Id, source.Name,
            privacy.MaskProtectedMobile(source.MobileCiphertext), source.Version, target.Id, target.Name,
            privacy.MaskProtectedMobile(target.MobileCiphertext), target.Version, cards,
            accounts.Where(x => x.AccountType == MemberAccountType.Principal).Sum(x => x.BalanceUnits),
            accounts.Where(x => x.AccountType == MemberAccountType.Bonus).Sum(x => x.BalanceUnits),
            accounts.Where(x => x.AccountType == MemberAccountType.Points).Sum(x => x.BalanceUnits),
            orders, records, blockers, blockers.Count == 0);
    }

    public async Task<IReadOnlyList<MemberCardTypeDto>> ListCardTypesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.MemberCardTypes.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == MemberCardTypeStatus.Published)
            .OrderBy(x => x.Name).Select(x => new MemberCardTypeDto(x.Id, x.Code, x.Name, x.ValidityDays, x.Status.ToString()))
            .ToListAsync(cancellationToken);

    public async Task<Result<MemberCardTypeDto>> CreateCardTypeAsync(Guid tenantId, CreateMemberCardTypeCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<MemberCardTypeDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var requestHash = RequestHash($"CARD_TYPE_CREATE|{command.Code}|{command.Name}|{command.ValidityDays}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync<MemberCardTypeDto>(tenantId, command.CommandId, requestHash, async id =>
        {
            var item = await db.MemberCardTypes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, cancellationToken);
            return item is null ? ResultFactory.Failure<MemberCardTypeDto>("MEMBER_CARD_TYPE_NOT_FOUND", "卡类不存在")
                : ResultFactory.Success(new MemberCardTypeDto(item.Id, item.Code, item.Name, item.ValidityDays, item.Status.ToString()));
        }, cancellationToken);
        if (replay is not null) return replay;

        try
        {
            var now = clock.GetUtcNow();
            var cardType = new MemberCardType(tenantId, command.Code, command.Name, command.ValidityDays);
            db.MemberCardTypes.Add(cardType);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, cardType.Id, now);
            AddAudit(tenantId, null, command.OperatorId, "membership.card_type.create", "MemberCardType", cardType.Id,
                null, cardType.Status.ToString(), command.CommandId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(new MemberCardTypeDto(cardType.Id, cardType.Code, cardType.Name, cardType.ValidityDays, cardType.Status.ToString()));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<MemberCardTypeDto>("VALIDATION_FAILED", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<MemberCardTypeDto>("DUPLICATE_MEMBER_CARD_TYPE", "卡类编号已经存在");
        }
    }

    public async Task<Result<CustomerDetailDto>> OpenMembershipAsync(Guid tenantId, OpenMembershipCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var normalizedCardNo = string.IsNullOrWhiteSpace(command.CardNo) ? CreateCardNo(command.CommandId) : command.CardNo.Trim().ToUpperInvariant();
        var requestHash = RequestHash($"MEMBERSHIP_OPEN|{command.StoreId}|{command.CustomerId}|{command.CardTypeId}|{normalizedCardNo}|{command.Note}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync<CustomerDetailDto>(tenantId, command.CommandId, requestHash,
            _ => GetAsync(tenantId, command.StoreId, command.CustomerId, includeFinancialDetails: true,
                cancellationToken), cancellationToken);
        if (replay is not null) return replay;

        try
        {
            var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == command.CustomerId && x.TenantId == tenantId &&
                x.HomeStoreId == command.StoreId && x.Status == CustomerStatus.Active, cancellationToken);
            if (customer is null)
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("CUSTOMER_NOT_FOUND", "顾客不存在或当前不可开卡");
            }
            var cardType = await db.MemberCardTypes.SingleOrDefaultAsync(x => x.Id == command.CardTypeId && x.TenantId == tenantId &&
                x.Status == MemberCardTypeStatus.Published, cancellationToken);
            if (cardType is null)
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("MEMBER_CARD_TYPE_NOT_FOUND", "卡类不存在或未发布");
            }

            var timeZoneId = await db.Stores.Where(x => x.Id == command.StoreId && x.TenantId == tenantId)
                .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
            if (timeZoneId is null)
            {
                await RollbackIfActiveAsync(transaction, cancellationToken);
                return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", "门店时区配置无效");
            }

            var now = clock.GetUtcNow();
            var validFrom = StoreDate(now, timeZoneId);
            DateOnly? validTo = cardType.ValidityDays is null ? null : validFrom.AddDays(cardType.ValidityDays.Value);
            var card = new MemberCard(tenantId, customer.Id, cardType.Id, command.StoreId, normalizedCardNo,
                validFrom, validTo, command.Note);
            db.MemberCards.Add(card);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, card.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "membership.card.open", "MemberCard", card.Id,
                null, card.Status.ToString(), command.CommandId, now);
            // These aggregates carry identifiers rather than EF navigation properties. Persist the card first inside
            // the same transaction so PostgreSQL can enforce the account foreign key deterministically.
            await db.SaveChangesAsync(cancellationToken);
            db.MemberAccounts.AddRange(Enum.GetValues<MemberAccountType>().Select(type =>
                new MemberAccount(tenantId, customer.Id, card.Id, type)));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(tenantId, command.StoreId, customer.Id, includeFinancialDetails: true,
                cancellationToken);
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("VALIDATION_FAILED", exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("DUPLICATE_MEMBER_CARD", "会员卡号已经存在");
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<CustomerDetailDto>("VERSION_CONFLICT", "会员状态已变化，请刷新后重试");
        }
    }

    private async Task<CustomerDetailDto> ToDetailAsync(Customer customer, bool includeFinancialDetails,
        CancellationToken cancellationToken)
    {
        var customerIds = await db.Customers.AsNoTracking().Where(x => x.TenantId == customer.TenantId &&
                (x.Id == customer.Id || x.MergedIntoCustomerId == customer.Id))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var aliases = await db.Customers.AsNoTracking().Where(x => x.TenantId == customer.TenantId &&
                x.MergedIntoCustomerId == customer.Id)
            .OrderByDescending(x => x.MergedAtUtc).Select(x => new
            {
                x.Id, x.Name, x.MobileCiphertext, x.MergedAtUtc,
            }).ToListAsync(cancellationToken);
        var cards = await db.MemberCards.AsNoTracking().Where(x => customerIds.Contains(x.CustomerId))
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var cardTypeNames = await db.MemberCardTypes.AsNoTracking().Where(x => x.TenantId == customer.TenantId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var cardIds = cards.Select(x => x.Id).ToList();
        var accounts = includeFinancialDetails
            ? await db.MemberAccounts.AsNoTracking().Where(x => cardIds.Contains(x.CardId))
                .OrderBy(x => x.AccountType).ToListAsync(cancellationToken)
            : [];
        var cardDtos = cards.Select(card => new MemberCardDto(card.Id, cardTypeNames.GetValueOrDefault(card.CardTypeId, "未知卡类"),
            CustomerPrivacyService.MaskCardNo(card.CardNo), card.Status.ToString(), card.ValidFrom, card.ValidTo,
            accounts.Where(x => x.CardId == card.Id).OrderBy(x => AccountOrder(x.AccountType)).Select(x => new MemberAccountDto(x.Id, x.AccountType.ToString(),
                x.BalanceUnits, x.Status.ToString())).ToList())).ToList();
        return new CustomerDetailDto(customer.Id, customer.Name,
            privacy.MaskProtectedMobile(customer.MobileCiphertext), customer.Gender.ToString(), customer.BirthDate,
            customer.SourceCode,
            customer.ServiceNotificationConsent, customer.MarketingConsent, customer.Status.ToString(),
            customer.HomeStoreId, customer.Version, cardDtos, aliases.Select(x => new MergedCustomerAliasDto(
                x.Id, x.Name, privacy.MaskProtectedMobile(x.MobileCiphertext), x.MergedAtUtc)).ToList());
    }

    private async Task<Result<CustomerMobileRevealDto>> LoadMobileAsync(Guid tenantId, Guid storeId,
        Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId &&
            x.TenantId == tenantId && x.HomeStoreId == storeId, cancellationToken);
        return customer is null
            ? ResultFactory.Failure<CustomerMobileRevealDto>("CUSTOMER_NOT_FOUND", "顾客不存在")
            : ResultFactory.Success(new CustomerMobileRevealDto(customer.Id,
                privacy.RevealProtectedMobile(customer.MobileCiphertext), clock.GetUtcNow()));
    }

    private IQueryable<Customer>? BuildCustomerQuery(Guid tenantId, Guid storeId, string? query)
    {
        var customers = db.Customers.AsNoTracking().Where(x =>
            x.TenantId == tenantId && x.HomeStoreId == storeId && x.Status != CustomerStatus.Merged);
        var term = query?.Trim();
        if (term?.Length > 100) return null;
        if (string.IsNullOrEmpty(term)) return customers;

        var digits = new string(term.Where(char.IsDigit).ToArray());
        if (digits.Length == 11)
        {
            try
            {
                var hash = privacy.Hash(digits);
                return customers.Where(x => x.MobileLookupHash == hash || db.Customers.Any(alias =>
                    alias.TenantId == tenantId && alias.MergedIntoCustomerId == x.Id &&
                    alias.MobileLookupHash == hash));
            }
            catch (ArgumentException) { return null; }
        }
        if (digits.Length == 4 && term.All(char.IsDigit))
            return customers.Where(x => x.MobileLastFour == digits || db.Customers.Any(alias =>
                alias.TenantId == tenantId && alias.MergedIntoCustomerId == x.Id &&
                alias.MobileLastFour == digits));
        var upper = term.ToUpperInvariant();
        return customers.Where(x => x.Name.Contains(term) || db.Customers.Any(alias =>
                alias.TenantId == tenantId && alias.MergedIntoCustomerId == x.Id && alias.Name.Contains(term)) ||
            db.MemberCards.Any(card => (card.CustomerId == x.Id || db.Customers.Any(alias =>
                alias.Id == card.CustomerId && alias.MergedIntoCustomerId == x.Id)) && card.CardNo == upper));
    }

    private static string? NormalizePurpose(string? value)
    {
        var purpose = value?.Trim();
        return purpose?.Length is >= 2 and <= 200 ? purpose : null;
    }

    private static async Task<Result<T>> RollbackFailure<T>(IDbContextTransaction transaction, string code,
        string message, CancellationToken cancellationToken)
    {
        if (transaction.GetDbTransaction().Connection is not null)
            await transaction.RollbackAsync(cancellationToken);
        return ResultFactory.Failure<T>(code, message);
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

    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] requestHash, Guid entityId, DateTimeOffset now) =>
        db.IdempotencyCommands.Add(new IdempotencyCommandRecord
        {
            CommandId = commandId, TenantId = tenantId, OperatorId = operatorId, RequestHash = requestHash,
            ResponseStatus = 200, ResponseBody = JsonSerializer.Serialize(new CommandReceipt(entityId)),
            CreatedAtUtc = now, CompletedAtUtc = now,
        });

    private void AddAudit(Guid tenantId, Guid? storeId, Guid operatorId, string action, string entityType, Guid entityId,
        string? previous, string? current, Guid commandId, DateTimeOffset now, string? reason = null,
        string? metadata = null) => db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action, EntityType = entityType,
            EntityId = entityId, PreviousState = previous, CurrentState = current, RequestId = commandId,
            Reason = reason, Metadata = metadata ?? "{}",
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = now,
        });

    private static byte[] RequestHash(string identity) => SHA256.HashData(Encoding.UTF8.GetBytes(identity));
    private static async Task RollbackIfActiveAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        catch (InvalidOperationException) { /* PostgreSQL can complete a failed serializable transaction at commit. */ }
    }
    private static DateOnly StoreDate(DateTimeOffset now, string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);
    private static int AccountOrder(MemberAccountType type) => type switch
    {
        MemberAccountType.Principal => 0,
        MemberAccountType.Bonus => 1,
        MemberAccountType.Points => 2,
        _ => 99,
    };
    private static bool IsUniqueViolation(Exception exception) => FindPostgres(exception)?.SqlState == PostgresErrorCodes.UniqueViolation;
    private static bool IsDatabaseConcurrencyConflict(Exception exception) => FindPostgres(exception)?.SqlState is
        PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;
    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres) return postgres;
        return null;
    }
    private static string CreateCardNo(Guid commandId) => $"M{commandId:N}"[..24].ToUpperInvariant();
    private sealed record CommandReceipt(Guid EntityId);
}
