namespace Oa.Api.Domain;

public enum ExpenseStatus { Draft, Approving, Rejected, Approved, Completed, Withdrawn }
public sealed record ExpenseItem(DateOnly ExpenseDate, string Category, decimal Amount, string Description, string? ReceiptNumber, IReadOnlyList<string>? Attachments = null);
public sealed record CreateExpenseClaim(string? Project, string PayeeAccountName, string PayeeAccount, string? BankName, string? Description, IReadOnlyList<ExpenseItem> Items, IReadOnlyList<string>? CopyRecipientIds = null, Guid? TravelRequestId = null, IReadOnlyList<ExpenseInvoiceInput>? Invoices = null);
public sealed record RegisterPaymentRequest(DateOnly PaymentDate, string PaymentMethod, string TransactionNumber, decimal PaidAmount, string ProofFile, string? BatchTitle = null, decimal FeeAmount = 0m, string? Remarks = null);

public sealed class ExpenseClaim
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Number { get; init; } = string.Empty;
    public string ApplicantId { get; init; } = string.Empty;
    public string ApplicantName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public Guid? TravelRequestId { get; init; }
    public string? TravelRequestNumber { get; init; }
    public string PayeeAccountName { get; init; } = string.Empty;
    public string PayeeAccount { get; init; } = string.Empty;
    public string? BankName { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<ExpenseItem> Items { get; init; } = [];
    public IReadOnlyList<string> CopyRecipientIds { get; init; } = [];
    public decimal TotalAmount { get; init; }
    public string? Project { get; set; }
    public Guid? BudgetPoolId { get; set; }
    public bool IsOverBudget { get; set; }
    public decimal OverBudgetAmount { get; set; }
    public string? OverBudgetPolicySnapshot { get; set; }
    public string PaymentStatus { get; set; } = "UNPAID";
    public decimal PaidTotalAmount { get; set; }
    public int InvoiceCount { get; set; }
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
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Draft;
    public List<ExpenseTask> Tasks { get; } = [];
    public List<FlowInstance> FlowInstances { get; } = [];
    public List<ExpenseInvoice> Invoices { get; set; } = [];
    public List<PaymentTransaction> PaymentTransactions { get; set; } = [];
    public PaymentRecord? Payment { get; set; }
}

public sealed class ExpenseTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ExpenseClaimId { get; init; }
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

public sealed record PaymentRecord(DateOnly PaymentDate, string PaymentMethod, string TransactionNumber, decimal PaidAmount, string ProofFile, string OperatorId);
