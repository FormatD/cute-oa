namespace Oa.Api.Domain;

public static class AttendanceStatuses
{
    public const string Normal = "NORMAL";
    public const string Late = "LATE";
    public const string EarlyLeave = "EARLY_LEAVE";
    public const string LateAndEarly = "LATE_AND_EARLY";
    public const string MissingPunch = "MISSING_PUNCH";
    public const string Absent = "ABSENT";
    public const string Leave = "LEAVE";
    public const string RestDay = "REST_DAY";
    public const string Corrected = "CORRECTED";
    public static IReadOnlyList<string> All { get; } = [Normal, Late, EarlyLeave, LateAndEarly, MissingPunch, Absent, Leave, RestDay, Corrected];
}

public static class AttendanceAppealStatuses
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
}

public sealed record AttendanceShiftView(Guid Id, string Code, string Name, TimeOnly WorkStart, TimeOnly WorkEnd, int BreakMinutes, int LateToleranceMinutes, int EarlyLeaveToleranceMinutes, bool IsDefault, bool IsEnabled, int Version, DateTimeOffset UpdatedAt);
public sealed record SaveAttendanceShiftRequest(string Code, string Name, TimeOnly WorkStart, TimeOnly WorkEnd, int BreakMinutes, int LateToleranceMinutes, int EarlyLeaveToleranceMinutes, bool IsDefault, bool IsEnabled, int Version);

public sealed record AttendanceAppealView(Guid Id, string Status, string Reason, IReadOnlyList<string> Attachments, string SubmittedBy, string SubmittedByName, DateTimeOffset SubmittedAt, string? ReviewedBy, string? ReviewedByName, string? ReviewComment, DateTimeOffset? ReviewedAt);

public sealed record AttendanceRecordView(
    Guid Id,
    string UserId,
    string EmployeeName,
    string DepartmentId,
    string DepartmentName,
    DateOnly WorkDate,
    string ShiftCode,
    string ShiftName,
    TimeOnly ScheduledStart,
    TimeOnly ScheduledEnd,
    DateTimeOffset? CheckInAt,
    DateTimeOffset? CheckOutAt,
    string Status,
    string? OriginalStatus,
    int WorkedMinutes,
    int LateMinutes,
    int EarlyLeaveMinutes,
    string Source,
    int Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AttendanceAppealView> Appeals);

public sealed record AttendanceListQuery(string? Keyword, string? UserId, string? DepartmentId, string? Status, DateOnly Month);
public sealed record AttendanceImportItem(string UserId, DateOnly WorkDate, DateTimeOffset? CheckInAt, DateTimeOffset? CheckOutAt);
public sealed record ImportAttendanceRequest(IReadOnlyList<AttendanceImportItem> Items, string? Source = null);
public sealed record CreateAttendanceAppealRequest(string Reason, IReadOnlyList<string>? Attachments = null);
public sealed record ReviewAttendanceAppealRequest(bool Approved, string Comment);
public sealed record AttendanceImportResult(int Created, int Updated, int Skipped);
public sealed record AttendanceMonthlySummaryView(string UserId, string EmployeeName, string DepartmentName, DateOnly Month, int ScheduledDays, int AttendedDays, int NormalDays, int LateDays, int EarlyLeaveDays, int MissingPunchDays, int AbsentDays, int LeaveDays, int CorrectedDays, int PendingAppeals, int WorkedMinutes);

public sealed record AttendanceMonthLockView(
    DateOnly Month,
    bool IsLocked,
    int Version,
    string? LockReason,
    string? LockedBy,
    string? LockedByName,
    DateTimeOffset? LockedAt,
    string? UnlockedBy,
    string? UnlockedByName,
    DateTimeOffset? UnlockedAt,
    string? UnlockReason,
    int? SnapshotSequence,
    string? SnapshotHash,
    int SnapshotRowCount,
    DateTimeOffset? SnapshotCreatedAt);

public sealed record ChangeAttendanceMonthLockRequest(string Reason, int Version);
public sealed record AttendanceMonthSnapshotExport(byte[] Content, string FileName, int Sequence, string Hash, int RowCount);
