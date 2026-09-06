namespace Oa.Api.Domain;

public enum SealStatus { Draft, Approving, Rejected, Approved, Withdrawn, Executed, Out, Returned }

public sealed record SaveSealRequest(
    string Title,
    string DocumentCategory,
    string DocumentName,
    string SealType,
    int Copies,
    bool IsOut,
    DateOnly? OutStartDate = null,
    DateOnly? OutEndDate = null,
    string? OutCustodian = null,
    string Reason = "",
    IReadOnlyList<string>? Attachments = null,
    IReadOnlyList<string>? CopyRecipientIds = null);

public sealed record RegisterSealExecutionRequest(
    int Version,
    DateOnly ExecutedDate,
    string OperatorName,
    string? Notes = null,
    IReadOnlyList<string>? Attachments = null);

public sealed record RegisterSealReturnRequest(
    int Version,
    DateOnly ReturnDate,
    string SealCondition,
    string ReceiverName,
    string? Notes = null,
    IReadOnlyList<string>? Attachments = null);

public sealed record SealExecution(
    DateOnly ExecutedDate,
    string OperatorName,
    string? Notes,
    IReadOnlyList<string> Attachments,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt);

public sealed record SealReturn(
    DateOnly ReturnDate,
    string SealCondition,
    string ReceiverName,
    string? Notes,
    IReadOnlyList<string> Attachments,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt);

public sealed class SealRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Number { get; init; } = string.Empty;
    public string ApplicantId { get; init; } = string.Empty;
    public string ApplicantName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string DocumentCategory { get; init; } = string.Empty;
    public string DocumentName { get; init; } = string.Empty;
    public string SealType { get; init; } = string.Empty;
    public int Copies { get; init; }
    public bool IsOut { get; init; }
    public DateOnly? OutStartDate { get; init; }
    public DateOnly? OutEndDate { get; init; }
    public string? OutCustodian { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> Attachments { get; init; } = [];
    public IReadOnlyList<string> CopyRecipientIds { get; init; } = [];
    public SealStatus Status { get; init; }
    public int Version { get; init; }
    public bool IsDemo { get; init; }
    public Guid? ProcessDefinitionId { get; init; }
    public string? ProcessDefinitionCode { get; init; }
    public int? ProcessDefinitionVersion { get; init; }
    public Guid? CurrentFlowInstanceId { get; init; }
    public string? RiskLevel { get; init; }
    public string? CustodianUserId { get; init; }
    public int MaxOutDays { get; init; } = 7;
    public decimal RiskMetric { get; init; } = 1m;
    public Guid? ConfigVersionId { get; init; }
    public int? ConfigVersionNumber { get; init; }
    public string? ConfigSnapshotJson { get; init; }
    public DateTimeOffset? ConfigResolvedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<SealTask> Tasks { get; init; } = [];
    public IReadOnlyList<FlowInstance> FlowInstances { get; init; } = [];
    public SealExecution? Execution { get; init; }
    public SealReturn? Return { get; init; }
}

public sealed record SealRequestListItem(
    Guid Id,
    string Number,
    string ApplicantId,
    string ApplicantName,
    string DepartmentName,
    string Title,
    string DocumentCategory,
    string DocumentName,
    string SealType,
    int Copies,
    bool IsOut,
    SealStatus Status,
    int Version,
    bool IsDemo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SealTask(
    Guid Id,
    Guid SealRequestId,
    Guid? FlowInstanceId,
    string AssigneeId,
    string AssigneeName,
    string? OriginalAssigneeId,
    string? OriginalAssigneeName,
    Guid? DelegationId,
    int Sequence,
    FlowTaskStatus Status,
    string? Comment,
    DateTimeOffset? ProcessedAt,
    int Version);

public sealed record GenerateSealDemoResult(int Created, int Skipped);
