using Erp.Domain.Common;

namespace Erp.Domain.Organization;

public sealed class UserStore : Entity
{
    private UserStore()
    {
    }

    public UserStore(Guid tenantId, Guid userId, Guid storeId, bool isDefault)
        : base(tenantId)
    {
        UserId = userId;
        StoreId = storeId;
        IsDefault = isDefault;
    }

    public Guid UserId { get; private set; }

    public Guid StoreId { get; private set; }

    public bool IsDefault { get; private set; }
}

