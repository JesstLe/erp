using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Cashier;
using Erp.Application.Common;
using Erp.Application.Security;
using Erp.Domain.Cashier;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Domain.Facilities;
using Erp.Domain.Organization;
using Erp.Infrastructure.Customers;
using Erp.Infrastructure.Persistence;
using Erp.Infrastructure.Inventory;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Erp.Infrastructure.Cashier;

internal sealed class CashierService(ErpDbContext db, InventoryPostingService inventory, TimeProvider clock,
    IHttpContextAccessor httpContextAccessor) : ICashierService
{
    public async Task<PageResult<CashierVisitDto>> ListPendingVisitsAsync(Guid tenantId, Guid storeId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Visits.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId &&
            x.Status == VisitStatus.ServiceEnded && !db.ServiceOrderVisitLinks.Any(link => link.VisitId == x.Id &&
                db.ServiceOrders.Any(order => order.Id == link.OrderId && order.Status != ServiceOrderStatus.Voided)));
        var total = await query.CountAsync(cancellationToken);
        var visits = await query.OrderBy(x => x.ServiceEndedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var visitIds = visits.Select(x => x.Id).ToList();
        var sessions = await db.FacilitySessions.AsNoTracking().Include(x => x.Pauses)
            .Where(x => visitIds.Contains(x.VisitId)).ToListAsync(cancellationToken);
        var customerIds = visits.Where(x => x.CustomerId.HasValue).Select(x => x.CustomerId!.Value).Distinct().ToList();
        var customerNames = await db.Customers.AsNoTracking()
            .Where(x => x.TenantId == tenantId && customerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var plannedServiceIds = visits.Where(x => x.PlannedServiceItemId.HasValue)
            .Select(x => x.PlannedServiceItemId!.Value).Distinct().ToList();
        var plannedServiceNames = await db.ServiceItems.AsNoTracking()
            .Where(x => x.TenantId == tenantId && plannedServiceIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var facilityIds = sessions.Select(x => x.FacilityId).Distinct().ToList();
        var facilityNames = await db.Facilities.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId && facilityIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var now = clock.GetUtcNow();
        var items = visits.Select(visit => new CashierVisitDto(visit.Id, visit.VisitNo, visit.Status.ToString(), visit.CustomerId,
            visit.CustomerId is Guid customerId ? customerNames.GetValueOrDefault(customerId, "匿名顾客") : "匿名顾客",
            visit.PlannedServiceItemId,
            visit.PlannedServiceItemId is Guid serviceItemId ? plannedServiceNames.GetValueOrDefault(serviceItemId) : null,
            string.Join(" → ", sessions.Where(x => x.VisitId == visit.Id).OrderBy(x => x.StartedAtUtc)
                .Select(x => facilityNames.GetValueOrDefault(x.FacilityId, "未知设施")).Distinct()),
            visit.ArrivedAtUtc, visit.ServiceEndedAtUtc,
            sessions.Where(x => x.VisitId == visit.Id).Sum(x => x.GetActiveSeconds(now)), visit.Note)).ToList();
        return new PageResult<CashierVisitDto>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<ServiceEmployeeDto>> ListServiceEmployeesAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken)
        => await db.Employees.AsNoTracking().Where(employee => employee.TenantId == tenantId &&
                employee.Status == EmployeeStatus.Active && db.EmployeeStores.Any(assignment =>
                    assignment.TenantId == tenantId && assignment.EmployeeId == employee.Id &&
                    assignment.StoreId == storeId))
            .OrderBy(employee => employee.DisplayName).ThenBy(employee => employee.EmployeeNo)
            .Select(employee => new ServiceEmployeeDto(employee.Id, employee.EmployeeNo, employee.DisplayName,
                employee.PositionCode, db.EmployeePositions.Where(position => position.TenantId == tenantId &&
                    position.Code == employee.PositionCode).Select(position => position.Name).FirstOrDefault() ??
                    employee.PositionCode)).ToListAsync(cancellationToken);

    public async Task<PageResult<ServiceOrderDto>> ListOrdersAsync(Guid tenantId, Guid storeId,
        ServiceOrderSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.ServiceOrders.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId);
        var term = criteria.Query?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(order => order.OrderNo.Contains(term) ||
                (order.Note != null && order.Note.Contains(term)) ||
                (order.CustomerId.HasValue && db.Customers.Any(customer => customer.Id == order.CustomerId.Value &&
                    customer.TenantId == tenantId && customer.Name.Contains(term))) ||
                order.Lines.Any(line => line.ItemCodeSnapshot.Contains(term) ||
                    line.ItemNameSnapshot.Contains(term) ||
                    (line.EmployeeNoSnapshot != null && line.EmployeeNoSnapshot.Contains(term)) ||
                    (line.EmployeeNameSnapshot != null && line.EmployeeNameSnapshot.Contains(term))));
        if (criteria.CustomerId.HasValue)
            query = query.Where(x => x.CustomerId == criteria.CustomerId.Value);
        if (criteria.CatalogItemId.HasValue)
            query = query.Where(x => x.Lines.Any(line => line.ServiceItemId == criteria.CatalogItemId.Value ||
                line.ProductItemId == criteria.CatalogItemId.Value));
        if (criteria.EmployeeId.HasValue)
            query = query.Where(x => x.Lines.Any(line => line.ServiceEmployeeId == criteria.EmployeeId.Value));
        if (!string.IsNullOrWhiteSpace(criteria.Status) && Enum.TryParse<ServiceOrderStatus>(criteria.Status, true,
                out var status)) query = query.Where(x => x.Status == status);
        if (criteria.FromDate.HasValue || criteria.ToDate.HasValue)
        {
            var timeZoneId = await db.Stores.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == storeId)
                .Select(x => x.TimeZoneId).SingleAsync(cancellationToken);
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            if (criteria.FromDate.HasValue)
            {
                var local = DateTime.SpecifyKind(criteria.FromDate.Value.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Unspecified);
                var fromUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
                query = query.Where(x => x.CreatedAtUtc >= fromUtc);
            }
            if (criteria.ToDate.HasValue)
            {
                var localExclusive = DateTime.SpecifyKind(criteria.ToDate.Value.AddDays(1)
                    .ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
                var toUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localExclusive, timeZone));
                query = query.Where(x => x.CreatedAtUtc < toUtc);
            }
        }
        var total = await query.CountAsync(cancellationToken);
        var orders = await query.Include(x => x.Lines).OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<ServiceOrderDto>(orders.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<Result<ServiceOrderDto>> GetOrderAsync(Guid tenantId, Guid storeId, Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await db.ServiceOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.Id == orderId && x.TenantId == tenantId && x.StoreId == storeId, cancellationToken);
        return order is null ? ResultFactory.Failure<ServiceOrderDto>("SERVICE_ORDER_NOT_FOUND", "消费单不存在")
            : ResultFactory.Success(ToDto(order));
    }

    public async Task<ServiceOrderDto?> GetOrderByVisitAsync(Guid tenantId, Guid storeId, Guid visitId,
        CancellationToken cancellationToken)
    {
        var orderId = await db.ServiceOrderVisitLinks.AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.VisitId == visitId)
            .Join(db.ServiceOrders.AsNoTracking().Where(order => order.StoreId == storeId &&
                    order.Status != ServiceOrderStatus.Voided), link => link.OrderId, order => order.Id,
                (_, order) => new { order.Id, order.CreatedAtUtc })
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!orderId.HasValue)
        {
            orderId = await db.ServiceOrders.AsNoTracking().Where(order => order.TenantId == tenantId &&
                    order.StoreId == storeId && order.VisitId == visitId && order.Status != ServiceOrderStatus.Voided)
                .OrderByDescending(x => x.CreatedAtUtc).Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (!orderId.HasValue) return null;
        var result = await GetOrderAsync(tenantId, storeId, orderId.Value, cancellationToken);
        return result.Value;
    }

    public async Task<Result<ServiceOrderDto>> GetOrCreateVisitDraftAsync(Guid tenantId,
        GetOrCreateVisitDraftCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty || command.VisitId == Guid.Empty)
            return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "接待草稿请求无效");
        var existing = await GetOrderByVisitAsync(tenantId, command.StoreId, command.VisitId,
            cancellationToken);
        if (existing is not null) return ResultFactory.Success(existing);
        var created = await CreateOrderAsync(tenantId, new CreateServiceOrderCommand(command.StoreId,
            command.VisitId, null, null, null, null, null, 0, null, 0, null, [], command.CommandId, command.OperatorId,
            command.OperatorRoles), cancellationToken);
        if (created.IsSuccess) return created;
        if (created.Error?.Code is "VERSION_CONFLICT" or "VISIT_ALREADY_HAS_ORDER")
        {
            db.ChangeTracker.Clear();
            existing = await GetOrderByVisitAsync(tenantId, command.StoreId, command.VisitId,
                cancellationToken);
            if (existing is not null) return ResultFactory.Success(existing);
        }
        return created;
    }

    public async Task<Result<ServiceOrderDto>> CreateOrderAsync(Guid tenantId, CreateServiceOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "缺少幂等请求号");
        if (command.Lines.Count > 100 || command.VisitId is null && command.Lines.Count == 0)
            return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "消费单需要1到100行项目或产品；设施接待可先创建空草稿");
        if (command.Lines.Any(line => !TryGetLineType(line, out _)))
            return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "每行必须且只能选择一个服务项目或产品");
        var requestHash = RequestHash(JsonSerializer.Serialize(command with
        {
            OperatorId = Guid.Empty,
            OperatorRoles = [],
        }));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, requestHash,
            id => GetOrderAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;

        try
        {
            var now = clock.GetUtcNow();
            var timeZoneId = await db.Stores.Where(x => x.Id == command.StoreId && x.TenantId == tenantId)
                .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken);
            if (timeZoneId is null) return await FailureAndRollback(transaction, "VALIDATION_FAILED", "门店时区配置无效", cancellationToken);
            var localTime = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
            var localDate = DateOnly.FromDateTime(localTime.DateTime);
            var priceBook = await db.PriceBooks.Include(x => x.Lines).Include(x => x.ProductLines)
                .Where(x => x.TenantId == tenantId &&
                    x.Status == PriceBookStatus.Published && x.EffectiveFrom <= localDate)
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.PublishedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (priceBook is null) return await FailureAndRollback(transaction, "PRICE_BOOK_NOT_FOUND", "当前日期没有已发布价格版本", cancellationToken);

            var serviceIds = command.Lines.Where(x => TryGetLineType(x, out var type) &&
                    type == ServiceOrderLineType.Service).Select(x => x.ServiceItemId!.Value).Distinct().ToList();
            var productIds = command.Lines.Where(x => TryGetLineType(x, out var type) &&
                    type == ServiceOrderLineType.Product).Select(x => x.ProductItemId!.Value).Distinct().ToList();
            var items = await db.ServiceItems.Where(x => x.TenantId == tenantId && serviceIds.Contains(x.Id) &&
                x.Status == CatalogItemStatus.Enabled).ToDictionaryAsync(x => x.Id, cancellationToken);
            if (items.Count != serviceIds.Count)
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "服务项目不存在或已停用", cancellationToken);
            var products = await db.ProductItems.Where(x => x.TenantId == tenantId && productIds.Contains(x.Id) &&
                x.Status == CatalogItemStatus.Enabled).ToDictionaryAsync(x => x.Id, cancellationToken);
            if (products.Count != productIds.Count)
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "产品不存在或已停用", cancellationToken);
            var employeeIds = command.Lines.Where(line => line.ServiceEmployeeId.HasValue)
                .Select(line => line.ServiceEmployeeId!.Value)
                .Concat(command.ConsultantEmployeeId.HasValue ? [command.ConsultantEmployeeId.Value] : [])
                .Distinct().ToList();
            var employees = await db.Employees.Where(employee => employee.TenantId == tenantId &&
                    employeeIds.Contains(employee.Id) && employee.Status == EmployeeStatus.Active &&
                    db.EmployeeStores.Any(assignment => assignment.EmployeeId == employee.Id &&
                        assignment.TenantId == tenantId && assignment.StoreId == command.StoreId))
                .ToDictionaryAsync(employee => employee.Id, cancellationToken);
            if (employees.Count != employeeIds.Count)
                return await FailureAndRollback(transaction, "SERVICE_EMPLOYEE_NOT_ELIGIBLE",
                    "所选员工不存在、已停用或不属于当前门店", cancellationToken);
            var prices = priceBook.Lines.ToDictionary(x => x.ServiceItemId, x => x.UnitPriceMinor);
            var productPrices = priceBook.ProductLines.ToDictionary(x => x.ProductItemId, x => x.UnitPriceMinor);
            if (command.Lines.Any(line => line.ServiceItemId.HasValue &&
                    !prices.ContainsKey(line.ServiceItemId.Value) && line.EnteredPriceMinor <= 0))
                return await FailureAndRollback(transaction, "VALIDATION_FAILED",
                    "未设置标准价的服务项目必须输入本次成交价", cancellationToken);
            if (command.Lines.Any(line => line.ProductItemId.HasValue &&
                    !productPrices.ContainsKey(line.ProductItemId.Value) && line.EnteredPriceMinor <= 0))
                return await FailureAndRollback(transaction, "VALIDATION_FAILED",
                    "未设置标准价的产品必须输入本次成交价", cancellationToken);

            Customer? customer = null;
            if (command.CustomerId is not null)
            {
                customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == command.CustomerId && x.TenantId == tenantId &&
                    x.Status == CustomerStatus.Active, cancellationToken);
                if (customer is null) return await FailureAndRollback(transaction, "CUSTOMER_NOT_FOUND", "顾客不存在或已停用", cancellationToken);
            }
            var memberPricing = await ResolveMemberPricingAsync(tenantId, customer?.Id, localDate,
                cancellationToken);

            Visit visit;
            if (command.VisitId is null)
            {
                visit = new Visit(tenantId, command.StoreId, CreateVisitNo(localTime), null, "服务后补录", now);
                if (customer is not null) visit.LinkCustomer(customer.Id);
                visit.EndService(now);
                db.Visits.Add(visit);
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                visit = await db.Visits.SingleOrDefaultAsync(x => x.Id == command.VisitId && x.TenantId == tenantId &&
                    x.StoreId == command.StoreId && (x.Status == VisitStatus.InService ||
                        x.Status == VisitStatus.ServiceEnded), cancellationToken)
                    ?? throw new DomainRuleException("VISIT_NOT_READY", "接待记录不存在或当前不能建立消费草稿");
                if (await db.ServiceOrders.AnyAsync(x => x.VisitId == visit.Id && x.Status != ServiceOrderStatus.Voided, cancellationToken))
                    return await FailureAndRollback(transaction, "VISIT_ALREADY_HAS_ORDER", "该接待已经存在消费单", cancellationToken);
                if (customer is not null && visit.CustomerId != customer.Id)
                {
                    var previous = visit.CustomerId?.ToString();
                    visit.LinkCustomer(customer.Id);
                    AddAudit(tenantId, command.StoreId, command.OperatorId, "visit.customer.link", "Visit", visit.Id,
                        previous, customer.Id.ToString(), command.CommandId, now);
                }
            }

            var drafts = command.Lines.Select(line =>
            {
                _ = TryGetLineType(line, out var type);
                if (type == ServiceOrderLineType.Service)
                {
                    var id = line.ServiceItemId!.Value;
                    var item = items[id];
                    Employee? employee = line.ServiceEmployeeId.HasValue ? employees[line.ServiceEmployeeId.Value] : null;
                    if (item.CommissionMode != CommissionMode.None && employee is null)
                        throw new DomainRuleException("SERVICE_EMPLOYEE_REQUIRED", "已设置提成的服务项目必须选择服务员工");
                    var referencePrice = prices.GetValueOrDefault(id);
                    var pricing = ResolveLinePricing(referencePrice, line.EnteredPriceMinor,
                        line.PriceOverrideReason, line.PricingSource, false, memberPricing);
                    return new ServiceOrderLineDraft(id, items[id].Code, items[id].Name, line.Quantity,
                        line.ActualSeconds, referencePrice, pricing.EnteredPriceMinor, pricing.Reason,
                        employee?.Id, employee?.EmployeeNo, employee?.DisplayName, item.CommissionMode,
                        item.CommissionRateBasisPoints, item.CommissionFixedMinor, pricing.Source,
                        pricing.DiscountBasisPoints, pricing.CardTypeId, pricing.CardTypeName);
                }
                var productId = line.ProductItemId!.Value;
                var product = products[productId];
                Employee? addedByEmployee = line.ServiceEmployeeId.HasValue
                    ? employees[line.ServiceEmployeeId.Value]
                    : null;
                var productReferencePrice = productPrices.GetValueOrDefault(productId);
                var productPricing = ResolveLinePricing(productReferencePrice, line.EnteredPriceMinor,
                    line.PriceOverrideReason, line.PricingSource, true, memberPricing);
                return ServiceOrderLineDraft.Product(productId, product.Code, product.Name, product.UnitName,
                    line.Quantity, productReferencePrice, productPricing.EnteredPriceMinor, productPricing.Reason,
                    addedByEmployee?.Id, addedByEmployee?.EmployeeNo, addedByEmployee?.DisplayName,
                    productPricing.Source, productPricing.DiscountBasisPoints, productPricing.CardTypeId,
                    productPricing.CardTypeName);
            }).ToList();
            var consultant = command.ConsultantEmployeeId.HasValue
                ? employees[command.ConsultantEmployeeId.Value]
                : null;
            var order = new ServiceOrder(tenantId, command.StoreId, visit.Id, customer?.Id ?? visit.CustomerId,
                CreateOrderNo(localTime), priceBook.Id, command.Note, drafts, consultant?.Id,
                consultant?.EmployeeNo, consultant?.DisplayName, new ServiceOrderReceptionDraft(
                    command.SourceChannel, command.ManualTicketNo, command.MaleGuestCount,
                    command.MaleAgeBand, command.FemaleGuestCount, command.FemaleAgeBand));
            db.ServiceOrders.Add(order);
            db.ServiceOrderVisitLinks.Add(new ServiceOrderVisitLink(tenantId, order.Id, visit.Id));
            if (order.HasPriceOverride)
            {
                var role = ResolvePriceRole(command.OperatorRoles);
                if (role is null)
                    return await FailureAndRollback(transaction, "PRICE_OVERRIDE_FORBIDDEN",
                        "当前角色无权提交改价消费单", cancellationToken);
                var policy = await GetOrAddActivePricePolicyAsync(tenantId, command.OperatorId, now,
                    cancellationToken);
                var canAuthorizeDirectly = role == SystemRoles.Owner ||
                    role == SystemRoles.StoreManager && !policy.ManagerRequiresApproval(order);
                if (canAuthorizeDirectly)
                {
                    order.AuthorizePriceDirectly(policy.Id, policy.PolicyVersion, command.OperatorId, now);
                    AddAudit(tenantId, command.StoreId, command.OperatorId,
                        "service_order.price.direct_authorized", "ServiceOrder", order.Id, null,
                        order.PriceAuthorizationStatus.ToString(), command.CommandId, now,
                        PriceAuditReason(order, policy, role));
                }
                else
                {
                    order.RequestPriceApproval(policy.Id, policy.PolicyVersion);
                    var approval = new PriceOverrideApproval(tenantId, command.StoreId, order.Id,
                        command.OperatorId, role, policy.Id, policy.PolicyVersion, order.ReferenceAmountMinor,
                        order.ReceivableMinor, order.MaximumLineDiscountBasisPoints,
                        policy.ManagerLineDiscountBasisPoints, policy.ManagerOrderDiscountMinor,
                        policy.AllowManagerPriceIncrease, now);
                    db.PriceOverrideApprovals.Add(approval);
                    AddAudit(tenantId, command.StoreId, command.OperatorId,
                        "service_order.price.approval_requested", "PriceOverrideApproval", approval.Id,
                        null, approval.Status.ToString(), command.CommandId, now,
                        PriceAuditReason(order, policy, role));
                }
            }
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, order.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "service_order.create", "ServiceOrder", order.Id,
                null, order.Status.ToString(), command.CommandId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(order));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>("VERSION_CONFLICT", "接待或消费单状态已变化，请刷新后重试");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>("VERSION_CONFLICT", "消费单已由其他终端创建，请刷新后重试");
        }
    }

    public async Task<Result<ServiceOrderDto>> UpdateDraftAsync(Guid tenantId,
        UpdateServiceOrderDraftCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty || command.Lines.Count > 100)
            return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "消费单草稿请求无效");
        if (command.Lines.Any(line => !TryGetLineType(line, out _)))
            return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "每行必须且只能选择一个服务项目或产品");
        var requestHash = RequestHash(JsonSerializer.Serialize(command with
        {
            OperatorId = Guid.Empty,
            OperatorRoles = [],
        }));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, requestHash,
            id => GetOrderAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var order = await db.ServiceOrders.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.Id == command.OrderId && x.TenantId == tenantId && x.StoreId == command.StoreId,
                cancellationToken);
            if (order is null)
                return await FailureAndRollback(transaction, "SERVICE_ORDER_NOT_FOUND", "消费单不存在",
                    cancellationToken);
            if (order.Version != command.ExpectedVersion)
                return await FailureAndRollback(transaction, "VERSION_CONFLICT", "消费单草稿已被其他终端修改",
                    cancellationToken);
            if (order.Status != ServiceOrderStatus.Draft)
                return await FailureAndRollback(transaction, "STATE_TRANSITION_NOT_ALLOWED",
                    "只有草稿消费单可以编辑", cancellationToken);

            Customer? customer = null;
            if (command.CustomerId.HasValue)
            {
                customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == command.CustomerId.Value &&
                    x.TenantId == tenantId && x.Status == CustomerStatus.Active, cancellationToken);
                if (customer is null)
                    return await FailureAndRollback(transaction, "CUSTOMER_NOT_FOUND", "顾客不存在或已停用",
                        cancellationToken);
            }

            var prepared = await PrepareDraftAsync(tenantId, command.StoreId, order.PriceBookId,
                customer?.Id, command.ConsultantEmployeeId, command.Lines, cancellationToken);
            var now = clock.GetUtcNow();
            var oldLines = order.Lines.ToList();
            var pendingApproval = await db.PriceOverrideApprovals.SingleOrDefaultAsync(x =>
                x.ServiceOrderId == order.Id && x.TenantId == tenantId &&
                x.Status == PriceOverrideApprovalStatus.Pending, cancellationToken);
            pendingApproval?.Cancel(now, "消费单草稿已修改，原改价申请自动取消");
            order.ReplaceDraft(customer?.Id, command.Note, prepared.Lines, prepared.Consultant?.Id,
                prepared.Consultant?.EmployeeNo, prepared.Consultant?.DisplayName,
                new ServiceOrderReceptionDraft(command.SourceChannel, command.ManualTicketNo,
                    command.MaleGuestCount, command.MaleAgeBand, command.FemaleGuestCount,
                    command.FemaleAgeBand));
            db.ServiceOrderLines.RemoveRange(oldLines);
            await ApplyPriceAuthorizationAsync(tenantId, command.StoreId, order, command.OperatorId,
                command.OperatorRoles, command.CommandId, now, cancellationToken);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, order.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "service_order.draft.update",
                "ServiceOrder", order.Id, command.ExpectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Draft", command.CommandId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(order));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception) ||
                                           exception is DbUpdateConcurrencyException)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>("VERSION_CONFLICT",
                "消费单草稿已被其他终端修改，请刷新后重试");
        }
    }

    public async Task<Result<ServiceOrderDto>> MergeDraftAsync(Guid tenantId,
        MergeServiceOrderDraftCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty || command.SourceOrderId == command.TargetOrderId)
            return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "合并账单请求无效");
        var requestHash = RequestHash(JsonSerializer.Serialize(command with
        {
            OperatorId = Guid.Empty,
            OperatorRoles = [],
        }));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, requestHash,
            id => GetOrderAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var orders = await db.ServiceOrders.Include(x => x.Lines).Where(x =>
                    x.TenantId == tenantId && x.StoreId == command.StoreId &&
                    (x.Id == command.TargetOrderId || x.Id == command.SourceOrderId))
                .ToListAsync(cancellationToken);
            var target = orders.SingleOrDefault(x => x.Id == command.TargetOrderId);
            var source = orders.SingleOrDefault(x => x.Id == command.SourceOrderId);
            if (target is null || source is null)
                return await FailureAndRollback(transaction, "SERVICE_ORDER_NOT_FOUND", "待合并账单不存在",
                    cancellationToken);
            if (target.Status != ServiceOrderStatus.Draft || source.Status != ServiceOrderStatus.Draft)
                return await FailureAndRollback(transaction, "STATE_TRANSITION_NOT_ALLOWED",
                    "只有两张草稿账单可以合并", cancellationToken);
            if (target.Version != command.ExpectedTargetVersion || source.Version != command.ExpectedSourceVersion)
                return await FailureAndRollback(transaction, "VERSION_CONFLICT", "待合并账单已经发生变化",
                    cancellationToken);
            if (target.CustomerId.HasValue && source.CustomerId.HasValue && target.CustomerId != source.CustomerId)
                return await FailureAndRollback(transaction, "MERGE_CUSTOMER_CONFLICT",
                    "两张账单关联了不同顾客，请先核对顾客后再合并", cancellationToken);
            if (target.ConsultantEmployeeId.HasValue && source.ConsultantEmployeeId.HasValue &&
                target.ConsultantEmployeeId != source.ConsultantEmployeeId)
                return await FailureAndRollback(transaction, "MERGE_CONSULTANT_CONFLICT",
                    "两张账单的整单顾问不同，请先统一顾问后再合并", cancellationToken);

            var commands = target.Lines.Concat(source.Lines).Select(ToCommand).ToList();
            if (commands.Count > 100)
                return await FailureAndRollback(transaction, "VALIDATION_FAILED",
                    "合并后账单明细不能超过100行", cancellationToken);
            var consultantId = target.ConsultantEmployeeId ?? source.ConsultantEmployeeId;
            var prepared = await PrepareDraftAsync(tenantId, command.StoreId, target.PriceBookId,
                target.CustomerId ?? source.CustomerId, consultantId, commands, cancellationToken);
            var targetOldLines = target.Lines.ToList();
            var now = clock.GetUtcNow();
            var approvals = await db.PriceOverrideApprovals.Where(x => x.TenantId == tenantId &&
                    (x.ServiceOrderId == target.Id || x.ServiceOrderId == source.Id) &&
                    x.Status == PriceOverrideApprovalStatus.Pending)
                .ToListAsync(cancellationToken);
            foreach (var approval in approvals)
                approval.Cancel(now, "消费单草稿已合并，原改价申请自动取消");
            var mergedNote = string.Join("；", new[] { target.Note, source.Note }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
            target.ReplaceDraft(target.CustomerId ?? source.CustomerId,
                string.IsNullOrWhiteSpace(mergedNote) ? null : mergedNote, prepared.Lines,
                prepared.Consultant?.Id, prepared.Consultant?.EmployeeNo,
                prepared.Consultant?.DisplayName, new ServiceOrderReceptionDraft(
                    MergeText(target.SourceChannel, source.SourceChannel, "多渠道"),
                    target.ManualTicketNo ?? source.ManualTicketNo,
                    Math.Min(99, target.MaleGuestCount + source.MaleGuestCount),
                    MergeText(target.MaleAgeBand, source.MaleAgeBand, "多年龄段"),
                    Math.Min(99, target.FemaleGuestCount + source.FemaleGuestCount),
                    MergeText(target.FemaleAgeBand, source.FemaleAgeBand, "多年龄段")));
            db.ServiceOrderLines.RemoveRange(targetOldLines);
            source.Void();

            var targetVisitIds = await db.ServiceOrderVisitLinks.Where(x => x.OrderId == target.Id)
                .Select(x => x.VisitId).ToListAsync(cancellationToken);
            var sourceVisitIds = await db.ServiceOrderVisitLinks.Where(x => x.OrderId == source.Id)
                .Select(x => x.VisitId).ToListAsync(cancellationToken);
            if (sourceVisitIds.Count == 0) sourceVisitIds.Add(source.VisitId);
            foreach (var visitId in sourceVisitIds.Where(x => !targetVisitIds.Contains(x)))
                db.ServiceOrderVisitLinks.Add(new ServiceOrderVisitLink(tenantId, target.Id, visitId));

            await ApplyPriceAuthorizationAsync(tenantId, command.StoreId, target, command.OperatorId,
                command.OperatorRoles, command.CommandId, now, cancellationToken);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, target.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "service_order.draft.merge",
                "ServiceOrder", target.Id, source.OrderNo, target.OrderNo, command.CommandId, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "service_order.merge.source_void",
                "ServiceOrder", source.Id, "Draft", "Voided", command.CommandId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(target));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception) ||
                                           exception is DbUpdateConcurrencyException)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>("VERSION_CONFLICT",
                "待合并账单已经发生变化，请刷新后重试");
        }
    }

    public async Task<Result<ServiceOrderPrebillDto>> CreatePrebillAsync(Guid tenantId, Guid storeId,
        Guid orderId, uint expectedVersion, Guid commandId, Guid operatorId,
        CancellationToken cancellationToken)
    {
        if (commandId == Guid.Empty)
            return ResultFactory.Failure<ServiceOrderPrebillDto>("VALIDATION_FAILED", "预结请求无效");
        var requestHash = RequestHash($"PREBILL|{storeId}|{orderId}|{expectedVersion}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayPrebillAsync(tenantId, storeId, commandId, requestHash, cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var order = await db.ServiceOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.Id == orderId && x.TenantId == tenantId && x.StoreId == storeId, cancellationToken);
            if (order is null)
                return await FailureAndRollback<ServiceOrderPrebillDto>(transaction,
                    "SERVICE_ORDER_NOT_FOUND", "消费单不存在", cancellationToken);
            if (order.Version != expectedVersion)
                return await FailureAndRollback<ServiceOrderPrebillDto>(transaction, "VERSION_CONFLICT",
                    "消费单草稿已发生变化", cancellationToken);
            if (order.Status is not (ServiceOrderStatus.Draft or ServiceOrderStatus.PendingPayment))
                return await FailureAndRollback<ServiceOrderPrebillDto>(transaction,
                    "STATE_TRANSITION_NOT_ALLOWED", "当前消费单不能生成预结单", cancellationToken);
            if (order.Lines.Count == 0)
                return await FailureAndRollback<ServiceOrderPrebillDto>(transaction, "VALIDATION_FAILED",
                    "请先添加项目或产品", cancellationToken);

            var storeName = await db.Stores.AsNoTracking().Where(x => x.Id == storeId && x.TenantId == tenantId)
                .Select(x => x.Name).SingleAsync(cancellationToken);
            var customerName = order.CustomerId.HasValue
                ? await db.Customers.AsNoTracking().Where(x => x.Id == order.CustomerId.Value &&
                        x.TenantId == tenantId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken)
                    ?? "已停用顾客"
                : "散客";
            var now = clock.GetUtcNow();
            var localTime = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(
                await db.Stores.Where(x => x.Id == storeId).Select(x => x.TimeZoneId)
                    .SingleAsync(cancellationToken)));
            var prebillNo = $"PB{localTime:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..32].ToUpperInvariant();
            var lines = order.Lines.OrderBy(x => x.CreatedAtUtc).Select(x =>
                new ServiceOrderPrebillLineDto(x.LineType.ToString(), x.ItemCodeSnapshot,
                    x.ItemNameSnapshot, x.UnitNameSnapshot, x.Quantity, x.EnteredPriceMinor,
                    x.LineAmountMinor, x.EmployeeNameSnapshot)).ToList();
            var payload = new PrebillPayload(order.OrderNo, storeName, customerName,
                order.ConsultantEmployeeNameSnapshot, order.ReceivableMinor, now, lines);
            var snapshot = new ServiceOrderPrebillSnapshot(tenantId, storeId, order.Id, prebillNo,
                JsonSerializer.Serialize(payload), operatorId, now);
            db.ServiceOrderPrebillSnapshots.Add(snapshot);
            AddReceipt(tenantId, commandId, operatorId, requestHash, snapshot.Id, now);
            AddAudit(tenantId, storeId, operatorId, "service_order.prebill.generate", "ServiceOrder",
                order.Id, null, prebillNo, commandId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToPrebillDto(snapshot.Id, snapshot.PrebillNo, order.Id, payload));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderPrebillDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderPrebillDto>("VERSION_CONFLICT",
                "消费单或预结记录已发生变化，请刷新后重试");
        }
    }

    public async Task<Result<ServiceOrderDto>> ConfirmOrderAsync(Guid tenantId, ConfirmServiceOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var requestHash = RequestHash($"ORDER_CONFIRM|{command.StoreId}|{command.OrderId}|{command.ExpectedVersion}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, requestHash,
            id => GetOrderAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var order = await db.ServiceOrders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == command.OrderId &&
                x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
            if (order is null) return await FailureAndRollback(transaction, "SERVICE_ORDER_NOT_FOUND", "消费单不存在", cancellationToken);
            if (order.Version != command.ExpectedVersion) return await FailureAndRollback(transaction, "VERSION_CONFLICT", "消费单已被修改，请刷新后重试", cancellationToken);
            var linkedVisitIds = await db.ServiceOrderVisitLinks.AsNoTracking()
                .Where(x => x.OrderId == order.Id && x.TenantId == tenantId)
                .Select(x => x.VisitId).ToListAsync(cancellationToken);
            if (linkedVisitIds.Count == 0) linkedVisitIds.Add(order.VisitId);
            if (await db.Visits.AnyAsync(x => linkedVisitIds.Contains(x.Id) &&
                    x.Status != VisitStatus.ServiceEnded, cancellationToken))
                return await FailureAndRollback(transaction, "VISIT_NOT_READY",
                    "请先结束当前账单关联的全部设施服务，再确认收款金额", cancellationToken);
            var previous = order.Status.ToString();
            var now = clock.GetUtcNow();
            if (order.HasPriceOverride && order.PriceAuthorizationStatus is not
                (PriceAuthorizationState.DirectAuthorized or PriceAuthorizationState.Approved))
                return await FailureAndRollback(transaction, "PRICE_APPROVAL_REQUIRED",
                    "成交价尚未获得有效授权，不能确认收款金额", cancellationToken);
            await inventory.ReserveOrderAsync(order, now, cancellationToken);
            order.Confirm(now);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, order.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "service_order.confirm", "ServiceOrder", order.Id,
                previous, order.Status.ToString(), command.CommandId, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(order));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>("VERSION_CONFLICT", "消费单状态已变化，请刷新后重试");
        }
    }

    public async Task<Result<ServiceOrderDto>> VoidOrderAsync(Guid tenantId, VoidServiceOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var reason = command.Reason?.Trim();
        if (reason?.Length is not (>= 2 and <= 500))
            return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "作废原因必须为2到500字");
        var requestHash = RequestHash($"ORDER_VOID|{command.StoreId}|{command.OrderId}|{command.ExpectedVersion}|{reason}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayAsync(tenantId, command.CommandId, requestHash,
            id => GetOrderAsync(tenantId, command.StoreId, id, cancellationToken), cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var order = await db.ServiceOrders.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.Id == command.OrderId && x.TenantId == tenantId && x.StoreId == command.StoreId,
                cancellationToken);
            if (order is null)
                return await FailureAndRollback(transaction, "SERVICE_ORDER_NOT_FOUND", "消费单不存在",
                    cancellationToken);
            if (order.Version != command.ExpectedVersion)
                return await FailureAndRollback(transaction, "VERSION_CONFLICT", "消费单已被修改，请刷新后重试",
                    cancellationToken);
            var previous = order.Status.ToString();
            var now = clock.GetUtcNow();
            await inventory.ReleaseOrderAsync(order, now, cancellationToken);
            var approval = await db.PriceOverrideApprovals.SingleOrDefaultAsync(x =>
                x.ServiceOrderId == order.Id && x.TenantId == tenantId, cancellationToken);
            approval?.Cancel(now);
            order.Void();
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, order.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "service_order.void", "ServiceOrder",
                order.Id, previous, order.Status.ToString(), command.CommandId, now, reason);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(order));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<ServiceOrderDto>("VERSION_CONFLICT", "消费单或库存状态已变化，请刷新后重试");
        }
    }

    public async Task<PriceOverridePolicyDto> GetPriceOverridePolicyAsync(Guid tenantId, Guid operatorId,
        CancellationToken cancellationToken)
    {
        var policy = await db.PriceOverridePolicies.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == tenantId && x.IsActive, cancellationToken);
        if (policy is not null) return ToDto(policy);
        var baseline = PriceOverridePolicy.Default(tenantId, operatorId, clock.GetUtcNow());
        db.PriceOverridePolicies.Add(baseline);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ToDto(baseline);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
            policy = await db.PriceOverridePolicies.AsNoTracking().SingleAsync(x =>
                x.TenantId == tenantId && x.IsActive, cancellationToken);
            return ToDto(policy);
        }
    }

    public async Task<Result<PriceOverridePolicyDto>> UpdatePriceOverridePolicyAsync(Guid tenantId,
        UpdatePriceOverridePolicyCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty || command.ManagerLineDiscountBasisPoints is < 0 or > 10_000 ||
            command.ManagerOrderDiscountMinor is < 0 or > 10_000_000_000)
            return ResultFactory.Failure<PriceOverridePolicyDto>("VALIDATION_FAILED", "改价策略参数不完整");
        var requestHash = RequestHash($"PRICE_POLICY_UPDATE|{command.StoreId}|" +
            $"{command.ManagerLineDiscountBasisPoints}|{command.ManagerOrderDiscountMinor}|" +
            $"{command.AllowManagerPriceIncrease}|{command.ExpectedVersion}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayPolicyAsync(tenantId, command.CommandId, requestHash, cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var current = await GetOrAddActivePricePolicyAsync(tenantId, command.OperatorId, clock.GetUtcNow(),
                cancellationToken);
            if (current.Version != command.ExpectedVersion)
                return await FailureAndRollback<PriceOverridePolicyDto>(transaction, "VERSION_CONFLICT",
                    "改价策略已变化，请刷新后重试", cancellationToken);
            var now = clock.GetUtcNow();
            var previousState = PolicyAuditState(current);
            current.Retire();
            var next = new PriceOverridePolicy(tenantId, current.PolicyVersion + 1,
                command.ManagerLineDiscountBasisPoints, command.ManagerOrderDiscountMinor,
                command.AllowManagerPriceIncrease, command.OperatorId, now);
            db.PriceOverridePolicies.Add(next);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, next.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "price_override_policy.publish",
                "PriceOverridePolicy", next.Id, previousState, PolicyAuditState(next),
                command.CommandId, now, "发布新版本后只影响新建消费单，历史审批继续使用原策略快照");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToDto(next));
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<PriceOverridePolicyDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception) || IsUniqueViolation(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<PriceOverridePolicyDto>("VERSION_CONFLICT",
                "改价策略已由其他终端更新，请刷新后重试");
        }
    }

    public async Task<PageResult<PriceOverrideApprovalDto>> ListPriceOverrideApprovalsAsync(Guid tenantId,
        Guid storeId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.PriceOverrideApprovals.AsNoTracking().Where(x =>
            x.TenantId == tenantId && x.StoreId == storeId);
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<PriceOverrideApprovalStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);
        var total = await query.CountAsync(cancellationToken);
        var approvals = await query.OrderByDescending(x => x.RequestedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<PriceOverrideApprovalDto>(await ToApprovalDtosAsync(approvals, cancellationToken),
            total, page, pageSize);
    }

    public Task<Result<PriceOverrideApprovalDto>> ApprovePriceOverrideAsync(Guid tenantId,
        DecidePriceOverrideApprovalCommand command, CancellationToken cancellationToken) =>
        DecidePriceOverrideAsync(tenantId, command, approve: true, cancellationToken);

    public Task<Result<PriceOverrideApprovalDto>> RejectPriceOverrideAsync(Guid tenantId,
        DecidePriceOverrideApprovalCommand command, CancellationToken cancellationToken) =>
        DecidePriceOverrideAsync(tenantId, command, approve: false, cancellationToken);

    private async Task<Result<PriceOverrideApprovalDto>> DecidePriceOverrideAsync(Guid tenantId,
        DecidePriceOverrideApprovalCommand command, bool approve, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty || !approve && command.Note?.Trim().Length is not (>= 2 and <= 500))
            return ResultFactory.Failure<PriceOverrideApprovalDto>("VALIDATION_FAILED",
                approve ? "缺少幂等请求号" : "驳回原因必须为2到500字");
        var action = approve ? "APPROVE" : "REJECT";
        var requestHash = RequestHash($"PRICE_APPROVAL_{action}|{command.StoreId}|{command.ApprovalId}|" +
            $"{command.ExpectedVersion}|{command.Note?.Trim()}");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayApprovalAsync(tenantId, command.CommandId, requestHash, cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var approval = await db.PriceOverrideApprovals.SingleOrDefaultAsync(x =>
                x.Id == command.ApprovalId && x.TenantId == tenantId && x.StoreId == command.StoreId,
                cancellationToken);
            if (approval is null)
                return await FailureAndRollback<PriceOverrideApprovalDto>(transaction,
                    "PRICE_APPROVAL_NOT_FOUND", "改价审批不存在", cancellationToken);
            if (approval.Version != command.ExpectedVersion)
                return await FailureAndRollback<PriceOverrideApprovalDto>(transaction, "VERSION_CONFLICT",
                    "改价审批已被处理，请刷新后重试", cancellationToken);
            var order = await db.ServiceOrders.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.Id == approval.ServiceOrderId && x.TenantId == tenantId && x.StoreId == command.StoreId,
                cancellationToken);
            if (order is null)
                return await FailureAndRollback<PriceOverrideApprovalDto>(transaction,
                    "SERVICE_ORDER_NOT_FOUND", "关联消费单不存在", cancellationToken);
            var now = clock.GetUtcNow();
            var previous = approval.Status.ToString();
            if (approve)
            {
                approval.Approve(command.ApproverId, command.Note, now);
                order.ApprovePriceOverride(command.ApproverId, now);
            }
            else
            {
                approval.Reject(command.ApproverId, command.Note!, now);
                order.RejectPriceOverride();
            }
            AddReceipt(tenantId, command.CommandId, command.ApproverId, requestHash, approval.Id, now);
            AddAudit(tenantId, command.StoreId, command.ApproverId,
                approve ? "service_order.price.approval_approved" : "service_order.price.approval_rejected",
                "PriceOverrideApproval", approval.Id, previous, approval.Status.ToString(), command.CommandId,
                now, command.Note);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success((await ToApprovalDtosAsync([approval], cancellationToken)).Single());
        }
        catch (DomainRuleException exception)
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<PriceOverrideApprovalDto>(exception.Code, exception.Message);
        }
        catch (Exception exception) when (IsDatabaseConcurrencyConflict(exception))
        {
            await RollbackIfActiveAsync(transaction, cancellationToken);
            return ResultFactory.Failure<PriceOverrideApprovalDto>("VERSION_CONFLICT",
                "改价审批或消费单状态已变化，请刷新后重试");
        }
    }

    private static ServiceOrderDto ToDto(ServiceOrder order) => new(order.Id, order.OrderNo, order.VisitId,
        order.CustomerId, order.ConsultantEmployeeId, order.ConsultantEmployeeNoSnapshot,
        order.ConsultantEmployeeNameSnapshot, order.Status.ToString(), order.SourceChannel,
        order.ManualTicketNo, order.MaleGuestCount, order.MaleAgeBand, order.FemaleGuestCount,
        order.FemaleAgeBand, order.PriceBookId,
        order.ReferenceAmountMinor, order.ReceivableMinor,
        order.RefundedMinor, order.Note, order.Version, order.CreatedAtUtc,
        order.PriceAuthorizationStatus.ToString(), order.PricePolicyId, order.PricePolicyVersion,
        order.PriceAuthorizedBy, order.PriceAuthorizedAtUtc, order.Lines.OrderBy(x => x.CreatedAtUtc).Select(x =>
            new ServiceOrderLineDto(x.Id, x.LineType.ToString(), x.ServiceItemId, x.ProductItemId,
                x.ItemCodeSnapshot, x.ItemNameSnapshot, x.UnitNameSnapshot, x.Quantity, x.ReturnedQuantity,
                x.ActualSeconds, x.ReferencePriceMinor, x.EnteredPriceMinor, x.LineAmountMinor,
                x.PriceOverrideReason, x.ServiceEmployeeId, x.EmployeeNoSnapshot,
                x.EmployeeNameSnapshot, x.PricingSource.ToString(), x.MemberDiscountBasisPoints,
                x.MemberCardTypeId, x.MemberCardTypeNameSnapshot)).ToList());

    private static PriceOverridePolicyDto ToDto(PriceOverridePolicy policy) => new(policy.Id,
        policy.PolicyVersion, policy.ManagerLineDiscountBasisPoints, policy.ManagerOrderDiscountMinor,
        policy.AllowManagerPriceIncrease, policy.EffectiveFromUtc, policy.Version);

    private async Task<IReadOnlyList<PriceOverrideApprovalDto>> ToApprovalDtosAsync(
        IReadOnlyList<PriceOverrideApproval> approvals, CancellationToken cancellationToken)
    {
        var orderIds = approvals.Select(x => x.ServiceOrderId).Distinct().ToList();
        var userIds = approvals.Select(x => x.RequesterId).Concat(approvals.Where(x => x.DecidedBy.HasValue)
            .Select(x => x.DecidedBy!.Value)).Distinct().ToList();
        var orderNos = await db.ServiceOrders.AsNoTracking().Where(x => orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.OrderNo, cancellationToken);
        var userNames = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        return approvals.Select(x => new PriceOverrideApprovalDto(x.Id, x.ServiceOrderId,
            orderNos.GetValueOrDefault(x.ServiceOrderId, "未知消费单"), x.Status.ToString(), x.RequesterId,
            userNames.GetValueOrDefault(x.RequesterId, "未知员工"), x.RequesterRoleSnapshot, x.PolicyId,
            x.PolicyVersion, x.ReferenceAmountMinor, x.ReceivableMinor, x.DifferenceMinor,
            x.MaximumLineDiscountBasisPoints, x.ManagerLineDiscountBasisPoints,
            x.ManagerOrderDiscountMinor, x.AllowManagerPriceIncrease, x.RequestedAtUtc, x.DecidedBy,
            x.DecidedBy.HasValue ? userNames.GetValueOrDefault(x.DecidedBy.Value, "未知员工") : null,
            x.DecidedAtUtc, x.DecisionNote, x.Version)).ToList();
    }

    private static bool TryGetLineType(CreateServiceOrderLineCommand line, out ServiceOrderLineType type)
    {
        type = ServiceOrderLineType.Service;
        var explicitType = line.LineType?.Trim();
        if (explicitType is not null &&
            !Enum.TryParse<ServiceOrderLineType>(explicitType, ignoreCase: true, out type)) return false;
        if (explicitType is null)
            type = line.ProductItemId.HasValue ? ServiceOrderLineType.Product : ServiceOrderLineType.Service;
        return type switch
        {
            ServiceOrderLineType.Service => line.ServiceItemId.HasValue && !line.ProductItemId.HasValue,
            ServiceOrderLineType.Product => line.ProductItemId.HasValue && !line.ServiceItemId.HasValue,
            _ => false,
        };
    }

    private static string? MergeText(string? left, string? right, string conflictValue)
    {
        if (string.IsNullOrWhiteSpace(left)) return string.IsNullOrWhiteSpace(right) ? null : right;
        if (string.IsNullOrWhiteSpace(right) || string.Equals(left, right, StringComparison.Ordinal)) return left;
        return conflictValue;
    }

    private async Task<PreparedDraft> PrepareDraftAsync(Guid tenantId, Guid storeId, Guid? priceBookId,
        Guid? customerId, Guid? consultantEmployeeId, IReadOnlyList<CreateServiceOrderLineCommand> commands,
        CancellationToken cancellationToken)
    {
        if (commands.Count > 100 || commands.Any(line => !TryGetLineType(line, out _)))
            throw new DomainRuleException("VALIDATION_FAILED", "消费单草稿明细无效");
        var store = await db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.Id == storeId &&
            x.TenantId == tenantId, cancellationToken)
            ?? throw new DomainRuleException("VALIDATION_FAILED", "门店不存在");
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),
            TimeZoneInfo.FindSystemTimeZoneById(store.TimeZoneId)).DateTime);
        PriceBook? priceBook = null;
        if (priceBookId.HasValue)
            priceBook = await db.PriceBooks.Include(x => x.Lines).Include(x => x.ProductLines)
                .SingleOrDefaultAsync(x => x.Id == priceBookId.Value && x.TenantId == tenantId,
                    cancellationToken);
        if (priceBook is null)
        {
            priceBook = await db.PriceBooks.Include(x => x.Lines).Include(x => x.ProductLines)
                .Where(x => x.TenantId == tenantId && x.Status == PriceBookStatus.Published &&
                    x.EffectiveFrom <= localDate)
                .OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.PublishedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DomainRuleException("PRICE_BOOK_NOT_FOUND", "当前日期没有已发布价格版本");
        }

        var serviceIds = commands.Where(x => TryGetLineType(x, out var type) &&
                type == ServiceOrderLineType.Service).Select(x => x.ServiceItemId!.Value).Distinct().ToList();
        var productIds = commands.Where(x => TryGetLineType(x, out var type) &&
                type == ServiceOrderLineType.Product).Select(x => x.ProductItemId!.Value).Distinct().ToList();
        var items = await db.ServiceItems.Where(x => x.TenantId == tenantId && serviceIds.Contains(x.Id) &&
                x.Status == CatalogItemStatus.Enabled).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (items.Count != serviceIds.Count)
            throw new DomainRuleException("VALIDATION_FAILED", "服务项目不存在或已停用");
        var products = await db.ProductItems.Where(x => x.TenantId == tenantId && productIds.Contains(x.Id) &&
                x.Status == CatalogItemStatus.Enabled).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (products.Count != productIds.Count)
            throw new DomainRuleException("VALIDATION_FAILED", "产品不存在或已停用");
        var prices = priceBook.Lines.ToDictionary(x => x.ServiceItemId, x => x.UnitPriceMinor);
        var productPrices = priceBook.ProductLines.ToDictionary(x => x.ProductItemId, x => x.UnitPriceMinor);
        var memberPricing = await ResolveMemberPricingAsync(tenantId, customerId, localDate,
            cancellationToken);
        if (commands.Any(line => line.ServiceItemId.HasValue &&
                !prices.ContainsKey(line.ServiceItemId.Value) && line.EnteredPriceMinor <= 0))
            throw new DomainRuleException("VALIDATION_FAILED", "未设置标准价的服务项目必须输入本次成交价");
        if (commands.Any(line => line.ProductItemId.HasValue &&
                !productPrices.ContainsKey(line.ProductItemId.Value) && line.EnteredPriceMinor <= 0))
            throw new DomainRuleException("VALIDATION_FAILED", "未设置标准价的产品必须输入本次成交价");

        var employeeIds = commands.Where(x => x.ServiceEmployeeId.HasValue)
            .Select(x => x.ServiceEmployeeId!.Value)
            .Concat(consultantEmployeeId.HasValue ? [consultantEmployeeId.Value] : [])
            .Distinct().ToList();
        var employees = await db.Employees.Where(employee => employee.TenantId == tenantId &&
                employeeIds.Contains(employee.Id) && employee.Status == EmployeeStatus.Active &&
                db.EmployeeStores.Any(assignment => assignment.EmployeeId == employee.Id &&
                    assignment.TenantId == tenantId && assignment.StoreId == storeId))
            .ToDictionaryAsync(employee => employee.Id, cancellationToken);
        if (employees.Count != employeeIds.Count)
            throw new DomainRuleException("SERVICE_EMPLOYEE_NOT_ELIGIBLE",
                "所选员工不存在、已停用或不属于当前门店");

        var lines = commands.Select(line =>
        {
            _ = TryGetLineType(line, out var type);
            if (type == ServiceOrderLineType.Service)
            {
                var id = line.ServiceItemId!.Value;
                var item = items[id];
                Employee? employee = line.ServiceEmployeeId.HasValue
                    ? employees[line.ServiceEmployeeId.Value]
                    : null;
                if (item.CommissionMode != CommissionMode.None && employee is null)
                    throw new DomainRuleException("SERVICE_EMPLOYEE_REQUIRED",
                        "已设置提成的服务项目必须选择服务员工");
                var referencePrice = prices.GetValueOrDefault(id);
                var pricing = ResolveLinePricing(referencePrice, line.EnteredPriceMinor,
                    line.PriceOverrideReason, line.PricingSource, false, memberPricing);
                return new ServiceOrderLineDraft(id, item.Code, item.Name, line.Quantity,
                    line.ActualSeconds, referencePrice, pricing.EnteredPriceMinor, pricing.Reason,
                    employee?.Id, employee?.EmployeeNo, employee?.DisplayName, item.CommissionMode,
                    item.CommissionRateBasisPoints, item.CommissionFixedMinor, pricing.Source,
                    pricing.DiscountBasisPoints, pricing.CardTypeId, pricing.CardTypeName);
            }
            var productId = line.ProductItemId!.Value;
            var product = products[productId];
            Employee? addedByEmployee = line.ServiceEmployeeId.HasValue
                ? employees[line.ServiceEmployeeId.Value]
                : null;
            var productReferencePrice = productPrices.GetValueOrDefault(productId);
            var productPricing = ResolveLinePricing(productReferencePrice, line.EnteredPriceMinor,
                line.PriceOverrideReason, line.PricingSource, true, memberPricing);
            return ServiceOrderLineDraft.Product(productId, product.Code, product.Name, product.UnitName,
                line.Quantity, productReferencePrice, productPricing.EnteredPriceMinor, productPricing.Reason,
                addedByEmployee?.Id, addedByEmployee?.EmployeeNo, addedByEmployee?.DisplayName,
                productPricing.Source, productPricing.DiscountBasisPoints, productPricing.CardTypeId,
                productPricing.CardTypeName);
        }).ToList();
        return new PreparedDraft(priceBook.Id, lines, consultantEmployeeId.HasValue
            ? employees[consultantEmployeeId.Value]
            : null);
    }

    private async Task ApplyPriceAuthorizationAsync(Guid tenantId, Guid storeId, ServiceOrder order,
        Guid operatorId, IReadOnlyList<string> operatorRoles, Guid commandId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!order.HasPriceOverride) return;
        var role = ResolvePriceRole(operatorRoles)
            ?? throw new DomainRuleException("PRICE_OVERRIDE_FORBIDDEN", "当前角色无权提交改价消费单");
        var policy = await GetOrAddActivePricePolicyAsync(tenantId, operatorId, now, cancellationToken);
        var canAuthorizeDirectly = role == SystemRoles.Owner || role == SystemRoles.StoreManager &&
            !policy.ManagerRequiresApproval(order);
        if (canAuthorizeDirectly)
        {
            order.AuthorizePriceDirectly(policy.Id, policy.PolicyVersion, operatorId, now);
            AddAudit(tenantId, storeId, operatorId, "service_order.price.direct_authorized",
                "ServiceOrder", order.Id, null, order.PriceAuthorizationStatus.ToString(), commandId, now,
                PriceAuditReason(order, policy, role));
            return;
        }
        order.RequestPriceApproval(policy.Id, policy.PolicyVersion);
        var approval = new PriceOverrideApproval(tenantId, storeId, order.Id, operatorId, role,
            policy.Id, policy.PolicyVersion, order.ReferenceAmountMinor, order.ReceivableMinor,
            order.MaximumLineDiscountBasisPoints, policy.ManagerLineDiscountBasisPoints,
            policy.ManagerOrderDiscountMinor, policy.AllowManagerPriceIncrease, now);
        db.PriceOverrideApprovals.Add(approval);
        AddAudit(tenantId, storeId, operatorId, "service_order.price.approval_requested",
            "PriceOverrideApproval", approval.Id, null, approval.Status.ToString(), commandId, now,
            PriceAuditReason(order, policy, role));
    }

    private async Task<Result<ServiceOrderPrebillDto>?> ReplayPrebillAsync(Guid tenantId, Guid storeId,
        Guid commandId, byte[] requestHash, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId ||
            !CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
            return ResultFactory.Failure<ServiceOrderPrebillDto>("IDEMPOTENCY_CONFLICT",
                "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null
            ? null
            : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        if (receipt is null)
            return ResultFactory.Failure<ServiceOrderPrebillDto>("COMMAND_IN_PROGRESS",
                "请求正在处理，请稍后刷新");
        var snapshot = await db.ServiceOrderPrebillSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == receipt.EntityId && x.TenantId == tenantId && x.StoreId == storeId, cancellationToken);
        if (snapshot is null)
            return ResultFactory.Failure<ServiceOrderPrebillDto>("PREBILL_NOT_FOUND", "预结单不存在");
        var payload = JsonSerializer.Deserialize<PrebillPayload>(snapshot.PayloadJson)
            ?? throw new DomainRuleException("INVARIANT_VIOLATION", "预结快照内容无效");
        return ResultFactory.Success(ToPrebillDto(snapshot.Id, snapshot.PrebillNo, snapshot.OrderId,
            payload));
    }

    private static ServiceOrderPrebillDto ToPrebillDto(Guid id, string prebillNo, Guid orderId,
        PrebillPayload payload) => new(id, prebillNo, orderId, payload.OrderNo, payload.StoreName,
        payload.CustomerDisplayName, payload.ConsultantEmployeeName, payload.ReceivableMinor,
        payload.GeneratedAtUtc, payload.Lines);

    private static CreateServiceOrderLineCommand ToCommand(ServiceOrderLine line) => new(
        line.LineType.ToString(), line.ServiceItemId, line.ProductItemId, line.ServiceEmployeeId,
        line.Quantity, line.ActualSeconds, line.EnteredPriceMinor, line.PriceOverrideReason,
        line.PricingSource.ToString());

    private async Task<Result<ServiceOrderDto>?> ReplayAsync(Guid tenantId, Guid commandId, byte[] requestHash,
        Func<Guid, Task<Result<ServiceOrderDto>>> load, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x => x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
            return ResultFactory.Failure<ServiceOrderDto>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null ? null : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        return receipt is null ? ResultFactory.Failure<ServiceOrderDto>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新") : await load(receipt.EntityId);
    }

    private async Task<Result<PriceOverridePolicyDto>?> ReplayPolicyAsync(Guid tenantId, Guid commandId,
        byte[] requestHash, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId ||
            !CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
            return ResultFactory.Failure<PriceOverridePolicyDto>("IDEMPOTENCY_CONFLICT",
                "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null
            ? null
            : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        if (receipt is null)
            return ResultFactory.Failure<PriceOverridePolicyDto>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新");
        var policy = await db.PriceOverridePolicies.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == receipt.EntityId && x.TenantId == tenantId, cancellationToken);
        return policy is null
            ? ResultFactory.Failure<PriceOverridePolicyDto>("PRICE_POLICY_NOT_FOUND", "改价策略不存在")
            : ResultFactory.Success(ToDto(policy));
    }

    private async Task<Result<PriceOverrideApprovalDto>?> ReplayApprovalAsync(Guid tenantId, Guid commandId,
        byte[] requestHash, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId ||
            !CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
            return ResultFactory.Failure<PriceOverrideApprovalDto>("IDEMPOTENCY_CONFLICT",
                "幂等请求号已被其他请求使用");
        var receipt = existing.ResponseBody is null
            ? null
            : JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody);
        if (receipt is null)
            return ResultFactory.Failure<PriceOverrideApprovalDto>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新");
        var approval = await db.PriceOverrideApprovals.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == receipt.EntityId && x.TenantId == tenantId, cancellationToken);
        return approval is null
            ? ResultFactory.Failure<PriceOverrideApprovalDto>("PRICE_APPROVAL_NOT_FOUND", "改价审批不存在")
            : ResultFactory.Success((await ToApprovalDtosAsync([approval], cancellationToken)).Single());
    }

    private async Task<PriceOverridePolicy> GetOrAddActivePricePolicyAsync(Guid tenantId, Guid createdBy,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var policy = await db.PriceOverridePolicies.SingleOrDefaultAsync(x =>
            x.TenantId == tenantId && x.IsActive, cancellationToken);
        if (policy is not null) return policy;
        policy = PriceOverridePolicy.Default(tenantId, createdBy, now);
        db.PriceOverridePolicies.Add(policy);
        return policy;
    }

    private static string? ResolvePriceRole(IReadOnlyList<string> roles)
    {
        if (roles.Contains(SystemRoles.Owner, StringComparer.OrdinalIgnoreCase)) return SystemRoles.Owner;
        if (roles.Contains(SystemRoles.StoreManager, StringComparer.OrdinalIgnoreCase)) return SystemRoles.StoreManager;
        if (roles.Contains(SystemRoles.Cashier, StringComparer.OrdinalIgnoreCase)) return SystemRoles.Cashier;
        return null;
    }

    private static string PriceAuditReason(ServiceOrder order, PriceOverridePolicy policy, string role) =>
        JsonSerializer.Serialize(new
        {
            role,
            policyId = policy.Id,
            policyVersion = policy.PolicyVersion,
            referenceAmountMinor = order.ReferenceAmountMinor,
            receivableMinor = order.ReceivableMinor,
            differenceMinor = order.ReceivableMinor - order.ReferenceAmountMinor,
            maximumLineDiscountBasisPoints = order.MaximumLineDiscountBasisPoints,
            managerLineDiscountBasisPoints = policy.ManagerLineDiscountBasisPoints,
            managerOrderDiscountMinor = policy.ManagerOrderDiscountMinor,
            policy.AllowManagerPriceIncrease,
        });

    private static string PolicyAuditState(PriceOverridePolicy policy) => JsonSerializer.Serialize(new
    {
        policy.Id,
        policy.PolicyVersion,
        policy.ManagerLineDiscountBasisPoints,
        policy.ManagerOrderDiscountMinor,
        policy.AllowManagerPriceIncrease,
        policy.IsActive,
    });

    private static async Task<Result<ServiceOrderDto>> FailureAndRollback(IDbContextTransaction transaction, string code,
        string message, CancellationToken cancellationToken)
    {
        await RollbackIfActiveAsync(transaction, cancellationToken);
        return ResultFactory.Failure<ServiceOrderDto>(code, message);
    }

    private static async Task<Result<T>> FailureAndRollback<T>(IDbContextTransaction transaction, string code,
        string message, CancellationToken cancellationToken)
    {
        await RollbackIfActiveAsync(transaction, cancellationToken);
        return ResultFactory.Failure<T>(code, message);
    }

    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] requestHash, Guid entityId, DateTimeOffset now) =>
        db.IdempotencyCommands.Add(new IdempotencyCommandRecord { CommandId = commandId, TenantId = tenantId,
            OperatorId = operatorId, RequestHash = requestHash, ResponseStatus = 200,
            ResponseBody = JsonSerializer.Serialize(new CommandReceipt(entityId)), CreatedAtUtc = now, CompletedAtUtc = now });

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, string entityType, Guid entityId,
        string? previous, string? current, Guid commandId, DateTimeOffset now, string? reason = null) => db.AuditEvents.Add(new AuditEventRecord
        { TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action, EntityType = entityType,
            EntityId = entityId, PreviousState = previous, CurrentState = current, RequestId = commandId, Reason = reason,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = now });

    private static async Task RollbackIfActiveAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    { try { await transaction.RollbackAsync(cancellationToken); } catch (InvalidOperationException) { } }
    private static byte[] RequestHash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static bool IsUniqueViolation(Exception exception) => FindPostgres(exception)?.SqlState == PostgresErrorCodes.UniqueViolation;
    private static bool IsDatabaseConcurrencyConflict(Exception exception) => FindPostgres(exception)?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;
    private static PostgresException? FindPostgres(Exception exception)
    { for (Exception? current = exception; current is not null; current = current.InnerException) if (current is PostgresException postgres) return postgres; return null; }
    private static string CreateVisitNo(DateTimeOffset storeLocalTime) => $"V{storeLocalTime:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..30].ToUpperInvariant();
    private static string CreateOrderNo(DateTimeOffset storeLocalTime) => $"SO{storeLocalTime:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..32].ToUpperInvariant();
    private async Task<IReadOnlyList<MemberPricingOption>> ResolveMemberPricingAsync(Guid tenantId,
        Guid? customerId, DateOnly localDate, CancellationToken cancellationToken)
    {
        if (!customerId.HasValue) return [];
        return await (from card in db.MemberCards.AsNoTracking()
            join cardType in db.MemberCardTypes.AsNoTracking() on card.CardTypeId equals cardType.Id
            where card.TenantId == tenantId && card.CustomerId == customerId.Value &&
                  card.Status == MemberCardStatus.Active && card.ValidFrom <= localDate &&
                  (!card.ValidTo.HasValue || card.ValidTo.Value >= localDate) &&
                  cardType.TenantId == tenantId && cardType.Status == MemberCardTypeStatus.Published
            select new MemberPricingOption(cardType.Id, cardType.Name,
                cardType.ServiceDiscountBasisPoints, cardType.ProductDiscountBasisPoints))
            .ToListAsync(cancellationToken);
    }

    private static LinePricingDecision ResolveLinePricing(long referencePriceMinor,
        long enteredPriceMinor, string? reason, string? requestedPricingSource, bool product,
        IReadOnlyList<MemberPricingOption> memberPricing)
    {
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ServiceOrderLinePricingSource? requestedSource = null;
        if (!string.IsNullOrWhiteSpace(requestedPricingSource))
        {
            if (!Enum.TryParse<ServiceOrderLinePricingSource>(requestedPricingSource, true,
                    out var parsedSource))
                throw new DomainRuleException("VALIDATION_FAILED", "价格来源无效");
            requestedSource = parsedSource;
        }
        var inheritedMemberPrice = requestedSource == ServiceOrderLinePricingSource.MemberDiscount ||
            requestedSource is null && normalizedReason?.StartsWith("会员折扣：",
                StringComparison.Ordinal) == true;
        var explicitManualOverride = requestedSource == ServiceOrderLinePricingSource.ManualOverride;
        var option = memberPricing.Where(x => (product ? x.ProductDiscountBasisPoints :
                x.ServiceDiscountBasisPoints) < 10_000)
            .OrderBy(x => product ? x.ProductDiscountBasisPoints : x.ServiceDiscountBasisPoints)
            .ThenBy(x => x.Name).FirstOrDefault();
        if (referencePriceMinor > 0 && option is not null)
        {
            var basisPoints = product ? option.ProductDiscountBasisPoints :
                option.ServiceDiscountBasisPoints;
            var memberPrice = checked((referencePriceMinor * basisPoints + 5_000) / 10_000);
            if (!explicitManualOverride && (enteredPriceMinor == referencePriceMinor || enteredPriceMinor == memberPrice ||
                inheritedMemberPrice))
            {
                var discountText = (basisPoints / 1_000m).ToString("0.##",
                    CultureInfo.InvariantCulture);
                return new LinePricingDecision(memberPrice,
                    $"会员折扣：{option.Name} {discountText}折",
                    ServiceOrderLinePricingSource.MemberDiscount, basisPoints, option.Id, option.Name);
            }
        }
        if (!explicitManualOverride && (inheritedMemberPrice || enteredPriceMinor == referencePriceMinor))
            return new LinePricingDecision(referencePriceMinor, null,
                ServiceOrderLinePricingSource.ListPrice, null, null, null);
        return new LinePricingDecision(enteredPriceMinor, normalizedReason,
            ServiceOrderLinePricingSource.ManualOverride, null, null, null);
    }

    private sealed record MemberPricingOption(Guid Id, string Name, int ServiceDiscountBasisPoints,
        int ProductDiscountBasisPoints);
    private sealed record LinePricingDecision(long EnteredPriceMinor, string? Reason,
        ServiceOrderLinePricingSource Source, int? DiscountBasisPoints, Guid? CardTypeId,
        string? CardTypeName);
    private sealed record PreparedDraft(Guid PriceBookId, IReadOnlyList<ServiceOrderLineDraft> Lines,
        Employee? Consultant);
    private sealed record PrebillPayload(string OrderNo, string StoreName, string CustomerDisplayName,
        string? ConsultantEmployeeName, long ReceivableMinor, DateTimeOffset GeneratedAtUtc,
        IReadOnlyList<ServiceOrderPrebillLineDto> Lines);
    private sealed record CommandReceipt(Guid EntityId);
}
