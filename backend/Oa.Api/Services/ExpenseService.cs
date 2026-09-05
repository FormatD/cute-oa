using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class ExpenseService
{
    private const string TenantId = "demo";
    private readonly DemoData data;
    private readonly OaDbContext? db;
    private readonly NotificationService? notifications;
    private readonly FileService? files;
    private readonly IProcessRouter processRouter;
    private readonly FlowInstanceService flowInstances;
    private readonly FlowCopyService copyRecipients;
    private readonly TravelService? travelRequests;
    private readonly List<ExpenseClaim> _claims;
    private static readonly HashSet<string> AllowedCategories = ["交通", "住宿", "餐饮招待", "办公", "通讯", "培训", "其他"];

    public ExpenseService(DemoData data, OaDbContext? db = null, NotificationService? notifications = null, FileService? files = null, IProcessRouter? processRouter = null, FlowInstanceService? flowInstances = null, FlowCopyService? copyRecipients = null, TravelService? travelRequests = null)
    {
        this.data = data;
        this.db = db;
        this.notifications = notifications;
        this.files = files;
        this.processRouter = processRouter ?? new DefaultProcessRouter(data);
        this.flowInstances = flowInstances ?? new FlowInstanceService(db);
        this.copyRecipients = copyRecipients ?? new FlowCopyService(data, db);
        this.travelRequests = travelRequests;
        _claims = db is null ? [] : LoadClaims(db, this.flowInstances, this.copyRecipients);
    }

    public IReadOnlyList<ExpenseClaim> List(Employee actor, DocumentListQuery? query = null) => _claims
        .Where(item => data.CanView(actor, data.GetEmployee(item.ApplicantId), "Expense"))
        .Where(item => string.IsNullOrWhiteSpace(query?.Keyword) || item.Number.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) || (item.Description?.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) ?? false) || item.ApplicantName.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase))
        .Where(item => query?.Status is null || (int)item.Status == query.Status)
        .Where(item => string.IsNullOrWhiteSpace(query?.ApplicantId) || item.ApplicantId == query.ApplicantId)
        .Where(item => query?.StartDate is null || item.Items.Any(detail => detail.ExpenseDate >= query.StartDate))
        .Where(item => query?.EndDate is null || item.Items.Any(detail => detail.ExpenseDate <= query.EndDate))
        .Where(item => query?.MinAmount is null || item.TotalAmount >= query.MinAmount)
        .Where(item => query?.MaxAmount is null || item.TotalAmount <= query.MaxAmount)
        .OrderByDescending(item => item.CreatedAt)
        .ToList();
    public ServiceResult<ExpenseClaim> Get(Employee actor, Guid id)
    {
        var item = _claims.SingleOrDefault(claim => claim.Id == id);
        if (item is null) return ServiceResult<ExpenseClaim>.Failure("报销单不存在。", "DATA_001");
        if (!data.CanView(actor, data.GetEmployee(item.ApplicantId), "Expense") && item.Tasks.All(task => task.AssigneeId != actor.Id) && !copyRecipients.CanView(actor, "Expense", item.Id))
            return ServiceResult<ExpenseClaim>.Failure("无权查看该报销单。", "AUTH_002");
        return ServiceResult<ExpenseClaim>.Success(item);
    }
    public IReadOnlyList<ExpenseTask> GetPendingTasks(Employee actor) => _claims
        .Where(item => item.Status == ExpenseStatus.Approving)
        .SelectMany(item => item.Tasks.Where(task => task.Status == FlowTaskStatus.Pending && task.Sequence == item.Tasks.Where(candidate => candidate.Status == FlowTaskStatus.Pending).Min(candidate => candidate.Sequence)))
        .Where(item => item.AssigneeId == actor.Id)
        .OrderBy(item => item.Sequence)
        .ToList();
    public IReadOnlyList<ExpenseTask> GetProcessedTasks(Employee actor) => _claims.SelectMany(item => item.Tasks).Where(item => item.AssigneeId == actor.Id && item.Status is FlowTaskStatus.Approved or FlowTaskStatus.Rejected).OrderByDescending(item => item.ProcessedAt).ToList();

    public ServiceResult<ExpenseClaim> CreateDraft(Employee actor, CreateExpenseClaim request)
    {
        if (request.Items.Count == 0) return ServiceResult<ExpenseClaim>.Failure("至少需要一条费用明细。", "EXP_001");
        if (string.IsNullOrWhiteSpace(request.PayeeAccountName) || string.IsNullOrWhiteSpace(request.PayeeAccount)) return ServiceResult<ExpenseClaim>.Failure("请填写收款账户信息。");
        if (request.Items.Any(item => item.Amount <= 0 || item.Amount != decimal.Round(item.Amount, 2) || !AllowedCategories.Contains(item.Category))) return ServiceResult<ExpenseClaim>.Failure("费用类别或金额不合法。");
        if (request.Items.Any(item => item.Attachments is null || item.Attachments.Count == 0)) return ServiceResult<ExpenseClaim>.Failure("每条费用明细都需要合规票据或凭证。", "EXP_002");
        if (files is not null && !files.AreOwnedBy(actor, request.Items.SelectMany(item => item.Attachments!))) return ServiceResult<ExpenseClaim>.Failure("附件不存在或不属于当前用户。", "FILE_005");
        var copies = copyRecipients.Validate(actor, request.CopyRecipientIds);
        if (!copies.IsSuccess) return ServiceResult<ExpenseClaim>.Failure(copies.Error!, copies.Code!);
        if (request.Items.Any(item => item.ExpenseDate > DateOnly.FromDateTime(DateTime.Today) || item.ExpenseDate < DateOnly.FromDateTime(DateTime.Today).AddDays(-180))) return ServiceResult<ExpenseClaim>.Failure("费用日期超出允许范围。", "EXP_004");
        var travel = ValidateTravelReference(actor, request.TravelRequestId);
        if (!travel.IsSuccess) return ServiceResult<ExpenseClaim>.Failure(travel.Error!, travel.Code!);

        var total = request.Items.Sum(item => item.Amount);
        var configRecord = BusinessConfigurationDefaults.ResolveEffectiveConfig(db, ConfigurationDomains.Expense, "ExpensePolicy");
        var item = new ExpenseClaim
        {
            Number = $"BX-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}", ApplicantId = actor.Id, ApplicantName = actor.Name, DepartmentName = actor.DepartmentName,
            TravelRequestId = travel.Value?.Id, TravelRequestNumber = travel.Value?.Number, PayeeAccountName = request.PayeeAccountName.Trim(), PayeeAccount = request.PayeeAccount.Trim(), BankName = request.BankName?.Trim(), Description = request.Description?.Trim(), Items = request.Items, CopyRecipientIds = copies.Value!, TotalAmount = total,
            ConfigVersionId = configRecord?.Id, ConfigVersionNumber = configRecord?.Version, ConfigSnapshotJson = configRecord?.ContentJson, ConfigResolvedAt = configRecord is not null ? DateTimeOffset.UtcNow : null
        };
        _claims.Add(item);
        Persist(item);
        Audit(actor, "EXPENSE_DRAFT_CREATED", item, "创建报销草稿");
        return ServiceResult<ExpenseClaim>.Success(item);
    }

    public ServiceResult<ExpenseClaim> Submit(Employee actor, Guid id)
    {
        var item = _claims.SingleOrDefault(claim => claim.Id == id);
        if (item is null || item.ApplicantId != actor.Id) return ServiceResult<ExpenseClaim>.Failure("报销单不存在或无权限。", "DATA_001");
        if (item.Status is not ExpenseStatus.Draft and not ExpenseStatus.Rejected) return ServiceResult<ExpenseClaim>.Failure("当前状态不允许提交。", "STATE_001");
        var receiptNumbers = item.Items.Where(detail => !string.IsNullOrWhiteSpace(detail.ReceiptNumber)).Select(detail => (detail.Category, Number: detail.ReceiptNumber!.Trim())).ToList();
        if (receiptNumbers.GroupBy(detail => detail).Any(group => group.Count() > 1) ||
            _claims.Where(claim => claim.Id != item.Id && claim.Status is ExpenseStatus.Approving or ExpenseStatus.Approved or ExpenseStatus.Completed)
                .SelectMany(claim => claim.Items).Any(detail => !string.IsNullOrWhiteSpace(detail.ReceiptNumber) && receiptNumbers.Contains((detail.Category, detail.ReceiptNumber.Trim()))))
            return ServiceResult<ExpenseClaim>.Failure("存在已用于有效报销单的同类票据号。", "EXP_003");

        var configRecord = BusinessConfigurationDefaults.ResolveEffectiveConfig(db, ConfigurationDomains.Expense, "ExpensePolicy");
        var policy = configRecord is not null
            ? JsonSerializer.Deserialize<ExpensePolicyConfig>(configRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions)
            : BusinessConfigurationDefaults.CreateDefaultExpensePolicy();

        if (policy is not null)
        {
            foreach (var detail in item.Items)
            {
                var catRule = policy.Categories.FirstOrDefault(c => c.Name.Equals(detail.Category, StringComparison.OrdinalIgnoreCase));
                if (catRule is not null && !catRule.IsEnabled)
                    return ServiceResult<ExpenseClaim>.Failure($"费用类别【{detail.Category}】已被系统停用，无法提交申请。", "EXP_005");

                if (catRule is not null && catRule.RequiresReceipt && (detail.Attachments is null || detail.Attachments.Count == 0))
                    return ServiceResult<ExpenseClaim>.Failure($"费用明细【{detail.Description}】属于【{detail.Category}】，必须上传发票凭证。", "EXP_002");

                if (catRule is not null && catRule.SingleLimit > 0 && detail.Amount > catRule.SingleLimit)
                {
                    if (catRule.BlockWhenExceeded)
                        return ServiceResult<ExpenseClaim>.Failure($"费用明细【{detail.Description}】金额超出单笔限额 {catRule.SingleLimit:N2} 元，禁止提交。", "EXP_006");
                    if (catRule.RequiresReasonWhenExceeded && string.IsNullOrWhiteSpace(item.Description))
                        return ServiceResult<ExpenseClaim>.Failure($"费用明细【{detail.Description}】超出单笔限额 {catRule.SingleLimit:N2} 元，必须填写说明原因。", "EXP_007");
                }
            }
        }

        var route = processRouter.Resolve("Expense", actor, item.TotalAmount);
        if (!route.IsSuccess) return ServiceResult<ExpenseClaim>.Failure(route.Error!, route.Code!);
        item.Status = ExpenseStatus.Approving;
        item.Tasks.Clear();
        item.ProcessDefinitionId = route.Value!.DefinitionId;
        item.ProcessDefinitionCode = route.Value.Code;
        item.ProcessDefinitionVersion = route.Value.Version;
        if (configRecord is not null)
        {
            item.ConfigVersionId = configRecord.Id;
            item.ConfigVersionNumber = configRecord.Version;
            item.ConfigSnapshotJson = configRecord.ContentJson;
            item.ConfigResolvedAt = DateTimeOffset.UtcNow;
        }
        var instance = flowInstances.Start(item.FlowInstances, "Expense", item.Id, item.Number, actor, route.Value);
        item.CurrentFlowInstanceId = instance.Id;
        foreach (var (resolvedApprover, sequence) in route.Value.Approvers.Select((value, index) => (value, index + 1)))
        {
            item.Tasks.Add(new ExpenseTask { ExpenseClaimId = item.Id, FlowInstanceId = instance.Id, AssigneeId = resolvedApprover.Assignee.Id, AssigneeName = resolvedApprover.Assignee.Name, OriginalAssigneeId = resolvedApprover.DelegationId is null ? null : resolvedApprover.OriginalApprover.Id, OriginalAssigneeName = resolvedApprover.DelegationId is null ? null : resolvedApprover.OriginalApprover.Name, DelegationId = resolvedApprover.DelegationId, Sequence = sequence });
        }
        Persist(item);
        flowInstances.RegisterTasks(instance, "Expense", item.Tasks.Select(task => new ResolvedFlowTask(task.Id, task.Sequence, route.Value.Approvers[task.Sequence - 1])).ToList());
        Audit(actor, "EXPENSE_SUBMITTED", item, "提交报销审批");
        if (item.Tasks.OrderBy(task => task.Sequence).FirstOrDefault() is { } firstTask)
            notifications?.Create(firstTask.AssigneeId, "TODO_CREATED", "新增报销审批待办", $"{item.ApplicantName} 提交了 {item.Number}", "ExpenseClaim", item.Id);
        return ServiceResult<ExpenseClaim>.Success(item);
    }

    public ServiceResult<ExpenseClaim> Update(Employee actor, Guid id, int expectedVersion, CreateExpenseClaim request)
    {
        var original = _claims.SingleOrDefault(item => item.Id == id);
        if (original is null || original.ApplicantId != actor.Id) return ServiceResult<ExpenseClaim>.Failure("报销单不存在或无权限。", "DATA_001");
        if (original.Status is not (ExpenseStatus.Draft or ExpenseStatus.Rejected)) return ServiceResult<ExpenseClaim>.Failure("当前状态不允许编辑。", "STATE_001");
        if (original.Version != expectedVersion) return ServiceResult<ExpenseClaim>.Failure("单据已被更新，请刷新后重试。", "CONCURRENCY_001");
        if (request.Items.Count == 0) return ServiceResult<ExpenseClaim>.Failure("至少需要一条费用明细。", "EXP_001");
        if (string.IsNullOrWhiteSpace(request.PayeeAccountName) || string.IsNullOrWhiteSpace(request.PayeeAccount)) return ServiceResult<ExpenseClaim>.Failure("请填写收款账户信息。");
        if (request.Items.Any(item => item.Amount <= 0 || item.Amount != decimal.Round(item.Amount, 2) || !AllowedCategories.Contains(item.Category))) return ServiceResult<ExpenseClaim>.Failure("费用类别或金额不合法。");
        if (request.Items.Any(item => item.Attachments is null || item.Attachments.Count == 0)) return ServiceResult<ExpenseClaim>.Failure("每条费用明细都需要合规票据或凭证。", "EXP_002");
        if (files is not null && !files.AreOwnedBy(actor, request.Items.SelectMany(item => item.Attachments!))) return ServiceResult<ExpenseClaim>.Failure("附件不存在或不属于当前用户。", "FILE_005");
        var copies = copyRecipients.Validate(actor, request.CopyRecipientIds);
        if (!copies.IsSuccess) return ServiceResult<ExpenseClaim>.Failure(copies.Error!, copies.Code!);
        if (request.Items.Any(item => item.ExpenseDate > DateOnly.FromDateTime(DateTime.Today) || item.ExpenseDate < DateOnly.FromDateTime(DateTime.Today).AddDays(-180))) return ServiceResult<ExpenseClaim>.Failure("费用日期超出允许范围。", "EXP_004");
        var travel = ValidateTravelReference(actor, request.TravelRequestId);
        if (!travel.IsSuccess) return ServiceResult<ExpenseClaim>.Failure(travel.Error!, travel.Code!);
        var updated = new ExpenseClaim { Id = original.Id, Number = original.Number, ApplicantId = original.ApplicantId, ApplicantName = original.ApplicantName, DepartmentName = original.DepartmentName, TravelRequestId = travel.Value?.Id, TravelRequestNumber = travel.Value?.Number, PayeeAccountName = request.PayeeAccountName.Trim(), PayeeAccount = request.PayeeAccount.Trim(), BankName = request.BankName?.Trim(), Description = request.Description?.Trim(), Items = request.Items, CopyRecipientIds = copies.Value!, TotalAmount = request.Items.Sum(item => item.Amount), Version = original.Version + 1, Status = original.Status, ProcessDefinitionId = original.ProcessDefinitionId, ProcessDefinitionCode = original.ProcessDefinitionCode, ProcessDefinitionVersion = original.ProcessDefinitionVersion, CurrentFlowInstanceId = original.CurrentFlowInstanceId };
        updated.Tasks.AddRange(original.Tasks);
        updated.FlowInstances.AddRange(original.FlowInstances);
        updated.Payment = original.Payment;
        _claims[_claims.IndexOf(original)] = updated;
        Persist(updated);
        Audit(actor, "EXPENSE_UPDATED", updated, "编辑报销申请");
        return ServiceResult<ExpenseClaim>.Success(updated);
    }

    public ServiceResult<bool> Delete(Employee actor, Guid id)
    {
        var item = _claims.SingleOrDefault(claim => claim.Id == id);
        if (item is null || item.ApplicantId != actor.Id) return ServiceResult<bool>.Failure("报销单不存在或无权限。", "DATA_001");
        if (item.Status is not (ExpenseStatus.Draft or ExpenseStatus.Rejected)) return ServiceResult<bool>.Failure("当前状态不允许删除。", "STATE_001");
        _claims.Remove(item);
        if (db is not null)
        {
            copyRecipients.DeleteUnavailable("Expense", item.Id);
            db.ExpenseItems.Where(detail => detail.ExpenseClaimId == item.Id).ExecuteDelete();
            db.ExpenseTasks.Where(task => task.ExpenseClaimId == item.Id).ExecuteDelete();
            db.Payments.Where(payment => payment.ExpenseClaimId == item.Id).ExecuteDelete();
            db.ExpenseClaims.Where(claim => claim.Id == item.Id).ExecuteDelete();
            db.SaveChanges();
        }
        Audit(actor, "EXPENSE_DELETED", item, "删除报销申请");
        return ServiceResult<bool>.Success(true);
    }

    public ServiceResult<ExpenseClaim> Approve(Employee actor, Guid taskId, string? comment)
    {
        var found = _claims.SelectMany(claim => claim.Tasks.Select(task => (claim, task))).SingleOrDefault(pair => pair.task.Id == taskId);
        if (found.task is null) return ServiceResult<ExpenseClaim>.Failure("审批任务不存在。", "DATA_001");
        if (found.task.AssigneeId != actor.Id || found.task.Status != FlowTaskStatus.Pending) return ServiceResult<ExpenseClaim>.Failure("当前用户不能处理该审批任务。", "AUTH_002");
        if (found.claim.Tasks.Any(task => task.Sequence < found.task.Sequence && task.Status != FlowTaskStatus.Approved)) return ServiceResult<ExpenseClaim>.Failure("前序审批尚未完成。", "STATE_001");
        found.task.Status = FlowTaskStatus.Approved; found.task.Comment = comment?.Trim(); found.task.ProcessedAt = DateTimeOffset.UtcNow;
        var instance = flowInstances.Current(found.claim.FlowInstances, found.task.FlowInstanceId);
        if (instance is not null) flowInstances.Record(instance, FlowActionType.Approved, actor, found.task.Id, found.task.Sequence, found.task.Comment);
        if (found.claim.Tasks.All(task => task.Status == FlowTaskStatus.Approved))
        {
            found.claim.Status = ExpenseStatus.Approved;
            if (instance is not null) flowInstances.Complete(instance);
        }
        Persist(found.claim);
        var activatedCopies = found.claim.Status == ExpenseStatus.Approved ? copyRecipients.Activate("Expense", found.claim.Id, found.claim.Status.ToString()) : [];
        Audit(actor, "EXPENSE_APPROVED", found.claim, comment?.Trim() ?? "同意报销");
        if (found.claim.Status == ExpenseStatus.Approved)
        {
            notifications?.Create(found.claim.ApplicantId, "EXPENSE_APPROVED", "报销申请已审批通过", $"{found.claim.Number} 等待付款", "ExpenseClaim", found.claim.Id);
            foreach (var recipient in activatedCopies) notifications?.Create(recipient.Id, "FLOW_COPY", "报销抄送事项已审批完成", $"{found.claim.ApplicantName} 的 {found.claim.Number} 已审批完成", "ExpenseClaim", found.claim.Id);
        }
        else if (found.claim.Tasks.FirstOrDefault(task => task.Sequence == found.task.Sequence + 1 && task.Status == FlowTaskStatus.Pending) is { } next)
            notifications?.Create(next.AssigneeId, "TODO_CREATED", "新增报销审批待办", $"{found.claim.Number} 等待你审批", "ExpenseClaim", found.claim.Id);
        return ServiceResult<ExpenseClaim>.Success(found.claim);
    }

    public ServiceResult<ExpenseClaim> Reject(Employee actor, Guid taskId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return ServiceResult<ExpenseClaim>.Failure("驳回必须填写意见。");
        var found = _claims.SelectMany(claim => claim.Tasks.Select(task => (claim, task))).SingleOrDefault(pair => pair.task.Id == taskId);
        if (found.task is null) return ServiceResult<ExpenseClaim>.Failure("审批任务不存在。", "DATA_001");
        if (found.task.AssigneeId != actor.Id || found.task.Status != FlowTaskStatus.Pending) return ServiceResult<ExpenseClaim>.Failure("当前用户不能处理该审批任务。", "AUTH_002");
        found.task.Status = FlowTaskStatus.Rejected;
        found.task.Comment = comment.Trim();
        found.task.ProcessedAt = DateTimeOffset.UtcNow;
        foreach (var task in found.claim.Tasks.Where(task => task.Id != found.task.Id && task.Status == FlowTaskStatus.Pending)) task.Status = FlowTaskStatus.Cancelled;
        found.claim.Status = ExpenseStatus.Rejected;
        if (flowInstances.Current(found.claim.FlowInstances, found.task.FlowInstanceId) is { } instance)
            flowInstances.Record(instance, FlowActionType.Rejected, actor, found.task.Id, found.task.Sequence, found.task.Comment);
        Persist(found.claim);
        Audit(actor, "EXPENSE_REJECTED", found.claim, comment.Trim());
        notifications?.Create(found.claim.ApplicantId, "EXPENSE_REJECTED", "报销申请已驳回", $"{found.claim.Number} 已被驳回：{comment.Trim()}", "ExpenseClaim", found.claim.Id);
        return ServiceResult<ExpenseClaim>.Success(found.claim);
    }

    public ServiceResult<ExpenseClaim> Transfer(Employee actor, Guid taskId, TransferTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Comment)) return ServiceResult<ExpenseClaim>.Failure("转办必须填写意见。");
        var found = _claims.SelectMany(claim => claim.Tasks.Select(task => (claim, task))).SingleOrDefault(pair => pair.task.Id == taskId);
        if (found.task is null) return ServiceResult<ExpenseClaim>.Failure("审批任务不存在。", "DATA_001");
        if (found.task.AssigneeId != actor.Id || found.task.Status != FlowTaskStatus.Pending)
            return ServiceResult<ExpenseClaim>.Failure("当前用户不能转办该审批任务。", "AUTH_002");
        var assignee = data.FindEmployee(request.AssigneeId);
        if (assignee is null || assignee.Id == actor.Id)
            return ServiceResult<ExpenseClaim>.Failure("转办人不存在或不能为当前审批人。", "FLOW_002");
        var previousName = found.task.AssigneeName;
        var previous = data.GetEmployee(found.task.AssigneeId);
        found.task.AssigneeId = assignee.Id;
        found.task.AssigneeName = assignee.Name;
        if (flowInstances.Current(found.claim.FlowInstances, found.task.FlowInstanceId) is { } instance)
            flowInstances.Record(instance, FlowActionType.Transferred, actor, found.task.Id, found.task.Sequence, request.Comment, previous, assignee);
        Persist(found.claim);
        Audit(actor, "EXPENSE_TASK_TRANSFERRED", found.claim, $"将第 {found.task.Sequence} 节点从 {previousName} 转办给 {assignee.Name}：{request.Comment.Trim()}");
        notifications?.Create(assignee.Id, "TODO_TRANSFERRED", "报销审批待办已转办给你", $"{found.claim.Number}：{request.Comment.Trim()}", "ExpenseClaim", found.claim.Id);
        return ServiceResult<ExpenseClaim>.Success(found.claim);
    }

    public ServiceResult<ExpenseClaim> Withdraw(Employee actor, Guid id)
    {
        var item = _claims.SingleOrDefault(claim => claim.Id == id);
        if (item is null || item.ApplicantId != actor.Id) return ServiceResult<ExpenseClaim>.Failure("报销单不存在或无权限。", "DATA_001");
        if (item.Status != ExpenseStatus.Approving) return ServiceResult<ExpenseClaim>.Failure("当前状态不允许撤回。", "STATE_001");
        if (item.Tasks.Any(task => task.Status != FlowTaskStatus.Pending)) return ServiceResult<ExpenseClaim>.Failure("已有审批处理，不能撤回。", "STATE_001");
        foreach (var task in item.Tasks) task.Status = FlowTaskStatus.Cancelled;
        item.Status = ExpenseStatus.Withdrawn;
        if (flowInstances.Current(item.FlowInstances, item.CurrentFlowInstanceId) is { } instance)
            flowInstances.Record(instance, FlowActionType.Withdrawn, actor, comment: "申请人撤回");
        Persist(item);
        var activatedCopies = copyRecipients.Activate("Expense", item.Id, item.Status.ToString());
        Audit(actor, "EXPENSE_WITHDRAWN", item, "撤回报销申请");
        foreach (var task in item.Tasks.Where(task => task.AssigneeId != actor.Id)) notifications?.Create(task.AssigneeId, "EXPENSE_WITHDRAWN", "报销申请已撤回", $"{item.Number} 已被申请人撤回", "ExpenseClaim", item.Id);
        foreach (var recipient in activatedCopies) notifications?.Create(recipient.Id, "FLOW_COPY", "报销抄送事项已撤回", $"{item.ApplicantName} 的 {item.Number} 已撤回", "ExpenseClaim", item.Id);
        return ServiceResult<ExpenseClaim>.Success(item);
    }

    public ServiceResult<ExpenseClaim> RegisterPayment(Employee actor, Guid id, RegisterPaymentRequest request)
    {
        var item = _claims.SingleOrDefault(claim => claim.Id == id);
        if (item is null || !data.HasPermission(actor, OaPermissions.ExpensePay)) return ServiceResult<ExpenseClaim>.Failure("无付款权限。", "AUTH_002");
        if (item.Status != ExpenseStatus.Approved) return ServiceResult<ExpenseClaim>.Failure("仅审批完成的报销单可付款。", "STATE_001");
        if (request.PaidAmount != item.TotalAmount || string.IsNullOrWhiteSpace(request.PaymentMethod) || request.PaymentMethod.Trim().Length > 32 || string.IsNullOrWhiteSpace(request.TransactionNumber) || request.TransactionNumber.Trim().Length > 128 || string.IsNullOrWhiteSpace(request.ProofFile) || request.PaymentDate > DateOnly.FromDateTime(DateTime.Today))
            return ServiceResult<ExpenseClaim>.Failure("付款日期、方式、实付金额、流水号或付款凭证不符合要求。", "EXP_005");
        if (files is not null && !files.AreOwnedBy(actor, [request.ProofFile]))
            return ServiceResult<ExpenseClaim>.Failure("付款凭证不存在或不属于当前付款人。", "FILE_005");
        item.Payment = new PaymentRecord(request.PaymentDate, request.PaymentMethod.Trim(), request.TransactionNumber.Trim(), request.PaidAmount, request.ProofFile, actor.Id);
        item.Status = ExpenseStatus.Completed;
        Persist(item);
        Audit(actor, "EXPENSE_PAYMENT_REGISTERED", item, $"登记付款流水号 {request.TransactionNumber.Trim()}");
        notifications?.Create(item.ApplicantId, "EXPENSE_PAID", "报销已付款", $"{item.Number} 已完成付款登记", "ExpenseClaim", item.Id);
        return ServiceResult<ExpenseClaim>.Success(item);
    }

    private static List<ExpenseClaim> LoadClaims(OaDbContext db, FlowInstanceService flowInstances, FlowCopyService copyRecipients)
    {
        var items = db.ExpenseItems.AsNoTracking().ToLookup(x => x.ExpenseClaimId);
        var tasks = db.ExpenseTasks.AsNoTracking().Where(x => x.TenantId == TenantId).ToLookup(x => x.ExpenseClaimId);
        var payments = db.Payments.AsNoTracking().ToDictionary(x => x.ExpenseClaimId);
        var travels = db.TravelRequests.AsNoTracking().ToDictionary(x => x.Id, x => x.Number);
        return db.ExpenseClaims.AsNoTracking().Where(x => x.TenantId == TenantId).OrderBy(x => x.CreatedAt).ToList().Select(x =>
        {
            var claim = new ExpenseClaim { Id = x.Id, Number = x.Number, ApplicantId = x.ApplicantId, ApplicantName = x.ApplicantName, DepartmentName = x.DepartmentName, TravelRequestId = x.TravelRequestId, TravelRequestNumber = x.TravelRequestId is { } travelId ? travels.GetValueOrDefault(travelId) : null, PayeeAccountName = x.PayeeAccountName, PayeeAccount = x.PayeeAccount, BankName = x.BankName, Description = x.Description, TotalAmount = x.TotalAmount, Version = x.Version, ProcessDefinitionId = x.ProcessDefinitionId, ProcessDefinitionCode = x.ProcessDefinitionCode, ProcessDefinitionVersion = x.ProcessDefinitionVersion, CurrentFlowInstanceId = x.CurrentFlowInstanceId, ConfigVersionId = x.ConfigVersionId, ConfigVersionNumber = x.ConfigVersionNumber, ConfigSnapshotJson = x.ConfigSnapshotJson, ConfigResolvedAt = x.ConfigResolvedAt, CreatedAt = x.CreatedAt, Status = (ExpenseStatus)x.Status, Items = items[x.Id].Select(i => new ExpenseItem(i.ExpenseDate, i.Category, i.Amount, i.Description, i.ReceiptNumber, JsonSerializer.Deserialize<List<string>>(i.AttachmentsJson) ?? [])).ToList(), CopyRecipientIds = copyRecipients.LoadRecipientIds("Expense", x.Id) };
            claim.Tasks.AddRange(tasks[x.Id].OrderBy(t => t.Sequence).Select(t => new ExpenseTask { Id = t.Id, ExpenseClaimId = x.Id, FlowInstanceId = t.FlowInstanceId, AssigneeId = t.AssigneeId, AssigneeName = t.AssigneeName, OriginalAssigneeId = t.OriginalAssigneeId, OriginalAssigneeName = t.OriginalAssigneeName, DelegationId = t.DelegationId, Sequence = t.Sequence, Status = (FlowTaskStatus)t.Status, Comment = t.Comment, ProcessedAt = t.ProcessedAt }));
            claim.FlowInstances.AddRange(flowInstances.Load("Expense", x.Id));
            if (payments.TryGetValue(x.Id, out var payment)) claim.Payment = new PaymentRecord(payment.PaymentDate, payment.PaymentMethod, payment.TransactionNumber, payment.PaidAmount, payment.ProofFile, payment.OperatorId);
            return claim;
        }).ToList();
    }

    private void Persist(ExpenseClaim claim)
    {
        if (db is null) return;
        db.ChangeTracker.Clear();
        var record = db.ExpenseClaims.SingleOrDefault(x => x.Id == claim.Id);
        if (record is null) { record = new ExpenseRecord { Id = claim.Id, TenantId = TenantId }; db.ExpenseClaims.Add(record); }
        record.Number = claim.Number; record.ApplicantId = claim.ApplicantId; record.ApplicantName = claim.ApplicantName; record.DepartmentName = claim.DepartmentName; record.TravelRequestId = claim.TravelRequestId; record.PayeeAccountName = claim.PayeeAccountName; record.PayeeAccount = claim.PayeeAccount; record.BankName = claim.BankName; record.Description = claim.Description; record.TotalAmount = claim.TotalAmount; record.Status = (int)claim.Status; record.Version = claim.Version; record.ProcessDefinitionId = claim.ProcessDefinitionId; record.ProcessDefinitionCode = claim.ProcessDefinitionCode; record.ProcessDefinitionVersion = claim.ProcessDefinitionVersion; record.CurrentFlowInstanceId = claim.CurrentFlowInstanceId; record.ConfigVersionId = claim.ConfigVersionId; record.ConfigVersionNumber = claim.ConfigVersionNumber; record.ConfigSnapshotJson = claim.ConfigSnapshotJson; record.ConfigResolvedAt = claim.ConfigResolvedAt; record.UpdatedAt = DateTimeOffset.UtcNow;
        db.ExpenseItems.Where(x => x.ExpenseClaimId == claim.Id).ExecuteDelete();
        db.ExpenseTasks.Where(x => x.ExpenseClaimId == claim.Id).ExecuteDelete();
        db.Payments.Where(x => x.ExpenseClaimId == claim.Id).ExecuteDelete();
        db.ExpenseItems.AddRange(claim.Items.Select(i => new ExpenseItemRecord { ExpenseClaimId = claim.Id, ExpenseDate = i.ExpenseDate, Category = i.Category, Amount = i.Amount, Description = i.Description, ReceiptNumber = i.ReceiptNumber, AttachmentsJson = JsonSerializer.Serialize(i.Attachments ?? []) }));
        flowInstances.Track(claim.FlowInstances);
        copyRecipients.Track("Expense", claim.Id, claim.Number, claim.ApplicantName, claim.Description ?? "费用报销", claim.CopyRecipientIds);
        db.ExpenseTasks.AddRange(claim.Tasks.Select(t => new ExpenseTaskRecord { Id = t.Id, TenantId = TenantId, ExpenseClaimId = claim.Id, FlowInstanceId = t.FlowInstanceId, AssigneeId = t.AssigneeId, AssigneeName = t.AssigneeName, OriginalAssigneeId = t.OriginalAssigneeId, OriginalAssigneeName = t.OriginalAssigneeName, DelegationId = t.DelegationId, Sequence = t.Sequence, Status = (int)t.Status, Comment = t.Comment, ProcessedAt = t.ProcessedAt }));
        if (claim.Payment is { } payment) db.Payments.Add(new PaymentRecordEntity { ExpenseClaimId = claim.Id, PaymentDate = payment.PaymentDate, PaymentMethod = payment.PaymentMethod, TransactionNumber = payment.TransactionNumber, PaidAmount = payment.PaidAmount, ProofFile = payment.ProofFile, OperatorId = payment.OperatorId });
        db.SaveChanges();
    }

    private void Audit(Employee actor, string action, ExpenseClaim claim, string summary)
    {
        if (db is null) return;
        db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "ExpenseClaim", ResourceId = claim.Id.ToString(), Summary = summary });
        db.SaveChanges();
    }

    private ServiceResult<TravelRequest?> ValidateTravelReference(Employee actor, Guid? travelRequestId)
    {
        if (travelRequestId is null) return ServiceResult<TravelRequest?>.Success(null);
        if (travelRequests is null) return ServiceResult<TravelRequest?>.Failure("当前环境未启用出差关联服务。", "TRAVEL_004");
        var result = travelRequests.Get(actor, travelRequestId.Value);
        if (!result.IsSuccess || result.Value!.ApplicantId != actor.Id) return ServiceResult<TravelRequest?>.Failure("关联出差申请不存在或不属于当前申请人。", "TRAVEL_004");
        if (result.Value.Status != TravelStatus.Approved) return ServiceResult<TravelRequest?>.Failure("仅可关联已批准的出差申请。", "TRAVEL_004");
        return ServiceResult<TravelRequest?>.Success(result.Value);
    }
}
