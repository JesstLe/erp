using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Application.Common;
using Erp.Application.Identity;
using Erp.Application.Security;
using Erp.Domain.Common;
using Erp.Domain.Organization;
using Erp.Infrastructure.Persistence;
using Erp.Infrastructure.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Identity;

internal sealed partial class EmployeeService(ErpDbContext db, UserManager<ApplicationUser> userManager,
    IHttpContextAccessor httpContextAccessor, LoginSecurityEventWriter securityEvents) : IEmployeeService
{
    private static readonly Dictionary<string, string> RoleNames = new()
    {
        [SystemRoles.Owner] = "最高权限/老板", [SystemRoles.StoreManager] = "店长",
        [SystemRoles.FrontDesk] = "前台", [SystemRoles.Cashier] = "收银员", [SystemRoles.Technician] = "服务员工",
    };

    public async Task<PageResult<EmployeeDto>> ListAsync(Guid tenantId, string? query, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var term = query?.Trim();
        if (term?.Length > 100) return new PageResult<EmployeeDto>([], 0, page, pageSize);
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
        var total = await employeeQuery.CountAsync(cancellationToken);
        var employees = await employeeQuery.OrderBy(x => x.EmployeeNo).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
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

        var items = employees.Select(employee => ToDto(employee,
            employee.UserId.HasValue && users.TryGetValue(employee.UserId.Value, out var user) ? user : null,
            userRoles.Where(x => x.UserId == employee.UserId).Select(x => x.Role).Order().ToList(),
            assignments.Where(x => x.assignment.EmployeeId == employee.Id)
                .Select(x => new EmployeeStoreDto(x.store.Id, x.store.Code, x.store.Name, x.assignment.IsPrimary))
                .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Name).ToList())).ToList();
        return new PageResult<EmployeeDto>(items, total, page, pageSize);
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
        List<ApplicationRole> tenantRoles = [];
        if (command.CreateLoginAccount)
        {
            if (string.IsNullOrWhiteSpace(account) || !AccountPattern().IsMatch(account) || command.InitialPassword is null)
                return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "启用登录时，账号和初始密码必填");
            if (roles.Count == 0)
                return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "启用登录时至少选择一个角色");
            tenantRoles = await db.Roles.Where(x => x.TenantId == tenantId && x.Name != null &&
                roles.Contains(x.Name)).ToListAsync(cancellationToken);
            if (tenantRoles.Count != roles.Count)
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
                db.UserRoles.AddRange(tenantRoles.Select(role => new IdentityUserRole<Guid>
                {
                    UserId = user.Id,
                    RoleId = role.Id,
                }));
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
        if (employee.Status != EmployeeStatus.Active && command.IsEnabled)
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_INACTIVE", "离职员工不能启用登录账号");
        if (employee.UserId == command.OperatorId)
            return ResultFactory.Failure<EmployeeDto>("CANNOT_DISABLE_SELF", "不能在当前登录会话中变更自己的账号状态");
        var user = await userManager.FindByIdAsync(employee.UserId.Value.ToString());
        if (user is null || user.TenantId != tenantId)
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_ACCOUNT_NOT_FOUND", "员工登录账号不存在");
        if (user.IsEnabled == command.IsEnabled)
            return ResultFactory.Success(user is null
                ? await BuildEmployeeDtoWithoutUser(employee, tenantId, cancellationToken)
                : await BuildEmployeeDto(employee, user, tenantId, cancellationToken));

        if (!command.IsEnabled && await IsTenantOwnerAsync(tenantId, user.Id, cancellationToken))
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

    public async Task<Result<EmployeeDto>> UpdateAsync(Guid tenantId, UpdateEmployeeCommand command,
        CancellationToken cancellationToken)
    {
        var displayName = command.DisplayName.Trim();
        var positionCode = command.PositionCode.Trim();
        var storeIds = command.StoreIds.Distinct().ToList();
        var roles = command.Roles.Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0)
            .Distinct().Order().ToList();
        if (storeIds.Count == 0 || displayName.Length is < 2 or > 100 || positionCode.Length is < 2 or > 40)
            return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "请检查员工姓名、岗位和所属门店");
        var stores = await db.Stores.Where(x => x.TenantId == tenantId && storeIds.Contains(x.Id) &&
                x.Status == StoreStatus.Enabled).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        if (stores.Count != storeIds.Count)
            return ResultFactory.Failure<EmployeeDto>("INVALID_STORE_SCOPE", "所选门店不存在或已停用");
        var employee = await db.Set<Employee>().SingleOrDefaultAsync(x => x.Id == command.EmployeeId &&
            x.TenantId == tenantId, cancellationToken);
        if (employee is null)
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_NOT_FOUND", "员工不存在");
        if (employee.Version != command.ExpectedVersion)
            return ResultFactory.Failure<EmployeeDto>("VERSION_CONFLICT", "员工资料已变化，请刷新后重试");
        ApplicationUser? user = null;
        IList<string> currentRoles = [];
        if (employee.UserId.HasValue)
        {
            user = await userManager.FindByIdAsync(employee.UserId.Value.ToString());
            if (user is null || user.TenantId != tenantId)
                return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_ACCOUNT_NOT_FOUND", "员工登录账号不存在");
            currentRoles = await GetTenantRoleNamesAsync(tenantId, user.Id, cancellationToken);
            var validRoleCount = await db.Roles.CountAsync(x => x.TenantId == tenantId && x.Name != null &&
                roles.Contains(x.Name), cancellationToken);
            if (roles.Count == 0 || validRoleCount != roles.Count)
                return ResultFactory.Failure<EmployeeDto>("INVALID_ROLE", "有登录账号的员工必须保留至少一个有效角色");
        }
        else if (roles.Count > 0)
            return ResultFactory.Failure<EmployeeDto>("INVALID_ROLE", "无登录账号员工不能分配系统角色");

        var currentStoreIds = await db.Set<EmployeeStore>().Where(x => x.TenantId == tenantId &&
            x.EmployeeId == employee.Id).Select(x => x.StoreId).ToListAsync(cancellationToken);
        var authorizationChanged = !currentStoreIds.Order().SequenceEqual(storeIds.Order()) ||
            !currentRoles.Order().SequenceEqual(roles.Order(), StringComparer.OrdinalIgnoreCase);
        if (employee.UserId == command.OperatorId && authorizationChanged)
            return ResultFactory.Failure<EmployeeDto>("CANNOT_CHANGE_SELF_AUTHORIZATION",
                "不能在当前登录会话中修改自己的角色或门店范围");
        if (user is not null && currentRoles.Contains(SystemRoles.Owner, StringComparer.OrdinalIgnoreCase) &&
            !roles.Contains(SystemRoles.Owner, StringComparer.OrdinalIgnoreCase) &&
            !await HasAnotherEnabledOwnerAsync(tenantId, user.Id, cancellationToken))
            return ResultFactory.Failure<EmployeeDto>("LAST_OWNER_REQUIRED", "系统必须保留至少一个有效的最高权限账号");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var previous = new
            {
                employee.DisplayName, employee.PositionCode, stores = currentStoreIds, roles = currentRoles,
            };
            employee.UpdateProfile(displayName, positionCode);
            var oldAssignments = await db.Set<EmployeeStore>().Where(x => x.TenantId == tenantId &&
                x.EmployeeId == employee.Id).ToListAsync(cancellationToken);
            db.RemoveRange(oldAssignments);
            db.AddRange(stores.Select((store, index) => new EmployeeStore(tenantId, employee.Id, store.Id,
                index == 0)));
            if (user is not null)
            {
                user.DisplayName = displayName;
                var removeRoles = currentRoles.Where(x => !roles.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
                var addRoles = roles.Where(x => !currentRoles.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
                if (removeRoles.Count > 0)
                {
                    var removeLinks = await (from link in db.UserRoles
                        join role in db.Roles on link.RoleId equals role.Id
                        where link.UserId == user.Id && role.TenantId == tenantId && role.Name != null &&
                            removeRoles.Contains(role.Name)
                        select link).ToListAsync(cancellationToken);
                    db.UserRoles.RemoveRange(removeLinks);
                }
                if (addRoles.Count > 0)
                {
                    var addRoleIds = await db.Roles.Where(role => role.TenantId == tenantId && role.Name != null &&
                            addRoles.Contains(role.Name)).Select(role => role.Id).ToListAsync(cancellationToken);
                    db.UserRoles.AddRange(addRoleIds.Select(roleId => new IdentityUserRole<Guid>
                    {
                        UserId = user.Id,
                        RoleId = roleId,
                    }));
                }
                var oldUserStores = await db.UserStores.Where(x => x.TenantId == tenantId && x.UserId == user.Id)
                    .ToListAsync(cancellationToken);
                db.RemoveRange(oldUserStores);
                db.UserStores.AddRange(stores.Select((store, index) => new UserStore(tenantId, user.Id, store.Id,
                    index == 0)));
                if (!(await userManager.UpdateAsync(user)).Succeeded)
                    return await RollbackFailure(transaction, "ACCOUNT_UPDATE_FAILED", "登录账号资料更新失败",
                        cancellationToken);
            }
            AddAudit(tenantId, stores[0].Id, command.OperatorId, "employee.update", employee.Id,
                employee.Status.ToString(), employee.Status.ToString(), JsonSerializer.Serialize(new
                {
                    before = previous,
                    after = new { displayName, positionCode, stores = storeIds, roles },
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(user is null
                ? await BuildEmployeeDtoWithoutUser(employee, tenantId, cancellationToken)
                : await BuildEmployeeDto(employee, user, tenantId, cancellationToken));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<EmployeeDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<EmployeeDto>("VERSION_CONFLICT", "员工资料已变化，请刷新后重试");
        }
    }

    public async Task<Result<EmployeeDto>> ChangeEmploymentStatusAsync(Guid tenantId,
        ChangeEmploymentStatusCommand command, CancellationToken cancellationToken)
    {
        var reason = command.Reason.Trim();
        if (reason.Length is < 2 or > 200)
            return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "离职或恢复原因必须为2到200字");
        var employee = await db.Set<Employee>().SingleOrDefaultAsync(x => x.Id == command.EmployeeId &&
            x.TenantId == tenantId, cancellationToken);
        if (employee is null)
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_NOT_FOUND", "员工不存在");
        if (employee.Version != command.ExpectedVersion)
            return ResultFactory.Failure<EmployeeDto>("VERSION_CONFLICT", "员工状态已变化，请刷新后重试");
        if (employee.UserId == command.OperatorId)
            return ResultFactory.Failure<EmployeeDto>("CANNOT_DISABLE_SELF", "不能在当前登录会话中变更自己的在职状态");
        var user = employee.UserId.HasValue
            ? await userManager.FindByIdAsync(employee.UserId.Value.ToString()) : null;
        if (!command.Reactivate && user is not null &&
            await IsTenantOwnerAsync(tenantId, user.Id, cancellationToken) &&
            !await HasAnotherEnabledOwnerAsync(tenantId, user.Id, cancellationToken))
            return ResultFactory.Failure<EmployeeDto>("LAST_OWNER_REQUIRED", "系统必须保留至少一个有效的最高权限账号");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var previous = employee.Status.ToString();
            if (command.Reactivate) employee.Reactivate(); else employee.Deactivate();
            if (!command.Reactivate && user is not null && user.IsEnabled)
            {
                user.IsEnabled = false;
                user.SecurityStamp = Guid.NewGuid().ToString();
                if (!(await userManager.UpdateAsync(user)).Succeeded)
                    return await RollbackFailure(transaction, "ACCOUNT_STATUS_UPDATE_FAILED", "离职账号停用失败",
                        cancellationToken);
            }
            AddAudit(tenantId, await PrimaryStoreIdAsync(employee.Id, cancellationToken), command.OperatorId,
                command.Reactivate ? "employee.reactivate" : "employee.terminate", employee.Id, previous,
                employee.Status.ToString(), JsonSerializer.Serialize(new
                {
                    reason, loginDisabled = !command.Reactivate && user is not null,
                }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(user is null
                ? await BuildEmployeeDtoWithoutUser(employee, tenantId, cancellationToken)
                : await BuildEmployeeDto(employee, user, tenantId, cancellationToken));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<EmployeeDto>(exception.Code, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<EmployeeDto>("VERSION_CONFLICT", "员工状态已变化，请刷新后重试");
        }
    }

    public async Task<Result<EmployeeDto>> ResetPasswordAsync(Guid tenantId,
        ResetEmployeePasswordCommand command, CancellationToken cancellationToken)
    {
        var reason = command.Reason.Trim();
        if (reason.Length is < 2 or > 200 || string.IsNullOrEmpty(command.NewInitialPassword))
            return ResultFactory.Failure<EmployeeDto>("VALIDATION_FAILED", "新初始密码和2到200字重置原因必填");
        var employee = await db.Set<Employee>().SingleOrDefaultAsync(x => x.Id == command.EmployeeId &&
            x.TenantId == tenantId, cancellationToken);
        if (employee?.UserId is null)
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_ACCOUNT_NOT_FOUND", "该员工没有登录账号");
        if (employee.Status != EmployeeStatus.Active)
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_INACTIVE", "离职员工不能重置登录密码");
        if (employee.UserId == command.OperatorId)
            return ResultFactory.Failure<EmployeeDto>("CANNOT_RESET_SELF_PASSWORD", "请使用个人修改密码功能修改自己的密码");
        var user = await userManager.FindByIdAsync(employee.UserId.Value.ToString());
        if (user is null || user.TenantId != tenantId)
            return ResultFactory.Failure<EmployeeDto>("EMPLOYEE_ACCOUNT_NOT_FOUND", "员工登录账号不存在");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, token, command.NewInitialPassword);
        if (!reset.Succeeded)
            return await RollbackFailure(transaction, "INVALID_INITIAL_PASSWORD", "新初始密码不符合安全要求",
                cancellationToken);
        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid().ToString();
        if (!(await userManager.UpdateAsync(user)).Succeeded)
            return await RollbackFailure(transaction, "ACCOUNT_UPDATE_FAILED", "密码重置状态保存失败",
                cancellationToken);
        AddAudit(tenantId, await PrimaryStoreIdAsync(employee.Id, cancellationToken), command.OperatorId,
            "employee.password.reset", employee.Id, "CredentialActive", "MustChangePassword", JsonSerializer.Serialize(new
            {
                reason,
                passwordMaterialLogged = false,
            }));
        await db.SaveChangesAsync(cancellationToken);
        await securityEvents.RecordAsync("Merchant", user.UserName ?? string.Empty, "PasswordResetByAdmin",
            "SUCCESS", tenantId, user.Id, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ResultFactory.Success(await BuildEmployeeDto(employee, user, tenantId, cancellationToken));
    }

    private async Task<bool> HasAnotherEnabledOwnerAsync(Guid tenantId, Guid excludedUserId,
        CancellationToken cancellationToken) => await (from candidate in db.Users
        join link in db.UserRoles on candidate.Id equals link.UserId
        join role in db.Roles on link.RoleId equals role.Id
        where candidate.TenantId == tenantId && candidate.Id != excludedUserId && candidate.IsEnabled &&
            role.Name == SystemRoles.Owner
        select candidate.Id).Distinct().AnyAsync(cancellationToken);

    private async Task<bool> IsTenantOwnerAsync(Guid tenantId, Guid userId,
        CancellationToken cancellationToken) => await (from link in db.UserRoles
        join role in db.Roles on link.RoleId equals role.Id
        where link.UserId == userId && role.TenantId == tenantId && role.Name == SystemRoles.Owner
        select link.UserId).AnyAsync(cancellationToken);

    private async Task<IList<string>> GetTenantRoleNamesAsync(Guid tenantId, Guid userId,
        CancellationToken cancellationToken) => await (from link in db.UserRoles.AsNoTracking()
        join role in db.Roles.AsNoTracking() on link.RoleId equals role.Id
        where link.UserId == userId && role.TenantId == tenantId && role.Name != null
        orderby role.Name
        select role.Name!).ToListAsync(cancellationToken);

    private async Task<Guid?> PrimaryStoreIdAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.Set<EmployeeStore>().Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.IsPrimary)
            .Select(x => (Guid?)x.StoreId).FirstOrDefaultAsync(cancellationToken);

    private async Task<EmployeeDto> BuildEmployeeDtoWithoutUser(Employee employee, Guid tenantId,
        CancellationToken cancellationToken)
    {
        var stores = await db.Set<EmployeeStore>().AsNoTracking().Where(x => x.EmployeeId == employee.Id &&
                x.TenantId == tenantId)
            .Join(db.Stores.AsNoTracking(), x => x.StoreId, x => x.Id, (assignment, store) =>
                new EmployeeStoreDto(store.Id, store.Code, store.Name, assignment.IsPrimary))
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        return ToDto(employee, null, [], stores);
    }

    private async Task<EmployeeDto> BuildEmployeeDto(Employee employee, ApplicationUser user, Guid tenantId,
        CancellationToken cancellationToken)
    {
        var roles = await GetTenantRoleNamesAsync(tenantId, user.Id, cancellationToken);
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
        user?.MustChangePassword, roles, stores, employee.CreatedAtUtc, employee.Version);

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
