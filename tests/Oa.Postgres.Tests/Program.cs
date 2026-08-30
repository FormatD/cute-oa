using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Oa.Api.Domain;
using Oa.Api.Persistence;
using Oa.Api.Services;

var connectionString = "Host=localhost;Port=5433;Database=oa_test;Username=oa;Password=oa_dev_password";
var configuredConnectionString = Environment.GetEnvironmentVariable("OA_TEST_CONNECTION");
if (!string.IsNullOrWhiteSpace(configuredConnectionString)) connectionString = configuredConnectionString;
var options = new DbContextOptionsBuilder<OaDbContext>().UseNpgsql(connectionString).Options;

DemoData data;
await using (var setup = new OaDbContext(options))
{
    await setup.Database.MigrateAsync();
    data = new DemoData(setup);
}
var leaveDate = new DateOnly(2030, 1, 1).AddDays(Random.Shared.Next(0, 365));
while (leaveDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) leaveDate = leaveDate.AddDays(1);
Guid requestId;
Guid expenseId;
Guid fileId;
Guid versionedRequestId;
Guid delegatedRequestId;
Guid postCancellationRequestId;
Guid scopedRequestId;
Guid scopedProcessId;
Guid leaveCopyId;
Guid expenseCopyId;
Guid paymentProofId;
Guid announcementId;
Guid travelId;
Guid withdrawnTravelId;
Guid linkedExpenseId;
Guid attendanceRecordId;
Guid attendanceAppealId;
DateOnly attendanceLockMonth;
Guid employmentContractId;
Guid renewedEmploymentContractId;
string fileRoot;
var idempotencyKey = $"pg-integration-{Guid.NewGuid():N}";
var receiptNumber = $"FP-PG-{Guid.NewGuid():N}";
const string managedUserId = "u-pg-managed";
const string managedRoleCode = "PG审批观察员";
const string managedDepartmentId = "pg-operations";
const string managedChildDepartmentId = "pg-support";
const string managedPositionId = "pg-hr-partner";
await using (var writeDb = new OaDbContext(options))
{
    var calendar = new WorkCalendarService(writeDb);
    var employee = data.GetEmployee("u-zhang");
    var hr = data.GetEmployee("u-sun");
    var notifications = new NotificationService(writeDb);
    var processes = new ProcessDefinitionService(writeDb, data);
    var administrator = data.GetEmployee("u-admin");
    if (!data.HasPermission(administrator, OaPermissions.UserManage) || data.HasPermission(employee, OaPermissions.UserManage))
        throw new InvalidOperationException("持久化角色未正确解析用户管理权限。");
    var identity = new IdentityAdministrationService(writeDb, data);
    if (identity.List(employee, null, null, null, 1, 20).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权查询用户列表。");
    var seededUsers = identity.List(administrator, null, null, null, 1, 20);
    if (!seededUsers.IsSuccess || seededUsers.Value!.Total != IdentityDefaults.Employees.Count)
        throw new InvalidOperationException("默认用户未完整写入持久化身份目录。");
    var roles = identity.ListRoles(administrator);
    if (!roles.IsSuccess || roles.Value!.Count != IdentityDefaults.RolePermissions.Count || !roles.Value.Any(item => item.Code == "系统管理员" && item.Permissions.Contains(OaPermissions.UserManage)))
        throw new InvalidOperationException("默认角色或权限未完整写入数据库。");
    var permissionDefinitions = identity.ListPermissions(administrator);
    if (!permissionDefinitions.IsSuccess || permissionDefinitions.Value!.Count != OaPermissions.All.Count || identity.ListPermissions(employee).Code != "AUTH_002")
        throw new InvalidOperationException("权限定义查询或服务端授权错误。");
    if (identity.UpdateRole(administrator, new UpdateRoleRequest("系统管理员", "系统管理员", [OaPermissions.ProcessManage])).Code != "STATE_001")
        throw new InvalidOperationException("管理员可以移除自己最后一份用户管理权限。");
    if (identity.CreateRole(administrator, new CreateRoleRequest("PG无效范围", "无效范围", [], new Dictionary<string, string> { ["Expense"] = OaDataScopes.Company })).Code != "VALIDATION_001")
        throw new InvalidOperationException("未授予范围查看权限的角色可以配置跨用户数据范围。");
    var createdRole = identity.CreateRole(administrator, new CreateRoleRequest(managedRoleCode, "集成审批观察员", [OaPermissions.CalendarManage]));
    if (!createdRole.IsSuccess || createdRole.Value!.IsSystem || createdRole.Value.UserCount != 0)
        throw new InvalidOperationException(createdRole.Error ?? "自定义角色无法创建。");
    if (identity.CreateRole(administrator, new CreateRoleRequest(managedRoleCode, "重复角色", [])).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复角色编码。");
    var createdUser = identity.Create(administrator, new CreateManagedUserRequest(
        managedUserId, "集成测试用户", "engineering", "u-li", 4, [managedRoleCode], "StartPass123"));
    if (!createdUser.IsSuccess || createdUser.Value!.Status != "ACTIVE" || createdUser.Value.Roles.SingleOrDefault() != managedRoleCode)
        throw new InvalidOperationException(createdUser.Error ?? "管理员无法创建持久化用户。");
    if (identity.DeleteRole(administrator, managedRoleCode).Code != "CONFLICT_001")
        throw new InvalidOperationException("系统允许删除已分配给用户的角色。");
    if (identity.Create(administrator, new CreateManagedUserRequest(managedUserId, "重复用户", "engineering", "u-li", 4, ["员工"], "StartPass123")).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复用户 ID。");

    var authConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Authentication:Issuer"] = "cute-oa-pg-tests",
        ["Authentication:Audience"] = "cute-oa-web-pg-tests",
        ["Authentication:SigningKey"] = "cute-oa-pg-tests-signing-key-with-at-least-thirty-two-bytes",
        ["Authentication:AccessTokenMinutes"] = "30",
        ["DemoFeatures:AllowDataGeneration"] = "true"
    }).Build();
    var persistedDirectory = new DemoData(writeDb);
    var personnel = new PersonnelService(writeDb, persistedDirectory, notifications);
    var employeeRoster = personnel.List(persistedDirectory.GetEmployee("u-zhang"), null, null, null, null, 1, 20);
    if (!employeeRoster.IsSuccess || employeeRoster.Value!.Total != 1 || employeeRoster.Value.Items.Single().UserId != "u-zhang")
        throw new InvalidOperationException("普通员工花名册未限制为本人。 ");
    var managerRoster = personnel.List(persistedDirectory.GetEmployee("u-li"), null, null, null, null, 1, 20);
    if (!managerRoster.IsSuccess || managerRoster.Value!.Items.All(item => item.UserId != "u-zhang"))
        throw new InvalidOperationException("直属上级无法查看管理链下属人事档案。 ");
    var hrRoster = personnel.List(persistedDirectory.GetEmployee("u-sun"), null, null, null, null, 1, 100);
    if (!hrRoster.IsSuccess || hrRoster.Value!.Total != IdentityDefaults.Employees.Count + 1)
        throw new InvalidOperationException("HR 未按全公司数据范围读取完整花名册。 ");
    var currentPersonnel = personnel.Get(persistedDirectory.GetEmployee("u-sun"), "u-zhang");
    if (!currentPersonnel.IsSuccess || currentPersonnel.Value!.Events.SingleOrDefault()?.EventType != "IMPORTED")
        throw new InvalidOperationException("默认人事档案或初始化事件未生成。 ");
    var personnelToday = DateOnly.FromDateTime(DateTime.UtcNow);
    var personnelUpdate = new UpdatePersonnelProfileRequest(
        "engineering", "software-engineer", "u-li", "zhang.chen@xxx.example", "+86 21 5555 0101", "上海办公室", "FULL_TIME", "ACTIVE",
        personnelToday.AddYears(-2), null, personnelToday.AddYears(-2), personnelToday.AddYears(-6), null, null,
        currentPersonnel.Value.Version, personnelToday, "补充工作联系方式并核验累计工作年限");
    if (personnel.Update(persistedDirectory.GetEmployee("u-zhang"), "u-zhang", personnelUpdate).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权编辑自己的人事档案。 ");
    var updatedPersonnel = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate);
    if (!updatedPersonnel.IsSuccess || updatedPersonnel.Value!.CumulativeWorkYears != 6 || updatedPersonnel.Value.WorkEmail != "zhang.chen@xxx.example" || updatedPersonnel.Value.Events.First().EventType != "PROFILE_UPDATED")
        throw new InvalidOperationException(updatedPersonnel.Error ?? "HR 无法更新人事档案或社会工龄。 ");
    var invalidLifecycle = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = "PROBATION", ProbationEndDate = null, RegularizedDate = null, Version = updatedPersonnel.Value.Version, ChangeReason = "测试无效试用状态" });
    if (invalidLifecycle.Code != "PERSONNEL_003")
        throw new InvalidOperationException("缺少试用期结束日期的试用状态未被拒绝。 ");
    var authentication = new AuthenticationService(persistedDirectory, new JwtTokenService(authConfiguration), writeDb);
    var initialLogin = authentication.Login(new LoginRequest(managedUserId, "StartPass123"), new SessionClient("Mozilla/5.0 (Macintosh) Chrome/140", "127.0.0.1"));
    if (!initialLogin.IsSuccess || initialLogin.Value!.SessionId is null || initialLogin.Value.RefreshToken is null)
        throw new InvalidOperationException("新建账号无法使用加密密码登录。");
    var storedInitialSession = writeDb.AuthSessions.Single(item => item.Id == initialLogin.Value.SessionId);
    if (storedInitialSession.RefreshTokenHash == initialLogin.Value.RefreshToken || storedInitialSession.RefreshTokenHash.Length != 64)
        throw new InvalidOperationException("刷新令牌未以 SHA-256 摘要持久化。");
    for (var attempt = 0; attempt < 5; attempt++) authentication.Login(new LoginRequest(managedUserId, "WrongPass123"));
    if (writeDb.UserAccounts.Single(item => item.UserId == managedUserId).LockedUntil <= DateTimeOffset.UtcNow || authentication.Login(new LoginRequest(managedUserId, "StartPass123")).IsSuccess)
        throw new InvalidOperationException("连续登录失败未锁定账号。");
    var passwordReset = identity.ResetPassword(administrator, managedUserId, new ResetUserPasswordRequest("ResetPass123"));
    writeDb.Entry(storedInitialSession).Reload();
    if (!passwordReset.IsSuccess || storedInitialSession.RevokedAt is null || authentication.Login(new LoginRequest(managedUserId, "StartPass123")).IsSuccess)
        throw new InvalidOperationException("密码重置未解除锁定或新旧密码校验错误。");
    var firstSession = authentication.Login(new LoginRequest(managedUserId, "ResetPass123"), new SessionClient("Mozilla/5.0 (Macintosh) Safari/605", "127.0.0.1"));
    var secondSession = authentication.Login(new LoginRequest(managedUserId, "ResetPass123"), new SessionClient("Mozilla/5.0 (Windows NT 10.0) Chrome/140", "10.0.0.2"));
    if (!firstSession.IsSuccess || !secondSession.IsSuccess)
        throw new InvalidOperationException("密码重置后无法建立新会话。");
    var sessions = authentication.ListSessions(persistedDirectory.GetEmployee(managedUserId), secondSession.Value!.SessionId, 1, 20);
    if (!sessions.IsSuccess || sessions.Value!.Items.Count(item => item.Status == "ACTIVE") < 2 || sessions.Value.Items.Single(item => item.Id == secondSession.Value.SessionId).IsCurrent is false)
        throw new InvalidOperationException("登录设备列表未正确标记当前会话。");
    if (!authentication.RevokeSession(persistedDirectory.GetEmployee(managedUserId), secondSession.Value.SessionId!.Value, firstSession.Value!.SessionId!.Value).IsSuccess || authentication.Refresh(firstSession.Value.RefreshToken).IsSuccess)
        throw new InvalidOperationException("单设备会话撤销未生效。");
    var refreshCandidate = authentication.Login(new LoginRequest(managedUserId, "ResetPass123"), new SessionClient("Mozilla/5.0 (iPhone) Safari/605", "10.0.0.3"));
    var rotated = authentication.Refresh(refreshCandidate.Value!.RefreshToken, new SessionClient("Mozilla/5.0 (iPhone) Safari/605", "10.0.0.3"));
    if (!rotated.IsSuccess || rotated.Value!.RefreshToken == refreshCandidate.Value.RefreshToken || rotated.Value.SessionId != refreshCandidate.Value.SessionId)
        throw new InvalidOperationException("刷新令牌未轮换或访问令牌未保持会话绑定。");
    var reusedRecord = writeDb.AuthSessions.Single(item => item.Id == refreshCandidate.Value.SessionId);
    var reused = authentication.Refresh(refreshCandidate.Value.RefreshToken);
    writeDb.Entry(reusedRecord).Reload();
    if (reused.IsSuccess || reusedRecord.RevokedReason != "TOKEN_REUSE")
        throw new InvalidOperationException("已轮换刷新令牌可被重复使用或未阻断对应会话。");
    var roleChangeSession = authentication.Login(new LoginRequest(managedUserId, "ResetPass123"));
    var configuredPermissions = new[] { OaPermissions.CalendarManage, OaPermissions.AuditView, OaPermissions.LeaveScopeView };
    var permissionUpdate = identity.UpdateRole(administrator, new UpdateRoleRequest(managedRoleCode, "集成审批观察员（已更新）", configuredPermissions));
    var permissionChangedRecord = writeDb.AuthSessions.Single(item => item.Id == roleChangeSession.Value!.SessionId);
    writeDb.Entry(permissionChangedRecord).Reload();
    if (!permissionUpdate.IsSuccess || !permissionUpdate.Value!.Permissions.Contains(OaPermissions.CalendarManage) || !permissionUpdate.Value.Permissions.Contains(OaPermissions.AuditView) || permissionChangedRecord.RevokedReason != "ROLE_PERMISSIONS_CHANGED")
        throw new InvalidOperationException(permissionUpdate.Error ?? "角色权限变化未持久化或未撤销受影响会话。");
    var scopeChangeSession = authentication.Login(new LoginRequest(managedUserId, "ResetPass123"));
    var scopeUpdate = identity.UpdateRole(administrator, new UpdateRoleRequest(managedRoleCode, "集成审批观察员（已更新）", configuredPermissions, new Dictionary<string, string> { ["Leave"] = OaDataScopes.Department, ["Expense"] = OaDataScopes.Self }));
    var scopeChangedRecord = writeDb.AuthSessions.Single(item => item.Id == scopeChangeSession.Value!.SessionId);
    writeDb.Entry(scopeChangedRecord).Reload();
    var scopedDirectory = new DemoData(writeDb);
    var scopedActor = scopedDirectory.GetEmployee(managedUserId);
    if (!scopeUpdate.IsSuccess || scopeUpdate.Value!.DataScopes["Leave"] != OaDataScopes.Department || scopeChangedRecord.RevokedReason != "ROLE_DATA_SCOPE_CHANGED" || !scopedDirectory.CanView(scopedActor, scopedDirectory.GetEmployee("u-li"), "Leave") || scopedDirectory.CanView(scopedActor, scopedDirectory.GetEmployee("u-chen"), "Leave"))
        throw new InvalidOperationException(scopeUpdate.Error ?? "角色数据范围未生效、未隔离部门或未撤销受影响会话。");
    roleChangeSession = authentication.Login(new LoginRequest(managedUserId, "ResetPass123"));
    var updatedUser = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        "集成测试用户（已更新）", "hr", "u-wang", 5, "ACTIVE", ["HR/行政"], createdUser.Value.Version));
    if (!updatedUser.IsSuccess || !updatedUser.Value!.Permissions.Contains(OaPermissions.CalendarManage) || updatedUser.Value.Permissions.Contains(OaPermissions.UserManage))
        throw new InvalidOperationException(updatedUser.Error ?? "用户资料或角色权限无法更新。");
    var roleChangedRecord = writeDb.AuthSessions.Single(item => item.Id == roleChangeSession.Value!.SessionId);
    writeDb.Entry(roleChangedRecord).Reload();
    if (roleChangedRecord.RevokedReason != "USER_ROLES_CHANGED")
        throw new InvalidOperationException("安全角色变化后既有会话未失效。");
    if (!identity.DeleteRole(administrator, managedRoleCode).IsSuccess || writeDb.Roles.Any(item => item.Code == managedRoleCode))
        throw new InvalidOperationException("未分配的自定义角色无法删除。");
    if (identity.DeleteRole(administrator, "员工").Code != "STATE_001")
        throw new InvalidOperationException("系统预置角色可被删除。");
    var adminView = identity.List(administrator, "u-admin", null, null, 1, 20).Value!.Items.Single();
    if (identity.Update(administrator, administrator.Id, new UpdateManagedUserRequest(
        adminView.Name, adminView.DepartmentId, adminView.ManagerId, adminView.CumulativeWorkYears, "DISABLED", adminView.Roles, adminView.Version)).Code != "STATE_001")
        throw new InvalidOperationException("管理员可以停用自己的账号。");
    var refreshedUsers = identity.List(administrator, "集成测试", "ACTIVE", "hr", 1, 10);
    if (!refreshedUsers.IsSuccess || refreshedUsers.Value!.Total != 1 || refreshedUsers.Value.Items[0].Id != managedUserId)
        throw new InvalidOperationException("用户管理列表筛选或分页结果错误。");

    var departments = new DepartmentAdministrationService(writeDb, data);
    if (departments.List(employee).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权维护组织架构。");
    var seededDepartments = departments.List(administrator);
    if (!seededDepartments.IsSuccess || seededDepartments.Value!.Count != IdentityDefaults.Departments.Count || !seededDepartments.Value.Any(item => item.Id == "general" && item.Depth == 0 && item.IsSystem))
        throw new InvalidOperationException("默认部门树未完整初始化或根节点信息错误。");
    if (!data.HasPermission(administrator, OaPermissions.OrgManage) || data.HasPermission(employee, OaPermissions.OrgManage))
        throw new InvalidOperationException("组织架构维护权限未正确解析。");

    var createdDepartment = departments.Create(administrator, new CreateDepartmentRequest(managedDepartmentId, "集成运营部", "engineering", 30));
    if (!createdDepartment.IsSuccess || createdDepartment.Value!.Depth != 2 || createdDepartment.Value.IsSystem || createdDepartment.Value.Version != 1)
        throw new InvalidOperationException(createdDepartment.Error ?? "无法创建自定义部门。");
    if (departments.Create(administrator, new CreateDepartmentRequest(managedDepartmentId, "重复编码部门", "engineering", 31)).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复部门编码。");
    if (departments.Create(administrator, new CreateDepartmentRequest("pg-duplicate-name", "集成运营部", "engineering", 31)).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复部门名称。");
    var createdChildDepartment = departments.Create(administrator, new CreateDepartmentRequest(managedChildDepartmentId, "集成支持组", managedDepartmentId, 10));
    if (!createdChildDepartment.IsSuccess || createdChildDepartment.Value!.Depth != 3)
        throw new InvalidOperationException(createdChildDepartment.Error ?? "无法创建下级部门。");
    if (departments.Delete(administrator, managedDepartmentId, createdDepartment.Value.Version).Code != "CONFLICT_001")
        throw new InvalidOperationException("存在下级部门时仍可删除上级部门。");
    if (departments.Update(administrator, managedDepartmentId, new UpdateDepartmentRequest("集成运营部", managedChildDepartmentId, 30, createdDepartment.Value.Version)).Code != "VALIDATION_001")
        throw new InvalidOperationException("组织架构允许形成循环层级。");

    var departmentAdminSession = authentication.Login(new LoginRequest("u-admin", "Oa@123456"));
    if (!departmentAdminSession.IsSuccess) throw new InvalidOperationException("管理员无法建立组织调整验证会话。");
    var movedChildDepartment = departments.Update(administrator, managedChildDepartmentId, new UpdateDepartmentRequest("集成支持组", "sales", 10, createdChildDepartment.Value.Version));
    var revokedDepartmentSession = writeDb.AuthSessions.Single(item => item.Id == departmentAdminSession.Value!.SessionId);
    writeDb.Entry(revokedDepartmentSession).Reload();
    if (!movedChildDepartment.IsSuccess || movedChildDepartment.Value!.ParentId != "sales" || movedChildDepartment.Value.Depth != 2 || movedChildDepartment.Value.Version != 2 || revokedDepartmentSession.RevokedReason != "DEPARTMENT_HIERARCHY_CHANGED")
        throw new InvalidOperationException(movedChildDepartment.Error ?? "部门移动未持久化或未撤销组织权限缓存会话。");
    if (departments.Update(administrator, managedChildDepartmentId, new UpdateDepartmentRequest("过期修改", "sales", 10, 1)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("部门更新未执行乐观锁校验。");
    var engineeringDepartment = departments.List(administrator).Value!.Single(item => item.Id == "engineering");
    if (departments.Delete(administrator, engineeringDepartment.Id, engineeringDepartment.Version).Code != "STATE_001")
        throw new InvalidOperationException("系统预置部门可以被删除。");
    if (!departments.Delete(administrator, managedChildDepartmentId, movedChildDepartment.Value.Version).IsSuccess)
        throw new InvalidOperationException("无下级且无用户的自定义部门无法删除。");

    var assignedToCustomDepartment = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        updatedUser.Value.Name, managedDepartmentId, updatedUser.Value.ManagerId, updatedUser.Value.CumulativeWorkYears, "ACTIVE", updatedUser.Value.Roles, updatedUser.Value.Version));
    if (!assignedToCustomDepartment.IsSuccess || departments.Delete(administrator, managedDepartmentId, createdDepartment.Value.Version).Code != "CONFLICT_001")
        throw new InvalidOperationException(assignedToCustomDepartment.Error ?? "部门用户占用保护未生效。");
    var returnedToHr = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        assignedToCustomDepartment.Value!.Name, "hr", assignedToCustomDepartment.Value.ManagerId, assignedToCustomDepartment.Value.CumulativeWorkYears, "ACTIVE", assignedToCustomDepartment.Value.Roles, assignedToCustomDepartment.Value.Version));
    if (!returnedToHr.IsSuccess || !departments.Delete(administrator, managedDepartmentId, createdDepartment.Value.Version).IsSuccess)
        throw new InvalidOperationException(returnedToHr.Error ?? "自定义部门清理失败。");

    var generalManager = identity.List(administrator, "u-li", null, null, 1, 20).Value!.Items.Single(item => item.Id == "u-li");
    if (identity.Update(administrator, generalManager.Id, new UpdateManagedUserRequest(
        generalManager.Name, generalManager.DepartmentId, "u-zhang", generalManager.CumulativeWorkYears, generalManager.Status, generalManager.Roles, generalManager.Version)).Code != "VALIDATION_001")
        throw new InvalidOperationException("经理关系允许形成间接循环。");
    if (!writeDb.AuditLogs.Any(item => item.ResourceType == "Department" && item.Action == "DEPARTMENT_CREATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Department" && item.Action == "DEPARTMENT_UPDATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Department" && item.Action == "DEPARTMENT_DELETED"))
        throw new InvalidOperationException("部门新增、移动或删除操作未记录审计日志。");
    if (departments.List(administrator).Value!.Count != IdentityDefaults.Departments.Count)
        throw new InvalidOperationException("组织架构集成用例未恢复默认部门数据。");

    var positions = new PositionAdministrationService(writeDb, data);
    if (positions.List(employee).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权维护岗位。");
    var seededPositions = positions.List(administrator);
    if (!seededPositions.IsSuccess || seededPositions.Value!.Count != IdentityDefaults.Positions.Count || !seededPositions.Value.Any(item => item.Id == "software-engineer" && item.DepartmentId == "engineering" && item.IsSystem))
        throw new InvalidOperationException("默认岗位未完整初始化。");
    var createdPosition = positions.Create(administrator, new CreatePositionRequest(managedPositionId, "集成人力伙伴", "hr", 30));
    if (!createdPosition.IsSuccess || createdPosition.Value!.Status != "ACTIVE" || createdPosition.Value.UserCount != 0 || createdPosition.Value.Version != 1)
        throw new InvalidOperationException(createdPosition.Error ?? "无法创建自定义岗位。");
    if (positions.Create(administrator, new CreatePositionRequest(managedPositionId, "重复编码岗位", "hr", 31)).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复岗位编码。");
    if (positions.Create(administrator, new CreatePositionRequest("pg-duplicate-position", "集成人力伙伴", "hr", 31)).Code != "DUPLICATE_001")
        throw new InvalidOperationException("同一部门允许创建同名岗位。");
    var updatedPosition = positions.Update(administrator, managedPositionId, new UpdatePositionRequest("高级集成人力伙伴", "hr", 35, "ACTIVE", createdPosition.Value.Version));
    if (!updatedPosition.IsSuccess || updatedPosition.Value!.Version != 2 || updatedPosition.Value.SortOrder != 35)
        throw new InvalidOperationException(updatedPosition.Error ?? "岗位名称或排序无法更新。");
    if (positions.Update(administrator, managedPositionId, new UpdatePositionRequest("过期更新", "hr", 35, "ACTIVE", 1)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("岗位更新未执行乐观锁校验。");
    if (identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        returnedToHr.Value!.Name, "hr", returnedToHr.Value.ManagerId, returnedToHr.Value.CumulativeWorkYears, "ACTIVE", returnedToHr.Value.Roles, returnedToHr.Value.Version, "software-engineer")).Code != "VALIDATION_001")
        throw new InvalidOperationException("用户可以分配其他部门的岗位。");
    var positionAssignmentSession = authentication.Login(new LoginRequest(managedUserId, "ResetPass123"));
    var assignedPosition = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        returnedToHr.Value.Name, "hr", returnedToHr.Value.ManagerId, returnedToHr.Value.CumulativeWorkYears, "ACTIVE", returnedToHr.Value.Roles, returnedToHr.Value.Version, managedPositionId));
    var revokedPositionSession = writeDb.AuthSessions.Single(item => item.Id == positionAssignmentSession.Value!.SessionId);
    writeDb.Entry(revokedPositionSession).Reload();
    if (!assignedPosition.IsSuccess || assignedPosition.Value!.PositionId != managedPositionId || assignedPosition.Value.PositionName != "高级集成人力伙伴" || revokedPositionSession.RevokedReason != "USER_ORGANIZATION_CHANGED")
        throw new InvalidOperationException(assignedPosition.Error ?? "用户岗位分配未生效或未撤销旧会话。");
    if (positions.Delete(administrator, managedPositionId, updatedPosition.Value.Version).Code != "CONFLICT_001" ||
        positions.Update(administrator, managedPositionId, new UpdatePositionRequest("高级集成人力伙伴", "engineering", 35, "ACTIVE", updatedPosition.Value.Version)).Code != "CONFLICT_001" ||
        positions.Update(administrator, managedPositionId, new UpdatePositionRequest("高级集成人力伙伴", "hr", 35, "DISABLED", updatedPosition.Value.Version)).Code != "CONFLICT_001")
        throw new InvalidOperationException("已分配岗位的删除、跨部门移动或停用保护未生效。");
    var clearedPosition = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        assignedPosition.Value.Name, "hr", assignedPosition.Value.ManagerId, assignedPosition.Value.CumulativeWorkYears, "ACTIVE", assignedPosition.Value.Roles, assignedPosition.Value.Version, null));
    if (!clearedPosition.IsSuccess || clearedPosition.Value!.PositionId is not null || !positions.Delete(administrator, managedPositionId, updatedPosition.Value.Version).IsSuccess)
        throw new InvalidOperationException(clearedPosition.Error ?? "岗位解除分配或自定义岗位删除失败。");
    var systemPosition = positions.List(administrator).Value!.Single(item => item.Id == "software-engineer");
    if (positions.Delete(administrator, systemPosition.Id, systemPosition.Version).Code != "STATE_001")
        throw new InvalidOperationException("系统预置岗位可以被删除。");
    if (!writeDb.AuditLogs.Any(item => item.ResourceType == "Position" && item.Action == "POSITION_CREATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Position" && item.Action == "POSITION_UPDATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Position" && item.Action == "POSITION_DELETED") ||
        positions.List(administrator).Value!.Count != IdentityDefaults.Positions.Count)
        throw new InvalidOperationException("岗位审计记录不完整或集成用例未恢复默认岗位数据。");

    var announcements = new AnnouncementService(writeDb, data);
    if (announcements.ListAdmin(employee, null, 1, 20).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权维护公告。");
    if (!data.HasPermission(hr, OaPermissions.AnnouncementManage) || announcements.Create(hr, new SaveAnnouncementRequest("", "无效公告", null)).Code != "VALIDATION_001")
        throw new InvalidOperationException("HR 公告管理权限或公告必填校验错误。");
    var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
    var draftAnnouncement = announcements.Create(hr, new SaveAnnouncementRequest("数据库维护通知", "本周六凌晨进行系统维护，请提前保存工作。", expiresAt));
    if (!draftAnnouncement.IsSuccess || draftAnnouncement.Value!.Status != "DRAFT" || draftAnnouncement.Value.Version != 1 || announcements.ListPublished(employee, 1, 20).Value!.Total != 1)
        throw new InvalidOperationException(draftAnnouncement.Error ?? "公告草稿创建或发布前隔离失败。");
    var updatedAnnouncement = announcements.Update(hr, draftAnnouncement.Value.Id, new SaveAnnouncementRequest("数据库维护时间调整", "系统维护调整至本周日凌晨，请提前保存工作。", expiresAt, draftAnnouncement.Value.Version));
    if (!updatedAnnouncement.IsSuccess || updatedAnnouncement.Value!.Version != 2 || announcements.Update(hr, draftAnnouncement.Value.Id, new SaveAnnouncementRequest("过期更新", "过期版本不应保存。", expiresAt, 1)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException(updatedAnnouncement.Error ?? "公告草稿更新或乐观锁校验失败。");
    if (announcements.Publish(hr, draftAnnouncement.Value.Id, 1).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("公告发布未执行乐观锁校验。");
    var publishedAnnouncement = announcements.Publish(hr, draftAnnouncement.Value.Id, updatedAnnouncement.Value.Version);
    if (!publishedAnnouncement.IsSuccess || publishedAnnouncement.Value!.Status != "PUBLISHED" || publishedAnnouncement.Value.Version != 3 || announcements.Update(hr, draftAnnouncement.Value.Id, new SaveAnnouncementRequest("违规编辑", "已发布公告不应编辑。", expiresAt, 3)).Code != "STATE_001")
        throw new InvalidOperationException(publishedAnnouncement.Error ?? "公告发布或发布后只读保护失败。");
    announcementId = publishedAnnouncement.Value.Id;
    var secondDraft = announcements.Create(administrator, new SaveAnnouncementRequest("新员工入职欢迎", "欢迎新同事加入 xxx 公司。", null));
    var secondPublished = announcements.Publish(administrator, secondDraft.Value!.Id, secondDraft.Value.Version);
    var publishedPage = announcements.ListPublished(employee, 1, 1);
    if (!secondPublished.IsSuccess || !publishedPage.IsSuccess || publishedPage.Value!.Total != 3 || publishedPage.Value.Items.Single().Id != secondPublished.Value!.Id || publishedPage.Value.TotalPages != 3)
        throw new InvalidOperationException(secondPublished.Error ?? "公告发布时间倒序或服务端分页错误。");
    var announcementDetail = announcements.Get(employee, announcementId);
    if (!announcementDetail.IsSuccess || announcementDetail.Value!.ReadAt is not null || !announcements.MarkRead(employee, announcementId).IsSuccess || !announcements.MarkRead(employee, announcementId).IsSuccess || writeDb.AnnouncementReads.Count(item => item.AnnouncementId == announcementId && item.UserId == employee.Id) != 1)
        throw new InvalidOperationException("公告详情、确认已读或重复已读幂等失败。");
    if (announcements.Create(hr, new SaveAnnouncementRequest("过期公告", "不允许设置过去时间。", DateTimeOffset.UtcNow.AddMinutes(-1))).Code != "VALIDATION_001")
        throw new InvalidOperationException("公告允许设置过去的有效期。");
    if (announcements.Withdraw(hr, announcementId, 2).Code != "CONCURRENCY_001" || !announcements.Withdraw(hr, announcementId, publishedAnnouncement.Value.Version).IsSuccess || !announcements.Withdraw(administrator, secondPublished.Value.Id, secondPublished.Value.Version).IsSuccess)
        throw new InvalidOperationException("公告撤回或撤回版本校验失败。");
    if (announcements.ListPublished(employee, 1, 20).Value!.Total != 1 || announcements.Get(employee, announcementId).Code != "DATA_001" || !announcements.Get(administrator, announcementId).IsSuccess)
        throw new InvalidOperationException("撤回公告仍向普通员工可见或管理员无法追溯。");
    if (!writeDb.AuditLogs.Any(item => item.ResourceType == "Announcement" && item.Action == "ANNOUNCEMENT_CREATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Announcement" && item.Action == "ANNOUNCEMENT_PUBLISHED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Announcement" && item.Action == "ANNOUNCEMENT_WITHDRAWN") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Announcement" && item.Action == "ANNOUNCEMENT_READ"))
        throw new InvalidOperationException("公告关键动作审计日志不完整。");

    var definitions = processes.List(administrator);
    if (!definitions.IsSuccess || definitions.Value!.Count != 3 || definitions.Value.Any(item => item.Status != ProcessDefinitionStatus.Published) || definitions.Value.All(item => item.BusinessType != "Travel"))
        throw new InvalidOperationException("默认请假、报销和出差流程未能初始化为已发布版本。");
    if (processes.List(employee).IsSuccess)
        throw new InvalidOperationException("普通员工可以越权维护流程定义。");
    fileRoot = Path.Combine(Path.GetTempPath(), "cute-oa-file-test", Guid.NewGuid().ToString("N"));
    var fileService = new FileService(writeDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build());
    var attendance = new AttendanceService(writeDb, persistedDirectory, calendar, notifications, fileService, authConfiguration);
    var attendanceMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
    attendanceLockMonth = attendanceMonth;
    if (attendance.GenerateDemoData(employee, attendanceMonth).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权生成模拟考勤。 ");
    var generatedAttendance = attendance.GenerateDemoData(hr, attendanceMonth);
    if (!generatedAttendance.IsSuccess || generatedAttendance.Value!.Created == 0 || attendance.GenerateDemoData(hr, attendanceMonth).Value!.Created != 0)
        throw new InvalidOperationException(generatedAttendance.Error ?? "模拟考勤未生成或重复生成覆盖了已有数据。 ");
    var employeeAttendance = attendance.List(employee, new AttendanceListQuery(null, null, null, null, attendanceMonth));
    if (employeeAttendance.Count == 0 || employeeAttendance.Any(item => item.UserId != employee.Id))
        throw new InvalidOperationException("普通员工考勤列表未限制为本人。 ");
    var managerAttendance = attendance.List(data.GetEmployee("u-li"), new AttendanceListQuery(null, null, null, null, attendanceMonth));
    if (!managerAttendance.Any(item => item.UserId == employee.Id))
        throw new InvalidOperationException("直属上级无法查看下属考勤。 ");
    var attendanceDate = attendanceMonth;
    while (!calendar.IsWorkingDay(attendanceDate)) attendanceDate = attendanceDate.AddDays(1);
    var forcedLate = new AttendanceImportItem(employee.Id, attendanceDate,
        new DateTimeOffset(attendanceDate.ToDateTime(new TimeOnly(9, 30)), TimeSpan.FromHours(8)),
        new DateTimeOffset(attendanceDate.ToDateTime(new TimeOnly(18, 5)), TimeSpan.FromHours(8)));
    if (attendance.Import(employee, new ImportAttendanceRequest([forcedLate], "MANUAL")).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权录入考勤。 ");
    var importedAttendance = attendance.Import(hr, new ImportAttendanceRequest([forcedLate], "MANUAL"));
    if (!importedAttendance.IsSuccess || importedAttendance.Value!.Updated != 1)
        throw new InvalidOperationException(importedAttendance.Error ?? "HR 无法更新已有考勤。 ");
    var lateRecord = attendance.List(employee, new AttendanceListQuery(null, employee.Id, null, AttendanceStatuses.Late, attendanceMonth)).SingleOrDefault(item => item.WorkDate == attendanceDate);
    if (lateRecord is null || lateRecord.LateMinutes != 25 || lateRecord.Source != "MANUAL")
        throw new InvalidOperationException("迟到识别、宽限分钟或数据来源不正确。 ");
    attendanceRecordId = lateRecord.Id;
    if (attendance.SubmitAppeal(data.GetEmployee("u-li"), attendanceRecordId, new CreateAttendanceAppealRequest("代员工提交申诉")).Code != "DATA_001")
        throw new InvalidOperationException("上级可以代员工提交考勤申诉。 ");
    var submittedAppeal = attendance.SubmitAppeal(employee, attendanceRecordId, new CreateAttendanceAppealRequest("当天地铁故障导致迟到，请核实。"));
    if (!submittedAppeal.IsSuccess || attendance.SubmitAppeal(employee, attendanceRecordId, new CreateAttendanceAppealRequest("重复提交同一待审申诉。" )).Code != "ATTENDANCE_002")
        throw new InvalidOperationException(submittedAppeal.Error ?? "员工考勤申诉或重复保护失败。 ");
    attendanceAppealId = submittedAppeal.Value!.Id;
    var reviewedAppeal = attendance.ReviewAppeal(hr, attendanceAppealId, new ReviewAttendanceAppealRequest(true, "已核实交通故障，本次不计迟到。"));
    var correctedRecord = attendance.Get(employee, attendanceRecordId);
    if (!reviewedAppeal.IsSuccess || correctedRecord.Value!.Status != AttendanceStatuses.Corrected || correctedRecord.Value.OriginalStatus != AttendanceStatuses.Late)
        throw new InvalidOperationException(reviewedAppeal.Error ?? "考勤申诉通过后未修正记录或未保留原始异常。 ");
    var summary = attendance.MonthlySummary(employee, attendanceMonth).Single();
    if (summary.CorrectedDays < 1 || summary.PendingAppeals != 0)
        throw new InvalidOperationException("考勤月度汇总未反映申诉修正结果。 ");
    var defaultShift = attendance.ListShifts(employee).Single(item => item.IsDefault);
    if (attendance.UpdateShift(employee, defaultShift.Id, new SaveAttendanceShiftRequest(defaultShift.Code, defaultShift.Name, defaultShift.WorkStart, defaultShift.WorkEnd, defaultShift.BreakMinutes, defaultShift.LateToleranceMinutes, defaultShift.EarlyLeaveToleranceMinutes, true, true, defaultShift.Version)).Code != "AUTH_002" ||
        attendance.UpdateShift(hr, defaultShift.Id, new SaveAttendanceShiftRequest(defaultShift.Code, defaultShift.Name, defaultShift.WorkStart, defaultShift.WorkEnd, defaultShift.BreakMinutes, defaultShift.LateToleranceMinutes, defaultShift.EarlyLeaveToleranceMinutes, true, true, defaultShift.Version - 1)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("考勤班次权限或乐观版本校验失败。 ");
    var secondEmployee = data.GetEmployee("u-wang");
    var secondLate = forcedLate with { UserId = secondEmployee.Id };
    var secondImport = attendance.Import(hr, new ImportAttendanceRequest([secondLate], "MANUAL"));
    var secondLateRecord = attendance.List(secondEmployee, new AttendanceListQuery(null, secondEmployee.Id, null, AttendanceStatuses.Late, attendanceMonth)).Single(item => item.WorkDate == attendanceDate);
    var pendingClosureAppeal = attendance.SubmitAppeal(secondEmployee, secondLateRecord.Id, new CreateAttendanceAppealRequest("封账前验证待审申诉拦截。"));
    if (!secondImport.IsSuccess || !pendingClosureAppeal.IsSuccess)
        throw new InvalidOperationException(secondImport.Error ?? pendingClosureAppeal.Error ?? "无法建立考勤封账前置测试数据。 ");
    if (attendance.LockMonth(employee, attendanceMonth, new ChangeAttendanceMonthLockRequest("普通员工尝试封账", 0)).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权封账考勤月份。 ");
    if (attendance.LockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("月报核对完成并申请封账", 0)).Code != "ATTENDANCE_005")
        throw new InvalidOperationException("存在待审核申诉时仍可封账。 ");
    if (!attendance.ReviewAppeal(hr, pendingClosureAppeal.Value!.Id, new ReviewAttendanceAppealRequest(false, "封账前已核验，本次申诉不通过。")).IsSuccess)
        throw new InvalidOperationException("封账前无法办结待审核申诉。 ");
    var lockedMonth = attendance.LockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("本月异常和申诉已处理完毕，月报核对完成。", 0));
    if (!lockedMonth.IsSuccess || lockedMonth.Value is not { IsLocked: true, Version: 1 })
        throw new InvalidOperationException(lockedMonth.Error ?? "考勤月份无法封账。 ");
    if (attendance.Import(hr, new ImportAttendanceRequest([forcedLate], "MANUAL")).Code != "ATTENDANCE_004" ||
        attendance.GenerateDemoData(hr, attendanceMonth).Code != "ATTENDANCE_004" ||
        attendance.SubmitAppeal(employee, attendanceRecordId, new CreateAttendanceAppealRequest("封账后不应允许申诉。" )).Code != "ATTENDANCE_004")
        throw new InvalidOperationException("封账后仍可导入、生成或提交考勤申诉。 ");
    if (attendance.UnlockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("使用过期版本尝试解封", 0)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("考勤解封未校验乐观版本。 ");
    var unlockedMonth = attendance.UnlockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("发现需补充核验的历史考勤，授权临时解封。", lockedMonth.Value.Version));
    if (!unlockedMonth.IsSuccess || unlockedMonth.Value is not { IsLocked: false, Version: 2 })
        throw new InvalidOperationException(unlockedMonth.Error ?? "考勤月份无法解封。 ");
    var relockedMonth = attendance.LockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("补充核验完成，重新确认本月月报。", unlockedMonth.Value.Version));
    if (!relockedMonth.IsSuccess || relockedMonth.Value is not { IsLocked: true, Version: 3 })
        throw new InvalidOperationException(relockedMonth.Error ?? "考勤月份解封后无法重新封账。 ");
    var contractService = new EmploymentContractService(writeDb, persistedDirectory, fileService, notifications, authConfiguration);
    var contractStart = DateOnly.FromDateTime(DateTime.Today).AddYears(-1);
    var contractEnd = DateOnly.FromDateTime(DateTime.Today).AddDays(20);
    var baseContract = new SaveEmploymentContractRequest(employee.Id, EmploymentContractTypes.FixedTerm, null, contractStart, contractEnd, null, null, "上海办公室", "研发工程师", null, [], "数据库集成测试合同", 1, "建立劳动合同测试草稿");
    if (contractService.Create(employee, baseContract).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权创建劳动合同。 ");
    var invalidProbation = baseContract with { ProbationStartDate = contractStart, ProbationEndDate = contractStart.AddMonths(3), EndDate = contractStart.AddYears(1), ChangeReason = "验证试用期期限上限" };
    if (contractService.Create(hr, invalidProbation).Code != "CONTRACT_003")
        throw new InvalidOperationException("超过法定上限的试用期未被拒绝。 ");
    var contractDraft = contractService.Create(hr, baseContract);
    if (!contractDraft.IsSuccess || contractService.Activate(hr, contractDraft.Value!.Id, new ActivateEmploymentContractRequest(contractDraft.Value.Version, "核验签署件后激活合同")).Code != "CONTRACT_005")
        throw new InvalidOperationException(contractDraft.Error ?? "未签订或无附件的合同可以激活。 ");
    employmentContractId = contractDraft.Value.Id;
    if (!contractService.Get(employee, employmentContractId).IsSuccess || contractService.Get(data.GetEmployee("u-li"), employmentContractId).Code != "AUTH_002")
        throw new InvalidOperationException("劳动合同本人查看或直属上级敏感数据隔离错误。 ");
    if (contractService.List(data.GetEmployee("u-li"), null, null, null, null, null, 1, 20).Value!.Items.Any(item => item.UserId == employee.Id))
        throw new InvalidOperationException("直属上级通过合同列表读取了下属敏感合同。 ");
    var signedFile = fileService.CreateDemoPdf(hr, "张晨-签署劳动合同.pdf");
    var completedDraft = contractService.Update(hr, employmentContractId, baseContract with { SignedDate = DateOnly.FromDateTime(DateTime.Today), Attachments = [signedFile.Value!.Id.ToString()], Version = contractDraft.Value.Version, ChangeReason = "补充已签署合同扫描件" });
    if (!completedDraft.IsSuccess) throw new InvalidOperationException(completedDraft.Error ?? "HR 无法补充合同签署附件。 ");
    var activatedContract = contractService.Activate(hr, employmentContractId, new ActivateEmploymentContractRequest(completedDraft.Value!.Version, "签署主体和扫描件核验完成"));
    if (!activatedContract.IsSuccess || activatedContract.Value!.Status != EmploymentContractStatuses.Active || activatedContract.Value.DisplayStatus != EmploymentContractStatuses.Expiring)
        throw new InvalidOperationException(activatedContract.Error ?? "合同无法激活或到期展示状态错误。 ");
    var overlapFile = fileService.CreateDemoPdf(hr, "张晨-重叠合同.pdf");
    var contractOverlapDraft = contractService.Create(hr, baseContract with { SignedDate = DateOnly.FromDateTime(DateTime.Today), Attachments = [overlapFile.Value!.Id.ToString()], ChangeReason = "验证有效合同日期重叠保护" });
    if (!contractOverlapDraft.IsSuccess || contractService.Activate(hr, contractOverlapDraft.Value!.Id, new ActivateEmploymentContractRequest(contractOverlapDraft.Value.Version, "验证日期重叠保护逻辑")).Code != "CONTRACT_002")
        throw new InvalidOperationException("系统允许激活日期重叠的有效合同。 ");
    var contractSummary = contractService.AlertSummary(hr);
    if (contractSummary.Within30Days < 1 || contractSummary.Unacknowledged < 1 || !contractService.AcknowledgeAlert(hr, employmentContractId, new AcknowledgeContractAlertRequest(30)).IsSuccess)
        throw new InvalidOperationException("合同到期预警分档或确认处理错误。 ");
    var dispatchedAlerts = contractService.DispatchDueAlerts();
    if (dispatchedAlerts < 1 || contractService.DispatchDueAlerts() != 0)
        throw new InvalidOperationException("合同到期预警后台派发未执行或重复派发。 ");
    var renewalFile = fileService.CreateDemoPdf(hr, "张晨-续签劳动合同.pdf");
    var renewalStart = contractEnd.AddDays(1);
    var renewedContract = contractService.Renew(hr, employmentContractId, baseContract with { SignedDate = DateOnly.FromDateTime(DateTime.Today), StartDate = renewalStart, EndDate = renewalStart.AddYears(1), Attachments = [renewalFile.Value!.Id.ToString()], ChangeReason = "劳动合同到期续签", Version = activatedContract.Value.Version });
    if (!renewedContract.IsSuccess || renewedContract.Value!.RenewalOfId != employmentContractId || contractService.Get(hr, employmentContractId).Value!.Status != EmploymentContractStatuses.Superseded)
        throw new InvalidOperationException(renewedContract.Error ?? "合同续签版本链或原合同状态错误。 ");
    renewedEmploymentContractId = renewedContract.Value.Id;
    await using var fileContent = new MemoryStream("demo-pdf-content"u8.ToArray());
    var uploadedFile = fileService.Upload(employee, new FormFile(fileContent, 0, fileContent.Length, "file", "medical-proof.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" });
    if (!uploadedFile.IsSuccess) throw new InvalidOperationException(uploadedFile.Error);
    fileId = uploadedFile.Value!.Id;
    var manager = data.GetEmployee("u-li");
    var protectedLeave = new LeaveService(data, writeDb, calendar, notifications, fileService, processes).CreateDraft(manager, new CreateLeaveRequest(LeaveType.Personal, leaveDate, LeavePeriod.FullDay, leaveDate, LeavePeriod.FullDay, "越权附件验证", [fileId.ToString()]));
    if (protectedLeave.IsSuccess || protectedLeave.Code != "FILE_005")
        throw new InvalidOperationException("其他用户可以关联不属于自己的附件。");
    var blockedDate = new DateOnly(2031, 1, 6);
    if (!calendar.Update(hr, blockedDate, new UpdateWorkCalendarRequest(false, "企业团建休息日")).IsSuccess)
        throw new InvalidOperationException("HR 无法维护企业工作日历。");
    var leave = new LeaveService(data, writeDb, calendar, notifications, null, processes);
    if (leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, blockedDate, LeavePeriod.FullDay, blockedDate, LeavePeriod.FullDay, "日历覆盖验证")).IsSuccess)
        throw new InvalidOperationException("企业设置的非工作日仍允许创建有效请假单。");
    var draft = leave.CreateDraft(employee, new CreateLeaveRequest(
        LeaveType.Personal, leaveDate, LeavePeriod.FullDay,
        leaveDate, LeavePeriod.FullDay, "数据库持久化验证", [fileId.ToString()], [hr.Id]));
    if (!draft.IsSuccess) throw new InvalidOperationException(draft.Error);
    var submitted = leave.Submit(employee, draft.Value!.Id);
    if (!submitted.IsSuccess || submitted.Value!.Tasks.Count != 1) throw new InvalidOperationException(submitted.Error ?? "请假提交或审批任务创建失败。");
    var copyService = new FlowCopyService(data, writeDb);
    if (copyService.ListMine(hr).Any(item => item.BusinessId == submitted.Value.Id) || leave.Get(hr, submitted.Value.Id).IsSuccess)
        throw new InvalidOperationException("审批完成前抄送事项被提前暴露给抄送人。");
    var transferredLeave = leave.Transfer(data.GetEmployee("u-li"), submitted.Value.Tasks[0].Id, new TransferTaskRequest("u-wang", "部门负责人外出，转办总经理"));
    if (!transferredLeave.IsSuccess || transferredLeave.Value!.Tasks[0].AssigneeId != "u-wang") throw new InvalidOperationException("请假审批任务转办失败。");
    if (!leave.Approve(data.GetEmployee("u-wang"), submitted.Value.Tasks[0].Id, "已处理转办任务").IsSuccess) throw new InvalidOperationException("转办后的请假任务无法完成。");
    var leaveCopy = copyService.ListMine(hr).SingleOrDefault(item => item.BusinessId == submitted.Value.Id);
    if (leaveCopy is null || !leave.Get(hr, submitted.Value.Id).IsSuccess || leaveCopy.ReadAt is not null)
        throw new InvalidOperationException("请假完成后未激活抄送事项、只读权限或未读状态。");
    if (copyService.MarkRead(employee, leaveCopy.Id).Code != "DATA_001" || !copyService.MarkRead(hr, leaveCopy.Id).IsSuccess)
        throw new InvalidOperationException("抄送事项已阅权限或正常已阅失败。");
    leaveCopyId = leaveCopy.Id;
    requestId = submitted.Value.Id;

    var publishedLeave = definitions.Value.Single(item => item.BusinessType == "Leave");
    var immutableUpdate = processes.Update(administrator, publishedLeave.Id, new UpdateProcessDefinitionRequest(publishedLeave.Name, publishedLeave.Routes.Select(route => new ProcessRouteInput(route.MaxValue, route.ApproverKeys)).ToList()));
    if (immutableUpdate.Code != "STATE_001")
        throw new InvalidOperationException("已发布流程版本仍可直接修改。");
    var cloned = processes.Clone(administrator, publishedLeave.Id);
    if (!cloned.IsSuccess || cloned.Value!.Version != 2 || cloned.Value.Status != ProcessDefinitionStatus.Draft)
        throw new InvalidOperationException("流程定义无法复制为递增的草稿版本。");
    var updatedProcess = processes.Update(administrator, cloned.Value.Id, new UpdateProcessDefinitionRequest("默认请假审批（严格版）",
    [
        new ProcessRouteInput(.5m, ["DIRECT_MANAGER"]),
        new ProcessRouteInput(null, ["DIRECT_MANAGER", "ROLE:总经理"])
    ]));
    if (!updatedProcess.IsSuccess || !processes.Publish(administrator, cloned.Value.Id).IsSuccess)
        throw new InvalidOperationException("流程草稿无法保存或发布。");
    var versionedDate = leaveDate.AddDays(14);
    while (!calendar.IsWorkingDay(versionedDate)) versionedDate = versionedDate.AddDays(1);
    var versionedDraft = leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Sick, versionedDate, LeavePeriod.FullDay, versionedDate, LeavePeriod.FullDay, "流程版本切换验证"));
    if (!versionedDraft.IsSuccess) throw new InvalidOperationException(versionedDraft.Error);
    var versionedSubmission = leave.Submit(employee, versionedDraft.Value!.Id);
    if (!versionedSubmission.IsSuccess || versionedSubmission.Value!.ProcessDefinitionVersion != 2 || versionedSubmission.Value.Tasks.Count != 2)
        throw new InvalidOperationException("新提交请假单未使用最新发布流程版本。");
    if (leave.GetPendingTasks(data.GetEmployee("u-wang")).Any(task => task.LeaveRequestId == versionedSubmission.Value.Id))
        throw new InvalidOperationException("未来审批节点在前序节点处理前错误进入待办。");
    if (!leave.Reject(data.GetEmployee("u-li"), versionedSubmission.Value.Tasks[0].Id, "请补充工作交接").IsSuccess || versionedSubmission.Value.Tasks[1].Status != FlowTaskStatus.Cancelled)
        throw new InvalidOperationException("驳回未终止流程实例或未取消后续待办。");
    var revisedVersioned = leave.Update(employee, versionedSubmission.Value.Id, versionedSubmission.Value.Version, new CreateLeaveRequest(LeaveType.Sick, versionedDate, LeavePeriod.FullDay, versionedDate, LeavePeriod.FullDay, "已补充工作交接"));
    var resubmittedVersioned = revisedVersioned.IsSuccess ? leave.Submit(employee, revisedVersioned.Value!.Id) : ServiceResult<LeaveRequest>.Failure(revisedVersioned.Error!);
    if (!resubmittedVersioned.IsSuccess || resubmittedVersioned.Value!.FlowInstances.Count != 2 || resubmittedVersioned.Value.FlowInstances[0].Status != FlowInstanceStatus.Rejected || resubmittedVersioned.Value.FlowInstances[1].Attempt != 2)
        throw new InvalidOperationException("驳回重提未创建递增的新流程实例或旧轨迹丢失。");
    if (submitted.Value.ProcessDefinitionVersion != 1 || submitted.Value.Tasks.Count != 1)
        throw new InvalidOperationException("发布新流程版本影响了既有请假单的版本或任务。");
    versionedRequestId = resubmittedVersioned.Value.Id;

    var invalidExpenseScope = processes.Create(administrator, new CreateProcessDefinitionRequest(
        "EXPENSE_INVALID_SCOPE", "无效报销假别范围", "Expense",
        [new ProcessRouteInput(null, ["ROLE:财务专员"])], 10, [], ["Sick"]));
    if (invalidExpenseScope.IsSuccess || invalidExpenseScope.Code != "VALIDATION_001")
        throw new InvalidOperationException("报销流程错误接受了请假类型适用条件。");

    var companySickProcess = processes.Create(administrator, new CreateProcessDefinitionRequest(
        "LEAVE_COMPANY_SICK", "公司病假审批", "Leave",
        [new ProcessRouteInput(null, ["ROLE:总经理"])], 15, ["general"], ["Sick"]));
    if (!companySickProcess.IsSuccess || !processes.Publish(administrator, companySickProcess.Value!.Id).IsSuccess)
        throw new InvalidOperationException("公司级病假流程无法创建或发布。");

    var engineeringSickProcess = processes.Create(administrator, new CreateProcessDefinitionRequest(
        "LEAVE_ENGINEERING_SICK", "研发病假审批", "Leave",
        [new ProcessRouteInput(null, ["ROLE:总经理"])], 20, ["engineering"], ["Sick"]));
    if (!engineeringSickProcess.IsSuccess || !processes.Publish(administrator, engineeringSickProcess.Value!.Id).IsSuccess)
        throw new InvalidOperationException("研发病假流程无法创建或发布。");
    scopedProcessId = engineeringSickProcess.Value.Id;

    var companyResolved = processes.Resolve("Leave", data.GetEmployee("u-chen"), 1m, "Sick");
    if (!companyResolved.IsSuccess || companyResolved.Value!.Code != "LEAVE_COMPANY_SICK")
        throw new InvalidOperationException("公司级部门范围未包含财务部下级组织，或病假条件未生效。");

    var scopedDate = leaveDate.AddDays(42);
    while (!calendar.IsWorkingDay(scopedDate)) scopedDate = scopedDate.AddDays(1);
    var scopedDraft = leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Sick, scopedDate, LeavePeriod.FullDay, scopedDate, LeavePeriod.FullDay, "研发病假流程范围验证"));
    var scopedSubmission = scopedDraft.IsSuccess ? leave.Submit(employee, scopedDraft.Value!.Id) : ServiceResult<LeaveRequest>.Failure(scopedDraft.Error!);
    if (!scopedSubmission.IsSuccess || scopedSubmission.Value!.ProcessDefinitionCode != "LEAVE_ENGINEERING_SICK" || scopedSubmission.Value.ProcessDefinitionVersion != 1 || scopedSubmission.Value.Tasks.SingleOrDefault()?.AssigneeId != "u-wang")
        throw new InvalidOperationException("研发病假未选择更高优先级的部门/假别流程或任务快照错误。");
    scopedRequestId = scopedSubmission.Value.Id;

    var conflictingProcess = processes.Create(administrator, new CreateProcessDefinitionRequest(
        "LEAVE_ENGINEERING_SICK_CONFLICT", "研发病假冲突流程", "Leave",
        [new ProcessRouteInput(null, ["DIRECT_MANAGER"])], 20, ["engineering"], ["Sick"]));
    if (!conflictingProcess.IsSuccess || processes.Publish(administrator, conflictingProcess.Value!.Id).Code != "FLOW_003")
        throw new InvalidOperationException("同优先级、同具体度且范围重叠的流程未在发布时拒绝。");
    if (versionedSubmission.Value.ProcessDefinitionCode != "LEAVE_DEFAULT" || versionedSubmission.Value.ProcessDefinitionVersion != 2)
        throw new InvalidOperationException("发布适用范围流程影响了既有单据的流程版本快照。");

    var delegationService = new DelegationService(writeDb, data);
    var delegation = delegationService.Create(manager, new CreateFlowDelegationRequest("u-wang", "Leave", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2), "外出期间委托王总代办请假审批"));
    if (!delegation.IsSuccess || !delegation.Value!.IsEffective || delegationService.ListMine(manager).Count != 1)
        throw new InvalidOperationException("有效审批委托无法创建或查询。");
    if (delegationService.Create(manager, new CreateFlowDelegationRequest("u-chen", "All", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), "重叠委托验证")).Code != "FLOW_004")
        throw new InvalidOperationException("系统允许创建业务范围与时间重叠的审批委托。");
    var delegatedDate = leaveDate.AddDays(28);
    while (!calendar.IsWorkingDay(delegatedDate)) delegatedDate = delegatedDate.AddDays(1);
    var delegatedDraft = leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, delegatedDate, LeavePeriod.FullDay, delegatedDate, LeavePeriod.FullDay, "审批委托生效验证"));
    if (!delegatedDraft.IsSuccess) throw new InvalidOperationException(delegatedDraft.Error);
    var delegatedSubmission = leave.Submit(employee, delegatedDraft.Value!.Id);
    if (!delegatedSubmission.IsSuccess || delegatedSubmission.Value!.Tasks.Count != 1 || delegatedSubmission.Value.Tasks[0].AssigneeId != "u-wang" || delegatedSubmission.Value.Tasks[0].OriginalAssigneeId != "u-li" || delegatedSubmission.Value.Tasks[0].DelegationId != delegation.Value.Id)
        throw new InvalidOperationException("有效委托未替换实际审批人或未保留原审批人快照。");
    if (delegationService.Cancel(employee, delegation.Value.Id).Code != "DATA_001" || !delegationService.Cancel(manager, delegation.Value.Id).IsSuccess)
        throw new InvalidOperationException("审批委托取消权限或正常取消失败。");
    delegatedRequestId = delegatedSubmission.Value.Id;
    var postCancellationDate = leaveDate.AddDays(35);
    while (!calendar.IsWorkingDay(postCancellationDate)) postCancellationDate = postCancellationDate.AddDays(1);
    var postCancellationDraft = leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, postCancellationDate, LeavePeriod.FullDay, postCancellationDate, LeavePeriod.FullDay, "审批委托取消验证"));
    if (!postCancellationDraft.IsSuccess) throw new InvalidOperationException(postCancellationDraft.Error);
    var postCancellationSubmission = leave.Submit(employee, postCancellationDraft.Value!.Id);
    if (!postCancellationSubmission.IsSuccess || postCancellationSubmission.Value!.Tasks.Count != 2 || postCancellationSubmission.Value.Tasks[0].AssigneeId != "u-li" || postCancellationSubmission.Value.Tasks[0].DelegationId is not null)
        throw new InvalidOperationException("取消委托后新任务仍错误分配给代办人。");
    postCancellationRequestId = postCancellationSubmission.Value.Id;

    var expense = new ExpenseService(data, writeDb, notifications, null, processes);
    var claim = expense.CreateDraft(employee, new CreateExpenseClaim(
        null, "张晨", "6222026000001234", "模拟银行", "持久化报销验证",
        [new ExpenseItem(new DateOnly(2026, 8, 20), "交通", 120m, "客户拜访", receiptNumber, ["receipt.pdf"])], [hr.Id]));
    if (!claim.IsSuccess) throw new InvalidOperationException(claim.Error);
    var expenseSubmitted = expense.Submit(employee, claim.Value!.Id);
    if (!expenseSubmitted.IsSuccess || expenseSubmitted.Value!.Tasks.Count != 2) throw new InvalidOperationException(expenseSubmitted.Error ?? "报销提交或审批任务创建失败。");
    var transferredExpense = expense.Transfer(data.GetEmployee("u-li"), expenseSubmitted.Value.Tasks[0].Id, new TransferTaskRequest("u-wang", "部门负责人外出，转办总经理"));
    if (!transferredExpense.IsSuccess || transferredExpense.Value!.Tasks[0].AssigneeId != "u-wang") throw new InvalidOperationException("报销审批任务转办失败。");
    if (!expense.Approve(data.GetEmployee("u-wang"), expenseSubmitted.Value.Tasks[0].Id, "已处理转办任务").IsSuccess) throw new InvalidOperationException("转办后的报销任务无法完成。");
    if (copyService.ListMine(hr).Any(item => item.BusinessId == expenseSubmitted.Value.Id))
        throw new InvalidOperationException("报销前序节点通过后抄送事项被提前激活。");
    if (!expense.Approve(data.GetEmployee("u-chen"), expenseSubmitted.Value.Tasks[1].Id, "财务审批通过").IsSuccess)
        throw new InvalidOperationException("报销最终节点无法完成。");
    var expenseCopy = copyService.ListMine(hr).SingleOrDefault(item => item.BusinessId == expenseSubmitted.Value.Id);
    if (expenseCopy is null || !expense.Get(hr, expenseSubmitted.Value.Id).IsSuccess || expenseCopy.ReadAt is not null)
        throw new InvalidOperationException("报销审批完成后未激活抄送事项或只读权限。");
    expenseCopyId = expenseCopy.Id;
    expenseId = expenseSubmitted.Value.Id;

    var finance = data.GetEmployee("u-chen");
    var securedExpense = new ExpenseService(data, writeDb, notifications, fileService, processes);
    var invalidProof = securedExpense.RegisterPayment(finance, expenseId, new RegisterPaymentRequest(DateOnly.FromDateTime(DateTime.Today), "企业网银", "TX-PG-WRONG", expenseSubmitted.Value.TotalAmount, fileId.ToString()));
    if (invalidProof.Code != "FILE_005")
        throw new InvalidOperationException("付款人可以冒用其他员工上传的附件作为付款凭证。");
    await using var paymentContent = new MemoryStream("payment-proof-content"u8.ToArray());
    var paymentFile = fileService.Upload(finance, new FormFile(paymentContent, 0, paymentContent.Length, "file", "payment-proof.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" });
    if (!paymentFile.IsSuccess) throw new InvalidOperationException(paymentFile.Error);
    paymentProofId = paymentFile.Value!.Id;
    var payment = securedExpense.RegisterPayment(finance, expenseId, new RegisterPaymentRequest(DateOnly.FromDateTime(DateTime.Today), "企业网银", "TX-PG-VALID", expenseSubmitted.Value.TotalAmount, paymentProofId.ToString()));
    if (!payment.IsSuccess || payment.Value!.Status != ExpenseStatus.Completed || payment.Value.Payment?.ProofFile != paymentProofId.ToString())
        throw new InvalidOperationException(payment.Error ?? "真实付款凭证无法关联报销付款记录。");

    var travel = new TravelService(data, writeDb, notifications, fileService, processes);
    var travelStart = DateOnly.FromDateTime(DateTime.Today).AddDays(-10);
    var travelDraft = travel.CreateDraft(employee, new SaveTravelRequest(
        "客户现场需求确认和方案汇报", 5600m,
        [new TravelItineraryItem("上海", travelStart, travelStart.AddDays(3), "高铁", "客户访谈、方案评审与项目计划确认")],
        ["u-sun"], [fileId.ToString()], ["u-chen"]));
    if (!travelDraft.IsSuccess || travelDraft.Value!.Days != 4 || travelDraft.Value.CompanionNames.SingleOrDefault() != "孙悦")
        throw new InvalidOperationException(travelDraft.Error ?? "出差草稿、日历天数或同行人快照创建失败。");
    var travelSubmitted = travel.Submit(employee, travelDraft.Value.Id);
    if (!travelSubmitted.IsSuccess || travelSubmitted.Value!.Tasks.Count != 2 || travelSubmitted.Value.ProcessDefinitionCode != "TRAVEL_DEFAULT")
        throw new InvalidOperationException(travelSubmitted.Error ?? "超过 3 天出差未按直属上级和总经理两级审批路由。");
    if (!travel.Approve(data.GetEmployee("u-li"), travelSubmitted.Value.Tasks[0].Id, "行程安排合理").IsSuccess ||
        !travel.Approve(data.GetEmployee("u-wang"), travelSubmitted.Value.Tasks[1].Id, "批准出差").IsSuccess || travelSubmitted.Value.Status != TravelStatus.Approved)
        throw new InvalidOperationException("出差两级审批无法完成。");
    var travelCopy = copyService.ListMine(data.GetEmployee("u-chen")).SingleOrDefault(item => item.BusinessType == "Travel" && item.BusinessId == travelSubmitted.Value.Id);
    if (travelCopy is null || !travel.Get(data.GetEmployee("u-chen"), travelSubmitted.Value.Id).IsSuccess)
        throw new InvalidOperationException("出差审批完成后未激活抄送事项或抄送只读权限。");
    travelId = travelSubmitted.Value.Id;

    var shortStart = travelStart.AddDays(20);
    var shortDraft = travel.CreateDraft(employee, new SaveTravelRequest("短期项目启动会", 800m, [new TravelItineraryItem("苏州", shortStart, shortStart, "高铁", "参加项目启动会")]));
    var shortSubmitted = shortDraft.IsSuccess ? travel.Submit(employee, shortDraft.Value!.Id) : ServiceResult<TravelRequest>.Failure(shortDraft.Error!);
    if (!shortSubmitted.IsSuccess || shortSubmitted.Value!.Tasks.Count != 1 || shortSubmitted.Value.Tasks[0].AssigneeId != "u-li" || !travel.Withdraw(employee, shortSubmitted.Value.Id).IsSuccess || shortSubmitted.Value.Status != TravelStatus.Withdrawn)
        throw new InvalidOperationException("3 天内出差路由或未审批撤回失败。");
    withdrawnTravelId = shortSubmitted.Value.Id;

    var overlapDraft = travel.CreateDraft(employee, new SaveTravelRequest("重复时间验证", 100m, [new TravelItineraryItem("上海", travelStart.AddDays(1), travelStart.AddDays(2), "高铁", "验证重复出差")]))!;
    if (!overlapDraft.IsSuccess || travel.Submit(employee, overlapDraft.Value!.Id).Code != "TRAVEL_002")
        throw new InvalidOperationException("系统允许提交与已批准出差日期重叠的申请。");

    var linkedExpenseService = new ExpenseService(data, writeDb, notifications, fileService, processes, null, copyService, travel);
    var linkedExpense = linkedExpenseService.CreateDraft(employee, new CreateExpenseClaim(
        null, "张晨", "6222026000001234", "模拟银行", "上海出差交通费用",
        [new ExpenseItem(travelStart, "交通", 680m, "往返高铁票", $"TRAVEL-{Guid.NewGuid():N}", [fileId.ToString()])], null, travelId));
    if (!linkedExpense.IsSuccess || linkedExpense.Value!.TravelRequestId != travelId || linkedExpense.Value.TravelRequestNumber != travelSubmitted.Value.Number)
        throw new InvalidOperationException(linkedExpense.Error ?? "已批准出差无法关联报销草稿。");
    linkedExpenseId = linkedExpense.Value.Id;
    var invalidTravelLink = linkedExpenseService.CreateDraft(employee, new CreateExpenseClaim(
        null, "张晨", "6222026000001234", "模拟银行", "无效出差关联",
        [new ExpenseItem(travelStart, "交通", 10m, "关联撤回出差", null, [fileId.ToString()])], null, withdrawnTravelId));
    if (invalidTravelLink.Code != "TRAVEL_004") throw new InvalidOperationException("报销可以关联未批准或已撤回的出差申请。");

    var idempotency = new IdempotencyService(writeDb);
    idempotency.Store(employee.Id, "POST:/api/v1/leave-requests", idempotencyKey, 201, "{\"id\":\"cached\"}");
    if (idempotency.Find(employee.Id, "POST:/api/v1/leave-requests", idempotencyKey) is not { StatusCode: 201 })
        throw new InvalidOperationException("幂等键未能持久化或重新读取。");
}

