namespace Oa.Api.Domain;

public enum TravelStatus { Draft, Approving, Rejected, Approved, Withdrawn }

public sealed record TravelItineraryItem(
    string Destination,
    DateOnly StartDate,
    DateOnly EndDate,
    string Transportation,
    string Purpose);

public sealed record SaveTravelRequest(
    string Purpose,
    decimal EstimatedBudget,
    IReadOnlyList<TravelItineraryItem> Itinerary,
    IReadOnlyList<string>? CompanionIds = null,
    IReadOnlyList<string>? Attachments = null,
    IReadOnlyList<string>? CopyRecipientIds = null,
    string? OverStandardReason = null);

public sealed class TravelRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Number { get; init; } = string.Empty;
    public string ApplicantId { get; init; } = string.Empty;
    public string ApplicantName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public int Days { get; init; }
    public decimal EstimatedBudget { get; init; }
    public IReadOnlyList<TravelItineraryItem> Itinerary { get; init; } = [];
    public IReadOnlyList<string> CompanionIds { get; init; } = [];
    public IReadOnlyList<string> CompanionNames { get; init; } = [];
    public IReadOnlyList<string> Attachments { get; init; } = [];
    public IReadOnlyList<string> CopyRecipientIds { get; init; } = [];
    public string? EmployeeRank { get; set; }
    public string? PrimaryCityTier { get; set; }
    public decimal StandardHotelDailyLimit { get; set; }
    public decimal StandardMealDailyAllowance { get; set; }
    public string? StandardTransportation { get; set; }
    public bool IsOverStandard { get; set; }
    public string? OverStandardReason { get; set; }
    public decimal AllowedBudgetCap => (StandardHotelDailyLimit + StandardMealDailyAllowance) * Days * (1 + CompanionIds.Count);
    public int Version { get; init; } = 1;
    public Guid? ProcessDefinitionId { get; set; }
    public string? ProcessDefinitionCode { get; set; }
    public int? ProcessDefinitionVersion { get; set; }
    public Guid? CurrentFlowInstanceId { get; set; }
    public Guid? ConfigVersionId { get; set; }
    public int? ConfigVersionNumber { get; set; }
    public string? ConfigSnapshotJson { get; set; }
    public DateTimeOffset? ConfigResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public TravelStatus Status { get; set; } = TravelStatus.Draft;
    public List<TravelTask> Tasks { get; } = [];
    public List<FlowInstance> FlowInstances { get; } = [];
}

public sealed class TravelTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TravelRequestId { get; init; }
    public Guid? FlowInstanceId { get; init; }
    public string AssigneeId { get; set; } = string.Empty;
    public string AssigneeName { get; set; } = string.Empty;
    public string? OriginalAssigneeId { get; init; }
    public string? OriginalAssigneeName { get; init; }
    public Guid? DelegationId { get; init; }
    public int Sequence { get; init; }
    public FlowTaskStatus Status { get; set; } = FlowTaskStatus.Pending;
    public string? Comment { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
