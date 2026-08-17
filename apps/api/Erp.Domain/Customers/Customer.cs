using Erp.Domain.Common;

namespace Erp.Domain.Customers;

public enum CustomerStatus { Active, Disabled, Merged }
public enum CustomerGender { Unknown, Female, Male, Other }

public sealed class Customer : Entity
{
    private Customer() { }

    public Customer(Guid tenantId, Guid homeStoreId, string name, string mobileCiphertext, byte[] mobileLookupHash,
        string mobileLastFour, CustomerGender gender, DateOnly? birthDate, string? sourceCode,
        bool serviceNotificationConsent, bool marketingConsent, DateOnly currentDate)
        : base(tenantId)
    {
        HomeStoreId = homeStoreId;
        Name = RequireText(name, 100, "顾客姓名");
        MobileCiphertext = RequireText(mobileCiphertext, 2048, "手机号密文");
        MobileLookupHash = mobileLookupHash.Length == 32 ? mobileLookupHash : throw new DomainRuleException("VALIDATION_FAILED", "手机号查询摘要无效");
        MobileLastFour = mobileLastFour.Length == 4 && mobileLastFour.All(char.IsDigit)
            ? mobileLastFour
            : throw new DomainRuleException("VALIDATION_FAILED", "手机号尾号无效");
        if (birthDate > currentDate)
            throw new DomainRuleException("VALIDATION_FAILED", "生日不能晚于今天");
        Gender = gender;
        BirthDate = birthDate;
        SourceCode = OptionalText(sourceCode, 40);
        ServiceNotificationConsent = serviceNotificationConsent;
        MarketingConsent = marketingConsent;
        Status = CustomerStatus.Active;
    }

    public Guid HomeStoreId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string MobileCiphertext { get; private set; } = string.Empty;
    public byte[] MobileLookupHash { get; private set; } = [];
    public string MobileLastFour { get; private set; } = string.Empty;
    public CustomerGender Gender { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public string? SourceCode { get; private set; }
    public bool ServiceNotificationConsent { get; private set; }
    public bool MarketingConsent { get; private set; }
    public CustomerStatus Status { get; private set; }

    private static string RequireText(string value, int max, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > max)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }

    private static string? OptionalText(string? value, int max)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > max) throw new DomainRuleException("VALIDATION_FAILED", "可选字段长度超限");
        return normalized;
    }
}
