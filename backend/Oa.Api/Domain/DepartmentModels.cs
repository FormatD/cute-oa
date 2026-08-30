namespace Oa.Api.Domain;

public sealed record DepartmentView(
    string Id,
    string Name,
    string? ParentId,
    string? ParentName,
    int Depth,
    int SortOrder,
    bool IsSystem,
    int Version,
    int UserCount,
    int ChildCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateDepartmentRequest(string Id, string Name, string ParentId, int SortOrder = 0);
public sealed record UpdateDepartmentRequest(string Name, string? ParentId, int SortOrder, int Version);
