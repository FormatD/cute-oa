using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Oa.Api.Domain;
using Oa.Api.Persistence;

using Microsoft.Extensions.Logging;

namespace Oa.Api.Services;

public sealed class SealService
{
    private const string TenantId = "demo";
    private const string BusinessType = "Seal";
    private readonly ILogger<SealService>? logger;

    private ServiceResult<(SealPolicyConfig Policy, BusinessConfigurationRecord? Record)> ResolveSealPolicy()
    {
        var configRecord = BusinessConfigurationDefaults.ResolveEffectiveConfig(db, ConfigurationDomains.Seal, "SealPolicy");
        if (db is not null && configRecord is null)
        {
            logger?.LogWarning("未找到生效中的用章管理策略配置【SealPolicy】。租户：{TenantId}", TenantId);
            return ServiceResult<(SealPolicyConfig, BusinessConfigurationRecord?)>.Failure("未找到生效中的用章管理策略配置【SealPolicy】。", "CONFIG_MISSING");
        }

        SealPolicyConfig? policy = null;
        if (configRecord is not null)
        {
            try
            {
                policy = JsonSerializer.Deserialize<SealPolicyConfig>(configRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "用章管理策略配置内容损坏，无法解析。租户：{TenantId}", TenantId);
                return ServiceResult<(SealPolicyConfig, BusinessConfigurationRecord?)>.Failure("用章管理策略配置内容损坏，无法解析。", "CONFIG_INVALID");
            }
            if (policy is null)
            {
                logger?.LogError("用章管理策略配置内容损坏，反序列化为 null。租户：{TenantId}", TenantId);
                return ServiceResult<(SealPolicyConfig, BusinessConfigurationRecord?)>.Failure("用章管理策略配置内容损坏，无法解析。", "CONFIG_INVALID");
            }
        }
        else
        {
            policy = BusinessConfigurationDefaults.CreateDefaultSealPolicy();
        }

        return ServiceResult<(SealPolicyConfig, BusinessConfigurationRecord?)>.Success((policy, configRecord));
    }

    private readonly DemoData data;
    private readonly OaDbContext db;
    private readonly NotificationService notifications;
    private readonly FileService? files;
    private readonly IProcessRouter processRouter;
    private readonly FlowInstanceService flowInstances;
    private readonly FlowCopyService copyRecipients;

    public SealService(
        DemoData data,
        OaDbContext db,
        NotificationService? notifications = null,
        FileService? files = null,
        IProcessRouter? processRouter = null,
        FlowInstanceService? flowInstances = null,
        FlowCopyService? copyRecipients = null,
        ILogger<SealService>? logger = null)
    {
        this.data = data;
        this.db = db;
        this.notifications = notifications ?? new NotificationService(db);
        this.files = files;
        this.processRouter = processRouter ?? new DefaultProcessRouter(data);
        this.flowInstances = flowInstances ?? new FlowInstanceService(db);
        this.copyRecipients = copyRecipients ?? new FlowCopyService(data, db);
        this.logger = logger;
    }

