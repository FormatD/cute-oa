using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Oa.Api.Domain;
using Oa.Api.Persistence;
using Microsoft.Extensions.Logging;

namespace Oa.Api.Services;

public sealed class PurchaseService
{
    private const string TenantId = "demo";
    private const string BusinessType = "Purchase";
    private readonly ILogger<PurchaseService>? logger;

    private ServiceResult<(ProcurementPolicyConfig Policy, BusinessConfigurationRecord? Record)> ResolveProcurementPolicy()
    {
        var configRecord = BusinessConfigurationDefaults.ResolveEffectiveConfig(db, ConfigurationDomains.Procurement, "ProcurementPolicy");
        if (db is not null && configRecord is null)
        {
            logger?.LogWarning("未找到生效中的采购管理策略配置【ProcurementPolicy】。租户：{TenantId}", TenantId);
            return ServiceResult<(ProcurementPolicyConfig, BusinessConfigurationRecord?)>.Failure("未找到生效中的采购管理策略配置【ProcurementPolicy】。", "CONFIG_MISSING");
        }

        ProcurementPolicyConfig? policy = null;
        if (configRecord is not null)
        {
            try
            {
                policy = JsonSerializer.Deserialize<ProcurementPolicyConfig>(configRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "采购管理策略配置内容损坏，无法解析。租户：{TenantId}", TenantId);
                return ServiceResult<(ProcurementPolicyConfig, BusinessConfigurationRecord?)>.Failure("采购管理策略配置内容损坏，无法解析。", "CONFIG_INVALID");
            }
            if (policy is null)
            {
                logger?.LogError("采购管理策略配置内容损坏，反序列化为 null。租户：{TenantId}", TenantId);
                return ServiceResult<(ProcurementPolicyConfig, BusinessConfigurationRecord?)>.Failure("采购管理策略配置内容损坏，无法解析。", "CONFIG_INVALID");
            }
        }
        else
        {
            policy = BusinessConfigurationDefaults.CreateDefaultProcurementPolicy();
        }

        return ServiceResult<(ProcurementPolicyConfig, BusinessConfigurationRecord?)>.Success((policy, configRecord));
    }
    private readonly DemoData data;
    private readonly OaDbContext db;
    private readonly NotificationService notifications;
    private readonly FileService? files;
    private readonly IProcessRouter processRouter;
    private readonly FlowInstanceService flowInstances;
    private readonly FlowCopyService copyRecipients;
    private readonly BudgetService budgetService;
    private readonly PaymentService paymentService;

    public PurchaseService(
        DemoData data,
        OaDbContext db,
        NotificationService? notifications = null,
        FileService? files = null,
        IProcessRouter? processRouter = null,
        FlowInstanceService? flowInstances = null,
        FlowCopyService? copyRecipients = null,
        BudgetService? budgetService = null,
        PaymentService? paymentService = null,
        ILogger<PurchaseService>? logger = null)
    {
        this.data = data;
        this.db = db;
        this.notifications = notifications ?? new NotificationService(db);
        this.files = files;
        this.processRouter = processRouter ?? new DefaultProcessRouter(data);
        this.flowInstances = flowInstances ?? new FlowInstanceService(db);
        this.copyRecipients = copyRecipients ?? new FlowCopyService(data, db);
        this.budgetService = budgetService ?? new BudgetService(data, db);
        this.paymentService = paymentService ?? new PaymentService(data, db, this.budgetService, this.notifications, this.files);
        this.logger = logger;
    }

