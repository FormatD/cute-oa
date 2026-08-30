using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public static class OaPermissions
{
    public const string UserManage = "USER_MANAGE";
    public const string ProcessManage = "PROCESS_MANAGE";
    public const string AuditView = "AUDIT_VIEW";
    public const string CalendarManage = "CALENDAR_MANAGE";
    public const string ExpensePay = "EXPENSE_PAY";
    public const string ExpenseAllView = "EXPENSE_ALL_VIEW";
    public const string LeaveScopeView = "LEAVE_SCOPE_VIEW";
    public const string ExpenseScopeView = "EXPENSE_SCOPE_VIEW";
    public const string OrgManage = "ORG_MANAGE";
    public const string AnnouncementManage = "ANNOUNCEMENT_MANAGE";
    public const string TravelScopeView = "TRAVEL_SCOPE_VIEW";
    public const string PersonnelScopeView = "PERSONNEL_SCOPE_VIEW";
    public const string PersonnelManage = "PERSONNEL_MANAGE";
    public const string AttendanceScopeView = "ATTENDANCE_SCOPE_VIEW";
    public const string AttendanceManage = "ATTENDANCE_MANAGE";
    public const string ContractScopeView = "CONTRACT_SCOPE_VIEW";
    public const string ContractManage = "CONTRACT_MANAGE";

    public static IReadOnlyList<string> All { get; } = [UserManage, ProcessManage, AuditView, CalendarManage, ExpensePay, ExpenseAllView, LeaveScopeView, ExpenseScopeView, OrgManage, AnnouncementManage, TravelScopeView, PersonnelScopeView, PersonnelManage, AttendanceScopeView, AttendanceManage, ContractScopeView, ContractManage];
    public static IReadOnlyList<PermissionView> Definitions { get; } =
    [
        new(UserManage, "用户管理", "维护用户、账号、角色和权限配置"),
        new(ProcessManage, "流程维护", "创建、编辑、发布和归档审批流程"),
        new(AuditView, "审计查看", "查询公司操作审计日志"),
        new(CalendarManage, "日历维护", "维护中国工作日历和企业特殊工作日"),
        new(ExpensePay, "付款登记", "为审批通过的报销登记付款和凭证"),
        new(ExpenseAllView, "全公司报销查看（兼容）", "保留既有财务角色兼容性，新配置请使用报销数据范围"),
        new(LeaveScopeView, "请假范围查看", "按角色配置的数据范围查看他人请假"),
        new(ExpenseScopeView, "报销范围查看", "按角色配置的数据范围查看他人报销"),
        new(OrgManage, "组织架构维护", "新增、编辑、移动和删除部门与岗位"),
        new(AnnouncementManage, "公告管理", "创建、编辑、发布和撤回公司公告"),
        new(TravelScopeView, "出差范围查看", "按角色配置的数据范围查看他人出差"),
        new(PersonnelScopeView, "人事档案范围查看", "按角色配置的数据范围查看员工档案"),
        new(PersonnelManage, "人事档案维护", "编辑员工人事档案、组织关系和生命周期状态"),
        new(AttendanceScopeView, "考勤范围查看", "按角色配置的数据范围查看员工考勤"),
        new(AttendanceManage, "考勤维护", "维护班次、导入考勤并审核异常申诉"),
        new(ContractScopeView, "劳动合同范围查看", "按独立数据范围查看员工劳动合同"),
        new(ContractManage, "劳动合同维护", "创建、激活、续签、终止劳动合同并处理预警")
    ];
}

public static class OaDataScopes
{
    public const string Self = "SELF";
    public const string Department = "DEPARTMENT";
    public const string DepartmentAndChildren = "DEPARTMENT_AND_CHILDREN";
    public const string Company = "COMPANY";
    public static IReadOnlyList<string> All { get; } = [Self, Department, DepartmentAndChildren, Company];
    public static IReadOnlyList<string> ResourceTypes { get; } = ["Leave", "Expense", "Travel", "Personnel", "Attendance", "Contract"];
}

public static class IdentityDefaults
{
    public const string TenantId = "demo";
    public const string TenantName = "xxx公司";
    public const string DemoPassword = "Oa@123456";
    public static readonly Guid WelcomeAnnouncementId = Guid.Parse("6f8fa9bf-f20e-4e2f-90b6-5d22db657be3");

    public static IReadOnlyList<Department> Departments { get; } =
    [
        new("general", "总经办", null),
        new("finance", "财务部", "general"),
        new("hr", "行政人事部", "general"),
        new("engineering", "研发部", "general"),
        new("sales", "销售部", "general")
    ];

