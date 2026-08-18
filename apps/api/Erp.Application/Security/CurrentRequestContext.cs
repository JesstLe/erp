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

public static class SystemPermissions
{
    public const string DashboardRead = "dashboard.read";
    public const string CatalogRead = "catalog.read";
    public const string CatalogWrite = "catalog.write";
    public const string PricePublish = "price.publish";
    public const string FacilityOperate = "facility.operate";
    public const string FacilityConfigure = "facility.configure";
    public const string FacilityConfigureAllStores = "facility.configure.all-stores";
    public const string SchedulingOperate = "scheduling.operate";
    public const string SchedulingShiftManage = "scheduling.shift.manage";
    public const string CustomerRead = "customer.read";
    public const string CustomerWrite = "customer.write";
    public const string CustomerManage = "customer.manage";
    public const string CustomerExport = "customer.export";
    public const string CustomerMerge = "customer.merge";
    public const string CustomerExportFullMobile = "customer.export.full-mobile";
    public const string MembershipOpen = "membership.open";
    public const string MembershipCardTypeManage = "membership.card-type.manage";
    public const string MembershipTopup = "membership.topup";
    public const string MembershipManage = "membership.manage";
    public const string MembershipAdmin = "membership.admin";
    public const string MembershipGrantBonus = "membership.grant-bonus";
    public const string MembershipReverse = "membership.reverse";
    public const string ServiceRecordManage = "service-record.manage";
    public const string CashierCheckout = "cashier.checkout";
    public const string CashierApprovePrice = "cashier.approve-price";
    public const string RefundApprove = "refund.approve";
    public const string RefundRequest = "refund.request";
    public const string ShiftReview = "shift.review";
    public const string InventoryRead = "inventory.read";
    public const string InventoryWrite = "inventory.write";
    public const string SupplyChainRead = "supply-chain.read";
    public const string SupplyChainOperate = "supply-chain.operate";
    public const string SupplyChainManage = "supply-chain.manage";
    public const string ReportRead = "report.read";
    public const string AuditRead = "audit.read";
    public const string OrganizationManage = "organization.manage";
    public const string EmployeeManage = "employee.manage";
    public const string PaymentChannelRead = "payment-channel.read";
    public const string PaymentChannelManage = "payment-channel.manage";

    private static readonly Dictionary<string, HashSet<string>> ByRole =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SystemRoles.Owner] = Set(
                DashboardRead, CatalogRead, CatalogWrite, PricePublish, FacilityOperate, FacilityConfigure,
                FacilityConfigureAllStores, SchedulingOperate, SchedulingShiftManage, CustomerRead,
                CustomerWrite, CustomerManage, CustomerExport, CustomerMerge, CustomerExportFullMobile,
                MembershipOpen, MembershipCardTypeManage, MembershipTopup, MembershipManage, MembershipAdmin,
                MembershipGrantBonus, MembershipReverse, CashierCheckout, CashierApprovePrice, RefundApprove,
                RefundRequest, ServiceRecordManage,
                ShiftReview, InventoryRead, InventoryWrite, SupplyChainRead, SupplyChainOperate, SupplyChainManage, ReportRead,
                AuditRead, OrganizationManage, EmployeeManage, PaymentChannelRead, PaymentChannelManage),
            [SystemRoles.StoreManager] = Set(
                DashboardRead, CatalogRead, FacilityOperate, FacilityConfigure, SchedulingOperate,
                SchedulingShiftManage, CustomerRead, CustomerWrite, CustomerManage, CustomerExport,
                MembershipOpen, MembershipTopup, MembershipManage, MembershipAdmin, ServiceRecordManage, CashierCheckout,
                RefundRequest,
                ShiftReview, InventoryRead, SupplyChainRead, SupplyChainOperate, ReportRead, AuditRead, PaymentChannelRead),
            [SystemRoles.FrontDesk] = Set(
                DashboardRead, CatalogRead, FacilityOperate, SchedulingOperate, CustomerRead, CustomerWrite),
            [SystemRoles.Cashier] = Set(
                DashboardRead, CatalogRead, CustomerRead, CustomerWrite, MembershipTopup, MembershipManage,
                CashierCheckout),
            [SystemRoles.Technician] = Set(DashboardRead, CatalogRead),
        };

    public static IReadOnlyList<string> Resolve(IEnumerable<string> roles) => roles
        .SelectMany(role => ByRole.TryGetValue(role, out var permissions)
            ? permissions
            : Enumerable.Empty<string>())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(permission => permission, StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<string> All => ForRole(SystemRoles.Owner);

    public static IReadOnlyList<string> ForRole(string role) =>
        ByRole.TryGetValue(role, out var permissions)
            ? permissions.OrderBy(permission => permission, StringComparer.Ordinal).ToArray()
            : [];

    private static HashSet<string> Set(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.Ordinal);
}
