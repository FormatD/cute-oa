using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class LeaveService
{
    private const string TenantId = "demo";
    private readonly DemoData data;
    private readonly OaDbContext? db;
    private readonly IWorkCalendar calendar;
    private readonly NotificationService? notifications;
    private readonly FileService? files;
    private readonly IProcessRouter processRouter;
    private readonly FlowInstanceService flowInstances;
    private readonly FlowCopyService copyRecipients;
    private readonly List<LeaveRequest> _requests;
    private readonly Dictionary<(string UserId, LeaveType Type, int Year), LeaveBalance> _balances = [];

    public LeaveService(DemoData data, OaDbContext? db = null, IWorkCalendar? calendar = null, NotificationService? notifications = null, FileService? files = null, IProcessRouter? processRouter = null, FlowInstanceService? flowInstances = null, FlowCopyService? copyRecipients = null)
    {
        this.data = data;
        this.db = db;
        this.calendar = calendar ?? new ChinaWorkCalendar();
        this.notifications = notifications;
        this.files = files;
        this.processRouter = processRouter ?? new DefaultProcessRouter(data);
        this.flowInstances = flowInstances ?? new FlowInstanceService(db);
        this.copyRecipients = copyRecipients ?? new FlowCopyService(data, db);
        _requests = db is null ? [] : LoadRequests(db, this.flowInstances, this.copyRecipients);
    }

    public IReadOnlyList<LeaveRequest> List(Employee actor, DocumentListQuery? query = null) => _requests
        .Where(item => data.CanView(actor, data.GetEmployee(item.ApplicantId), "Leave"))
        .Where(item => string.IsNullOrWhiteSpace(query?.Keyword) || item.Number.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) || item.Reason.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) || item.ApplicantName.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase))
        .Where(item => query?.Status is null || (int)item.Status == query.Status)
        .Where(item => string.IsNullOrWhiteSpace(query?.ApplicantId) || item.ApplicantId == query.ApplicantId)
        .Where(item => query?.StartDate is null || item.StartDate >= query.StartDate)
        .Where(item => query?.EndDate is null || item.EndDate <= query.EndDate)
        .OrderByDescending(item => item.CreatedAt)
        .ToList();

    public ServiceResult<LeaveRequest> Get(Employee actor, Guid id)
    {
        var item = _requests.SingleOrDefault(request => request.Id == id);
        if (item is null) return ServiceResult<LeaveRequest>.Failure("请假单不存在。", "DATA_001");
        if (!data.CanView(actor, data.GetEmployee(item.ApplicantId), "Leave") && item.Tasks.All(task => task.AssigneeId != actor.Id) && !copyRecipients.CanView(actor, "Leave", item.Id))
            return ServiceResult<LeaveRequest>.Failure("无权查看该请假单。", "AUTH_002");
        return ServiceResult<LeaveRequest>.Success(item);
    }

    public LeaveBalance GetBalance(Employee actor, LeaveType type = LeaveType.Annual, int? year = null) => Balance(actor, type, year ?? BusinessTime.ChinaToday().Year);

    public ServiceResult<LeaveBalance> GetBalance(Employee actor, string userId, LeaveType type, int year)
    {
        var subject = data.FindEmployee(userId);
        if (subject is null) return ServiceResult<LeaveBalance>.Failure("员工不存在。", "DATA_001");
        if (actor.Id != subject.Id && (!data.HasPermission(actor, OaPermissions.PersonnelManage) || !data.CanView(actor, subject, "Personnel")))
            return ServiceResult<LeaveBalance>.Failure("无权查看该员工假期余额。", "AUTH_002");
        if (type is not (LeaveType.Annual or LeaveType.CompTime) || year is < 2000 or > 2100)
            return ServiceResult<LeaveBalance>.Failure("仅支持查询 2000–2100 年的年假或调休余额。", "LEAVE_004");
        return ServiceResult<LeaveBalance>.Success(Balance(subject, type, year));
    }

    public ServiceResult<LeaveBalance> AdjustBalance(Employee actor, string userId, AdjustLeaveBalanceRequest request)
    {
        var subject = data.FindEmployee(userId);
        if (subject is null) return ServiceResult<LeaveBalance>.Failure("员工不存在。", "DATA_001");
        if (!data.HasPermission(actor, OaPermissions.PersonnelManage) || !data.CanView(actor, subject, "Personnel"))
            return ServiceResult<LeaveBalance>.Failure("无权调整该员工假期余额。", "AUTH_002");
        var reason = request.Reason.Trim();
        if (request.Type is not (LeaveType.Annual or LeaveType.CompTime) || request.Year is < 2000 or > 2100 || request.Adjustment is < -100m or > 100m || request.Adjustment * 2 != decimal.Truncate(request.Adjustment * 2) || reason.Length is < 5 or > 500)
            return ServiceResult<LeaveBalance>.Failure("假别、年度或调整值不合法；调整应为 -100 至 100 天的 0.5 天倍数，原因应为 5–500 个字符。", "LEAVE_004");
        var key = (subject.Id, request.Type, request.Year);
        var current = Balance(subject, request.Type, request.Year);
        if (current.Version != request.Version) return ServiceResult<LeaveBalance>.Failure("假期余额已被其他人更新，请刷新后重试。", "CONCURRENCY_001");
        var entitled = current.StatutoryEntitled + request.Adjustment;
        if (entitled < current.Frozen + current.Used || entitled < 0)
            return ServiceResult<LeaveBalance>.Failure("调整后的总额度不能低于已冻结和已使用额度。", "LEAVE_001");
        return ExecuteMutation(() =>
        {
            var updated = current with { Entitled = entitled, Adjustment = request.Adjustment };
            _balances[key] = updated;
            PersistBalance(subject.Id, request.Type, request.Year);
            var saved = _balances[key];
            if (db is not null)
            {
                db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = "LEAVE_BALANCE_ADJUSTED", ResourceType = "LeaveBalance", ResourceId = $"{subject.Id}:{request.Type}:{request.Year}", Summary = $"{subject.Name} {request.Year} 年{request.Type}余额调整为 {request.Adjustment:+0.0;-0.0;0.0} 天：{reason}" });
                db.SaveChanges();
            }
            return ServiceResult<LeaveBalance>.Success(saved);
        });
    }

    public IReadOnlyList<FlowTask> GetPendingTasks(Employee actor) => _requests
        .Where(item => item.Status == LeaveStatus.Approving)
        .SelectMany(item => item.Tasks.Where(task => task.Status == FlowTaskStatus.Pending && task.Sequence == item.Tasks.Where(candidate => candidate.Status == FlowTaskStatus.Pending).Min(candidate => candidate.Sequence)))
        .Where(task => task.AssigneeId == actor.Id)
        .OrderBy(task => task.Sequence)
        .ToList();

    public IReadOnlyList<FlowTask> GetProcessedTasks(Employee actor) => _requests
        .SelectMany(item => item.Tasks)
        .Where(task => task.AssigneeId == actor.Id && task.Status is FlowTaskStatus.Approved or FlowTaskStatus.Rejected)
        .OrderByDescending(task => task.ProcessedAt)
        .ToList();

    public ServiceResult<LeaveRequest> CreateDraft(Employee actor, CreateLeaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return ServiceResult<LeaveRequest>.Failure("请假事由应为 1–500 个字符。");
        if (request.EndDate < request.StartDate)
            return ServiceResult<LeaveRequest>.Failure("结束日期不能早于开始日期。");
        if (files is not null && request.Attachments is { Count: > 0 } && !files.AreOwnedBy(actor, request.Attachments))
            return ServiceResult<LeaveRequest>.Failure("附件不存在或不属于当前用户。", "FILE_005");
        var copies = copyRecipients.Validate(actor, request.CopyRecipientIds);
        if (!copies.IsSuccess) return ServiceResult<LeaveRequest>.Failure(copies.Error!, copies.Code!);

        var days = CalculateWorkingDays(request.StartDate, request.StartPeriod, request.EndDate, request.EndPeriod, calendar);
        if (days <= 0)
            return ServiceResult<LeaveRequest>.Failure("请假时长必须大于 0。", "LEAVE_003");

        var configRecord = BusinessConfigurationDefaults.ResolveEffectiveConfig(db, ConfigurationDomains.Leave, "LeavePolicy");
        var item = new LeaveRequest
        {
            Number = $"QJ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            ApplicantId = actor.Id,
            ApplicantName = actor.Name,
            Type = request.Type,
            StartDate = request.StartDate,
            StartPeriod = request.StartPeriod,
            EndDate = request.EndDate,
            EndPeriod = request.EndPeriod,
            Days = days,
            Reason = request.Reason.Trim(),
            Attachments = request.Attachments?.ToList() ?? [],
            CopyRecipientIds = copies.Value!,
            ConfigVersionId = configRecord?.Id,
            ConfigVersionNumber = configRecord?.Version,
            ConfigSnapshotJson = configRecord?.ContentJson,
            ConfigResolvedAt = configRecord is not null ? DateTimeOffset.UtcNow : null
        };
        return ExecuteMutation(() =>
        {
            _requests.Add(item);
            Persist(item);
            Audit(actor, "LEAVE_DRAFT_CREATED", item, "创建请假草稿");
            return ServiceResult<LeaveRequest>.Success(item);
        });
    }

    public ServiceResult<LeaveRequest> Submit(Employee actor, Guid id)
    {
        var item = _requests.SingleOrDefault(request => request.Id == id);
        if (item is null || item.ApplicantId != actor.Id) return ServiceResult<LeaveRequest>.Failure("请假单不存在或无权限。", "DATA_001");
        if (item.Status is not LeaveStatus.Draft and not LeaveStatus.Rejected)
            return ServiceResult<LeaveRequest>.Failure("当前状态不允许提交。", "STATE_001");
        if (_requests.Any(request => request.Id != id && request.ApplicantId == actor.Id && request.Type == item.Type &&
            (request.Status is LeaveStatus.Approving or LeaveStatus.Completed) && IsOverlapping(request, item)))
            return ServiceResult<LeaveRequest>.Failure("存在时间重叠的有效请假申请。", "LEAVE_002");

        var configRecord = BusinessConfigurationDefaults.ResolveEffectiveConfig(db, ConfigurationDomains.Leave, "LeavePolicy");
        var policy = configRecord is not null
            ? JsonSerializer.Deserialize<LeavePolicyConfig>(configRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions)
            : BusinessConfigurationDefaults.CreateDefaultLeavePolicy();

        var typeRule = policy?.LeaveTypes.FirstOrDefault(t => t.Type.Equals(item.Type.ToString(), StringComparison.OrdinalIgnoreCase));
        if (typeRule is not null && !typeRule.IsEnabled)
            return ServiceResult<LeaveRequest>.Failure($"假期类型【{typeRule.Name}】已被系统停用，无法提交申请。", "LEAVE_005");

        if (typeRule is not null && typeRule.MinUnit > 0)
        {
            var remainder = item.Days % typeRule.MinUnit;
            if (remainder != 0)
                return ServiceResult<LeaveRequest>.Failure($"【{typeRule.Name}】最小申请单位为 {typeRule.MinUnit} 天，当前申请时长 {item.Days} 天不符合要求。", "LEAVE_006");
        }

        if (typeRule is not null)
        {
            var needsAttachment = typeRule.RequiresAttachment && (!typeRule.AttachmentThresholdDays.HasValue || item.Days > typeRule.AttachmentThresholdDays.Value);
            if (needsAttachment && (item.Attachments is null || item.Attachments.Count == 0))
                return ServiceResult<LeaveRequest>.Failure(typeRule.AttachmentThresholdDays.HasValue ? $"【{typeRule.Name}】申请时长超过 {typeRule.AttachmentThresholdDays} 天必须上传证明材料附件。" : $"【{typeRule.Name}】申请必须上传证明材料附件。", "LEAVE_007");
        }

        var route = processRouter.Resolve("Leave", actor, item.Days, item.Type.ToString());
        if (!route.IsSuccess) return ServiceResult<LeaveRequest>.Failure(route.Error!, route.Code!);

        int? balanceYear = null;
        LeaveBalance? balanceToFreeze = null;
        if (item.Type is LeaveType.Annual or LeaveType.CompTime)
        {
            if (item.StartDate.Year != item.EndDate.Year)
                return ServiceResult<LeaveRequest>.Failure("年假和调休不能跨自然年度申请，请按年度拆分。", "LEAVE_004");
            balanceYear = item.StartDate.Year;
            balanceToFreeze = Balance(actor, item.Type, balanceYear.Value);
            if (balanceToFreeze.Available < item.Days)
                return ServiceResult<LeaveRequest>.Failure("假期余额不足。", "LEAVE_001");
        }

        var result = ExecuteMutation(() =>
        {
            var expectedVersion = item.Version;
            item.Version++;
            item.BalanceYear = balanceYear;
            if (balanceYear.HasValue)
                _balances[(actor.Id, item.Type, balanceYear.Value)] = balanceToFreeze! with { Frozen = balanceToFreeze.Frozen + item.Days };
            item.Status = LeaveStatus.Approving;
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
            var instance = flowInstances.Start(item.FlowInstances, "Leave", item.Id, item.Number, actor, route.Value);
            item.CurrentFlowInstanceId = instance.Id;
            foreach (var (resolvedApprover, sequence) in route.Value.Approvers.Select((value, index) => (value, index + 1)))
                item.Tasks.Add(new FlowTask { LeaveRequestId = item.Id, FlowInstanceId = instance.Id, AssigneeId = resolvedApprover.Assignee.Id, AssigneeName = resolvedApprover.Assignee.Name, OriginalAssigneeId = resolvedApprover.DelegationId is null ? null : resolvedApprover.OriginalApprover.Id, OriginalAssigneeName = resolvedApprover.DelegationId is null ? null : resolvedApprover.OriginalApprover.Name, DelegationId = resolvedApprover.DelegationId, Sequence = sequence });
            Persist(item, expectedVersion);
            if (balanceYear.HasValue) PersistBalance(actor.Id, item.Type, balanceYear.Value);
            if (item.Tasks.OrderBy(task => task.Sequence).FirstOrDefault() is { } firstTask)
                notifications?.Enqueue(firstTask.AssigneeId, "TODO_CREATED", "新增请假审批待办", $"{item.ApplicantName} 提交了 {item.Number}", "LeaveRequest", item.Id);
            Audit(actor, "LEAVE_SUBMITTED", item, "提交请假审批");
            return ServiceResult<LeaveRequest>.Success(item);
        });
        return result;
    }

    public ServiceResult<LeaveRequest> Update(Employee actor, Guid id, int expectedVersion, CreateLeaveRequest request)
    {
        var original = _requests.SingleOrDefault(item => item.Id == id);
        if (original is null || original.ApplicantId != actor.Id) return ServiceResult<LeaveRequest>.Failure("请假单不存在或无权限。", "DATA_001");
        if (original.Status is not (LeaveStatus.Draft or LeaveStatus.Rejected)) return ServiceResult<LeaveRequest>.Failure("当前状态不允许编辑。", "STATE_001");
        if (original.Version != expectedVersion) return ServiceResult<LeaveRequest>.Failure("单据已被更新，请刷新后重试。", "CONCURRENCY_001");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) return ServiceResult<LeaveRequest>.Failure("请假事由应为 1–500 个字符。");
        if (request.EndDate < request.StartDate) return ServiceResult<LeaveRequest>.Failure("结束日期不能早于开始日期。");
        if (files is not null && request.Attachments is { Count: > 0 } && !files.AreOwnedBy(actor, request.Attachments)) return ServiceResult<LeaveRequest>.Failure("附件不存在或不属于当前用户。", "FILE_005");
        var copies = copyRecipients.Validate(actor, request.CopyRecipientIds);
        if (!copies.IsSuccess) return ServiceResult<LeaveRequest>.Failure(copies.Error!, copies.Code!);
        var days = CalculateWorkingDays(request.StartDate, request.StartPeriod, request.EndDate, request.EndPeriod, calendar);
        if (days <= 0) return ServiceResult<LeaveRequest>.Failure("请假时长必须大于 0。", "LEAVE_003");
        var updated = new LeaveRequest { Id = original.Id, Number = original.Number, ApplicantId = original.ApplicantId, ApplicantName = original.ApplicantName, Type = request.Type, StartDate = request.StartDate, StartPeriod = request.StartPeriod, EndDate = request.EndDate, EndPeriod = request.EndPeriod, Days = days, Reason = request.Reason.Trim(), Attachments = request.Attachments?.ToList() ?? [], CopyRecipientIds = copies.Value!, Version = original.Version + 1, Status = original.Status, ProcessDefinitionId = original.ProcessDefinitionId, ProcessDefinitionCode = original.ProcessDefinitionCode, ProcessDefinitionVersion = original.ProcessDefinitionVersion, CurrentFlowInstanceId = original.CurrentFlowInstanceId, BalanceYear = original.BalanceYear };
        updated.Tasks.AddRange(original.Tasks);
        updated.FlowInstances.AddRange(original.FlowInstances);
        return ExecuteMutation(() =>
        {
            _requests[_requests.IndexOf(original)] = updated;
            Persist(updated, expectedVersion);
            Audit(actor, "LEAVE_UPDATED", updated, "编辑请假申请");
            return ServiceResult<LeaveRequest>.Success(updated);
        });
    }

    public ServiceResult<bool> Delete(Employee actor, Guid id)
    {
        var item = _requests.SingleOrDefault(request => request.Id == id);
        if (item is null || item.ApplicantId != actor.Id) return ServiceResult<bool>.Failure("请假单不存在或无权限。", "DATA_001");
        if (item.Status is not (LeaveStatus.Draft or LeaveStatus.Rejected)) return ServiceResult<bool>.Failure("当前状态不允许删除。", "STATE_001");
        return ExecuteMutation(() =>
        {
            _requests.Remove(item);
            if (db is not null)
            {
                copyRecipients.DeleteUnavailable("Leave", item.Id);
                db.FlowTasks.Where(task => task.LeaveRequestId == item.Id).ExecuteDelete();
                if (db.LeaveRequests.Where(request => request.Id == item.Id && request.Version == item.Version).ExecuteDelete() != 1)
                    throw new DbUpdateConcurrencyException();
                db.SaveChanges();
            }
            Audit(actor, "LEAVE_DELETED", item, "删除请假申请");
            return ServiceResult<bool>.Success(true);
        });
    }

    public ServiceResult<LeaveRequest> Approve(Employee actor, Guid taskId, string? comment)
    {
        var found = FindTask(taskId);
        if (found is null) return ServiceResult<LeaveRequest>.Failure("审批任务不存在。", "DATA_001");
        var (item, task) = found.Value;
        if (task.AssigneeId != actor.Id || task.Status != FlowTaskStatus.Pending)
            return ServiceResult<LeaveRequest>.Failure("当前用户不能处理该审批任务。", "AUTH_002");
        if (item.Tasks.Any(candidate => candidate.Sequence < task.Sequence && candidate.Status != FlowTaskStatus.Approved))
            return ServiceResult<LeaveRequest>.Failure("前序审批尚未完成。", "STATE_001");

        IReadOnlyList<Employee> activatedCopies = [];
        var result = ExecuteMutation(() =>
        {
            var expectedVersion = item.Version;
            item.Version++;
            task.Status = FlowTaskStatus.Approved;
            task.Comment = comment?.Trim();
            task.ProcessedAt = DateTimeOffset.UtcNow;
            var instance = flowInstances.Current(item.FlowInstances, task.FlowInstanceId);
            if (instance is not null) flowInstances.Record(instance, FlowActionType.Approved, actor, task.Id, task.Sequence, task.Comment);
            if (item.Tasks.All(candidate => candidate.Status == FlowTaskStatus.Approved))
            {
                item.Status = LeaveStatus.Completed;
                if (instance is not null) flowInstances.Complete(instance);
                ReleaseFrozenToUsed(data.GetEmployee(item.ApplicantId), item);
            }
            Persist(item, expectedVersion);
            if (item.Status == LeaveStatus.Completed && item.Type is LeaveType.Annual or LeaveType.CompTime)
                PersistBalance(item.ApplicantId, item.Type, item.BalanceYear ?? item.StartDate.Year);
            activatedCopies = item.Status == LeaveStatus.Completed ? copyRecipients.Activate("Leave", item.Id, item.Status.ToString()) : [];
            if (item.Status == LeaveStatus.Completed)
            {
                notifications?.Enqueue(item.ApplicantId, "LEAVE_COMPLETED", "请假申请已通过", $"{item.Number} 已全部审批通过", "LeaveRequest", item.Id);
                foreach (var recipient in activatedCopies) notifications?.Enqueue(recipient.Id, "FLOW_COPY", "请假抄送事项已完成", $"{item.ApplicantName} 的 {item.Number} 已审批完成", "LeaveRequest", item.Id);
            }
            else if (item.Tasks.FirstOrDefault(candidate => candidate.Sequence == task.Sequence + 1 && candidate.Status == FlowTaskStatus.Pending) is { } next)
                notifications?.Enqueue(next.AssigneeId, "TODO_CREATED", "新增请假审批待办", $"{item.Number} 等待你审批", "LeaveRequest", item.Id);
            Audit(actor, "LEAVE_APPROVED", item, comment?.Trim() ?? "同意请假");
            return ServiceResult<LeaveRequest>.Success(item);
        });
        return result;
    }

    public ServiceResult<LeaveRequest> Reject(Employee actor, Guid taskId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return ServiceResult<LeaveRequest>.Failure("驳回必须填写意见。");
        var found = FindTask(taskId);
        if (found is null) return ServiceResult<LeaveRequest>.Failure("审批任务不存在。", "DATA_001");
        var (item, task) = found.Value;
        if (task.AssigneeId != actor.Id || task.Status != FlowTaskStatus.Pending)
            return ServiceResult<LeaveRequest>.Failure("当前用户不能处理该审批任务。", "AUTH_002");

        var result = ExecuteMutation(() =>
        {
            var expectedVersion = item.Version;
            item.Version++;
            task.Status = FlowTaskStatus.Rejected;
            task.Comment = comment.Trim();
            task.ProcessedAt = DateTimeOffset.UtcNow;
            foreach (var candidate in item.Tasks.Where(candidate => candidate.Id != task.Id && candidate.Status == FlowTaskStatus.Pending)) candidate.Status = FlowTaskStatus.Cancelled;
            item.Status = LeaveStatus.Rejected;
            if (flowInstances.Current(item.FlowInstances, task.FlowInstanceId) is { } instance)
                flowInstances.Record(instance, FlowActionType.Rejected, actor, task.Id, task.Sequence, task.Comment);
            ReleaseFrozen(data.GetEmployee(item.ApplicantId), item);
            Persist(item, expectedVersion);
            if (item.Type is LeaveType.Annual or LeaveType.CompTime)
                PersistBalance(item.ApplicantId, item.Type, item.BalanceYear ?? item.StartDate.Year);
            notifications?.Enqueue(item.ApplicantId, "LEAVE_REJECTED", "请假申请已驳回", $"{item.Number} 已被驳回：{comment.Trim()}", "LeaveRequest", item.Id);
            Audit(actor, "LEAVE_REJECTED", item, comment.Trim());
            return ServiceResult<LeaveRequest>.Success(item);
        });
        return result;
    }

    public ServiceResult<LeaveRequest> Transfer(Employee actor, Guid taskId, TransferTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Comment)) return ServiceResult<LeaveRequest>.Failure("转办必须填写意见。");
        var found = FindTask(taskId);
        if (found is null) return ServiceResult<LeaveRequest>.Failure("审批任务不存在。", "DATA_001");
        var (item, task) = found.Value;
        if (task.AssigneeId != actor.Id || task.Status != FlowTaskStatus.Pending)
            return ServiceResult<LeaveRequest>.Failure("当前用户不能转办该审批任务。", "AUTH_002");
        var assignee = data.FindEmployee(request.AssigneeId);
        if (assignee is null || assignee.Id == actor.Id)
            return ServiceResult<LeaveRequest>.Failure("转办人不存在或不能为当前审批人。", "FLOW_002");
        var result = ExecuteMutation(() =>
        {
            var expectedVersion = item.Version;
            item.Version++;
            var previousName = task.AssigneeName;
            var previous = data.GetEmployee(task.AssigneeId);
            task.AssigneeId = assignee.Id;
            task.AssigneeName = assignee.Name;
            if (flowInstances.Current(item.FlowInstances, task.FlowInstanceId) is { } instance)
                flowInstances.Record(instance, FlowActionType.Transferred, actor, task.Id, task.Sequence, request.Comment, previous, assignee);
            Persist(item, expectedVersion);
            notifications?.Enqueue(assignee.Id, "TODO_TRANSFERRED", "请假审批待办已转办给你", $"{item.Number}：{request.Comment.Trim()}", "LeaveRequest", item.Id);
            Audit(actor, "LEAVE_TASK_TRANSFERRED", item, $"将第 {task.Sequence} 节点从 {previousName} 转办给 {assignee.Name}：{request.Comment.Trim()}");
            return ServiceResult<LeaveRequest>.Success(item);
        });
        return result;
    }

    public ServiceResult<LeaveRequest> Withdraw(Employee actor, Guid id)
    {
        var item = _requests.SingleOrDefault(request => request.Id == id);
        if (item is null || item.ApplicantId != actor.Id) return ServiceResult<LeaveRequest>.Failure("请假单不存在或无权限。", "DATA_001");
        if (item.Status != LeaveStatus.Approving) return ServiceResult<LeaveRequest>.Failure("当前状态不允许撤回。", "STATE_001");
        if (item.Tasks.Any(task => task.Status != FlowTaskStatus.Pending)) return ServiceResult<LeaveRequest>.Failure("已有审批处理，不能撤回。", "STATE_001");
        IReadOnlyList<Employee> activatedCopies = [];
        var result = ExecuteMutation(() =>
        {
            var expectedVersion = item.Version;
            item.Version++;
            foreach (var task in item.Tasks) task.Status = FlowTaskStatus.Cancelled;
            item.Status = LeaveStatus.Withdrawn;
            if (flowInstances.Current(item.FlowInstances, item.CurrentFlowInstanceId) is { } instance)
                flowInstances.Record(instance, FlowActionType.Withdrawn, actor, comment: "申请人撤回");
            ReleaseFrozen(actor, item);
            Persist(item, expectedVersion);
            if (item.Type is LeaveType.Annual or LeaveType.CompTime)
                PersistBalance(item.ApplicantId, item.Type, item.BalanceYear ?? item.StartDate.Year);
            activatedCopies = copyRecipients.Activate("Leave", item.Id, item.Status.ToString());
            foreach (var pendingTask in item.Tasks.Where(candidate => candidate.AssigneeId != actor.Id)) notifications?.Enqueue(pendingTask.AssigneeId, "LEAVE_WITHDRAWN", "请假申请已撤回", $"{item.Number} 已被申请人撤回", "LeaveRequest", item.Id);
            foreach (var recipient in activatedCopies) notifications?.Enqueue(recipient.Id, "FLOW_COPY", "请假抄送事项已撤回", $"{item.ApplicantName} 的 {item.Number} 已撤回", "LeaveRequest", item.Id);
            Audit(actor, "LEAVE_WITHDRAWN", item, "撤回请假申请");
            return ServiceResult<LeaveRequest>.Success(item);
        });
        return result;
    }

    private (LeaveRequest Request, FlowTask Task)? FindTask(Guid taskId)
    {
        foreach (var item in _requests)
        {
            var task = item.Tasks.SingleOrDefault(candidate => candidate.Id == taskId);
            if (task is not null) return (item, task);
        }
        return null;
    }

    private void ReleaseFrozen(Employee actor, LeaveRequest item)
    {
        if (item.Type is not (LeaveType.Annual or LeaveType.CompTime)) return;
        var year = item.BalanceYear ?? item.StartDate.Year;
        var key = (actor.Id, item.Type, year);
        var balance = Balance(actor, item.Type, year);
        _balances[key] = balance with { Frozen = Math.Max(0, balance.Frozen - item.Days) };
    }

    private void ReleaseFrozenToUsed(Employee actor, LeaveRequest item)
    {
        if (item.Type is not (LeaveType.Annual or LeaveType.CompTime)) return;
        var year = item.BalanceYear ?? item.StartDate.Year;
        var key = (actor.Id, item.Type, year);
        var balance = Balance(actor, item.Type, year);
        _balances[key] = balance with { Frozen = Math.Max(0, balance.Frozen - item.Days), Used = balance.Used + item.Days };
    }

    private LeaveBalance Balance(Employee employee, LeaveType type, int year)
    {
        if (type is not (LeaveType.Annual or LeaveType.CompTime)) throw new ArgumentOutOfRangeException(nameof(type), "仅年假和调休具有余额。");
        if (year is < 2000 or > 2100) throw new ArgumentOutOfRangeException(nameof(year), "余额年度必须在 2000–2100 之间。");
        var key = (employee.Id, type, year);
        if (_balances.TryGetValue(key, out var cached)) return cached;
        var loaded = LoadBalance(employee, type, year);
        _balances[key] = loaded;
        return loaded;
    }

    private LeaveBalance LoadBalance(Employee employee, LeaveType type, int year)
    {
        var today = BusinessTime.ChinaToday();
        var asOf = year == today.Year ? today : new DateOnly(year, 12, 31);
        var profile = db?.PersonnelProfiles.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.UserId == employee.Id);
        var statutory = type == LeaveType.Annual
            ? AnnualLeavePolicy.Calculate(asOf, profile?.CumulativeWorkStartDate, employee.CumulativeWorkYears, profile?.HireDate)
            : 0m;
        if (type == LeaveType.Annual)
        {
            var configRecord = BusinessConfigurationDefaults.ResolveEffectiveConfig(db, ConfigurationDomains.Leave, "LeavePolicy");
            if (configRecord is not null)
            {
                var policy = JsonSerializer.Deserialize<LeavePolicyConfig>(configRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions);
                if (policy?.AnnualLeaveBonus is { } bonus)
                {
                    var bonusDays = employee.CumulativeWorkYears switch
                    {
                        < 10 => bonus.Tier1BonusDays,
                        < 20 => bonus.Tier2BonusDays,
                        _ => bonus.Tier3BonusDays
                    };
                    statutory += bonusDays;
                }
            }
        }
        if (db is null) return new LeaveBalance(statutory, 0m, 0m, year, statutory, 0m);
        var record = db.LeaveBalances.AsNoTracking().SingleOrDefault(x => x.TenantId == TenantId && x.UserId == employee.Id && x.LeaveType == (int)type && x.Year == year);
        return record is null
            ? new LeaveBalance(statutory, 0m, 0m, year, statutory, 0m)
            : new LeaveBalance(statutory + record.Adjustment, record.Frozen, record.Used, year, statutory, record.Adjustment, record.Version);
    }

    private static List<LeaveRequest> LoadRequests(OaDbContext db, FlowInstanceService flowInstances, FlowCopyService copyRecipients)
    {
        var tasks = db.FlowTasks.AsNoTracking().Where(x => x.TenantId == TenantId).ToLookup(x => x.LeaveRequestId);
        return db.LeaveRequests.AsNoTracking().Where(x => x.TenantId == TenantId).OrderBy(x => x.CreatedAt).ToList().Select(x =>
        {
            var request = new LeaveRequest { Id = x.Id, Number = x.Number, ApplicantId = x.ApplicantId, ApplicantName = x.ApplicantName, Type = (LeaveType)x.Type, StartDate = x.StartDate, StartPeriod = (LeavePeriod)x.StartPeriod, EndDate = x.EndDate, EndPeriod = (LeavePeriod)x.EndPeriod, Days = x.Days, Reason = x.Reason, Attachments = JsonSerializer.Deserialize<List<string>>(x.AttachmentsJson) ?? [], CopyRecipientIds = copyRecipients.LoadRecipientIds("Leave", x.Id), Version = x.Version, ProcessDefinitionId = x.ProcessDefinitionId, ProcessDefinitionCode = x.ProcessDefinitionCode, ProcessDefinitionVersion = x.ProcessDefinitionVersion, CurrentFlowInstanceId = x.CurrentFlowInstanceId, BalanceYear = x.BalanceYear, ConfigVersionId = x.ConfigVersionId, ConfigVersionNumber = x.ConfigVersionNumber, ConfigSnapshotJson = x.ConfigSnapshotJson, ConfigResolvedAt = x.ConfigResolvedAt, CreatedAt = x.CreatedAt, Status = (LeaveStatus)x.Status };
            request.Tasks.AddRange(tasks[x.Id].OrderBy(t => t.Sequence).Select(t => new FlowTask { Id = t.Id, LeaveRequestId = t.LeaveRequestId, FlowInstanceId = t.FlowInstanceId, AssigneeId = t.AssigneeId, AssigneeName = t.AssigneeName, OriginalAssigneeId = t.OriginalAssigneeId, OriginalAssigneeName = t.OriginalAssigneeName, DelegationId = t.DelegationId, Sequence = t.Sequence, Status = (FlowTaskStatus)t.Status, Comment = t.Comment, ProcessedAt = t.ProcessedAt }));
            request.FlowInstances.AddRange(flowInstances.Load("Leave", x.Id));
            return request;
        }).ToList();
    }

    private void Persist(LeaveRequest request, int? expectedVersion = null)
    {
        if (db is null) return;
        db.ChangeTracker.Clear();
        var record = db.LeaveRequests.SingleOrDefault(x => x.Id == request.Id);
        if (record is null)
        {
            if (expectedVersion.HasValue) throw new DbUpdateConcurrencyException();
            record = new LeaveRecord { Id = request.Id, TenantId = TenantId };
            db.LeaveRequests.Add(record);
        }
        else if (expectedVersion.HasValue && record.Version != expectedVersion.Value)
        {
            throw new DbUpdateConcurrencyException();
        }
        record.Number = request.Number; record.ApplicantId = request.ApplicantId; record.ApplicantName = request.ApplicantName; record.Type = (int)request.Type; record.StartDate = request.StartDate; record.StartPeriod = (int)request.StartPeriod; record.EndDate = request.EndDate; record.EndPeriod = (int)request.EndPeriod; record.Days = request.Days; record.Reason = request.Reason; record.AttachmentsJson = JsonSerializer.Serialize(request.Attachments); record.Status = (int)request.Status; record.Version = request.Version; record.ProcessDefinitionId = request.ProcessDefinitionId; record.ProcessDefinitionCode = request.ProcessDefinitionCode; record.ProcessDefinitionVersion = request.ProcessDefinitionVersion; record.CurrentFlowInstanceId = request.CurrentFlowInstanceId; record.BalanceYear = request.BalanceYear; record.ConfigVersionId = request.ConfigVersionId; record.ConfigVersionNumber = request.ConfigVersionNumber; record.ConfigSnapshotJson = request.ConfigSnapshotJson; record.ConfigResolvedAt = request.ConfigResolvedAt; record.UpdatedAt = DateTimeOffset.UtcNow;
        db.FlowTasks.Where(x => x.LeaveRequestId == request.Id).ExecuteDelete();
        flowInstances.Track(request.FlowInstances);
        copyRecipients.Track("Leave", request.Id, request.Number, request.ApplicantName, $"{request.Type}请假：{request.Reason}", request.CopyRecipientIds);
        db.FlowTasks.AddRange(request.Tasks.Select(t => new FlowTaskRecord { Id = t.Id, TenantId = TenantId, LeaveRequestId = request.Id, FlowInstanceId = t.FlowInstanceId, AssigneeId = t.AssigneeId, AssigneeName = t.AssigneeName, OriginalAssigneeId = t.OriginalAssigneeId, OriginalAssigneeName = t.OriginalAssigneeName, DelegationId = t.DelegationId, Sequence = t.Sequence, Status = (int)t.Status, Comment = t.Comment, ProcessedAt = t.ProcessedAt }));
        db.SaveChanges();
    }

    private void PersistBalance(string employeeId, LeaveType type, int year)
    {
        if (type is not (LeaveType.Annual or LeaveType.CompTime)) return;
        var key = (employeeId, type, year);
        var balance = _balances[key];
        if (db is null)
        {
            _balances[key] = balance with { Version = balance.Version + 1 };
            return;
        }
        var record = db.LeaveBalances.SingleOrDefault(x => x.TenantId == TenantId && x.UserId == employeeId && x.LeaveType == (int)type && x.Year == year);
        if (record is null)
        {
            if (balance.Version != 0) throw new DbUpdateConcurrencyException();
            record = new LeaveBalanceRecord { TenantId = TenantId, UserId = employeeId, LeaveType = (int)type, Year = year, Version = 1 };
            db.LeaveBalances.Add(record);
        }
        else
        {
            if (record.Version != balance.Version) throw new DbUpdateConcurrencyException();
            record.Version = balance.Version + 1;
        }
        record.StatutoryEntitled = balance.StatutoryEntitled; record.Adjustment = balance.Adjustment; record.Entitled = balance.Entitled; record.Frozen = balance.Frozen; record.Used = balance.Used; record.UpdatedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
        _balances[key] = balance with { Version = record.Version };
    }

    private ServiceResult<T> ExecuteMutation<T>(Func<ServiceResult<T>> mutation)
    {
        if (db is null) return mutation();
        using var transaction = db.Database.BeginTransaction();
        try
        {
            var result = mutation();
            if (result.IsSuccess) transaction.Commit();
            else
            {
                transaction.Rollback();
                ReloadPersistedState();
            }
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            transaction.Rollback();
            ReloadPersistedState();
            return ServiceResult<T>.Failure("请假状态或假期余额已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        }
        catch (DbUpdateException exception) when (IsLeaveBalanceUniqueConflict(exception))
        {
            transaction.Rollback();
            ReloadPersistedState();
            return ServiceResult<T>.Failure("请假状态或假期余额已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        }
        catch
        {
            transaction.Rollback();
            ReloadPersistedState();
            throw;
        }
    }

    private void ReloadPersistedState()
    {
        if (db is null) return;
        db.ChangeTracker.Clear();
        _requests.Clear();
        _requests.AddRange(LoadRequests(db, flowInstances, copyRecipients));
        _balances.Clear();
    }

    private static bool IsLeaveBalanceUniqueConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_leave_balance_TenantId_UserId_LeaveType_Year"
        };

    private void Audit(Employee actor, string action, LeaveRequest request, string summary)
    {
        if (db is null) return;
        db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "LeaveRequest", ResourceId = request.Id.ToString(), Summary = summary });
        db.SaveChanges();
    }

    private static bool IsOverlapping(LeaveRequest first, LeaveRequest second) => first.StartDate <= second.EndDate && second.StartDate <= first.EndDate;

    public static decimal CalculateWorkingDays(DateOnly start, LeavePeriod startPeriod, DateOnly end, LeavePeriod endPeriod) =>
        CalculateWorkingDays(start, startPeriod, end, endPeriod, new WeekdayCalendar());

    private static decimal CalculateWorkingDays(DateOnly start, LeavePeriod startPeriod, DateOnly end, LeavePeriod endPeriod, IWorkCalendar calendar)
    {
        decimal days = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (!calendar.IsWorkingDay(date)) continue;
            if (date == start && date == end) return startPeriod == LeavePeriod.FullDay && endPeriod == LeavePeriod.FullDay ? 1m : 0.5m;
            if (date == start) days += startPeriod == LeavePeriod.FullDay ? 1m : 0.5m;
            else if (date == end) days += endPeriod == LeavePeriod.FullDay ? 1m : 0.5m;
            else days += 1m;
        }
        return days;
    }

    private sealed class WeekdayCalendar : IWorkCalendar
    {
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }
}