    public static IReadOnlyList<Employee> Employees { get; } =
    [
        new("u-zhang", "张晨", "员工", "engineering", "研发部", "u-li", 3, "ACTIVE"),
        new("u-li", "李薇", "部门负责人", "engineering", "研发部", "u-wang", 12, "ACTIVE"),
        new("u-wang", "王总", "总经理", "general", "总经办", null, 22, "ACTIVE"),
        new("u-chen", "陈敏", "财务专员", "finance", "财务部", "u-lin", 8, "ACTIVE"),
        new("u-lin", "林涛", "财务经理", "finance", "财务部", "u-wang", 15, "ACTIVE"),
        new("u-sun", "孙悦", "HR/行政", "hr", "行政人事部", "u-wang", 6, "ACTIVE"),
        new("u-admin", "系统管理员", "系统管理员", "general", "总经办", "u-wang", 7, "ACTIVE"),
        new("u-disabled", "停用账号", "员工", "sales", "销售部", "u-wang", 2, "DISABLED")
    ];

    public static IReadOnlyList<DefaultPosition> Positions { get; } =
    [
        new("general-manager", "总经理", "general", 10),
        new("system-administrator", "系统管理员", "general", 20),
        new("engineering-manager", "研发部经理", "engineering", 10),
        new("software-engineer", "研发工程师", "engineering", 20),
        new("finance-manager", "财务经理", "finance", 10),
        new("finance-specialist", "财务专员", "finance", 20),
        new("hr-specialist", "人事行政专员", "hr", 10),
        new("sales-specialist", "销售专员", "sales", 10)
    ];

    public static IReadOnlyDictionary<string, string> UserPositions { get; } = new Dictionary<string, string>
    {
        ["u-zhang"] = "software-engineer",
        ["u-li"] = "engineering-manager",
        ["u-wang"] = "general-manager",
        ["u-chen"] = "finance-specialist",
        ["u-lin"] = "finance-manager",
        ["u-sun"] = "hr-specialist",
        ["u-admin"] = "system-administrator",
        ["u-disabled"] = "sales-specialist"
    };

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> RolePermissions { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        ["员工"] = [],
        ["部门负责人"] = [],
        ["总经理"] = [],
        ["财务专员"] = [OaPermissions.ExpensePay, OaPermissions.ExpenseAllView, OaPermissions.ExpenseScopeView],
        ["财务经理"] = [OaPermissions.ExpensePay, OaPermissions.ExpenseAllView, OaPermissions.ExpenseScopeView],
        ["HR/行政"] = [OaPermissions.CalendarManage, OaPermissions.AnnouncementManage, OaPermissions.PersonnelScopeView, OaPermissions.PersonnelManage, OaPermissions.AttendanceScopeView, OaPermissions.AttendanceManage, OaPermissions.ContractScopeView, OaPermissions.ContractManage],
        ["系统管理员"] = OaPermissions.All
    };

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RoleDataScopes { get; } = RolePermissions.Keys.ToDictionary(
        role => role,
        role => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
        {
            ["Leave"] = role == "系统管理员" ? OaDataScopes.Company : OaDataScopes.Self,
            ["Expense"] = role is "财务专员" or "财务经理" or "系统管理员" ? OaDataScopes.Company : OaDataScopes.Self,
            ["Travel"] = role == "系统管理员" ? OaDataScopes.Company : OaDataScopes.Self,
            ["Personnel"] = role is "HR/行政" or "系统管理员" ? OaDataScopes.Company : OaDataScopes.Self,
            ["Attendance"] = role is "HR/行政" or "系统管理员" ? OaDataScopes.Company : OaDataScopes.Self,
            ["Contract"] = role is "HR/行政" or "系统管理员" ? OaDataScopes.Company : OaDataScopes.Self
        });
}

public static class PasswordHasher
{
    public const int DefaultIterations = 120_000;

