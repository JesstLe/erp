namespace Erp.Application.Security;

public sealed record CurrentRequestContext(Guid UserId, Guid TenantId, Guid StoreId, IReadOnlySet<string> Roles);

public interface ICurrentRequestContextAccessor
{
    CurrentRequestContext? Current { get; }
}

public static class SystemRoles
{
    public const string Owner = "OWNER";
    public const string StoreManager = "STORE_MANAGER";
    public const string FrontDesk = "FRONT_DESK";
    public const string Cashier = "CASHIER";
    public const string Technician = "TECHNICIAN";
}

public static class SystemActions
{
    public const string CatalogRead = "catalog.read";
    public const string CatalogWrite = "catalog.write";
    public const string PricePublish = "price.publish";
    public const string FacilityOperate = "facility.operate";
    public const string CustomerRead = "customer.read";
    public const string CustomerWrite = "customer.write";
    public const string MembershipOpen = "membership.open";
    public const string CashierCheckout = "cashier.checkout";
    public const string AuditRead = "audit.read";
}
