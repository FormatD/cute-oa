using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed partial class PersonnelService(OaDbContext db, DemoData data, NotificationService? notifications = null, IConfiguration? configuration = null)
{
    private const string TenantId = IdentityDefaults.TenantId;
    private const int DefaultMaxExportRows = 10_000;

    public ServiceResult<PagedResponse<PersonnelProfileView>> List(Employee actor, string? keyword, string? departmentId, string? personnelStatus, string? employmentType, int? page, int? pageSize)
    {
        var visibleUserIds = data.Employees.Where(subject => CanView(actor, subject)).Select(subject => subject.Id).ToList();
        var size = Math.Clamp(pageSize ?? 20, 1, 100);
        if (visibleUserIds.Count == 0)
            return ServiceResult<PagedResponse<PersonnelProfileView>>.Success(new PagedResponse<PersonnelProfileView>([], 0, 1, size, 1));

        var query = from profile in db.PersonnelProfiles.AsNoTracking()
                    join user in db.Users.AsNoTracking() on profile.UserId equals user.Id
                    join department in db.Departments.AsNoTracking() on user.DepartmentId equals department.Id
                    join position in db.Positions.AsNoTracking() on user.PositionId equals position.Id into positionGroup
                    from position in positionGroup.DefaultIfEmpty()
                    join manager in db.Users.AsNoTracking() on user.ManagerId equals manager.Id into managerGroup
                    from manager in managerGroup.DefaultIfEmpty()
                    where profile.TenantId == TenantId && user.TenantId == TenantId && visibleUserIds.Contains(user.Id)
                    select new { Profile = profile, User = user, DepartmentName = department.Name, PositionName = position == null ? null : position.Name, ManagerName = manager == null ? null : manager.Name };

        var normalizedDepartment = departmentId?.Trim();
        var normalizedStatus = personnelStatus?.Trim().ToUpperInvariant();
        var normalizedEmploymentType = employmentType?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedDepartment)) query = query.Where(item => item.User.DepartmentId == normalizedDepartment);
        if (!string.IsNullOrWhiteSpace(normalizedStatus)) query = query.Where(item => item.Profile.PersonnelStatus == normalizedStatus);
        if (!string.IsNullOrWhiteSpace(normalizedEmploymentType)) query = query.Where(item => item.Profile.EmploymentType == normalizedEmploymentType);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{EscapeLikePattern(keyword.Trim())}%";
            query = query.Where(item =>
                EF.Functions.ILike(item.Profile.EmployeeNumber, pattern, "\\") ||
                EF.Functions.ILike(item.User.Id, pattern, "\\") ||
                EF.Functions.ILike(item.User.Name, pattern, "\\") ||
                EF.Functions.ILike(item.DepartmentName, pattern, "\\") ||
                item.PositionName != null && EF.Functions.ILike(item.PositionName, pattern, "\\"));
        }

        var total = query.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)size));
        var currentPage = Math.Clamp(page ?? 1, 1, totalPages);
        var rows = query.OrderByDescending(item => item.Profile.HireDate).ThenBy(item => item.Profile.EmployeeNumber)
            .Skip((currentPage - 1) * size).Take(size).ToList();
        var items = rows.Select(item => ToView(item.Profile, item.User, item.DepartmentName, item.PositionName, item.ManagerName, [])).ToList();
        return ServiceResult<PagedResponse<PersonnelProfileView>>.Success(new PagedResponse<PersonnelProfileView>(items, total, currentPage, size, totalPages));
    }

    public ServiceResult<PersonnelRosterExport> Export(Employee actor, string? keyword, string? departmentId, string? personnelStatus, string? employmentType)
    {
        if (!data.HasPermission(actor, OaPermissions.PersonnelExport))
            return ServiceResult<PersonnelRosterExport>.Failure("无员工花名册导出权限。", "AUTH_002");

        var visibleUserIds = data.Employees.Where(subject => CanView(actor, subject)).Select(subject => subject.Id).ToList();
        var query = from profile in db.PersonnelProfiles.AsNoTracking()
                    join user in db.Users.AsNoTracking() on profile.UserId equals user.Id
                    join department in db.Departments.AsNoTracking() on user.DepartmentId equals department.Id
                    join position in db.Positions.AsNoTracking() on user.PositionId equals position.Id into positionGroup
                    from position in positionGroup.DefaultIfEmpty()
                    join manager in db.Users.AsNoTracking() on user.ManagerId equals manager.Id into managerGroup
                    from manager in managerGroup.DefaultIfEmpty()
                    where profile.TenantId == TenantId && user.TenantId == TenantId && visibleUserIds.Contains(user.Id)
                    select new { Profile = profile, User = user, DepartmentName = department.Name, PositionName = position == null ? null : position.Name, ManagerName = manager == null ? null : manager.Name };

        var normalizedDepartment = departmentId?.Trim();
        var normalizedStatus = personnelStatus?.Trim().ToUpperInvariant();
        var normalizedEmploymentType = employmentType?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedDepartment)) query = query.Where(item => item.User.DepartmentId == normalizedDepartment);
        if (!string.IsNullOrWhiteSpace(normalizedStatus)) query = query.Where(item => item.Profile.PersonnelStatus == normalizedStatus);
        if (!string.IsNullOrWhiteSpace(normalizedEmploymentType)) query = query.Where(item => item.Profile.EmploymentType == normalizedEmploymentType);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{EscapeLikePattern(keyword.Trim())}%";
            query = query.Where(item =>
                EF.Functions.ILike(item.Profile.EmployeeNumber, pattern, "\\") ||
                EF.Functions.ILike(item.User.Id, pattern, "\\") ||
                EF.Functions.ILike(item.User.Name, pattern, "\\") ||
                EF.Functions.ILike(item.DepartmentName, pattern, "\\") ||
                item.PositionName != null && EF.Functions.ILike(item.PositionName, pattern, "\\"));
        }

        var configuredMaximum = configuration?.GetValue<int?>("PersonnelExport:MaxRows") ?? DefaultMaxExportRows;
        var maximum = Math.Clamp(configuredMaximum, 1, DefaultMaxExportRows);
        var rows = query.OrderByDescending(item => item.Profile.HireDate).ThenBy(item => item.Profile.EmployeeNumber).Take(maximum + 1).ToList();
        if (rows.Count > maximum)
            return ServiceResult<PersonnelRosterExport>.Failure($"当前筛选结果超过 {maximum:N0} 行，请缩小筛选范围后重试。", "PERSONNEL_EXPORT_001");

        var csv = new StringBuilder();
        SafeCsv.AppendRow(csv, ["工号", "姓名", "用户 ID", "部门", "岗位", "直属上级", "工作邮箱", "工作电话", "办公地点", "入职日期", "试用期结束日期", "转正日期", "累计工作起始日期", "社会工龄（年）", "用工类型", "人事状态", "账号状态", "离职日期", "离职原因"]);
        foreach (var item in rows)
        {
            SafeCsv.AppendRow(csv,
            [
                item.Profile.EmployeeNumber, item.User.Name, item.User.Id, item.DepartmentName, item.PositionName, item.ManagerName,
                item.Profile.WorkEmail, item.Profile.WorkPhone, item.Profile.WorkLocation, FormatDate(item.Profile.HireDate),
                FormatDate(item.Profile.ProbationEndDate), FormatDate(item.Profile.RegularizedDate), FormatDate(item.Profile.CumulativeWorkStartDate),
                item.User.CumulativeWorkYears.ToString(), item.Profile.EmploymentType, item.Profile.PersonnelStatus, item.User.Status,
                FormatDate(item.Profile.DepartureDate), item.Profile.DepartureReason
            ]);
        }

        db.AuditLogs.Add(new AuditRecord
        {
            TenantId = TenantId,
            ActorId = actor.Id,
            Action = "PERSONNEL_ROSTER_EXPORTED",
            ResourceType = "Personnel",
            ResourceId = "roster",
            Summary = $"导出员工花名册 {rows.Count} 行；关键词筛选={(string.IsNullOrWhiteSpace(keyword) ? "否" : "是")}；部门={normalizedDepartment ?? "全部"}；人事状态={normalizedStatus ?? "全部"}；用工类型={normalizedEmploymentType ?? "全部"}"
        });
        db.SaveChanges();

        var content = SafeCsv.ToUtf8Bom(csv.ToString());
        var exportedAt = DateTimeOffset.UtcNow.ToOffset(BusinessTime.ChinaOffset);
        return ServiceResult<PersonnelRosterExport>.Success(new PersonnelRosterExport(content, $"employee-roster-{exportedAt:yyyyMMdd-HHmmss}.csv", rows.Count));
    }

    public ServiceResult<PersonnelProfileView> Get(Employee actor, string userId)
    {
        var user = db.Users.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == userId);
        var subject = data.FindEmployee(userId);
        if (user is null || subject is null) return ServiceResult<PersonnelProfileView>.Failure("员工档案不存在。", "DATA_001");
        if (!CanView(actor, subject)) return ServiceResult<PersonnelProfileView>.Failure("无权查看该员工档案。", "AUTH_002");
        var profile = db.PersonnelProfiles.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.UserId == userId);
        if (profile is null) return ServiceResult<PersonnelProfileView>.Failure("员工账号尚未建立人事档案，请由 HR 完成建档。", "PERSONNEL_004");
        return ServiceResult<PersonnelProfileView>.Success(ToView(profile, user, true));
    }

    public ServiceResult<PersonnelProfileView> Update(Employee actor, string userId, UpdatePersonnelProfileRequest request)
    {
        if (!data.HasPermission(actor, OaPermissions.PersonnelManage)) return ServiceResult<PersonnelProfileView>.Failure("无人事档案维护权限。", "AUTH_002");
        var user = db.Users.SingleOrDefault(item => item.TenantId == TenantId && item.Id == userId);
        var subject = data.FindEmployee(userId);
        var profile = db.PersonnelProfiles.SingleOrDefault(item => item.TenantId == TenantId && item.UserId == userId);
        if (user is null || subject is null || profile is null) return ServiceResult<PersonnelProfileView>.Failure("员工档案不存在。", "DATA_001");
        if (!CanView(actor, subject)) return ServiceResult<PersonnelProfileView>.Failure("无权维护该员工档案。", "AUTH_002");
        if (profile.Version != request.Version) return ServiceResult<PersonnelProfileView>.Failure("档案已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        if (actor.Id == userId && request.PersonnelStatus == PersonnelStatuses.Terminated) return ServiceResult<PersonnelProfileView>.Failure("不能将自己办结离职。", "STATE_001");
        var validation = Validate(userId, request);
        if (!validation.IsSuccess) return ServiceResult<PersonnelProfileView>.Failure(validation.Error!, validation.Code!);

        var normalizedPosition = NullIfEmpty(request.PositionId);
        var normalizedManager = NullIfEmpty(request.ManagerId);
        var previousStatus = profile.PersonnelStatus;
        var organizationChanged = user.DepartmentId != request.DepartmentId || user.PositionId != normalizedPosition || user.ManagerId != normalizedManager;
        var accountStatus = request.PersonnelStatus == PersonnelStatuses.Terminated ? "DISABLED" : "ACTIVE";
        var accountStatusChanged = user.Status != accountStatus;
        var eventType = ResolveEventType(previousStatus, request.PersonnelStatus, organizationChanged);
        var caseService = new PersonnelCaseService(db, data, notifications);
        if (request.PersonnelStatus == PersonnelStatuses.Terminated && previousStatus != PersonnelStatuses.Offboarding)
            return ServiceResult<PersonnelProfileView>.Failure("必须先进入离职办理中并完成离职清单，才能办结离职。", "PERSONNEL_CASE_003");
        if (request.PersonnelStatus == PersonnelStatuses.Terminated && !caseService.HasCompletedOffboarding(userId, request.DepartureDate!.Value))
            return ServiceResult<PersonnelProfileView>.Failure("离职办理清单尚未全部完成，不能停用员工账号。", "PERSONNEL_CASE_003");
        if (request.PersonnelStatus == PersonnelStatuses.Terminated && user.Status == "ACTIVE" &&
            db.FlowTaskSlas.Any(item => item.TenantId == TenantId && item.AssigneeId == userId && item.ActivatedAt != null && item.CompletedAt == null && item.CancelledAt == null))
            return ServiceResult<PersonnelProfileView>.Failure("该员工尚有进行中的审批待办，请先转办后再办结离职。", "CONFLICT_001");

        using var transaction = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        try
        {
            user.DepartmentId = request.DepartmentId;
            user.PositionId = normalizedPosition;
            user.ManagerId = normalizedManager;
            user.CumulativeWorkYears = request.CumulativeWorkStartDate is null ? user.CumulativeWorkYears : PersonnelProfileProvisioning.CompletedYears(request.CumulativeWorkStartDate.Value, BusinessTime.ChinaToday());
            user.Status = accountStatus;
            user.Version++;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            profile.WorkEmail = Normalize(request.WorkEmail, 128);
            profile.WorkPhone = Normalize(request.WorkPhone, 32);
            profile.WorkLocation = Normalize(request.WorkLocation, 100);
            profile.EmploymentType = request.EmploymentType.Trim().ToUpperInvariant();
            profile.PersonnelStatus = request.PersonnelStatus.Trim().ToUpperInvariant();
            profile.HireDate = request.HireDate;
            profile.ProbationEndDate = request.ProbationEndDate;
            profile.RegularizedDate = request.RegularizedDate;
            profile.CumulativeWorkStartDate = request.CumulativeWorkStartDate;
            profile.DepartureDate = request.DepartureDate;
            profile.DepartureReason = NullIfEmpty(request.DepartureReason);
            profile.Version++;
            profile.UpdatedAt = DateTimeOffset.UtcNow;

            var summary = request.ChangeReason.Trim();
            db.PersonnelEvents.Add(new PersonnelEventRecord
            {
                TenantId = TenantId,
                UserId = userId,
                EventType = eventType,
                EffectiveDate = request.EffectiveDate,
                Summary = summary,
                ChangedBy = actor.Id,
                ChangedByName = actor.Name,
                SnapshotJson = JsonSerializer.Serialize(new { profile.EmployeeNumber, request.DepartmentId, PositionId = normalizedPosition, ManagerId = normalizedManager, profile.EmploymentType, profile.PersonnelStatus, profile.HireDate, profile.RegularizedDate, profile.DepartureDate })
            });
            db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = $"PERSONNEL_{eventType}", ResourceType = "PersonnelProfile", ResourceId = userId, Summary = summary });
            if (actor.Id != userId)
                notifications?.Enqueue(userId, "PERSONNEL_PROFILE_UPDATED", "人事档案已更新", $"{actor.Name} 更新了你的人事档案：{summary}", "PersonnelProfile", userId);
            db.SaveChanges();

            var caseType = eventType switch
            {
                "REGULARIZED" => PersonnelCaseTypes.Regularization,
                "TRANSFER" => PersonnelCaseTypes.Transfer,
                "OFFBOARDING_STARTED" => PersonnelCaseTypes.Offboarding,
                _ => null
            };
            if (caseType is not null)
            {
                var caseEffectiveDate = caseType == PersonnelCaseTypes.Offboarding ? request.DepartureDate!.Value : request.EffectiveDate;
                var caseResult = caseService.EnsureAutomatic(actor, userId, caseType, caseEffectiveDate, summary);
                if (!caseResult.IsSuccess)
                {
                    transaction?.Rollback();
                    db.ChangeTracker.Clear();
                    return ServiceResult<PersonnelProfileView>.Failure(caseResult.Error!, caseResult.Code!);
                }
            }
            transaction?.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            transaction?.Rollback();
            db.ChangeTracker.Clear();
            return ServiceResult<PersonnelProfileView>.Failure("人事档案或员工主数据已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        }
        catch
        {
            transaction?.Rollback();
            db.ChangeTracker.Clear();
            throw;
        }

        if (accountStatusChanged || organizationChanged)
            AuthenticationService.RevokeAllSessions(db, userId, accountStatusChanged ? "PERSONNEL_STATUS_CHANGED" : "PERSONNEL_ORGANIZATION_CHANGED");

        return ServiceResult<PersonnelProfileView>.Success(ToView(profile, user, true));
    }

    private ServiceResult<bool> Validate(string userId, UpdatePersonnelProfileRequest request)
    {
        var status = request.PersonnelStatus.Trim().ToUpperInvariant();
        var employmentType = request.EmploymentType.Trim().ToUpperInvariant();
        var today = BusinessTime.ChinaToday();
        if (!EmploymentTypes.All.Contains(employmentType) || !PersonnelStatuses.All.Contains(status)) return ServiceResult<bool>.Failure("用工类型或人事状态不合法。", "PERSONNEL_001");
        if (request.HireDate > today.AddDays(90)) return ServiceResult<bool>.Failure("入职日期不能晚于当前日期后 90 天。", "PERSONNEL_003");
        if (request.ProbationEndDate < request.HireDate || request.RegularizedDate < request.HireDate || request.DepartureDate < request.HireDate) return ServiceResult<bool>.Failure("试用、转正或离职日期不能早于入职日期。", "PERSONNEL_003");
        if (status == PersonnelStatuses.Probation && request.ProbationEndDate is null) return ServiceResult<bool>.Failure("试用员工必须填写试用期结束日期。", "PERSONNEL_003");
        if (status == PersonnelStatuses.Probation && request.RegularizedDate is not null) return ServiceResult<bool>.Failure("已填写转正日期的员工不能保持试用状态。", "PERSONNEL_003");
        if (status == PersonnelStatuses.Offboarding && (request.DepartureDate is null || string.IsNullOrWhiteSpace(request.DepartureReason))) return ServiceResult<bool>.Failure("进入离职办理必须填写计划离职日期和原因。", "PERSONNEL_003");
        if (status == PersonnelStatuses.Terminated && (request.DepartureDate is null || string.IsNullOrWhiteSpace(request.DepartureReason))) return ServiceResult<bool>.Failure("离职办结必须填写离职日期和原因。", "PERSONNEL_003");
        if (status is PersonnelStatuses.Active or PersonnelStatuses.Probation && request.DepartureDate is not null) return ServiceResult<bool>.Failure("在职或试用员工不能填写离职日期。", "PERSONNEL_003");
        if (request.CumulativeWorkStartDate > today) return ServiceResult<bool>.Failure("累计工作起始日期不能晚于当前日期。", "PERSONNEL_003");
        if (string.IsNullOrWhiteSpace(request.ChangeReason) || request.ChangeReason.Trim().Length > 500) return ServiceResult<bool>.Failure("变更说明应为 1–500 个字符。", "PERSONNEL_001");
        if (request.EffectiveDate < request.HireDate || request.EffectiveDate > today.AddDays(90)) return ServiceResult<bool>.Failure("生效日期超出允许范围。", "PERSONNEL_003");
        var email = request.WorkEmail?.Trim() ?? string.Empty;
        if (email.Length > 128 || email.Length > 0 && !EmailPattern().IsMatch(email)) return ServiceResult<bool>.Failure("工作邮箱格式不正确。", "PERSONNEL_001");
        var phone = request.WorkPhone?.Trim() ?? string.Empty;
        if (phone.Length > 32 || phone.Length > 0 && (phone.Length < 5 || !PhonePattern().IsMatch(phone))) return ServiceResult<bool>.Failure("工作电话格式不正确。", "PERSONNEL_001");
        if ((request.WorkLocation?.Trim().Length ?? 0) > 100) return ServiceResult<bool>.Failure("办公地点不能超过 100 个字符。", "PERSONNEL_001");
        if (!db.Departments.Any(item => item.TenantId == TenantId && item.Id == request.DepartmentId)) return ServiceResult<bool>.Failure("所属部门不存在。", "PERSONNEL_001");
        var position = NullIfEmpty(request.PositionId);
        if (position is not null && !db.Positions.Any(item => item.TenantId == TenantId && item.Id == position && item.DepartmentId == request.DepartmentId && item.Status == "ACTIVE")) return ServiceResult<bool>.Failure("岗位不存在、已停用或不属于所选部门。", "PERSONNEL_001");
        var manager = NullIfEmpty(request.ManagerId);
        if (manager == userId) return ServiceResult<bool>.Failure("员工不能作为自己的直属上级。", "PERSONNEL_001");
        if (manager is not null && !db.Users.Any(item => item.TenantId == TenantId && item.Id == manager && item.Status == "ACTIVE")) return ServiceResult<bool>.Failure("直属上级不存在或已停用。", "PERSONNEL_001");
        if (manager is not null && WouldCreateManagerCycle(userId, manager)) return ServiceResult<bool>.Failure("直属上级关系不能形成循环。", "PERSONNEL_001");
        return ServiceResult<bool>.Success(true);
    }

    private PersonnelProfileView ToView(PersonnelProfileRecord profile, UserRecord user, bool includeEvents)
    {
        var departmentName = db.Departments.AsNoTracking().Where(item => item.Id == user.DepartmentId).Select(item => item.Name).SingleOrDefault() ?? user.DepartmentId;
        var positionName = user.PositionId is null ? null : db.Positions.AsNoTracking().Where(item => item.Id == user.PositionId).Select(item => item.Name).SingleOrDefault();
        var managerName = user.ManagerId is null ? null : db.Users.AsNoTracking().Where(item => item.Id == user.ManagerId).Select(item => item.Name).SingleOrDefault();
        var events = includeEvents ? db.PersonnelEvents.AsNoTracking().Where(item => item.TenantId == TenantId && item.UserId == user.Id).OrderByDescending(item => item.EffectiveDate).ThenByDescending(item => item.CreatedAt).Select(item => new PersonnelEventView(item.Id, item.EventType, item.EffectiveDate, item.Summary, item.ChangedBy, item.ChangedByName, item.CreatedAt)).ToList() : [];
        return ToView(profile, user, departmentName, positionName, managerName, events);
    }

    private static PersonnelProfileView ToView(PersonnelProfileRecord profile, UserRecord user, string departmentName, string? positionName, string? managerName, IReadOnlyList<PersonnelEventView> events) =>
        new(user.Id, profile.EmployeeNumber, user.Name, user.DepartmentId, departmentName, user.PositionId, positionName, user.ManagerId, managerName, profile.WorkEmail, profile.WorkPhone, profile.WorkLocation, profile.EmploymentType, profile.PersonnelStatus, user.Status, profile.HireDate, profile.ProbationEndDate, profile.RegularizedDate, profile.CumulativeWorkStartDate, user.CumulativeWorkYears, profile.DepartureDate, profile.DepartureReason, profile.Version, profile.CreatedAt, profile.UpdatedAt, events);

    private bool CanView(Employee actor, Employee? subject) => subject is not null && data.CanView(actor, subject, "Personnel");
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
    private static string EscapeLikePattern(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
    private static string FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? string.Empty;
    private static string ResolveEventType(string previous, string next, bool organizationChanged) => next == PersonnelStatuses.Terminated && previous != next ? "TERMINATED" : next == PersonnelStatuses.Offboarding && previous != next ? "OFFBOARDING_STARTED" : previous == PersonnelStatuses.Probation && next == PersonnelStatuses.Active ? "REGULARIZED" : organizationChanged ? "TRANSFER" : "PROFILE_UPDATED";
    private static string Normalize(string? value, int maxLength) { var normalized = value?.Trim() ?? string.Empty; return normalized.Length <= maxLength ? normalized : normalized[..maxLength]; }
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
    [GeneratedRegex(@"^[0-9+\-() ]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}

public sealed record PersonnelRosterExport(byte[] Content, string FileName, int Count);