    public static (string Salt, string Hash, int Iterations) Hash(string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash), iterations);
    }

    public static bool Verify(string password, string salt, string expectedHash, int iterations)
    {
        try
        {
            var candidate = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), iterations, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(candidate, Convert.FromBase64String(expectedHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool MeetsPolicy(string password) => password.Length is >= 8 and <= 128 && password.Any(char.IsLetter) && password.Any(char.IsDigit);
}

public static class IdentitySeeder
{
    public static void EnsureSeeded(OaDbContext db)
    {
        foreach (var department in IdentityDefaults.Departments.Where(item => !db.Departments.Any(existing => existing.Id == item.Id)))
            db.Departments.Add(new DepartmentRecord { Id = department.Id, TenantId = IdentityDefaults.TenantId, Name = department.Name, ParentId = department.ParentId, IsSystem = true });
        foreach (var departmentId in IdentityDefaults.Departments.Select(item => item.Id))
        {
            var existing = db.Departments.SingleOrDefault(item => item.Id == departmentId);
            if (existing is not null) existing.IsSystem = true;
        }
        db.SaveChanges();

        foreach (var position in IdentityDefaults.Positions.Where(item => !db.Positions.Any(existing => existing.Id == item.Id)))
            db.Positions.Add(new PositionRecord { Id = position.Id, TenantId = IdentityDefaults.TenantId, Name = position.Name, DepartmentId = position.DepartmentId, SortOrder = position.SortOrder, Status = "ACTIVE", IsSystem = true });
        foreach (var positionId in IdentityDefaults.Positions.Select(item => item.Id))
        {
            var existing = db.Positions.SingleOrDefault(item => item.Id == positionId);
            if (existing is not null) existing.IsSystem = true;
        }
        db.SaveChanges();

        foreach (var role in IdentityDefaults.RolePermissions.Keys.Where(code => !db.Roles.Any(existing => existing.Code == code)))
            db.Roles.Add(new RoleRecord { Code = role, TenantId = IdentityDefaults.TenantId, Name = role, IsSystem = true });
        db.SaveChanges();

        foreach (var (role, permissions) in IdentityDefaults.RolePermissions)
            foreach (var permission in permissions.Where(permission => !db.RolePermissions.Any(existing => existing.RoleCode == role && existing.PermissionCode == permission)))
                db.RolePermissions.Add(new RolePermissionRecord { RoleCode = role, PermissionCode = permission });
        db.SaveChanges();

        foreach (var role in db.Roles.AsNoTracking().Where(item => item.TenantId == IdentityDefaults.TenantId).Select(item => item.Code).ToList())
            foreach (var resourceType in OaDataScopes.ResourceTypes.Where(resourceType => !db.RoleDataScopes.Any(existing => existing.RoleCode == role && existing.ResourceType == resourceType)))
                db.RoleDataScopes.Add(new RoleDataScopeRecord { RoleCode = role, ResourceType = resourceType, Scope = IdentityDefaults.RoleDataScopes.GetValueOrDefault(role)?.GetValueOrDefault(resourceType) ?? OaDataScopes.Self });
        db.SaveChanges();

        foreach (var employee in IdentityDefaults.Employees.Where(item => !db.Users.Any(existing => existing.Id == item.Id)))
            db.Users.Add(new UserRecord { Id = employee.Id, TenantId = IdentityDefaults.TenantId, Name = employee.Name, DepartmentId = employee.DepartmentId, PositionId = IdentityDefaults.UserPositions.GetValueOrDefault(employee.Id), ManagerId = employee.ManagerId, CumulativeWorkYears = employee.CumulativeWorkYears, Status = employee.Status });
        foreach (var (userId, positionId) in IdentityDefaults.UserPositions)
        {
            var existing = db.Users.SingleOrDefault(item => item.Id == userId);
            if (existing is not null && existing.PositionId is null) existing.PositionId = positionId;
        }
        db.SaveChanges();

        foreach (var employee in IdentityDefaults.Employees.Where(item => !db.UserRoles.Any(existing => existing.UserId == item.Id)))
            db.UserRoles.Add(new UserRoleRecord { UserId = employee.Id, RoleCode = employee.Role, IsPrimary = true });
        db.SaveChanges();

        foreach (var employee in IdentityDefaults.Employees.Where(item => !db.UserAccounts.Any(existing => existing.UserId == item.Id)))
        {
            var password = PasswordHasher.Hash(IdentityDefaults.DemoPassword);
            db.UserAccounts.Add(new UserAccountRecord { UserId = employee.Id, PasswordSalt = password.Salt, PasswordHash = password.Hash, PasswordIterations = password.Iterations });
        }
        db.SaveChanges();

        if (!db.Announcements.Any(item => item.Id == IdentityDefaults.WelcomeAnnouncementId))
        {
            var now = DateTimeOffset.UtcNow;
            db.Announcements.Add(new AnnouncementRecord
            {
                Id = IdentityDefaults.WelcomeAnnouncementId,
                TenantId = IdentityDefaults.TenantId,
                Title = "欢迎使用 xxx公司 OA",
                Content = "OA 系统用于公司请假、报销、审批、组织通讯录和公告协同。请在开始使用前检查个人部门、岗位和直属上级信息，如有问题请联系行政人事部。",
                Status = "PUBLISHED",
                CreatedBy = "u-admin",
                PublishedBy = "u-admin",
                CreatedAt = now,
                UpdatedAt = now,
                PublishedAt = now
            });
            db.SaveChanges();
        }
    }
}

public sealed record DefaultPosition(string Id, string Name, string DepartmentId, int SortOrder);
