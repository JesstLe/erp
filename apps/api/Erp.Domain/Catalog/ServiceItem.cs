using Erp.Domain.Common;

namespace Erp.Domain.Catalog;

public sealed class ServiceItem : Entity
{
    private ServiceItem()
    {
    }

    public ServiceItem(Guid tenantId, string code, string name, int standardDurationMinutes)
        : base(tenantId)
    {
        Code = Normalize(code, 40, nameof(code));
        Name = Normalize(name, 120, nameof(name));
        SetStandardDuration(standardDurationMinutes);
        Status = CatalogItemStatus.Enabled;
        CommissionMode = CommissionMode.None;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int StandardDurationMinutes { get; private set; }

    public CatalogItemStatus Status { get; private set; }

    public CommissionMode CommissionMode { get; private set; }

    public int? CommissionRateBasisPoints { get; private set; }

    public long? CommissionFixedMinor { get; private set; }

    public void Update(string name, int standardDurationMinutes)
    {
        Name = Normalize(name, 120, nameof(name));
        SetStandardDuration(standardDurationMinutes);
        Touch();
    }

    public void Disable()
    {
        Status = CatalogItemStatus.Disabled;
        Touch();
    }

    public void Enable()
    {
        Status = CatalogItemStatus.Enabled;
        Touch();
    }

    public void ConfigureCommission(CommissionMode mode, int? rateBasisPoints, long? fixedMinor)
    {
        switch (mode)
        {
            case CommissionMode.None when rateBasisPoints is null && fixedMinor is null:
                break;
            case CommissionMode.Percentage when rateBasisPoints is >= 1 and <= 10_000 && fixedMinor is null:
                break;
            case CommissionMode.FixedAmount when fixedMinor is >= 1 and <= 10_000_000_000 && rateBasisPoints is null:
                break;
            default:
                throw new DomainRuleException("VALIDATION_FAILED", "提成规则必须选择不计提、0.01%精度的比例或固定金额中的一种");
        }

        CommissionMode = mode;
        CommissionRateBasisPoints = rateBasisPoints;
        CommissionFixedMinor = fixedMinor;
        Touch();
    }

    private void SetStandardDuration(int minutes)
    {
        if (minutes is < 0 or > 1440)
        {
            throw new DomainRuleException("VALIDATION_FAILED", "标准时长必须在0到1440分钟之间");
        }

        StandardDurationMinutes = minutes;
    }

    private static string Normalize(string value, int maxLength, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maxLength)
        {
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        }

        return normalized;
    }
}

public enum CatalogItemStatus
{
    Enabled = 1,
    Disabled = 2,
}

public enum CommissionMode
{
    None = 0,
    Percentage = 1,
    FixedAmount = 2,
}
