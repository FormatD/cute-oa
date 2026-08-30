namespace Oa.Api.Domain;

public static class EmploymentContractTypes
{
    public const string FixedTerm = "FIXED_TERM";
    public const string OpenEnded = "OPEN_ENDED";
    public const string ProjectBased = "PROJECT_BASED";
    public static IReadOnlyList<string> All { get; } = [FixedTerm, OpenEnded, ProjectBased];
}

public static class EmploymentContractStatuses
{
    public const string Draft = "DRAFT";
    public const string Active = "ACTIVE";
    public const string Expiring = "EXPIRING";
    public const string Expired = "EXPIRED";
    public const string Terminated = "TERMINATED";
    public const string Superseded = "SUPERSEDED";
}

public sealed record EmploymentContractEventView(Guid Id, string EventType, string Summary, string Reason, string ChangedBy, string ChangedByName, DateTimeOffset CreatedAt);
public sealed record ContractAlertAcknowledgementView(Guid Id, int ThresholdDays, string AcknowledgedBy, string AcknowledgedByName, DateTimeOffset AcknowledgedAt);

public sealed record EmploymentContractView(
    Guid Id,
    string ContractNumber,
    string UserId,
    string EmployeeName,
    string DepartmentId,
    string DepartmentName,
    string PositionName,
    string WorkLocation,
    string ContractType,
    string Status,
    string DisplayStatus,
    DateOnly? SignedDate,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? ProbationStartDate,
    DateOnly? ProbationEndDate,
    string? ProjectDescription,
    IReadOnlyList<string> Attachments,
    string? Notes,
    Guid? RenewalOfId,
    string? RenewalOfNumber,
    int RenewalSequence,
    bool OpenEndedReviewRequired,
    string? OpenEndedReviewReason,
    DateOnly? TerminationDate,
    string? TerminationReason,
    int? DaysUntilEnd,
    int? CurrentAlertThreshold,
    bool CurrentAlertAcknowledged,
    bool IsDemo,
    int Version,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<EmploymentContractEventView> Events,
    IReadOnlyList<ContractAlertAcknowledgementView> AlertAcknowledgements);

public sealed record SaveEmploymentContractRequest(
    string UserId,
    string ContractType,
    DateOnly? SignedDate,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? ProbationStartDate,
    DateOnly? ProbationEndDate,
    string WorkLocation,
    string PositionName,
    string? ProjectDescription,
    IReadOnlyList<string>? Attachments,
    string? Notes,
    int Version,
    string ChangeReason);

public sealed record ActivateEmploymentContractRequest(int Version, string Reason);
public sealed record TerminateEmploymentContractRequest(DateOnly TerminationDate, string Reason, int Version);
public sealed record AcknowledgeContractAlertRequest(int ThresholdDays);
public sealed record ContractAlertSummaryView(int MissingWrittenContract, int Expired, int Within7Days, int Within30Days, int Within60Days, int Within90Days, int OpenEndedReviewRequired, int Unacknowledged, int AtRiskContracts);
public sealed record GenerateContractDemoResult(int Created, int Skipped);
