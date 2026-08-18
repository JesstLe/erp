using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Common;
using Erp.Application.Inventory;
using Erp.Domain.Cashier;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Domain.Inventory;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Erp.Infrastructure.Inventory;

internal sealed class InventoryPostingService(ErpDbContext db, TimeProvider clock,
    IHttpContextAccessor httpContextAccessor) : IInventoryService
{
    public async Task<IReadOnlyList<InventoryBalanceDto>> ListBalancesAsync(Guid tenantId, Guid storeId,
        CancellationToken cancellationToken)
    {
        var products = await db.ProductItems.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Code).ToListAsync(cancellationToken);
        var balances = await db.InventoryBalances.AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.StoreId == storeId).ToDictionaryAsync(x => x.ProductItemId, cancellationToken);
        return products.Select(product =>
        {
            balances.TryGetValue(product.Id, out var balance);
            return new InventoryBalanceDto(product.Id, product.Code, product.Name, product.UnitName,
                product.TrackInventory, balance?.OnHandQuantity ?? 0, balance?.ReservedQuantity ?? 0,
                balance?.AvailableQuantity ?? 0, balance?.Version ?? 0);
        }).ToList();
    }

    public async Task<PageResult<InventoryMovementDto>> ListMovementsAsync(Guid tenantId, Guid storeId,
        Guid? productItemId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.InventoryMovements.AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.StoreId == storeId);
        if (productItemId.HasValue) query = query.Where(x => x.ProductItemId == productItemId.Value);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var productIds = rows.Select(x => x.ProductItemId).Distinct().ToList();
        var products = await db.ProductItems.AsNoTracking().Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var items = rows.Select(row =>
        {
            var product = products[row.ProductItemId];
            return new InventoryMovementDto(row.Id, row.ProductItemId, product.Code, product.Name,
                product.UnitName, row.MovementType.ToString(), row.Direction.ToString(), row.Quantity,
                row.OnHandBefore, row.OnHandAfter, row.SourceType, row.SourceId, row.SourceLineId,
                row.CommandId, row.OperatorId, row.OccurredAtUtc);
        }).ToList();
        return new PageResult<InventoryMovementDto>(items, total, page, pageSize);
    }

    public async Task<PageResult<InventoryDocumentDto>> ListDocumentsAsync(Guid tenantId, Guid storeId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.InventoryDocuments.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId);
        var total = await query.CountAsync(cancellationToken);
        var documents = await query.Include(x => x.Lines).OrderByDescending(x => x.PostedAtUtc)
            .ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<InventoryDocumentDto>(await ToDocumentDtosAsync(documents, cancellationToken),
            total, page, pageSize);
    }

    public async Task<Result<InventoryDocumentDto>> PostDocumentAsync(Guid tenantId,
        PostInventoryDocumentCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return Failure<InventoryDocumentDto>("VALIDATION_FAILED", "缺少幂等请求号");
        if (!Enum.TryParse<InventoryDocumentType>(command.DocumentType, true, out var documentType))
            return Failure<InventoryDocumentDto>("VALIDATION_FAILED", "库存单据类型无效");
        var hash = Hash(JsonSerializer.Serialize(command with { OperatorId = Guid.Empty }));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayDocumentAsync(tenantId, command, hash, cancellationToken);
        if (replay is not null) return replay;
        try
        {
            if (command.Lines.Count is 0 or > 100 ||
                command.Lines.Select(x => x.ProductItemId).Distinct().Count() != command.Lines.Count)
                return await RollbackFailure<InventoryDocumentDto>(transaction, "VALIDATION_FAILED",
                    "库存单据需要1到100行且不能重复产品", cancellationToken);
            var productIds = command.Lines.Select(x => x.ProductItemId).ToList();
            var products = await db.ProductItems.Where(x => x.TenantId == tenantId &&
                    productIds.Contains(x.Id) && x.Status == CatalogItemStatus.Enabled && x.TrackInventory)
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            if (products.Count != productIds.Count)
                return await RollbackFailure<InventoryDocumentDto>(transaction, "INVENTORY_PRODUCT_NOT_TRACKED",
                    "库存单据只能选择已启用且跟踪库存的产品", cancellationToken);
            var now = clock.GetUtcNow();
            var document = new InventoryDocument(tenantId, command.StoreId, CreateDocumentNo(now),
                documentType, command.Reason, command.OperatorId, now,
                command.Lines.Select(x => (x.ProductItemId, x.Quantity)));
            db.InventoryDocuments.Add(document);
            foreach (var line in document.Lines)
            {
                var balance = await GetOrCreateBalanceAsync(tenantId, command.StoreId, line.ProductItemId,
                    cancellationToken);
                if (documentType == InventoryDocumentType.Opening &&
                    await db.InventoryMovements.AnyAsync(x => x.BalanceId == balance.Id, cancellationToken))
                    throw new DomainRuleException("INVENTORY_OPENING_ALREADY_POSTED", "已有库存流水的产品不能再次录入期初库存");
                var before = balance.OnHandQuantity;
                var direction = documentType == InventoryDocumentType.AdjustmentOut
                    ? InventoryMovementDirection.Out : InventoryMovementDirection.In;
                if (direction == InventoryMovementDirection.Out) balance.AdjustOut(line.Quantity);
                else balance.Receive(line.Quantity);
                var movement = new InventoryMovement(tenantId, command.StoreId,
                    line.ProductItemId, balance.Id, ToMovementType(documentType), direction, line.Quantity,
                    before, balance.OnHandQuantity, "InventoryDocument", document.Id, line.Id,
                    command.CommandId, command.OperatorId, now);
                db.InventoryMovements.Add(movement);
                if (direction == InventoryMovementDirection.In)
                    AddInboundLot(tenantId, command.StoreId, line.ProductItemId,
                        $"{documentType.ToString().ToUpperInvariant()}-{document.DocumentNo}", line.Quantity,
                        "InventoryDocumentLine", line.Id, movement);
                else await IssueLotsAsync(tenantId, command.StoreId, line.ProductItemId, line.Quantity,
                    movement, cancellationToken);
            }
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, document.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "inventory.document.post",
                "InventoryDocument", document.Id, null, documentType.ToString(), command.CommandId,
                command.Reason, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var dto = (await ToDocumentDtosAsync([document], cancellationToken))[0];
            return ResultFactory.Success(dto);
        }
        catch (DomainRuleException exception)
        {
            return await DomainFailure<InventoryDocumentDto>(transaction, exception, cancellationToken);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await RollbackQuietly(transaction, cancellationToken);
            return Failure<InventoryDocumentDto>("VERSION_CONFLICT", "库存已被其他终端修改，请刷新后重试");
        }
    }

    public async Task<Result<ProductReturnDto>> ReturnProductAsync(Guid tenantId, ReturnProductCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return Failure<ProductReturnDto>("VALIDATION_FAILED", "缺少幂等请求号");
        var hash = Hash(JsonSerializer.Serialize(command with { OperatorId = Guid.Empty }));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        var replay = await ReplayReturnAsync(tenantId, command, hash, cancellationToken);
        if (replay is not null) return replay;
        try
        {
            var order = await db.ServiceOrders.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.Id == command.OrderId && x.TenantId == tenantId && x.StoreId == command.StoreId,
                cancellationToken);
            if (order is null)
                return await RollbackFailure<ProductReturnDto>(transaction, "SERVICE_ORDER_NOT_FOUND",
                    "消费单不存在", cancellationToken);
            if (order.Version != command.ExpectedOrderVersion)
                return await RollbackFailure<ProductReturnDto>(transaction, "VERSION_CONFLICT",
                    "消费单已变化，请刷新后重试", cancellationToken);
            if (order.Status is not (ServiceOrderStatus.Settled or ServiceOrderStatus.PartiallyRefunded or
                    ServiceOrderStatus.Refunded))
                return await RollbackFailure<ProductReturnDto>(transaction, "STATE_TRANSITION_NOT_ALLOWED",
                    "只有已结算消费单可以登记产品退货", cancellationToken);
            var line = order.Lines.SingleOrDefault(x => x.Id == command.OrderLineId &&
                x.LineType == ServiceOrderLineType.Product);
            if (line?.ProductItemId is null)
                return await RollbackFailure<ProductReturnDto>(transaction, "PRODUCT_ORDER_LINE_NOT_FOUND",
                    "产品销售明细不存在", cancellationToken);
            var product = await db.ProductItems.SingleAsync(x => x.Id == line.ProductItemId.Value,
                cancellationToken);
            line.ApplyProductReturn(command.Quantity);
            var now = clock.GetUtcNow();
            var productReturn = new ProductReturn(tenantId, command.StoreId, order.Id, line.Id,
                product.Id, command.Quantity, command.Reason, command.CommandId, command.OperatorId, now);
            db.ProductReturns.Add(productReturn);
            if (product.TrackInventory)
            {
                var balance = await GetOrCreateBalanceAsync(tenantId, command.StoreId, product.Id,
                    cancellationToken);
                var before = balance.OnHandQuantity;
                balance.Receive(command.Quantity);
                var movement = new InventoryMovement(tenantId, command.StoreId, product.Id,
                    balance.Id, InventoryMovementType.SalesReturn, InventoryMovementDirection.In,
                    command.Quantity, before, balance.OnHandQuantity, "ProductReturn", productReturn.Id,
                    productReturn.Id, command.CommandId, command.OperatorId, now);
                db.InventoryMovements.Add(movement);
                AddInboundLot(tenantId, command.StoreId, product.Id,
                    $"RETURN-{productReturn.Id:N}", command.Quantity, "ProductReturn", productReturn.Id,
                    movement);
            }
            AddReceipt(tenantId, command.CommandId, command.OperatorId, hash, productReturn.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "inventory.product.return",
                "ProductReturn", productReturn.Id, null,
                command.Quantity.ToString(CultureInfo.InvariantCulture), command.CommandId,
                command.Reason, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToReturnDto(productReturn, product));
        }
        catch (DomainRuleException exception)
        {
            return await DomainFailure<ProductReturnDto>(transaction, exception, cancellationToken);
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await RollbackQuietly(transaction, cancellationToken);
            return Failure<ProductReturnDto>("VERSION_CONFLICT", "退货或库存状态已变化，请刷新后重试");
        }
    }

    internal async Task ReserveOrderAsync(ServiceOrder order, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var productLines = order.Lines.Where(x => x.LineType == ServiceOrderLineType.Product &&
            x.ProductItemId.HasValue).ToList();
        if (productLines.Count == 0) return;
        var productIds = productLines.Select(x => x.ProductItemId!.Value).ToList();
        var tracked = await db.ProductItems.Where(x => x.TenantId == order.TenantId &&
                productIds.Contains(x.Id) && x.TrackInventory)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        foreach (var line in productLines.Where(x => tracked.ContainsKey(x.ProductItemId!.Value)))
        {
            var productId = line.ProductItemId!.Value;
            var balance = await GetOrCreateBalanceAsync(order.TenantId, order.StoreId, productId,
                cancellationToken);
            balance.Reserve(line.Quantity);
            db.InventoryReservations.Add(new InventoryReservation(order.TenantId, order.StoreId, order.Id,
                line.Id, productId, balance.Id, line.Quantity, now));
        }
    }

    internal async Task ConsumeOrderAsync(ServiceOrder order, Guid commandId, Guid? operatorId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reservations = await db.InventoryReservations.Where(x => x.OrderId == order.Id &&
            x.Status == InventoryReservationStatus.Active).ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            var balance = await db.InventoryBalances.SingleAsync(x => x.Id == reservation.BalanceId,
                cancellationToken);
            var before = balance.OnHandQuantity;
            balance.ConsumeReserved(reservation.Quantity);
            reservation.Consume(now);
            var movement = new InventoryMovement(order.TenantId, order.StoreId,
                reservation.ProductItemId, balance.Id, InventoryMovementType.SaleIssue,
                InventoryMovementDirection.Out, reservation.Quantity, before, balance.OnHandQuantity,
                "ServiceOrder", order.Id, reservation.OrderLineId, commandId, operatorId, now);
            db.InventoryMovements.Add(movement);
            await IssueLotsAsync(order.TenantId, order.StoreId, reservation.ProductItemId,
                reservation.Quantity, movement, cancellationToken);
        }
    }

    internal async Task ReleaseOrderAsync(ServiceOrder order, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reservations = await db.InventoryReservations.Where(x => x.OrderId == order.Id &&
            x.Status == InventoryReservationStatus.Active).ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            var balance = await db.InventoryBalances.SingleAsync(x => x.Id == reservation.BalanceId,
                cancellationToken);
            balance.Release(reservation.Quantity);
            reservation.Release(now);
        }
    }

    private async Task<InventoryBalance> GetOrCreateBalanceAsync(Guid tenantId, Guid storeId, Guid productId,
        CancellationToken cancellationToken)
    {
        var balance = await db.InventoryBalances.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
            x.StoreId == storeId && x.ProductItemId == productId, cancellationToken);
        if (balance is not null) return balance;
        balance = new InventoryBalance(tenantId, storeId, productId);
        db.InventoryBalances.Add(balance);
        return balance;
    }

    private void AddInboundLot(Guid tenantId, Guid storeId, Guid productId, string batchNo,
        int quantity, string sourceType, Guid sourceLineId, InventoryMovement movement)
    {
        var lot = new InventoryLot(tenantId, storeId, productId, batchNo, null, 0, quantity,
            sourceType, sourceLineId);
        db.InventoryLots.Add(lot);
        db.InventoryLotAllocations.Add(new InventoryLotAllocation(tenantId, movement.Id, lot.Id, quantity));
    }

    private async Task IssueLotsAsync(Guid tenantId, Guid storeId, Guid productId, int quantity,
        InventoryMovement movement, CancellationToken cancellationToken)
    {
        var lots = await db.InventoryLots.Where(x => x.TenantId == tenantId && x.StoreId == storeId &&
                x.ProductItemId == productId && x.RemainingQuantity > 0)
            .OrderBy(x => x.ExpiresOn == null).ThenBy(x => x.ExpiresOn).ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var remaining = quantity;
        foreach (var lot in lots)
        {
            var used = Math.Min(remaining, lot.RemainingQuantity);
            if (used == 0) continue;
            lot.Issue(used);
            db.InventoryLotAllocations.Add(new InventoryLotAllocation(tenantId, movement.Id, lot.Id, used));
            remaining -= used;
            if (remaining == 0) break;
        }
        if (remaining != 0)
            throw new DomainRuleException("INVENTORY_LOT_INSUFFICIENT", "批次库存与库存余额不一致，请先核对库存");
    }

    private async Task<Result<InventoryDocumentDto>?> ReplayDocumentAsync(Guid tenantId,
        PostInventoryDocumentCommand command, byte[] hash, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CommandId == command.CommandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, hash))
            return Failure<InventoryDocumentDto>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var id = ReadReceipt(existing.ResponseBody);
        if (!id.HasValue) return Failure<InventoryDocumentDto>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新");
        var document = await db.InventoryDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x =>
            x.Id == id.Value && x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
        return ResultFactory.Success((await ToDocumentDtosAsync([document], cancellationToken))[0]);
    }

    private async Task<Result<ProductReturnDto>?> ReplayReturnAsync(Guid tenantId, ReturnProductCommand command,
        byte[] hash, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CommandId == command.CommandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId || !CryptographicOperations.FixedTimeEquals(existing.RequestHash, hash))
            return Failure<ProductReturnDto>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var id = ReadReceipt(existing.ResponseBody);
        if (!id.HasValue) return Failure<ProductReturnDto>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新");
        var productReturn = await db.ProductReturns.AsNoTracking().SingleAsync(x => x.Id == id.Value &&
            x.TenantId == tenantId && x.StoreId == command.StoreId, cancellationToken);
        var product = await db.ProductItems.AsNoTracking().SingleAsync(x => x.Id == productReturn.ProductItemId,
            cancellationToken);
        return ResultFactory.Success(ToReturnDto(productReturn, product));
    }

    private async Task<IReadOnlyList<InventoryDocumentDto>> ToDocumentDtosAsync(
        IReadOnlyList<InventoryDocument> documents, CancellationToken cancellationToken)
    {
        var ids = documents.SelectMany(x => x.Lines).Select(x => x.ProductItemId).Distinct().ToList();
        var products = await db.ProductItems.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return documents.Select(document => new InventoryDocumentDto(document.Id, document.DocumentNo,
            document.DocumentType.ToString(), document.Reason, document.PostedBy, document.PostedAtUtc,
            document.Lines.Select(line =>
            {
                var product = products[line.ProductItemId];
                return new InventoryDocumentLineDto(line.Id, product.Id, product.Code, product.Name,
                    product.UnitName, line.Quantity);
            }).ToList())).ToList();
    }

    private static ProductReturnDto ToReturnDto(ProductReturn value, ProductItem product) => new(value.Id,
        value.OrderId, value.OrderLineId, value.ProductItemId, product.Code, product.Name, product.UnitName,
        value.Quantity, value.Reason, value.ReturnedBy, value.ReturnedAtUtc);

    private static InventoryMovementType ToMovementType(InventoryDocumentType type) => type switch
    {
        InventoryDocumentType.Opening => InventoryMovementType.Opening,
        InventoryDocumentType.Receipt => InventoryMovementType.Receipt,
        InventoryDocumentType.AdjustmentIn => InventoryMovementType.AdjustmentIn,
        InventoryDocumentType.AdjustmentOut => InventoryMovementType.AdjustmentOut,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] hash, Guid id,
        DateTimeOffset now) => db.IdempotencyCommands.Add(new IdempotencyCommandRecord
        {
            CommandId = commandId, TenantId = tenantId, OperatorId = operatorId, RequestHash = hash,
            ResponseStatus = 200, ResponseBody = JsonSerializer.Serialize(new CommandReceipt(id)),
            CreatedAtUtc = now, CompletedAtUtc = now,
        });

    private void AddAudit(Guid tenantId, Guid storeId, Guid operatorId, string action, string entityType,
        Guid entityId, string? previous, string? current, Guid commandId, string? reason, DateTimeOffset now) =>
        db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action,
            EntityType = entityType, EntityId = entityId, PreviousState = previous, CurrentState = current,
            RequestId = commandId, Reason = reason,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background", OccurredAtUtc = now,
        });

    private static Guid? ReadReceipt(string? json) => json is null
        ? null : JsonSerializer.Deserialize<CommandReceipt>(json)?.EntityId;
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static Result<T> Failure<T>(string code, string message) => ResultFactory.Failure<T>(code, message);

    private static async Task<Result<T>> RollbackFailure<T>(IDbContextTransaction transaction, string code,
        string message, CancellationToken cancellationToken)
    {
        await RollbackQuietly(transaction, cancellationToken);
        return Failure<T>(code, message);
    }

    private static async Task<Result<T>> DomainFailure<T>(IDbContextTransaction transaction,
        DomainRuleException exception, CancellationToken cancellationToken)
    {
        await RollbackQuietly(transaction, cancellationToken);
        return Failure<T>(exception.Code, exception.Message);
    }

    private static async Task RollbackQuietly(IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try { await transaction.RollbackAsync(cancellationToken); }
        catch (InvalidOperationException) { }
    }

    private static bool IsConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && postgres.SqlState is
                    PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or
                    PostgresErrorCodes.UniqueViolation) return true;
        return exception is DbUpdateConcurrencyException;
    }

    private static string CreateDocumentNo(DateTimeOffset now) =>
        $"INV{now:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..34].ToUpperInvariant();

    private sealed record CommandReceipt(Guid EntityId);
}
