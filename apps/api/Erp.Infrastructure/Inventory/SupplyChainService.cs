using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Application.Common;
using Erp.Application.Inventory;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Domain.Inventory;
using Erp.Domain.Organization;
using Erp.Infrastructure.Persistence;
using Erp.Infrastructure.Organization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Erp.Infrastructure.Inventory;

internal sealed class SupplyChainService(ErpDbContext db, TimeProvider clock,
    IHttpContextAccessor httpContextAccessor, BusinessCodeGenerator codeGenerator) : ISupplyChainService
{
    public async Task<PageResult<SupplierDto>> ListSuppliersAsync(Guid tenantId, string? keyword,
        bool includeDisabled, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Suppliers.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!includeDisabled) query = query.Where(x => x.Status == SupplierStatus.Active);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var value = keyword.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Code, $"%{value}%") ||
                EF.Functions.ILike(x.Name, $"%{value}%") ||
                (x.ContactName != null && EF.Functions.ILike(x.ContactName, $"%{value}%")) ||
                (x.Mobile != null && EF.Functions.ILike(x.Mobile, $"%{value}%")));
        }
        var total = await query.CountAsync(cancellationToken);
        var values = await query.OrderBy(x => x.Code).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PageResult<SupplierDto>(values.Select(ToSupplierDto).ToList(), total, page, pageSize);
    }

    public async Task<Result<SupplierDto>> SaveSupplierAsync(Guid tenantId, SaveSupplierCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            Supplier supplier;
            string? previous = null;
            if (command.Id.HasValue)
            {
                supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.Id == command.Id &&
                    x.TenantId == tenantId, cancellationToken) ?? throw new DomainRuleException(
                    "SUPPLIER_NOT_FOUND", "供应商不存在");
                if (!command.ExpectedVersion.HasValue || supplier.Version != command.ExpectedVersion)
                    throw new DomainRuleException("VERSION_CONFLICT", "供应商已变化，请刷新后重试");
                previous = supplier.Status.ToString();
                supplier.Update(command.Name, command.ContactName, command.Mobile, command.SettlementTerms);
            }
            else
            {
                var code = await codeGenerator.NextSupplierCodeAsync(tenantId, cancellationToken);
                supplier = new Supplier(tenantId, code, command.Name, command.ContactName,
                    command.Mobile, command.SettlementTerms);
                db.Suppliers.Add(supplier);
            }
            AddAudit(tenantId, null, command.OperatorId, command.Id.HasValue ? "supplier.update" :
                "supplier.create", "Supplier", supplier.Id, previous,
                supplier.Status.ToString(), Guid.CreateVersion7(), null);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(ToSupplierDto(supplier));
        }
        catch (DomainRuleException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure<SupplierDto>(exception);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<SupplierDto>("VERSION_CONFLICT", "供应商已变化，请刷新后重试");
        }
        catch (Exception exception) when (IsConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ResultFactory.Failure<SupplierDto>("CODE_GENERATION_CONFLICT", "供应商编码生成冲突，请重试");
        }
    }

    public async Task<Result<SupplierDto>> ChangeSupplierStatusAsync(Guid tenantId,
        ChangeSupplierStatusCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.Id == command.SupplierId &&
                x.TenantId == tenantId, cancellationToken) ?? throw new DomainRuleException(
                "SUPPLIER_NOT_FOUND", "供应商不存在");
            if (supplier.Version != command.ExpectedVersion)
                throw new DomainRuleException("VERSION_CONFLICT", "供应商已变化，请刷新后重试");
            var previous = supplier.Status.ToString();
            supplier.ChangeStatus(command.Enable);
            AddAudit(tenantId, null, command.OperatorId, "supplier.status.change", "Supplier", supplier.Id,
                previous, supplier.Status.ToString(), Guid.CreateVersion7(), null);
            await db.SaveChangesAsync(cancellationToken);
            return ResultFactory.Success(ToSupplierDto(supplier));
        }
        catch (DomainRuleException exception) { return Failure<SupplierDto>(exception); }
        catch (DbUpdateConcurrencyException)
        { return ResultFactory.Failure<SupplierDto>("VERSION_CONFLICT", "供应商已变化，请刷新后重试"); }
    }

    public async Task<PageResult<InventoryLotDto>> ListLotsAsync(Guid tenantId, Guid storeId,
        Guid? productItemId, bool expiringOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.InventoryLots.AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.StoreId == storeId && x.RemainingQuantity > 0);
        if (productItemId.HasValue) query = query.Where(x => x.ProductItemId == productItemId);
        if (expiringOnly)
        {
            var until = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddDays(90));
            query = query.Where(x => x.ExpiresOn != null && x.ExpiresOn <= until);
        }
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.ExpiresOn == null).ThenBy(x => x.ExpiresOn)
            .ThenBy(x => x.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize)
            .Join(db.ProductItems.AsNoTracking(), lot => lot.ProductItemId, product => product.Id,
                (lot, product) => new InventoryLotDto(lot.Id, lot.StoreId, lot.ProductItemId,
                    product.Code, product.Name, product.UnitName, lot.BatchNo, lot.ExpiresOn,
                    lot.UnitCostMinor, lot.OriginalQuantity, lot.RemainingQuantity, lot.SourceType,
                    lot.CreatedAtUtc)).ToListAsync(cancellationToken);
        return new PageResult<InventoryLotDto>(rows, total, page, pageSize);
    }

    public async Task<PageResult<PurchaseReceiptDto>> ListPurchaseReceiptsAsync(Guid tenantId,
        Guid storeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.PurchaseReceipts.AsNoTracking().Where(x => x.TenantId == tenantId &&
            x.StoreId == storeId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Include(x => x.Lines).OrderByDescending(x => x.PostedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<PurchaseReceiptDto>(await ToPurchaseDtosAsync(rows, cancellationToken),
            total, page, pageSize);
    }

    public async Task<Result<PurchaseReceiptDto>> PostPurchaseReceiptAsync(Guid tenantId,
        PostPurchaseReceiptCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<PurchaseReceiptDto>("VALIDATION_FAILED", "缺少幂等请求号");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var requestHash = Hash(command with { OperatorId = Guid.Empty });
            var replay = await ReplayAsync(command.CommandId, tenantId, requestHash, async id =>
            {
                var existing = await db.PurchaseReceipts.AsNoTracking().Include(x => x.Lines)
                    .SingleAsync(x => x.Id == id && x.StoreId == command.StoreId, cancellationToken);
                return (await ToPurchaseDtosAsync([existing], cancellationToken))[0];
            }, cancellationToken);
            if (replay is not null) return replay;
            var supplier = await db.Suppliers.SingleOrDefaultAsync(x => x.Id == command.SupplierId &&
                x.TenantId == tenantId && x.Status == SupplierStatus.Active, cancellationToken)
                ?? throw new DomainRuleException("SUPPLIER_NOT_ACTIVE", "供应商不存在或已停用");
            await EnsureStoreAsync(tenantId, command.StoreId, cancellationToken);
            var products = await LoadTrackedProductsAsync(tenantId,
                command.Lines.Select(x => x.ProductItemId), cancellationToken);
            var now = clock.GetUtcNow();
            var receipt = new PurchaseReceipt(tenantId, command.StoreId, supplier.Id,
                CreateNo("PUR", now), command.ExternalNo, command.Note, command.OperatorId, now,
                command.Lines.Select(x => (x.ProductItemId, x.Quantity, x.UnitCostMinor,
                    x.BatchNo, x.ExpiresOn)));
            db.PurchaseReceipts.Add(receipt);
            foreach (var line in receipt.Lines)
            {
                _ = products[line.ProductItemId];
                var balance = await GetOrCreateBalanceAsync(tenantId, command.StoreId,
                    line.ProductItemId, cancellationToken);
                var before = balance.OnHandQuantity;
                balance.Receive(line.Quantity);
                var movement = new InventoryMovement(tenantId, command.StoreId, line.ProductItemId,
                    balance.Id, InventoryMovementType.PurchaseReceipt, InventoryMovementDirection.In,
                    line.Quantity, before, balance.OnHandQuantity, "PurchaseReceipt", receipt.Id,
                    line.Id, command.CommandId, command.OperatorId, now);
                db.InventoryMovements.Add(movement);
                AddInboundLot(tenantId, command.StoreId, line.ProductItemId, line.BatchNo,
                    line.ExpiresOn, line.UnitCostMinor, line.Quantity, "PurchaseReceiptLine", line.Id,
                    movement);
            }
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, receipt.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "purchase.receipt.post",
                "PurchaseReceipt", receipt.Id, null,
                receipt.TotalCostMinor.ToString(CultureInfo.InvariantCulture), command.CommandId,
                command.Note);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success((await ToPurchaseDtosAsync([receipt], cancellationToken))[0]);
        }
        catch (DomainRuleException exception)
        { return await RollbackFailure<PurchaseReceiptDto>(transaction, exception, cancellationToken); }
        catch (Exception exception) when (IsConflict(exception))
        { return await RollbackConflict<PurchaseReceiptDto>(transaction, cancellationToken); }
    }

    public async Task<PageResult<StocktakeDto>> ListStocktakesAsync(Guid tenantId, Guid storeId,
        string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Stocktakes.AsNoTracking().Where(x => x.TenantId == tenantId && x.StoreId == storeId);
        if (Enum.TryParse<StocktakeStatus>(status, true, out var parsed)) query = query.Where(x => x.Status == parsed);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Include(x => x.Lines).OrderByDescending(x => x.FrozenAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<StocktakeDto>(await ToStocktakeDtosAsync(rows, cancellationToken), total,
            page, pageSize);
    }

    public async Task<Result<StocktakeDto>> CreateStocktakeAsync(Guid tenantId,
        CreateStocktakeCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<StocktakeDto>("VALIDATION_FAILED", "缺少幂等请求号");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var requestHash = Hash(command with { OperatorId = Guid.Empty });
            var replay = await ReplayAsync(command.CommandId, tenantId, requestHash,
                async id => await LoadStocktakeDtoAsync(tenantId, id,
                    cancellationToken), cancellationToken);
            if (replay is not null) return replay;
            await EnsureStoreAsync(tenantId, command.StoreId, cancellationToken);
            _ = await LoadTrackedProductsAsync(tenantId, command.Lines.Select(x => x.ProductItemId),
                cancellationToken);
            var productIds = command.Lines.Select(x => x.ProductItemId).ToList();
            var balances = await db.InventoryBalances.Where(x => x.TenantId == tenantId &&
                x.StoreId == command.StoreId && productIds.Contains(x.ProductItemId))
                .ToDictionaryAsync(x => x.ProductItemId, cancellationToken);
            var now = clock.GetUtcNow();
            var stocktake = new Stocktake(tenantId, command.StoreId, CreateNo("STK", now),
                command.Reason, command.OperatorId, now, command.Lines.Select(x =>
                    (x.ProductItemId, balances.GetValueOrDefault(x.ProductItemId)?.OnHandQuantity ?? 0,
                        x.CountedQuantity)));
            db.Stocktakes.Add(stocktake);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, stocktake.Id, now);
            AddAudit(tenantId, command.StoreId, command.OperatorId, "stocktake.create", "Stocktake",
                stocktake.Id, null, "PendingApproval", command.CommandId, command.Reason);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadStocktakeDtoAsync(tenantId, stocktake.Id,
                cancellationToken));
        }
        catch (DomainRuleException exception)
        { return await RollbackFailure<StocktakeDto>(transaction, exception, cancellationToken); }
        catch (Exception exception) when (IsConflict(exception))
        { return await RollbackConflict<StocktakeDto>(transaction, cancellationToken); }
    }

    public Task<Result<StocktakeDto>> ApproveStocktakeAsync(Guid tenantId,
        DecideStocktakeCommand command, CancellationToken cancellationToken) =>
        DecideStocktakeAsync(tenantId, command, true, cancellationToken);

    public Task<Result<StocktakeDto>> CancelStocktakeAsync(Guid tenantId,
        DecideStocktakeCommand command, CancellationToken cancellationToken) =>
        DecideStocktakeAsync(tenantId, command, false, cancellationToken);

    private async Task<Result<StocktakeDto>> DecideStocktakeAsync(Guid tenantId,
        DecideStocktakeCommand command, bool approve, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<StocktakeDto>("VALIDATION_FAILED", "缺少幂等请求号");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var requestHash = Hash(command with { OperatorId = Guid.Empty });
            var replay = await ReplayAsync(command.CommandId, tenantId, requestHash,
                async id => await LoadStocktakeDtoAsync(tenantId, id,
                    cancellationToken), cancellationToken);
            if (replay is not null) return replay;
            var value = await db.Stocktakes.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.Id == command.StocktakeId && x.TenantId == tenantId &&
                x.StoreId == command.StoreId, cancellationToken)
                ?? throw new DomainRuleException("STOCKTAKE_NOT_FOUND", "盘点单不存在");
            if (value.Version != command.ExpectedVersion)
                throw new DomainRuleException("VERSION_CONFLICT", "盘点单已变化，请刷新后重试");
            var now = clock.GetUtcNow();
            if (approve)
            {
                value.Approve(command.OperatorId, command.Reason, now);
                foreach (var line in value.Lines.Where(x => x.DifferenceQuantity != 0))
                {
                    var balance = await GetOrCreateBalanceAsync(tenantId, value.StoreId,
                        line.ProductItemId, cancellationToken);
                    var before = balance.OnHandQuantity;
                    var quantity = Math.Abs(line.DifferenceQuantity);
                    var direction = line.DifferenceQuantity > 0 ? InventoryMovementDirection.In :
                        InventoryMovementDirection.Out;
                    if (direction == InventoryMovementDirection.In) balance.Receive(quantity);
                    else balance.AdjustOut(quantity);
                    var movement = new InventoryMovement(tenantId, value.StoreId, line.ProductItemId,
                        balance.Id, direction == InventoryMovementDirection.In ?
                            InventoryMovementType.StocktakeGain : InventoryMovementType.StocktakeLoss,
                        direction, quantity, before, balance.OnHandQuantity, "Stocktake", value.Id,
                        line.Id, command.CommandId, command.OperatorId, now);
                    db.InventoryMovements.Add(movement);
                    if (direction == InventoryMovementDirection.In)
                        AddInboundLot(tenantId, value.StoreId, line.ProductItemId,
                            $"STK-{value.StocktakeNo}", null, 0, quantity, "StocktakeLine", line.Id, movement);
                    else await IssueLotsAsync(tenantId, value.StoreId, line.ProductItemId, quantity,
                        movement, null, cancellationToken);
                }
            }
            else value.Cancel(command.OperatorId, command.Reason);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, value.Id, now);
            AddAudit(tenantId, value.StoreId, command.OperatorId,
                approve ? "stocktake.approve" : "stocktake.cancel", "Stocktake", value.Id,
                "PendingApproval", value.Status.ToString(), command.CommandId, command.Reason);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadStocktakeDtoAsync(tenantId, value.Id,
                cancellationToken));
        }
        catch (DomainRuleException exception)
        { return await RollbackFailure<StocktakeDto>(transaction, exception, cancellationToken); }
        catch (Exception exception) when (IsConflict(exception))
        { return await RollbackConflict<StocktakeDto>(transaction, cancellationToken); }
    }

    public async Task<PageResult<InventoryTransferDto>> ListTransfersAsync(Guid tenantId, Guid? storeId,
        string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.InventoryTransfers.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (storeId.HasValue) query = query.Where(x => x.SourceStoreId == storeId ||
            x.DestinationStoreId == storeId);
        if (Enum.TryParse<InventoryTransferStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Include(x => x.Lines).OrderByDescending(x => x.RequestedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PageResult<InventoryTransferDto>(await ToTransferDtosAsync(rows, cancellationToken),
            total, page, pageSize);
    }

    public async Task<Result<InventoryTransferDto>> CreateTransferAsync(Guid tenantId,
        CreateInventoryTransferCommand command, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<InventoryTransferDto>("VALIDATION_FAILED", "缺少幂等请求号");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var requestHash = Hash(command with { OperatorId = Guid.Empty });
            var replay = await ReplayAsync(command.CommandId, tenantId, requestHash,
                async id => await LoadTransferDtoAsync(tenantId, id,
                    cancellationToken), cancellationToken);
            if (replay is not null) return replay;
            await EnsureStoreAsync(tenantId, command.SourceStoreId, cancellationToken);
            await EnsureStoreAsync(tenantId, command.DestinationStoreId, cancellationToken);
            _ = await LoadTrackedProductsAsync(tenantId, command.Lines.Select(x => x.ProductItemId),
                cancellationToken);
            var now = clock.GetUtcNow();
            var transfer = new InventoryTransfer(tenantId, command.SourceStoreId,
                command.DestinationStoreId, CreateNo("TRF", now), command.Reason, command.OperatorId,
                now, command.Lines.Select(x => (x.ProductItemId, x.Quantity)));
            db.InventoryTransfers.Add(transfer);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, transfer.Id, now);
            AddAudit(tenantId, command.SourceStoreId, command.OperatorId, "inventory.transfer.create",
                "InventoryTransfer", transfer.Id, null, "Requested", command.CommandId, command.Reason);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadTransferDtoAsync(tenantId, transfer.Id,
                cancellationToken));
        }
        catch (DomainRuleException exception)
        { return await RollbackFailure<InventoryTransferDto>(transaction, exception, cancellationToken); }
        catch (Exception exception) when (IsConflict(exception))
        { return await RollbackConflict<InventoryTransferDto>(transaction, cancellationToken); }
    }

    public Task<Result<InventoryTransferDto>> ShipTransferAsync(Guid tenantId,
        TransitionInventoryTransferCommand command, CancellationToken cancellationToken) =>
        TransitionTransferAsync(tenantId, command, "ship", cancellationToken);
    public Task<Result<InventoryTransferDto>> ReceiveTransferAsync(Guid tenantId,
        TransitionInventoryTransferCommand command, CancellationToken cancellationToken) =>
        TransitionTransferAsync(tenantId, command, "receive", cancellationToken);
    public Task<Result<InventoryTransferDto>> CancelTransferAsync(Guid tenantId,
        TransitionInventoryTransferCommand command, CancellationToken cancellationToken) =>
        TransitionTransferAsync(tenantId, command, "cancel", cancellationToken);

    private async Task<Result<InventoryTransferDto>> TransitionTransferAsync(Guid tenantId,
        TransitionInventoryTransferCommand command, string action, CancellationToken cancellationToken)
    {
        if (command.CommandId == Guid.Empty)
            return ResultFactory.Failure<InventoryTransferDto>("VALIDATION_FAILED", "缺少幂等请求号");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var requestHash = Hash(command with { OperatorId = Guid.Empty });
            var replay = await ReplayAsync(command.CommandId, tenantId, requestHash,
                async id => await LoadTransferDtoAsync(tenantId, id,
                    cancellationToken), cancellationToken);
            if (replay is not null) return replay;
            var value = await db.InventoryTransfers.Include(x => x.Lines).SingleOrDefaultAsync(x =>
                x.Id == command.TransferId && x.TenantId == tenantId, cancellationToken)
                ?? throw new DomainRuleException("INVENTORY_TRANSFER_NOT_FOUND", "调拨单不存在");
            if (value.Version != command.ExpectedVersion)
                throw new DomainRuleException("VERSION_CONFLICT", "调拨单已变化，请刷新后重试");
            var previous = value.Status.ToString();
            var now = clock.GetUtcNow();
            if (action == "ship")
            {
                value.Ship(command.OperatorId, command.Reason, now);
                foreach (var line in value.Lines)
                {
                    var balance = await GetOrCreateBalanceAsync(tenantId, value.SourceStoreId,
                        line.ProductItemId, cancellationToken);
                    var before = balance.OnHandQuantity;
                    balance.AdjustOut(line.Quantity);
                    var movement = new InventoryMovement(tenantId, value.SourceStoreId,
                        line.ProductItemId, balance.Id, InventoryMovementType.TransferOut,
                        InventoryMovementDirection.Out, line.Quantity, before, balance.OnHandQuantity,
                        "InventoryTransfer", value.Id, line.Id, command.CommandId, command.OperatorId, now);
                    db.InventoryMovements.Add(movement);
                    await IssueLotsAsync(tenantId, value.SourceStoreId, line.ProductItemId, line.Quantity,
                        movement, line, cancellationToken);
                }
            }
            else if (action == "receive")
            {
                value.Receive(command.OperatorId, command.Reason, now);
                foreach (var line in value.Lines)
                {
                    var transferLots = await db.InventoryTransferLots.Where(x =>
                        x.TransferLineId == line.Id).ToListAsync(cancellationToken);
                    if (transferLots.Sum(x => x.Quantity) != line.Quantity)
                        throw new DomainRuleException("INVENTORY_TRANSFER_LOT_MISMATCH",
                            "调拨批次分摊与调拨数量不一致");
                    var balance = await GetOrCreateBalanceAsync(tenantId, value.DestinationStoreId,
                        line.ProductItemId, cancellationToken);
                    var before = balance.OnHandQuantity;
                    balance.Receive(line.Quantity);
                    var movement = new InventoryMovement(tenantId, value.DestinationStoreId,
                        line.ProductItemId, balance.Id, InventoryMovementType.TransferIn,
                        InventoryMovementDirection.In, line.Quantity, before, balance.OnHandQuantity,
                        "InventoryTransfer", value.Id, line.Id, command.CommandId, command.OperatorId, now);
                    db.InventoryMovements.Add(movement);
                    foreach (var transferLot in transferLots)
                        AddInboundLot(tenantId, value.DestinationStoreId, line.ProductItemId,
                            transferLot.BatchNo, transferLot.ExpiresOn, transferLot.UnitCostMinor,
                            transferLot.Quantity, "InventoryTransferLot", transferLot.Id, movement);
                }
            }
            else value.Cancel(command.Reason);
            AddReceipt(tenantId, command.CommandId, command.OperatorId, requestHash, value.Id, now);
            AddAudit(tenantId, value.SourceStoreId, command.OperatorId,
                $"inventory.transfer.{action}", "InventoryTransfer", value.Id, previous,
                value.Status.ToString(), command.CommandId, command.Reason);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ResultFactory.Success(await LoadTransferDtoAsync(tenantId, value.Id,
                cancellationToken));
        }
        catch (DomainRuleException exception)
        { return await RollbackFailure<InventoryTransferDto>(transaction, exception, cancellationToken); }
        catch (Exception exception) when (IsConflict(exception))
        { return await RollbackConflict<InventoryTransferDto>(transaction, cancellationToken); }
    }

    private async Task IssueLotsAsync(Guid tenantId, Guid storeId, Guid productId, int quantity,
        InventoryMovement movement, InventoryTransferLine? transferLine,
        CancellationToken cancellationToken)
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
            if (transferLine is not null)
                db.InventoryTransferLots.Add(new InventoryTransferLot(tenantId, transferLine.Id, lot.Id,
                    lot.BatchNo, lot.ExpiresOn, lot.UnitCostMinor, used));
            remaining -= used;
            if (remaining == 0) break;
        }
        if (remaining != 0)
            throw new DomainRuleException("INVENTORY_LOT_INSUFFICIENT", "批次库存与库存余额不一致，请先核对库存");
    }

    private void AddInboundLot(Guid tenantId, Guid storeId, Guid productId, string batchNo,
        DateOnly? expiresOn, long unitCostMinor, int quantity, string sourceType, Guid sourceLineId,
        InventoryMovement movement)
    {
        var lot = new InventoryLot(tenantId, storeId, productId, batchNo, expiresOn, unitCostMinor,
            quantity, sourceType, sourceLineId);
        db.InventoryLots.Add(lot);
        db.InventoryLotAllocations.Add(new InventoryLotAllocation(tenantId, movement.Id, lot.Id, quantity));
    }

    private async Task<Dictionary<Guid, ProductItem>> LoadTrackedProductsAsync(Guid tenantId,
        IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var values = ids.Distinct().ToList();
        var products = await db.ProductItems.Where(x => x.TenantId == tenantId && values.Contains(x.Id) &&
            x.Status == CatalogItemStatus.Enabled && x.TrackInventory)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (products.Count != values.Count)
            throw new DomainRuleException("INVENTORY_PRODUCT_NOT_TRACKED", "只能选择已启用且跟踪库存的产品");
        return products;
    }

    private async Task EnsureStoreAsync(Guid tenantId, Guid storeId, CancellationToken cancellationToken)
    {
        if (!await db.Stores.AnyAsync(x => x.Id == storeId && x.TenantId == tenantId &&
            x.Status == StoreStatus.Enabled, cancellationToken))
            throw new DomainRuleException("STORE_NOT_FOUND", "门店不存在或已停用");
    }

    private async Task<InventoryBalance> GetOrCreateBalanceAsync(Guid tenantId, Guid storeId,
        Guid productId, CancellationToken cancellationToken)
    {
        var value = await db.InventoryBalances.SingleOrDefaultAsync(x => x.TenantId == tenantId &&
            x.StoreId == storeId && x.ProductItemId == productId, cancellationToken);
        if (value is not null) return value;
        value = new InventoryBalance(tenantId, storeId, productId);
        db.InventoryBalances.Add(value);
        return value;
    }

    private async Task<Result<T>?> ReplayAsync<T>(Guid commandId, Guid tenantId, byte[] hash,
        Func<Guid, Task<T>> loader, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyCommands.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CommandId == commandId, cancellationToken);
        if (existing is null) return null;
        if (existing.TenantId != tenantId ||
            !CryptographicOperations.FixedTimeEquals(existing.RequestHash, hash))
            return ResultFactory.Failure<T>("IDEMPOTENCY_CONFLICT", "幂等请求号已被其他请求使用");
        var id = existing.ResponseBody is null ? null :
            JsonSerializer.Deserialize<CommandReceipt>(existing.ResponseBody)?.EntityId;
        return id.HasValue ? ResultFactory.Success(await loader(id.Value)) :
            ResultFactory.Failure<T>("COMMAND_IN_PROGRESS", "请求正在处理，请稍后刷新");
    }

    private void AddReceipt(Guid tenantId, Guid commandId, Guid operatorId, byte[] requestHash,
        Guid entityId, DateTimeOffset now)
    {
        db.IdempotencyCommands.Add(new IdempotencyCommandRecord
        {
            CommandId = commandId, TenantId = tenantId, OperatorId = operatorId,
            RequestHash = requestHash, ResponseStatus = 200,
            ResponseBody = JsonSerializer.Serialize(new CommandReceipt(entityId)), CreatedAtUtc = now,
            CompletedAtUtc = now,
        });
    }

    private static byte[] Hash<T>(T command) => SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(command)));

    private void AddAudit(Guid tenantId, Guid? storeId, Guid operatorId, string action,
        string entityType, Guid entityId, string? previous, string? current, Guid requestId,
        string? reason) => db.AuditEvents.Add(new AuditEventRecord
        {
            TenantId = tenantId, StoreId = storeId, OperatorId = operatorId, Action = action,
            EntityType = entityType, EntityId = entityId, PreviousState = previous, CurrentState = current,
            RequestId = requestId, Reason = reason,
            TraceId = httpContextAccessor.HttpContext?.TraceIdentifier ?? "background",
            OccurredAtUtc = clock.GetUtcNow(),
        });

    private async Task<IReadOnlyList<PurchaseReceiptDto>> ToPurchaseDtosAsync(
        IReadOnlyList<PurchaseReceipt> rows, CancellationToken cancellationToken)
    {
        var productIds = rows.SelectMany(x => x.Lines).Select(x => x.ProductItemId).Distinct().ToList();
        var supplierIds = rows.Select(x => x.SupplierId).Distinct().ToList();
        var products = await db.ProductItems.AsNoTracking().Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var suppliers = await db.Suppliers.AsNoTracking().Where(x => supplierIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return rows.Select(x => new PurchaseReceiptDto(x.Id, x.StoreId, x.SupplierId,
            suppliers[x.SupplierId].Name, x.ReceiptNo, x.ExternalNo, x.Note, x.TotalCostMinor,
            x.PostedBy, x.PostedAtUtc, x.Lines.Select(line =>
            {
                var product = products[line.ProductItemId];
                return new PurchaseReceiptLineDto(line.Id, line.ProductItemId, product.Code,
                    product.Name, product.UnitName, line.Quantity, line.UnitCostMinor,
                    line.LineCostMinor, line.BatchNo, line.ExpiresOn);
            }).ToList())).ToList();
    }

    private async Task<IReadOnlyList<StocktakeDto>> ToStocktakeDtosAsync(IReadOnlyList<Stocktake> rows,
        CancellationToken cancellationToken)
    {
        var ids = rows.SelectMany(x => x.Lines).Select(x => x.ProductItemId).Distinct().ToList();
        var products = await db.ProductItems.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return rows.Select(x => new StocktakeDto(x.Id, x.StoreId, x.StocktakeNo, x.Reason,
            x.RequestedBy, x.FrozenAtUtc, x.Status.ToString(), x.ApprovedBy, x.PostedAtUtc,
            x.DecisionReason, x.Version, x.Lines.Select(line =>
            {
                var product = products[line.ProductItemId];
                return new StocktakeLineDto(line.Id, line.ProductItemId, product.Code, product.Name,
                    product.UnitName, line.BookQuantity, line.CountedQuantity, line.DifferenceQuantity);
            }).ToList())).ToList();
    }

    private async Task<StocktakeDto> LoadStocktakeDtoAsync(Guid tenantId, Guid id,
        CancellationToken cancellationToken)
    {
        var value = await db.Stocktakes.AsNoTracking().Include(x => x.Lines).SingleAsync(x =>
            x.Id == id && x.TenantId == tenantId, cancellationToken);
        return (await ToStocktakeDtosAsync([value], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<InventoryTransferDto>> ToTransferDtosAsync(
        IReadOnlyList<InventoryTransfer> rows, CancellationToken cancellationToken)
    {
        var ids = rows.SelectMany(x => x.Lines).Select(x => x.ProductItemId).Distinct().ToList();
        var products = await db.ProductItems.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return rows.Select(x => new InventoryTransferDto(x.Id, x.SourceStoreId, x.DestinationStoreId,
            x.TransferNo, x.Reason, x.RequestedBy, x.RequestedAtUtc, x.Status.ToString(), x.ShippedBy,
            x.ShippedAtUtc, x.ReceivedBy, x.ReceivedAtUtc, x.DecisionReason, x.Version,
            x.Lines.Select(line =>
            {
                var product = products[line.ProductItemId];
                return new InventoryTransferLineDto(line.Id, line.ProductItemId, product.Code,
                    product.Name, product.UnitName, line.Quantity);
            }).ToList())).ToList();
    }

    private async Task<InventoryTransferDto> LoadTransferDtoAsync(Guid tenantId, Guid id,
        CancellationToken cancellationToken)
    {
        var value = await db.InventoryTransfers.AsNoTracking().Include(x => x.Lines).SingleAsync(x =>
            x.Id == id && x.TenantId == tenantId, cancellationToken);
        return (await ToTransferDtosAsync([value], cancellationToken))[0];
    }

    private static SupplierDto ToSupplierDto(Supplier x) => new(x.Id, x.Code, x.Name, x.ContactName,
        x.Mobile, x.SettlementTerms, x.Status.ToString(), x.Version);
    private static string CreateNo(string prefix, DateTimeOffset now) =>
        $"{prefix}{now:yyyyMMddHHmmss}{Guid.CreateVersion7():N}"[..34].ToUpperInvariant();
    private static Result<T> Failure<T>(DomainRuleException e) => ResultFactory.Failure<T>(e.Code, e.Message);
    private static async Task<Result<T>> RollbackFailure<T>(IDbContextTransaction transaction,
        DomainRuleException exception, CancellationToken cancellationToken)
    { await transaction.RollbackAsync(cancellationToken); return Failure<T>(exception); }
    private static async Task<Result<T>> RollbackConflict<T>(IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    { await transaction.RollbackAsync(cancellationToken); return ResultFactory.Failure<T>(
        "VERSION_CONFLICT", "数据已被其他终端修改，请刷新后重试"); }
    private static bool IsConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is PostgresException postgres && postgres.SqlState is
                    PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or
                    PostgresErrorCodes.UniqueViolation) return true;
        return exception is DbUpdateConcurrencyException;
    }
    private sealed record CommandReceipt(Guid EntityId);
}
