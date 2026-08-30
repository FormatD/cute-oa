namespace Oa.Api.Domain;

public sealed record PositionOption(string Id, string Name, string DepartmentId, string DepartmentName);
public sealed record PositionView(
    string Id,
    string Name,
    string DepartmentId,
    string DepartmentName,
    int SortOrder,
    string Status,
    bool IsSystem,
    int Version,
    int UserCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreatePositionRequest(string Id, string Name, string DepartmentId, int SortOrder = 0);
public sealed record UpdatePositionRequest(string Name, string DepartmentId, int SortOrder, string Status, int Version);
