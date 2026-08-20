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

        if (LegacyEntityCatalog.All.Any(entity =>
            string.Equals(uri.AbsolutePath, entity.Path, StringComparison.Ordinal) &&
            string.Equals(action, entity.Action, StringComparison.Ordinal)))
        {
            return;
        }

        throw new LegacyMigrationException("请求被安全策略拒绝：端点未列入只读白名单。");
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

public sealed record LegacyEntityDefinition(string Name, string Path, string Action)
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

    public Uri BuildPageUri(int page, int pageSize)
    {
        var query = string.Join(
            '&',
            $"act={Uri.EscapeDataString(Action)}",
            "_search=false",
            $"nd={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            $"rows={pageSize}",
            $"page={page}",
            "sidx=",
            "sord=asc");

        return new Uri(LegacyEndpointPolicy.Origin, $"{Path}?{query}");
    }

    private static LegacyEntityDefinition Base(string name, string controller) =>
        new(name, $"/swshop/base/{controller}.php", "grid");
}

public static class LegacyEntityCatalog
{
    public const string BaseMasterSelection = "base-master";

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
        LegacyEntityDefinition.CustomerSources
    ];

    public static IReadOnlyList<LegacyEntityDefinition> BaseMasterData { get; } =
        All.Where(entity => entity != LegacyEntityDefinition.Customers).ToArray();

    public static IReadOnlyList<LegacyEntityDefinition> Resolve(string selection)
    {
        if (string.Equals(selection, BaseMasterSelection, StringComparison.Ordinal))
        {
            return BaseMasterData;
        }

        var entity = All.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, selection, StringComparison.Ordinal));
        return entity is null
            ? throw new LegacyMigrationException(
                $"未登记的只读实体：{selection}。可用值：{string.Join(", ", All.Select(item => item.Name))}, {BaseMasterSelection}")
            : [entity];
    }
}
