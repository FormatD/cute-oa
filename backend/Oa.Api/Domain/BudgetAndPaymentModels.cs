using System.Security.Cryptography;
using System.Text;

namespace Oa.Api.Domain;

public enum BudgetStatus
{
    Draft,
    Active,
    Frozen,
    Closed
}

public enum BudgetTransactionType
{
    Reserved,   // 预占
    Released,   // 释放退还
    Consumed,   // 结转实支
    Adjusted    // 手工调整/冲销
}

public enum InvoiceType
{
    VatSpecial,     // 增值税专用发票
    VatNormal,      // 增值税普通发票
    VatElectronic,  // 增值税电子普通发票/数电票
    TrainTicket,    // 铁路车票
    AirItinerary,   // 航空客票行程单
    QuotaInvoice,   // 定额发票
    OtherReceipt    // 其他合规收据
}

public enum InvoiceStatus
{
    Committed,  // 正常报销占用中
    Paid,       // 已付款归档
    Released,   // 已释放/退还
    Cancelled   // 作废
}

public enum PaymentTransactionStatus
{
    Success,
    Failed,
    Cancelled
}

public static class PaymentMethodNames
{
    public const string BankTransfer = "BANK_TRANSFER";
    public const string CorporateAlipay = "CORPORATE_ALIPAY";
    public const string CorporateWechat = "CORPORATE_WECHAT";
    public const string Cheque = "CHEQUE";
    public const string Cash = "CASH";

    public static readonly IReadOnlyList<string> All = [BankTransfer, CorporateAlipay, CorporateWechat, Cheque, Cash];
}

