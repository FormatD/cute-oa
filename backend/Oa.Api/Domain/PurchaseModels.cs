namespace Oa.Api.Domain;

public enum PurchaseStatus { Draft, Approving, Rejected, Approved, Withdrawn, Ordered, Received }

public sealed record SavePurchaseItem(
    string Category,
    string Name,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal EstimatedUnitPrice,
    string? Remark);

public sealed record PurchaseItem(
    string Category,
    string Name,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal EstimatedUnitPrice,
    decimal EstimatedAmount,
    string? Remark);

public sealed record SavePurchaseRequest(
    string Title,
    string Purpose,
    DateOnly RequiredDate,
    string? SuggestedSupplier,
    IReadOnlyList<SavePurchaseItem> Items,
    IReadOnlyList<string>? Attachments = null,
    IReadOnlyList<string>? CopyRecipientIds = null);

public sealed record RegisterPurchaseOrderRequest(
    int Version,
    string Supplier,
    string OrderNumber,
    decimal ActualAmount,
    DateOnly OrderDate,
    DateOnly ExpectedDeliveryDate,
    string? Notes,
    IReadOnlyList<string>? Attachments = null);

public sealed record ReceivePurchaseRequest(
    int Version,
    DateOnly ReceivedDate,
    string Result,
    string Notes,
    IReadOnlyList<string>? Attachments = null);

public sealed record PurchaseOrder(
    string Supplier,
    string OrderNumber,
    decimal ActualAmount,
    DateOnly OrderDate,
    DateOnly ExpectedDeliveryDate,
    string? Notes,
    IReadOnlyList<string> Attachments,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt);

public sealed record PurchaseReceipt(
    DateOnly ReceivedDate,
    string Result,
    string Notes,
    IReadOnlyList<string> Attachments,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt);

public sealed class PurchaseRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Number { get; init; } = string.Empty;
    public string ApplicantId { get; init; } = string.Empty;
    public string ApplicantName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public DateOnly RequiredDate { get; init; }
    public string? SuggestedSupplier { get; init; }
    public IReadOnlyList<PurchaseItem> Items { get; init; } = [];
    public decimal EstimatedTotal { get; init; }
    public IReadOnlyList<string> Attachments { get; init; } = [];
    public IReadOnlyList<string> CopyRecipientIds { get; init; } = [];
    public PurchaseStatus Status { get; init; }
    public int Version { get; init; }
    public bool IsDemo { get; init; }
    public Guid? ProcessDefinitionId { get; init; }
    public string? ProcessDefinitionCode { get; init; }
    public int? ProcessDefinitionVersion { get; init; }
    public Guid? CurrentFlowInstanceId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<PurchaseTask> Tasks { get; init; } = [];
    public IReadOnlyList<FlowInstance> FlowInstances { get; init; } = [];
    public PurchaseOrder? Order { get; init; }
    public PurchaseReceipt? Receipt { get; init; }
}

public sealed record PurchaseRequestListItem(
    Guid Id,
    string Number,
    string ApplicantId,
    string ApplicantName,
    string DepartmentName,
    string Title,
    DateOnly RequiredDate,
    int ItemCount,
    decimal EstimatedTotal,
    PurchaseStatus Status,
    int Version,
    bool IsDemo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PurchaseTask(
    Guid Id,
    Guid PurchaseRequestId,
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

public sealed record GeneratePurchaseDemoResult(int Created, int Skipped);
