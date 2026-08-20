using Erp.Domain.Common;

namespace Erp.Domain.Organization;

public sealed class Tenant : Entity
{
    private Tenant()
    {
    }

    public Tenant(string code, string name)
        : base(Guid.Empty)
    {
        Code = Require(code, 32, nameof(code)).ToUpperInvariant();
        Name = Require(name, 100, nameof(name));
        TenantId = Id;
        Status = TenantStatus.Enabled;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; }

    public void UpdateProfile(string code, string name)
    {
        var normalizedCode = Require(code, 32, nameof(code)).ToUpperInvariant();
        if (!string.Equals(Code, normalizedCode, StringComparison.Ordinal))
        {
            throw new DomainRuleException("TENANT_CODE_IMMUTABLE", "品牌编码创建后不可修改");
        }

        Name = Require(name, 100, nameof(name));
        Touch();
    }

    public void ChangeStatus(bool enable)
    {
        Status = enable ? TenantStatus.Enabled : TenantStatus.Disabled;
        Touch();
    }

    private static string Require(string value, int maxLength, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maxLength)
        {
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        }

        return normalized;
    }
}

public enum TenantStatus
{
    Enabled = 1,
    Disabled = 2,
}
