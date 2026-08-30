namespace Erp.LegacyMigration;

public sealed class LegacyEndpointPolicy
{
    public static readonly Uri Origin = new("https://app5.siweicloud.com", UriKind.Absolute);

    private readonly Uri _origin = Origin;

    private static readonly HashSet<string> ForbiddenActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "adds", "batch", "drop", "delete", "edit", "import", "insert", "refund", "save", "update"
    };

    public void EnsureAllowed(HttpMethod method, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !string.Equals(uri.Host, _origin.Host, StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort)
        {
            throw new LegacyMigrationException("请求被安全策略拒绝：只允许旧系统固定 HTTPS 主机。");
        }

        var query = ParseQuery(uri.Query);
        var action = GetSingleValue(query, "act");

        if (method == HttpMethod.Get && IsReviewedPhotoRead(uri, query, action))
        {
            return;
        }

        if (action is not null && ForbiddenActions.Any(forbidden => action.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
        {
            throw new LegacyMigrationException("请求被安全策略拒绝：检测到写入动作。");
        }

        if (method == HttpMethod.Post &&
            string.Equals(uri.AbsolutePath, "/swshop/login/login.php", StringComparison.Ordinal) &&
            string.Equals(action, "login", StringComparison.Ordinal))
        {
            return;
        }

        // The nursing page obtains its read-only jqGrid column model through an empty POST
        // before issuing the GET that returns rows. This exact initialization endpoint has
        // been reviewed in the legacy UI; every other non-login POST remains denied.
        if (method == HttpMethod.Post &&
            string.Equals(uri.AbsolutePath, "/swshop/vip/nurse.php", StringComparison.Ordinal) &&
            string.Equals(action, "custom", StringComparison.Ordinal) &&
            query.Count == 1)
        {
            return;
        }

        if (method != HttpMethod.Get)
        {
            throw new LegacyMigrationException("请求被安全策略拒绝：除登录外只允许 GET。");
        }

        if (string.Equals(uri.AbsolutePath, "/swshop/login/login.php", StringComparison.Ordinal) &&
            (action is null || string.Equals(action, "showcode", StringComparison.Ordinal)))
        {
            return;
        }

        if (string.Equals(uri.AbsolutePath, "/swshop/public/code/image.php", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(uri.AbsolutePath, "/swshop/vip/nurse.php", StringComparison.Ordinal) &&
            action is null && query.Count == 0)
        {
            return;
        }

        if (action is null && query.Count == 0 && LegacyEntityCatalog.All.Any(entity =>
            entity.IncludeFullHistoryFilters && !entity.IsReport &&
            string.Equals(uri.AbsolutePath, entity.Path, StringComparison.Ordinal)))
        {
            return;
        }

        if (LegacyEntityCatalog.All.Any(entity =>
            string.Equals(uri.AbsolutePath, entity.Path, StringComparison.Ordinal) &&
            string.Equals(action, entity.Action, StringComparison.Ordinal)))
        {
            return;
        }

        throw new LegacyMigrationException("请求被安全策略拒绝：端点未列入只读白名单。");
    }

    private static bool IsReviewedPhotoRead(
        Uri uri,
        Dictionary<string, List<string>> query,
        string? action)
    {
        if (string.Equals(uri.AbsolutePath, "/swshop/base/member.php", StringComparison.Ordinal) &&
            string.Equals(action, "adds", StringComparison.Ordinal) &&
            query.Keys.All(key => key is "act" or "id" or "wintop" or "winpid") &&
            GetSingleValue(query, "id") is { } id && long.TryParse(id, out var numericId) && numericId > 0 &&
            GetSingleValue(query, "wintop") is "N" && GetSingleValue(query, "winpid") is "2")
        {
            return true;
        }

        if (string.Equals(uri.AbsolutePath, "/swshop/vip/nurse.php", StringComparison.Ordinal) &&
            string.Equals(action, "adds", StringComparison.Ordinal) &&
            query.Keys.All(key => key is "act" or "id" or "wintop" or "winpid") &&
            GetSingleValue(query, "id") is { } careId && long.TryParse(careId, out var numericCareId) &&
            numericCareId > 0 && GetSingleValue(query, "wintop") is "N" &&
            GetSingleValue(query, "winpid") is "1")
        {
            return true;
        }

        if (query.Count != 0 || !uri.AbsolutePath.StartsWith("/swshop/picture/", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 5 && segments[0] == "swshop" && segments[1] == "picture" &&
            segments[2].Length is > 0 and <= 32 && segments[2].All(char.IsLetterOrDigit) &&
            segments[3] is "member" or "nurse" && IsSafeImageFileName(segments[4]);
    }

    private static bool IsSafeImageFileName(string value)
    {
        if (value.Length is < 5 or > 160 || value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(value);
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".JPG" or ".JPEG" or ".PNG" or ".WEBP" &&
            value[..^extension.Length].All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
    }

    private static Dictionary<string, List<string>> ParseQuery(string query)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Decode(pair[0]);
            var value = pair.Length == 2 ? Decode(pair[1]) : string.Empty;
            if (!result.TryGetValue(key, out var values))
            {
                values = [];
                result.Add(key, values);
            }

            values.Add(value);
        }

        return result;
    }

    private static string? GetSingleValue(Dictionary<string, List<string>> query, string key)
    {
        if (!query.TryGetValue(key, out var values))
        {
            return null;
        }

        if (values.Count != 1)
        {
            throw new LegacyMigrationException("请求被安全策略拒绝：动作参数重复。");
        }

        return values[0];
    }

    private static string Decode(string value) =>
        Uri.UnescapeDataString(value.Replace('+', ' '));
}

public sealed record LegacyEntityDefinition(
    string Name,
    string Path,
    string Action,
    IReadOnlyDictionary<string, string>? FixedQuery = null,
    bool IncludeFullHistoryFilters = false)
{
    public static readonly LegacyEntityDefinition Customers = new(
        "customers",
        "/swshop/base/member.php",
        "grid");

    public static readonly LegacyEntityDefinition Stores = Base("stores", "shop");

    public static readonly LegacyEntityDefinition Employees = Base("employees", "emplee");

    public static readonly LegacyEntityDefinition Services = Base("services", "service");

    public static readonly LegacyEntityDefinition Products = Base("products", "product");

    public static readonly LegacyEntityDefinition ServicePasses = Base("service-passes", "numcard");

    public static readonly LegacyEntityDefinition MemberLevels = Base("member-levels", "iclevel");

    public static readonly LegacyEntityDefinition TopupPlans = Base("topup-plans", "icfull");

    public static readonly LegacyEntityDefinition Facilities = Base("facilities", "room");

    public static readonly LegacyEntityDefinition Brands = Base("brands", "brand");

    public static readonly LegacyEntityDefinition Units = Base("units", "unit");

    public static readonly LegacyEntityDefinition EmployeeTrades = Base("employee-trades", "ework");

    public static readonly LegacyEntityDefinition CustomerSources = Base("customer-sources", "source");

    public static readonly LegacyEntityDefinition CareRecords = new(
        "care-records",
        "/swshop/vip/nurse.php",
        "grid");

    public static readonly LegacyEntityDefinition CustomerSales = Report(
        "customer-sales",
        "vip_sell_list");

    public static readonly LegacyEntityDefinition CustomerTopups = Report(
        "customer-topups",
        "vip_full_list");

    public static readonly LegacyEntityDefinition ServicePassSales = Report(
        "service-pass-sales",
        "vip_fnum_list");

    public static readonly LegacyEntityDefinition CustomerArrears = Report(
        "customer-arrears",
        "vip_arrear_list");

    public static readonly LegacyEntityDefinition CustomerRepayments = Report(
        "customer-repayments",
        "vip_repay_list");

    public static readonly LegacyEntityDefinition ScoreAdjustments = Report(
        "score-adjustments",
        "vip_score_list");

    public static readonly LegacyEntityDefinition ScoreBalanceRedemptions = Report(
        "score-balance-redemptions",
        "vip_bonus_list");

    public static readonly LegacyEntityDefinition GiftRedemptions = Report(
        "gift-redemptions",
        "vip_gift_list");

    public static readonly LegacyEntityDefinition PurchaseOrders = Report(
        "purchase-orders",
        "buy_come_list",
        "S");

    public static readonly LegacyEntityDefinition PurchaseReturns = Report(
        "purchase-returns",
        "buy_come_list",
        "T");

    public static readonly LegacyEntityDefinition PurchaseSettlements = Report(
        "purchase-settlements",
        "buy_cpay_list");

    public static readonly LegacyEntityDefinition PurchaseFrontDeskOrders = Report(
        "purchase-front-desk-orders",
        "buy_front_list");

    public static readonly LegacyEntityDefinition SalesOrders = Report(
        "sales-orders",
        "sell_come_list",
        "S");

    public static readonly LegacyEntityDefinition SalesReturns = Report(
        "sales-returns",
        "sell_come_list",
        "T");

    public static readonly LegacyEntityDefinition SalesSettlements = Report(
        "sales-settlements",
        "sell_cpay_list");

    public static readonly LegacyEntityDefinition SalesFrontDeskOrders = Report(
        "sales-front-desk-orders",
        "sell_front_list");

    public static readonly LegacyEntityDefinition InventoryInbound = Report(
        "inventory-inbound",
        "depot_come_list",
        "S");

    public static readonly LegacyEntityDefinition InventoryOutbound = Report(
        "inventory-outbound",
        "depot_come_list",
        "T");

    public static readonly LegacyEntityDefinition InventoryFrontInbound = Report(
        "inventory-front-inbound",
        "depot_front_list",
        "S");

    public static readonly LegacyEntityDefinition InventoryFrontOutbound = Report(
        "inventory-front-outbound",
        "depot_front_list",
        "T");

    public static readonly LegacyEntityDefinition InventoryTransfers = Report(
        "inventory-transfers",
        "depot_move_list");

    public static readonly LegacyEntityDefinition InventoryPriceAdjustments = Report(
        "inventory-price-adjustments",
        "depot_price_list");

    public static readonly LegacyEntityDefinition InventoryCounts = Report(
        "inventory-counts",
        "depot_check_list");

    public static readonly LegacyEntityDefinition FundReceipts = Report(
        "fund-receipts",
        "fund_come_list");

    public static readonly LegacyEntityDefinition FundPayments = Report(
        "fund-payments",
        "fund_send_list");

    public static readonly LegacyEntityDefinition FundTransfers = Report(
        "fund-transfers",
        "fund_move_list");

    public static readonly LegacyEntityDefinition FundSettlements = Report(
        "fund-settlements",
        "fund_cpay_list");

    public static readonly LegacyEntityDefinition InventoryBalances = Report(
        "inventory-balances",
        "depot_number");

    public static readonly LegacyEntityDefinition CustomerServiceLines = Report(
        "customer-service-lines",
        "vip_sell_child",
        "service");

    public static readonly LegacyEntityDefinition CustomerProductLines = Report(
        "customer-product-lines",
        "vip_sell_child",
        "product");

    public static readonly LegacyEntityDefinition CustomerPassLines = Report(
        "customer-pass-lines",
        "vip_sell_child",
        "numcard");

    public static readonly LegacyEntityDefinition CustomerTopupLines = Report(
        "customer-topup-lines",
        "vip_full_child");

    public static readonly LegacyEntityDefinition ServicePassLines = Report(
        "service-pass-lines",
        "vip_fnum_child");

    public static readonly LegacyEntityDefinition PurchaseOrderLines = Report(
        "purchase-order-lines",
        "buy_come_child",
        "S");

    public static readonly LegacyEntityDefinition PurchaseReturnLines = Report(
        "purchase-return-lines",
        "buy_come_child",
        "T");

    public static readonly LegacyEntityDefinition PurchaseFrontDeskLines = Report(
        "purchase-front-desk-lines",
        "buy_front_child");

    public static readonly LegacyEntityDefinition SalesOrderLines = Report(
        "sales-order-lines",
        "sell_come_child",
        "S");

    public static readonly LegacyEntityDefinition SalesReturnLines = Report(
        "sales-return-lines",
        "sell_come_child",
        "T");

    public static readonly LegacyEntityDefinition SalesFrontDeskLines = Report(
        "sales-front-desk-lines",
        "sell_front_child");

    public static readonly LegacyEntityDefinition InventoryInboundLines = Report(
        "inventory-inbound-lines",
        "depot_come_child",
        "S");

    public static readonly LegacyEntityDefinition InventoryOutboundLines = Report(
        "inventory-outbound-lines",
        "depot_come_child",
        "T");

    public static readonly LegacyEntityDefinition InventoryTransferInLines = Report(
        "inventory-transfer-in-lines",
        "depot_move_child",
        "I");

    public static readonly LegacyEntityDefinition InventoryTransferOutLines = Report(
        "inventory-transfer-out-lines",
        "depot_move_child",
        "O");

    public static readonly LegacyEntityDefinition InventoryPriceAdjustmentLines = Report(
        "inventory-price-adjustment-lines",
        "depot_price_child");

    public static readonly LegacyEntityDefinition InventoryCountLines = Report(
        "inventory-count-lines",
        "depot_check_child");

    public static readonly LegacyEntityDefinition Appointments = DirectLedger(
        "appointments",
        "/swshop/pos/calend.php");

    public static readonly LegacyEntityDefinition PayrollData = DirectLedger(
        "payroll-data",
        "/swshop/pay/pdata.php");

    public static readonly LegacyEntityDefinition EmployeeCompensation = DirectLedger(
        "employee-compensation",
        "/swshop/pay/pmoney.php");

    public static readonly LegacyEntityDefinition EmployeeRewards = DirectLedger(
        "employee-rewards",
        "/swshop/pay/reward.php");

    public static readonly LegacyEntityDefinition EmployeeAssessments = DirectLedger(
        "employee-assessments",
        "/swshop/pay/assess.php");

    public static readonly LegacyEntityDefinition FundAccounts = DirectLedger(
        "fund-accounts",
        "/swshop/fund/pdata.php");

    public bool IsReport => Path.StartsWith("/swshop/print/", StringComparison.Ordinal);

    public Uri BuildPageUri(int page, int pageSize)
    {
        var queryParts = new List<string>
        {
            $"act={Uri.EscapeDataString(Action)}",
            "_search=false",
            $"nd={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            $"rows={pageSize}",
            $"page={page}",
            "sidx=",
            "sord=asc"
        };

        if (FixedQuery is not null)
        {
            queryParts.AddRange(FixedQuery.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        }

        // The nursing grid does not return its history unless the same reviewed search
        // parameters used by the UI are present. Use a bounded, deterministic full-history
        // interval so a migration run cannot silently produce an empty care-record export.
        if (this == CareRecords)
        {
            queryParts.AddRange(
            [
                "search_find=Y",
                "search_bdate=2019-01-01",
                $"search_edate={DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)):yyyy-MM-dd}",
                "search_area=0",
                "search_zone=0",
                "search_shop=0",
                $"search_shopv={Uri.EscapeDataString("全部")}",
                "search_nusort=0",
                "search_member="
            ]);
        }

        if (IncludeFullHistoryFilters)
        {
            queryParts.AddRange(
            [
                "search_find=Y",
                "search_bdate=2019-01-01",
                $"search_edate={DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)):yyyy-MM-dd}",
                "search_area=0",
                "search_zone=0",
                "search_shop=0",
                $"search_shopv={Uri.EscapeDataString("全部")}",
                "search_audit=",
                "search_form=",
                "search_member=",
                "search_code=",
                "search_memo="
            ]);
        }

        var query = string.Join('&', queryParts);

        return new Uri(LegacyEndpointPolicy.Origin, $"{Path}?{query}");
    }

    private static LegacyEntityDefinition Base(string name, string controller) =>
        new(name, $"/swshop/base/{controller}.php", "grid");

    private static LegacyEntityDefinition Report(string name, string controller, string type = "") =>
        new(
            name,
            $"/swshop/print/{controller}.php",
            "grid",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["type"] = type },
            IncludeFullHistoryFilters: true);

    private static LegacyEntityDefinition DirectLedger(string name, string path) =>
        new(name, path, "grid", IncludeFullHistoryFilters: true);
}

public static class LegacyEntityCatalog
{
    public const string BaseMasterSelection = "base-master";
    public const string CoreLedgersSelection = "core-ledgers";
    public const string OperationalLedgersSelection = "operational-ledgers";
    public const string LedgerLinesSelection = "ledger-lines";
    public const string SupplementalLedgersSelection = "supplemental-ledgers";

    public static IReadOnlyList<LegacyEntityDefinition> All { get; } =
    [
        LegacyEntityDefinition.Customers,
        LegacyEntityDefinition.Stores,
        LegacyEntityDefinition.Employees,
        LegacyEntityDefinition.Services,
        LegacyEntityDefinition.Products,
        LegacyEntityDefinition.ServicePasses,
        LegacyEntityDefinition.MemberLevels,
        LegacyEntityDefinition.TopupPlans,
        LegacyEntityDefinition.Facilities,
        LegacyEntityDefinition.Brands,
        LegacyEntityDefinition.Units,
        LegacyEntityDefinition.EmployeeTrades,
        LegacyEntityDefinition.CustomerSources,
        LegacyEntityDefinition.CareRecords,
        LegacyEntityDefinition.CustomerSales,
        LegacyEntityDefinition.CustomerTopups,
        LegacyEntityDefinition.ServicePassSales,
        LegacyEntityDefinition.CustomerArrears,
        LegacyEntityDefinition.CustomerRepayments,
        LegacyEntityDefinition.ScoreAdjustments,
        LegacyEntityDefinition.ScoreBalanceRedemptions,
        LegacyEntityDefinition.GiftRedemptions,
        LegacyEntityDefinition.PurchaseOrders,
        LegacyEntityDefinition.PurchaseReturns,
        LegacyEntityDefinition.PurchaseSettlements,
        LegacyEntityDefinition.PurchaseFrontDeskOrders,
        LegacyEntityDefinition.SalesOrders,
        LegacyEntityDefinition.SalesReturns,
        LegacyEntityDefinition.SalesSettlements,
        LegacyEntityDefinition.SalesFrontDeskOrders,
        LegacyEntityDefinition.InventoryInbound,
        LegacyEntityDefinition.InventoryOutbound,
        LegacyEntityDefinition.InventoryFrontInbound,
        LegacyEntityDefinition.InventoryFrontOutbound,
        LegacyEntityDefinition.InventoryTransfers,
        LegacyEntityDefinition.InventoryPriceAdjustments,
        LegacyEntityDefinition.InventoryCounts,
        LegacyEntityDefinition.FundReceipts,
        LegacyEntityDefinition.FundPayments,
        LegacyEntityDefinition.FundTransfers,
        LegacyEntityDefinition.FundSettlements,
        LegacyEntityDefinition.InventoryBalances,
        LegacyEntityDefinition.CustomerServiceLines,
        LegacyEntityDefinition.CustomerProductLines,
        LegacyEntityDefinition.CustomerPassLines,
        LegacyEntityDefinition.CustomerTopupLines,
        LegacyEntityDefinition.ServicePassLines,
        LegacyEntityDefinition.PurchaseOrderLines,
        LegacyEntityDefinition.PurchaseReturnLines,
        LegacyEntityDefinition.PurchaseFrontDeskLines,
        LegacyEntityDefinition.SalesOrderLines,
        LegacyEntityDefinition.SalesReturnLines,
        LegacyEntityDefinition.SalesFrontDeskLines,
        LegacyEntityDefinition.InventoryInboundLines,
        LegacyEntityDefinition.InventoryOutboundLines,
        LegacyEntityDefinition.InventoryTransferInLines,
        LegacyEntityDefinition.InventoryTransferOutLines,
        LegacyEntityDefinition.InventoryPriceAdjustmentLines,
        LegacyEntityDefinition.InventoryCountLines,
        LegacyEntityDefinition.Appointments,
        LegacyEntityDefinition.PayrollData,
        LegacyEntityDefinition.EmployeeCompensation,
        LegacyEntityDefinition.EmployeeRewards,
        LegacyEntityDefinition.EmployeeAssessments,
        LegacyEntityDefinition.FundAccounts
    ];

    public static IReadOnlyList<LegacyEntityDefinition> BaseMasterData { get; } =
        All.Where(entity => entity.Path.StartsWith("/swshop/base/", StringComparison.Ordinal) &&
                            entity != LegacyEntityDefinition.Customers)
            .ToArray();

    public static IReadOnlyList<LegacyEntityDefinition> CoreLedgers { get; } =
    [
        LegacyEntityDefinition.CustomerSales,
        LegacyEntityDefinition.CustomerTopups,
        LegacyEntityDefinition.ServicePassSales,
        LegacyEntityDefinition.CustomerArrears,
        LegacyEntityDefinition.CustomerRepayments,
        LegacyEntityDefinition.ScoreAdjustments,
        LegacyEntityDefinition.ScoreBalanceRedemptions,
        LegacyEntityDefinition.GiftRedemptions
    ];

    public static IReadOnlyList<LegacyEntityDefinition> OperationalLedgers { get; } =
    [
        LegacyEntityDefinition.PurchaseOrders,
        LegacyEntityDefinition.PurchaseReturns,
        LegacyEntityDefinition.PurchaseSettlements,
        LegacyEntityDefinition.PurchaseFrontDeskOrders,
        LegacyEntityDefinition.SalesOrders,
        LegacyEntityDefinition.SalesReturns,
        LegacyEntityDefinition.SalesSettlements,
        LegacyEntityDefinition.SalesFrontDeskOrders,
        LegacyEntityDefinition.InventoryInbound,
        LegacyEntityDefinition.InventoryOutbound,
        LegacyEntityDefinition.InventoryFrontInbound,
        LegacyEntityDefinition.InventoryFrontOutbound,
        LegacyEntityDefinition.InventoryTransfers,
        LegacyEntityDefinition.InventoryPriceAdjustments,
        LegacyEntityDefinition.InventoryCounts,
        LegacyEntityDefinition.FundReceipts,
        LegacyEntityDefinition.FundPayments,
        LegacyEntityDefinition.FundTransfers,
        LegacyEntityDefinition.FundSettlements,
        LegacyEntityDefinition.InventoryBalances
    ];

    public static IReadOnlyList<LegacyEntityDefinition> LedgerLines { get; } =
        All.Where(entity => entity.IsReport && !CoreLedgers.Contains(entity) &&
                            !OperationalLedgers.Contains(entity)).ToArray();

    public static IReadOnlyList<LegacyEntityDefinition> SupplementalLedgers { get; } =
    [
        LegacyEntityDefinition.Appointments,
        LegacyEntityDefinition.PayrollData,
        LegacyEntityDefinition.EmployeeCompensation,
        LegacyEntityDefinition.EmployeeRewards,
        LegacyEntityDefinition.EmployeeAssessments,
        LegacyEntityDefinition.FundAccounts
    ];

    public static IReadOnlyList<LegacyEntityDefinition> Resolve(string selection)
    {
        if (string.Equals(selection, BaseMasterSelection, StringComparison.Ordinal))
        {
            return BaseMasterData;
        }

        if (string.Equals(selection, CoreLedgersSelection, StringComparison.Ordinal))
        {
            return CoreLedgers;
        }

        if (string.Equals(selection, OperationalLedgersSelection, StringComparison.Ordinal))
        {
            return OperationalLedgers;
        }

        if (string.Equals(selection, LedgerLinesSelection, StringComparison.Ordinal))
        {
            return LedgerLines;
        }

        if (string.Equals(selection, SupplementalLedgersSelection, StringComparison.Ordinal))
        {
            return SupplementalLedgers;
        }

        var entity = All.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, selection, StringComparison.Ordinal));
        return entity is null
            ? throw new LegacyMigrationException(
                $"未登记的只读实体：{selection}。可用值：{string.Join(", ", All.Select(item => item.Name))}, {BaseMasterSelection}, {CoreLedgersSelection}, {OperationalLedgersSelection}, {LedgerLinesSelection}, {SupplementalLedgersSelection}")
            : [entity];
    }
}