public static class InvoiceFingerprintHelper
{
    public static string ComputeFingerprint(string tenantId, string? invoiceCode, string invoiceNumber)
    {
        var raw = $"{tenantId.Trim()}_{invoiceCode?.Trim() ?? string.Empty}_{invoiceNumber.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }
}

// ---------------- Budget Models ----------------
public sealed class Budget
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TenantId { get; init; } = "demo";
    public string DepartmentId { get; init; } = string.Empty;
    public string? ExpenseCategory { get; init; }
    public string? ProjectId { get; init; }
    public int Year { get; init; }
    public int Month { get; init; } // 0: 年度总预算, 1-12: 月度预算
    public decimal AllocatedAmount { get; set; }
    public decimal CommittedAmount { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal AvailableAmount => AllocatedAmount - CommittedAmount - ActualAmount;
    public decimal ExecutionRate => AllocatedAmount > 0 ? decimal.Round((ActualAmount / AllocatedAmount) * 100, 2) : 0m;
    public BudgetStatus Status { get; set; } = BudgetStatus.Active;
    public int ConcurrencyVersion { get; set; } = 1;
    public string CreatedBy { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record CreateBudgetRequest(
    string DepartmentId,
    string? ExpenseCategory,
    string? ProjectId,
    int Year,
    int Month,
    decimal AllocatedAmount);

public sealed record AdjustBudgetRequest(
    decimal Amount, // 正数调增，负数调减
    string Reason);

public sealed record BudgetCheckRequest(
    string DepartmentId,
    string? ExpenseCategory,
    decimal Amount,
    int? Year = null,
    int? Month = null);

public sealed record BudgetCheckResult(
    bool IsAllowed,
    bool IsExceeded,
    decimal AvailableAmount,
    decimal AllocatedAmount,
    decimal CommittedAmount,
    decimal ActualAmount,
    decimal RequestedAmount,
    string? WarningMessage,
    bool BlockWhenExceeded);

public sealed class BudgetTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TenantId { get; init; } = "demo";
    public Guid BudgetId { get; init; }
    public string BusinessType { get; init; } = string.Empty; // Expense, Purchase
    public Guid BusinessId { get; init; }
    public string BusinessNumber { get; init; } = string.Empty;
    public BudgetTransactionType TransactionType { get; init; }
    public decimal Amount { get; init; }
    public decimal BalanceAfter { get; init; }
    public string? Description { get; init; }
    public string OperatorId { get; init; } = string.Empty;
    public string? ActionKey { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

// ---------------- Invoice Models ----------------
public sealed class ExpenseInvoice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TenantId { get; init; } = "demo";
    public Guid ExpenseClaimId { get; set; }
    public InvoiceType InvoiceType { get; init; } = InvoiceType.VatElectronic;
    public string InvoiceCode { get; init; } = string.Empty;
    public string InvoiceNumber { get; init; } = string.Empty;
    public string InvoiceFingerprint { get; init; } = string.Empty;
    public DateOnly BillingDate { get; init; }
    public decimal AmountWithoutTax { get; init; }
    public decimal TaxRate { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal TotalAmount { get; init; }
    public string? VerificationCode { get; init; }
    public Guid? AttachmentId { get; init; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Committed;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ExpenseInvoiceInput(
    InvoiceType InvoiceType,
    string? InvoiceCode,
    string InvoiceNumber,
    DateOnly BillingDate,
    decimal AmountWithoutTax,
    decimal TaxRate,
    decimal TaxAmount,
    decimal TotalAmount,
    string? VerificationCode,
    Guid? AttachmentId);

public sealed record ValidateInvoiceRequest(
    InvoiceType InvoiceType,
    string? InvoiceCode,
    string InvoiceNumber,
    DateOnly BillingDate,
    decimal TotalAmount,
    Guid? CurrentExpenseClaimId = null);

public sealed record ValidateInvoiceResult(
    bool IsValid,
    string? ConflictClaimNumber,
    string? ConflictApplicantName,
    string? ConflictStatus,
    string? ErrorMessage);

public sealed record ExpenseInvoiceListItem(
    Guid Id,
    Guid ExpenseClaimId,
    string ClaimNumber,
    string ApplicantName,
    InvoiceType InvoiceType,
    string InvoiceCode,
    string InvoiceNumber,
    DateOnly BillingDate,
    decimal AmountWithoutTax,
    decimal TaxRate,
    decimal TaxAmount,
    decimal TotalAmount,
    string? VerificationCode,
    Guid? AttachmentId,
    InvoiceStatus Status,
    DateTimeOffset CreatedAt);

// ---------------- Payment Transaction Models ----------------
public sealed class PaymentTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TenantId { get; init; } = "demo";
    public string BusinessType { get; init; } = string.Empty; // Expense, Purchase
    public Guid BusinessId { get; init; }
    public string BusinessNumber { get; init; } = string.Empty;
    public int Sequence { get; init; } = 1;
    public string BatchTitle { get; init; } = string.Empty;
    public DateOnly PaymentDate { get; init; }
    public string PaymentMethod { get; init; } = "BANK_TRANSFER";
    public string PayerAccount { get; init; } = string.Empty;
    public string PayeeName { get; init; } = string.Empty;
    public string PayeeAccount { get; init; } = string.Empty;
    public string PayeeBank { get; init; } = string.Empty;
    public string TransactionNumber { get; init; } = string.Empty;
    public decimal PaidAmount { get; init; }
    public decimal FeeAmount { get; init; }
    public string? ProofAttachmentId { get; init; }
    public string? Remarks { get; init; }
    public PaymentTransactionStatus Status { get; set; } = PaymentTransactionStatus.Success;
    public string OperatorId { get; init; } = string.Empty;
    public string OperatorName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CreatePaymentTransactionRequest(
    string BatchTitle,
    DateOnly PaymentDate,
    string PaymentMethod,
    string PayerAccount,
    string PayeeName,
    string PayeeAccount,
    string PayeeBank,
    string TransactionNumber,
    decimal PaidAmount,
    decimal FeeAmount,
    string? ProofAttachmentId,
    string? Remarks);

public sealed record PaymentTransactionListItem(
    Guid Id,
    string BusinessType,
    Guid BusinessId,
    string BusinessNumber,
    int Sequence,
    string BatchTitle,
    DateOnly PaymentDate,
    string PaymentMethod,
    string PayerAccount,
    string PayeeName,
    string PayeeAccountMasked,
    string PayeeBank,
    string TransactionNumber,
    decimal PaidAmount,
    decimal FeeAmount,
    string? ProofAttachmentId,
    string? Remarks,
    string Status,
    string OperatorId,
    string OperatorName,
    DateTimeOffset CreatedAt);

// ---------------- Purchase Reconciliation Models ----------------
public sealed record PurchaseReconciliation(
    Guid PurchaseRequestId,
    string PurchaseRequestNumber,
    decimal EstimatedAmount,
    decimal OrderedAmount,
    decimal AcceptedAmount,
    decimal PaidAmount,
    decimal RemainingPayable,
    decimal PrepaymentLimitRate,
    decimal MaxPrepaymentAllowed,
    bool IsAcceptancePassed,
    string PaymentStatus,
    IReadOnlyList<PaymentTransactionListItem> Payments);
