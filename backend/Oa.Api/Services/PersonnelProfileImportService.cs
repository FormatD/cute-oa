using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed record PersonnelProfileImportRow(
    string UserId,
    string EmployeeNumber,
    DateOnly HireDate,
    string EmploymentType,
    string PersonnelStatus,
    DateOnly? ProbationEndDate = null,
    DateOnly? RegularizedDate = null,
    DateOnly? CumulativeWorkStartDate = null,
    DateOnly? DepartureDate = null,
    string? DepartureReason = null,
    string? WorkEmail = null,
    string? WorkPhone = null,
    string? WorkLocation = null);

public sealed record PersonnelProfileImportIssue(int Row, string? UserId, string Field, string Message);

public sealed record PersonnelProfileImportResult(
    bool IsSuccess,
    string Code,
    bool Applied,
    int Requested,
    int Ready,
    int RemainingProfiles,
    IReadOnlyList<PersonnelProfileImportIssue> Issues);

public sealed partial class PersonnelProfileImportService(OaDbContext db)
{
    private const string TenantId = IdentityDefaults.TenantId;
    public const int MaximumRows = 10_000;

    public PersonnelProfileImportResult Execute(string actorId, IReadOnlyList<PersonnelProfileImportRow> rows, string source, bool apply)
    {
        var issues = new List<PersonnelProfileImportIssue>();
        var actor = db.Users.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == actorId && item.Status == "ACTIVE");
        if (actor is null || !HasImportPermission(actorId))
        {
            issues.Add(new PersonnelProfileImportIssue(0, actorId, "actorId", "导入操作人不存在、已停用或没有 PERSONNEL_MANAGE 权限。"));
            return Failure(rows.Count, issues);
        }
        if (rows.Count is < 1 or > MaximumRows)
        {
            issues.Add(new PersonnelProfileImportIssue(0, null, "rows", $"每批必须包含 1–{MaximumRows} 行。"));
            return Failure(rows.Count, issues);
        }
        if (string.IsNullOrWhiteSpace(source) || source.Trim().Length > 100)
        {
            issues.Add(new PersonnelProfileImportIssue(0, null, "source", "导入来源应为 1–100 个字符。"));
            return Failure(rows.Count, issues);
        }

        var normalized = rows.Select((row, index) => Normalize(row, index + 1, issues)).ToList();
        foreach (var duplicate in normalized.GroupBy(item => item.Row.UserId, StringComparer.Ordinal).Where(group => group.Count() > 1))
            foreach (var item in duplicate) issues.Add(new PersonnelProfileImportIssue(item.Index, item.Row.UserId, "userId", "同一导入文件中用户 ID 重复。"));
        foreach (var duplicate in normalized.GroupBy(item => item.Row.EmployeeNumber, StringComparer.Ordinal).Where(group => group.Count() > 1))
            foreach (var item in duplicate) issues.Add(new PersonnelProfileImportIssue(item.Index, item.Row.UserId, "employeeNumber", "同一导入文件中工号重复。"));

        var userIds = normalized.Select(item => item.Row.UserId).Distinct(StringComparer.Ordinal).ToList();
        var users = db.Users.AsNoTracking().Where(item => item.TenantId == TenantId && userIds.Contains(item.Id)).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var existingProfiles = db.PersonnelProfiles.AsNoTracking().Where(item => item.TenantId == TenantId && userIds.Contains(item.UserId)).Select(item => item.UserId).ToHashSet(StringComparer.Ordinal);
        var employeeNumbers = normalized.Select(item => item.Row.EmployeeNumber).Distinct(StringComparer.Ordinal).ToList();
        var occupiedNumbers = db.PersonnelProfiles.AsNoTracking().Where(item => item.TenantId == TenantId && employeeNumbers.Contains(item.EmployeeNumber)).Select(item => item.EmployeeNumber).ToHashSet(StringComparer.Ordinal);
        foreach (var item in normalized)
        {
            if (!users.ContainsKey(item.Row.UserId)) issues.Add(new PersonnelProfileImportIssue(item.Index, item.Row.UserId, "userId", "账号不存在。"));
            if (existingProfiles.Contains(item.Row.UserId)) issues.Add(new PersonnelProfileImportIssue(item.Index, item.Row.UserId, "userId", "账号已经有人事档案；离线补录工具不覆盖现有档案。"));
            if (occupiedNumbers.Contains(item.Row.EmployeeNumber)) issues.Add(new PersonnelProfileImportIssue(item.Index, item.Row.UserId, "employeeNumber", "工号已被现有人事档案使用。"));
        }
        if (issues.Count > 0) return Failure(rows.Count, issues);

