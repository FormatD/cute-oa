using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed partial class PersonnelService(OaDbContext db, DemoData data, NotificationService? notifications = null)
{
    private const string TenantId = IdentityDefaults.TenantId;

    public ServiceResult<PagedResponse<PersonnelProfileView>> List(Employee actor, string? keyword, string? departmentId, string? personnelStatus, string? employmentType, int? page, int? pageSize)
    {
        EnsureProfiles();
        var profiles = db.PersonnelProfiles.AsNoTracking().Where(item => item.TenantId == TenantId).ToList();
        var users = db.Users.AsNoTracking().Where(item => item.TenantId == TenantId).ToDictionary(item => item.Id);
        var visible = profiles.Where(profile => users.TryGetValue(profile.UserId, out var user) && CanView(actor, data.FindEmployee(user.Id)))
            .Where(profile => string.IsNullOrWhiteSpace(departmentId) || users[profile.UserId].DepartmentId == departmentId)
            .Where(profile => string.IsNullOrWhiteSpace(personnelStatus) || profile.PersonnelStatus == personnelStatus.Trim().ToUpperInvariant())
            .Where(profile => string.IsNullOrWhiteSpace(employmentType) || profile.EmploymentType == employmentType.Trim().ToUpperInvariant())
            .Select(profile => ToView(profile, users[profile.UserId], false))
            .Where(view => string.IsNullOrWhiteSpace(keyword) || Matches(view, keyword.Trim()))
            .OrderByDescending(view => view.HireDate).ThenBy(view => view.EmployeeNumber)
            .ToList();
        return ServiceResult<PagedResponse<PersonnelProfileView>>.Success(Paging.Create(visible, page, pageSize));
    }

    public ServiceResult<PersonnelProfileView> Get(Employee actor, string userId)
    {
        EnsureProfiles();
        var user = db.Users.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == userId);
        var subject = data.FindEmployee(userId);
        if (user is null || subject is null) return ServiceResult<PersonnelProfileView>.Failure("员工档案不存在。", "DATA_001");
        if (!CanView(actor, subject)) return ServiceResult<PersonnelProfileView>.Failure("无权查看该员工档案。", "AUTH_002");
        var profile = db.PersonnelProfiles.AsNoTracking().Single(item => item.UserId == userId);
        return ServiceResult<PersonnelProfileView>.Success(ToView(profile, user, true));
    }

    public ServiceResult<PersonnelProfileView> Update(Employee actor, string userId, UpdatePersonnelProfileRequest request)
    {
        EnsureProfiles();
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

        user.DepartmentId = request.DepartmentId;
        user.PositionId = normalizedPosition;
        user.ManagerId = normalizedManager;
        user.CumulativeWorkYears = request.CumulativeWorkStartDate is null ? user.CumulativeWorkYears : CompletedYears(request.CumulativeWorkStartDate.Value, DateOnly.FromDateTime(DateTime.UtcNow));
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
        db.SaveChanges();

        if (accountStatusChanged || organizationChanged)
            AuthenticationService.RevokeAllSessions(db, userId, accountStatusChanged ? "PERSONNEL_STATUS_CHANGED" : "PERSONNEL_ORGANIZATION_CHANGED");
        if (actor.Id != userId)
            notifications?.Create(userId, "PERSONNEL_PROFILE_UPDATED", "人事档案已更新", $"{actor.Name} 更新了你的人事档案：{summary}", "PersonnelProfile", userId);

        return ServiceResult<PersonnelProfileView>.Success(ToView(profile, user, true));
    }

    private ServiceResult<bool> Validate(string userId, UpdatePersonnelProfileRequest request)
    {
        var status = request.PersonnelStatus.Trim().ToUpperInvariant();
        var employmentType = request.EmploymentType.Trim().ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!EmploymentTypes.All.Contains(employmentType) || !PersonnelStatuses.All.Contains(status)) return ServiceResult<bool>.Failure("用工类型或人事状态不合法。", "PERSONNEL_001");
        if (request.HireDate > today.AddDays(90)) return ServiceResult<bool>.Failure("入职日期不能晚于当前日期后 90 天。", "PERSONNEL_003");
        if (request.ProbationEndDate < request.HireDate || request.RegularizedDate < request.HireDate || request.DepartureDate < request.HireDate) return ServiceResult<bool>.Failure("试用、转正或离职日期不能早于入职日期。", "PERSONNEL_003");
        if (status == PersonnelStatuses.Probation && request.ProbationEndDate is null) return ServiceResult<bool>.Failure("试用员工必须填写试用期结束日期。", "PERSONNEL_003");
        if (status == PersonnelStatuses.Probation && request.RegularizedDate is not null) return ServiceResult<bool>.Failure("已填写转正日期的员工不能保持试用状态。", "PERSONNEL_003");
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
        return new PersonnelProfileView(user.Id, profile.EmployeeNumber, user.Name, user.DepartmentId, departmentName, user.PositionId, positionName, user.ManagerId, managerName, profile.WorkEmail, profile.WorkPhone, profile.WorkLocation, profile.EmploymentType, profile.PersonnelStatus, user.Status, profile.HireDate, profile.ProbationEndDate, profile.RegularizedDate, profile.CumulativeWorkStartDate, user.CumulativeWorkYears, profile.DepartureDate, profile.DepartureReason, profile.Version, profile.CreatedAt, profile.UpdatedAt, events);
    }

    private void EnsureProfiles()
    {
        var existing = db.PersonnelProfiles.AsNoTracking().Where(item => item.TenantId == TenantId).Select(item => item.UserId).ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var user in db.Users.AsNoTracking().Where(item => item.TenantId == TenantId).ToList().Where(item => !existing.Contains(item.Id)))
        {
            var terminated = user.Status != "ACTIVE";
            var profile = new PersonnelProfileRecord
            {
                UserId = user.Id,
                TenantId = TenantId,
                EmployeeNumber = GenerateEmployeeNumber(user.Id),
                EmploymentType = "FULL_TIME",
                PersonnelStatus = terminated ? PersonnelStatuses.Terminated : PersonnelStatuses.Active,
                HireDate = today.AddYears(-Math.Min(user.CumulativeWorkYears, 20)),
                CumulativeWorkStartDate = today.AddYears(-user.CumulativeWorkYears),
                DepartureDate = terminated ? today : null,
                DepartureReason = terminated ? "历史停用账号导入" : null
            };
            db.PersonnelProfiles.Add(profile);
            db.PersonnelEvents.Add(new PersonnelEventRecord { TenantId = TenantId, UserId = user.Id, EventType = "IMPORTED", EffectiveDate = profile.HireDate, Summary = "从现有用户主数据初始化人事档案", ChangedBy = "system", ChangedByName = "系统", SnapshotJson = JsonSerializer.Serialize(new { profile.EmployeeNumber, profile.PersonnelStatus }) });
        }
        db.SaveChanges();
    }

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
    private static bool Matches(PersonnelProfileView view, string keyword) => view.EmployeeNumber.Contains(keyword, StringComparison.OrdinalIgnoreCase) || view.UserId.Contains(keyword, StringComparison.OrdinalIgnoreCase) || view.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) || view.DepartmentName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || (view.PositionName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);
    private static string ResolveEventType(string previous, string next, bool organizationChanged) => next == PersonnelStatuses.Terminated && previous != next ? "TERMINATED" : next == PersonnelStatuses.Offboarding && previous != next ? "OFFBOARDING_STARTED" : previous == PersonnelStatuses.Probation && next == PersonnelStatuses.Active ? "REGULARIZED" : organizationChanged ? "TRANSFER" : "PROFILE_UPDATED";
    private static int CompletedYears(DateOnly start, DateOnly end) { var years = end.Year - start.Year; if (end < start.AddYears(years)) years--; return Math.Clamp(years, 0, 60); }
    private static string GenerateEmployeeNumber(string userId) => $"EMP-{new string(userId.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray())}"[..Math.Min(32, 4 + userId.Count(char.IsLetterOrDigit))];
    private static string Normalize(string? value, int maxLength) { var normalized = value?.Trim() ?? string.Empty; return normalized.Length <= maxLength ? normalized : normalized[..maxLength]; }
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
    [GeneratedRegex(@"^[0-9+\-() ]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
