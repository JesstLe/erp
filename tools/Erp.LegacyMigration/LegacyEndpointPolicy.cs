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

    public static readonly LegacyEntityDefinition CareRecords = new(
        "care-records",
        "/swshop/vip/nurse.php",
        "grid");

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

        var query = string.Join('&', queryParts);

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
        LegacyEntityDefinition.CustomerSources,
        LegacyEntityDefinition.CareRecords
    ];

    public static IReadOnlyList<LegacyEntityDefinition> BaseMasterData { get; } =
        All.Where(entity => entity != LegacyEntityDefinition.Customers && entity != LegacyEntityDefinition.CareRecords)
            .ToArray();

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
