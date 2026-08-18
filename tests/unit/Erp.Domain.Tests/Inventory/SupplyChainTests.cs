using Erp.Domain.Common;
using Erp.Domain.Inventory;

namespace Erp.Domain.Tests.Inventory;

public sealed class SupplyChainTests
{
    [Fact]
    public void InventoryLotIssuesWithoutBecomingNegative()
    {
        var lot = new InventoryLot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "batch-01",
            new DateOnly(2027, 1, 1), 1250, 10, "PurchaseReceiptLine", Guid.NewGuid());

        lot.Issue(4);

        Assert.Equal(6, lot.RemainingQuantity);
        var error = Assert.Throws<DomainRuleException>(() => lot.Issue(7));
        Assert.Equal("INVENTORY_LOT_INSUFFICIENT", error.Code);
    }

    [Fact]
    public void StocktakeRequiresSeparationBetweenRequesterAndApprover()
    {
        var tenantId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var stocktake = new Stocktake(tenantId, Guid.NewGuid(), "STK001", "月度盘点",
            requesterId, DateTimeOffset.UtcNow, [(Guid.NewGuid(), 10, 8)]);

        var error = Assert.Throws<DomainRuleException>(() => stocktake.Approve(requesterId,
            "复核无误", DateTimeOffset.UtcNow));

        Assert.Equal("FORBIDDEN_ACTION", error.Code);
        Assert.Equal(StocktakeStatus.PendingApproval, stocktake.Status);
    }

    [Fact]
    public void StocktakeUsesFrozenBookQuantityForDifference()
    {
        var stocktake = new Stocktake(Guid.NewGuid(), Guid.NewGuid(), "STK002", "抽盘",
            Guid.NewGuid(), DateTimeOffset.UtcNow, [(Guid.NewGuid(), 10, 13)]);

        Assert.Equal(3, stocktake.Lines.Single().DifferenceQuantity);
    }

    [Fact]
    public void TransferFollowsRequestedInTransitReceivedStateMachine()
    {
        var transfer = new InventoryTransfer(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "TRF001", "门店补货", Guid.NewGuid(), DateTimeOffset.UtcNow,
            [(Guid.NewGuid(), 3)]);

        transfer.Ship(Guid.NewGuid(), "已复核出库", DateTimeOffset.UtcNow);
        Assert.Equal(InventoryTransferStatus.InTransit, transfer.Status);
        transfer.Receive(Guid.NewGuid(), "到货无误", DateTimeOffset.UtcNow);
        Assert.Equal(InventoryTransferStatus.Received, transfer.Status);
        Assert.Throws<DomainRuleException>(() => transfer.Cancel("错误取消"));
    }

    [Fact]
    public void TransferRejectsSameSourceAndDestination()
    {
        var storeId = Guid.NewGuid();
        var error = Assert.Throws<DomainRuleException>(() => new InventoryTransfer(Guid.NewGuid(),
            storeId, storeId, "TRF002", "无效调拨", Guid.NewGuid(), DateTimeOffset.UtcNow,
            [(Guid.NewGuid(), 1)]));

        Assert.Equal("VALIDATION_FAILED", error.Code);
    }
}
