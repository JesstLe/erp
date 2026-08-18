using Erp.Domain.Common;
using Erp.Domain.Inventory;

namespace Erp.Domain.Tests.Inventory;

public sealed class InventoryTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly Guid ProductId = Guid.CreateVersion7();

    [Fact]
    public void ReservationDoesNotReduceOnHandUntilConsumed()
    {
        var balance = new InventoryBalance(TenantId, StoreId, ProductId);
        balance.Receive(10);

        balance.Reserve(3);

        Assert.Equal(10, balance.OnHandQuantity);
        Assert.Equal(3, balance.ReservedQuantity);
        Assert.Equal(7, balance.AvailableQuantity);

        balance.ConsumeReserved(3);
        Assert.Equal(7, balance.OnHandQuantity);
        Assert.Equal(0, balance.ReservedQuantity);
        Assert.Equal(7, balance.AvailableQuantity);
    }

    [Fact]
    public void ReleasedReservationRestoresAvailableQuantityWithoutCreatingStock()
    {
        var balance = new InventoryBalance(TenantId, StoreId, ProductId);
        balance.Receive(8);
        balance.Reserve(5);

        balance.Release(5);

        Assert.Equal(8, balance.OnHandQuantity);
        Assert.Equal(0, balance.ReservedQuantity);
        Assert.Equal(8, balance.AvailableQuantity);
    }

    [Fact]
    public void CannotReserveOrAdjustOutMoreThanAvailable()
    {
        var balance = new InventoryBalance(TenantId, StoreId, ProductId);
        balance.Receive(4);
        balance.Reserve(3);

        var reserve = Assert.Throws<DomainRuleException>(() => balance.Reserve(2));
        var issue = Assert.Throws<DomainRuleException>(() => balance.AdjustOut(2));

        Assert.Equal("INSUFFICIENT_INVENTORY", reserve.Code);
        Assert.Equal("INSUFFICIENT_INVENTORY", issue.Code);
    }

    [Fact]
    public void MovementRequiresExactBeforeAndAfterSnapshot()
    {
        var exception = Assert.Throws<DomainRuleException>(() => new InventoryMovement(TenantId, StoreId,
            ProductId, Guid.CreateVersion7(), InventoryMovementType.Receipt, InventoryMovementDirection.In,
            3, 4, 6, "InventoryDocument", Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }

    [Fact]
    public void ReservationCanOnlyFinishOnce()
    {
        var reservation = new InventoryReservation(TenantId, StoreId, Guid.CreateVersion7(),
            Guid.CreateVersion7(), ProductId, Guid.CreateVersion7(), 2, DateTimeOffset.UtcNow);
        reservation.Release(DateTimeOffset.UtcNow);

        Assert.Throws<DomainRuleException>(() => reservation.Consume(DateTimeOffset.UtcNow));
    }
}
