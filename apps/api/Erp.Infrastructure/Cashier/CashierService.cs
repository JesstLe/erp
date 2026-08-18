using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Cashier;
using Erp.Application.Common;
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
    public async Task<IReadOnlyList<CashierVisitDto>> ListPendingVisitsAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken)
    {
        var visits = await db.Visits.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId &&
            x.Status == VisitStatus.ServiceEnded && !db.ServiceOrders.Any(order => order.VisitId == x.Id && order.Status != ServiceOrderStatus.Voided))
            .OrderBy(x => x.ServiceEndedAtUtc).Take(100).ToListAsync(cancellationToken);
        var visitIds = visits.Select(x => x.Id).ToList();
        var sessions = await db.FacilitySessions.AsNoTracking().Include(x => x.Pauses)
            .Where(x => visitIds.Contains(x.VisitId)).ToListAsync(cancellationToken);
        var customerIds = visits.Where(x => x.CustomerId.HasValue).Select(x => x.CustomerId!.Value).Distinct().ToList();
        var rawCustomerNames = await db.Customers.AsNoTracking()
            .Where(x => x.TenantId == tenantId && customerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var customerNames = rawCustomerNames.ToDictionary(x => x.Key, x => CustomerPrivacyService.MaskName(x.Value));
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
        return visits.Select(visit => new CashierVisitDto(visit.Id, visit.VisitNo, visit.Status.ToString(), visit.CustomerId,
            visit.CustomerId is Guid customerId ? customerNames.GetValueOrDefault(customerId, "匿名顾客") : "匿名顾客",
            visit.PlannedServiceItemId,
            visit.PlannedServiceItemId is Guid serviceItemId ? plannedServiceNames.GetValueOrDefault(serviceItemId) : null,
            string.Join(" → ", sessions.Where(x => x.VisitId == visit.Id).OrderBy(x => x.StartedAtUtc)
                .Select(x => facilityNames.GetValueOrDefault(x.FacilityId, "未知设施")).Distinct()),
            visit.ArrivedAtUtc, visit.ServiceEndedAtUtc,
            sessions.Where(x => x.VisitId == visit.Id).Sum(x => x.GetActiveSeconds(now)), visit.Note)).ToList();
    }

    public async Task<IReadOnlyList<ServiceEmployeeDto>> ListServiceEmployeesAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken)
        => await db.Employees.AsNoTracking().Where(employee => employee.TenantId == tenantId &&
                employee.Status == EmployeeStatus.Active && db.EmployeeStores.Any(assignment =>
                    assignment.TenantId == tenantId && assignment.EmployeeId == employee.Id &&
                    assignment.StoreId == storeId))
            .OrderBy(employee => employee.DisplayName).ThenBy(employee => employee.EmployeeNo)
            .Select(employee => new ServiceEmployeeDto(employee.Id, employee.EmployeeNo, employee.DisplayName,
                employee.PositionCode)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ServiceOrderDto>> ListOrdersAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken)
    {
        var orders = await db.ServiceOrders.AsNoTracking().Include(x => x.Lines)
            .Where(x => x.TenantId == tenantId && x.StoreId == storeId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
        return orders.Select(ToDto).ToList();
    }

    public async Task<Result<ServiceOrderDto>> GetOrderAsync(Guid tenantId, Guid storeId, Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await db.ServiceOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x =>
            x.Id == orderId && x.TenantId == tenantId && x.StoreId == storeId, cancellationToken);
        return order is null ? ResultFactory.Failure<ServiceOrderDto>("SERVICE_ORDER_NOT_FOUND", "消费单不存在")
            : ResultFactory.Success(ToDto(order));
    }

    public async Task<Result<ServiceOrderDto>> CreateOrderAsync(Guid tenantId, CreateServiceOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty) return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "缺少幂等请求号");
        if (command.Lines.Count is 0 or > 100) return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "消费单需要1到100行项目或产品");
        if (command.Lines.Any(line => !TryGetLineType(line, out _)))
            return ResultFactory.Failure<ServiceOrderDto>("VALIDATION_FAILED", "每行必须且只能选择一个服务项目或产品");
        var requestHash = RequestHash(JsonSerializer.Serialize(command with { OperatorId = Guid.Empty }));
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
                    type == ServiceOrderLineType.Service).Select(x => x.ServiceItemId!.Value).ToList();
            var productIds = command.Lines.Where(x => TryGetLineType(x, out var type) &&
                    type == ServiceOrderLineType.Product).Select(x => x.ProductItemId!.Value).ToList();
            if (serviceIds.Distinct().Count() != serviceIds.Count || productIds.Distinct().Count() != productIds.Count)
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "同一服务项目或产品不能重复录入，请调整数量", cancellationToken);
            if (command.Lines.Any(line => TryGetLineType(line, out var type) &&
                    type == ServiceOrderLineType.Product && line.ServiceEmployeeId.HasValue))
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "商品明细不能选择服务员工",
                    cancellationToken);
            var items = await db.ServiceItems.Where(x => x.TenantId == tenantId && serviceIds.Contains(x.Id) &&
                x.Status == CatalogItemStatus.Enabled).ToDictionaryAsync(x => x.Id, cancellationToken);
            if (items.Count != serviceIds.Count)
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "服务项目不存在或已停用", cancellationToken);
            var products = await db.ProductItems.Where(x => x.TenantId == tenantId && productIds.Contains(x.Id) &&
                x.Status == CatalogItemStatus.Enabled).ToDictionaryAsync(x => x.Id, cancellationToken);
            if (products.Count != productIds.Count)
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "产品不存在或已停用", cancellationToken);
            var employeeIds = command.Lines.Where(line => line.ServiceEmployeeId.HasValue)
                .Select(line => line.ServiceEmployeeId!.Value).Distinct().ToList();
            var employees = await db.Employees.Where(employee => employee.TenantId == tenantId &&
                    employeeIds.Contains(employee.Id) && employee.Status == EmployeeStatus.Active &&
                    db.EmployeeStores.Any(assignment => assignment.EmployeeId == employee.Id &&
                        assignment.TenantId == tenantId && assignment.StoreId == command.StoreId))
                .ToDictionaryAsync(employee => employee.Id, cancellationToken);
            if (employees.Count != employeeIds.Count)
                return await FailureAndRollback(transaction, "SERVICE_EMPLOYEE_NOT_ELIGIBLE",
                    "所选服务员工不存在、已停用或不属于当前门店", cancellationToken);
            var prices = priceBook.Lines.ToDictionary(x => x.ServiceItemId, x => x.UnitPriceMinor);
            var productPrices = priceBook.ProductLines.ToDictionary(x => x.ProductItemId, x => x.UnitPriceMinor);
            if (serviceIds.Any(id => !prices.ContainsKey(id)))
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "已发布价格版本缺少所选项目价格", cancellationToken);
            if (productIds.Any(id => !productPrices.ContainsKey(id)))
                return await FailureAndRollback(transaction, "VALIDATION_FAILED", "已发布价格版本缺少所选产品价格", cancellationToken);

            Customer? customer = null;
            if (command.CustomerId is not null)
            {
                customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == command.CustomerId && x.TenantId == tenantId &&
                    x.HomeStoreId == command.StoreId && x.Status == CustomerStatus.Active, cancellationToken);
                if (customer is null) return await FailureAndRollback(transaction, "CUSTOMER_NOT_FOUND", "顾客不存在或不属于当前门店", cancellationToken);
            }

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
                    x.StoreId == command.StoreId && x.Status == VisitStatus.ServiceEnded, cancellationToken)
                    ?? throw new DomainRuleException("VISIT_NOT_READY", "接待记录不存在或服务尚未结束");
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
                    return new ServiceOrderLineDraft(id, items[id].Code, items[id].Name, line.Quantity,
                        line.ActualSeconds, prices[id], line.EnteredPriceMinor, line.PriceOverrideReason,
                        employee?.Id, employee?.EmployeeNo, employee?.DisplayName, item.CommissionMode,
                        item.CommissionRateBasisPoints, item.CommissionFixedMinor);
                }
                var productId = line.ProductItemId!.Value;
                var product = products[productId];
                return ServiceOrderLineDraft.Product(productId, product.Code, product.Name, product.UnitName,
                    line.Quantity, productPrices[productId], line.EnteredPriceMinor, line.PriceOverrideReason);
            }).ToList();
            var order = new ServiceOrder(tenantId, command.StoreId, visit.Id, customer?.Id ?? visit.CustomerId,
                CreateOrderNo(localTime), priceBook.Id, command.Note, drafts);
            db.ServiceOrders.Add(order);
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
            var previous = order.Status.ToString();
            var now = clock.GetUtcNow();
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

    private static ServiceOrderDto ToDto(ServiceOrder order) => new(order.Id, order.OrderNo, order.VisitId,
        order.CustomerId, order.Status.ToString(), order.PriceBookId, order.ReferenceAmountMinor, order.ReceivableMinor,
        order.RefundedMinor, order.Note, order.Version, order.CreatedAtUtc, order.Lines.OrderBy(x => x.CreatedAtUtc).Select(x =>
            new ServiceOrderLineDto(x.Id, x.LineType.ToString(), x.ServiceItemId, x.ProductItemId,
                x.ItemCodeSnapshot, x.ItemNameSnapshot, x.UnitNameSnapshot, x.Quantity, x.ReturnedQuantity,
                x.ActualSeconds, x.ReferencePriceMinor, x.EnteredPriceMinor, x.LineAmountMinor,
                x.PriceOverrideReason, x.ServiceEmployeeId, x.EmployeeNoSnapshot,
                x.EmployeeNameSnapshot)).ToList());

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

    private static async Task<Result<ServiceOrderDto>> FailureAndRollback(IDbContextTransaction transaction, string code,
        string message, CancellationToken cancellationToken)
    {
        await RollbackIfActiveAsync(transaction, cancellationToken);
        return ResultFactory.Failure<ServiceOrderDto>(code, message);
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
    private sealed record CommandReceipt(Guid EntityId);
}