await using (var readDb = new OaDbContext(options))
{
    var persistedUser = await readDb.Users.SingleOrDefaultAsync(item => item.Id == managedUserId);
    var persistedAccount = await readDb.UserAccounts.SingleOrDefaultAsync(item => item.UserId == managedUserId);
    if (persistedUser is not { Name: "集成测试用户（已更新）", DepartmentId: "hr", PositionId: null, ManagerId: "u-wang", CumulativeWorkYears: 5, Version: 6 } ||
        persistedAccount is null || persistedAccount.PasswordHash == "ResetPass123" || persistedAccount.PasswordSalt == "ResetPass123" ||
        !PasswordHasher.Verify("ResetPass123", persistedAccount.PasswordSalt, persistedAccount.PasswordHash, persistedAccount.PasswordIterations) ||
        persistedAccount.LockedUntil is not null || persistedAccount.FailedLoginCount != 0)
        throw new InvalidOperationException("用户资料、密码哈希或账号锁定状态未跨 DbContext 持久化。");
    if (await readDb.UserRoles.CountAsync(item => item.UserId == managedUserId && item.RoleCode == "HR/行政" && item.IsPrimary) != 1 ||
        await readDb.UserRoles.AnyAsync(item => item.UserId == managedUserId && item.RoleCode == "员工"))
        throw new InvalidOperationException("用户角色替换未跨 DbContext 持久化。");
    var reloadedDirectory = new DemoData(readDb);
    var managedEmployee = reloadedDirectory.GetEmployee(managedUserId);
    if (!reloadedDirectory.HasRole(managedEmployee, "HR/行政") || !reloadedDirectory.HasPermission(managedEmployee, OaPermissions.CalendarManage) || reloadedDirectory.HasPermission(managedEmployee, OaPermissions.UserManage))
        throw new InvalidOperationException("重建身份目录后未能从角色解析有效权限。");
    if (await readDb.AuditLogs.CountAsync(item => item.ResourceType == "User" && item.ResourceId == managedUserId) < 3)
        throw new InvalidOperationException("用户创建、更新或密码重置未完整生成审计记录。");
    var persistedPersonnel = await readDb.PersonnelProfiles.SingleOrDefaultAsync(item => item.UserId == "u-zhang");
    if (persistedPersonnel is not { WorkEmail: "zhang.chen@xxx.example", WorkLocation: "上海办公室", Version: 2 } ||
        await readDb.PersonnelEvents.CountAsync(item => item.UserId == "u-zhang") != 2 ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "PersonnelProfile" && item.ResourceId == "u-zhang"))
        throw new InvalidOperationException("人事档案、生命周期事件或审计日志未跨 DbContext 持久化。");
    if (await readDb.Announcements.CountAsync() != 3 || await readDb.AnnouncementReads.CountAsync(item => item.AnnouncementId == announcementId && item.UserId == "u-zhang") != 1 || await readDb.AuditLogs.CountAsync(item => item.ResourceType == "Announcement") < 8)
        throw new InvalidOperationException("公告、已读确认或公告审计未跨 DbContext 持久化。");

    var reloaded = new LeaveService(data, readDb);
    var employee = data.GetEmployee("u-zhang");
    var request = reloaded.List(employee).SingleOrDefault(x => x.Id == requestId);
    if (request is null || request.Status != LeaveStatus.Completed || request.Tasks.Count != 1 || request.Tasks[0].AssigneeId != "u-wang")
        throw new InvalidOperationException("重建 DbContext 后未能恢复请假单或审批任务。");
    if (request.ProcessDefinitionVersion != 1 || request.ProcessDefinitionCode != "LEAVE_DEFAULT")
        throw new InvalidOperationException("请假单绑定的流程版本快照未持久化。");
    var versionedRequest = reloaded.List(employee).SingleOrDefault(item => item.Id == versionedRequestId);
    if (versionedRequest?.ProcessDefinitionVersion != 2 || versionedRequest.Tasks.Count != 2 || versionedRequest.FlowInstances.Count != 2 || versionedRequest.CurrentFlowInstanceId != versionedRequest.FlowInstances[1].Id)
        throw new InvalidOperationException("最新发布流程版本及其新单据任务未跨 DbContext 保留。");
    if (versionedRequest.FlowInstances[0].Status != FlowInstanceStatus.Rejected || !versionedRequest.FlowInstances[0].Actions.Any(action => action.Action == FlowActionType.Rejected && action.Comment == "请补充工作交接") || versionedRequest.FlowInstances[1].Status != FlowInstanceStatus.Running)
        throw new InvalidOperationException("旧流程实例状态、驳回意见或重提实例未跨 DbContext 永久保留。");
    if (versionedRequest.Tasks.Any(task => task.FlowInstanceId != versionedRequest.CurrentFlowInstanceId))
        throw new InvalidOperationException("重提后的审批任务未绑定当前流程实例。");
    var delegatedRequest = reloaded.List(employee).SingleOrDefault(item => item.Id == delegatedRequestId);
    if (delegatedRequest?.Tasks.SingleOrDefault() is not { AssigneeId: "u-wang", OriginalAssigneeId: "u-li", DelegationId: not null })
        throw new InvalidOperationException("委托任务的实际审批人、原审批人或委托标识未持久化。");
    var postCancellationRequest = reloaded.List(employee).SingleOrDefault(item => item.Id == postCancellationRequestId);
    if (postCancellationRequest?.Tasks.FirstOrDefault() is not { AssigneeId: "u-li", DelegationId: null })
        throw new InvalidOperationException("委托取消后的任务分配未跨 DbContext 保留。");
    if (!reloaded.GetProcessedTasks(data.GetEmployee("u-wang")).Any(task => task.Id == request.Tasks[0].Id))
        throw new InvalidOperationException("重建 DbContext 后已处理请假任务未进入我的已办。");
    if (request.Attachments.SingleOrDefault() != fileId.ToString())
        throw new InvalidOperationException("请假单未能持久化关联的附件标识。");
    var fileService = new FileService(readDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build());
    var opened = fileService.Open(employee, fileId);
    if (!opened.IsSuccess || opened.Value!.Content.Length != "demo-pdf-content"u8.Length)
        throw new InvalidOperationException("附件元数据或文件内容未能跨 DbContext 读取。");
    await opened.Value.Content.DisposeAsync();
    var openedPayment = fileService.Open(data.GetEmployee("u-chen"), paymentProofId);
    if (!openedPayment.IsSuccess || openedPayment.Value!.Content.Length != "payment-proof-content"u8.Length)
        throw new InvalidOperationException("付款凭证文件未能跨 DbContext 读取。");
    await openedPayment.Value.Content.DisposeAsync();
    if (await readDb.AuditLogs.CountAsync(x => x.ResourceType == "LeaveRequest" && x.ResourceId == requestId.ToString()) < 2)
        throw new InvalidOperationException("请假创建与提交未生成审计记录。");
    var reloadedExpense = new ExpenseService(data, readDb).List(employee).SingleOrDefault(x => x.Id == expenseId);
    if (reloadedExpense is null || reloadedExpense.Status != ExpenseStatus.Completed || reloadedExpense.Items.Count != 1 || reloadedExpense.Tasks.Count != 2 || reloadedExpense.Tasks[0].AssigneeId != "u-wang")
        throw new InvalidOperationException("重建 DbContext 后未能恢复报销单、明细或审批任务。");
    if (reloadedExpense.Payment is not { PaymentMethod: "企业网银", TransactionNumber: "TX-PG-VALID" } persistedPayment || persistedPayment.ProofFile != paymentProofId.ToString())
        throw new InvalidOperationException("付款方式、流水号或付款凭证未跨 DbContext 持久化。");
    if (reloadedExpense.Tasks.Any(task => task.ExpenseClaimId != expenseId))
        throw new InvalidOperationException("报销审批任务未保留关联报销单标识，详情页无法跳转。");
    if (!new ExpenseService(data, readDb).GetProcessedTasks(data.GetEmployee("u-wang")).Any(task => task.Id == reloadedExpense.Tasks[0].Id))
        throw new InvalidOperationException("重建 DbContext 后已处理报销任务未进入我的已办。");
    if (reloadedExpense.FlowInstances.SingleOrDefault() is not { Status: FlowInstanceStatus.Completed } expenseInstance || !expenseInstance.Actions.Any(action => action.Action == FlowActionType.Transferred) || expenseInstance.Actions.Count(action => action.Action == FlowActionType.Approved) != 2)
        throw new InvalidOperationException("报销流程实例的转办或审批动作未跨 DbContext 保留。");
    if (await readDb.AuditLogs.CountAsync(x => x.ResourceType == "ExpenseClaim" && x.ResourceId == expenseId.ToString()) < 2)
        throw new InvalidOperationException("报销创建与提交未生成审计记录。");
    if (!await readDb.AuditLogs.AnyAsync(x => x.ResourceType == "ExpenseClaim" && x.ResourceId == expenseId.ToString() && x.Action == "EXPENSE_PAYMENT_REGISTERED"))
        throw new InvalidOperationException("付款登记未生成审计记录。");
    var reloadedTravelService = new TravelService(data, readDb);
    var reloadedTravel = reloadedTravelService.List(employee).SingleOrDefault(item => item.Id == travelId);
    if (reloadedTravel is null || reloadedTravel.Status != TravelStatus.Approved || reloadedTravel.Tasks.Count != 2 || reloadedTravel.Itinerary.SingleOrDefault()?.Destination != "上海" || reloadedTravel.CompanionNames.SingleOrDefault() != "孙悦" || reloadedTravel.Attachments.SingleOrDefault() != fileId.ToString())
        throw new InvalidOperationException("出差申请、行程、同行人、附件或审批任务未跨 DbContext 持久化。");
    if (reloadedTravel.FlowInstances.SingleOrDefault() is not { Status: FlowInstanceStatus.Completed } travelInstance || travelInstance.Actions.Count(action => action.Action == FlowActionType.Approved) != 2)
        throw new InvalidOperationException("出差流程实例或审批动作轨迹未跨 DbContext 持久化。");
    if (reloadedTravelService.List(employee).SingleOrDefault(item => item.Id == withdrawnTravelId)?.Status != TravelStatus.Withdrawn || await readDb.AuditLogs.CountAsync(item => item.ResourceType == "TravelRequest" && item.ResourceId == travelId.ToString()) < 4)
        throw new InvalidOperationException("出差撤回状态或审计记录未持久化。");
    var reloadedLinkedExpense = new ExpenseService(data, readDb).List(employee).SingleOrDefault(item => item.Id == linkedExpenseId);
    if (reloadedLinkedExpense?.TravelRequestId != travelId || reloadedLinkedExpense.TravelRequestNumber != reloadedTravel.Number)
        throw new InvalidOperationException("报销与出差申请的关联未跨 DbContext 持久化。");
    if (!await readDb.IdempotencyKeys.AnyAsync(x => x.Key == idempotencyKey))
        throw new InvalidOperationException("幂等键跨 DbContext 未保留。");
    if (!await readDb.WorkCalendarEntries.AnyAsync(x => x.Date == new DateOnly(2031, 1, 6) && !x.IsWorkingDay))
        throw new InvalidOperationException("企业工作日历覆盖未持久化。");
    var persistedAttendance = await readDb.AttendanceRecords.SingleOrDefaultAsync(item => item.Id == attendanceRecordId);
    var persistedAppeal = await readDb.AttendanceAppeals.SingleOrDefaultAsync(item => item.Id == attendanceAppealId);
    if (persistedAttendance is not { Status: AttendanceStatuses.Corrected, OriginalStatus: AttendanceStatuses.Late } || persistedAppeal is not { Status: AttendanceAppealStatuses.Approved } ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "AttendanceAppeal" && item.ResourceId == attendanceAppealId.ToString()))
        throw new InvalidOperationException("考勤记录、原始异常、申诉结果或审计日志未跨 DbContext 持久化。");
    var persistedMonthLock = await readDb.AttendanceMonthLocks.SingleOrDefaultAsync(item => item.Month == attendanceLockMonth);
    if (persistedMonthLock is not { IsLocked: true, Version: 3 } ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "AttendanceMonthLock" && item.ResourceId == attendanceLockMonth.ToString("yyyy-MM") && item.Action == "ATTENDANCE_MONTH_LOCKED") ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "AttendanceMonthLock" && item.ResourceId == attendanceLockMonth.ToString("yyyy-MM") && item.Action == "ATTENDANCE_MONTH_UNLOCKED"))
        throw new InvalidOperationException("考勤月度封账状态、版本或审计记录未跨 DbContext 持久化。");
    var persistedContract = await readDb.EmploymentContracts.SingleOrDefaultAsync(item => item.Id == employmentContractId);
    var persistedRenewal = await readDb.EmploymentContracts.SingleOrDefaultAsync(item => item.Id == renewedEmploymentContractId);
    if (persistedContract is not { Status: EmploymentContractStatuses.Superseded } || persistedRenewal is not { Status: EmploymentContractStatuses.Active } || persistedRenewal.RenewalOfId != employmentContractId ||
        await readDb.EmploymentContractEvents.CountAsync(item => item.ContractId == employmentContractId) < 4 ||
        await readDb.ContractAlertAcknowledgements.CountAsync(item => item.ContractId == employmentContractId && item.ThresholdDays == 30) != 1 ||
        await readDb.ContractAlertDeliveries.CountAsync(item => item.ContractId == employmentContractId && item.ThresholdDays == 30) < 1 ||
        !await readDb.Notifications.AnyAsync(item => item.ResourceType == "EmploymentContract" && item.ResourceId == employmentContractId.ToString() && item.Type == "CONTRACT_EXPIRY_30") ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "EmploymentContract" && item.ResourceId == renewedEmploymentContractId.ToString() && item.Action == "EMPLOYMENT_CONTRACT_RENEWED"))
        throw new InvalidOperationException("劳动合同续签链、事件、预警确认或审计日志未跨 DbContext 持久化。");
    if (await readDb.ProcessDefinitions.CountAsync(item => item.Code == "LEAVE_DEFAULT") != 2 || await readDb.ProcessDefinitions.CountAsync(item => item.Code == "LEAVE_DEFAULT" && item.Status == (int)ProcessDefinitionStatus.Published) != 1)
        throw new InvalidOperationException("流程定义版本归档和唯一当前发布版本未持久化。");
    var scopedDefinition = await readDb.ProcessDefinitions.SingleOrDefaultAsync(item => item.Id == scopedProcessId);
    if (scopedDefinition is not { Priority: 20, Status: (int)ProcessDefinitionStatus.Published } ||
        await readDb.ProcessScopes.CountAsync(item => item.ProcessDefinitionId == scopedProcessId) != 2 ||
        !await readDb.ProcessScopes.AnyAsync(item => item.ProcessDefinitionId == scopedProcessId && item.ScopeType == "Department" && item.Value == "engineering") ||
        !await readDb.ProcessScopes.AnyAsync(item => item.ProcessDefinitionId == scopedProcessId && item.ScopeType == "LeaveType" && item.Value == "Sick"))
        throw new InvalidOperationException("流程优先级或部门/假别适用范围未跨 DbContext 持久化。");
    var scopedRequest = reloaded.List(employee).SingleOrDefault(item => item.Id == scopedRequestId);
    if (scopedRequest?.ProcessDefinitionCode != "LEAVE_ENGINEERING_SICK" || scopedRequest.ProcessDefinitionVersion != 1)
        throw new InvalidOperationException("适用范围流程选择结果未持久化到请假单快照。");
    if (await readDb.FlowDelegations.CountAsync(item => item.OwnerId == "u-li" && item.Status == (int)FlowDelegationStatus.Cancelled) != 1 || await readDb.AuditLogs.CountAsync(item => item.ResourceType == "FlowDelegation") < 2)
        throw new InvalidOperationException("审批委托取消状态或审计记录未持久化。");
    if (await readDb.FlowInstances.CountAsync() < 2 || await readDb.FlowActions.CountAsync() < 4)
        throw new InvalidOperationException("独立流程实例或动作轨迹未写入持久化表。");
    var reloadedCopies = new FlowCopyService(data, readDb).ListMine(data.GetEmployee("u-sun"));
    if (reloadedCopies.SingleOrDefault(item => item.Id == leaveCopyId)?.ReadAt is null || reloadedCopies.SingleOrDefault(item => item.Id == expenseCopyId)?.ReadAt is not null)
        throw new InvalidOperationException("抄送事项及其独立已阅/未读状态未跨 DbContext 保留。");
    if (!new LeaveService(data, readDb).Get(data.GetEmployee("u-sun"), requestId).IsSuccess || !new ExpenseService(data, readDb).Get(data.GetEmployee("u-sun"), expenseId).IsSuccess)
        throw new InvalidOperationException("抄送人的请假或报销只读数据权限未跨 DbContext 保留。");
    var auditService = new AuditService(readDb, data);
    var auditResult = auditService.List(data.GetEmployee("u-admin"), new AuditLogQuery("提交", "u-zhang", "LeaveRequest", null, null, 1, 1));
    if (!auditResult.IsSuccess || auditResult.Value is null || auditResult.Value.Total < 1 || auditResult.Value.Items.Count != 1 || auditResult.Value.PageSize != 1)
        throw new InvalidOperationException("系统管理员无法按操作人、资源类型和关键字分页查询审计日志。");
    var deniedAuditResult = auditService.List(employee, new AuditLogQuery(null, null, null, null, null, 1, 20));
    if (deniedAuditResult.IsSuccess || deniedAuditResult.Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权查询审计日志。");
    var manager = data.GetEmployee("u-li");
    var notificationService = new NotificationService(readDb);
    var notification = notificationService.List(manager).FirstOrDefault(item => item.ResourceId == requestId.ToString());
    if (notification is null || !notificationService.MarkRead(manager, notification.Id).IsSuccess)
        throw new InvalidOperationException("待办通知未生成或无法标记已读。");
    if (notificationService.List(data.GetEmployee("u-sun")).Count(item => item.Type == "FLOW_COPY" && (item.ResourceId == requestId.ToString() || item.ResourceId == expenseId.ToString())) != 2)
        throw new InvalidOperationException("请假或报销流程完成后未向抄送人发送站内通知。");
}

Console.WriteLine("PostgreSQL persistence integration passed.");

file sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "Oa.Postgres.Tests";
    public string WebRootPath { get; set; } = string.Empty;
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = string.Empty;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
}
