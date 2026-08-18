namespace Erp.Application.Common;

public sealed record PageResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class Pagination
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public static bool TryNormalize(int? page, int? pageSize, out int normalizedPage,
        out int normalizedPageSize)
    {
        normalizedPage = page ?? 1;
        normalizedPageSize = pageSize ?? DefaultPageSize;
        return normalizedPage > 0 && normalizedPageSize > 0 && normalizedPageSize <= MaximumPageSize;
    }
}
