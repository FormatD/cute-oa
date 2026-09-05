namespace Oa.Api.Domain;

public static class WorkItemTabs
{
    public const string Pending = "pending";
    public const string Processed = "processed";
    public const string Initiated = "initiated";
    public const string Reading = "reading";
    public const string Risk = "risk";
    public static IReadOnlyList<string> All { get; } = [Pending, Processed, Initiated, Reading, Risk];
}

public static class WorkItemCategories
{
    public const string Approval = "APPROVAL";
    public const string Personnel = "PERSONNEL";
    public const string Attendance = "ATTENDANCE";
    public const string Finance = "FINANCE";
    public const string Copy = "COPY";
    public const string Announcement = "ANNOUNCEMENT";
    public const string Document = "DOCUMENT";
    public const string Contract = "CONTRACT";
}

public sealed record WorkItemView(
    string Id,
    string Tab,
    string Category,
    string BusinessType,
    Guid ResourceId,
    Guid? TaskId,
    string Number,
    string Title,
    string? ApplicantId,
    string ApplicantName,
    string DepartmentName,
    string Status,
    string? CurrentNode,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ProcessedAt,
    DateOnly? DueDate,
    string Urgency,
    string Route,
    bool CanProcess,
    bool IsRead,
    string ActionType,
    DateTimeOffset? DueAt = null);

public sealed record WorkItemSummary(
    int PendingCount,
    int PendingApprovalCount,
    int PendingPersonnelCount,
    int PendingAttendanceCount,
    int PendingFinanceCount,
    int ProcessedCount,
    int InitiatedCount,
    int PendingReadCount,
    int RiskCount);

public sealed record WorkItemResponse(
    IReadOnlyList<WorkItemView> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages,
    WorkItemSummary Summary);

public sealed record WorkItemOverview(
    IReadOnlyList<WorkItemView> Pending,
    IReadOnlyList<WorkItemView> Initiated,
    IReadOnlyList<WorkItemView> Reading,
    WorkItemSummary Summary);

public sealed record WorkItemQuery(
    string? Tab,
    string? BusinessType,
    string? Keyword,
    string? Status,
    string? ApplicantId,
    string? DepartmentId,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? Page,
    int? PageSize);
