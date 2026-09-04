using Microsoft.EntityFrameworkCore;
using Npgsql;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class IdentityAdministrationService(OaDbContext db, DemoData data, IConfiguration? configuration = null)
{
    private const string TenantId = IdentityDefaults.TenantId;

    public ServiceResult<PagedResponse<ManagedUserView>> List(Employee actor, string? keyword, string? status, string? departmentId, int? page, int? pageSize)
    {
        if (!CanManage(actor)) return ServiceResult<PagedResponse<ManagedUserView>>.Failure("无用户管理权限。", "AUTH_002");
        var query = db.Users.AsNoTracking().Where(item => item.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var value = keyword.Trim();
            query = query.Where(item => item.Id.Contains(value) || item.Name.Contains(value));
        }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(departmentId)) query = query.Where(item => item.DepartmentId == departmentId);
        var normalizedPageSize = Math.Clamp(pageSize ?? 20, 1, 100);
        var total = query.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)normalizedPageSize));
        var normalizedPage = Math.Clamp(page ?? 1, 1, totalPages);
        var items = query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize)
            .ToList().Select(ToView).ToList();
        return ServiceResult<PagedResponse<ManagedUserView>>.Success(new PagedResponse<ManagedUserView>(items, total, normalizedPage, normalizedPageSize, totalPages));
    }

    public ServiceResult<IReadOnlyList<RoleView>> ListRoles(Employee actor)
    {
        if (!CanManage(actor)) return ServiceResult<IReadOnlyList<RoleView>>.Failure("无用户管理权限。", "AUTH_002");
        var permissions = db.RolePermissions.AsNoTracking().ToLookup(item => item.RoleCode);
        var scopes = db.RoleDataScopes.AsNoTracking().ToLookup(item => item.RoleCode);
        var userCounts = db.UserRoles.AsNoTracking().GroupBy(item => item.RoleCode).ToDictionary(group => group.Key, group => group.Count());
        var roles = db.Roles.AsNoTracking().Where(item => item.TenantId == TenantId).OrderByDescending(item => item.IsSystem).ThenBy(item => item.Name).ToList()
            .Select(role => new RoleView(role.Code, role.Name, role.IsSystem, userCounts.GetValueOrDefault(role.Code), permissions[role.Code].Select(item => item.PermissionCode).OrderBy(value => value).ToList(), scopes[role.Code].ToDictionary(item => item.ResourceType, item => item.Scope))).ToList();
        return ServiceResult<IReadOnlyList<RoleView>>.Success(roles);
    }

    public ServiceResult<IReadOnlyList<PermissionView>> ListPermissions(Employee actor) => CanManage(actor)
        ? ServiceResult<IReadOnlyList<PermissionView>>.Success(OaPermissions.Definitions)
        : ServiceResult<IReadOnlyList<PermissionView>>.Failure("无角色权限管理权限。", "AUTH_002");

    public ServiceResult<RoleView> CreateRole(Employee actor, CreateRoleRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<RoleView>.Failure("无角色权限管理权限。", "AUTH_002");
        var code = request.Code?.Trim() ?? string.Empty;
        var requestedPermissions = request.Permissions ?? [];
        var dataScopes = NormalizeDataScopes(request.DataScopes);
        var validation = ValidateRole(code, request.Name, requestedPermissions, dataScopes, true);
        if (!validation.IsSuccess) return ServiceResult<RoleView>.Failure(validation.Error!, validation.Code!);
        var role = new RoleRecord { Code = code, TenantId = TenantId, Name = request.Name.Trim(), IsSystem = false };
        db.Roles.Add(role);
        db.RolePermissions.AddRange(requestedPermissions.Distinct().Select(permission => new RolePermissionRecord { RoleCode = code, PermissionCode = permission }));
        db.RoleDataScopes.AddRange(dataScopes.Select(item => new RoleDataScopeRecord { RoleCode = code, ResourceType = item.Key, Scope = item.Value }));
        db.SaveChanges();
        Audit(actor, "ROLE_CREATED", role.Code, $"创建角色 {role.Name}（{role.Code}）");
        return ServiceResult<RoleView>.Success(ToRoleView(role));
    }

    public ServiceResult<RoleView> UpdateRole(Employee actor, UpdateRoleRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<RoleView>.Failure("无角色权限管理权限。", "AUTH_002");
        var code = request.Code?.Trim() ?? string.Empty;
        var role = db.Roles.SingleOrDefault(item => item.TenantId == TenantId && item.Code == code);
        if (role is null) return ServiceResult<RoleView>.Failure("角色不存在。", "DATA_001");
        var requestedPermissions = request.Permissions ?? [];
        var dataScopes = NormalizeDataScopes(request.DataScopes);
        var validation = ValidateRole(code, request.Name, requestedPermissions, dataScopes, false);
        if (!validation.IsSuccess) return ServiceResult<RoleView>.Failure(validation.Error!, validation.Code!);
        if (!ActorRetainsUserManagement(actor.Id, code, requestedPermissions)) return ServiceResult<RoleView>.Failure("不能移除自己最后一份用户管理权限。", "STATE_001");

        var existingPermissionRecords = db.RolePermissions.Where(item => item.RoleCode == code).ToList();
        var previousPermissions = existingPermissionRecords.Select(item => item.PermissionCode).ToHashSet();
        var nextPermissions = requestedPermissions.Distinct().ToHashSet();
        var permissionsChanged = !previousPermissions.SetEquals(nextPermissions);
        var existingScopeRecords = db.RoleDataScopes.Where(item => item.RoleCode == code).ToList();
        var scopesChanged = OaDataScopes.ResourceTypes.Any(resourceType => (existingScopeRecords.SingleOrDefault(item => item.ResourceType == resourceType)?.Scope ?? OaDataScopes.Self) != dataScopes[resourceType]);
        role.Name = request.Name.Trim();
        db.RolePermissions.RemoveRange(existingPermissionRecords.Where(item => !nextPermissions.Contains(item.PermissionCode)));
        db.RolePermissions.AddRange(nextPermissions.Where(permission => !previousPermissions.Contains(permission)).Select(permission => new RolePermissionRecord { RoleCode = code, PermissionCode = permission }));
        foreach (var scope in existingScopeRecords) scope.Scope = dataScopes[scope.ResourceType];
        db.RoleDataScopes.AddRange(OaDataScopes.ResourceTypes.Where(resourceType => existingScopeRecords.All(item => item.ResourceType != resourceType)).Select(resourceType => new RoleDataScopeRecord { RoleCode = code, ResourceType = resourceType, Scope = dataScopes[resourceType] }));
        db.SaveChanges();
        if (permissionsChanged || scopesChanged)
        {
            var userIds = db.UserRoles.AsNoTracking().Where(item => item.RoleCode == code).Select(item => item.UserId).ToList();
            var now = DateTimeOffset.UtcNow;
            db.AuthSessions.Where(item => userIds.Contains(item.UserId) && item.RevokedAt == null)
                .ExecuteUpdate(setters => setters.SetProperty(item => item.RevokedAt, now).SetProperty(item => item.RevokedReason, permissionsChanged ? "ROLE_PERMISSIONS_CHANGED" : "ROLE_DATA_SCOPE_CHANGED"));
        }
        Audit(actor, "ROLE_UPDATED", role.Code, $"更新角色 {role.Name}（{role.Code}）权限");
        return ServiceResult<RoleView>.Success(ToRoleView(role));
    }

    public ServiceResult<RoleView> DeleteRole(Employee actor, string code)
    {
        if (!CanManage(actor)) return ServiceResult<RoleView>.Failure("无角色权限管理权限。", "AUTH_002");
        var normalized = code?.Trim() ?? string.Empty;
        var role = db.Roles.SingleOrDefault(item => item.TenantId == TenantId && item.Code == normalized);
        if (role is null) return ServiceResult<RoleView>.Failure("角色不存在。", "DATA_001");
        if (role.IsSystem) return ServiceResult<RoleView>.Failure("系统预置角色不可删除。", "STATE_001");
        if (db.UserRoles.Any(item => item.RoleCode == normalized)) return ServiceResult<RoleView>.Failure("角色已分配给用户，请先调整用户角色。", "CONFLICT_001");
        var view = ToRoleView(role);
        db.Roles.Remove(role);
        db.SaveChanges();
        Audit(actor, "ROLE_DELETED", role.Code, $"删除角色 {role.Name}（{role.Code}）");
        return ServiceResult<RoleView>.Success(view);
    }

    public ServiceResult<ManagedUserView> Create(Employee actor, CreateManagedUserRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<ManagedUserView>.Failure("无用户管理权限。", "AUTH_002");
        var id = request.Id.Trim().ToLowerInvariant();
        if (id.Length is < 3 or > 64 || id.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            return ServiceResult<ManagedUserView>.Failure("用户 ID 应为 3–64 位字母、数字、点、横线或下划线。", "VALIDATION_001");
        if (db.Users.Any(item => item.Id == id)) return ServiceResult<ManagedUserView>.Failure("用户 ID 已存在。", "DUPLICATE_001");
        var validation = Validate(request.Name, request.DepartmentId, request.PositionId, request.ManagerId, request.CumulativeWorkYears, "ACTIVE", request.Roles, id);
        if (!validation.IsSuccess) return ServiceResult<ManagedUserView>.Failure(validation.Error!, validation.Code!);
        if (!PasswordHasher.MeetsProductionPolicy(request.Password)) return ServiceResult<ManagedUserView>.Failure("临时密码须为 12–128 位，并包含大小写字母、数字和特殊字符。", "VALIDATION_001");

        var profileResult = ResolveInitialProfile(id, request);
        if (!profileResult.IsSuccess) return ServiceResult<ManagedUserView>.Failure(profileResult.Error!, profileResult.Code!);
        var initial = profileResult.Value!;

        var cumulativeWorkYears = initial.CumulativeWorkStartDate is null
            ? request.CumulativeWorkYears
            : PersonnelProfileProvisioning.CompletedYears(initial.CumulativeWorkStartDate.Value, BusinessTime.ChinaToday());
        var user = new UserRecord { Id = id, TenantId = TenantId, Name = request.Name.Trim(), DepartmentId = request.DepartmentId, PositionId = NullIfEmpty(request.PositionId), ManagerId = NullIfEmpty(request.ManagerId), CumulativeWorkYears = cumulativeWorkYears, Status = "ACTIVE" };
        var roles = request.Roles.Distinct().ToList();
        var password = PasswordHasher.Hash(request.Password);
        var profile = PersonnelProfileProvisioning.Create(user, initial.EmployeeNumber, initial.HireDate, initial.EmploymentType, initial.PersonnelStatus, initial.ProbationEndDate, initial.CumulativeWorkStartDate);
        using var transaction = db.Database.CurrentTransaction is null && db.Database.IsRelational() ? db.Database.BeginTransaction() : null;
        try
        {
            db.Users.Add(user);
            db.UserRoles.AddRange(roles.Select((role, index) => new UserRoleRecord { UserId = id, RoleCode = role, IsPrimary = index == 0 }));
            db.UserAccounts.Add(new UserAccountRecord { UserId = id, PasswordSalt = password.Salt, PasswordHash = password.Hash, PasswordIterations = password.Iterations, MustChangePassword = true });
            db.PersonnelProfiles.Add(profile);
            db.PersonnelEvents.Add(PersonnelProfileProvisioning.CreateInitialEvent(profile, actor.Id, actor.Name, "创建账号时建立人事档案"));
            db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = "USER_CREATED", ResourceType = "User", ResourceId = user.Id, Summary = $"创建用户 {user.Name}（{user.Id}）并建立人事档案 {profile.EmployeeNumber}" });
            db.SaveChanges();
            transaction?.Commit();
        }
        catch (DbUpdateException exception) when (IsUserOrEmployeeNumberConflict(exception))
        {
            transaction?.Rollback();
            db.ChangeTracker.Clear();
            return ServiceResult<ManagedUserView>.Failure(
                IsEmployeeNumberConflict(exception) ? "员工工号已存在。" : "用户 ID 已存在。",
                IsEmployeeNumberConflict(exception) ? "PERSONNEL_002" : "DUPLICATE_001");
        }
        catch
        {
            transaction?.Rollback();
            db.ChangeTracker.Clear();
            throw;
        }
        return ServiceResult<ManagedUserView>.Success(ToView(user));
    }

    private ServiceResult<InitialPersonnelProfile> ResolveInitialProfile(string userId, CreateManagedUserRequest request)
    {
        var allowDemoDefaults = configuration?.GetValue("DemoFeatures:AllowDataGeneration", true) ?? true;
        var employeeNumber = request.EmployeeNumber?.Trim().ToUpperInvariant() ?? string.Empty;
        var hireDate = request.HireDate;
        var employmentType = request.EmploymentType?.Trim().ToUpperInvariant() ?? string.Empty;
        var personnelStatus = request.PersonnelStatus?.Trim().ToUpperInvariant() ?? string.Empty;
        var cumulativeWorkStartDate = request.CumulativeWorkStartDate;
        if (allowDemoDefaults)
        {
            employeeNumber = string.IsNullOrWhiteSpace(employeeNumber) ? PersonnelProfileProvisioning.GenerateDemoEmployeeNumber(userId) : employeeNumber;
            hireDate ??= BusinessTime.ChinaToday().AddYears(-Math.Min(request.CumulativeWorkYears, 20));
            employmentType = string.IsNullOrWhiteSpace(employmentType) ? EmploymentTypes.FullTime : employmentType;
            personnelStatus = string.IsNullOrWhiteSpace(personnelStatus) ? PersonnelStatuses.Active : personnelStatus;
            cumulativeWorkStartDate ??= BusinessTime.ChinaToday().AddYears(-request.CumulativeWorkYears);
        }
        else if (string.IsNullOrWhiteSpace(employeeNumber) || hireDate is null || string.IsNullOrWhiteSpace(employmentType) || string.IsNullOrWhiteSpace(personnelStatus))
        {
            return ServiceResult<InitialPersonnelProfile>.Failure("生产环境创建用户必须填写工号、入职日期、用工类型和人事状态。", "PERSONNEL_001");
        }

        var today = BusinessTime.ChinaToday();
        if (employeeNumber.Length is < 2 or > 32 || employeeNumber.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
            return ServiceResult<InitialPersonnelProfile>.Failure("工号应为 2–32 位字母、数字或横线。", "PERSONNEL_001");
        if (db.PersonnelProfiles.Any(item => item.TenantId == TenantId && item.EmployeeNumber == employeeNumber))
            return ServiceResult<InitialPersonnelProfile>.Failure("员工工号已存在。", "PERSONNEL_002");
        if (hireDate > today.AddDays(90))
            return ServiceResult<InitialPersonnelProfile>.Failure("入职日期不能晚于当前日期后 90 天。", "PERSONNEL_003");
        if (!EmploymentTypes.All.Contains(employmentType))
            return ServiceResult<InitialPersonnelProfile>.Failure("用工类型不合法。", "PERSONNEL_001");
        if (personnelStatus is not (PersonnelStatuses.Active or PersonnelStatuses.Probation))
            return ServiceResult<InitialPersonnelProfile>.Failure("新建账号的人事状态仅支持在职或试用。", "PERSONNEL_003");
        if (personnelStatus == PersonnelStatuses.Probation && request.ProbationEndDate is null)
            return ServiceResult<InitialPersonnelProfile>.Failure("试用员工必须填写试用期结束日期。", "PERSONNEL_003");
        if (request.ProbationEndDate < hireDate)
            return ServiceResult<InitialPersonnelProfile>.Failure("试用期结束日期不能早于入职日期。", "PERSONNEL_003");
        if (cumulativeWorkStartDate > today)
            return ServiceResult<InitialPersonnelProfile>.Failure("累计工作起始日期不能晚于当前日期。", "PERSONNEL_003");

        return ServiceResult<InitialPersonnelProfile>.Success(new InitialPersonnelProfile(
            employeeNumber, hireDate!.Value, employmentType, personnelStatus, request.ProbationEndDate, cumulativeWorkStartDate));
    }

    private static bool IsUserOrEmployeeNumberConflict(DbUpdateException exception) => exception.InnerException is PostgresException
    {
        SqlState: PostgresErrorCodes.UniqueViolation,
        ConstraintName: "PK_oa_user" or "IX_personnel_profile_TenantId_EmployeeNumber"
    };

    private static bool IsEmployeeNumberConflict(DbUpdateException exception) => exception.InnerException is PostgresException
    {
        ConstraintName: "IX_personnel_profile_TenantId_EmployeeNumber"
    };

    private sealed record InitialPersonnelProfile(
        string EmployeeNumber,
        DateOnly HireDate,
        string EmploymentType,
        string PersonnelStatus,
        DateOnly? ProbationEndDate,
        DateOnly? CumulativeWorkStartDate);

    public ServiceResult<ManagedUserView> Update(Employee actor, string id, UpdateManagedUserRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<ManagedUserView>.Failure("无用户管理权限。", "AUTH_002");
        var user = db.Users.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (user is null) return ServiceResult<ManagedUserView>.Failure("用户不存在。", "DATA_001");
        if (user.Version != request.Version) return ServiceResult<ManagedUserView>.Failure("用户已被其他操作更新，请刷新后重试。", "CONFLICT_001");
        var validation = Validate(request.Name, request.DepartmentId, request.PositionId, request.ManagerId, request.CumulativeWorkYears, request.Status, request.Roles, id);
        if (!validation.IsSuccess) return ServiceResult<ManagedUserView>.Failure(validation.Error!, validation.Code!);
        var permissions = PermissionsFor(request.Roles);
        if (actor.Id == id && (request.Status != "ACTIVE" || !permissions.Contains(OaPermissions.UserManage)))
            return ServiceResult<ManagedUserView>.Failure("不能停用自己或移除自己的用户管理权限。", "STATE_001");

        var previousStatus = user.Status;
        var previousDepartmentId = user.DepartmentId;
        var previousPositionId = user.PositionId;
        var previousManagerId = user.ManagerId;
        var previousRoles = db.UserRoles.AsNoTracking().Where(item => item.UserId == id).Select(item => item.RoleCode).ToHashSet();
        var nextRoles = request.Roles.Distinct().ToHashSet();

        user.Name = request.Name.Trim();
        user.DepartmentId = request.DepartmentId;
        user.PositionId = NullIfEmpty(request.PositionId);
        user.ManagerId = NullIfEmpty(request.ManagerId);
        user.CumulativeWorkYears = request.CumulativeWorkYears;
        user.Status = request.Status;
        user.Version++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var existingRoleRecords = db.UserRoles.Where(item => item.UserId == id).ToList();
        var orderedRoles = request.Roles.Distinct().ToList();
        db.UserRoles.RemoveRange(existingRoleRecords.Where(item => !orderedRoles.Contains(item.RoleCode)));
        foreach (var existingRole in existingRoleRecords.Where(item => orderedRoles.Contains(item.RoleCode))) existingRole.IsPrimary = orderedRoles.IndexOf(existingRole.RoleCode) == 0;
        db.UserRoles.AddRange(orderedRoles.Where(role => existingRoleRecords.All(item => item.RoleCode != role)).Select((role, index) => new UserRoleRecord { UserId = id, RoleCode = role, IsPrimary = orderedRoles.IndexOf(role) == 0 }));
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ServiceResult<ManagedUserView>.Failure("用户已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        }
        var organizationChanged = previousDepartmentId != request.DepartmentId || previousPositionId != NullIfEmpty(request.PositionId) || previousManagerId != NullIfEmpty(request.ManagerId);
        if (previousStatus != request.Status || !previousRoles.SetEquals(nextRoles) || organizationChanged)
        {
            var reason = previousStatus != request.Status ? "USER_STATUS_CHANGED" : !previousRoles.SetEquals(nextRoles) ? "USER_ROLES_CHANGED" : "USER_ORGANIZATION_CHANGED";
            AuthenticationService.RevokeAllSessions(db, id, reason);
        }
        Audit(actor, "USER_UPDATED", user.Id, $"更新用户 {user.Name}（{user.Id}）资料、组织或角色");
        return ServiceResult<ManagedUserView>.Success(ToView(user));
    }

    public ServiceResult<ManagedUserView> ResetPassword(Employee actor, string id, ResetUserPasswordRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<ManagedUserView>.Failure("无用户管理权限。", "AUTH_002");
        if (!PasswordHasher.MeetsProductionPolicy(request.Password)) return ServiceResult<ManagedUserView>.Failure("临时密码须为 12–128 位，并包含大小写字母、数字和特殊字符。", "VALIDATION_001");
        var user = db.Users.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        var account = db.UserAccounts.SingleOrDefault(item => item.UserId == id);
        if (user is null || account is null) return ServiceResult<ManagedUserView>.Failure("用户不存在。", "DATA_001");
        var password = PasswordHasher.Hash(request.Password);
        account.PasswordSalt = password.Salt;
        account.PasswordHash = password.Hash;
        account.PasswordIterations = password.Iterations;
        account.PasswordChangedAt = DateTimeOffset.UtcNow;
        account.MustChangePassword = true;
        account.FailedLoginCount = 0;
        account.LockedUntil = null;
        db.MfaChallenges.Where(item => item.UserId == id).ExecuteDelete();
        db.SaveChanges();
        AuthenticationService.RevokeAllSessions(db, id, "PASSWORD_RESET");
        Audit(actor, "USER_PASSWORD_RESET", user.Id, $"重置用户 {user.Name}（{user.Id}）密码");
        return ServiceResult<ManagedUserView>.Success(ToView(user));
    }

    public ServiceResult<ManagedUserView> ResetMfa(Employee actor, string id)
    {
        if (!CanManage(actor)) return ServiceResult<ManagedUserView>.Failure("无用户管理权限。", "AUTH_002");
        if (actor.Id == id) return ServiceResult<ManagedUserView>.Failure("不能在当前会话中重置自己的多因素认证，请由另一名系统管理员处理。", "STATE_001");
        var user = db.Users.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        var account = db.UserAccounts.SingleOrDefault(item => item.UserId == id);
        if (user is null || account is null) return ServiceResult<ManagedUserView>.Failure("用户不存在。", "DATA_001");
        account.MfaEnabled = false;
        account.MfaSecretCiphertext = null;
        account.RecoveryCodeHashesJson = "[]";
        account.LastTotpTimeStep = null;
        account.MfaEnabledAt = null;
        account.MfaUpdatedAt = DateTimeOffset.UtcNow;
        db.MfaChallenges.Where(item => item.UserId == id).ExecuteDelete();
        db.SaveChanges();
        AuthenticationService.RevokeAllSessions(db, id, "MFA_RESET_BY_ADMIN");
        Audit(actor, "USER_MFA_RESET", user.Id, $"重置用户 {user.Name}（{user.Id}）多因素认证；敏感权限账号下次登录必须重新绑定");
        return ServiceResult<ManagedUserView>.Success(ToView(user));
    }

    private ServiceResult<bool> Validate(string name, string departmentId, string? positionId, string? managerId, int cumulativeWorkYears, string status, IReadOnlyList<string> roles, string userId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 64) return ServiceResult<bool>.Failure("姓名应为 1–64 个字符。", "VALIDATION_001");
        if (!db.Departments.Any(item => item.TenantId == TenantId && item.Id == departmentId)) return ServiceResult<bool>.Failure("所属部门不存在。", "VALIDATION_001");
        var position = NullIfEmpty(positionId);
        if (position is not null && !db.Positions.Any(item => item.TenantId == TenantId && item.Id == position && item.DepartmentId == departmentId && item.Status == "ACTIVE")) return ServiceResult<bool>.Failure("岗位不存在、已停用或不属于所选部门。", "VALIDATION_001");
        if (cumulativeWorkYears is < 0 or > 60) return ServiceResult<bool>.Failure("社会工龄应为 0–60 年。", "VALIDATION_001");
        if (status is not ("ACTIVE" or "DISABLED")) return ServiceResult<bool>.Failure("用户状态仅支持 ACTIVE 或 DISABLED。", "VALIDATION_001");
        var manager = NullIfEmpty(managerId);
        if (manager == userId) return ServiceResult<bool>.Failure("用户不能作为自己的直属上级。", "VALIDATION_001");
        if (manager is not null && !db.Users.Any(item => item.TenantId == TenantId && item.Id == manager && item.Status == "ACTIVE")) return ServiceResult<bool>.Failure("直属上级不存在或已停用。", "VALIDATION_001");
        if (manager is not null && WouldCreateManagerCycle(userId, manager)) return ServiceResult<bool>.Failure("直属上级关系不能形成循环。", "VALIDATION_001");
        var assignedRoles = roles.Distinct().ToList();
        if (assignedRoles.Count == 0 || assignedRoles.Any(role => !db.Roles.Any(item => item.TenantId == TenantId && item.Code == role))) return ServiceResult<bool>.Failure("至少选择一个有效角色。", "VALIDATION_001");
        return ServiceResult<bool>.Success(true);
    }

    private ManagedUserView ToView(UserRecord user)
    {
        var roles = db.UserRoles.AsNoTracking().Where(item => item.UserId == user.Id).OrderByDescending(item => item.IsPrimary).ThenBy(item => item.RoleCode).Select(item => item.RoleCode).ToList();
        var department = db.Departments.AsNoTracking().Single(item => item.Id == user.DepartmentId);
        var managerName = user.ManagerId is null ? null : db.Users.AsNoTracking().Where(item => item.Id == user.ManagerId).Select(item => item.Name).SingleOrDefault();
        var positionName = user.PositionId is null ? null : db.Positions.AsNoTracking().Where(item => item.Id == user.PositionId).Select(item => item.Name).SingleOrDefault();
        var authenticationState = db.UserAccounts.AsNoTracking().Where(item => item.UserId == user.Id)
            .Select(item => new { item.MfaEnabled, item.MustChangePassword }).SingleOrDefault();
        return new ManagedUserView(user.Id, user.Name, user.DepartmentId, department.Name, user.ManagerId, managerName, user.CumulativeWorkYears, user.Status, user.Version, roles, PermissionsFor(roles), user.CreatedAt, user.UpdatedAt, user.PositionId, positionName, authenticationState?.MfaEnabled ?? false, authenticationState?.MustChangePassword ?? false);
    }

    private IReadOnlyList<string> PermissionsFor(IEnumerable<string> roles)
    {
        var roleList = roles.Distinct().ToList();
        return db.RolePermissions.AsNoTracking().Where(item => roleList.Contains(item.RoleCode)).Select(item => item.PermissionCode).Distinct().OrderBy(value => value).ToList();
    }

    private ServiceResult<bool> ValidateRole(string code, string name, IReadOnlyList<string> permissions, IReadOnlyDictionary<string, string> dataScopes, bool creating)
    {
        if (creating && (code.Length is < 2 or > 64 || code.Any(character => !char.IsLetterOrDigit(character) && character is not ('_' or '-' or '.' or '/'))))
            return ServiceResult<bool>.Failure("角色编码应为 2–64 位字母、数字、中文、点、横线、斜线或下划线。", "VALIDATION_001");
        if (creating && db.Roles.Any(item => item.Code == code)) return ServiceResult<bool>.Failure("角色编码已存在。", "DUPLICATE_001");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 64) return ServiceResult<bool>.Failure("角色名称应为 1–64 个字符。", "VALIDATION_001");
        if (db.Roles.Any(item => item.TenantId == TenantId && item.Code != code && item.Name == name.Trim())) return ServiceResult<bool>.Failure("角色名称已存在。", "DUPLICATE_001");
        var assigned = permissions.Distinct().ToList();
        if (assigned.Any(permission => !OaPermissions.All.Contains(permission))) return ServiceResult<bool>.Failure("包含系统不支持的权限编码。", "VALIDATION_001");
        if (dataScopes.Any(item => !OaDataScopes.ResourceTypes.Contains(item.Key) || !OaDataScopes.All.Contains(item.Value))) return ServiceResult<bool>.Failure("包含系统不支持的数据范围。", "VALIDATION_001");
        if (dataScopes["Leave"] != OaDataScopes.Self && !assigned.Contains(OaPermissions.LeaveScopeView)) return ServiceResult<bool>.Failure("配置请假跨用户数据范围时必须授予请假范围查看权限。", "VALIDATION_001");
        if (dataScopes["Expense"] != OaDataScopes.Self && !assigned.Contains(OaPermissions.ExpenseScopeView) && !assigned.Contains(OaPermissions.ExpenseAllView)) return ServiceResult<bool>.Failure("配置报销跨用户数据范围时必须授予报销范围查看权限。", "VALIDATION_001");
        if (dataScopes["Travel"] != OaDataScopes.Self && !assigned.Contains(OaPermissions.TravelScopeView)) return ServiceResult<bool>.Failure("配置出差跨用户数据范围时必须授予出差范围查看权限。", "VALIDATION_001");
        if (dataScopes["Personnel"] != OaDataScopes.Self && !assigned.Contains(OaPermissions.PersonnelScopeView)) return ServiceResult<bool>.Failure("配置人事档案跨用户数据范围时必须授予人事档案范围查看权限。", "VALIDATION_001");
        if (assigned.Contains(OaPermissions.PersonnelExport) && !assigned.Contains(OaPermissions.PersonnelScopeView)) return ServiceResult<bool>.Failure("授予员工花名册导出权限时必须同时授予人事档案范围查看权限。", "VALIDATION_001");
        if (assigned.Contains(OaPermissions.PersonnelManage) && !assigned.Contains(OaPermissions.PersonnelScopeView)) return ServiceResult<bool>.Failure("授予人事档案维护权限时必须同时授予人事档案范围查看权限。", "VALIDATION_001");
        if (dataScopes["Attendance"] != OaDataScopes.Self && !assigned.Contains(OaPermissions.AttendanceScopeView)) return ServiceResult<bool>.Failure("配置考勤跨用户数据范围时必须授予考勤范围查看权限。", "VALIDATION_001");
        if (assigned.Contains(OaPermissions.AttendanceManage) && !assigned.Contains(OaPermissions.AttendanceScopeView)) return ServiceResult<bool>.Failure("授予考勤维护权限时必须同时授予考勤范围查看权限。", "VALIDATION_001");
        if (dataScopes["Contract"] != OaDataScopes.Self && !assigned.Contains(OaPermissions.ContractScopeView)) return ServiceResult<bool>.Failure("配置劳动合同跨用户数据范围时必须授予劳动合同范围查看权限。", "VALIDATION_001");
        if (assigned.Contains(OaPermissions.ContractManage) && !assigned.Contains(OaPermissions.ContractScopeView)) return ServiceResult<bool>.Failure("授予劳动合同维护权限时必须同时授予劳动合同范围查看权限。", "VALIDATION_001");
        if (assigned.Contains(OaPermissions.PurchaseManage) && !assigned.Contains(OaPermissions.PurchaseScopeView)) return ServiceResult<bool>.Failure("授予采购执行权限时必须同时授予采购范围查看权限。", "VALIDATION_001");
        if (dataScopes.TryGetValue("Seal", out var sealScope) && sealScope != OaDataScopes.Self && !assigned.Contains(OaPermissions.SealScopeView)) return ServiceResult<bool>.Failure("配置用章跨用户数据范围时必须授予用章范围查看权限。", "VALIDATION_001");
        if (assigned.Contains(OaPermissions.SealManage) && !assigned.Contains(OaPermissions.SealScopeView)) return ServiceResult<bool>.Failure("授予印章管理权限时必须同时授予用章范围查看权限。", "VALIDATION_001");
        if (dataScopes.TryGetValue("Document", out var docScope) && docScope != OaDataScopes.Self && !assigned.Contains(OaPermissions.DocumentScopeView) && !assigned.Contains(OaPermissions.DocumentManage)) return ServiceResult<bool>.Failure("配置知识库跨用户数据范围时必须授予知识库查看或管理权限。", "VALIDATION_001");
        if (assigned.Contains(OaPermissions.DocumentDeptManage) && !assigned.Contains(OaPermissions.DocumentScopeView) && !assigned.Contains(OaPermissions.DocumentManage)) return ServiceResult<bool>.Failure("授予部门文档管理权限时必须同时授予知识库范围查看权限。", "VALIDATION_001");
        return ServiceResult<bool>.Success(true);
    }

    private bool ActorRetainsUserManagement(string actorId, string editedRoleCode, IReadOnlyList<string> nextPermissions)
    {
        var roleCodes = db.UserRoles.AsNoTracking().Where(item => item.UserId == actorId).Select(item => item.RoleCode).ToList();
        if (!roleCodes.Contains(editedRoleCode)) return true;
        if (nextPermissions.Contains(OaPermissions.UserManage)) return true;
        return db.RolePermissions.AsNoTracking().Any(item => roleCodes.Contains(item.RoleCode) && item.RoleCode != editedRoleCode && item.PermissionCode == OaPermissions.UserManage);
    }

    private RoleView ToRoleView(RoleRecord role)
    {
        var permissions = db.RolePermissions.AsNoTracking().Where(item => item.RoleCode == role.Code).Select(item => item.PermissionCode).OrderBy(value => value).ToList();
        var userCount = db.UserRoles.AsNoTracking().Count(item => item.RoleCode == role.Code);
        var scopes = db.RoleDataScopes.AsNoTracking().Where(item => item.RoleCode == role.Code).ToDictionary(item => item.ResourceType, item => item.Scope);
        foreach (var resourceType in OaDataScopes.ResourceTypes) scopes.TryAdd(resourceType, OaDataScopes.Self);
        return new RoleView(role.Code, role.Name, role.IsSystem, userCount, permissions, scopes);
    }

    private static IReadOnlyDictionary<string, string> NormalizeDataScopes(IReadOnlyDictionary<string, string>? scopes) => OaDataScopes.ResourceTypes.ToDictionary(
        resourceType => resourceType,
        resourceType => scopes?.GetValueOrDefault(resourceType)?.Trim().ToUpperInvariant() ?? OaDataScopes.Self);

    private bool CanManage(Employee actor) => data.HasPermission(actor, OaPermissions.UserManage);
    private bool WouldCreateManagerCycle(string userId, string managerId)
    {
        string? cursor = managerId;
        var visited = new HashSet<string>();
        while (cursor is not null && visited.Add(cursor))
        {
            if (cursor == userId) return true;
            cursor = db.Users.AsNoTracking().Where(item => item.Id == cursor).Select(item => item.ManagerId).SingleOrDefault();
        }
        return false;
    }
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void Audit(Employee actor, string action, string resourceId, string summary)
    {
        db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "User", ResourceId = resourceId, Summary = summary });
        db.SaveChanges();
    }
}
