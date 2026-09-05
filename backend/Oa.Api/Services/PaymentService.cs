using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class PaymentService
{
    private const string TenantId = "demo";
    private readonly DemoData data;
    private readonly OaDbContext? db;
    private readonly BudgetService budgetService;
    private readonly NotificationService? notifications;
    private readonly FileService? files;
    private readonly List<PaymentTransaction> _memoryTransactions = [];

    public PaymentService(
        DemoData data,
        OaDbContext? db,
        BudgetService budgetService,
        NotificationService? notifications = null,
        FileService? files = null)
    {
        this.data = data;
        this.db = db;
        this.budgetService = budgetService;
        this.notifications = notifications;
        this.files = files;
    }

    public ServiceResult<PaymentTransaction> RegisterExpensePayment(Employee actor, Guid expenseId, CreatePaymentTransactionRequest request)
    {
        if (!data.HasPermission(actor, OaPermissions.ExpensePay))
            return ServiceResult<PaymentTransaction>.Failure("无报销付款登记权限。", "AUTH_002");

        if (request.PaidAmount <= 0)
            return ServiceResult<PaymentTransaction>.Failure("付款金额必须大于 0。");
        if (request.PaymentDate > DateOnly.FromDateTime(DateTime.Today))
            return ServiceResult<PaymentTransaction>.Failure("付款日期不能晚于今天。");
        if (string.IsNullOrWhiteSpace(request.TransactionNumber))
            return ServiceResult<PaymentTransaction>.Failure("银行转账流水号不能为空。");
        if (string.IsNullOrWhiteSpace(request.PayeeAccount))
            return ServiceResult<PaymentTransaction>.Failure("收款账号不能为空。");

        if (db is not null)
        {
            var claim = db.ExpenseClaims.SingleOrDefault(x => x.TenantId == TenantId && x.Id == expenseId);
            if (claim is null)
                return ServiceResult<PaymentTransaction>.Failure("报销单不存在。", "DATA_001");

            if (claim.Status != (int)ExpenseStatus.Approved && claim.Status != (int)ExpenseStatus.Completed)
                return ServiceResult<PaymentTransaction>.Failure("仅审批通过的报销单可登记付款。", "STATE_001");

            var remaining = claim.TotalAmount - claim.PaidTotalAmount;
            if (request.PaidAmount > remaining)
                return ServiceResult<PaymentTransaction>.Failure($"本次付款金额 ({request.PaidAmount:N2} 元) 超过当前剩余待付金额 ({remaining:N2} 元)。", "PAYMENT_AMOUNT_EXCEEDED");

            var duplicateTx = db.PaymentTransactions.Any(t => t.TenantId == TenantId && t.TransactionNumber == request.TransactionNumber.Trim());
            if (duplicateTx)
                return ServiceResult<PaymentTransaction>.Failure($"银行流水号【{request.TransactionNumber.Trim()}】已被使用，禁止重复录入。", "PAYMENT_TX_DUPLICATE");

            Guid? proofGuid = null;
            if (!string.IsNullOrWhiteSpace(request.ProofAttachmentId) && Guid.TryParse(request.ProofAttachmentId, out var parsedProof))
                proofGuid = parsedProof;

            var existingCount = db.PaymentTransactions.Count(t => t.TenantId == TenantId && t.BusinessType == "Expense" && t.BusinessId == expenseId);
            var sequence = existingCount + 1;

            var txRecord = new PaymentTransactionRecord
            {
                TenantId = TenantId,
                BusinessType = "Expense",
                BusinessId = expenseId,
                BusinessNumber = claim.Number,
                Sequence = sequence,
                BatchTitle = string.IsNullOrWhiteSpace(request.BatchTitle) ? $"第 {sequence} 笔款项" : request.BatchTitle.Trim(),
                PaymentDate = request.PaymentDate,
                PaymentMethod = NormalizePaymentMethod(request.PaymentMethod),
                PayerAccount = request.PayerAccount.Trim(),
                PayeeName = request.PayeeName.Trim(),
                PayeeAccount = request.PayeeAccount.Trim(),
                PayeeBank = request.PayeeBank.Trim(),
                TransactionNumber = request.TransactionNumber.Trim(),
                PaidAmount = request.PaidAmount,
                FeeAmount = request.FeeAmount,
                ProofAttachmentId = proofGuid,
                Remarks = request.Remarks?.Trim(),
                Status = "SUCCESS",
                OperatorId = actor.Id,
                OperatorName = actor.Name,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.PaymentTransactions.Add(txRecord);

            claim.PaidTotalAmount += request.PaidAmount;
            if (claim.PaidTotalAmount >= claim.TotalAmount)
            {
                claim.PaymentStatus = "PAID";
                claim.Status = (int)ExpenseStatus.Completed;

                // 关联发票状态更新为 PAID
                var invoices = db.ExpenseInvoices.Where(i => i.TenantId == TenantId && i.ExpenseClaimId == expenseId && i.Status == "COMMITTED").ToList();
                foreach (var inv in invoices) inv.Status = "PAID";
            }
            else
            {
                claim.PaymentStatus = "PARTIALLY_PAID";
            }
            claim.UpdatedAt = DateTimeOffset.UtcNow;

            budgetService.Consume(TenantId, claim.BudgetPoolId, "Expense", claim.Id, claim.Number, request.PaidAmount, actor.Id, $"报销付款登记 第 {sequence} 笔");

            db.AuditLogs.Add(new AuditRecord
            {
                TenantId = TenantId,
                ActorId = actor.Id,
                Action = "EXPENSE_PAYMENT_REGISTERED",
                ResourceType = "ExpenseClaim",
                ResourceId = claim.Id.ToString(),
                Summary = $"登记报销付款 第 {sequence} 笔，实付 {request.PaidAmount:N2} 元，流水号：{request.TransactionNumber.Trim()}"
            });
            db.SaveChanges();

            notifications?.Create(claim.ApplicantId, "EXPENSE_PAID", "报销付款通知", $"{claim.Number} 已完成第 {sequence} 笔付款登记（实付 {request.PaidAmount:N2} 元）", "ExpenseClaim", claim.Id);

            return ServiceResult<PaymentTransaction>.Success(MapToDomain(txRecord));
        }

        // In-memory mode for tests
        var memExisting = _memoryTransactions.Where(t => t.BusinessType == "Expense" && t.BusinessId == expenseId).ToList();
        var memSeq = memExisting.Count + 1;
        var memTx = new PaymentTransaction
        {
            TenantId = TenantId,
            BusinessType = "Expense",
            BusinessId = expenseId,
            BusinessNumber = $"BX-{expenseId.ToString()[..6]}",
            Sequence = memSeq,
            BatchTitle = string.IsNullOrWhiteSpace(request.BatchTitle) ? $"第 {memSeq} 笔款项" : request.BatchTitle.Trim(),
            PaymentDate = request.PaymentDate,
            PaymentMethod = NormalizePaymentMethod(request.PaymentMethod),
            PayerAccount = request.PayerAccount,
            PayeeName = request.PayeeName,
            PayeeAccount = request.PayeeAccount,
            PayeeBank = request.PayeeBank,
            TransactionNumber = request.TransactionNumber,
            PaidAmount = request.PaidAmount,
            FeeAmount = request.FeeAmount,
            Remarks = request.Remarks,
            Status = PaymentTransactionStatus.Success,
            OperatorId = actor.Id,
            OperatorName = actor.Name
        };
        _memoryTransactions.Add(memTx);
        budgetService.Consume(TenantId, null, "Expense", expenseId, memTx.BusinessNumber, request.PaidAmount, actor.Id);
        return ServiceResult<PaymentTransaction>.Success(memTx);
    }

    public ServiceResult<PaymentTransaction> RegisterPurchasePayment(Employee actor, Guid purchaseId, CreatePaymentTransactionRequest request)
    {
        if (!data.HasPermission(actor, OaPermissions.ExpensePay) && !data.HasPermission(actor, OaPermissions.PurchaseManage))
            return ServiceResult<PaymentTransaction>.Failure("无采购付款登记权限。", "AUTH_002");

        if (request.PaidAmount <= 0)
            return ServiceResult<PaymentTransaction>.Failure("付款金额必须大于 0。");
        if (request.PaymentDate > DateOnly.FromDateTime(DateTime.Today))
            return ServiceResult<PaymentTransaction>.Failure("付款日期不能晚于今天。");
        if (string.IsNullOrWhiteSpace(request.TransactionNumber))
            return ServiceResult<PaymentTransaction>.Failure("银行转账流水号不能为空。");

        if (db is not null)
        {
            var purchase = db.PurchaseRequests.SingleOrDefault(x => x.TenantId == TenantId && x.Id == purchaseId);
            if (purchase is null)
                return ServiceResult<PaymentTransaction>.Failure("采购申请不存在。", "DATA_001");

            var status = (PurchaseStatus)purchase.Status;
            if (status != PurchaseStatus.Ordered && status != PurchaseStatus.Received)
                return ServiceResult<PaymentTransaction>.Failure("仅已下单或已验收的采购单可登记付款。", "STATE_001");

            var order = db.PurchaseOrders.SingleOrDefault(o => o.TenantId == TenantId && o.PurchaseRequestId == purchaseId);
            if (order is null)
                return ServiceResult<PaymentTransaction>.Failure("尚未登记采购订单，无法付款。", "STATE_001");

            var contractAmount = order.ActualAmount;
            var isAccepted = db.PurchaseReceipts.Any(r => r.TenantId == TenantId && r.PurchaseRequestId == purchaseId && r.Result == "ALL_ACCEPTED") || status == PurchaseStatus.Received;

            var limitRate = purchase.PrepaymentLimitRate > 0 ? purchase.PrepaymentLimitRate : 0.50m;
            if (!isAccepted)
            {
                var maxPrepayment = decimal.Round(contractAmount * limitRate, 2);
                if (purchase.PaidTotalAmount + request.PaidAmount > maxPrepayment)
                {
                    return ServiceResult<PaymentTransaction>.Failure(
                        $"未完成到货验收前，累计预付款比例不得超过 {limitRate:P0}（最高允许付款 {maxPrepayment:N2} 元，当前已付 {purchase.PaidTotalAmount:N2} 元，本次尝试支付 {request.PaidAmount:N2} 元）。",
                        "PURCHASE_PREPAYMENT_EXCEEDED");
                }
            }

            if (purchase.PaidTotalAmount + request.PaidAmount > contractAmount)
            {
                var diff = (purchase.PaidTotalAmount + request.PaidAmount) - contractAmount;
                return ServiceResult<PaymentTransaction>.Failure(
                    $"累计付款金额不能超过采购合同金额 ({contractAmount:N2} 元)，本次付款将超出 {diff:N2} 元。",
                    "PAYMENT_AMOUNT_EXCEEDED");
            }

            var duplicateTx = db.PaymentTransactions.Any(t => t.TenantId == TenantId && t.TransactionNumber == request.TransactionNumber.Trim());
            if (duplicateTx)
                return ServiceResult<PaymentTransaction>.Failure($"银行流水号【{request.TransactionNumber.Trim()}】已被使用，禁止重复录入。", "PAYMENT_TX_DUPLICATE");

            Guid? proofGuid = null;
            if (!string.IsNullOrWhiteSpace(request.ProofAttachmentId) && Guid.TryParse(request.ProofAttachmentId, out var parsedProof))
                proofGuid = parsedProof;

            var existingCount = db.PaymentTransactions.Count(t => t.TenantId == TenantId && t.BusinessType == "Purchase" && t.BusinessId == purchaseId);
            var sequence = existingCount + 1;

            var txRecord = new PaymentTransactionRecord
            {
                TenantId = TenantId,
                BusinessType = "Purchase",
                BusinessId = purchaseId,
                BusinessNumber = purchase.Number,
                Sequence = sequence,
                BatchTitle = string.IsNullOrWhiteSpace(request.BatchTitle) ? (!isAccepted ? $"首期预付款 ({limitRate:P0})" : $"第 {sequence} 笔款项") : request.BatchTitle.Trim(),
                PaymentDate = request.PaymentDate,
                PaymentMethod = NormalizePaymentMethod(request.PaymentMethod),
                PayerAccount = request.PayerAccount.Trim(),
                PayeeName = request.PayeeName.Trim(),
                PayeeAccount = request.PayeeAccount.Trim(),
                PayeeBank = request.PayeeBank.Trim(),
                TransactionNumber = request.TransactionNumber.Trim(),
                PaidAmount = request.PaidAmount,
                FeeAmount = request.FeeAmount,
                ProofAttachmentId = proofGuid,
                Remarks = request.Remarks?.Trim(),
                Status = "SUCCESS",
                OperatorId = actor.Id,
                OperatorName = actor.Name,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.PaymentTransactions.Add(txRecord);

            purchase.PaidTotalAmount += request.PaidAmount;
            if (purchase.PaidTotalAmount >= contractAmount)
                purchase.PaymentStatus = "PAID";
            else
                purchase.PaymentStatus = "PARTIALLY_PAID";

            purchase.Version++;
            purchase.UpdatedAt = DateTimeOffset.UtcNow;

            budgetService.Consume(TenantId, purchase.BudgetPoolId, "Purchase", purchase.Id, purchase.Number, request.PaidAmount, actor.Id, $"采购付款登记 第 {sequence} 笔");

            db.AuditLogs.Add(new AuditRecord
            {
                TenantId = TenantId,
                ActorId = actor.Id,
                Action = "PURCHASE_PAYMENT_REGISTERED",
                ResourceType = "PurchaseRequest",
                ResourceId = purchase.Id.ToString(),
                Summary = $"登记采购付款 第 {sequence} 笔，实付 {request.PaidAmount:N2} 元，流水号：{request.TransactionNumber.Trim()}"
            });
            db.SaveChanges();

            notifications?.Create(purchase.ApplicantId, "PURCHASE_PAID", "采购付款通知", $"{purchase.Number} 已完成第 {sequence} 笔付款登记（实付 {request.PaidAmount:N2} 元）", "PurchaseRequest", purchase.Id);

            return ServiceResult<PaymentTransaction>.Success(MapToDomain(txRecord));
        }

        var memTx = new PaymentTransaction
        {
            TenantId = TenantId,
            BusinessType = "Purchase",
            BusinessId = purchaseId,
            BusinessNumber = $"PR-{purchaseId.ToString()[..6]}",
            Sequence = 1,
            BatchTitle = request.BatchTitle,
            PaymentDate = request.PaymentDate,
            PaymentMethod = NormalizePaymentMethod(request.PaymentMethod),
            PayerAccount = request.PayerAccount,
            PayeeName = request.PayeeName,
            PayeeAccount = request.PayeeAccount,
            PayeeBank = request.PayeeBank,
            TransactionNumber = request.TransactionNumber,
            PaidAmount = request.PaidAmount,
            FeeAmount = request.FeeAmount,
            Remarks = request.Remarks,
            Status = PaymentTransactionStatus.Success,
            OperatorId = actor.Id,
            OperatorName = actor.Name
        };
        _memoryTransactions.Add(memTx);
        budgetService.Consume(TenantId, null, "Purchase", purchaseId, memTx.BusinessNumber, request.PaidAmount, actor.Id);
        return ServiceResult<PaymentTransaction>.Success(memTx);
    }

    public IReadOnlyList<PaymentTransactionListItem> GetPayments(Employee actor, string businessType, Guid businessId)
    {
        var canViewRawAccount = data.HasPermission(actor, OaPermissions.ExpensePay) || data.HasPermission(actor, OaPermissions.ExpenseAllView);

        if (db is not null)
        {
            var list = db.PaymentTransactions.AsNoTracking()
                .Where(t => t.TenantId == TenantId && t.BusinessType == businessType && t.BusinessId == businessId)
                .OrderBy(t => t.Sequence)
                .ToList();

            return list.Select(t => new PaymentTransactionListItem(
                t.Id,
                t.BusinessType,
                t.BusinessId,
                t.BusinessNumber,
                t.Sequence,
                t.BatchTitle,
                t.PaymentDate,
                t.PaymentMethod,
                t.PayerAccount,
                t.PayeeName,
                canViewRawAccount ? t.PayeeAccount : MaskAccount(t.PayeeAccount),
                t.PayeeBank,
                t.TransactionNumber,
                t.PaidAmount,
                t.FeeAmount,
                t.ProofAttachmentId?.ToString(),
                t.Remarks,
                t.Status,
                t.OperatorId,
                t.OperatorName,
                t.CreatedAt
            )).ToList();
        }

        return _memoryTransactions
            .Where(t => t.BusinessType == businessType && t.BusinessId == businessId)
            .OrderBy(t => t.Sequence)
            .Select(t => new PaymentTransactionListItem(
                t.Id,
                t.BusinessType,
                t.BusinessId,
                t.BusinessNumber,
                t.Sequence,
                t.BatchTitle,
                t.PaymentDate,
                t.PaymentMethod,
                t.PayerAccount,
                t.PayeeName,
                canViewRawAccount ? t.PayeeAccount : MaskAccount(t.PayeeAccount),
                t.PayeeBank,
                t.TransactionNumber,
                t.PaidAmount,
                t.FeeAmount,
                t.ProofAttachmentId,
                t.Remarks,
                t.Status.ToString(),
                t.OperatorId,
                t.OperatorName,
                t.CreatedAt
            )).ToList();
    }

    public string ExportExpensesCsv(Employee actor, string? applicantId, string? departmentId, DateOnly? startDate, DateOnly? endDate, string? paymentStatus)
    {
        if (!data.HasPermission(actor, OaPermissions.ExpensePay))
            throw new UnauthorizedAccessException("无报销对账数据导出权限。");

        if (db is null) return string.Empty;

        var claimsQuery = db.ExpenseClaims.AsNoTracking().Where(c => c.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(applicantId)) claimsQuery = claimsQuery.Where(c => c.ApplicantId == applicantId);
        if (!string.IsNullOrWhiteSpace(departmentId)) claimsQuery = claimsQuery.Where(c => c.DepartmentName == departmentId);
        if (!string.IsNullOrWhiteSpace(paymentStatus)) claimsQuery = claimsQuery.Where(c => c.PaymentStatus == paymentStatus);

        var claims = claimsQuery.OrderByDescending(c => c.CreatedAt).ToList();
        var claimIds = claims.Select(c => c.Id).ToList();

        var invoices = db.ExpenseInvoices.AsNoTracking()
            .Where(i => i.TenantId == TenantId && claimIds.Contains(i.ExpenseClaimId))
            .ToLookup(i => i.ExpenseClaimId);

        var payments = db.PaymentTransactions.AsNoTracking()
            .Where(t => t.TenantId == TenantId && t.BusinessType == "Expense" && claimIds.Contains(t.BusinessId))
            .ToLookup(t => t.BusinessId);

        var sb = new StringBuilder();
        // UTF-8 BOM
        sb.Append('\uFEFF');
        sb.AppendLine("报销单号,申请人,归属部门,单据状态,付款状态,单据总额,已付总额,发票类型,发票代码,发票号码,开票日期,发票税额,价税合计,付款流水号,打款日期,实付金额,经办人");

        var rowCount = 0;
        foreach (var claim in claims)
        {
            var claimInvoices = invoices[claim.Id].ToList();
            var claimPayments = payments[claim.Id].ToList();

            if (claimInvoices.Count == 0 && claimPayments.Count == 0)
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(claim.Number),
                    EscapeCsv(claim.ApplicantName),
                    EscapeCsv(claim.DepartmentName),
                    EscapeCsv(((ExpenseStatus)claim.Status).ToString()),
                    EscapeCsv(claim.PaymentStatus),
                    claim.TotalAmount.ToString("F2", CultureInfo.InvariantCulture),
                    claim.PaidTotalAmount.ToString("F2", CultureInfo.InvariantCulture),
                    "", "", "", "", "", "",
                    "", "", "", ""));
                rowCount++;
            }
            else
            {
                var maxRows = Math.Max(claimInvoices.Count, claimPayments.Count);
                for (var i = 0; i < maxRows; i++)
                {
                    var inv = i < claimInvoices.Count ? claimInvoices[i] : null;
                    var pay = i < claimPayments.Count ? claimPayments[i] : null;

                    sb.AppendLine(string.Join(",",
                        EscapeCsv(claim.Number),
                        EscapeCsv(claim.ApplicantName),
                        EscapeCsv(claim.DepartmentName),
                        EscapeCsv(((ExpenseStatus)claim.Status).ToString()),
                        EscapeCsv(claim.PaymentStatus),
                        claim.TotalAmount.ToString("F2", CultureInfo.InvariantCulture),
                        claim.PaidTotalAmount.ToString("F2", CultureInfo.InvariantCulture),
                        inv is not null ? EscapeCsv(inv.InvoiceType) : "",
                        inv is not null ? EscapeCsv(inv.InvoiceCode) : "",
                        inv is not null ? EscapeCsv(inv.InvoiceNumber) : "",
                        inv is not null ? inv.BillingDate.ToString("yyyy-MM-dd") : "",
                        inv is not null ? inv.TaxAmount.ToString("F2", CultureInfo.InvariantCulture) : "",
                        inv is not null ? inv.TotalAmount.ToString("F2", CultureInfo.InvariantCulture) : "",
                        pay is not null ? EscapeCsv(pay.TransactionNumber) : "",
                        pay is not null ? pay.PaymentDate.ToString("yyyy-MM-dd") : "",
                        pay is not null ? pay.PaidAmount.ToString("F2", CultureInfo.InvariantCulture) : "",
                        pay is not null ? EscapeCsv(pay.OperatorName) : ""));
                    rowCount++;
                }
            }
        }

        db.AuditLogs.Add(new AuditRecord
        {
            TenantId = TenantId,
            ActorId = actor.Id,
            Action = "FINANCE_EXPORT",
            ResourceType = "ExpenseClaim",
            ResourceId = "ALL",
            Summary = $"导出报销发票与付款对账表，共计 {rowCount} 行"
        });
        db.SaveChanges();

        return sb.ToString();
    }

    public string ExportPurchasesCsv(Employee actor, string? applicantId, string? departmentId, DateOnly? startDate, DateOnly? endDate, string? paymentStatus)
    {
        if (!data.HasPermission(actor, OaPermissions.ExpensePay) && !data.HasPermission(actor, OaPermissions.PurchaseManage))
            throw new UnauthorizedAccessException("无采购对账数据导出权限。");

        if (db is null) return string.Empty;

        var purchaseQuery = db.PurchaseRequests.AsNoTracking().Where(p => p.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(applicantId)) purchaseQuery = purchaseQuery.Where(p => p.ApplicantId == applicantId);
        if (!string.IsNullOrWhiteSpace(departmentId)) purchaseQuery = purchaseQuery.Where(p => p.DepartmentName == departmentId);
        if (!string.IsNullOrWhiteSpace(paymentStatus)) purchaseQuery = purchaseQuery.Where(p => p.PaymentStatus == paymentStatus);

        var purchases = purchaseQuery.OrderByDescending(p => p.CreatedAt).ToList();
        var purchaseIds = purchases.Select(p => p.Id).ToList();

        var orders = db.PurchaseOrders.AsNoTracking().Where(o => o.TenantId == TenantId && purchaseIds.Contains(o.PurchaseRequestId)).ToDictionary(o => o.PurchaseRequestId);
        var receipts = db.PurchaseReceipts.AsNoTracking().Where(r => r.TenantId == TenantId && purchaseIds.Contains(r.PurchaseRequestId)).ToDictionary(r => r.PurchaseRequestId);
        var payments = db.PaymentTransactions.AsNoTracking().Where(t => t.TenantId == TenantId && t.BusinessType == "Purchase" && purchaseIds.Contains(t.BusinessId)).ToLookup(t => t.BusinessId);

        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("采购单号,申请人,归属部门,单据状态,付款状态,申请预估金额,采购订单号,成交供应商,合同下单金额,验收入库状态,累计实付金额,待付敞口金额,付款流水号,付款日期,本次实付金额,经办财务");

        var rowCount = 0;
        foreach (var p in purchases)
        {
            var order = orders.GetValueOrDefault(p.Id);
            var receipt = receipts.GetValueOrDefault(p.Id);
            var contractAmount = order?.ActualAmount ?? 0m;
            var remaining = contractAmount - p.PaidTotalAmount;
            var pPayments = payments[p.Id].ToList();

            if (pPayments.Count == 0)
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(p.Number),
                    EscapeCsv(p.ApplicantName),
                    EscapeCsv(p.DepartmentName),
                    EscapeCsv(((PurchaseStatus)p.Status).ToString()),
                    EscapeCsv(p.PaymentStatus),
                    p.EstimatedTotal.ToString("F2", CultureInfo.InvariantCulture),
                    EscapeCsv(order?.OrderNumber ?? ""),
                    EscapeCsv(order?.Supplier ?? ""),
                    contractAmount.ToString("F2", CultureInfo.InvariantCulture),
                    receipt is not null ? EscapeCsv(receipt.Result) : "未验收",
                    p.PaidTotalAmount.ToString("F2", CultureInfo.InvariantCulture),
                    remaining.ToString("F2", CultureInfo.InvariantCulture),
                    "", "", "", ""));
                rowCount++;
            }
            else
            {
                foreach (var pay in pPayments)
                {
                    sb.AppendLine(string.Join(",",
                        EscapeCsv(p.Number),
                        EscapeCsv(p.ApplicantName),
                        EscapeCsv(p.DepartmentName),
                        EscapeCsv(((PurchaseStatus)p.Status).ToString()),
                        EscapeCsv(p.PaymentStatus),
                        p.EstimatedTotal.ToString("F2", CultureInfo.InvariantCulture),
                        EscapeCsv(order?.OrderNumber ?? ""),
                        EscapeCsv(order?.Supplier ?? ""),
                        contractAmount.ToString("F2", CultureInfo.InvariantCulture),
                        receipt is not null ? EscapeCsv(receipt.Result) : "未验收",
                        p.PaidTotalAmount.ToString("F2", CultureInfo.InvariantCulture),
                        remaining.ToString("F2", CultureInfo.InvariantCulture),
                        EscapeCsv(pay.TransactionNumber),
                        pay.PaymentDate.ToString("yyyy-MM-dd"),
                        pay.PaidAmount.ToString("F2", CultureInfo.InvariantCulture),
                        EscapeCsv(pay.OperatorName)));
                    rowCount++;
                }
            }
        }

        db.AuditLogs.Add(new AuditRecord
        {
            TenantId = TenantId,
            ActorId = actor.Id,
            Action = "FINANCE_EXPORT",
            ResourceType = "PurchaseRequest",
            ResourceId = "ALL",
            Summary = $"导出采购四单对账汇总表，共计 {rowCount} 行"
        });
        db.SaveChanges();

        return sb.ToString();
    }

    private static string NormalizePaymentMethod(string method)
    {
        var upper = method.Trim().ToUpperInvariant();
        return PaymentMethodNames.All.Contains(upper) ? upper : PaymentMethodNames.BankTransfer;
    }

    private static string MaskAccount(string account)
    {
        if (string.IsNullOrWhiteSpace(account)) return string.Empty;
        var clean = account.Trim();
        if (clean.Length <= 4) return "****";
        return $"**** **** **** {clean[^4..]}";
    }

    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return $"\"{field}\"";
    }

    private static PaymentTransaction MapToDomain(PaymentTransactionRecord r) => new()
    {
        Id = r.Id,
        TenantId = r.TenantId,
        BusinessType = r.BusinessType,
        BusinessId = r.BusinessId,
        BusinessNumber = r.BusinessNumber,
        Sequence = r.Sequence,
        BatchTitle = r.BatchTitle,
        PaymentDate = r.PaymentDate,
        PaymentMethod = r.PaymentMethod,
        PayerAccount = r.PayerAccount,
        PayeeName = r.PayeeName,
        PayeeAccount = r.PayeeAccount,
        PayeeBank = r.PayeeBank,
        TransactionNumber = r.TransactionNumber,
        PaidAmount = r.PaidAmount,
        FeeAmount = r.FeeAmount,
        ProofAttachmentId = r.ProofAttachmentId?.ToString(),
        Remarks = r.Remarks,
        Status = Enum.TryParse<PaymentTransactionStatus>(r.Status, true, out var st) ? st : PaymentTransactionStatus.Success,
        OperatorId = r.OperatorId,
        OperatorName = r.OperatorName,
        CreatedAt = r.CreatedAt
    };
}
