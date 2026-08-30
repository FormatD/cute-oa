namespace Oa.Api.Domain;

public enum FlowInstanceStatus { Running, Completed, Rejected, Withdrawn }
public enum FlowActionType { Submitted, Approved, Rejected, Transferred, Withdrawn }

public sealed class FlowInstance
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string BusinessType { get; init; } = string.Empty;
    public Guid BusinessId { get; init; }
    public string BusinessNumber { get; init; } = string.Empty;
    public string ApplicantId { get; init; } = string.Empty;
    public Guid ProcessDefinitionId { get; init; }
    public string ProcessDefinitionCode { get; init; } = string.Empty;
    public int ProcessDefinitionVersion { get; init; }
    public int Attempt { get; init; }
    public FlowInstanceStatus Status { get; set; } = FlowInstanceStatus.Running;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<FlowAction> Actions { get; } = [];
}

public sealed class FlowAction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FlowInstanceId { get; init; }
    public Guid? TaskId { get; init; }
    public int? Sequence { get; init; }
    public FlowActionType Action { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string ActorName { get; init; } = string.Empty;
    public string? FromAssigneeId { get; init; }
    public string? FromAssigneeName { get; init; }
    public string? ToAssigneeId { get; init; }
    public string? ToAssigneeName { get; init; }
    public string? Comment { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
