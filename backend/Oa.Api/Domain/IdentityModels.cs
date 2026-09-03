namespace Oa.Api.Domain;

public sealed record RoleView(string Code, string Name, bool IsSystem, int UserCount, IReadOnlyList<string> Permissions, IReadOnlyDictionary<string, string> DataScopes);
public sealed record PermissionView(string Code, string Name, string Description);
public sealed record CreateRoleRequest(string Code, string Name, IReadOnlyList<string> Permissions, IReadOnlyDictionary<string, string>? DataScopes = null);
public sealed record UpdateRoleRequest(string Code, string Name, IReadOnlyList<string> Permissions, IReadOnlyDictionary<string, string>? DataScopes = null);
public sealed record ManagedUserView(string Id, string Name, string DepartmentId, string DepartmentName, string? ManagerId, string? ManagerName, int CumulativeWorkYears, string Status, int Version, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? PositionId = null, string? PositionName = null, bool MfaEnabled = false, bool MustChangePassword = false);
public sealed record CreateManagedUserRequest(
    string Id,
    string Name,
    string DepartmentId,
    string? ManagerId,
    int CumulativeWorkYears,
    IReadOnlyList<string> Roles,
    string Password,
    string? PositionId = null,
    string? EmployeeNumber = null,
    DateOnly? HireDate = null,
    string? EmploymentType = null,
    string? PersonnelStatus = null,
    DateOnly? ProbationEndDate = null,
    DateOnly? CumulativeWorkStartDate = null);
public sealed record UpdateManagedUserRequest(string Name, string DepartmentId, string? ManagerId, int CumulativeWorkYears, string Status, IReadOnlyList<string> Roles, int Version, string? PositionId = null);
public sealed record ResetUserPasswordRequest(string Password);
