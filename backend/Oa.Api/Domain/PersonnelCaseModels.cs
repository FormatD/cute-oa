namespace Oa.Api.Domain;

public static class PersonnelCaseTypes
{
    public const string Onboarding = "ONBOARDING";
    public const string Regularization = "REGULARIZATION";
    public const string Transfer = "TRANSFER";
    public const string Offboarding = "OFFBOARDING";
    public static IReadOnlyList<string> All { get; } = [Onboarding, Regularization, Transfer, Offboarding];
}

public static class PersonnelCaseStatuses
{
    public const string Open = "OPEN";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public static IReadOnlyList<string> All { get; } = [Open, Completed, Cancelled];
}

public static class PersonnelTaskStatuses
{
    public const string Pending = "PENDING";
    public const string Completed = "COMPLETED";
    public const string Waived = "WAIVED";
    public static IReadOnlyList<string> All { get; } = [Pending, Completed, Waived];
}

public sealed record PersonnelCaseTaskView(
    Guid Id,
    string Code,
    string Title,
    string Category,
    bool Required,
    string AssigneeId,
    string AssigneeName,
    DateOnly DueDate,
    string Status,
    bool IsOverdue,
    string? CompletionNote,
    string? CompletedBy,
    string? CompletedByName,
    DateTimeOffset? CompletedAt,
    int SortOrder,
    int Version);

public sealed record PersonnelCaseView(
    Guid Id,
    string Number,
    string UserId,
    string EmployeeName,
    string DepartmentName,
    string Type,
    string Title,
    DateOnly EffectiveDate,
    string Status,
    string OwnerId,
    string OwnerName,
    string? Notes,
    int TaskCount,
    int ResolvedTaskCount,
    int OverdueTaskCount,
    int Version,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CompletedBy,
    string? CompletedByName,
    DateTimeOffset? CompletedAt,
    string? CompletionComment,
    string? CancelledBy,
    string? CancelledByName,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    IReadOnlyList<PersonnelCaseTaskView> Tasks);

public sealed record CreatePersonnelCaseRequest(
    string UserId,
    string Type,
    DateOnly EffectiveDate,
    string OwnerId,
    string? Notes);

public sealed record UpdatePersonnelCaseTaskRequest(
    string AssigneeId,
    DateOnly DueDate,
    string Status,
    string? CompletionNote,
    int Version);

public sealed record CompletePersonnelCaseRequest(int Version, string Comment);
public sealed record CancelPersonnelCaseRequest(int Version, string Reason);