        var remainingBefore = CountMissingProfiles();
        if (!apply)
            return new PersonnelProfileImportResult(true, "OK", false, rows.Count, rows.Count, remainingBefore, []);

        using var transaction = db.Database.CurrentTransaction is null ? db.Database.BeginTransaction() : null;
        try
        {
            var sourceLabel = source.Trim();
            foreach (var item in normalized)
            {
                var row = item.Row;
                var user = db.Users.Single(value => value.TenantId == TenantId && value.Id == row.UserId);
                user.CumulativeWorkYears = row.CumulativeWorkStartDate is null
                    ? user.CumulativeWorkYears
                    : PersonnelProfileProvisioning.CompletedYears(row.CumulativeWorkStartDate.Value, BusinessTime.ChinaToday());
                user.Status = row.PersonnelStatus == PersonnelStatuses.Terminated ? "DISABLED" : "ACTIVE";
                user.Version++;
                user.UpdatedAt = DateTimeOffset.UtcNow;
                var profile = PersonnelProfileProvisioning.Create(user, row.EmployeeNumber, row.HireDate, row.EmploymentType, row.PersonnelStatus, row.ProbationEndDate, row.CumulativeWorkStartDate);
                profile.WorkEmail = row.WorkEmail ?? string.Empty;
                profile.WorkPhone = row.WorkPhone ?? string.Empty;
                profile.WorkLocation = row.WorkLocation ?? string.Empty;
                profile.RegularizedDate = row.RegularizedDate;
                profile.DepartureDate = row.DepartureDate;
                profile.DepartureReason = row.DepartureReason;
                db.PersonnelProfiles.Add(profile);
                db.PersonnelEvents.Add(PersonnelProfileProvisioning.CreateInitialEvent(profile, actor.Id, actor.Name, $"通过受控离线导入建立人事档案；来源：{sourceLabel}"));
                db.AuditLogs.Add(new AuditRecord
                {
                    TenantId = TenantId,
                    ActorId = actor.Id,
                    Action = "PERSONNEL_PROFILE_IMPORTED",
                    ResourceType = "PersonnelProfile",
                    ResourceId = user.Id,
                    Summary = $"补录人事档案 {profile.EmployeeNumber}；来源：{sourceLabel}"
                });
            }
            db.SaveChanges();
            transaction?.Commit();
        }
        catch (DbUpdateConcurrencyException)
        {
            transaction?.Rollback();
            db.ChangeTracker.Clear();
            issues.Add(new PersonnelProfileImportIssue(0, null, "database", "导入期间账号被其他操作更新，整批已回滚。"));
            return Failure(rows.Count, issues, "CONCURRENCY_001");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            transaction?.Rollback();
            db.ChangeTracker.Clear();
            issues.Add(new PersonnelProfileImportIssue(0, null, "database", "导入期间发生用户或工号唯一冲突，整批已回滚。"));
            return Failure(rows.Count, issues, "PERSONNEL_002");
        }
        catch
        {
            transaction?.Rollback();
            db.ChangeTracker.Clear();
            throw;
        }

