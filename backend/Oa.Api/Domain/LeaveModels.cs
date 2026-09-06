namespace Oa.Api.Domain;

public enum LeaveType { Annual, Personal, Sick, CompTime, Marriage, Maternity, Paternity, Bereavement }
public enum LeavePeriod { FullDay, Morning, Afternoon }
public enum LeaveStatus { Draft, Approving, Rejected, Completed, Withdrawn }
public enum FlowTaskStatus { Pending, Approved, Rejected, Cancelled }

public sealed record CreateLeaveRequest(
    LeaveType Type,
    DateOnly StartDate,
    LeavePeriod StartPeriod,
    DateOnly EndDate,
    LeavePeriod EndPeriod,
    string Reason,
    IReadOnlyList<string>? Attachments = null,
    IReadOnlyList<string>? CopyRecipientIds = null);

public sealed record ApproveTaskRequest(string? Comment);
public sealed record RejectTaskRequest(string Comment);
public sealed record TransferTaskRequest(string AssigneeId, string Comment);
public sealed record AdjustLeaveBalanceRequest(LeaveType Type, int Year, decimal Adjustment, string Reason, int Version);
public sealed record DocumentListQuery(string? Keyword, int? Status, string? ApplicantId, DateOnly? StartDate, DateOnly? EndDate, decimal? MinAmount = null, decimal? MaxAmount = null);

public sealed class LeaveRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Number { get; init; } = string.Empty;
    public string ApplicantId { get; init; } = string.Empty;
    public string ApplicantName { get; init; } = string.Empty;
    public LeaveType Type { get; init; }
    public DateOnly StartDate { get; init; }
    public LeavePeriod StartPeriod { get; init; }
    public DateOnly EndDate { get; init; }
    public LeavePeriod EndPeriod { get; init; }
    public decimal Days { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> Attachments { get; init; } = [];
    public IReadOnlyList<string> CopyRecipientIds { get; init; } = [];
    public int Version { get; set; } = 1;
    public Guid? ProcessDefinitionId { get; set; }
    public string? ProcessDefinitionCode { get; set; }
    public int? ProcessDefinitionVersion { get; set; }
    public Guid? CurrentFlowInstanceId { get; set; }
    public int? BalanceYear { get; set; }
    public Guid? ConfigVersionId { get; set; }
    public int? ConfigVersionNumber { get; set; }
    public string? ConfigSnapshotJson { get; set; }
    public DateTimeOffset? ConfigResolvedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public LeaveStatus Status { get; set; } = LeaveStatus.Draft;
    public List<FlowTask> Tasks { get; } = [];
    public List<FlowInstance> FlowInstances { get; } = [];
}

public sealed class FlowTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LeaveRequestId { get; init; }
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

public sealed record LeaveBalance(decimal Entitled, decimal Frozen, decimal Used, int Year = 0, decimal StatutoryEntitled = 0, decimal Adjustment = 0, int Version = 0)
{
    public decimal Available => Entitled - Frozen - Used;
}

public sealed record ServiceResult<T>(bool IsSuccess, T? Value, string? Error, string? Code = null)
{
    public static ServiceResult<T> Success(T value) => new(true, value, null);
    public static ServiceResult<T> Failure(string error, string code = "VALIDATION_001") => new(false, default, error, code);
}
