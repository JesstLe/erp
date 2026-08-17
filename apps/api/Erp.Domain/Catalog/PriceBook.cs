using Erp.Domain.Common;

namespace Erp.Domain.Catalog;

public sealed class PriceBook : Entity
{
    private readonly List<PriceBookLine> _lines = [];

    private PriceBook()
    {
    }

    public PriceBook(Guid tenantId, string name, DateOnly effectiveFrom)
        : base(tenantId)
    {
        Name = name.Trim();
        if (Name.Length is 0 or > 120)
        {
            throw new DomainRuleException("VALIDATION_FAILED", "价格版本名称长度不正确");
        }

        EffectiveFrom = effectiveFrom;
        Status = PriceBookStatus.Draft;
    }

    public string Name { get; private set; } = string.Empty;

    public int Revision { get; private set; } = 1;

    public DateOnly EffectiveFrom { get; private set; }

    public PriceBookStatus Status { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public IReadOnlyCollection<PriceBookLine> Lines => _lines;

    public void SetPrice(Guid serviceItemId, long unitPriceMinor)
    {
        EnsureDraft();
        if (unitPriceMinor < 0)
        {
            throw new DomainRuleException("VALIDATION_FAILED", "价格不能小于0");
        }

        var existing = _lines.SingleOrDefault(x => x.ServiceItemId == serviceItemId);
        if (existing is null)
        {
            _lines.Add(new PriceBookLine(TenantId, Id, serviceItemId, unitPriceMinor));
        }
        else
        {
            existing.ChangePrice(unitPriceMinor);
        }

        Touch();
    }

    public void Publish(DateTimeOffset now)
    {
        EnsureDraft();
        if (_lines.Count == 0)
        {
            throw new DomainRuleException("VALIDATION_FAILED", "价格版本至少需要一个项目价格");
        }

        Status = PriceBookStatus.Published;
        PublishedAtUtc = now;
        Touch();
    }

    private void EnsureDraft()
    {
        if (Status != PriceBookStatus.Draft)
        {
            throw new DomainRuleException("STATE_TRANSITION_NOT_ALLOWED", "只有草稿价格版本可以修改");
        }
    }
}

public sealed class PriceBookLine : Entity
{
    private PriceBookLine()
    {
    }

    internal PriceBookLine(Guid tenantId, Guid priceBookId, Guid serviceItemId, long unitPriceMinor)
        : base(tenantId)
    {
        PriceBookId = priceBookId;
        ServiceItemId = serviceItemId;
        UnitPriceMinor = unitPriceMinor;
    }

    public Guid PriceBookId { get; private set; }

    public Guid ServiceItemId { get; private set; }

    public long UnitPriceMinor { get; private set; }

    internal void ChangePrice(long unitPriceMinor)
    {
        UnitPriceMinor = unitPriceMinor;
        Touch();
    }
}

public enum PriceBookStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3,
}

