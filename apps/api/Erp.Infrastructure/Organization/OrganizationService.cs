using System.Text.Json;
using Erp.Application.Common;
using Erp.Application.Organization;
using Erp.Application.Security;
using Erp.Domain.Cashier;
using Erp.Domain.Common;
using Erp.Domain.Facilities;
using Erp.Domain.Organization;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Infrastructure.Organization;

public sealed class OrganizationService(ErpDbContext db, IHttpContextAccessor httpContextAccessor,
    BusinessCodeGenerator codeGenerator)
    : IOrganizationService
{
    public async Task<OrganizationSettingsDto?> GetSettingsAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId,
            cancellationToken);
        if (tenant is null) return null;
        var stores = await db.Stores.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Code).ToListAsync(cancellationToken);
        return new OrganizationSettingsDto(ToBrand(tenant), await BuildStoresAsync(tenantId, stores,
            cancellationToken));
    }

    public async Task<Result<BrandProfileDto>> UpdateBrandAsync(Guid tenantId,
        UpdateBrandProfileCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (tenant is null) return ResultFactory.Failure<BrandProfileDto>("TENANT_NOT_FOUND", "品牌不存在");
        if (tenant.Version != command.ExpectedVersion)
            return ResultFactory.Failure<BrandProfileDto>("VERSION_CONFLICT", "品牌资料已变化，请刷新后重试");
        var code = command.Code.Trim().ToUpperInvariant();
        if (!string.Equals(tenant.Code, code, StringComparison.Ordinal))
            return ResultFactory.Failure<BrandProfileDto>("TENANT_CODE_IMMUTABLE", "品牌编码创建后不可修改");
        var before = new { tenant.Code, tenant.Name };
        try
        {
            tenant.UpdateProfile(code, command.Name);
            AddAudit(tenantId, null, command.OperatorId, "organization.brand.update", "Tenant", tenant.Id,
                tenant.Status.ToString(), tenant.Status.ToString(), JsonSerializer.Serialize(new
                {
                    before, after = new { tenant.Code, tenant.Name },
                }));
            await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(ToBrand(tenant));
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<BrandProfileDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultFactory.Failure<BrandProfileDto>("VERSION_CONFLICT", "品牌资料已变化，请刷新后重试");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ResultFactory.Failure<BrandProfileDto>("DUPLICATE_TENANT_CODE", "品牌编码已存在");
        }
    }

    public async Task<Result<StoreProfileDto>> CreateStoreAsync(Guid tenantId, CreateStoreCommand command,
        CancellationToken cancellationToken)
    {
        if (!ValidTimeZone(command.TimeZoneId))
            return ResultFactory.Failure<StoreProfileDto>("INVALID_TIME_ZONE", "门店时区无效");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var code = await codeGenerator.NextStoreCodeAsync(tenantId, cancellationToken);
            var store = new Store(tenantId, code, command.Name, command.TimeZoneId);
            db.Stores.Add(store);
            await db.SaveChangesAsync(cancellationToken);
            AddAudit(tenantId, store.Id, command.OperatorId, "organization.store.create", "Store", store.Id,
                null, store.Status.ToString(), JsonSerializer.Serialize(new
                {
                    store.Code, store.Name, store.TimeZoneId,
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success((await BuildStoresAsync(tenantId, [store], cancellationToken)).Single());
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<StoreProfileDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<StoreProfileDto>("DUPLICATE_STORE_CODE", "门店编码已存在");
        }
    }

    public async Task<Result<StoreProfileDto>> UpdateStoreAsync(Guid tenantId, UpdateStoreCommand command,
        CancellationToken cancellationToken)
    {
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == command.StoreId && x.TenantId == tenantId,
            cancellationToken);
        if (store is null) return ResultFactory.Failure<StoreProfileDto>("STORE_NOT_FOUND", "门店不存在");
        if (store.Version != command.ExpectedVersion)
            return ResultFactory.Failure<StoreProfileDto>("VERSION_CONFLICT", "门店资料已变化，请刷新后重试");
        if (!ValidTimeZone(command.TimeZoneId))
            return ResultFactory.Failure<StoreProfileDto>("INVALID_TIME_ZONE", "门店时区无效");
        var code = command.Code.Trim().ToUpperInvariant();
        if (!string.Equals(store.Code, code, StringComparison.Ordinal))
            return ResultFactory.Failure<StoreProfileDto>("STORE_CODE_IMMUTABLE", "门店编码创建后不可修改");
        var before = new { store.Code, store.Name, store.TimeZoneId };
        try
        {
            store.UpdateProfile(code, command.Name, command.TimeZoneId);
            AddAudit(tenantId, store.Id, command.OperatorId, "organization.store.update", "Store", store.Id,
                store.Status.ToString(), store.Status.ToString(), JsonSerializer.Serialize(new
                {
                    before, after = new { store.Code, store.Name, store.TimeZoneId },
                }));
            await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success((await BuildStoresAsync(tenantId, [store], cancellationToken)).Single());
        }
        catch (DomainRuleException exception)
        {
            return ResultFactory.Failure<StoreProfileDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultFactory.Failure<StoreProfileDto>("VERSION_CONFLICT", "门店资料已变化，请刷新后重试");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ResultFactory.Failure<StoreProfileDto>("DUPLICATE_STORE_CODE", "门店编码已存在");
        }
    }

    public async Task<Result<StoreProfileDto>> ChangeStoreStatusAsync(Guid tenantId,
        ChangeStoreStatusCommand command, CancellationToken cancellationToken)
    {
        var reason = command.Reason.Trim();
        if (reason.Length is < 2 or > 200)
            return ResultFactory.Failure<StoreProfileDto>("VALIDATION_FAILED", "启停原因必须为2到200字");
        var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == command.StoreId && x.TenantId == tenantId,
            cancellationToken);
        if (store is null) return ResultFactory.Failure<StoreProfileDto>("STORE_NOT_FOUND", "门店不存在");
        if (store.Version != command.ExpectedVersion)
            return ResultFactory.Failure<StoreProfileDto>("VERSION_CONFLICT", "门店状态已变化，请刷新后重试");
        if (!command.Enable && store.Status == StoreStatus.Enabled)
        {
            if (await db.Stores.CountAsync(x => x.TenantId == tenantId && x.Status == StoreStatus.Enabled,
                    cancellationToken) <= 1)
                return ResultFactory.Failure<StoreProfileDto>("LAST_STORE_REQUIRED", "系统必须保留至少一家有效门店");
            var blockers = await DisableBlockersAsync(tenantId, store.Id, cancellationToken);
            if (blockers.Count > 0)
                return ResultFactory.Failure<StoreProfileDto>("STORE_DISABLE_BLOCKED", string.Join('；', blockers));
        }
        var previous = store.Status.ToString();
        if (command.Enable) store.Enable(); else store.Disable();
        try
        {
            AddAudit(tenantId, store.Id, command.OperatorId,
                command.Enable ? "organization.store.enable" : "organization.store.disable", "Store", store.Id,
                previous, store.Status.ToString(), JsonSerializer.Serialize(new { reason }));
            await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success((await BuildStoresAsync(tenantId, [store], cancellationToken)).Single());
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultFactory.Failure<StoreProfileDto>("VERSION_CONFLICT", "门店状态已变化，请刷新后重试");
        }
    }

    private async Task<List<string>> DisableBlockersAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken)
    {
        var blockers = new List<string>();
        if (await db.FacilitySessions.AnyAsync(x => x.TenantId == tenantId && x.StoreId == storeId &&
                (x.Status == FacilitySessionStatus.Active || x.Status == FacilitySessionStatus.Paused),
                cancellationToken)) blockers.Add("门店仍有使用中的设施");
        if (await db.Visits.AnyAsync(x => x.TenantId == tenantId && x.StoreId == storeId &&
                (x.Status == VisitStatus.Arrived || x.Status == VisitStatus.InService ||
                 x.Status == VisitStatus.ServiceEnded), cancellationToken)) blockers.Add("门店仍有未完成接待");
        if (await db.ServiceOrders.AnyAsync(x => x.TenantId == tenantId && x.StoreId == storeId &&
                (x.Status == ServiceOrderStatus.Draft || x.Status == ServiceOrderStatus.PendingPayment ||
                 x.Status == ServiceOrderStatus.PaymentProcessing), cancellationToken)) blockers.Add("门店仍有未完成消费单");
        if (await db.CashierShifts.AnyAsync(x => x.TenantId == tenantId && x.StoreId == storeId &&
                x.Status != CashierShiftStatus.Closed, cancellationToken)) blockers.Add("门店仍有未关闭班次");
        return blockers;
    }

    private async Task<IReadOnlyList<StoreProfileDto>> BuildStoresAsync(Guid tenantId, IReadOnlyList<Store> stores,
        CancellationToken cancellationToken)
    {
        var ids = stores.Select(x => x.Id).ToList();
        var employeeCounts = await db.EmployeeStores.AsNoTracking().Where(x => ids.Contains(x.StoreId))
            .Join(db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == EmployeeStatus.Active),
                assignment => assignment.EmployeeId, employee => employee.Id,
                (assignment, _) => assignment.StoreId).GroupBy(x => x)
            .Select(x => new { StoreId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);
        var managerUserIds = await (from link in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on link.RoleId equals role.Id
            where role.TenantId == tenantId && role.Name == SystemRoles.StoreManager
            select link.UserId).Distinct().ToListAsync(cancellationToken);
        var managerRows = await (from assignment in db.EmployeeStores.AsNoTracking()
            join employee in db.Employees.AsNoTracking() on assignment.EmployeeId equals employee.Id
            where ids.Contains(assignment.StoreId) && employee.TenantId == tenantId &&
                employee.Status == EmployeeStatus.Active && employee.UserId.HasValue &&
                managerUserIds.Contains(employee.UserId.Value)
            select new { assignment.StoreId, employee.DisplayName }).ToListAsync(cancellationToken);
        var managers = managerRows.GroupBy(x => x.StoreId).ToDictionary(x => x.Key,
            x => (IReadOnlyList<string>)x.Select(row => row.DisplayName).Distinct().Order().ToList());
        var groupCounts = await db.FacilityGroups.AsNoTracking().Where(x => ids.Contains(x.StoreId))
            .GroupBy(x => x.StoreId).Select(x => new { StoreId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.StoreId, x => x.Count, cancellationToken);
        var facilityCounts = await db.Facilities.AsNoTracking().Where(x => ids.Contains(x.StoreId))
            .GroupBy(x => x.StoreId).Select(x => new
            {
                StoreId = x.Key, Count = x.Count(),
                Enabled = x.Count(item => item.LifecycleStatus == FacilityLifecycleStatus.Enabled),
            }).ToDictionaryAsync(x => x.StoreId, cancellationToken);
        return stores.Select(store => new StoreProfileDto(store.Id, store.Code, store.Name, store.TimeZoneId,
            store.Status.ToString(), managers.GetValueOrDefault(store.Id, []),
            employeeCounts.GetValueOrDefault(store.Id), groupCounts.GetValueOrDefault(store.Id),
            facilityCounts.TryGetValue(store.Id, out var counts) ? counts.Count : 0,
            facilityCounts.TryGetValue(store.Id, out counts) ? counts.Enabled : 0, store.Version)).ToList();
    }

    private static BrandProfileDto ToBrand(Tenant tenant) => new(tenant.Id, tenant.Code, tenant.Name,
        tenant.Status.ToString(), tenant.Version);

    private static bool ValidTimeZone(string value)
    {
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(value.Trim()); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private void AddAudit(Guid tenantId, Guid? storeId, Guid operatorId, string action, string entityType,
        Guid entityId, string? previousState, string? currentState, string? metadata) =>
        db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action,
            EntityType = entityType, EntityId = entityId, PreviousState = previousState,
            CurrentState = currentState, TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ??
                Guid.NewGuid().ToString("N"), Metadata = metadata ?? "{}", OccurredAtUtc = DateTimeOffset.UtcNow,
        });
}
