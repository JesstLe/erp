using System.Text.Json;
using Erp.Application.Common;
using Erp.Application.Platform;
using Erp.Application.Security;
using Erp.Domain.Authorization;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Domain.Organization;
using Erp.Domain.Platform;
using Erp.Infrastructure.Identity;
using Erp.Infrastructure.Organization;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Platform;

internal sealed partial class PlatformAdminService(
    ErpDbContext db,
    UserManager<ApplicationUser> userManager,
    PlatformRegistrationPrivacyService registrationPrivacy,
    LoginSecurityEventWriter securityEvents,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    BusinessCodeGenerator codeGenerator) : IPlatformAdminService
{
    private static readonly string[] MerchantRoleNames =
    [
        SystemRoles.Owner, SystemRoles.StoreManager, SystemRoles.FrontDesk, SystemRoles.Cashier,
        SystemRoles.Technician,
    ];

    public async Task<MerchantRegistrationPageDto> ListRegistrationsAsync(string? status, string? query,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var registrations = db.MerchantRegistrationApplications.AsNoTracking();
        if (Enum.TryParse<MerchantRegistrationStatus>(status, true, out var parsedStatus))
            registrations = registrations.Where(x => x.Status == parsedStatus);
        var normalizedQuery = Normalize(query, 100);
        if (normalizedQuery is not null)
            registrations = registrations.Where(x => x.MerchantName.Contains(normalizedQuery) ||
                x.StoreName.Contains(normalizedQuery) || x.ApplicationNo.Contains(normalizedQuery) ||
                x.DesiredOwnerAccount.Contains(normalizedQuery));
        var total = await registrations.CountAsync(cancellationToken);
        var rows = await registrations.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new MerchantRegistrationPageDto(rows.Select(ToRegistrationDto).ToList(), total, page, pageSize);
    }

    public async Task<Result<MerchantRegistrationApplicationDto>> ApproveAsync(Guid platformUserId,
        ApproveMerchantRegistrationCommand command, CancellationToken cancellationToken)
    {
        if (!PlatformIdentityService.ValidPassword(command.InitialPassword))
            return ResultFactory.Failure<MerchantRegistrationApplicationDto>("VALIDATION_FAILED",
                PasswordPolicy.RequirementText);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var application = await db.MerchantRegistrationApplications.SingleOrDefaultAsync(
                x => x.Id == command.ApplicationId, cancellationToken);
            if (application is null)
                return ResultFactory.Failure<MerchantRegistrationApplicationDto>("REGISTRATION_NOT_FOUND",
                    "注册申请不存在");
            if (application.Version != command.ExpectedVersion)
                return ResultFactory.Failure<MerchantRegistrationApplicationDto>("VERSION_CONFLICT",
                    "注册申请已变化，请刷新后重试");
            if (application.Status != MerchantRegistrationStatus.PendingReview)
                return ResultFactory.Failure<MerchantRegistrationApplicationDto>("REGISTRATION_ALREADY_REVIEWED",
                    "该注册申请已经处理");
            if (await db.Users.AnyAsync(x => x.NormalizedUserName == application.NormalizedDesiredOwnerAccount,
                    cancellationToken))
                return ResultFactory.Failure<MerchantRegistrationApplicationDto>("DUPLICATE_ACCOUNT",
                    "负责人账号已存在");

            var now = timeProvider.GetUtcNow();
            var tenantCode = await codeGenerator.NextBrandCodeAsync(cancellationToken);
            var tenant = new Tenant(tenantCode, application.MerchantName);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(cancellationToken);
            var storeCode = await codeGenerator.NextStoreCodeAsync(tenant.Id, cancellationToken);
            var store = new Store(tenant.Id, storeCode, application.StoreName, "Asia/Shanghai");
            db.Stores.Add(store);

            var roles = MerchantRoleNames.ToDictionary(roleName => roleName, roleName => new ApplicationRole
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Name = roleName,
                NormalizedName = roleName.ToUpperInvariant(),
            }, StringComparer.OrdinalIgnoreCase);
            db.Roles.AddRange(roles.Values);
            await db.SaveChangesAsync(cancellationToken);

            var owner = new ApplicationUser
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id,
                UserName = application.DesiredOwnerAccount, DisplayName = application.ContactName,
                IsEnabled = true, MustChangePassword = true,
            };
            var createResult = await userManager.CreateAsync(owner, command.InitialPassword);
            if (!createResult.Succeeded)
                return ResultFactory.Failure<MerchantRegistrationApplicationDto>("ACCOUNT_CREATE_FAILED",
                    $"负责人账号创建失败：{string.Join(';', createResult.Errors.Select(x => x.Code))}");
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = owner.Id, RoleId = roles[SystemRoles.Owner].Id });
            db.UserStores.Add(new UserStore(tenant.Id, owner.Id, store.Id, true));
            var employee = new Employee(tenant.Id, "E0001", application.ContactName, "负责人", owner.Id);
            db.Employees.Add(employee);
            db.EmployeeStores.Add(new EmployeeStore(tenant.Id, employee.Id, store.Id, true));
            foreach (var (roleName, role) in roles)
            foreach (var action in SystemPermissions.ForRole(roleName))
                db.RoleActionGrants.Add(new RoleActionGrant(tenant.Id, role.Id, action));
            db.PriceOverridePolicies.Add(PriceOverridePolicy.Default(tenant.Id, owner.Id, now));
            db.MemberCardTypes.Add(new MemberCardType(tenant.Id, "STANDARD", "标准会员", null));
            AddDefaultPaymentMethods(tenant.Id);

            application.Approve(tenant.Id, platformUserId, command.Reason, now);
            AddPlatformAudit(platformUserId, "platform.registration.approve", "MerchantRegistration",
                application.Id, MerchantRegistrationStatus.PendingReview.ToString(), application.Status.ToString(),
                command.Reason, new { application.ApplicationNo, TenantCode = tenant.Code, StoreCode = store.Code });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToRegistrationDto(application));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<MerchantRegistrationApplicationDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<MerchantRegistrationApplicationDto>("VERSION_CONFLICT",
                "注册申请已变化，请刷新后重试");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<MerchantRegistrationApplicationDto>("RESOURCE_CONFLICT",
                "商户编码、门店编码或负责人账号已存在");
        }
    }

    public async Task<Result<MerchantRegistrationApplicationDto>> RejectAsync(Guid platformUserId,
        RejectMerchantRegistrationCommand command, CancellationToken cancellationToken)
    {
        var application = await db.MerchantRegistrationApplications.SingleOrDefaultAsync(
            x => x.Id == command.ApplicationId, cancellationToken);
        if (application is null)
            return ResultFactory.Failure<MerchantRegistrationApplicationDto>("REGISTRATION_NOT_FOUND", "注册申请不存在");
        if (application.Version != command.ExpectedVersion)
            return ResultFactory.Failure<MerchantRegistrationApplicationDto>("VERSION_CONFLICT",
                "注册申请已变化，请刷新后重试");
        try
        {
            application.Reject(platformUserId, command.Reason, timeProvider.GetUtcNow());
            AddPlatformAudit(platformUserId, "platform.registration.reject", "MerchantRegistration",
                application.Id, MerchantRegistrationStatus.PendingReview.ToString(), application.Status.ToString(),
                command.Reason, new { application.ApplicationNo });
            await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(ToRegistrationDto(application));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<MerchantRegistrationApplicationDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultFactory.Failure<MerchantRegistrationApplicationDto>("VERSION_CONFLICT",
                "注册申请已变化，请刷新后重试");
        }
    }

    public async Task<PlatformMerchantPageDto> ListMerchantsAsync(string? status, string? query, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        var tenants = db.Tenants.AsNoTracking();
        if (Enum.TryParse<TenantStatus>(status, true, out var parsedStatus))
            tenants = tenants.Where(x => x.Status == parsedStatus);
        var normalized = Normalize(query, 100);
        if (normalized is not null)
            tenants = tenants.Where(x => x.Code.Contains(normalized) || x.Name.Contains(normalized));
        var total = await tenants.CountAsync(cancellationToken);
        var items = await tenants.OrderBy(x => x.Code).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(tenant => new PlatformMerchantDto(tenant.Id, tenant.Code, tenant.Name,
                tenant.Status.ToString(), db.Stores.Count(x => x.TenantId == tenant.Id),
                db.Employees.Count(x => x.TenantId == tenant.Id), db.Users.Count(x => x.TenantId == tenant.Id),
                tenant.CreatedAtUtc, tenant.Version)).ToListAsync(cancellationToken);
        return new PlatformMerchantPageDto(items, total, page, pageSize);
    }

    public async Task<Result<PlatformMerchantDto>> ChangeMerchantStatusAsync(Guid platformUserId,
        ChangeMerchantStatusCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == command.TenantId, cancellationToken);
        if (tenant is null) return ResultFactory.Failure<PlatformMerchantDto>("TENANT_NOT_FOUND", "商户不存在");
        if (tenant.Version != command.ExpectedVersion)
            return ResultFactory.Failure<PlatformMerchantDto>("VERSION_CONFLICT", "商户状态已变化，请刷新后重试");
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length is < 2 or > 500)
            return ResultFactory.Failure<PlatformMerchantDto>("VALIDATION_FAILED", "操作原因需要2到500字");
        var previous = tenant.Status.ToString();
        tenant.ChangeStatus(command.Enable);
        AddPlatformAudit(platformUserId, "platform.merchant.status-change", "Tenant", tenant.Id, previous,
            tenant.Status.ToString(), command.Reason.Trim(), new { tenant.Code });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success((await ListMerchantsAsync(null, tenant.Code, 1, 1, cancellationToken))
                .Items.Single(x => x.Id == tenant.Id));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultFactory.Failure<PlatformMerchantDto>("VERSION_CONFLICT", "商户状态已变化，请刷新后重试");
        }
    }

    public async Task<LoginSecurityEventPageDto> ListSecurityEventsAsync(string? scope, string? resultCode,
        Guid? tenantId, string? account, DateOnly? fromDate, DateOnly? toDate, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var events = db.LoginSecurityEvents.AsNoTracking();
        if (scope is "Merchant" or "Platform") events = events.Where(x => x.Scope == scope);
        var normalizedResult = Normalize(resultCode, 64);
        if (normalizedResult is not null) events = events.Where(x => x.ResultCode == normalizedResult);
        if (tenantId is not null) events = events.Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(account))
        {
            var hash = securityEvents.HashAccount(account);
            events = events.Where(x => x.AccountHash == hash);
        }
        if (fromDate is not null)
        {
            var from = new DateTimeOffset(fromDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            events = events.Where(x => x.OccurredAtUtc >= from);
        }
        if (toDate is not null)
        {
            var to = new DateTimeOffset(toDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            events = events.Where(x => x.OccurredAtUtc < to);
        }
        var total = await events.CountAsync(cancellationToken);
        var rows = await events.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var tenantIds = rows.Where(x => x.TenantId is not null).Select(x => x.TenantId!.Value).Distinct().ToList();
        var merchantUserIds = rows.Where(x => x.MerchantUserId is not null).Select(x => x.MerchantUserId!.Value)
            .Distinct().ToList();
        var platformUserIds = rows.Where(x => x.PlatformUserId is not null).Select(x => x.PlatformUserId!.Value)
            .Distinct().ToList();
        var tenants = await db.Tenants.AsNoTracking().Where(x => tenantIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var merchantAccounts = await db.Users.AsNoTracking().Where(x => merchantUserIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserName ?? string.Empty, cancellationToken);
        var platformAccounts = await db.PlatformAdminUsers.AsNoTracking().Where(x => platformUserIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Account, cancellationToken);
        var items = rows.Select(row => new LoginSecurityEventDto(row.Id, row.Scope, row.EventType,
            row.ResultCode, row.TenantId, row.TenantId is not null && tenants.TryGetValue(row.TenantId.Value,
                out var tenantName) ? tenantName : null,
            row.MerchantUserId is not null && merchantAccounts.TryGetValue(row.MerchantUserId.Value,
                out var merchantAccount) ? merchantAccount : row.PlatformUserId is not null &&
                platformAccounts.TryGetValue(row.PlatformUserId.Value, out var platformAccount)
                    ? platformAccount : row.AccountMask,
            row.IpAddress, row.UserAgentSummary, row.TraceId, row.OccurredAtUtc)).ToList();
        return new LoginSecurityEventPageDto(items, total, page, pageSize);
    }

    private MerchantRegistrationApplicationDto ToRegistrationDto(MerchantRegistrationApplication application) =>
        new(application.Id, application.ApplicationNo, application.MerchantName, application.StoreName,
            application.ContactName, $"***{application.ContactMobileLastFour}",
            registrationPrivacy.MaskEmail(application.ContactEmailCiphertext), application.DesiredOwnerAccount,
            application.Note, application.SourceIp, application.Status.ToString(), application.TenantId,
            application.ReviewReason, application.CreatedAtUtc, application.ReviewedAtUtc, application.Version);

    private void AddDefaultPaymentMethods(Guid tenantId)
    {
        db.PaymentMethods.AddRange(
            new PaymentMethod(tenantId, "CASH", "现金", PaymentMethodCategory.Cash, true),
            new PaymentMethod(tenantId, "WECHAT_MANUAL", "微信人工登记", PaymentMethodCategory.ManualExternal, true),
            new PaymentMethod(tenantId, "ALIPAY_MANUAL", "支付宝人工登记", PaymentMethodCategory.ManualExternal, true),
            new PaymentMethod(tenantId, "MEMBER_PRINCIPAL", "会员储值本金",
                PaymentMethodCategory.InternalAccount, false, MemberAccountType.Principal),
            new PaymentMethod(tenantId, "MEMBER_BONUS", "会员奖励金",
                PaymentMethodCategory.InternalAccount, false, MemberAccountType.Bonus));
        var wechat = new PaymentMethod(tenantId, "WECHAT_NATIVE", "微信支付 Native",
            PaymentMethodCategory.ChannelExternal, true, channelProvider: PaymentChannelProvider.WeChatPay);
        wechat.SetEnabled(false);
        var alipay = new PaymentMethod(tenantId, "ALIPAY_QR", "支付宝订单码",
            PaymentMethodCategory.ChannelExternal, true, channelProvider: PaymentChannelProvider.Alipay);
        alipay.SetEnabled(false);
        db.PaymentMethods.AddRange(wechat, alipay);
    }

    private void AddPlatformAudit(Guid platformUserId, string action, string entityType, Guid entityId,
        string? previous, string? current, string? reason, object metadata) =>
        db.PlatformAuditEvents.Add(new PlatformAuditEventRecord
        {
            PlatformUserId = platformUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            PreviousState = previous,
            CurrentState = current,
            Reason = reason,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? string.Empty,
            Metadata = JsonSerializer.Serialize(metadata),
            OccurredAtUtc = timeProvider.GetUtcNow(),
        });

    private static string? Normalize(string? value, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        return normalized.Length > maximum ? normalized[..maximum] : normalized;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

}
