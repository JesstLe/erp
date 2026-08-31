using Erp.Domain.Common;

namespace Erp.Domain.Organization;

public sealed class Store : Entity
{
    private Store()
    {
    }

    public Store(Guid tenantId, string code, string name, string timeZoneId = "Asia/Shanghai",
        string? address = null)
        : base(tenantId)
    {
        Code = Normalize(code, 32, nameof(code)).ToUpperInvariant();
        Name = Normalize(name, 100, nameof(name));
        TimeZoneId = Normalize(timeZoneId, 64, nameof(timeZoneId));
        Address = Optional(address, 300, "门店地址");
        Status = StoreStatus.Enabled;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string TimeZoneId { get; private set; } = "Asia/Shanghai";

    public string? Address { get; private set; }

    public StoreStatus Status { get; private set; }

    public void Rename(string name)
    {
        Name = Normalize(name, 100, nameof(name));
        Touch();
    }

    public void UpdateProfile(string code, string name, string timeZoneId, string? address = null)
    {
        var normalizedCode = Normalize(code, 32, nameof(code)).ToUpperInvariant();
        if (!string.Equals(Code, normalizedCode, StringComparison.Ordinal))
        {
            throw new DomainRuleException("STORE_CODE_IMMUTABLE", "门店编码创建后不可修改");
        }

        Name = Normalize(name, 100, nameof(name));
        TimeZoneId = Normalize(timeZoneId, 64, nameof(timeZoneId));
        Address = Optional(address, 300, "门店地址");
        Touch();
    }

    public void Disable()
    {
        if (Status == StoreStatus.Disabled) return;
        Status = StoreStatus.Disabled;
        Touch();
    }

    public void Enable()
    {
        if (Status == StoreStatus.Enabled) return;
        Status = StoreStatus.Enabled;
        Touch();
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

    private static string? Optional(string? value, int maxLength, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maxLength)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}最多{maxLength}个字符");
        return normalized;
    }
}

public enum StoreStatus
{
    Enabled = 1,
    Disabled = 2,
}
