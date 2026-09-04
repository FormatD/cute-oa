using Microsoft.EntityFrameworkCore;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed record Department(string Id, string Name, string? ParentId);
public sealed record Employee(string Id, string Name, string Role, string DepartmentId, string DepartmentName, string? ManagerId, int CumulativeWorkYears, string Status, IReadOnlyList<string>? Roles = null, IReadOnlyList<string>? Permissions = null, string? PositionId = null, string? PositionName = null);
public sealed record UserProfile(string Id, string Name, string Role, string DepartmentName, IReadOnlyList<string>? Roles = null, IReadOnlyList<string>? Permissions = null, string? DepartmentId = null);
public sealed record DirectoryEmployee(string Id, string Name, string Role, string DepartmentId, string DepartmentName, string? ManagerName, string? PositionId = null, string? PositionName = null);

public sealed class DemoData
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> rolePermissions;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> roleDataScopes;
    public string Tenant => IdentityDefaults.TenantName;
    public IReadOnlyList<Department> Departments { get; }
    public IReadOnlyList<Employee> Employees { get; }

    public DemoData()
    {
        rolePermissions = IdentityDefaults.RolePermissions;
        roleDataScopes = IdentityDefaults.RoleDataScopes;
        Departments = IdentityDefaults.Departments;
        var positionNames = IdentityDefaults.Positions.ToDictionary(item => item.Id, item => item.Name);
        Employees = IdentityDefaults.Employees.Select(WithDefaultSecurity).Select(employee =>
        {
            var positionId = IdentityDefaults.UserPositions.GetValueOrDefault(employee.Id);
            return employee with { PositionId = positionId, PositionName = positionId is null ? null : positionNames.GetValueOrDefault(positionId) };
        }).ToList();
    }

    public DemoData(OaDbContext db)
    {
        Departments = db.Departments.AsNoTracking().Where(item => item.TenantId == IdentityDefaults.TenantId)
            .OrderBy(item => item.ParentId).ThenBy(item => item.Name)
            .Select(item => new Department(item.Id, item.Name, item.ParentId)).ToList();
        var roles = db.UserRoles.AsNoTracking().ToList().ToLookup(item => item.UserId);
        var permissions = db.RolePermissions.AsNoTracking().ToList().ToLookup(item => item.RoleCode);
        rolePermissions = db.Roles.AsNoTracking().Where(item => item.TenantId == IdentityDefaults.TenantId).Select(item => item.Code).ToList()
            .ToDictionary(code => code, code => (IReadOnlyList<string>)permissions[code].Select(item => item.PermissionCode).Distinct().ToList());
        var scopes = db.RoleDataScopes.AsNoTracking().ToList().ToLookup(item => item.RoleCode);
        roleDataScopes = rolePermissions.Keys.ToDictionary(code => code, code => (IReadOnlyDictionary<string, string>)scopes[code].ToDictionary(item => item.ResourceType, item => item.Scope));
        var departmentNames = Departments.ToDictionary(item => item.Id, item => item.Name);
        var positionNames = db.Positions.AsNoTracking().Where(item => item.TenantId == IdentityDefaults.TenantId).ToDictionary(item => item.Id, item => item.Name);
        Employees = db.Users.AsNoTracking().Where(item => item.TenantId == IdentityDefaults.TenantId).OrderBy(item => item.CreatedAt).ToList().Select(item =>
        {
            var assignedRoles = roles[item.Id].OrderByDescending(role => role.IsPrimary).ThenBy(role => role.RoleCode).Select(role => role.RoleCode).ToList();
            var primaryRole = roles[item.Id].FirstOrDefault(role => role.IsPrimary)?.RoleCode ?? assignedRoles.FirstOrDefault() ?? "员工";
            var assignedPermissions = assignedRoles.SelectMany(role => permissions[role].Select(permission => permission.PermissionCode)).Distinct().OrderBy(value => value).ToList();
            return new Employee(item.Id, item.Name, primaryRole, item.DepartmentId, departmentNames.GetValueOrDefault(item.DepartmentId, item.DepartmentId), item.ManagerId, item.CumulativeWorkYears, item.Status, assignedRoles, assignedPermissions, item.PositionId, item.PositionId is null ? null : positionNames.GetValueOrDefault(item.PositionId));
        }).ToList();
    }

    public Employee GetEmployee(string id) => Employees.Single(item => item.Id == id);
    public Employee? FindEmployee(string id) => Employees.SingleOrDefault(item => item.Id == id);
    public IReadOnlyList<Employee> ActiveEmployees => Employees.Where(item => item.Status == "ACTIVE").ToList();
    public bool HasRole(Employee employee, string role) => employee.Roles?.Contains(role, StringComparer.Ordinal) == true || employee.Role == role;
    public bool HasPermission(Employee employee, string permission) => employee.Permissions?.Contains(permission, StringComparer.Ordinal) == true;

    public bool IsDepartmentWithin(string departmentId, string scopeDepartmentId)
    {
        string? current = departmentId;
        while (current is not null)
        {
            if (current == scopeDepartmentId) return true;
            current = Departments.SingleOrDefault(item => item.Id == current)?.ParentId;
        }
        return false;
    }

    public UserProfile ToProfile(Employee employee) => new(employee.Id, employee.Name, employee.Role, employee.DepartmentName, employee.Roles ?? [employee.Role], employee.Permissions ?? [], employee.DepartmentId);
    public IReadOnlyList<DirectoryEmployee> DirectoryEmployees => ActiveEmployees.Select(item => new DirectoryEmployee(item.Id, item.Name, item.Role, item.DepartmentId, item.DepartmentName, item.ManagerId is null ? null : FindEmployee(item.ManagerId)?.Name, item.PositionId, item.PositionName)).ToList();
    public bool CanView(Employee actor, Employee subject, string resourceType)
    {
        if (actor.Id == subject.Id || IsManagerOf(actor.Id, subject)) return true;
        var scope = EffectiveDataScope(actor, resourceType);
        return scope switch
        {
            OaDataScopes.Company => true,
            OaDataScopes.Department => actor.DepartmentId == subject.DepartmentId,
            OaDataScopes.DepartmentAndChildren => IsDepartmentWithin(subject.DepartmentId, actor.DepartmentId),
            _ => false
        };
    }

    public string EffectiveDataScope(Employee actor, string resourceType)
    {
        var requiredPermission = resourceType switch
        {
            "Leave" => OaPermissions.LeaveScopeView,
            "Travel" => OaPermissions.TravelScopeView,
            "Personnel" => OaPermissions.PersonnelScopeView,
            "Attendance" => OaPermissions.AttendanceScopeView,
            "Contract" => OaPermissions.ContractScopeView,
            "Purchase" => OaPermissions.PurchaseScopeView,
            "Seal" => OaPermissions.SealScopeView,
            "Document" => OaPermissions.DocumentScopeView,
            _ => OaPermissions.ExpenseScopeView
        };
        var roles = actor.Roles ?? [actor.Role];
        var eligibleScopes = roles.Where(role =>
            rolePermissions.GetValueOrDefault(role)?.Contains(requiredPermission) == true
            || resourceType == "Expense" && rolePermissions.GetValueOrDefault(role)?.Contains(OaPermissions.ExpenseAllView) == true
            || resourceType == "Document" && (rolePermissions.GetValueOrDefault(role)?.Contains(OaPermissions.DocumentManage) == true || rolePermissions.GetValueOrDefault(role)?.Contains(OaPermissions.DocumentDeptManage) == true))
            .Select(role => roleDataScopes.GetValueOrDefault(role)?.GetValueOrDefault(resourceType) ?? OaDataScopes.Self);
        return eligibleScopes.OrderByDescending(ScopeRank).FirstOrDefault() ?? OaDataScopes.Self;
    }

    public bool CanAccessDepartmentDocument(Employee actor, string? docDepartmentId)
    {
        if (string.IsNullOrWhiteSpace(docDepartmentId)) return true;
        if (HasPermission(actor, OaPermissions.DocumentManage))
        {
            var scope = EffectiveDataScope(actor, "Document");
            if (scope == OaDataScopes.Company) return true;
        }
        if (actor.DepartmentId == docDepartmentId) return true;
        var effectiveScope = EffectiveDataScope(actor, "Document");
        return effectiveScope switch
        {
            OaDataScopes.Company => true,
            OaDataScopes.Department => actor.DepartmentId == docDepartmentId,
            OaDataScopes.DepartmentAndChildren => IsDepartmentWithin(docDepartmentId, actor.DepartmentId),
            _ => actor.DepartmentId == docDepartmentId
        };
    }

    public bool CanManageDepartmentDocument(Employee actor, string? docDepartmentId)
    {
        var hasGlobalManage = HasPermission(actor, OaPermissions.DocumentManage);
        var hasDeptManage = HasPermission(actor, OaPermissions.DocumentDeptManage);
        if (!hasGlobalManage && !hasDeptManage) return false;

        var scope = EffectiveDataScope(actor, "Document");
        if (scope == OaDataScopes.Company || (hasGlobalManage && string.IsNullOrWhiteSpace(docDepartmentId)))
            return true;

        if (string.IsNullOrWhiteSpace(docDepartmentId))
            return false;

        return scope switch
        {
            OaDataScopes.Company => true,
            OaDataScopes.Department => actor.DepartmentId == docDepartmentId,
            OaDataScopes.DepartmentAndChildren => IsDepartmentWithin(docDepartmentId, actor.DepartmentId),
            _ => hasDeptManage && actor.DepartmentId == docDepartmentId
        };
    }

    private bool IsManagerOf(string managerId, Employee employee)
    {
        var cursor = employee;
        while (cursor.ManagerId is not null)
        {
            if (cursor.ManagerId == managerId) return true;
            var manager = FindEmployee(cursor.ManagerId);
            if (manager is null) return false;
            cursor = manager;
        }
        return false;
    }

    private static Employee WithDefaultSecurity(Employee employee)
    {
        var permissions = IdentityDefaults.RolePermissions.GetValueOrDefault(employee.Role) ?? [];
        return employee with { Roles = [employee.Role], Permissions = permissions };
    }

    private static int ScopeRank(string scope) => scope switch { OaDataScopes.Company => 3, OaDataScopes.DepartmentAndChildren => 2, OaDataScopes.Department => 1, _ => 0 };
}
