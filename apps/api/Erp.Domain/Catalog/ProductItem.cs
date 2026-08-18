using Erp.Domain.Common;

namespace Erp.Domain.Catalog;

public sealed class ProductItem : Entity
{
    private ProductItem()
    {
    }

    public ProductItem(Guid tenantId, string code, string name, string unitName, bool trackInventory)
        : base(tenantId)
    {
        Code = Normalize(code, 40, "产品编码").ToUpperInvariant();
        Name = Normalize(name, 120, "产品名称");
        UnitName = Normalize(unitName, 20, "计量单位");
        TrackInventory = trackInventory;
        Status = CatalogItemStatus.Enabled;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string UnitName { get; private set; } = string.Empty;

    public bool TrackInventory { get; private set; }

    public Guid? ImageFileId { get; private set; }

    public CatalogItemStatus Status { get; private set; }

    public void Update(string name, string unitName, bool trackInventory)
    {
        Name = Normalize(name, 120, "产品名称");
        UnitName = Normalize(unitName, 20, "计量单位");
        TrackInventory = trackInventory;
        Touch();
    }

    public void Enable()
    {
        Status = CatalogItemStatus.Enabled;
        Touch();
    }

    public void Disable()
    {
        Status = CatalogItemStatus.Disabled;
        Touch();
    }

    public void SetImage(Guid fileId)
    {
        if (fileId == Guid.Empty)
            throw new DomainRuleException("VALIDATION_FAILED", "产品图片文件无效");
        ImageFileId = fileId;
        Touch();
    }

    private static string Normalize(string value, int maxLength, string field)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 || normalized.Length > maxLength)
            throw new DomainRuleException("VALIDATION_FAILED", $"{field}长度不正确");
        return normalized;
    }
}