        return new PersonnelProfileImportResult(true, "OK", true, rows.Count, rows.Count, CountMissingProfiles(), []);
    }

    private bool HasImportPermission(string actorId)
    {
        var roles = db.UserRoles.AsNoTracking().Where(item => item.UserId == actorId).Select(item => item.RoleCode);
        return db.RolePermissions.AsNoTracking().Any(item => roles.Contains(item.RoleCode) && item.PermissionCode == OaPermissions.PersonnelManage);
    }

    private int CountMissingProfiles() => db.Users.AsNoTracking().Count(item => item.TenantId == TenantId && !db.PersonnelProfiles.Any(profile => profile.TenantId == item.TenantId && profile.UserId == item.Id));

    private static (int Index, PersonnelProfileImportRow Row) Normalize(PersonnelProfileImportRow value, int index, ICollection<PersonnelProfileImportIssue> issues)
    {
        var row = value with
        {
            UserId = value.UserId?.Trim() ?? string.Empty,
            EmployeeNumber = (value.EmployeeNumber?.Trim() ?? string.Empty).ToUpperInvariant(),
            EmploymentType = (value.EmploymentType?.Trim() ?? string.Empty).ToUpperInvariant(),
            PersonnelStatus = (value.PersonnelStatus?.Trim() ?? string.Empty).ToUpperInvariant(),
            DepartureReason = NullIfEmpty(value.DepartureReason),
            WorkEmail = NullIfEmpty(value.WorkEmail),
            WorkPhone = NullIfEmpty(value.WorkPhone),
            WorkLocation = NullIfEmpty(value.WorkLocation)
        };
        Validate(index, row, issues);
        return (index, row);
    }

    private static void Validate(int index, PersonnelProfileImportRow row, ICollection<PersonnelProfileImportIssue> issues)
    {
        var today = BusinessTime.ChinaToday();
        if (row.UserId.Length is < 2 or > 64) Add("userId", "用户 ID 应为 2–64 个字符。");
        if (row.EmployeeNumber.Length is < 2 or > 32 || row.EmployeeNumber.Any(character => !char.IsLetterOrDigit(character) && character != '-')) Add("employeeNumber", "工号应为 2–32 位字母、数字或横线。");
        if (row.HireDate > today.AddDays(90)) Add("hireDate", "入职日期不能晚于当前日期后 90 天。");
        if (!EmploymentTypes.All.Contains(row.EmploymentType)) Add("employmentType", "用工类型不合法。");
        if (!PersonnelStatuses.All.Contains(row.PersonnelStatus)) Add("personnelStatus", "人事状态不合法。");
        if (row.ProbationEndDate < row.HireDate) Add("probationEndDate", "试用期结束日期不能早于入职日期。");
        if (row.RegularizedDate < row.HireDate) Add("regularizedDate", "转正日期不能早于入职日期。");
        if (row.DepartureDate < row.HireDate) Add("departureDate", "离职日期不能早于入职日期。");
        if (row.CumulativeWorkStartDate > today) Add("cumulativeWorkStartDate", "累计工作起始日期不能晚于当前日期。");
        if (row.PersonnelStatus == PersonnelStatuses.Probation && row.ProbationEndDate is null) Add("probationEndDate", "试用员工必须填写试用期结束日期。");
        if (row.PersonnelStatus == PersonnelStatuses.Probation && row.RegularizedDate is not null) Add("regularizedDate", "试用员工不能填写转正日期。");
        if (row.PersonnelStatus is PersonnelStatuses.Offboarding or PersonnelStatuses.Terminated && (row.DepartureDate is null || string.IsNullOrWhiteSpace(row.DepartureReason))) Add("departureDate", "离职办理中或已离职员工必须填写离职日期和原因。");
        if (row.PersonnelStatus is PersonnelStatuses.Active or PersonnelStatuses.Probation && (row.DepartureDate is not null || row.DepartureReason is not null)) Add("departureDate", "在职或试用员工不能填写离职日期和原因。");
        if ((row.DepartureReason?.Length ?? 0) > 500) Add("departureReason", "离职原因不能超过 500 个字符。");
        if ((row.WorkEmail?.Length ?? 0) > 128 || row.WorkEmail is not null && !EmailPattern().IsMatch(row.WorkEmail)) Add("workEmail", "工作邮箱格式不正确或超过 128 个字符。");
        if ((row.WorkPhone?.Length ?? 0) > 32 || row.WorkPhone is not null && (row.WorkPhone.Length < 5 || !PhonePattern().IsMatch(row.WorkPhone))) Add("workPhone", "工作电话格式不正确或超过 32 个字符。");
        if ((row.WorkLocation?.Length ?? 0) > 100) Add("workLocation", "办公地点不能超过 100 个字符。");
        return;

        void Add(string field, string message) => issues.Add(new PersonnelProfileImportIssue(index, row.UserId, field, message));
    }

    private static PersonnelProfileImportResult Failure(int requested, IReadOnlyList<PersonnelProfileImportIssue> issues, string code = "PERSONNEL_IMPORT_001") =>
        new(false, code, false, requested, 0, -1, issues);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
    [GeneratedRegex(@"^[0-9+\-() ]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
