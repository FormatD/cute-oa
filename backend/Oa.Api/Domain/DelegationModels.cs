namespace Oa.Api.Domain;

public enum FlowDelegationStatus { Active, Cancelled }

public sealed record CreateFlowDelegationRequest(string DelegateId, string BusinessType, DateTimeOffset StartAt, DateTimeOffset EndAt, string Reason);
public sealed record FlowDelegationView(
    Guid Id,
    string OwnerId,
    string OwnerName,
    string DelegateId,
    string DelegateName,
    string BusinessType,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Reason,
    FlowDelegationStatus Status,
    bool IsEffective,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt);
