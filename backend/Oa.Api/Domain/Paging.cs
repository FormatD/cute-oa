namespace Oa.Api.Domain;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize, int TotalPages);

public static class Paging
{
    public static PagedResponse<T> Create<T>(IReadOnlyList<T> source, int? requestedPage, int? requestedPageSize)
    {
        var pageSize = Math.Clamp(requestedPageSize ?? 20, 1, 100);
        var total = source.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)pageSize));
        var page = Math.Clamp(requestedPage ?? 1, 1, totalPages);
        return new PagedResponse<T>(source.Skip((page - 1) * pageSize).Take(pageSize).ToList(), total, page, pageSize, totalPages);
    }
}
