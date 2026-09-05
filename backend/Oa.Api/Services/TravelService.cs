using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class TravelService
{
    private const string TenantId = "demo";
    private readonly DemoData data;
    private readonly OaDbContext? db;
    private readonly NotificationService? notifications;
    private readonly FileService? files;
    private readonly IProcessRouter processRouter;
    private readonly FlowInstanceService flowInstances;
    private readonly FlowCopyService copyRecipients;
    private readonly List<TravelRequest> requests;

    public TravelService(DemoData data, OaDbContext? db = null, NotificationService? notifications = null, FileService? files = null, IProcessRouter? processRouter = null, FlowInstanceService? flowInstances = null, FlowCopyService? copyRecipients = null)
    {
        this.data = data;
        this.db = db;
        this.notifications = notifications;
        this.files = files;
        this.processRouter = processRouter ?? new DefaultProcessRouter(data);
        this.flowInstances = flowInstances ?? new FlowInstanceService(db);
        this.copyRecipients = copyRecipients ?? new FlowCopyService(data, db);
        requests = db is null ? [] : LoadRequests(db, this.flowInstances, this.copyRecipients, data);
    }

    public IReadOnlyList<TravelRequest> List(Employee actor, DocumentListQuery? query = null) => requests
        .Where(item => data.CanView(actor, data.GetEmployee(item.ApplicantId), "Travel"))
        .Where(item => string.IsNullOrWhiteSpace(query?.Keyword) || item.Number.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) || item.Purpose.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) || item.ApplicantName.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) || item.Itinerary.Any(line => line.Destination.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase)))
        .Where(item => query?.Status is null || (int)item.Status == query.Status)
        .Where(item => string.IsNullOrWhiteSpace(query?.ApplicantId) || item.ApplicantId == query.ApplicantId)
        .Where(item => query?.StartDate is null || item.StartDate >= query.StartDate)
        .Where(item => query?.EndDate is null || item.EndDate <= query.EndDate)
        .OrderByDescending(item => item.CreatedAt)
        .ToList();

    public ServiceResult<TravelRequest> Get(Employee actor, Guid id)
    {
        var item = requests.SingleOrDefault(request => request.Id == id);
        if (item is null) return ServiceResult<TravelRequest>.Failure("出差申请不存在。", "DATA_001");
        if (!data.CanView(actor, data.GetEmployee(item.ApplicantId), "Travel") && item.Tasks.All(task => task.AssigneeId != actor.Id) && !copyRecipients.CanView(actor, "Travel", item.Id))
            return ServiceResult<TravelRequest>.Failure("无权查看该出差申请。", "AUTH_002");
        return ServiceResult<TravelRequest>.Success(item);
    }

    public IReadOnlyList<TravelTask> GetPendingTasks(Employee actor) => requests
        .Where(item => item.Status == TravelStatus.Approving)
        .SelectMany(item => item.Tasks.Where(task => task.Status == FlowTaskStatus.Pending && task.Sequence == item.Tasks.Where(candidate => candidate.Status == FlowTaskStatus.Pending).Min(candidate => candidate.Sequence)))
        .Where(task => task.AssigneeId == actor.Id).OrderBy(task => task.Sequence).ToList();

    public IReadOnlyList<TravelTask> GetProcessedTasks(Employee actor) => requests.SelectMany(item => item.Tasks)
        .Where(task => task.AssigneeId == actor.Id && task.Status is FlowTaskStatus.Approved or FlowTaskStatus.Rejected)
        .OrderByDescending(task => task.ProcessedAt).ToList();

    public ServiceResult<TravelRequest> CreateDraft(Employee actor, SaveTravelRequest request)
    {
        var validated = Validate(actor, request);
        if (!validated.IsSuccess) return ServiceResult<TravelRequest>.Failure(validated.Error!, validated.Code!);
        var itinerary = request.Itinerary.OrderBy(item => item.StartDate).ToList();
        var companions = request.CompanionIds?.Distinct().Select(data.FindEmployee).Where(item => item is not null).Cast<Employee>().ToList() ?? [];
        var item = new TravelRequest
        {
            Number = $"CC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            ApplicantId = actor.Id, ApplicantName = actor.Name, DepartmentName = actor.DepartmentName,
            Purpose = request.Purpose.Trim(), StartDate = itinerary.Min(line => line.StartDate), EndDate = itinerary.Max(line => line.EndDate),
            Days = itinerary.Max(line => line.EndDate).DayNumber - itinerary.Min(line => line.StartDate).DayNumber + 1,
            EstimatedBudget = request.EstimatedBudget, Itinerary = itinerary, CompanionIds = companions.Select(value => value.Id).ToList(), CompanionNames = companions.Select(value => value.Name).ToList(),
            Attachments = request.Attachments?.Distinct().ToList() ?? [], CopyRecipientIds = validated.Value!
        };
        requests.Add(item); Persist(item); Audit(actor, "TRAVEL_DRAFT_CREATED", item, "创建出差草稿");
        return ServiceResult<TravelRequest>.Success(item);
    }

    public ServiceResult<TravelRequest> Update(Employee actor, Guid id, int expectedVersion, SaveTravelRequest request)
    {
        var original = requests.SingleOrDefault(item => item.Id == id);
        if (original is null || original.ApplicantId != actor.Id) return ServiceResult<TravelRequest>.Failure("出差申请不存在或无权限。", "DATA_001");
        if (original.Status is not (TravelStatus.Draft or TravelStatus.Rejected)) return ServiceResult<TravelRequest>.Failure("当前状态不允许编辑。", "STATE_001");
        if (original.Version != expectedVersion) return ServiceResult<TravelRequest>.Failure("单据已被更新，请刷新后重试。", "CONCURRENCY_001");
        var validated = Validate(actor, request);
        if (!validated.IsSuccess) return ServiceResult<TravelRequest>.Failure(validated.Error!, validated.Code!);
        var itinerary = request.Itinerary.OrderBy(item => item.StartDate).ToList();
        var companions = request.CompanionIds?.Distinct().Select(data.FindEmployee).Where(item => item is not null).Cast<Employee>().ToList() ?? [];
        var updated = new TravelRequest
        {
            Id = original.Id, Number = original.Number, ApplicantId = original.ApplicantId, ApplicantName = original.ApplicantName, DepartmentName = original.DepartmentName,
            Purpose = request.Purpose.Trim(), StartDate = itinerary.Min(line => line.StartDate), EndDate = itinerary.Max(line => line.EndDate), Days = itinerary.Max(line => line.EndDate).DayNumber - itinerary.Min(line => line.StartDate).DayNumber + 1,
            EstimatedBudget = request.EstimatedBudget, Itinerary = itinerary, CompanionIds = companions.Select(value => value.Id).ToList(), CompanionNames = companions.Select(value => value.Name).ToList(), Attachments = request.Attachments?.Distinct().ToList() ?? [], CopyRecipientIds = validated.Value!,
            Version = original.Version + 1, Status = original.Status, ProcessDefinitionId = original.ProcessDefinitionId, ProcessDefinitionCode = original.ProcessDefinitionCode, ProcessDefinitionVersion = original.ProcessDefinitionVersion, CurrentFlowInstanceId = original.CurrentFlowInstanceId, CreatedAt = original.CreatedAt
        };
        updated.Tasks.AddRange(original.Tasks); updated.FlowInstances.AddRange(original.FlowInstances);
        requests[requests.IndexOf(original)] = updated; Persist(updated); Audit(actor, "TRAVEL_UPDATED", updated, "编辑出差申请");
        return ServiceResult<TravelRequest>.Success(updated);
    }

    public ServiceResult<TravelRequest> Submit(Employee actor, Guid id)
    {
        var item = requests.SingleOrDefault(request => request.Id == id);
        if (item is null || item.ApplicantId != actor.Id) return ServiceResult<TravelRequest>.Failure("出差申请不存在或无权限。", "DATA_001");
        if (item.Status is not (TravelStatus.Draft or TravelStatus.Rejected)) return ServiceResult<TravelRequest>.Failure("当前状态不允许提交。", "STATE_001");
        if (requests.Any(other => other.Id != item.Id && other.ApplicantId == actor.Id && other.Status is TravelStatus.Approving or TravelStatus.Approved && other.StartDate <= item.EndDate && item.StartDate <= other.EndDate))
            return ServiceResult<TravelRequest>.Failure("存在时间重叠的有效出差申请。", "TRAVEL_002");
        var route = processRouter.Resolve("Travel", actor, item.Days);
        if (!route.IsSuccess) return ServiceResult<TravelRequest>.Failure(route.Error!, route.Code!);
        item.Status = TravelStatus.Approving; item.Tasks.Clear(); item.ProcessDefinitionId = route.Value!.DefinitionId; item.ProcessDefinitionCode = route.Value.Code; item.ProcessDefinitionVersion = route.Value.Version;
        var instance = flowInstances.Start(item.FlowInstances, "Travel", item.Id, item.Number, actor, route.Value); item.CurrentFlowInstanceId = instance.Id;
        foreach (var (approver, sequence) in route.Value.Approvers.Select((value, index) => (value, index + 1)))
            item.Tasks.Add(new TravelTask { TravelRequestId = item.Id, FlowInstanceId = instance.Id, AssigneeId = approver.Assignee.Id, AssigneeName = approver.Assignee.Name, OriginalAssigneeId = approver.DelegationId is null ? null : approver.OriginalApprover.Id, OriginalAssigneeName = approver.DelegationId is null ? null : approver.OriginalApprover.Name, DelegationId = approver.DelegationId, Sequence = sequence });
        Persist(item);
        flowInstances.RegisterTasks(instance, "Travel", item.Tasks.Select(task => new ResolvedFlowTask(task.Id, task.Sequence, route.Value.Approvers[task.Sequence - 1])).ToList());
        Audit(actor, "TRAVEL_SUBMITTED", item, "提交出差审批");
        if (item.Tasks.OrderBy(task => task.Sequence).FirstOrDefault() is { } first) notifications?.Create(first.AssigneeId, "TODO_CREATED", "新增出差审批待办", $"{item.ApplicantName} 提交了 {item.Number}", "TravelRequest", item.Id);
        return ServiceResult<TravelRequest>.Success(item);
    }

    public ServiceResult<TravelRequest> Approve(Employee actor, Guid taskId, string? comment)
    {
        var found = FindTask(taskId); if (found is null) return ServiceResult<TravelRequest>.Failure("审批任务不存在。", "DATA_001");
        var (item, task) = found.Value;
        if (task.AssigneeId != actor.Id || task.Status != FlowTaskStatus.Pending) return ServiceResult<TravelRequest>.Failure("当前用户不能处理该审批任务。", "AUTH_002");
        if (item.Tasks.Any(candidate => candidate.Sequence < task.Sequence && candidate.Status != FlowTaskStatus.Approved)) return ServiceResult<TravelRequest>.Failure("前序审批尚未完成。", "STATE_001");
        task.Status = FlowTaskStatus.Approved; task.Comment = comment?.Trim(); task.ProcessedAt = DateTimeOffset.UtcNow;
        var instance = flowInstances.Current(item.FlowInstances, task.FlowInstanceId); if (instance is not null) flowInstances.Record(instance, FlowActionType.Approved, actor, task.Id, task.Sequence, task.Comment);
        if (item.Tasks.All(candidate => candidate.Status == FlowTaskStatus.Approved)) { item.Status = TravelStatus.Approved; if (instance is not null) flowInstances.Complete(instance); }
        Persist(item); var copies = item.Status == TravelStatus.Approved ? copyRecipients.Activate("Travel", item.Id, item.Status.ToString()) : [];
        Audit(actor, "TRAVEL_APPROVED", item, comment?.Trim() ?? "同意出差");
        if (item.Status == TravelStatus.Approved) { notifications?.Create(item.ApplicantId, "TRAVEL_APPROVED", "出差申请已审批通过", $"{item.Number} 已全部审批通过", "TravelRequest", item.Id); foreach (var recipient in copies) notifications?.Create(recipient.Id, "FLOW_COPY", "出差抄送事项已完成", $"{item.ApplicantName} 的 {item.Number} 已审批完成", "TravelRequest", item.Id); }
        else if (item.Tasks.FirstOrDefault(candidate => candidate.Sequence == task.Sequence + 1 && candidate.Status == FlowTaskStatus.Pending) is { } next) notifications?.Create(next.AssigneeId, "TODO_CREATED", "新增出差审批待办", $"{item.Number} 等待你审批", "TravelRequest", item.Id);
        return ServiceResult<TravelRequest>.Success(item);
    }

    public ServiceResult<TravelRequest> Reject(Employee actor, Guid taskId, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return ServiceResult<TravelRequest>.Failure("驳回必须填写意见。");
        var found = FindTask(taskId); if (found is null) return ServiceResult<TravelRequest>.Failure("审批任务不存在。", "DATA_001"); var (item, task) = found.Value;
        if (task.AssigneeId != actor.Id || task.Status != FlowTaskStatus.Pending) return ServiceResult<TravelRequest>.Failure("当前用户不能处理该审批任务。", "AUTH_002");
        task.Status = FlowTaskStatus.Rejected; task.Comment = comment.Trim(); task.ProcessedAt = DateTimeOffset.UtcNow; foreach (var candidate in item.Tasks.Where(candidate => candidate.Id != task.Id && candidate.Status == FlowTaskStatus.Pending)) candidate.Status = FlowTaskStatus.Cancelled; item.Status = TravelStatus.Rejected;
        if (flowInstances.Current(item.FlowInstances, task.FlowInstanceId) is { } instance) flowInstances.Record(instance, FlowActionType.Rejected, actor, task.Id, task.Sequence, task.Comment);
        Persist(item); Audit(actor, "TRAVEL_REJECTED", item, comment.Trim()); notifications?.Create(item.ApplicantId, "TRAVEL_REJECTED", "出差申请已驳回", $"{item.Number} 已被驳回：{comment.Trim()}", "TravelRequest", item.Id);
        return ServiceResult<TravelRequest>.Success(item);
    }

    public ServiceResult<TravelRequest> Transfer(Employee actor, Guid taskId, TransferTaskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Comment)) return ServiceResult<TravelRequest>.Failure("转办必须填写意见。");
        var found = FindTask(taskId); if (found is null) return ServiceResult<TravelRequest>.Failure("审批任务不存在。", "DATA_001"); var (item, task) = found.Value;
        if (task.AssigneeId != actor.Id || task.Status != FlowTaskStatus.Pending) return ServiceResult<TravelRequest>.Failure("当前用户不能转办该审批任务。", "AUTH_002");
        var assignee = data.FindEmployee(request.AssigneeId); if (assignee is null || assignee.Id == actor.Id) return ServiceResult<TravelRequest>.Failure("转办人不存在或不能为当前审批人。", "FLOW_002");
        var previous = data.GetEmployee(task.AssigneeId); task.AssigneeId = assignee.Id; task.AssigneeName = assignee.Name;
        if (flowInstances.Current(item.FlowInstances, task.FlowInstanceId) is { } instance) flowInstances.Record(instance, FlowActionType.Transferred, actor, task.Id, task.Sequence, request.Comment, previous, assignee);
        Persist(item); Audit(actor, "TRAVEL_TASK_TRANSFERRED", item, $"将第 {task.Sequence} 节点转办给 {assignee.Name}：{request.Comment.Trim()}"); notifications?.Create(assignee.Id, "TODO_TRANSFERRED", "出差审批待办已转办给你", $"{item.Number}：{request.Comment.Trim()}", "TravelRequest", item.Id);
        return ServiceResult<TravelRequest>.Success(item);
    }

    public ServiceResult<TravelRequest> Withdraw(Employee actor, Guid id)
    {
        var item = requests.SingleOrDefault(request => request.Id == id);
        if (item is null || item.ApplicantId != actor.Id) return ServiceResult<TravelRequest>.Failure("出差申请不存在或无权限。", "DATA_001");
        if (item.Status != TravelStatus.Approving || item.Tasks.Any(task => task.Status != FlowTaskStatus.Pending)) return ServiceResult<TravelRequest>.Failure("仅尚未处理的审批中申请可撤回。", "STATE_001");
        foreach (var task in item.Tasks) task.Status = FlowTaskStatus.Cancelled; item.Status = TravelStatus.Withdrawn;
        if (flowInstances.Current(item.FlowInstances, item.CurrentFlowInstanceId) is { } instance) flowInstances.Record(instance, FlowActionType.Withdrawn, actor, comment: "申请人撤回");
        Persist(item); var copies = copyRecipients.Activate("Travel", item.Id, item.Status.ToString()); Audit(actor, "TRAVEL_WITHDRAWN", item, "撤回出差申请");
        foreach (var task in item.Tasks.Where(task => task.AssigneeId != actor.Id)) notifications?.Create(task.AssigneeId, "TRAVEL_WITHDRAWN", "出差申请已撤回", $"{item.Number} 已被申请人撤回", "TravelRequest", item.Id);
        foreach (var recipient in copies) notifications?.Create(recipient.Id, "FLOW_COPY", "出差抄送事项已撤回", $"{item.ApplicantName} 的 {item.Number} 已撤回", "TravelRequest", item.Id);
        return ServiceResult<TravelRequest>.Success(item);
    }

    public ServiceResult<bool> Delete(Employee actor, Guid id)
    {
        var item = requests.SingleOrDefault(request => request.Id == id); if (item is null || item.ApplicantId != actor.Id) return ServiceResult<bool>.Failure("出差申请不存在或无权限。", "DATA_001");
        if (item.Status is not (TravelStatus.Draft or TravelStatus.Rejected or TravelStatus.Withdrawn)) return ServiceResult<bool>.Failure("当前状态不允许删除。", "STATE_001");
        requests.Remove(item); if (db is not null) { copyRecipients.DeleteUnavailable("Travel", item.Id); db.TravelTasks.Where(task => task.TravelRequestId == item.Id).ExecuteDelete(); db.TravelRequests.Where(request => request.Id == item.Id).ExecuteDelete(); db.SaveChanges(); }
        Audit(actor, "TRAVEL_DELETED", item, "删除出差申请"); return ServiceResult<bool>.Success(true);
    }

    private ServiceResult<IReadOnlyList<string>> Validate(Employee actor, SaveTravelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Purpose) || request.Purpose.Trim().Length > 500) return ServiceResult<IReadOnlyList<string>>.Failure("出差事由应为 1–500 个字符。");
        if (request.EstimatedBudget < 0 || request.EstimatedBudget != decimal.Round(request.EstimatedBudget, 2) || request.EstimatedBudget > 10_000_000m) return ServiceResult<IReadOnlyList<string>>.Failure("预估预算应为 0–1000 万元且最多两位小数。");
        if (request.Itinerary.Count is < 1 or > 20) return ServiceResult<IReadOnlyList<string>>.Failure("行程明细应为 1–20 条。", "TRAVEL_001");
        var itinerary = request.Itinerary.OrderBy(item => item.StartDate).ToList();
        if (itinerary.Any(item => string.IsNullOrWhiteSpace(item.Destination) || item.Destination.Trim().Length > 100 || string.IsNullOrWhiteSpace(item.Purpose) || item.Purpose.Trim().Length > 300 || string.IsNullOrWhiteSpace(item.Transportation) || item.Transportation.Trim().Length > 32 || item.EndDate < item.StartDate)) return ServiceResult<IReadOnlyList<string>>.Failure("行程地点、日期、交通方式或任务说明不合法。", "TRAVEL_001");
        if (itinerary[^1].EndDate.DayNumber - itinerary[0].StartDate.DayNumber + 1 > 180) return ServiceResult<IReadOnlyList<string>>.Failure("单次出差跨度不能超过 180 天。", "TRAVEL_001");
        if (itinerary.Zip(itinerary.Skip(1)).Any(pair => pair.Second.StartDate <= pair.First.EndDate)) return ServiceResult<IReadOnlyList<string>>.Failure("行程日期不能重叠。", "TRAVEL_001");
        var companionIds = request.CompanionIds?.Distinct().ToList() ?? [];
        if (companionIds.Count > 20 || companionIds.Contains(actor.Id) || companionIds.Any(id => data.FindEmployee(id) is not { Status: "ACTIVE" })) return ServiceResult<IReadOnlyList<string>>.Failure("同行人不存在、已停用、包含申请人或超过 20 人。", "TRAVEL_003");
        if (files is not null && request.Attachments is { Count: > 0 } && !files.AreOwnedBy(actor, request.Attachments)) return ServiceResult<IReadOnlyList<string>>.Failure("附件不存在或不属于当前用户。", "FILE_005");
        return copyRecipients.Validate(actor, request.CopyRecipientIds);
    }

    private (TravelRequest item, TravelTask task)? FindTask(Guid taskId) => requests.SelectMany(item => item.Tasks.Select(task => (item, task))).Cast<(TravelRequest item, TravelTask task)?>().SingleOrDefault(pair => pair!.Value.task.Id == taskId);

    private static List<TravelRequest> LoadRequests(OaDbContext db, FlowInstanceService flows, FlowCopyService copies, DemoData data)
    {
        var tasks = db.TravelTasks.AsNoTracking().Where(item => item.TenantId == TenantId).ToLookup(item => item.TravelRequestId);
        return db.TravelRequests.AsNoTracking().Where(item => item.TenantId == TenantId).OrderBy(item => item.CreatedAt).ToList().Select(record =>
        {
            var companionIds = JsonSerializer.Deserialize<List<string>>(record.CompanionIdsJson) ?? [];
            var item = new TravelRequest { Id = record.Id, Number = record.Number, ApplicantId = record.ApplicantId, ApplicantName = record.ApplicantName, DepartmentName = record.DepartmentName, Purpose = record.Purpose, StartDate = record.StartDate, EndDate = record.EndDate, Days = record.Days, EstimatedBudget = record.EstimatedBudget, Itinerary = JsonSerializer.Deserialize<List<TravelItineraryItem>>(record.ItineraryJson) ?? [], CompanionIds = companionIds, CompanionNames = companionIds.Select(data.FindEmployee).Where(value => value is not null).Select(value => value!.Name).ToList(), Attachments = JsonSerializer.Deserialize<List<string>>(record.AttachmentsJson) ?? [], CopyRecipientIds = copies.LoadRecipientIds("Travel", record.Id), Version = record.Version, Status = (TravelStatus)record.Status, ProcessDefinitionId = record.ProcessDefinitionId, ProcessDefinitionCode = record.ProcessDefinitionCode, ProcessDefinitionVersion = record.ProcessDefinitionVersion, CurrentFlowInstanceId = record.CurrentFlowInstanceId, CreatedAt = record.CreatedAt };
            item.Tasks.AddRange(tasks[record.Id].OrderBy(task => task.Sequence).Select(task => new TravelTask { Id = task.Id, TravelRequestId = record.Id, FlowInstanceId = task.FlowInstanceId, AssigneeId = task.AssigneeId, AssigneeName = task.AssigneeName, OriginalAssigneeId = task.OriginalAssigneeId, OriginalAssigneeName = task.OriginalAssigneeName, DelegationId = task.DelegationId, Sequence = task.Sequence, Status = (FlowTaskStatus)task.Status, Comment = task.Comment, ProcessedAt = task.ProcessedAt })); item.FlowInstances.AddRange(flows.Load("Travel", record.Id)); return item;
        }).ToList();
    }

    private void Persist(TravelRequest item)
    {
        if (db is null) return; db.ChangeTracker.Clear(); var record = db.TravelRequests.SingleOrDefault(value => value.Id == item.Id); if (record is null) { record = new TravelRecord { Id = item.Id, TenantId = TenantId }; db.TravelRequests.Add(record); }
        record.Number = item.Number; record.ApplicantId = item.ApplicantId; record.ApplicantName = item.ApplicantName; record.DepartmentName = item.DepartmentName; record.Purpose = item.Purpose; record.StartDate = item.StartDate; record.EndDate = item.EndDate; record.Days = item.Days; record.EstimatedBudget = item.EstimatedBudget; record.ItineraryJson = JsonSerializer.Serialize(item.Itinerary); record.CompanionIdsJson = JsonSerializer.Serialize(item.CompanionIds); record.AttachmentsJson = JsonSerializer.Serialize(item.Attachments); record.Status = (int)item.Status; record.Version = item.Version; record.ProcessDefinitionId = item.ProcessDefinitionId; record.ProcessDefinitionCode = item.ProcessDefinitionCode; record.ProcessDefinitionVersion = item.ProcessDefinitionVersion; record.CurrentFlowInstanceId = item.CurrentFlowInstanceId; record.UpdatedAt = DateTimeOffset.UtcNow;
        db.TravelTasks.Where(task => task.TravelRequestId == item.Id).ExecuteDelete(); flowInstances.Track(item.FlowInstances); copyRecipients.Track("Travel", item.Id, item.Number, item.ApplicantName, item.Purpose, item.CopyRecipientIds);
        db.TravelTasks.AddRange(item.Tasks.Select(task => new TravelTaskRecord { Id = task.Id, TenantId = TenantId, TravelRequestId = item.Id, FlowInstanceId = task.FlowInstanceId, AssigneeId = task.AssigneeId, AssigneeName = task.AssigneeName, OriginalAssigneeId = task.OriginalAssigneeId, OriginalAssigneeName = task.OriginalAssigneeName, DelegationId = task.DelegationId, Sequence = task.Sequence, Status = (int)task.Status, Comment = task.Comment, ProcessedAt = task.ProcessedAt })); db.SaveChanges();
    }

    private void Audit(Employee actor, string action, TravelRequest item, string summary) { if (db is null) return; db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "TravelRequest", ResourceId = item.Id.ToString(), Summary = summary }); db.SaveChanges(); }
}