    public PagedResponse<SealRequestListItem> List(Employee actor, DocumentListQuery query, int? requestedPage, int? requestedPageSize)
    {
        var pageSize = Math.Clamp(requestedPageSize ?? 20, 1, 100);
        var visibleApplicantIds = data.ActiveEmployees.Where(subject => data.CanView(actor, subject, BusinessType)).Select(item => item.Id).ToList();
        var directRequestIds = db.SealTasks.AsNoTracking().Where(item => item.TenantId == TenantId && item.AssigneeId == actor.Id).Select(item => item.SealRequestId)
            .Union(db.FlowCopyRecipients.AsNoTracking().Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.RecipientId == actor.Id && item.AvailableAt != null).Select(item => item.BusinessId));
        var source = db.SealRequests.AsNoTracking().Where(item => item.TenantId == TenantId && (visibleApplicantIds.Contains(item.ApplicantId) || directRequestIds.Contains(item.Id)));

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = $"%{EscapeLike(query.Keyword.Trim())}%";
            source = source.Where(item => EF.Functions.ILike(item.Number, pattern, "\\") || EF.Functions.ILike(item.Title, pattern, "\\") ||
                EF.Functions.ILike(item.DocumentName, pattern, "\\") || EF.Functions.ILike(item.ApplicantName, pattern, "\\") || EF.Functions.ILike(item.Reason, pattern, "\\"));
        }
        if (query.Status is not null) source = source.Where(item => item.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.ApplicantId)) source = source.Where(item => item.ApplicantId == query.ApplicantId);

        var total = source.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)pageSize));
        var page = Math.Clamp(requestedPage ?? 1, 1, totalPages);
        var items = source.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new SealRequestListItem(item.Id, item.Number, item.ApplicantId, item.ApplicantName, item.DepartmentName,
                item.Title, item.DocumentCategory, item.DocumentName, item.SealType, item.Copies, item.IsOut, (SealStatus)item.Status, item.Version, item.IsDemo, item.CreatedAt, item.UpdatedAt))
            .ToList();
        return new PagedResponse<SealRequestListItem>(items, total, page, pageSize, totalPages);
    }

    public ServiceResult<SealRequest> Get(Employee actor, Guid id)
    {
        var record = db.SealRequests.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<SealRequest>.Failure("用章申请不存在。", "DATA_001");
        var subject = data.FindEmployee(record.ApplicantId);
        var directlyRelated = db.SealTasks.AsNoTracking().Any(item => item.TenantId == TenantId && item.SealRequestId == id && item.AssigneeId == actor.Id) ||
            copyRecipients.CanView(actor, BusinessType, id);
        if (subject is null || !data.CanView(actor, subject, BusinessType) && !directlyRelated)
            return ServiceResult<SealRequest>.Failure("无权查看该用章申请。", "AUTH_002");
        return ServiceResult<SealRequest>.Success(Load(record));
    }

    public IReadOnlyList<SealTask> GetPendingTasks(Employee actor) => db.SealTasks.AsNoTracking()
        .Where(task => task.TenantId == TenantId && task.AssigneeId == actor.Id && task.Status == (int)FlowTaskStatus.Pending)
        .Where(task => db.SealRequests.Any(request => request.Id == task.SealRequestId && request.Status == (int)SealStatus.Approving && request.CurrentFlowInstanceId == task.FlowInstanceId))
        .Where(task => !db.SealTasks.Any(previous => previous.SealRequestId == task.SealRequestId && previous.FlowInstanceId == task.FlowInstanceId && previous.Status == (int)FlowTaskStatus.Pending && previous.Sequence < task.Sequence))
        .OrderBy(task => task.Sequence).ThenBy(task => task.Id).Select(ToTaskExpression()).ToList();

    public IReadOnlyList<SealTask> GetProcessedTasks(Employee actor) => db.SealTasks.AsNoTracking()
        .Where(task => task.TenantId == TenantId && task.AssigneeId == actor.Id && (task.Status == (int)FlowTaskStatus.Approved || task.Status == (int)FlowTaskStatus.Rejected))
        .OrderByDescending(task => task.ProcessedAt).Select(ToTaskExpression()).ToList();

    public ServiceResult<SealRequest> CreateDraft(Employee actor, SaveSealRequest request) => CreateDraft(actor, request, false);

    public ServiceResult<SealRequest> CreateDraft(Employee actor, SaveSealRequest request, bool isDemo)
    {
        var validation = Validate(actor, request);
        if (!validation.IsSuccess) return ServiceResult<SealRequest>.Failure(validation.Error!, validation.Code!);

        var today = BusinessTime.ChinaToday();
        var policyResult = ResolveSealPolicy();
        if (!policyResult.IsSuccess) return ServiceResult<SealRequest>.Failure(policyResult.Error!, policyResult.Code!);
        var (policy, configRecord) = policyResult.Value;
        var sealItem = validation.Value!.SealItem;
        var docCategory = validation.Value.DocCategory;
        var riskLevel = docCategory.RiskLevel ?? "LOW";

        var riskRules = policy.RiskRules ?? new SealRiskRulesConfig();
        decimal metric = riskLevel == "HIGH" || request.IsOut || sealItem.SealType == "法人章"
            ? riskRules.HighRiskMetric
            : (riskLevel == "MEDIUM" || request.Copies > 3 ? riskRules.MediumRiskMetric : riskRules.LowRiskMetric);

        var record = new SealRequestRecord
        {
            TenantId = TenantId,
            Number = GenerateNumber(today),
            ApplicantId = actor.Id,
            ApplicantName = actor.Name,
            DepartmentName = actor.DepartmentName,
            Title = request.Title.Trim(),
            DocumentCategory = docCategory.Name,
            DocumentName = request.DocumentName.Trim(),
            SealType = sealItem.Name,
            CustodianUserId = sealItem.CustodianUserId,
            MaxOutDays = sealItem.MaxOutDays,
            RiskMetric = metric,
            Copies = request.Copies,
            IsOut = request.IsOut,
            OutStartDate = request.IsOut ? request.OutStartDate : null,
            OutEndDate = request.IsOut ? request.OutEndDate : null,
            OutCustodian = request.IsOut ? request.OutCustodian?.Trim() : null,
            Reason = request.Reason.Trim(),
            AttachmentsJson = JsonSerializer.Serialize(validation.Value.Attachments),
            Status = (int)SealStatus.Draft,
            Version = 1,
            IsDemo = isDemo,
            RiskLevel = riskLevel,
            ConfigVersionId = configRecord?.Id,
            ConfigVersionNumber = configRecord?.Version,
            ConfigSnapshotJson = configRecord?.ContentJson,
            ConfigResolvedAt = configRecord is not null ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.SealRequests.Add(record);
        copyRecipients.Track(BusinessType, record.Id, record.Number, record.ApplicantName, record.Title, validation.Value.CopyRecipientIds);
        Audit(actor, "SEAL_DRAFT_CREATED", record, $"保存用章草稿 {record.Number}");
        return SaveAndReload(record, "用章申请编号冲突，请重试。");
    }

    public ServiceResult<SealRequest> Update(Employee actor, Guid id, int expectedVersion, SaveSealRequest request)
    {
        var record = db.SealRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id && item.ApplicantId == actor.Id);
        if (record is null) return ServiceResult<SealRequest>.Failure("用章申请不存在或无权限。", "DATA_001");
        if ((SealStatus)record.Status is not (SealStatus.Draft or SealStatus.Rejected or SealStatus.Withdrawn))
            return ServiceResult<SealRequest>.Failure("当前状态不允许编辑。", "STATE_001");
        if (record.Version != expectedVersion) return ServiceResult<SealRequest>.Failure("单据已被更新，请刷新后重试。", "CONCURRENCY_001");
        var validation = Validate(actor, request);
        if (!validation.IsSuccess) return ServiceResult<SealRequest>.Failure(validation.Error!, validation.Code!);

        var policyResult = ResolveSealPolicy();
        if (!policyResult.IsSuccess) return ServiceResult<SealRequest>.Failure(policyResult.Error!, policyResult.Code!);
        var (policy, _) = policyResult.Value;
        var sealItem = validation.Value!.SealItem;
        var docCategory = validation.Value.DocCategory;
        var riskLevel = docCategory.RiskLevel ?? "LOW";

        var riskRules = policy.RiskRules ?? new SealRiskRulesConfig();
        decimal metric = riskLevel == "HIGH" || request.IsOut || sealItem.SealType == "法人章"
            ? riskRules.HighRiskMetric
            : (riskLevel == "MEDIUM" || request.Copies > 3 ? riskRules.MediumRiskMetric : riskRules.LowRiskMetric);

        record.Title = request.Title.Trim();
        record.DocumentCategory = docCategory.Name;
        record.DocumentName = request.DocumentName.Trim();
        record.SealType = sealItem.Name;
        record.CustodianUserId = sealItem.CustodianUserId;
        record.MaxOutDays = sealItem.MaxOutDays;
        record.RiskMetric = metric;
        record.RiskLevel = riskLevel;
        record.Copies = request.Copies;
        record.IsOut = request.IsOut;
        record.OutStartDate = request.IsOut ? request.OutStartDate : null;
        record.OutEndDate = request.IsOut ? request.OutEndDate : null;
        record.OutCustodian = request.IsOut ? request.OutCustodian?.Trim() : null;
        record.Reason = request.Reason.Trim();
        record.AttachmentsJson = JsonSerializer.Serialize(validation.Value.Attachments);
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        copyRecipients.Track(BusinessType, record.Id, record.Number, record.ApplicantName, record.Title, validation.Value.CopyRecipientIds);
        Audit(actor, "SEAL_UPDATED", record, "编辑用章申请");
        return SaveAndReload(record, "用章申请存在并发修改，请刷新后重试。");
    }

    public ServiceResult<SealRequest> Submit(Employee actor, Guid id)
    {
        var record = db.SealRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id && item.ApplicantId == actor.Id);
        if (record is null) return ServiceResult<SealRequest>.Failure("用章申请不存在或无权限。", "DATA_001");
        if ((SealStatus)record.Status is not (SealStatus.Draft or SealStatus.Rejected or SealStatus.Withdrawn))
            return ServiceResult<SealRequest>.Failure("当前状态不允许提交。", "STATE_001");

        var policyResult = ResolveSealPolicy();
        if (!policyResult.IsSuccess) return ServiceResult<SealRequest>.Failure(policyResult.Error!, policyResult.Code!);
        var (policy, configRecord) = policyResult.Value;

        var normSealType = NormalizeSealType(record.SealType);
        var sealItem = policy.Seals.FirstOrDefault(s => (!string.IsNullOrWhiteSpace(s.Code) && s.Code.Equals(normSealType, StringComparison.OrdinalIgnoreCase)) || s.Name.Equals(normSealType, StringComparison.OrdinalIgnoreCase) || s.SealType.Equals(normSealType, StringComparison.OrdinalIgnoreCase));
        if (sealItem is null)
            return ServiceResult<SealRequest>.Failure($"印章【{record.SealType}】不存在或已失效。", "SEAL_005");
        if (!sealItem.IsEnabled)
            return ServiceResult<SealRequest>.Failure($"印章【{sealItem.Name}】已被系统停用，无法申请。", "SEAL_005");

        if (record.IsOut)
        {
            if (!sealItem.AllowOut)
                return ServiceResult<SealRequest>.Failure($"印章【{sealItem.Name}】禁止外带使用。", "SEAL_006");
            if (record.OutStartDate.HasValue && record.OutEndDate.HasValue)
            {
                var diffDays = record.OutEndDate.Value.DayNumber - record.OutStartDate.Value.DayNumber;
                if (diffDays > sealItem.MaxOutDays)
                    return ServiceResult<SealRequest>.Failure($"印章【{sealItem.Name}】最长外带天数为 {sealItem.MaxOutDays} 天，当前申请天数超出限制。", "SEAL_002");
            }
        }

        var docCategory = policy.DocumentCategories.FirstOrDefault(r => (!string.IsNullOrWhiteSpace(r.Code) && r.Code.Equals(record.DocumentCategory, StringComparison.OrdinalIgnoreCase)) || r.Name.Equals(record.DocumentCategory, StringComparison.OrdinalIgnoreCase));
        if (docCategory is null)
            return ServiceResult<SealRequest>.Failure($"文件类别【{record.DocumentCategory}】不存在或已失效。", "SEAL_007");
        if (!docCategory.IsEnabled)
            return ServiceResult<SealRequest>.Failure($"文件类别【{docCategory.Name}】已被系统停用，无法申请。", "SEAL_007");

        var riskLevel = docCategory.RiskLevel ?? "LOW";
        record.RiskLevel = riskLevel;
        record.CustodianUserId = sealItem.CustodianUserId;
        record.MaxOutDays = sealItem.MaxOutDays;

        var riskRules = policy.RiskRules ?? new SealRiskRulesConfig();
        decimal metric = riskLevel == "HIGH" || record.IsOut || sealItem.SealType == "法人章"
            ? riskRules.HighRiskMetric
            : (riskLevel == "MEDIUM" || record.Copies > 3 ? riskRules.MediumRiskMetric : riskRules.LowRiskMetric);
        record.RiskMetric = metric;

        if (configRecord is not null)
        {
            record.ConfigVersionId = configRecord.Id;
            record.ConfigVersionNumber = configRecord.Version;
            record.ConfigSnapshotJson = configRecord.ContentJson;
            record.ConfigResolvedAt = DateTimeOffset.UtcNow;
        }

        var route = processRouter.Resolve(BusinessType, actor, metric, record.SealType);
        if (!route.IsSuccess) return ServiceResult<SealRequest>.Failure(route.Error!, route.Code!);

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
        var approvalTasks = route.Value.Approvers.Select((approver, index) => new SealTaskRecord
            {
                TenantId = TenantId, SealRequestId = record.Id, FlowInstanceId = instance.Id,
                AssigneeId = approver.Assignee.Id, AssigneeName = approver.Assignee.Name,
                OriginalAssigneeId = approver.DelegationId is null ? null : approver.OriginalApprover.Id,
                OriginalAssigneeName = approver.DelegationId is null ? null : approver.OriginalApprover.Name,
                DelegationId = approver.DelegationId, Sequence = index + 1, Status = (int)FlowTaskStatus.Pending
            }).ToList();
        db.SealTasks.AddRange(approvalTasks);
        FlowInstanceService.RegisterTasks(db, instance.Id, instance.StartedAt, BusinessType,
            approvalTasks.Select((task, index) => new ResolvedFlowTask(task.Id, task.Sequence, route.Value.Approvers[index])).ToList());
        record.Status = (int)SealStatus.Approving;
        record.ProcessDefinitionId = route.Value.DefinitionId;
        record.ProcessDefinitionCode = route.Value.Code;
        record.ProcessDefinitionVersion = route.Value.Version;
        record.CurrentFlowInstanceId = instance.Id;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "SEAL_SUBMITTED", record, $"提交用章审批，文件：{record.DocumentName}（{record.SealType}，{record.Copies}份）");
        var first = route.Value.Approvers.First().Assignee;
        notifications.Enqueue(first.Id, "TODO_CREATED", "新增用章审批待办", $"{actor.Name} 提交了 {record.Number}", "SealRequest", record.Id);
        return SaveAndReload(record, "用章申请已被其他操作更新，请刷新后重试。");
    }

    public ServiceResult<SealRequest> Approve(Employee actor, Guid taskId, string? comment) => Decide(actor, taskId, true, comment);

    public ServiceResult<SealRequest> Reject(Employee actor, Guid taskId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return ServiceResult<SealRequest>.Failure("驳回必须填写意见。", "VALIDATION_001");
        if (comment.Trim().Length > 500) return ServiceResult<SealRequest>.Failure("审批意见最多 500 个字符。", "VALIDATION_001");
        return Decide(actor, taskId, false, comment);
    }

    public ServiceResult<SealRequest> Transfer(Employee actor, Guid taskId, TransferTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Comment)) return ServiceResult<SealRequest>.Failure("转办必须填写意见。", "VALIDATION_001");
        if (request.Comment.Trim().Length > 500) return ServiceResult<SealRequest>.Failure("转办意见最多 500 个字符。", "VALIDATION_001");
        var task = db.SealTasks.SingleOrDefault(item => item.TenantId == TenantId && item.Id == taskId);
        if (task is null) return ServiceResult<SealRequest>.Failure("用章审批任务不存在。", "DATA_001");
        var seal = db.SealRequests.Single(item => item.Id == task.SealRequestId);
        if (task.AssigneeId != actor.Id || task.Status != (int)FlowTaskStatus.Pending || seal.Status != (int)SealStatus.Approving || seal.CurrentFlowInstanceId != task.FlowInstanceId)
            return ServiceResult<SealRequest>.Failure("当前用户不能转办该用章审批任务。", "AUTH_002");
        var assignee = data.FindEmployee(request.AssigneeId);
        if (assignee is null || assignee.Status != "ACTIVE" || assignee.Id == actor.Id)
            return ServiceResult<SealRequest>.Failure("转办人不存在、已停用或不能为当前审批人。", "FLOW_002");
        var previousId = task.AssigneeId;
        var previousName = task.AssigneeName;
        task.AssigneeId = assignee.Id;
        task.AssigneeName = assignee.Name;
        task.Version++;
        AddFlowAction(task.FlowInstanceId!.Value, FlowActionType.Transferred, actor, task.Id, task.Sequence, request.Comment.Trim(), previousId, previousName, assignee.Id, assignee.Name);
        FlowInstanceService.UpdateTaskSla(db, task.FlowInstanceId.Value, FlowActionType.Transferred, task.Id, assignee);
        Audit(actor, "SEAL_TASK_TRANSFERRED", seal, $"将第 {task.Sequence} 节点转办给 {assignee.Name}：{request.Comment.Trim()}");
        notifications.Enqueue(assignee.Id, "TODO_TRANSFERRED", "用章审批待办已转办给你", $"{seal.Number}：{request.Comment.Trim()}", "SealRequest", seal.Id);
        return SaveAndReload(seal, "用章审批任务已被其他操作处理，请刷新后重试。");
    }

    public ServiceResult<SealRequest> Withdraw(Employee actor, Guid id)
    {
        var record = db.SealRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id && item.ApplicantId == actor.Id);
        if (record is null) return ServiceResult<SealRequest>.Failure("用章申请不存在或无权限。", "DATA_001");
        if (record.Status != (int)SealStatus.Approving || record.CurrentFlowInstanceId is null)
            return ServiceResult<SealRequest>.Failure("仅尚未处理的审批中申请可撤回。", "STATE_001");
        var tasks = db.SealTasks.Where(item => item.SealRequestId == id && item.FlowInstanceId == record.CurrentFlowInstanceId).ToList();
        if (tasks.Count == 0 || tasks.Any(item => item.Status != (int)FlowTaskStatus.Pending))
            return ServiceResult<SealRequest>.Failure("审批节点已有处理记录，不能撤回。", "STATE_001");
        foreach (var task in tasks) { task.Status = (int)FlowTaskStatus.Cancelled; task.Version++; }
        record.Status = (int)SealStatus.Withdrawn;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        var instance = db.FlowInstances.Single(item => item.Id == record.CurrentFlowInstanceId);
        instance.Status = (int)FlowInstanceStatus.Withdrawn;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        AddFlowAction(instance.Id, FlowActionType.Withdrawn, actor, comment: "申请人撤回");
        FlowInstanceService.UpdateTaskSla(db, instance.Id, FlowActionType.Withdrawn);
        var copied = ActivateCopies(record, SealStatus.Withdrawn.ToString());
        Audit(actor, "SEAL_WITHDRAWN", record, "撤回用章申请");
        foreach (var assignee in tasks.Select(item => item.AssigneeId).Distinct().Where(item => item != actor.Id))
            notifications.Enqueue(assignee, "SEAL_WITHDRAWN", "用章申请已撤回", $"{record.Number} 已被申请人撤回", "SealRequest", record.Id);
        foreach (var recipient in copied) notifications.Enqueue(recipient.Id, "FLOW_COPY", "用章抄送事项已撤回", $"{record.ApplicantName} 的 {record.Number} 已撤回", "SealRequest", record.Id);
        return SaveAndReload(record, "用章申请已被其他操作更新，请刷新后重试。");
    }

    public ServiceResult<bool> Delete(Employee actor, Guid id)
    {
        var record = db.SealRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id && item.ApplicantId == actor.Id);
        if (record is null) return ServiceResult<bool>.Failure("用章申请不存在或无权限。", "DATA_001");
        if ((SealStatus)record.Status is not (SealStatus.Draft or SealStatus.Rejected or SealStatus.Withdrawn))
            return ServiceResult<bool>.Failure("当前状态不允许删除。", "STATE_001");
        var instanceIds = db.FlowInstances.Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.BusinessId == id).Select(item => item.Id).ToList();
        db.FlowCopyRecipients.Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.BusinessId == id).ExecuteDelete();
        db.SealTasks.Where(item => item.SealRequestId == id).ExecuteDelete();
        db.FlowActions.Where(item => instanceIds.Contains(item.FlowInstanceId)).ExecuteDelete();
        db.FlowInstances.Where(item => instanceIds.Contains(item.Id)).ExecuteDelete();
        db.SealRequests.Remove(record);
        Audit(actor, "SEAL_DELETED", record, "删除用章申请");
        db.SaveChanges();
        return ServiceResult<bool>.Success(true);
    }

    public ServiceResult<SealRequest> RegisterExecution(Employee actor, Guid id, RegisterSealExecutionRequest request)
    {
        var record = db.SealRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<SealRequest>.Failure("用章申请不存在。", "DATA_001");

        var isCustodian = !string.IsNullOrWhiteSpace(record.CustodianUserId) && record.CustodianUserId == actor.Id;
        var hasManagePerm = data.HasPermission(actor, OaPermissions.SealManage);
        if (!hasManagePerm && !isCustodian)
            return ServiceResult<SealRequest>.Failure("无印章管理执行权限。", "AUTH_002");

        var subject = data.FindEmployee(record.ApplicantId);
        if (subject is null || (!isCustodian && !data.CanView(actor, subject, BusinessType)))
            return ServiceResult<SealRequest>.Failure("印章管理权限不覆盖该申请。", "AUTH_002");

        if (record.Status != (int)SealStatus.Approved || db.SealExecutions.Any(item => item.SealRequestId == id)) return ServiceResult<SealRequest>.Failure("仅已批准且未用印的申请可登记用印/借出。", "STATE_001");
        if (record.Version != request.Version) return ServiceResult<SealRequest>.Failure("单据已被更新，请刷新后重试。", "CONCURRENCY_001");

        var today = BusinessTime.ChinaToday();
        var createdDate = ChinaDate(record.CreatedAt);
        if (request.ExecutedDate < createdDate || request.ExecutedDate > today)
            return ServiceResult<SealRequest>.Failure($"执行日期必须在申请日 {createdDate:yyyy-MM-dd} 与今日 {today:yyyy-MM-dd} 之间。", "SEAL_003");
        if (string.IsNullOrWhiteSpace(request.OperatorName) || request.OperatorName.Trim().Length > 50)
            return ServiceResult<SealRequest>.Failure("经办人姓名应为 1–50 个字符。", "SEAL_003");
        if (request.Notes?.Trim().Length > 500)
            return ServiceResult<SealRequest>.Failure("登记说明最多 500 个字符。", "SEAL_003");

        var validAttachments = NormalizeAttachments(actor, request.Attachments);
        if (validAttachments is null) return ServiceResult<SealRequest>.Failure("包含无效或越权附件。", "FILE_005");

        db.SealExecutions.Add(new SealExecutionRecord
        {
            TenantId = TenantId,
            SealRequestId = id,
            ExecutedDate = request.ExecutedDate,
            OperatorName = request.OperatorName.Trim(),
            Notes = NormalizeOptional(request.Notes),
            AttachmentsJson = JsonSerializer.Serialize(validAttachments),
            CreatedBy = actor.Id,
            CreatedByName = actor.Name,
            CreatedAt = DateTimeOffset.UtcNow
        });

        if (record.IsOut)
        {
            record.Status = (int)SealStatus.Out;
            Audit(actor, "SEAL_OUT", record, $"登记外带借出，经办人：{request.OperatorName.Trim()}");
            notifications.Enqueue(record.ApplicantId, "SEAL_OUT", "用章申请已登记外带借出", $"{record.Number} 已办理借出，请妥善保管并在使用完毕后及时归还", "SealRequest", record.Id);
        }
        else
        {
            record.Status = (int)SealStatus.Executed;
            Audit(actor, "SEAL_EXECUTED", record, $"登记在司用印，经办人：{request.OperatorName.Trim()}");
            notifications.Enqueue(record.ApplicantId, "SEAL_EXECUTED", "用章申请已完成盖章", $"{record.Number} 已由 {request.OperatorName.Trim()} 完成盖章登记", "SealRequest", record.Id);
        }

        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        return SaveAndReload(record, "用章申请已被其他操作更新，请刷新后重试。");
    }

    public ServiceResult<SealRequest> RegisterReturn(Employee actor, Guid id, RegisterSealReturnRequest request)
    {
        if (!data.HasPermission(actor, OaPermissions.SealManage)) return ServiceResult<SealRequest>.Failure("无印章管理执行权限。", "AUTH_002");
        var record = db.SealRequests.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<SealRequest>.Failure("用章申请不存在。", "DATA_001");
        var subject = data.FindEmployee(record.ApplicantId);
        if (subject is null || !data.CanView(actor, subject, BusinessType)) return ServiceResult<SealRequest>.Failure("印章管理权限不覆盖该申请。", "AUTH_002");
        var execution = db.SealExecutions.AsNoTracking().SingleOrDefault(item => item.SealRequestId == id);
        if (record.Status != (int)SealStatus.Out || execution is null || db.SealReturns.Any(item => item.SealRequestId == id))
            return ServiceResult<SealRequest>.Failure("仅处于借出中的申请可登记归还。", "STATE_001");
        if (record.Version != request.Version) return ServiceResult<SealRequest>.Failure("单据已被更新，请刷新后重试。", "CONCURRENCY_001");

        var today = BusinessTime.ChinaToday();
        if (request.ReturnDate < execution.ExecutedDate || request.ReturnDate > today)
            return ServiceResult<SealRequest>.Failure($"归还日期必须在借出日 {execution.ExecutedDate:yyyy-MM-dd} 与今日 {today:yyyy-MM-dd} 之间。", "SEAL_004");
        if (request.SealCondition is not ("INTACT" or "DAMAGED" or "LOST"))
            return ServiceResult<SealRequest>.Failure("印章状态仅支持完好（INTACT）、损坏（DAMAGED）或遗失（LOST）。", "SEAL_004");
        if (string.IsNullOrWhiteSpace(request.ReceiverName) || request.ReceiverName.Trim().Length > 50)
            return ServiceResult<SealRequest>.Failure("接收人姓名应为 1–50 个字符。", "SEAL_004");
        if (request.Notes?.Trim().Length > 500)
            return ServiceResult<SealRequest>.Failure("归还说明最多 500 个字符。", "SEAL_004");

        var validAttachments = NormalizeAttachments(actor, request.Attachments);
        if (validAttachments is null) return ServiceResult<SealRequest>.Failure("包含无效或越权附件。", "FILE_005");

        db.SealReturns.Add(new SealReturnRecord
        {
            TenantId = TenantId,
            SealRequestId = id,
            ReturnDate = request.ReturnDate,
            SealCondition = request.SealCondition,
            ReceiverName = request.ReceiverName.Trim(),
            Notes = NormalizeOptional(request.Notes),
            AttachmentsJson = JsonSerializer.Serialize(validAttachments),
            CreatedBy = actor.Id,
            CreatedByName = actor.Name,
            CreatedAt = DateTimeOffset.UtcNow
        });

        record.Status = (int)SealStatus.Returned;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "SEAL_RETURNED", record, $"登记外带归还，接收人：{request.ReceiverName.Trim()}，状态：{request.SealCondition}");
        notifications.Enqueue(record.ApplicantId, "SEAL_RETURNED", "外带印章已归还核验", $"{record.Number} 外带借出已归还（状态：{request.SealCondition}）", "SealRequest", record.Id);
        return SaveAndReload(record, "用章申请已被其他操作更新，请刷新后重试。");
    }

    public ServiceResult<GenerateSealDemoResult> GenerateDemoData(Employee actor)
    {
        if (!data.HasPermission(actor, OaPermissions.SealManage)) return ServiceResult<GenerateSealDemoResult>.Failure("无用章演示数据生成权限。", "AUTH_002");
        if (db.SealRequests.Any(item => item.TenantId == TenantId && item.IsDemo && item.ApplicantId == actor.Id))
            return ServiceResult<GenerateSealDemoResult>.Success(new GenerateSealDemoResult(0, 1));
        var result = CreateDraft(actor, new SaveSealRequest(
            "技术服务框架协议盖章", "合同协议", "2026年企业级技术支持服务协议", "合同专用章", 2, false,
            null, null, null, "甲乙双方年度技术服务框架签署"), true);
        return result.IsSuccess
            ? ServiceResult<GenerateSealDemoResult>.Success(new GenerateSealDemoResult(1, 0))
            : ServiceResult<GenerateSealDemoResult>.Failure(result.Error!, result.Code!);
    }

    private ServiceResult<SealRequest> Decide(Employee actor, Guid taskId, bool approve, string? comment)
    {
        var task = db.SealTasks.SingleOrDefault(item => item.TenantId == TenantId && item.Id == taskId);
        if (task is null) return ServiceResult<SealRequest>.Failure("用章审批任务不存在。", "DATA_001");
        var record = db.SealRequests.Single(item => item.Id == task.SealRequestId);
        if (task.AssigneeId != actor.Id || task.Status != (int)FlowTaskStatus.Pending || record.Status != (int)SealStatus.Approving || record.CurrentFlowInstanceId != task.FlowInstanceId)
            return ServiceResult<SealRequest>.Failure("当前用户不能处理该用章审批任务。", "AUTH_002");

        var instance = db.FlowInstances.Single(item => item.Id == task.FlowInstanceId);
        var tasks = db.SealTasks.Where(item => item.SealRequestId == record.Id && item.FlowInstanceId == instance.Id).OrderBy(item => item.Sequence).ToList();
        var currentSequence = tasks.Where(item => item.Status == (int)FlowTaskStatus.Pending).Min(item => item.Sequence);
        if (task.Sequence != currentSequence) return ServiceResult<SealRequest>.Failure("当前节点尚未轮到审批。", "STATE_001");

        task.Status = (int)(approve ? FlowTaskStatus.Approved : FlowTaskStatus.Rejected);
        task.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        task.ProcessedAt = DateTimeOffset.UtcNow;
        task.Version++;
        AddFlowAction(instance.Id, approve ? FlowActionType.Approved : FlowActionType.Rejected, actor, task.Id, task.Sequence, task.Comment);
        FlowInstanceService.UpdateTaskSla(db, instance.Id, approve ? FlowActionType.Approved : FlowActionType.Rejected, task.Id);

        if (!approve)
        {
            foreach (var remaining in tasks.Where(item => item.Sequence > task.Sequence && item.Status == (int)FlowTaskStatus.Pending))
            {
                remaining.Status = (int)FlowTaskStatus.Cancelled;
                remaining.Version++;
            }
            record.Status = (int)SealStatus.Rejected;
            instance.Status = (int)FlowInstanceStatus.Rejected;
            instance.CompletedAt = DateTimeOffset.UtcNow;
            var copied = ActivateCopies(record, SealStatus.Rejected.ToString());
            Audit(actor, "SEAL_REJECTED", record, $"驳回第 {task.Sequence} 节点：{task.Comment}");
            notifications.Enqueue(record.ApplicantId, "SEAL_REJECTED", "用章申请已被驳回", $"{record.Number}：{task.Comment}", "SealRequest", record.Id);
            foreach (var recipient in copied) notifications.Enqueue(recipient.Id, "FLOW_COPY", "用章抄送事项已驳回", $"{record.ApplicantName} 的 {record.Number} 已被驳回", "SealRequest", record.Id);
            record.Version++;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            return SaveAndReload(record, "用章审批已被其他操作更新，请刷新后重试。");
        }

        var next = tasks.FirstOrDefault(item => item.Sequence > task.Sequence && item.Status == (int)FlowTaskStatus.Pending);
        if (next is not null)
        {
            Audit(actor, "SEAL_APPROVED", record, $"通过第 {task.Sequence} 节点，流转至 {next.AssigneeName}");
            notifications.Enqueue(next.AssigneeId, "TODO_CREATED", "新增用章审批待办", $"{record.ApplicantName} 提交的 {record.Number} 等待审批", "SealRequest", record.Id);
            record.Version++;
            record.UpdatedAt = DateTimeOffset.UtcNow;
            return SaveAndReload(record, "用章审批已被其他操作更新，请刷新后重试。");
        }

        record.Status = (int)SealStatus.Approved;
        instance.Status = (int)FlowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        var finalCopied = ActivateCopies(record, SealStatus.Approved.ToString());
        Audit(actor, "SEAL_APPROVED", record, $"通过第 {task.Sequence} 节点，用章申请已最终通过");
        notifications.Enqueue(record.ApplicantId, "SEAL_APPROVED", "用章申请已批准", $"{record.Number} 审批已通过", "SealRequest", record.Id);
        if (!string.IsNullOrWhiteSpace(record.CustodianUserId))
        {
            notifications.Enqueue(record.CustodianUserId, "SEAL_ASSIGNED", "用章申请待用印执行", $"{record.ApplicantName} 的用章申请 {record.Number} 已审批通过，请办理用印/借出", "SealRequest", record.Id);
        }
        foreach (var recipient in finalCopied) notifications.Enqueue(recipient.Id, "FLOW_COPY", "用章抄送事项已办结", $"{record.ApplicantName} 的 {record.Number} 审批已通过", "SealRequest", record.Id);
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        return SaveAndReload(record, "用章审批已被其他操作更新，请刷新后重试。");
    }

    private ServiceResult<ValidatedSealInput> Validate(Employee actor, SaveSealRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 100)
            return ServiceResult<ValidatedSealInput>.Failure("用印主题应为 1–100 个字符。", "SEAL_001");
        if (string.IsNullOrWhiteSpace(request.DocumentCategory))
            return ServiceResult<ValidatedSealInput>.Failure("文件类别不能为空。", "SEAL_001");
        if (string.IsNullOrWhiteSpace(request.DocumentName) || request.DocumentName.Trim().Length > 100)
            return ServiceResult<ValidatedSealInput>.Failure("文件名称应为 1–100 个字符。", "SEAL_001");
        if (string.IsNullOrWhiteSpace(request.SealType))
            return ServiceResult<ValidatedSealInput>.Failure("印章类型不能为空。", "SEAL_001");
        if (request.Copies is < 1 or > 100)
            return ServiceResult<ValidatedSealInput>.Failure("用印份数应为 1–100 之间的整数。", "SEAL_001");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return ServiceResult<ValidatedSealInput>.Failure("申请事由应为 1–500 个字符。", "SEAL_001");

        var policyResult = ResolveSealPolicy();
        if (!policyResult.IsSuccess) return ServiceResult<ValidatedSealInput>.Failure(policyResult.Error!, policyResult.Code!);
        var (policy, _) = policyResult.Value;
        var normSealType = NormalizeSealType(request.SealType);
        var sealItem = policy.Seals.FirstOrDefault(s => (!string.IsNullOrWhiteSpace(s.Code) && s.Code.Equals(normSealType, StringComparison.OrdinalIgnoreCase)) || s.Name.Equals(normSealType, StringComparison.OrdinalIgnoreCase) || s.SealType.Equals(normSealType, StringComparison.OrdinalIgnoreCase));
        if (sealItem is null)
            return ServiceResult<ValidatedSealInput>.Failure($"印章类型【{request.SealType}】不存在。", "SEAL_001");
        if (!sealItem.IsEnabled)
            return ServiceResult<ValidatedSealInput>.Failure($"印章【{sealItem.Name}】已被系统停用，无法申请。", "SEAL_005");

        var normDocCategory = NormalizeDocumentCategory(request.DocumentCategory);
        var docCategory = policy.DocumentCategories.FirstOrDefault(r => (!string.IsNullOrWhiteSpace(r.Code) && r.Code.Equals(normDocCategory, StringComparison.OrdinalIgnoreCase)) || r.Name.Equals(normDocCategory, StringComparison.OrdinalIgnoreCase));
        if (docCategory is null)
            return ServiceResult<ValidatedSealInput>.Failure($"文件类别【{request.DocumentCategory}】不存在。", "SEAL_001");
        if (!docCategory.IsEnabled)
            return ServiceResult<ValidatedSealInput>.Failure($"文件类别【{docCategory.Name}】已被系统停用，无法申请。", "SEAL_007");

        if (request.IsOut)
        {
            if (!sealItem.AllowOut)
                return ServiceResult<ValidatedSealInput>.Failure($"印章【{sealItem.Name}】禁止外带使用。", "SEAL_006");
            if (request.OutStartDate is null || request.OutEndDate is null)
                return ServiceResult<ValidatedSealInput>.Failure("外带借出必须指定预计借出日期与归还日期。", "SEAL_002");
            if (request.OutEndDate < request.OutStartDate)
                return ServiceResult<ValidatedSealInput>.Failure("预计归还日期不能早于借出日期。", "SEAL_002");
            var diffDays = request.OutEndDate.Value.DayNumber - request.OutStartDate.Value.DayNumber;
            if (diffDays > sealItem.MaxOutDays)
                return ServiceResult<ValidatedSealInput>.Failure($"印章【{sealItem.Name}】最长外带天数为 {sealItem.MaxOutDays} 天，当前申请天数（{diffDays} 天）超出限制。", "SEAL_002");
            if (string.IsNullOrWhiteSpace(request.OutCustodian) || request.OutCustodian.Trim().Length > 50)
                return ServiceResult<ValidatedSealInput>.Failure("外带保管人应为 1–50 个字符。", "SEAL_002");
        }

        var validAttachments = NormalizeAttachments(actor, request.Attachments);
        if (validAttachments is null) return ServiceResult<ValidatedSealInput>.Failure("包含无效或越权附件。", "FILE_005");

        var copyRecipientsList = (request.CopyRecipientIds ?? []).Distinct().Where(id => id != actor.Id).ToList();
        if (copyRecipientsList.Any(id => data.FindEmployee(id) is not { Status: "ACTIVE" }))
            return ServiceResult<ValidatedSealInput>.Failure("抄送人必须为在职员工。", "VALIDATION_001");

        return ServiceResult<ValidatedSealInput>.Success(new ValidatedSealInput(sealItem, docCategory, validAttachments, copyRecipientsList));
    }

    private IReadOnlyList<string>? NormalizeAttachments(Employee actor, IReadOnlyList<string>? attachments)
    {
        var list = (attachments ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct().ToList();
        if (list.Count > 20) return null;
        if (files is not null && !files.AreOwnedBy(actor, list)) return null;
        return list;
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateOnly ChinaDate(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(BusinessTime.ChinaOffset).DateTime);

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static System.Linq.Expressions.Expression<Func<SealTaskRecord, SealTask>> ToTaskExpression() => task => new SealTask(
        task.Id, task.SealRequestId, task.FlowInstanceId, task.AssigneeId, task.AssigneeName,
        task.OriginalAssigneeId, task.OriginalAssigneeName, task.DelegationId, task.Sequence,
        (FlowTaskStatus)task.Status, task.Comment, task.ProcessedAt, task.Version);

    private SealRequest Load(SealRequestRecord record)
    {
        var tasks = db.SealTasks.AsNoTracking().Where(item => item.TenantId == TenantId && item.SealRequestId == record.Id && item.FlowInstanceId == record.CurrentFlowInstanceId)
            .OrderBy(item => item.Sequence).ThenBy(item => item.Id).Select(ToTaskExpression()).ToList();
        var flowInstancesList = flowInstances.Load(BusinessType, record.Id);
        var execution = db.SealExecutions.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.SealRequestId == record.Id);
        var returnRecord = db.SealReturns.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.SealRequestId == record.Id);

        return new SealRequest
        {
            Id = record.Id,
            Number = record.Number,
            ApplicantId = record.ApplicantId,
            ApplicantName = record.ApplicantName,
            DepartmentName = record.DepartmentName,
            Title = record.Title,
            DocumentCategory = record.DocumentCategory,
            DocumentName = record.DocumentName,
            SealType = record.SealType,
            CustodianUserId = record.CustodianUserId,
            MaxOutDays = record.MaxOutDays,
            RiskMetric = record.RiskMetric,
            Copies = record.Copies,
            IsOut = record.IsOut,
            OutStartDate = record.OutStartDate,
            OutEndDate = record.OutEndDate,
            OutCustodian = record.OutCustodian,
            Reason = record.Reason,
            Attachments = DeserializeList(record.AttachmentsJson),
            CopyRecipientIds = copyRecipients.LoadRecipientIds(BusinessType, record.Id),
            Status = (SealStatus)record.Status,
            Version = record.Version,
            IsDemo = record.IsDemo,
            ProcessDefinitionId = record.ProcessDefinitionId,
            ProcessDefinitionCode = record.ProcessDefinitionCode,
            ProcessDefinitionVersion = record.ProcessDefinitionVersion,
            CurrentFlowInstanceId = record.CurrentFlowInstanceId,
            RiskLevel = record.RiskLevel,
            ConfigVersionId = record.ConfigVersionId,
            ConfigVersionNumber = record.ConfigVersionNumber,
            ConfigSnapshotJson = record.ConfigSnapshotJson,
            ConfigResolvedAt = record.ConfigResolvedAt,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            Tasks = tasks,
            FlowInstances = flowInstancesList,
            Execution = execution is null ? null : new SealExecution(execution.ExecutedDate, execution.OperatorName, execution.Notes, DeserializeList(execution.AttachmentsJson), execution.CreatedBy, execution.CreatedByName, execution.CreatedAt),
            Return = returnRecord is null ? null : new SealReturn(returnRecord.ReturnDate, returnRecord.SealCondition, returnRecord.ReceiverName, returnRecord.Notes, DeserializeList(returnRecord.AttachmentsJson), returnRecord.CreatedBy, returnRecord.CreatedByName, returnRecord.CreatedAt)
        };
    }

    private ServiceResult<SealRequest> SaveAndReload(SealRequestRecord record, string concurrencyMessage, string duplicateMessage = "用章数据重复。")
    {
        try
        {
            db.SaveChanges();
            db.ChangeTracker.Clear();
            var reloaded = db.SealRequests.AsNoTracking().Single(item => item.Id == record.Id);
            return ServiceResult<SealRequest>.Success(Load(reloaded));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult<SealRequest>.Failure(concurrencyMessage, "CONCURRENCY_001");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ServiceResult<SealRequest>.Failure(duplicateMessage, "DUPLICATE_001");
        }
    }

    private void AddFlowAction(Guid flowInstanceId, FlowActionType type, Employee actor, Guid? taskId = null, int? sequence = null, string? comment = null, string? fromAssigneeId = null, string? fromAssigneeName = null, string? toAssigneeId = null, string? toAssigneeName = null)
    {
        db.FlowActions.Add(new FlowActionRecord
        {
            TenantId = TenantId, FlowInstanceId = flowInstanceId, TaskId = taskId, Sequence = sequence, Action = (int)type,
            ActorId = actor.Id, ActorName = actor.Name, FromAssigneeId = fromAssigneeId, FromAssigneeName = fromAssigneeName,
            ToAssigneeId = toAssigneeId, ToAssigneeName = toAssigneeName, Comment = comment, OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private IReadOnlyList<Employee> ActivateCopies(SealRequestRecord record, string status)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = db.FlowCopyRecipients.Where(item => item.TenantId == TenantId && item.BusinessType == BusinessType && item.BusinessId == record.Id && item.AvailableAt == null).ToList();
        foreach (var row in rows) { row.Status = status; row.AvailableAt = now; }
        return rows.Select(item => data.FindEmployee(item.RecipientId)).Where(item => item is not null).Cast<Employee>().ToList();
    }

    private void Audit(Employee actor, string action, SealRequestRecord record, string summary)
    {
        db.AuditLogs.Add(new AuditRecord
        {
            TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "SealRequest", ResourceId = record.Id.ToString(),
            Summary = summary, OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private string GenerateNumber(DateOnly today)
    {
        var prefix = $"SEAL-{today:yyyyMMdd}-";
        var max = db.SealRequests.Where(item => item.TenantId == TenantId && item.Number.StartsWith(prefix))
            .Select(item => item.Number).ToList().Select(number => int.TryParse(number[prefix.Length..], out var sequence) ? sequence : 0)
            .DefaultIfEmpty(0).Max();
        return $"{prefix}{max + 1:D3}";
    }

    private static string NormalizeSealType(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed switch
        {
            "合同章" => "合同专用章",
            "财务章" => "财务专用章",
            "人事章" => "人事专用章",
            _ => trimmed
        };
    }

    private static string NormalizeDocumentCategory(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed switch
        {
            "证照资质" => "资质证明",
            "财务票据" => "财务报表",
            "人事证明" => "人事材料",
            _ => trimmed
        };
    }

    private sealed record ValidatedSealInput(SealRegistryItemConfig SealItem, SealDocumentCategoryConfig DocCategory, IReadOnlyList<string> Attachments, IReadOnlyList<string> CopyRecipientIds);
}
