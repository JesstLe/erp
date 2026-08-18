using Erp.Domain.Common;
using Erp.Domain.Customers;

namespace Erp.Domain.Tests.Customers;

public sealed class ServiceRecordTests
{
    [Fact]
    public void OptionalNarrativeAndImagesMayBeEmpty()
    {
        var now = DateTimeOffset.UtcNow;

        var record = new ServiceRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            now.AddMinutes(-30), null, "  ", null, Guid.CreateVersion7(), Guid.CreateVersion7(), now);

        Assert.Null(record.ServiceOrderId);
        Assert.Null(record.ConditionNotes);
        Assert.Null(record.ServiceContent);
        Assert.Null(record.FollowUpNotes);
        Assert.Empty(record.Attachments);
    }

    [Fact]
    public void FutureServiceTimeIsRejected()
    {
        var now = DateTimeOffset.UtcNow;

        var exception = Assert.Throws<DomainRuleException>(() => new ServiceRecord(Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, now.AddMinutes(6), null, null, null,
            Guid.CreateVersion7(), Guid.CreateVersion7(), now));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
    }

    [Fact]
    public void AtMostSixImagesCanBeAttached()
    {
        var now = DateTimeOffset.UtcNow;
        var record = new ServiceRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null,
            now, null, null, null, Guid.CreateVersion7(), Guid.CreateVersion7(), now);
        for (var index = 0; index < 6; index++) record.AttachImage(Guid.CreateVersion7());

        var exception = Assert.Throws<DomainRuleException>(() => record.AttachImage(Guid.CreateVersion7()));

        Assert.Equal("VALIDATION_FAILED", exception.Code);
        Assert.Equal(6, record.Attachments.Count);
    }

    [Fact]
    public void CorrectionStoresAFullAppendOnlyNarrativeSnapshot()
    {
        var correction = new ServiceRecordCorrection(Guid.CreateVersion7(), Guid.CreateVersion7(),
            " 服务描述录入有误 ", " 更正情况 ", " 更正服务 ", null, Guid.CreateVersion7(),
            Guid.CreateVersion7());

        Assert.Equal("服务描述录入有误", correction.Reason);
        Assert.Equal("更正情况", correction.ConditionNotes);
        Assert.Equal("更正服务", correction.ServiceContent);
        Assert.Null(correction.FollowUpNotes);
    }
}
