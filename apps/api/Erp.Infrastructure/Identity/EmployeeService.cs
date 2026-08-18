using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Security;
using Erp.Domain.Organization;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Identity;

public sealed partial class EmployeeService(ErpDbContext db, UserManager<ApplicationUser> userManager,
    IHttpContextAccessor httpContextAccessor) : IEmployeeService
{
    private static readonly Dictionary<string, string> RoleNames = new()
    {
        [SystemRoles.Owner] = "最高权限/老板", [SystemRoles.StoreManager] = "店长",
        [SystemRoles.FrontDesk] = "前台", [SystemRoles.Cashier] = "收银员", [SystemRoles.Technician] = "服务员工",
    };

    public async Task<IReadOnlyList<EmployeeDto>> ListAsync(Guid tenantId, string? query,
        CancellationToken cancellationToken)
    {
        var term = query?.Trim();
        if (term?.Length > 100) return [];
        var employeeQuery = db.Set<Employee>().AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(term))
        {
            employeeQuery = employeeQuery.Where(employee =>
                employee.EmployeeNo.Contains(term) || employee.DisplayName.Contains(term) ||
                employee.PositionCode.Contains(term) ||
                db.Users.Any(user => employee.UserId.HasValue && user.Id == employee.UserId.Value &&
                    user.TenantId == tenantId && user.UserName != null && user.UserName.Contains(term)) ||
                db.Set<EmployeeStore>().Any(assignment => assignment.EmployeeId == employee.Id &&
                    assignment.TenantId == tenantId && db.Stores.Any(store => store.Id == assignment.StoreId &&
                        store.TenantId == tenantId && (store.Code.Contains(term) || store.Name.Contains(term)))));
        }
        var employees = await employeeQuery
            .OrderBy(x => x.EmployeeNo).ToListAsync(cancellationToken);
        var userIds = employees.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).ToList();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id) && x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var employeeIds = employees.Select(x => x.Id).ToList();
        var assignments = await db.Set<EmployeeStore>().AsNoTracking().Where(x =>
                x.TenantId == tenantId && employeeIds.Contains(x.EmployeeId))
            .Join(db.Stores.AsNoTracking(), x => x.StoreId, x => x.Id, (assignment, store) => new { assignment, store })
            .ToListAsync(cancellationToken);
        var userRoles = await (from link in db.UserRoles.AsNoTracking()
            join role in db.Roles.AsNoTracking() on link.RoleId equals role.Id
            where userIds.Contains(link.UserId) && role.TenantId == tenantId
            select new { link.UserId, Role = role.Name! }).ToListAsync(cancellationToken);

        return employees.Select(employee => ToDto(employee,
            employee.UserId.HasValue && users.TryGetValue(employee.UserId.Value, out var user) ? user : null,
            userRoles.Where(x => x.UserId == employee.UserId).Select(x => x.Role).Order().ToList(),
            assignments.Where(x => x.assignment.EmployeeId == employee.Id)
                .Select(x => new EmployeeStoreDto(x.store.Id, x.store.Code, x.store.Name, x.assignment.IsPrimary))
                .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Name).ToList())).ToList();
    }

    public async Task<IReadOnlyList<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().Where(x => x.TenantId == tenantId && x.Name != null)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(cancellationToken);
        return roles.Select(x => new RoleDto(x.Id, x.Name!,
            RoleNames.TryGetValue(x.Name!, out var displayName) ? displayName : x.Name!)).ToList();
    }

    public async Task<Result<EmployeeDto>> CreateAsync(Guid tenantId, CreateEmployeeCommand command,
        CancellationToken cancellationToken)
    {
        var employeeNo = command.EmployeeNo.Trim().ToUpperInvariant();
        var displayName = command.DisplayName.Trim();
        var positionCode = command.PositionCode.Trim();
        var storeIds = command.StoreIds.Distinct().ToList();
        if (!EmployeeNoPattern().IsMatch(employeeNo) || displayName.Length is < 2 or > 100 || positionCode.Length is < 2 or > 40)
            return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "请检查员工工号、姓名和岗位");
        if (storeIds.Count == 0)
            return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "至少选择一个所属门店");
        if (await db.Set<Employee>().AnyAsync(x => x.TenantId == tenantId && x.EmployeeNo == employeeNo, cancellationToken))
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_NO_EXISTS", "员工工号已存在");
        var stores = await db.Stores.Where(x => x.TenantId == tenantId && storeIds.Contains(x.Id) && x.Status == StoreStatus.Enabled)
            .OrderBy(x => x.Name).ToListAsync(cancellationToken);
        if (stores.Count != storeIds.Count)
            return ResultFactory.Failure<EmployeeDto>("INVALID_STORE_SCOPE", "所选门店不存在、已停用或不在授权范围");

        var roles = command.Roles.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList();
        var account = command.Account?.Trim();
        if (command.CreateLoginAccount)
        {
            if (string.IsNullOrWhiteSpace(account) || !AccountPattern().IsMatch(account) || command.InitialPassword is null)
                return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "启用登录时，账号和初始密码必填");
            if (roles.Count == 0)
                return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "启用登录时至少选择一个角色");
            var validRoleCount = await db.Roles.CountAsync(x => x.TenantId == tenantId && x.Name != null && roles.Contains(x.Name), cancellationToken);
            if (validRoleCount != roles.Count)
                return ResultFactory.Failure<EmployeeDto>("INVALID_ROLE", "所选角色无效");
            if (await userManager.FindByNameAsync(account) is not null)
                return ResultFactory.Failure<EmployeeDto>("ACCOUNT_EXISTS", "登录账号已存在");
        }
        else if (!string.IsNullOrWhiteSpace(account) || !string.IsNullOrEmpty(command.InitialPassword) || roles.Count > 0)
            return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "未启用登录时不能提交账号、密码或角色");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        ApplicationUser? user = null;
        try
        {
            if (command.CreateLoginAccount)
            {
                user = new ApplicationUser { Id = Guid.CreateVersion7(), TenantId = tenantId, UserName = account,
                    DisplayName = displayName, IsEnabled = true, MustChangePassword = true };
                var createResult = await userManager.CreateAsync(user, command.InitialPassword!);
                if (!createResult.Succeeded)
                    return await RollbackFailure(transaction, "INVALID_INITIAL_PASSWORD", "初始密码不符合安全要求", cancellationToken);
                var roleResult = await userManager.AddToRolesAsync(user, roles);
                if (!roleResult.Succeeded)
                    return await RollbackFailure(transaction, "ROLE_ASSIGNMENT_FAILED", "角色分配失败", cancellationToken);
                db.UserStores.AddRange(stores.Select((store, index) => new UserStore(tenantId, user.Id, store.Id, index == 0)));
            }

            var employee = new Employee(tenantId, employeeNo, displayName, positionCode, user?.Id);
            db.Set<Employee>().Add(employee);
            db.Set<EmployeeStore>().AddRange(stores.Select((store, index) => new EmployeeStore(tenantId, employee.Id, store.Id, index == 0)));
            AddAudit(tenantId, stores[0].Id, command.OperatorId, "employee.create", employee.Id, null, "Active",
                JsonSerializer.Serialize(new { employeeNo, positionCode, loginEnabled = user is not null, roles }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(employee, user, roles,
                stores.Select((store, index) => new EmployeeStoreDto(store.Id, store.Code, store.Name, index == 0)).ToList()));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_CREATE_CONFLICT", "员工或账号信息已存在，请刷新后重试");
        }
    }

    public async Task<Result<EmployeeDto>> SetAccountStatusAsync(Guid tenantId, SetEmployeeAccountStatusCommand command,
        CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().SingleOrDefaultAsync(x => x.Id == command.EmployeeId && x.TenantId == tenantId,
            cancellationToken);
        if (employee?.UserId is null)
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_ACCOUNT_NOT_FOUND", "该员工没有登录账号");
        if (employee.UserId == command.OperatorId)
            return ResultFactory.Failure<EmployeeDto>("CANNOT_DISABLE_SELF", "不能在当前登录会话中变更自己的账号状态");
        var user = await userManager.FindByIdAsync(employee.UserId.Value.ToString());
        if (user is null || user.TenantId != tenantId)
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_ACCOUNT_NOT_FOUND", "员工登录账号不存在");
        if (user.IsEnabled == command.IsEnabled)
            return ResultFactory.Success(await BuildEmployeeDto(employee, user, tenantId, cancellationToken));

        if (!command.IsEnabled && await userManager.IsInRoleAsync(user, SystemRoles.Owner))
        {
            var enabledOwnerCount = await (from candidate in db.Users
                join link in db.UserRoles on candidate.Id equals link.UserId
                join role in db.Roles on link.RoleId equals role.Id
                where candidate.TenantId == tenantId && candidate.IsEnabled && role.Name == SystemRoles.Owner
                select candidate.Id).Distinct().CountAsync(cancellationToken);
            if (enabledOwnerCount <= 1)
                return ResultFactory.Failure<EmployeeDto>("LAST_OWNER_REQUIRED", "系统必须保留至少一个有效的最高权限账号");
        }

        var previous = user.IsEnabled ? "Enabled" : "Disabled";
        user.IsEnabled = command.IsEnabled;
        user.SecurityStamp = Guid.NewGuid().ToString();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return ResultFactory.Failure<EmployeeDto>("ACCOUNT_STATUS_UPDATE_FAILED", "账号状态更新失败");
        var primaryStoreId = await db.Set<EmployeeStore>().Where(x => x.EmployeeId == employee.Id).OrderByDescending(x => x.IsPrimary)
            .Select(x => (Guid?)x.StoreId).FirstOrDefaultAsync(cancellationToken);
        AddAudit(tenantId, primaryStoreId, command.OperatorId, command.IsEnabled ? "employee.account.enable" : "employee.account.disable",
            employee.Id, previous, command.IsEnabled ? "Enabled" : "Disabled", null);
        await db.SaveChangesAsync(cancellationToken);
        return ResultFactory.Success(await BuildEmployeeDto(employee, user, tenantId, cancellationToken));
    }

    private async Task<EmployeeDto> BuildEmployeeDto(Employee employee, ApplicationUser user, Guid tenantId,
        CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var stores = await db.Set<EmployeeStore>().AsNoTracking().Where(x => x.EmployeeId == employee.Id && x.TenantId == tenantId)
            .Join(db.Stores.AsNoTracking(), x => x.StoreId, x => x.Id, (assignment, store) =>
                new { Store = store, assignment.IsPrimary })
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Store.Name)
            .Select(x => new EmployeeStoreDto(x.Store.Id, x.Store.Code, x.Store.Name, x.IsPrimary))
            .ToListAsync(cancellationToken);
        return ToDto(employee, user, roles.Order().ToList(), stores);
    }

    private static EmployeeDto ToDto(Employee employee, ApplicationUser? user, IReadOnlyList<string> roles,
        IReadOnlyList<EmployeeStoreDto> stores) => new(employee.Id, employee.EmployeeNo, employee.DisplayName,
        employee.PositionCode, employee.Status.ToString(), user?.Id, user?.UserName, user?.IsEnabled,
        user?.MustChangePassword, roles, stores, employee.CreatedAtUtc);

    private static async Task<Result<EmployeeDto>> RollbackFailure(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string code, string message, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return ResultFactory.Failure<EmployeeDto>(code, message);
    }

    private void AddAudit(Guid tenantId, Guid? storeId, Guid operatorId, string action, Guid employeeId,
        string? previousState, string? currentState, string? metadata) => db.AuditEvents.Add(new AuditEventRecord
    {
        TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action, EntityType = "Employee",
        EntityId = employeeId, PreviousState = previousState, CurrentState = currentState,
        TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N"),
        Metadata = metadata ?? "{}", OccurredAtUtc = DateTimeOffset.UtcNow,
    });

    [GeneratedRegex("^[A-Z0-9_-]{2,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmployeeNoPattern();

    [GeneratedRegex("^[A-Za-z0-9._@-]{4,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountPattern();
}