    public PagedResponse<PurchaseRequestListItem> List(Employee actor, DocumentListQuery query, int? requestedPage, int? requestedPageSize)
    {
        var pageSize = Math.Clamp(requestedPageSize ?? 20, 1, 100);
        var visibleApplicantIds = data.ActiveEmployees.Where(subject => data.CanView(actor, subject, BusinessType)).Select(item => item.Id).ToList();
        var directRequestIds = db.PurchaseTasks.AsNoTracking().Where(item => item.TenantId == TenantId && item.AssigneeId == actor.Id).Select(item => item.PurchaseRequestId)
            .Union(db.FlowCopyRecipients.AsNoTracking().Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.RecipientId == actor.Id && item.AvailableAt != null).Select(item => item.BusinessId));
        var source = db.PurchaseRequests.AsNoTracking().Where(item => item.TenantId == TenantId && (visibleApplicantIds.Contains(item.ApplicantId) || directRequestIds.Contains(item.Id)));

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = $"%{EscapeLike(query.Keyword.Trim())}%";
            source = source.Where(item => EF.Functions.ILike(item.Number, pattern, "\\") || EF.Functions.ILike(item.Title, pattern, "\\") ||
                EF.Functions.ILike(item.Purpose, pattern, "\\") || EF.Functions.ILike(item.ApplicantName, pattern, "\\") || EF.Functions.ILike(item.ItemSearchText, pattern, "\\"));
        }
        if (query.Status is not null) source = source.Where(item => item.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.ApplicantId)) source = source.Where(item => item.ApplicantId == query.ApplicantId);
        if (query.StartDate is not null) source = source.Where(item => item.RequiredDate >= query.StartDate);
        if (query.EndDate is not null) source = source.Where(item => item.RequiredDate <= query.EndDate);
        if (query.MinAmount is not null) source = source.Where(item => item.EstimatedTotal >= query.MinAmount);
        if (query.MaxAmount is not null) source = source.Where(item => item.EstimatedTotal <= query.MaxAmount);

        var total = source.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)pageSize));
        var page = Math.Clamp(requestedPage ?? 1, 1, totalPages);
        var records = source.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToList();
        var items = records
            .Select(item => new PurchaseRequestListItem(item.Id, item.Number, item.ApplicantId, item.ApplicantName, item.DepartmentName,
                item.Title, item.RequiredDate, item.ItemCount, item.EstimatedTotal, (PurchaseStatus)item.Status, item.PaymentStatus, item.PaidTotalAmount, item.Version, item.IsDemo, item.CreatedAt, item.UpdatedAt,
                item.ConfigSnapshotJson != null && item.ConfigSnapshotJson.Contains("\"isOverBudget\":true")))
            .ToList();
        return new PagedResponse<PurchaseRequestListItem>(items, total, page, pageSize, totalPages);
    }

    public ServiceResult<PurchaseRequest> Get(Employee actor, Guid id)
    {
        var record = db.PurchaseRequests.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<PurchaseRequest>.Failure("采购申请不存在。", "DATA_001");
        var subject = data.FindEmployee(record.ApplicantId);
        var directlyRelated = db.PurchaseTasks.AsNoTracking().Any(item => item.TenantId == TenantId && item.PurchaseRequestId == id && item.AssigneeId == actor.Id) ||
            copyRecipients.CanView(actor, BusinessType, id);
        if (subject is null || !data.CanView(actor, subject, BusinessType) && !directlyRelated)
            return ServiceResult<PurchaseRequest>.Failure("无权查看该采购申请。", "AUTH_002");
        return ServiceResult<PurchaseRequest>.Success(Load(record));
    }

    public IReadOnlyList<PurchaseTask> GetPendingTasks(Employee actor) => db.PurchaseTasks.AsNoTracking()
        .Where(task => task.TenantId == TenantId && task.AssigneeId == actor.Id && task.Status == (int)FlowTaskStatus.Pending)
        .Where(task => db.PurchaseRequests.Any(request => request.Id == task.PurchaseRequestId && request.Status == (int)PurchaseStatus.Approving && request.CurrentFlowInstanceId == task.FlowInstanceId))
        .Where(task => !db.PurchaseTasks.Any(previous => previous.PurchaseRequestId == task.PurchaseRequestId && previous.FlowInstanceId == task.FlowInstanceId && previous.Status == (int)FlowTaskStatus.Pending && previous.Sequence < task.Sequence))
        .OrderBy(task => task.Sequence).ThenBy(task => task.Id).Select(ToTaskExpression()).ToList();

    public IReadOnlyList<PurchaseTask> GetProcessedTasks(Employee actor) => db.PurchaseTasks.AsNoTracking()
        .Where(task => task.TenantId == TenantId && task.AssigneeId == actor.Id && (task.Status == (int)FlowTaskStatus.Approved || task.Status == (int)FlowTaskStatus.Rejected))
        .OrderByDescending(task => task.ProcessedAt).Select(ToTaskExpression()).ToList();

    public ServiceResult<PurchaseRequest> CreateDraft(Employee actor, SavePurchaseRequest request) => CreateDraft(actor, request, false);

    public ServiceResult<PurchaseRequest> Update(Employee actor, Guid id, int expectedVersion, SavePurchaseRequest request)
    {
        var record = db.PurchaseRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id && item.ApplicantId == actor.Id);
        if (record is null) return ServiceResult<PurchaseRequest>.Failure("采购申请不存在或无权限。", "DATA_001");
        if ((PurchaseStatus)record.Status is not (PurchaseStatus.Draft or PurchaseStatus.Rejected or PurchaseStatus.Withdrawn))
            return ServiceResult<PurchaseRequest>.Failure("当前状态不允许编辑。", "STATE_001");
        if (record.Version != expectedVersion) return ServiceResult<PurchaseRequest>.Failure("单据已被更新，请刷新后重试。", "CONCURRENCY_001");
        var validation = Validate(actor, request, ChinaDate(record.CreatedAt));
        if (!validation.IsSuccess) return ServiceResult<PurchaseRequest>.Failure(validation.Error!, validation.Code!);

        Apply(record, request, validation.Value!.Items, validation.Value.Attachments);
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        copyRecipients.Track(BusinessType, record.Id, record.Number, record.ApplicantName, record.Title, validation.Value.CopyRecipientIds);
        Audit(actor, "PURCHASE_UPDATED", record, "编辑采购申请");
        return SaveAndReload(record, "采购申请存在并发修改，请刷新后重试。");
    }

    public ServiceResult<PurchaseRequest> Submit(Employee actor, Guid id)
    {
        var record = db.PurchaseRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id && item.ApplicantId == actor.Id);
        if (record is null) return ServiceResult<PurchaseRequest>.Failure("采购申请不存在或无权限。", "DATA_001");
        if ((PurchaseStatus)record.Status is not (PurchaseStatus.Draft or PurchaseStatus.Rejected or PurchaseStatus.Withdrawn))
            return ServiceResult<PurchaseRequest>.Failure("当前状态不允许提交。", "STATE_001");

        var policyResult = ResolveProcurementPolicy();
        if (!policyResult.IsSuccess) return ServiceResult<PurchaseRequest>.Failure(policyResult.Error!, policyResult.Code!);
        var (policy, configRecord) = policyResult.Value;
        var quoteThreshold = policy.QuoteAttachmentThreshold > 0 ? policy.QuoteAttachmentThreshold : 5000m;
        var doubleQuoteThreshold = 50000m;
        var attachmentCount = DeserializeList(record.AttachmentsJson).Count;
        if (record.EstimatedTotal >= doubleQuoteThreshold && attachmentCount < 2 || record.EstimatedTotal >= quoteThreshold && attachmentCount < 1)
            return ServiceResult<PurchaseRequest>.Failure(record.EstimatedTotal >= doubleQuoteThreshold ? $"{doubleQuoteThreshold:N0} 元及以上采购至少需要两份报价或依据附件。" : $"{quoteThreshold:N0} 元及以上采购至少需要一份报价或依据附件。", "PURCHASE_002");

        var matchingTier = policy.AmountTiers.OrderBy(t => t.MaxAmount ?? decimal.MaxValue)
            .FirstOrDefault(t => t.MaxAmount == null || record.EstimatedTotal <= t.MaxAmount);
        record.AmountTier = matchingTier?.Name ?? "常规采购";
        record.Category = (JsonSerializer.Deserialize<List<PurchaseItem>>(record.ItemsJson) ?? []).FirstOrDefault()?.Category ?? "其他";
        record.RequiresAcceptance = policy.RequiresAcceptance;
        record.AcceptanceRoleOrAssignee = policy.AcceptanceRoleOrAssignee;

        var route = processRouter.Resolve(BusinessType, actor, record.EstimatedTotal);
        if (!route.IsSuccess) return ServiceResult<PurchaseRequest>.Failure(route.Error!, route.Code!);

        if (configRecord is not null)
        {
            record.ConfigVersionId = configRecord.Id;
            record.ConfigVersionNumber = configRecord.Version;
            record.ConfigSnapshotJson = configRecord.ContentJson;
            record.ConfigResolvedAt = DateTimeOffset.UtcNow;
        }

        // 预算预占
        var reserveResult = budgetService.Reserve(
            TenantId,
            record.DepartmentName,
            record.Category,
            record.EstimatedTotal,
            BusinessType,
            record.Id,
            record.Number,
            actor.Id,
            blockWhenExceeded: policy.BlockWhenExceeded,
            targetYear: DateTime.Today.Year,
            targetMonth: DateTime.Today.Month,
            projectId: null,
            actionKeySuffix: record.Category ?? "general");
        if (!reserveResult.IsSuccess)
            return ServiceResult<PurchaseRequest>.Failure(reserveResult.Error!, reserveResult.Code!);
        record.BudgetPoolId = reserveResult.Value?.BudgetId;

        var isOverBudget = reserveResult.Value?.IsOverBudget == true;
        var overBudgetAmount = isOverBudget ? reserveResult.Value!.OverBudgetAmount : 0m;

        if (isOverBudget)
        {
            var overSnapshot = new OverBudgetSnapshot(
                IsOverBudget: true,
                RequestedAmount: record.EstimatedTotal,
                AvailableAmount: Math.Max(0m, record.EstimatedTotal - overBudgetAmount),
                OverBudgetAmount: overBudgetAmount,
                Department: record.DepartmentName,
                Category: record.Category,
                PolicyCode: configRecord?.Code ?? "DEFAULT",
                ResolvedAt: DateTimeOffset.UtcNow
            );
            var baseJson = string.IsNullOrEmpty(record.ConfigSnapshotJson) ? "{}" : record.ConfigSnapshotJson;
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(baseJson) ?? new Dictionary<string, object>();
                dict["isOverBudget"] = true;
                dict["overBudgetSnapshot"] = overSnapshot;
                record.ConfigSnapshotJson = JsonSerializer.Serialize(dict);
            }
            catch
            {
                record.ConfigSnapshotJson = JsonSerializer.Serialize(new { isOverBudget = true, overBudgetSnapshot = overSnapshot });
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(record.ConfigSnapshotJson) && record.ConfigSnapshotJson.Contains("\"isOverBudget\":true"))
            {
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(record.ConfigSnapshotJson) ?? new Dictionary<string, object>();
                    dict.Remove("isOverBudget");
                    dict.Remove("overBudgetSnapshot");
                    record.ConfigSnapshotJson = JsonSerializer.Serialize(dict);
                }
                catch { }
            }
        }

        var attempt = db.FlowInstances.Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.BusinessId == record.Id)
            .Select(item => (int?)item.Attempt).Max() ?? 0;
        var instance = new FlowInstanceRecord
        {
            TenantId = TenantId, BusinessType = BusinessType, BusinessId = record.Id, BusinessNumber = record.Number, ApplicantId = actor.Id,
            ProcessDefinitionId = route.Value!.DefinitionId, ProcessDefinitionCode = route.Value.Code, ProcessDefinitionVersion = route.Value.Version,
            Attempt = attempt + 1, Status = (int)FlowInstanceStatus.Running
        };
        db.FlowInstances.Add(instance);
        AddFlowAction(instance.Id, FlowActionType.Submitted, actor, comment: "提交审批");
        var approvalTasks = route.Value.Approvers.Select((approver, index) => new PurchaseTaskRecord
            {
                TenantId = TenantId, PurchaseRequestId = record.Id, FlowInstanceId = instance.Id,
                AssigneeId = approver.Assignee.Id, AssigneeName = approver.Assignee.Name,
                OriginalAssigneeId = approver.DelegationId is null ? null : approver.OriginalApprover.Id,
                OriginalAssigneeName = approver.DelegationId is null ? null : approver.OriginalApprover.Name,
                DelegationId = approver.DelegationId, Sequence = index + 1, Status = (int)FlowTaskStatus.Pending
            }).ToList();

        Employee? financeManager = null;
        if (isOverBudget)
        {
            financeManager = data.ActiveEmployees.FirstOrDefault(e => data.HasRole(e, "财务经理") || e.Role == "财务经理") ?? data.FindEmployee("u-lin");
            if (financeManager is not null && !route.Value.Approvers.Any(a => a.Assignee.Id == financeManager.Id))
            {
                var extraSeq = approvalTasks.Count + 1;
                approvalTasks.Add(new PurchaseTaskRecord
                {
                    TenantId = TenantId,
                    PurchaseRequestId = record.Id,
                    FlowInstanceId = instance.Id,
                    AssigneeId = financeManager.Id,
                    AssigneeName = financeManager.Name,
                    Sequence = extraSeq,
                    Status = (int)FlowTaskStatus.Pending,
                    Comment = "超预算特批节点"
                });
            }
        }

        db.PurchaseTasks.AddRange(approvalTasks);
        FlowInstanceService.RegisterTasks(db, instance.Id, instance.StartedAt, BusinessType,
            approvalTasks.Select((task, index) =>
            {
                if (index < route.Value.Approvers.Count)
                    return new ResolvedFlowTask(task.Id, task.Sequence, route.Value.Approvers[index]);
                var emp = data.FindEmployee(task.AssigneeId) ?? financeManager!;
                return new ResolvedFlowTask(task.Id, task.Sequence, new ResolvedApprover(emp, emp, null));
            }).ToList());
        record.Status = (int)PurchaseStatus.Approving;
        record.ProcessDefinitionId = route.Value.DefinitionId;
        record.ProcessDefinitionCode = route.Value.Code;
        record.ProcessDefinitionVersion = route.Value.Version;
        record.CurrentFlowInstanceId = instance.Id;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        Audit(actor, "PURCHASE_SUBMITTED", record, $"提交采购审批，预估金额 {record.EstimatedTotal:F2} 元{(isOverBudget ? $"（超预算 {overBudgetAmount:F2} 元）" : "")}");
        var first = approvalTasks.First();
        notifications.Enqueue(first.AssigneeId, "TODO_CREATED", "新增采购审批待办", $"{actor.Name} 提交了 {record.Number}", "PurchaseRequest", record.Id);
        return SaveAndReload(record, "采购申请已被其他操作更新，请刷新后重试。");
    }

    public ServiceResult<PurchaseRequest> Approve(Employee actor, Guid taskId, string? comment) => Decide(actor, taskId, true, comment);

    public ServiceResult<PurchaseRequest> Reject(Employee actor, Guid taskId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return ServiceResult<PurchaseRequest>.Failure("驳回必须填写意见。", "VALIDATION_001");
        if (comment.Trim().Length > 500) return ServiceResult<PurchaseRequest>.Failure("审批意见最多 500 个字符。", "VALIDATION_001");
        return Decide(actor, taskId, false, comment);
    }

    public ServiceResult<PurchaseRequest> Transfer(Employee actor, Guid taskId, TransferTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Comment)) return ServiceResult<PurchaseRequest>.Failure("转办必须填写意见。", "VALIDATION_001");
        if (request.Comment.Trim().Length > 500) return ServiceResult<PurchaseRequest>.Failure("转办意见最多 500 个字符。", "VALIDATION_001");
        var task = db.PurchaseTasks.SingleOrDefault(item => item.TenantId == TenantId && item.Id == taskId);
        if (task is null) return ServiceResult<PurchaseRequest>.Failure("采购审批任务不存在。", "DATA_001");
        var purchase = db.PurchaseRequests.Single(item => item.Id == task.PurchaseRequestId);
        if (task.AssigneeId != actor.Id || task.Status != (int)FlowTaskStatus.Pending || purchase.Status != (int)PurchaseStatus.Approving || purchase.CurrentFlowInstanceId != task.FlowInstanceId)
            return ServiceResult<PurchaseRequest>.Failure("当前用户不能转办该采购审批任务。", "AUTH_002");
        var assignee = data.FindEmployee(request.AssigneeId);
        if (assignee is null || assignee.Status != "ACTIVE" || assignee.Id == actor.Id)
            return ServiceResult<PurchaseRequest>.Failure("转办人不存在、已停用或不能为当前审批人。", "FLOW_002");
        var previousId = task.AssigneeId;
        var previousName = task.AssigneeName;
        task.AssigneeId = assignee.Id;
        task.AssigneeName = assignee.Name;
        task.Version++;
        AddFlowAction(task.FlowInstanceId!.Value, FlowActionType.Transferred, actor, task.Id, task.Sequence, request.Comment.Trim(), previousId, previousName, assignee.Id, assignee.Name);
        FlowInstanceService.UpdateTaskSla(db, task.FlowInstanceId.Value, FlowActionType.Transferred, task.Id, assignee);
        Audit(actor, "PURCHASE_TASK_TRANSFERRED", purchase, $"将第 {task.Sequence} 节点转办给 {assignee.Name}：{request.Comment.Trim()}");
        notifications.Enqueue(assignee.Id, "TODO_TRANSFERRED", "采购审批待办已转办给你", $"{purchase.Number}：{request.Comment.Trim()}", "PurchaseRequest", purchase.Id);
        return SaveAndReload(purchase, "采购审批任务已被其他操作处理，请刷新后重试。");
    }

    public ServiceResult<PurchaseRequest> Withdraw(Employee actor, Guid id)
    {
        var record = db.PurchaseRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id && item.ApplicantId == actor.Id);
        if (record is null) return ServiceResult<PurchaseRequest>.Failure("采购申请不存在或无权限。", "DATA_001");
        if (record.Status != (int)PurchaseStatus.Approving || record.CurrentFlowInstanceId is null)
            return ServiceResult<PurchaseRequest>.Failure("仅尚未处理的审批中申请可撤回。", "STATE_001");
        var tasks = db.PurchaseTasks.Where(item => item.PurchaseRequestId == id && item.FlowInstanceId == record.CurrentFlowInstanceId).ToList();
        if (tasks.Count == 0 || tasks.Any(item => item.Status != (int)FlowTaskStatus.Pending))
            return ServiceResult<PurchaseRequest>.Failure("审批节点已有处理记录，不能撤回。", "STATE_001");
        foreach (var task in tasks) { task.Status = (int)FlowTaskStatus.Cancelled; task.Version++; }
        record.Status = (int)PurchaseStatus.Withdrawn;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        var instance = db.FlowInstances.Single(item => item.Id == record.CurrentFlowInstanceId);
        instance.Status = (int)FlowInstanceStatus.Withdrawn;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        AddFlowAction(instance.Id, FlowActionType.Withdrawn, actor, comment: "申请人撤回");
        FlowInstanceService.UpdateTaskSla(db, instance.Id, FlowActionType.Withdrawn);
        budgetService.Release(TenantId, record.BudgetPoolId, BusinessType, record.Id, record.Number, record.EstimatedTotal, actor.Id, "撤回采购申请释放预占预算");
        var copied = ActivateCopies(record, PurchaseStatus.Withdrawn.ToString());
        Audit(actor, "PURCHASE_WITHDRAWN", record, "撤回采购申请");
        foreach (var assignee in tasks.Select(item => item.AssigneeId).Distinct().Where(item => item != actor.Id))
            notifications.Enqueue(assignee, "PURCHASE_WITHDRAWN", "采购申请已撤回", $"{record.Number} 已被申请人撤回", "PurchaseRequest", record.Id);
        foreach (var recipient in copied) notifications.Enqueue(recipient.Id, "FLOW_COPY", "采购抄送事项已撤回", $"{record.ApplicantName} 的 {record.Number} 已撤回", "PurchaseRequest", record.Id);
        return SaveAndReload(record, "采购申请已被其他操作更新，请刷新后重试。");
    }

    public ServiceResult<bool> Delete(Employee actor, Guid id)
    {
        var record = db.PurchaseRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id && item.ApplicantId == actor.Id);
        if (record is null) return ServiceResult<bool>.Failure("采购申请不存在或无权限。", "DATA_001");
        if ((PurchaseStatus)record.Status is not (PurchaseStatus.Draft or PurchaseStatus.Rejected or PurchaseStatus.Withdrawn))
            return ServiceResult<bool>.Failure("当前状态不允许删除。", "STATE_001");
        budgetService.Release(TenantId, record.BudgetPoolId, BusinessType, record.Id, record.Number, record.EstimatedTotal, actor.Id, "删除采购申请释放预占预算");
        var instanceIds = db.FlowInstances.Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.BusinessId == id).Select(item => item.Id).ToList();
        db.FlowCopyRecipients.Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.BusinessId == id).ExecuteDelete();
        db.PurchaseTasks.Where(item => item.PurchaseRequestId == id).ExecuteDelete();
        db.PaymentTransactions.Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.BusinessId == id).ExecuteDelete();
        db.FlowActions.Where(item => instanceIds.Contains(item.FlowInstanceId)).ExecuteDelete();
        db.FlowInstances.Where(item => instanceIds.Contains(item.Id)).ExecuteDelete();
        db.PurchaseRequests.Remove(record);
        Audit(actor, "PURCHASE_DELETED", record, "删除采购申请");
        db.SaveChanges();
        return ServiceResult<bool>.Success(true);
    }

    public ServiceResult<PurchaseRequest> RegisterOrder(Employee actor, Guid id, RegisterPurchaseOrderRequest request)
    {
        if (!data.HasPermission(actor, OaPermissions.PurchaseManage)) return ServiceResult<PurchaseRequest>.Failure("无采购执行权限。", "AUTH_002");
        var record = db.PurchaseRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<PurchaseRequest>.Failure("采购申请不存在。", "DATA_001");
        var subject = data.FindEmployee(record.ApplicantId);
        if (subject is null || !data.CanView(actor, subject, BusinessType)) return ServiceResult<PurchaseRequest>.Failure("采购执行权限不覆盖该申请。", "AUTH_002");
        if (record.Status != (int)PurchaseStatus.Approved || db.PurchaseOrders.Any(item => item.PurchaseRequestId == id)) return ServiceResult<PurchaseRequest>.Failure("仅已批准且未下单的采购申请可登记下单。", "STATE_001");
        if (record.Version != request.Version) return ServiceResult<PurchaseRequest>.Failure("单据已被更新，请刷新后重试。", "CONCURRENCY_001");
        var validation = ValidateOrder(actor, record, request);
        if (!validation.IsSuccess) return ServiceResult<PurchaseRequest>.Failure(validation.Error!, validation.Code!);
        if (db.PurchaseOrders.Any(item => item.TenantId == TenantId && item.OrderNumber == request.OrderNumber.Trim()))
            return ServiceResult<PurchaseRequest>.Failure("采购订单号已存在。", "PURCHASE_003");
        db.PurchaseOrders.Add(new PurchaseOrderRecord
        {
            TenantId = TenantId, PurchaseRequestId = id, Supplier = request.Supplier.Trim(), OrderNumber = request.OrderNumber.Trim(), ActualAmount = request.ActualAmount,
            OrderDate = request.OrderDate, ExpectedDeliveryDate = request.ExpectedDeliveryDate, Notes = NormalizeOptional(request.Notes),
            AttachmentsJson = JsonSerializer.Serialize(validation.Value), CreatedBy = actor.Id, CreatedByName = actor.Name
        });
        record.Status = (int)PurchaseStatus.Ordered;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "PURCHASE_ORDERED", record, $"登记订单 {request.OrderNumber.Trim()}，成交金额 {request.ActualAmount:F2} 元");
        notifications.Enqueue(record.ApplicantId, "PURCHASE_ORDERED", "采购申请已下单", $"{record.Number} 已登记下单，预计 {request.ExpectedDeliveryDate:yyyy-MM-dd} 到货", "PurchaseRequest", record.Id);
        return SaveAndReload(record, "采购申请已被其他操作更新，请刷新后重试。", "PURCHASE_003");
    }

    public ServiceResult<PurchaseRequest> Receive(Employee actor, Guid id, ReceivePurchaseRequest request)
    {
        var record = db.PurchaseRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<PurchaseRequest>.Failure("采购申请不存在。", "DATA_001");

        bool isAuthorized = false;
        if (string.IsNullOrWhiteSpace(record.AcceptanceRoleOrAssignee) || record.AcceptanceRoleOrAssignee.Equals("APPLICANT", StringComparison.OrdinalIgnoreCase))
        {
            isAuthorized = actor.Id == record.ApplicantId || data.HasPermission(actor, OaPermissions.PurchaseManage);
        }
        else if (record.AcceptanceRoleOrAssignee.StartsWith("ROLE:", StringComparison.OrdinalIgnoreCase))
        {
            var roleName = record.AcceptanceRoleOrAssignee["ROLE:".Length..];
            isAuthorized = (actor.Roles ?? []).Contains(roleName) || (roleName == "采购员" && data.HasPermission(actor, OaPermissions.PurchaseManage));
        }
        else if (record.AcceptanceRoleOrAssignee == "PURCHASE_MANAGE")
        {
            isAuthorized = data.HasPermission(actor, OaPermissions.PurchaseManage);
        }
        else
        {
            isAuthorized = actor.Id == record.AcceptanceRoleOrAssignee;
        }

        if (!isAuthorized)
            return ServiceResult<PurchaseRequest>.Failure("当前用户不是指定的验收人或无验收权限。", "AUTH_002");

        if (actor.Id != record.ApplicantId)
        {
            var subject = data.FindEmployee(record.ApplicantId);
            if (subject is null || !data.CanView(actor, subject, BusinessType))
                return ServiceResult<PurchaseRequest>.Failure("采购执行或验收权限不覆盖该申请。", "AUTH_002");
        }

        var order = db.PurchaseOrders.AsNoTracking().SingleOrDefault(item => item.PurchaseRequestId == id);
        if (record.Status != (int)PurchaseStatus.Ordered || order is null || db.PurchaseReceipts.Any(item => item.PurchaseRequestId == id))
            return ServiceResult<PurchaseRequest>.Failure("仅已下单且未验收的采购申请可登记验收。", "STATE_001");
        if (record.Version != request.Version) return ServiceResult<PurchaseRequest>.Failure("单据已被更新，请刷新后重试。", "CONCURRENCY_001");
        var validation = ValidateReceipt(actor, order, request);
        if (!validation.IsSuccess) return ServiceResult<PurchaseRequest>.Failure(validation.Error!, validation.Code!);
        db.PurchaseReceipts.Add(new PurchaseReceiptRecord
        {
            TenantId = TenantId, PurchaseRequestId = id, ReceivedDate = request.ReceivedDate, Result = request.Result,
            Notes = request.Notes.Trim(), AttachmentsJson = JsonSerializer.Serialize(validation.Value), CreatedBy = actor.Id, CreatedByName = actor.Name
        });
        record.Status = (int)PurchaseStatus.Received;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "PURCHASE_RECEIVED", record, $"{request.ReceivedDate:yyyy-MM-dd} 验收全部通过：{request.Notes.Trim()}");
        if (actor.Id != record.ApplicantId) notifications.Enqueue(record.ApplicantId, "PURCHASE_RECEIVED", "采购申请已完成验收", $"{record.Number} 已由 {actor.Name} 登记验收通过", "PurchaseRequest", record.Id);
        return SaveAndReload(record, "采购申请已被其他操作更新，请刷新后重试。");
    }

    public ServiceResult<GeneratePurchaseDemoResult> GenerateDemoData(Employee actor)
    {
        if (!data.HasPermission(actor, OaPermissions.PurchaseManage)) return ServiceResult<GeneratePurchaseDemoResult>.Failure("无采购演示数据生成权限。", "AUTH_002");
        if (db.PurchaseRequests.Any(item => item.TenantId == TenantId && item.IsDemo && item.ApplicantId == actor.Id))
            return ServiceResult<GeneratePurchaseDemoResult>.Success(new GeneratePurchaseDemoResult(0, 1));
        var result = CreateDraft(actor, new SavePurchaseRequest(
            "办公区人体工学椅采购", "替换损坏座椅并补充新员工工位", BusinessTime.ChinaToday().AddDays(14), "通用办公用品供应商",
            [new SavePurchaseItem("办公用品", "人体工学椅", "黑色，可调腰托", 4, "把", 950m, "演示采购草稿")]), true);
        return result.IsSuccess
            ? ServiceResult<GeneratePurchaseDemoResult>.Success(new GeneratePurchaseDemoResult(1, 0))
            : ServiceResult<GeneratePurchaseDemoResult>.Failure(result.Error!, result.Code!);
    }

    private ServiceResult<PurchaseRequest> CreateDraft(Employee actor, SavePurchaseRequest request, bool isDemo)
    {
        var validation = Validate(actor, request, BusinessTime.ChinaToday());
        if (!validation.IsSuccess) return ServiceResult<PurchaseRequest>.Failure(validation.Error!, validation.Code!);
        var configRecord = BusinessConfigurationDefaults.ResolveEffectiveConfig(db, ConfigurationDomains.Procurement, "ProcurementPolicy");
        if (configRecord is null)
            return ServiceResult<PurchaseRequest>.Failure("未找到生效中的采购管理策略配置【ProcurementPolicy】。", "CONFIG_MISSING");
        var record = new PurchaseRequestRecord
        {
            TenantId = TenantId, Number = $"CG-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            ApplicantId = actor.Id, ApplicantName = actor.Name, DepartmentName = actor.DepartmentName, Status = (int)PurchaseStatus.Draft, Version = 1, IsDemo = isDemo,
            ConfigVersionId = configRecord?.Id, ConfigVersionNumber = configRecord?.Version, ConfigSnapshotJson = configRecord?.ContentJson, ConfigResolvedAt = configRecord is not null ? DateTimeOffset.UtcNow : null
        };
        var valid = validation.Value!;
        Apply(record, request, valid.Items, valid.Attachments);
        db.PurchaseRequests.Add(record);
        copyRecipients?.Track(BusinessType, record.Id, record.Number, record.ApplicantName, record.Title, valid.CopyRecipientIds);
        Audit(actor, isDemo ? "PURCHASE_DEMO_CREATED" : "PURCHASE_DRAFT_CREATED", record, isDemo ? "生成采购演示草稿" : "创建采购草稿");
        db.SaveChanges();
        return ServiceResult<PurchaseRequest>.Success(Load(record));
    }

    private ServiceResult<PurchaseRequest> Decide(Employee actor, Guid taskId, bool approve, string? comment)
    {
        var task = db.PurchaseTasks.SingleOrDefault(item => item.TenantId == TenantId && item.Id == taskId);
        if (task is null) return ServiceResult<PurchaseRequest>.Failure("采购审批任务不存在。", "DATA_001");
        var purchase = db.PurchaseRequests.Single(item => item.Id == task.PurchaseRequestId);
        if (task.AssigneeId != actor.Id || task.Status != (int)FlowTaskStatus.Pending || purchase.Status != (int)PurchaseStatus.Approving || purchase.CurrentFlowInstanceId != task.FlowInstanceId)
            return ServiceResult<PurchaseRequest>.Failure("当前用户不能处理该采购审批任务。", "AUTH_002");
        if (db.PurchaseTasks.Any(item => item.PurchaseRequestId == purchase.Id && item.FlowInstanceId == task.FlowInstanceId && item.Sequence < task.Sequence && item.Status != (int)FlowTaskStatus.Approved))
            return ServiceResult<PurchaseRequest>.Failure("前序审批尚未完成。", "STATE_001");
        task.Status = approve ? (int)FlowTaskStatus.Approved : (int)FlowTaskStatus.Rejected;
        task.Comment = NormalizeOptional(comment);
        task.ProcessedAt = DateTimeOffset.UtcNow;
        task.Version++;
        AddFlowAction(task.FlowInstanceId!.Value, approve ? FlowActionType.Approved : FlowActionType.Rejected, actor, task.Id, task.Sequence, task.Comment);
        FlowInstanceService.UpdateTaskSla(db, task.FlowInstanceId.Value, approve ? FlowActionType.Approved : FlowActionType.Rejected, task.Id);
        var instance = db.FlowInstances.Single(item => item.Id == task.FlowInstanceId);

        if (!approve)
        {
            foreach (var pending in db.PurchaseTasks.Where(item => item.PurchaseRequestId == purchase.Id && item.FlowInstanceId == task.FlowInstanceId && item.Id != task.Id && item.Status == (int)FlowTaskStatus.Pending))
            {
                pending.Status = (int)FlowTaskStatus.Cancelled;
                pending.Version++;
            }
            purchase.Status = (int)PurchaseStatus.Rejected;
            instance.Status = (int)FlowInstanceStatus.Rejected;
            instance.CompletedAt = DateTimeOffset.UtcNow;
            budgetService.Release(TenantId, purchase.BudgetPoolId, BusinessType, purchase.Id, purchase.Number, purchase.EstimatedTotal, actor.Id, $"驳回采购申请释放预占预算：{task.Comment}");
            notifications.Enqueue(purchase.ApplicantId, "PURCHASE_REJECTED", "采购申请已驳回", $"{purchase.Number} 已被驳回：{task.Comment}", "PurchaseRequest", purchase.Id);
        }
        else
        {
            var remaining = db.PurchaseTasks.Any(item => item.PurchaseRequestId == purchase.Id && item.FlowInstanceId == task.FlowInstanceId && item.Id != task.Id && item.Status == (int)FlowTaskStatus.Pending);
            if (!remaining)
            {
                purchase.Status = (int)PurchaseStatus.Approved;
                instance.Status = (int)FlowInstanceStatus.Completed;
                instance.CompletedAt = DateTimeOffset.UtcNow;

                ProcurementPolicyConfig procurementPolicy;
                if (!string.IsNullOrWhiteSpace(purchase.ConfigSnapshotJson))
                {
                    try
                    {
                        procurementPolicy = JsonSerializer.Deserialize<ProcurementPolicyConfig>(purchase.ConfigSnapshotJson, BusinessConfigurationDefaults.JsonOptions)
                            ?? (ResolveProcurementPolicy().IsSuccess ? ResolveProcurementPolicy().Value.Policy : BusinessConfigurationDefaults.CreateDefaultProcurementPolicy());
                    }
                    catch
                    {
                        var fallbackResult = ResolveProcurementPolicy();
                        procurementPolicy = fallbackResult.IsSuccess ? fallbackResult.Value.Policy : BusinessConfigurationDefaults.CreateDefaultProcurementPolicy();
                    }
                }
                else
                {
                    var policyResult = ResolveProcurementPolicy();
                    procurementPolicy = policyResult.IsSuccess ? policyResult.Value.Policy : BusinessConfigurationDefaults.CreateDefaultProcurementPolicy();
                }
                if (!string.IsNullOrWhiteSpace(procurementPolicy.DefaultPurchaserUserId))
                {
                    var purchaser = data.FindEmployee(procurementPolicy.DefaultPurchaserUserId);
                    if (purchaser is not null)
                    {
                        purchase.PurchaserUserId = purchaser.Id;
                        notifications.Enqueue(purchaser.Id, "PURCHASE_ASSIGNED", "已指派采购执行任务", $"{purchase.Number} 已审批通过，指派由你负责采购执行", "PurchaseRequest", purchase.Id);
                    }
                }

                var copied = ActivateCopies(purchase, PurchaseStatus.Approved.ToString());
                notifications.Enqueue(purchase.ApplicantId, "PURCHASE_APPROVED", "采购申请已审批通过", $"{purchase.Number} 已全部审批通过，等待采购执行", "PurchaseRequest", purchase.Id);
                foreach (var recipient in copied) notifications.Enqueue(recipient.Id, "FLOW_COPY", "采购抄送事项已审批完成", $"{purchase.ApplicantName} 的 {purchase.Number} 已审批完成", "PurchaseRequest", purchase.Id);
            }
            else
            {
                var next = db.PurchaseTasks.Where(item => item.PurchaseRequestId == purchase.Id && item.FlowInstanceId == task.FlowInstanceId && item.Status == (int)FlowTaskStatus.Pending)
                    .OrderBy(item => item.Sequence).First();
                notifications.Enqueue(next.AssigneeId, "TODO_CREATED", "新增采购审批待办", $"{purchase.Number} 等待你审批", "PurchaseRequest", purchase.Id);
            }
        }
        purchase.Version++;
        purchase.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, approve ? "PURCHASE_APPROVED" : "PURCHASE_REJECTED", purchase, approve ? task.Comment ?? "同意采购" : task.Comment!);
        return SaveAndReload(purchase, "采购审批任务已被其他操作处理，请刷新后重试。");
    }

    private ServiceResult<(IReadOnlyList<PurchaseItem> Items, IReadOnlyList<string> Attachments, IReadOnlyList<string> CopyRecipientIds)> Validate(Employee actor, SavePurchaseRequest request, DateOnly createdDate)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var purpose = request.Purpose?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 100 || purpose.Length is < 1 or > 500)
            return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure("采购主题应为 1–100 字，采购用途应为 1–500 字。", "PURCHASE_001");
        if (request.RequiredDate < createdDate || request.RequiredDate > createdDate.AddDays(365))
            return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure("期望到货日期不能早于申请创建日，且不能超过 365 天。", "PURCHASE_001");
        if ((request.SuggestedSupplier?.Trim().Length ?? 0) > 100)
            return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure("建议供应商最多 100 个字符。", "PURCHASE_001");
        if (request.Items is null || request.Items.Count is < 1 or > 50)
            return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure("采购明细应为 1–50 行。", "PURCHASE_001");

        var policyResult = ResolveProcurementPolicy();
        if (!policyResult.IsSuccess)
            return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure(policyResult.Error!, policyResult.Code!);
        var (policy, _) = policyResult.Value;
        var allowedCategories = policy.Categories.Where(c => c.IsEnabled).SelectMany(c => (new[] { c.Code, c.Name }).Concat(c.Aliases ?? [])).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var normalized = new List<PurchaseItem>();
        foreach (var item in request.Items)
        {
            var name = item.Name?.Trim() ?? string.Empty;
            var unit = item.Unit?.Trim() ?? string.Empty;
            var specification = NormalizeOptional(item.Specification);
            var remark = NormalizeOptional(item.Remark);
            var cat = item.Category?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cat) || !allowedCategories.Contains(cat))
                return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure($"采购明细品类【{item.Category}】未启用或不存在。", "PURCHASE_001");
            if (name.Length is < 1 or > 100 || unit.Length is < 1 or > 20 || (specification?.Length ?? 0) > 200 || (remark?.Length ?? 0) > 300 ||
                item.Quantity <= 0 || item.Quantity > 1_000_000m || decimal.Round(item.Quantity, 4) != item.Quantity ||
                item.EstimatedUnitPrice < 0 || item.EstimatedUnitPrice > 10_000_000m || decimal.Round(item.EstimatedUnitPrice, 2) != item.EstimatedUnitPrice)
                return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure("采购明细的品类、名称、规格、数量、单位、单价或备注不合法。", "PURCHASE_001");
            var amount = decimal.Round(item.Quantity * item.EstimatedUnitPrice, 2, MidpointRounding.AwayFromZero);
            normalized.Add(new PurchaseItem(cat, name, specification, item.Quantity, unit, item.EstimatedUnitPrice, amount, remark));
        }
        var total = normalized.Sum(item => item.EstimatedAmount);
        if (total <= 0 || total > 100_000_000m)
            return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure("采购预估总额必须大于 0 且不超过 1 亿元。", "PURCHASE_001");
        var attachments = (request.Attachments ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
        if (attachments.Count > 20 || attachments.Count > 0 && files is not null && !files.AreOwnedBy(actor, attachments))
            return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure("申请附件不存在、不属于当前用户或超过 20 个。", "FILE_005");
        var copies = copyRecipients.Validate(actor, request.CopyRecipientIds);
        if (!copies.IsSuccess) return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Failure(copies.Error!, copies.Code!);
        return ServiceResult<(IReadOnlyList<PurchaseItem>, IReadOnlyList<string>, IReadOnlyList<string>)>.Success((normalized, attachments, copies.Value!));
    }

    private ServiceResult<IReadOnlyList<string>> ValidateOrder(Employee actor, PurchaseRequestRecord record, RegisterPurchaseOrderRequest request)
    {
        var supplier = request.Supplier?.Trim() ?? string.Empty;
        var orderNumber = request.OrderNumber?.Trim() ?? string.Empty;
        var createdDate = ChinaDate(record.CreatedAt);
        if (supplier.Length is < 1 or > 100 || orderNumber.Length is < 1 or > 64 || request.ActualAmount <= 0 || decimal.Round(request.ActualAmount, 2) != request.ActualAmount ||
            request.ActualAmount > decimal.Round(record.EstimatedTotal * 1.10m, 2) || request.OrderDate < createdDate || request.OrderDate > BusinessTime.ChinaToday() ||
            request.ExpectedDeliveryDate < request.OrderDate || (request.Notes?.Trim().Length ?? 0) > 500)
            return ServiceResult<IReadOnlyList<string>>.Failure("成交供应商、订单号、实际金额、下单日期或预计到货日期不合法。", "PURCHASE_003");
        return ValidateActionAttachments(actor, request.Attachments, "下单附件");
    }

    private ServiceResult<IReadOnlyList<string>> ValidateReceipt(Employee actor, PurchaseOrderRecord order, ReceivePurchaseRequest request)
    {
        if (request.Result != "ALL_ACCEPTED" || request.ReceivedDate < order.OrderDate || request.ReceivedDate > BusinessTime.ChinaToday() ||
            string.IsNullOrWhiteSpace(request.Notes) || request.Notes.Trim().Length > 500)
            return ServiceResult<IReadOnlyList<string>>.Failure("验收日期、结果或说明不合法；当前仅支持全部通过。", "PURCHASE_004");
        return ValidateActionAttachments(actor, request.Attachments, "验收附件");
    }

    private ServiceResult<IReadOnlyList<string>> ValidateActionAttachments(Employee actor, IReadOnlyList<string>? requested, string label)
    {
        var attachments = (requested ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
        return attachments.Count > 20 || attachments.Count > 0 && files is not null && !files.AreOwnedBy(actor, attachments)
            ? ServiceResult<IReadOnlyList<string>>.Failure($"{label}不存在、不属于当前用户或超过 20 个。", "FILE_005")
            : ServiceResult<IReadOnlyList<string>>.Success(attachments);
    }

    private void Apply(PurchaseRequestRecord record, SavePurchaseRequest request, IReadOnlyList<PurchaseItem> items, IReadOnlyList<string> attachments)
    {
        record.Title = request.Title.Trim();
        record.Purpose = request.Purpose.Trim();
        record.RequiredDate = request.RequiredDate;
        record.SuggestedSupplier = NormalizeOptional(request.SuggestedSupplier);
        record.ItemsJson = JsonSerializer.Serialize(items);
        record.ItemSearchText = string.Join(' ', items.Select(item => $"{item.Category} {item.Name} {item.Specification} {item.Remark}"));
        record.ItemCount = items.Count;
        record.EstimatedTotal = items.Sum(item => item.EstimatedAmount);
        record.AttachmentsJson = JsonSerializer.Serialize(attachments);

        var policyResult = ResolveProcurementPolicy();
        var policy = policyResult.IsSuccess ? policyResult.Value.Policy : BusinessConfigurationDefaults.CreateDefaultProcurementPolicy();
        var matchingTier = policy.AmountTiers.OrderBy(t => t.MaxAmount ?? decimal.MaxValue)
            .FirstOrDefault(t => t.MaxAmount == null || record.EstimatedTotal <= t.MaxAmount);
        record.AmountTier = matchingTier?.Name ?? "常规采购";
        record.Category = items.FirstOrDefault()?.Category ?? "其他";
        record.RequiresAcceptance = policy.RequiresAcceptance;
        record.AcceptanceRoleOrAssignee = policy.AcceptanceRoleOrAssignee;
    }

    private PurchaseRequest Load(PurchaseRequestRecord record)
    {
        var order = db.PurchaseOrders.AsNoTracking().SingleOrDefault(item => item.PurchaseRequestId == record.Id);
        var receipt = db.PurchaseReceipts.AsNoTracking().SingleOrDefault(item => item.PurchaseRequestId == record.Id);
        var isOverBudget = false;
        var overBudgetAmount = 0m;
        string? overBudgetPolicySnapshot = null;
        if (!string.IsNullOrEmpty(record.ConfigSnapshotJson) && record.ConfigSnapshotJson.Contains("\"isOverBudget\":true", StringComparison.OrdinalIgnoreCase))
        {
            isOverBudget = true;
            try
            {
                using var doc = JsonDocument.Parse(record.ConfigSnapshotJson);
                if (doc.RootElement.TryGetProperty("overBudgetSnapshot", out var obs))
                {
                    overBudgetAmount = obs.TryGetProperty("overBudgetAmount", out var oba) ? oba.GetDecimal() : 0m;
                    overBudgetPolicySnapshot = obs.GetRawText();
                }
            }
            catch { }
        }

        return new PurchaseRequest
        {
            Id = record.Id, Number = record.Number, ApplicantId = record.ApplicantId, ApplicantName = record.ApplicantName, DepartmentName = record.DepartmentName,
            Title = record.Title, Purpose = record.Purpose, RequiredDate = record.RequiredDate, SuggestedSupplier = record.SuggestedSupplier,
            Items = JsonSerializer.Deserialize<List<PurchaseItem>>(record.ItemsJson) ?? [], EstimatedTotal = record.EstimatedTotal,
            Attachments = DeserializeList(record.AttachmentsJson), CopyRecipientIds = copyRecipients.LoadRecipientIds(BusinessType, record.Id),
            Status = (PurchaseStatus)record.Status, PaymentStatus = record.PaymentStatus, PaidTotalAmount = record.PaidTotalAmount, PrepaymentLimitRate = record.PrepaymentLimitRate, Version = record.Version, IsDemo = record.IsDemo,
            Category = record.Category,
            AmountTier = record.AmountTier,
            PurchaserUserId = record.PurchaserUserId,
            PurchaserUserName = record.PurchaserUserId is not null ? data.FindEmployee(record.PurchaserUserId)?.Name : null,
            RequiresAcceptance = record.RequiresAcceptance,
            AcceptanceRoleOrAssignee = record.AcceptanceRoleOrAssignee,
            BudgetPoolId = record.BudgetPoolId,
            IsOverBudget = isOverBudget,
            OverBudgetAmount = overBudgetAmount,
            OverBudgetPolicySnapshot = overBudgetPolicySnapshot,
            ProcessDefinitionId = record.ProcessDefinitionId, ProcessDefinitionCode = record.ProcessDefinitionCode, ProcessDefinitionVersion = record.ProcessDefinitionVersion,
            CurrentFlowInstanceId = record.CurrentFlowInstanceId,
            ConfigVersionId = record.ConfigVersionId, ConfigVersionNumber = record.ConfigVersionNumber, ConfigSnapshotJson = record.ConfigSnapshotJson, ConfigResolvedAt = record.ConfigResolvedAt,
            CreatedAt = record.CreatedAt, UpdatedAt = record.UpdatedAt,
            Tasks = db.PurchaseTasks.AsNoTracking().Where(item => item.PurchaseRequestId == record.Id && item.FlowInstanceId == record.CurrentFlowInstanceId).OrderBy(item => item.Sequence).Select(ToTaskExpression()).ToList(),
            FlowInstances = flowInstances.Load(BusinessType, record.Id),
            Order = order is null ? null : new PurchaseOrder(order.Supplier, order.OrderNumber, order.ActualAmount, order.OrderDate, order.ExpectedDeliveryDate, order.Notes, DeserializeList(order.AttachmentsJson), order.CreatedBy, order.CreatedByName, order.CreatedAt),
            Receipt = receipt is null ? null : new PurchaseReceipt(receipt.ReceivedDate, receipt.Result, receipt.Notes, DeserializeList(receipt.AttachmentsJson), receipt.CreatedBy, receipt.CreatedByName, receipt.CreatedAt)
        };
    }

    public ServiceResult<PurchaseReconciliation> GetReconciliation(Employee actor, Guid id)
    {
        var record = db.PurchaseRequests.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<PurchaseReconciliation>.Failure("采购申请不存在。", "DATA_001");
        var subject = data.FindEmployee(record.ApplicantId);
        var directlyRelated = db.PurchaseTasks.AsNoTracking().Any(item => item.TenantId == TenantId && item.PurchaseRequestId == id && item.AssigneeId == actor.Id) ||
            copyRecipients.CanView(actor, BusinessType, id) ||
            data.HasPermission(actor, OaPermissions.PurchaseManage) ||
            data.HasPermission(actor, OaPermissions.ExpensePay);
        if (subject is null || !data.CanView(actor, subject, BusinessType) && !directlyRelated)
            return ServiceResult<PurchaseReconciliation>.Failure("无权查看该采购对账信息。", "AUTH_002");

        var order = db.PurchaseOrders.AsNoTracking().SingleOrDefault(item => item.PurchaseRequestId == id);
        var receipt = db.PurchaseReceipts.AsNoTracking().SingleOrDefault(item => item.PurchaseRequestId == id);
        var isAccepted = !record.RequiresAcceptance || (receipt is not null && receipt.Result == "ALL_ACCEPTED");
        var contractAmount = order?.ActualAmount ?? 0m;
        var limitRate = record.PrepaymentLimitRate > 0 ? record.PrepaymentLimitRate : 0.50m;
        var maxPrepayment = decimal.Round(contractAmount * limitRate, 2);
        var paymentsResult = paymentService.GetPayments(actor, BusinessType, id);
        var payments = paymentsResult.IsSuccess ? paymentsResult.Value! : [];

        var dto = new PurchaseReconciliation(
            PurchaseRequestId: record.Id,
            PurchaseRequestNumber: record.Number,
            EstimatedAmount: record.EstimatedTotal,
            OrderedAmount: contractAmount,
            AcceptedAmount: isAccepted ? contractAmount : 0m,
            PaidAmount: record.PaidTotalAmount,
            RemainingPayable: Math.Max(0m, contractAmount - record.PaidTotalAmount),
            PrepaymentLimitRate: limitRate,
            MaxPrepaymentAllowed: maxPrepayment,
            IsAcceptancePassed: isAccepted,
            PaymentStatus: record.PaymentStatus,
            Payments: payments);

        return ServiceResult<PurchaseReconciliation>.Success(dto);
    }

    public ServiceResult<PagedResponse<PurchaseReconciliationItem>> GetReconciliationsPaged(
        Employee actor,
        string? keyword = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        string? departmentId = null,
        string? status = null,
        int? page = null,
        int? pageSize = null)
    {
        var canViewFinance = data.HasPermission(actor, OaPermissions.ExpensePay) ||
                             data.HasPermission(actor, OaPermissions.ExpenseAllView) ||
                             data.HasPermission(actor, OaPermissions.PurchaseManage);
        if (!canViewFinance)
            return ServiceResult<PagedResponse<PurchaseReconciliationItem>>.Failure("无采购对账台账查看权限。", "AUTH_002");

        var curPage = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);

        var query = db.PurchaseRequests.AsNoTracking().Where(item => item.TenantId == TenantId);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var pattern = $"%{EscapeLike(keyword.Trim())}%";
            query = query.Where(item => EF.Functions.ILike(item.Number, pattern, "\\") ||
                                        EF.Functions.ILike(item.Title, pattern, "\\") ||
                                        EF.Functions.ILike(item.ApplicantName, pattern, "\\") ||
                                        (item.SuggestedSupplier != null && EF.Functions.ILike(item.SuggestedSupplier, pattern, "\\")));
        }

        if (startDate.HasValue)
            query = query.Where(item => item.CreatedAt >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(item => item.CreatedAt <= endDate.Value);
        if (!string.IsNullOrWhiteSpace(departmentId))
            query = query.Where(item => item.DepartmentName == departmentId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<PurchaseStatus>(status, true, out var parsedStatus))
                query = query.Where(item => item.Status == (int)parsedStatus);
        }

        var total = query.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)total / size));

        var requests = query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((curPage - 1) * size)
            .Take(size)
            .ToList();

        var requestIds = requests.Select(r => r.Id).ToList();
        var orders = db.PurchaseOrders.AsNoTracking()
            .Where(o => o.TenantId == TenantId && requestIds.Contains(o.PurchaseRequestId))
            .ToDictionary(o => o.PurchaseRequestId);
        var receipts = db.PurchaseReceipts.AsNoTracking()
            .Where(r => r.TenantId == TenantId && requestIds.Contains(r.PurchaseRequestId))
            .ToDictionary(r => r.PurchaseRequestId);

        var items = requests.Select(r =>
        {
            var order = orders.GetValueOrDefault(r.Id);
            var receipt = receipts.GetValueOrDefault(r.Id);
            var contractAmount = order?.ActualAmount ?? 0m;
            var isAccepted = !r.RequiresAcceptance || (receipt is not null && receipt.Result == "ALL_ACCEPTED") || r.Status == (int)PurchaseStatus.Received;
            var acceptedAmount = isAccepted ? contractAmount : 0m;
            var remainingAmount = Math.Max(0m, contractAmount - r.PaidTotalAmount);

            return new PurchaseReconciliationItem(
                Id: r.Id,
                Number: r.Number,
                ApplicantName: r.ApplicantName,
                DepartmentName: r.DepartmentName,
                EstimatedTotal: r.EstimatedTotal,
                OrderAmount: contractAmount,
                OrderNumber: order?.OrderNumber,
                Supplier: order?.Supplier ?? r.SuggestedSupplier,
                AcceptedAmount: acceptedAmount,
                PaidAmount: r.PaidTotalAmount,
                RemainingAmount: remainingAmount,
                Status: ((PurchaseStatus)r.Status).ToString(),
                CreatedAt: r.CreatedAt
            );
        }).ToList();

        return ServiceResult<PagedResponse<PurchaseReconciliationItem>>.Success(new PagedResponse<PurchaseReconciliationItem>(
            Items: items,
            Total: total,
            Page: curPage,
            PageSize: size,
            TotalPages: totalPages
        ));
    }

    private ServiceResult<PurchaseRequest> SaveAndReload(PurchaseRequestRecord record, string concurrencyMessage, string? uniqueCode = null)
    {
        try
        {
            db.SaveChanges();
            db.ChangeTracker.Clear();
            var reloaded = db.PurchaseRequests.AsNoTracking().Single(item => item.Id == record.Id);
            return ServiceResult<PurchaseRequest>.Success(Load(reloaded));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<PurchaseRequest>.Failure(concurrencyMessage, "CONCURRENCY_001");
        }
        catch (DbUpdateException exception) when (uniqueCode is not null && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ServiceResult<PurchaseRequest>.Failure("采购订单号已存在。", uniqueCode);
        }
    }

    private IReadOnlyList<Employee> ActivateCopies(PurchaseRequestRecord record, string status)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = db.FlowCopyRecipients.Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.BusinessId == record.Id && item.AvailableAt == null).ToList();
        foreach (var row in rows) { row.Status = status; row.AvailableAt = now; }
        return rows.Select(item => data.FindEmployee(item.RecipientId)).Where(item => item is not null).Cast<Employee>().ToList();
    }

    private void AddFlowAction(Guid instanceId, FlowActionType action, Employee actor, Guid? taskId = null, int? sequence = null, string? comment = null,
        string? fromId = null, string? fromName = null, string? toId = null, string? toName = null) =>
        db.FlowActions.Add(new FlowActionRecord
        {
            TenantId = TenantId, FlowInstanceId = instanceId, TaskId = taskId, Sequence = sequence, Action = (int)action,
            ActorId = actor.Id, ActorName = actor.Name, Comment = NormalizeOptional(comment), FromAssigneeId = fromId, FromAssigneeName = fromName,
            ToAssigneeId = toId, ToAssigneeName = toName
        });

    private void Audit(Employee actor, string action, PurchaseRequestRecord record, string summary) => db.AuditLogs.Add(new AuditRecord
    {
        TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "PurchaseRequest", ResourceId = record.Id.ToString(), Summary = summary
    });

    private static System.Linq.Expressions.Expression<Func<PurchaseTaskRecord, PurchaseTask>> ToTaskExpression() => item => new PurchaseTask(
        item.Id, item.PurchaseRequestId, item.FlowInstanceId, item.AssigneeId, item.AssigneeName, item.OriginalAssigneeId, item.OriginalAssigneeName,
        item.DelegationId, item.Sequence, (FlowTaskStatus)item.Status, item.Comment, item.ProcessedAt, item.Version);

    private static DateOnly ChinaDate(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(BusinessTime.ChinaOffset).DateTime);
    private static List<string> DeserializeList(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
