using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class AttendanceService(OaDbContext db, DemoData data, IWorkCalendar calendar, NotificationService notifications, FileService files, IConfiguration configuration)
{
    private const string TenantId = IdentityDefaults.TenantId;
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public IReadOnlyList<AttendanceShiftView> ListShifts(Employee actor)
    {
        EnsureDefaultShift();
        return db.AttendanceShifts.AsNoTracking().Where(item => item.TenantId == TenantId)
            .OrderByDescending(item => item.IsDefault).ThenBy(item => item.Code).Select(ToShiftView).ToList();
    }

    public ServiceResult<AttendanceShiftView> UpdateShift(Employee actor, Guid id, SaveAttendanceShiftRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<AttendanceShiftView>.Failure("无考勤班次维护权限。", "AUTH_002");
        var item = db.AttendanceShifts.SingleOrDefault(candidate => candidate.Id == id && candidate.TenantId == TenantId);
        if (item is null) return ServiceResult<AttendanceShiftView>.Failure("班次不存在。", "DATA_001");
        if (item.Version != request.Version) return ServiceResult<AttendanceShiftView>.Failure("班次已被更新，请刷新后重试。", "CONCURRENCY_001");
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        if (code.Length is < 2 or > 32 || code.Any(character => !char.IsLetterOrDigit(character) && character is not ('_' or '-')) || name.Length is < 1 or > 100)
            return ServiceResult<AttendanceShiftView>.Failure("班次编码或名称不符合要求。", "ATTENDANCE_001");
        if (request.WorkEnd <= request.WorkStart || request.BreakMinutes is < 0 or > 240 || request.LateToleranceMinutes is < 0 or > 120 || request.EarlyLeaveToleranceMinutes is < 0 or > 120)
            return ServiceResult<AttendanceShiftView>.Failure("班次时间、休息时间或宽限分钟不符合要求。", "ATTENDANCE_001");
        if (db.AttendanceShifts.Any(candidate => candidate.TenantId == TenantId && candidate.Id != id && candidate.Code == code))
            return ServiceResult<AttendanceShiftView>.Failure("班次编码已存在。", "DUPLICATE_001");
        if (!request.IsEnabled && item.IsDefault) return ServiceResult<AttendanceShiftView>.Failure("默认班次不能停用。", "ATTENDANCE_003");
        if (!request.IsDefault && item.IsDefault && !db.AttendanceShifts.Any(candidate => candidate.TenantId == TenantId && candidate.Id != id && candidate.IsDefault && candidate.IsEnabled))
            return ServiceResult<AttendanceShiftView>.Failure("至少需要一个启用的默认班次。", "ATTENDANCE_003");
        if (request.IsDefault)
            foreach (var other in db.AttendanceShifts.Where(candidate => candidate.TenantId == TenantId && candidate.Id != id && candidate.IsDefault)) other.IsDefault = false;

        item.Code = code; item.Name = name; item.WorkStart = request.WorkStart; item.WorkEnd = request.WorkEnd; item.BreakMinutes = request.BreakMinutes;
        item.LateToleranceMinutes = request.LateToleranceMinutes; item.EarlyLeaveToleranceMinutes = request.EarlyLeaveToleranceMinutes;
        item.IsDefault = request.IsDefault; item.IsEnabled = request.IsEnabled; item.Version++; item.UpdatedBy = actor.Id; item.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "ATTENDANCE_SHIFT_UPDATED", "AttendanceShift", item.Id.ToString(), $"更新班次 {item.Name}");
        db.SaveChanges();
        return ServiceResult<AttendanceShiftView>.Success(ToShiftView(item));
    }

    public IReadOnlyList<AttendanceRecordView> List(Employee actor, AttendanceListQuery query)
    {
        var month = NormalizeMonth(query.Month);
        var end = month.AddMonths(1);
        var visibleIds = VisibleEmployees(actor).Select(item => item.Id).ToHashSet();
        var records = db.AttendanceRecords.AsNoTracking().Where(item => item.TenantId == TenantId && visibleIds.Contains(item.UserId) && item.WorkDate >= month && item.WorkDate < end);
        if (!string.IsNullOrWhiteSpace(query.UserId)) records = records.Where(item => item.UserId == query.UserId);
        if (!string.IsNullOrWhiteSpace(query.DepartmentId)) records = records.Where(item => item.DepartmentId == query.DepartmentId);
        if (!string.IsNullOrWhiteSpace(query.Status)) records = records.Where(item => item.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            records = records.Where(item => item.EmployeeName.Contains(keyword) || item.UserId.Contains(keyword) || item.DepartmentName.Contains(keyword));
        }
        var items = records.OrderByDescending(item => item.WorkDate).ThenByDescending(item => item.UpdatedAt).ToList();
        return MapRecords(items);
    }

    public ServiceResult<AttendanceRecordView> Get(Employee actor, Guid id)
    {
        var record = db.AttendanceRecords.AsNoTracking().SingleOrDefault(item => item.Id == id && item.TenantId == TenantId);
        if (record is null) return ServiceResult<AttendanceRecordView>.Failure("考勤记录不存在。", "DATA_001");
        var subject = data.FindEmployee(record.UserId);
        if (subject is null || !data.CanView(actor, subject, "Attendance")) return ServiceResult<AttendanceRecordView>.Failure("无权查看该考勤记录。", "AUTH_002");
        return ServiceResult<AttendanceRecordView>.Success(MapRecords([record]).Single());
    }

    public ServiceResult<AttendanceImportResult> Import(Employee actor, ImportAttendanceRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<AttendanceImportResult>.Failure("无考勤导入权限。", "AUTH_002");
        if (request.Items is null || request.Items.Count is < 1 or > 500) return ServiceResult<AttendanceImportResult>.Failure("单次应导入 1–500 条考勤记录。", "ATTENDANCE_001");
        if (request.Items.GroupBy(item => (item.UserId, item.WorkDate)).Any(group => group.Count() > 1)) return ServiceResult<AttendanceImportResult>.Failure("同一批次包含重复的员工日期。", "ATTENDANCE_001");
        var lockedMonths = request.Items.Select(item => NormalizeMonth(item.WorkDate)).Distinct().Where(IsMonthLocked).OrderBy(item => item).ToList();
        if (lockedMonths.Count > 0) return ServiceResult<AttendanceImportResult>.Failure($"{string.Join('、', lockedMonths.Select(item => item.ToString("yyyy-MM")))} 已封账，不能导入或修改考勤。", "ATTENDANCE_004");
        var shift = EnsureDefaultShift();
        var created = 0; var updated = 0; var skipped = 0;
        foreach (var input in request.Items)
        {
            var subject = data.FindEmployee(input.UserId);
            if (subject is null || subject.Status != "ACTIVE" || !data.CanView(actor, subject, "Attendance")) { skipped++; continue; }
            var validation = ValidatePunches(input);
            if (validation is not null) return ServiceResult<AttendanceImportResult>.Failure(validation, "ATTENDANCE_001");
            var record = db.AttendanceRecords.SingleOrDefault(item => item.TenantId == TenantId && item.UserId == input.UserId && item.WorkDate == input.WorkDate);
            if (record is null)
            {
                record = NewRecord(subject, input.WorkDate, shift, request.Source ?? "IMPORT", actor.Id);
                db.AttendanceRecords.Add(record); created++;
            }
            else { record.Version++; updated++; }
            ApplyPunches(record, input.CheckInAt, input.CheckOutAt, shift, request.Source ?? "IMPORT", actor.Id);
        }
        Audit(actor, "ATTENDANCE_IMPORTED", "AttendanceRecord", "batch", $"导入考勤：新增 {created}，更新 {updated}，跳过 {skipped}");
        db.SaveChanges();
        return ServiceResult<AttendanceImportResult>.Success(new(created, updated, skipped));
    }

    public ServiceResult<AttendanceImportResult> GenerateDemoData(Employee actor, DateOnly requestedMonth)
    {
        if (!configuration.GetValue<bool>("DemoFeatures:AllowDataGeneration")) return ServiceResult<AttendanceImportResult>.Failure("当前环境已关闭模拟数据生成功能。", "DEMO_DISABLED");
        if (!CanManage(actor)) return ServiceResult<AttendanceImportResult>.Failure("无模拟考勤生成权限。", "AUTH_002");
        var month = NormalizeMonth(requestedMonth);
        if (IsMonthLocked(month)) return ServiceResult<AttendanceImportResult>.Failure($"{month:yyyy-MM} 已封账，不能生成模拟考勤。", "ATTENDANCE_004");
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (month > new DateOnly(today.Year, today.Month, 1) || month < new DateOnly(today.Year, today.Month, 1).AddMonths(-24))
            return ServiceResult<AttendanceImportResult>.Failure("仅可生成当前月及过去 24 个月的模拟考勤。", "ATTENDANCE_001");
        var shift = EnsureDefaultShift();
        var end = month.AddMonths(1).AddDays(-1);
        if (end > today) end = today;
        var created = 0; var skipped = 0;
        foreach (var subject in VisibleEmployees(actor).Where(item => item.Status == "ACTIVE"))
        {
            for (var date = month; date <= end; date = date.AddDays(1))
            {
                if (db.AttendanceRecords.Any(item => item.TenantId == TenantId && item.UserId == subject.Id && item.WorkDate == date)) { skipped++; continue; }
                var record = NewRecord(subject, date, shift, "DEMO", actor.Id);
                var (checkIn, checkOut) = DemoPunches(subject.Id, date, shift);
                ApplyPunches(record, checkIn, checkOut, shift, "DEMO", actor.Id);
                db.AttendanceRecords.Add(record); created++;
            }
        }
        Audit(actor, "ATTENDANCE_DEMO_GENERATED", "AttendanceRecord", month.ToString("yyyy-MM"), $"生成 {month:yyyy-MM} 模拟考勤 {created} 条，保留已有 {skipped} 条");
        db.SaveChanges();
        return ServiceResult<AttendanceImportResult>.Success(new(created, 0, skipped));
    }

    public ServiceResult<AttendanceAppealView> SubmitAppeal(Employee actor, Guid recordId, CreateAttendanceAppealRequest request)
    {
        var record = db.AttendanceRecords.SingleOrDefault(item => item.Id == recordId && item.TenantId == TenantId);
        if (record is null || record.UserId != actor.Id) return ServiceResult<AttendanceAppealView>.Failure("考勤记录不存在或不属于当前用户。", "DATA_001");
        if (IsMonthLocked(record.WorkDate)) return ServiceResult<AttendanceAppealView>.Failure($"{record.WorkDate:yyyy-MM} 已封账，不能再提交申诉。", "ATTENDANCE_004");
        if (record.Status is AttendanceStatuses.Normal or AttendanceStatuses.Leave or AttendanceStatuses.RestDay or AttendanceStatuses.Corrected)
            return ServiceResult<AttendanceAppealView>.Failure("当前记录不需要或不允许申诉。", "ATTENDANCE_003");
        var reason = request.Reason?.Trim() ?? string.Empty;
        var attachments = request.Attachments?.Distinct().ToList() ?? [];
        if (reason.Length is < 5 or > 500) return ServiceResult<AttendanceAppealView>.Failure("申诉原因应为 5–500 个字符。", "ATTENDANCE_001");
        if (attachments.Count > 5 || !files.AreOwnedBy(actor, attachments)) return ServiceResult<AttendanceAppealView>.Failure("申诉附件不存在、不属于当前用户或超过 5 个。", "FILE_005");
        if (db.AttendanceAppeals.Any(item => item.AttendanceRecordId == recordId && item.Status == AttendanceAppealStatuses.Pending))
            return ServiceResult<AttendanceAppealView>.Failure("该记录已有待审核申诉。", "ATTENDANCE_002");
        var appeal = new AttendanceAppealRecord { TenantId = TenantId, AttendanceRecordId = recordId, Reason = reason, AttachmentsJson = JsonSerializer.Serialize(attachments), SubmittedBy = actor.Id, SubmittedByName = actor.Name };
        db.AttendanceAppeals.Add(appeal);
        Audit(actor, "ATTENDANCE_APPEAL_SUBMITTED", "AttendanceAppeal", appeal.Id.ToString(), $"提交 {record.WorkDate:yyyy-MM-dd} 考勤申诉");
        db.SaveChanges();
        foreach (var reviewerId in AttendanceManagers().Where(id => id != actor.Id))
            notifications.Create(reviewerId, "ATTENDANCE_APPEAL", "有新的考勤申诉", $"{actor.Name} 提交了 {record.WorkDate:yyyy-MM-dd} 的考勤申诉。", "AttendanceRecord", record.Id);
        return ServiceResult<AttendanceAppealView>.Success(ToAppealView(appeal));
    }

    public ServiceResult<AttendanceAppealView> ReviewAppeal(Employee actor, Guid appealId, ReviewAttendanceAppealRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<AttendanceAppealView>.Failure("无考勤申诉审核权限。", "AUTH_002");
        var appeal = db.AttendanceAppeals.SingleOrDefault(item => item.Id == appealId && item.TenantId == TenantId);
        if (appeal is null) return ServiceResult<AttendanceAppealView>.Failure("考勤申诉不存在。", "DATA_001");
        if (appeal.Status != AttendanceAppealStatuses.Pending) return ServiceResult<AttendanceAppealView>.Failure("该申诉已处理。", "ATTENDANCE_003");
        var comment = request.Comment?.Trim() ?? string.Empty;
        if (comment.Length is < 2 or > 500) return ServiceResult<AttendanceAppealView>.Failure("审核意见应为 2–500 个字符。", "ATTENDANCE_001");
        var record = db.AttendanceRecords.Single(item => item.Id == appeal.AttendanceRecordId);
        if (IsMonthLocked(record.WorkDate)) return ServiceResult<AttendanceAppealView>.Failure($"{record.WorkDate:yyyy-MM} 已封账，不能再审核申诉。", "ATTENDANCE_004");
        var subject = data.FindEmployee(record.UserId);
        if (subject is null || !data.CanView(actor, subject, "Attendance")) return ServiceResult<AttendanceAppealView>.Failure("无权审核该员工的考勤申诉。", "AUTH_002");
        appeal.Status = request.Approved ? AttendanceAppealStatuses.Approved : AttendanceAppealStatuses.Rejected;
        appeal.ReviewedBy = actor.Id; appeal.ReviewedByName = actor.Name; appeal.ReviewComment = comment; appeal.ReviewedAt = DateTimeOffset.UtcNow;
        if (request.Approved)
        {
            record.OriginalStatus ??= record.Status;
            record.Status = AttendanceStatuses.Corrected;
            record.Version++; record.UpdatedBy = actor.Id; record.UpdatedAt = DateTimeOffset.UtcNow;
        }
        Audit(actor, request.Approved ? "ATTENDANCE_APPEAL_APPROVED" : "ATTENDANCE_APPEAL_REJECTED", "AttendanceAppeal", appeal.Id.ToString(), $"{(request.Approved ? "通过" : "驳回")} {record.WorkDate:yyyy-MM-dd} 考勤申诉");
        db.SaveChanges();
        notifications.Create(record.UserId, "ATTENDANCE_APPEAL_REVIEWED", request.Approved ? "考勤申诉已通过" : "考勤申诉已驳回", comment, "AttendanceRecord", record.Id);
        return ServiceResult<AttendanceAppealView>.Success(ToAppealView(appeal));
    }

    public IReadOnlyList<AttendanceMonthlySummaryView> MonthlySummary(Employee actor, DateOnly requestedMonth, string? userId = null)
    {
        var month = NormalizeMonth(requestedMonth);
        var end = month.AddMonths(1);
        var visible = VisibleEmployees(actor).Where(item => string.IsNullOrWhiteSpace(userId) || item.Id == userId).ToDictionary(item => item.Id);
        var records = db.AttendanceRecords.AsNoTracking().Where(item => item.TenantId == TenantId && visible.Keys.Contains(item.UserId) && item.WorkDate >= month && item.WorkDate < end).ToList();
        var pendingAppeals = db.AttendanceAppeals.AsNoTracking().Where(item => item.TenantId == TenantId && item.Status == AttendanceAppealStatuses.Pending && records.Select(record => record.Id).Contains(item.AttendanceRecordId)).ToList().ToLookup(item => item.AttendanceRecordId);
        return records.GroupBy(item => item.UserId).Select(group =>
        {
            var employee = visible[group.Key];
            return new AttendanceMonthlySummaryView(group.Key, employee.Name, employee.DepartmentName, month,
                group.Count(item => item.Status != AttendanceStatuses.RestDay), group.Count(item => item.CheckInAt is not null || item.CheckOutAt is not null),
                group.Count(item => item.Status == AttendanceStatuses.Normal), group.Count(item => item.Status is AttendanceStatuses.Late or AttendanceStatuses.LateAndEarly),
                group.Count(item => item.Status is AttendanceStatuses.EarlyLeave or AttendanceStatuses.LateAndEarly), group.Count(item => item.Status == AttendanceStatuses.MissingPunch),
                group.Count(item => item.Status == AttendanceStatuses.Absent), group.Count(item => item.Status == AttendanceStatuses.Leave), group.Count(item => item.Status == AttendanceStatuses.Corrected),
                group.Sum(item => pendingAppeals[item.Id].Count()), group.Sum(item => item.WorkedMinutes));
        }).OrderBy(item => item.DepartmentName).ThenBy(item => item.EmployeeName).ToList();
    }

    public AttendanceMonthLockView GetMonthLock(Employee actor, DateOnly requestedMonth)
    {
        var month = NormalizeMonth(requestedMonth);
        var item = db.AttendanceMonthLocks.AsNoTracking().SingleOrDefault(candidate => candidate.TenantId == TenantId && candidate.Month == month);
        return item is null ? EmptyMonthLock(month) : ToMonthLockView(item);
    }

    public ServiceResult<AttendanceMonthLockView> LockMonth(Employee actor, DateOnly requestedMonth, ChangeAttendanceMonthLockRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<AttendanceMonthLockView>.Failure("无考勤月度封账权限。", "AUTH_002");
        var month = NormalizeMonth(requestedMonth);
        var currentMonth = NormalizeMonth(DateOnly.FromDateTime(DateTime.Today));
        if (month > currentMonth) return ServiceResult<AttendanceMonthLockView>.Failure("不能封账未来月份。", "ATTENDANCE_001");
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500) return ServiceResult<AttendanceMonthLockView>.Failure("封账说明应为 5–500 个字符。", "ATTENDANCE_001");
        if (!db.AttendanceRecords.Any(item => item.TenantId == TenantId && item.WorkDate >= month && item.WorkDate < month.AddMonths(1)))
            return ServiceResult<AttendanceMonthLockView>.Failure("该月份没有考勤记录，不能封账。", "ATTENDANCE_003");
        var recordIds = db.AttendanceRecords.AsNoTracking().Where(item => item.TenantId == TenantId && item.WorkDate >= month && item.WorkDate < month.AddMonths(1)).Select(item => item.Id);
        if (db.AttendanceAppeals.Any(item => item.TenantId == TenantId && item.Status == AttendanceAppealStatuses.Pending && recordIds.Contains(item.AttendanceRecordId)))
            return ServiceResult<AttendanceMonthLockView>.Failure("该月份仍有待审核申诉，处理完毕后才能封账。", "ATTENDANCE_005");

        var item = db.AttendanceMonthLocks.SingleOrDefault(candidate => candidate.TenantId == TenantId && candidate.Month == month);
        if (item is null)
        {
            if (request.Version != 0) return ServiceResult<AttendanceMonthLockView>.Failure("封账状态已变化，请刷新后重试。", "CONCURRENCY_001");
            item = new AttendanceMonthLockRecord { TenantId = TenantId, Month = month };
            db.AttendanceMonthLocks.Add(item);
        }
        else
        {
            if (item.Version != request.Version) return ServiceResult<AttendanceMonthLockView>.Failure("封账状态已变化，请刷新后重试。", "CONCURRENCY_001");
            if (item.IsLocked) return ServiceResult<AttendanceMonthLockView>.Success(ToMonthLockView(item));
            item.Version++;
        }

        item.IsLocked = true;
        item.LockReason = reason;
        item.LockedBy = actor.Id;
        item.LockedByName = actor.Name;
        item.LockedAt = DateTimeOffset.UtcNow;
        item.UnlockedBy = null;
        item.UnlockedByName = null;
        item.UnlockedAt = null;
        item.UnlockReason = null;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "ATTENDANCE_MONTH_LOCKED", "AttendanceMonthLock", month.ToString("yyyy-MM"), $"封账 {month:yyyy-MM}：{reason}");
        db.SaveChanges();
        return ServiceResult<AttendanceMonthLockView>.Success(ToMonthLockView(item));
    }

    public ServiceResult<AttendanceMonthLockView> UnlockMonth(Employee actor, DateOnly requestedMonth, ChangeAttendanceMonthLockRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<AttendanceMonthLockView>.Failure("无考勤月度解封权限。", "AUTH_002");
        var month = NormalizeMonth(requestedMonth);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 5 or > 500) return ServiceResult<AttendanceMonthLockView>.Failure("解封说明应为 5–500 个字符。", "ATTENDANCE_001");
        var item = db.AttendanceMonthLocks.SingleOrDefault(candidate => candidate.TenantId == TenantId && candidate.Month == month);
        if (item is null || !item.IsLocked) return ServiceResult<AttendanceMonthLockView>.Failure("该月份当前未封账。", "ATTENDANCE_003");
        if (item.Version != request.Version) return ServiceResult<AttendanceMonthLockView>.Failure("封账状态已变化，请刷新后重试。", "CONCURRENCY_001");

        item.IsLocked = false;
        item.Version++;
        item.UnlockedBy = actor.Id;
        item.UnlockedByName = actor.Name;
        item.UnlockedAt = DateTimeOffset.UtcNow;
        item.UnlockReason = reason;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "ATTENDANCE_MONTH_UNLOCKED", "AttendanceMonthLock", month.ToString("yyyy-MM"), $"解封 {month:yyyy-MM}：{reason}");
        db.SaveChanges();
        return ServiceResult<AttendanceMonthLockView>.Success(ToMonthLockView(item));
    }

    private AttendanceShiftRecord EnsureDefaultShift()
    {
        var shift = db.AttendanceShifts.SingleOrDefault(item => item.TenantId == TenantId && item.IsDefault && item.IsEnabled)
            ?? db.AttendanceShifts.SingleOrDefault(item => item.TenantId == TenantId && item.Code == "STANDARD");
        if (shift is not null) return shift;
        shift = new AttendanceShiftRecord { TenantId = TenantId, Code = "STANDARD", Name = "标准班次", WorkStart = new TimeOnly(9, 0), WorkEnd = new TimeOnly(18, 0), BreakMinutes = 60, LateToleranceMinutes = 5, EarlyLeaveToleranceMinutes = 5, IsDefault = true, IsEnabled = true, UpdatedBy = "system" };
        db.AttendanceShifts.Add(shift); db.SaveChanges();
        return shift;
    }

    private AttendanceRecordEntity NewRecord(Employee subject, DateOnly date, AttendanceShiftRecord shift, string source, string actorId) => new()
    {
        TenantId = TenantId, UserId = subject.Id, EmployeeName = subject.Name, DepartmentId = subject.DepartmentId, DepartmentName = subject.DepartmentName,
        WorkDate = date, ShiftCode = shift.Code, ShiftName = shift.Name, ScheduledStart = shift.WorkStart, ScheduledEnd = shift.WorkEnd,
        Source = NormalizeSource(source), UpdatedBy = actorId
    };

    private void ApplyPunches(AttendanceRecordEntity record, DateTimeOffset? checkIn, DateTimeOffset? checkOut, AttendanceShiftRecord shift, string source, string actorId)
    {
        record.CheckInAt = checkIn?.ToUniversalTime(); record.CheckOutAt = checkOut?.ToUniversalTime(); record.Source = NormalizeSource(source); record.UpdatedBy = actorId; record.UpdatedAt = DateTimeOffset.UtcNow;
        record.OriginalStatus = null;
        var evaluation = Evaluate(record.UserId, record.WorkDate, checkIn, checkOut, shift);
        record.Status = evaluation.Status; record.WorkedMinutes = evaluation.WorkedMinutes; record.LateMinutes = evaluation.LateMinutes; record.EarlyLeaveMinutes = evaluation.EarlyLeaveMinutes;
    }

    private (string Status, int WorkedMinutes, int LateMinutes, int EarlyLeaveMinutes) Evaluate(string userId, DateOnly date, DateTimeOffset? checkIn, DateTimeOffset? checkOut, AttendanceShiftRecord shift)
    {
        if (!calendar.IsWorkingDay(date)) return (AttendanceStatuses.RestDay, WorkedMinutes(checkIn, checkOut, shift.BreakMinutes), 0, 0);
        if (db.LeaveRequests.AsNoTracking().Any(item => item.TenantId == TenantId && item.ApplicantId == userId && item.Status == (int)LeaveStatus.Completed && item.StartDate <= date && item.EndDate >= date))
            return (AttendanceStatuses.Leave, WorkedMinutes(checkIn, checkOut, shift.BreakMinutes), 0, 0);
        if (checkIn is null && checkOut is null) return (AttendanceStatuses.Absent, 0, 0, 0);
        if (checkIn is null || checkOut is null) return (AttendanceStatuses.MissingPunch, 0, 0, 0);
        var scheduledStart = ChinaDateTime(date, shift.WorkStart);
        var scheduledEnd = ChinaDateTime(date, shift.WorkEnd);
        var late = Math.Max(0, (int)Math.Ceiling((checkIn.Value.ToOffset(ChinaOffset) - scheduledStart.AddMinutes(shift.LateToleranceMinutes)).TotalMinutes));
        var early = Math.Max(0, (int)Math.Ceiling((scheduledEnd.AddMinutes(-shift.EarlyLeaveToleranceMinutes) - checkOut.Value.ToOffset(ChinaOffset)).TotalMinutes));
        var status = late > 0 && early > 0 ? AttendanceStatuses.LateAndEarly : late > 0 ? AttendanceStatuses.Late : early > 0 ? AttendanceStatuses.EarlyLeave : AttendanceStatuses.Normal;
        return (status, WorkedMinutes(checkIn, checkOut, shift.BreakMinutes), late, early);
    }

    private static int WorkedMinutes(DateTimeOffset? checkIn, DateTimeOffset? checkOut, int breakMinutes) => checkIn is null || checkOut is null || checkOut <= checkIn ? 0 : Math.Max(0, (int)(checkOut.Value - checkIn.Value).TotalMinutes - breakMinutes);

    private static (DateTimeOffset? CheckIn, DateTimeOffset? CheckOut) DemoPunches(string userId, DateOnly date, AttendanceShiftRecord shift)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return (null, null);
        var seed = userId.Sum(character => character) + date.Day * 7 + date.Month;
        var start = ChinaDateTime(date, shift.WorkStart);
        var end = ChinaDateTime(date, shift.WorkEnd);
        return (seed % 17) switch
        {
            0 => (null, null),
            1 => (start.AddMinutes(-8), null),
            2 => (start.AddMinutes(18), end.AddMinutes(6)),
            3 => (start.AddMinutes(-5), end.AddMinutes(-22)),
            4 => (start.AddMinutes(14), end.AddMinutes(-16)),
            _ => (start.AddMinutes(-5 - seed % 8), end.AddMinutes(3 + seed % 10))
        };
    }

    private IReadOnlyList<AttendanceRecordView> MapRecords(IReadOnlyList<AttendanceRecordEntity> records)
    {
        var ids = records.Select(item => item.Id).ToList();
        var appeals = db.AttendanceAppeals.AsNoTracking().Where(item => ids.Contains(item.AttendanceRecordId)).OrderByDescending(item => item.SubmittedAt).ToList().ToLookup(item => item.AttendanceRecordId);
        return records.Select(item => new AttendanceRecordView(item.Id, item.UserId, item.EmployeeName, item.DepartmentId, item.DepartmentName, item.WorkDate, item.ShiftCode, item.ShiftName, item.ScheduledStart, item.ScheduledEnd, item.CheckInAt, item.CheckOutAt, item.Status, item.OriginalStatus, item.WorkedMinutes, item.LateMinutes, item.EarlyLeaveMinutes, item.Source, item.Version, item.UpdatedAt, appeals[item.Id].Select(ToAppealView).ToList())).ToList();
    }

    private static AttendanceAppealView ToAppealView(AttendanceAppealRecord item) => new(item.Id, item.Status, item.Reason, DeserializeAttachments(item.AttachmentsJson), item.SubmittedBy, item.SubmittedByName, item.SubmittedAt, item.ReviewedBy, item.ReviewedByName, item.ReviewComment, item.ReviewedAt);
    private static AttendanceShiftView ToShiftView(AttendanceShiftRecord item) => new(item.Id, item.Code, item.Name, item.WorkStart, item.WorkEnd, item.BreakMinutes, item.LateToleranceMinutes, item.EarlyLeaveToleranceMinutes, item.IsDefault, item.IsEnabled, item.Version, item.UpdatedAt);
    private static AttendanceMonthLockView EmptyMonthLock(DateOnly month) => new(month, false, 0, null, null, null, null, null, null, null, null);
    private static AttendanceMonthLockView ToMonthLockView(AttendanceMonthLockRecord item) => new(item.Month, item.IsLocked, item.Version, item.LockReason, item.LockedBy, item.LockedByName, item.LockedAt, item.UnlockedBy, item.UnlockedByName, item.UnlockedAt, item.UnlockReason);
    private IReadOnlyList<Employee> VisibleEmployees(Employee actor) => data.Employees.Where(subject => data.CanView(actor, subject, "Attendance")).ToList();
    private IReadOnlyList<string> AttendanceManagers() => db.UserRoles.AsNoTracking().Join(db.RolePermissions.AsNoTracking().Where(item => item.PermissionCode == OaPermissions.AttendanceManage), role => role.RoleCode, permission => permission.RoleCode, (role, _) => role.UserId).Distinct().ToList();
    private bool CanManage(Employee actor) => data.HasPermission(actor, OaPermissions.AttendanceManage);
    private bool IsMonthLocked(DateOnly date) { var month = NormalizeMonth(date); return db.AttendanceMonthLocks.AsNoTracking().Any(item => item.TenantId == TenantId && item.Month == month && item.IsLocked); }
    private static DateOnly NormalizeMonth(DateOnly value) => new(value.Year, value.Month, 1);
    private static DateTimeOffset ChinaDateTime(DateOnly date, TimeOnly time) => new(date.ToDateTime(time), ChinaOffset);
    private static string NormalizeSource(string source) => source.Trim().ToUpperInvariant() switch { "DEMO" => "DEMO", "MANUAL" => "MANUAL", _ => "IMPORT" };
    private static string? ValidatePunches(AttendanceImportItem item)
    {
        if (string.IsNullOrWhiteSpace(item.UserId)) return "员工不能为空。";
        if (item.CheckInAt is not null && DateOnly.FromDateTime(item.CheckInAt.Value.ToOffset(ChinaOffset).DateTime) != item.WorkDate) return "上班打卡日期与考勤日期不一致。";
        if (item.CheckOutAt is not null && DateOnly.FromDateTime(item.CheckOutAt.Value.ToOffset(ChinaOffset).DateTime) != item.WorkDate) return "下班打卡日期与考勤日期不一致。";
        if (item.CheckInAt is not null && item.CheckOutAt is not null && item.CheckOutAt <= item.CheckInAt) return "下班打卡必须晚于上班打卡。";
        return null;
    }
    private static IReadOnlyList<string> DeserializeAttachments(string value) { try { return JsonSerializer.Deserialize<List<string>>(value) ?? []; } catch (JsonException) { return []; } }
    private void Audit(Employee actor, string action, string resourceType, string resourceId, string summary) => db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = resourceType, ResourceId = resourceId, Summary = summary });
}
