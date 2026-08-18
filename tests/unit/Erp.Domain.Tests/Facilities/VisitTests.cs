using Erp.Domain.Common;
using Erp.Domain.Facilities;

namespace Erp.Domain.Tests.Facilities;

public sealed class VisitTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();
    private static readonly DateTimeOffset ArrivedAt = new(2026, 8, 18, 2, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CustomerAndPlannedServiceAreOptionalRecognitionContext()
    {
        var plannedServiceItemId = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var visit = new Visit(TenantId, StoreId, "V202608180001", 45, null, ArrivedAt,
            plannedServiceItemId);

        visit.LinkCustomer(customerId);

        Assert.Equal(customerId, visit.CustomerId);
        Assert.Equal(plannedServiceItemId, visit.PlannedServiceItemId);
        Assert.Equal(45, visit.ExpectedDurationMinutes);
        Assert.Equal(VisitStatus.InService, visit.Status);
    }

    [Fact]
    public void EmptyPlannedServiceIdentifierIsRejected()
    {
        var exception = Assert.Throws<DomainRuleException>(() => new Visit(TenantId, StoreId,
            "V202608180002", null, null, ArrivedAt, Guid.Empty));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }
}
