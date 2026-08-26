using Erp.Domain.Common;
using Erp.Domain.Organization;

namespace Erp.Domain.Tests.Organization;

public sealed class EmployeePositionTests
{
    [Fact]
    public void PositionNameAndStatusAreConfigurableWithoutChangingCode()
    {
        var position = new EmployeePosition(Guid.NewGuid(), "POS000001", "顾问", 20);

        position.Update("高级顾问", 10);
        position.Disable();

        Assert.Equal("POS000001", position.Code);
        Assert.Equal("高级顾问", position.Name);
        Assert.Equal(10, position.SortOrder);
        Assert.Equal(EmployeePositionStatus.Disabled, position.Status);
    }

    [Fact]
    public void PositionRejectsInvalidSortOrder()
    {
        var exception = Assert.Throws<DomainRuleException>(() =>
            new EmployeePosition(Guid.NewGuid(), "POS000001", "顾问", -1));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }
}
