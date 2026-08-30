using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class FlowInstanceService(OaDbContext? db = null)
{
    private const string TenantId = "demo";

    public FlowInstance Start(
        IList<FlowInstance> history,
        string businessType,
        Guid businessId,
        string businessNumber,
        Employee applicant,
        ResolvedProcess process)
    {
        var instance = new FlowInstance
        {
            BusinessType = businessType,
            BusinessId = businessId,
            BusinessNumber = businessNumber,
            ApplicantId = applicant.Id,
            ProcessDefinitionId = process.DefinitionId,
            ProcessDefinitionCode = process.Code,
            ProcessDefinitionVersion = process.Version,
            Attempt = history.Count == 0 ? 1 : history.Max(item => item.Attempt) + 1
        };
        instance.Actions.Add(NewAction(instance.Id, FlowActionType.Submitted, applicant, comment: "提交审批"));
        history.Add(instance);
        return instance;
    }

    public void Record(
        FlowInstance instance,
        FlowActionType action,
        Employee actor,
        Guid? taskId = null,
        int? sequence = null,
        string? comment = null,
        Employee? fromAssignee = null,
        Employee? toAssignee = null)
    {
        instance.Actions.Add(NewAction(instance.Id, action, actor, taskId, sequence, comment, fromAssignee, toAssignee));
        if (action is FlowActionType.Approved && instance.Status == FlowInstanceStatus.Running) return;
        instance.Status = action switch
        {
            FlowActionType.Rejected => FlowInstanceStatus.Rejected,
            FlowActionType.Withdrawn => FlowInstanceStatus.Withdrawn,
            _ => instance.Status
        };
        if (action is FlowActionType.Rejected or FlowActionType.Withdrawn) instance.CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Complete(FlowInstance instance)
    {
        instance.Status = FlowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
    }

    public FlowInstance? Current(IReadOnlyCollection<FlowInstance> history, Guid? id) =>
        id is null ? null : history.SingleOrDefault(item => item.Id == id);

    public IReadOnlyList<FlowInstance> Load(string businessType, Guid businessId)
    {
        if (db is null) return [];
        var instances = db.FlowInstances.AsNoTracking()
            .Where(item => item.TenantId == TenantId && item.BusinessType == businessType && item.BusinessId == businessId)
            .OrderBy(item => item.Attempt)
            .ToList();
        if (instances.Count == 0) return [];
        var ids = instances.Select(item => item.Id).ToList();
        var actions = db.FlowActions.AsNoTracking().Where(item => ids.Contains(item.FlowInstanceId)).OrderBy(item => item.OccurredAt).ToLookup(item => item.FlowInstanceId);
        return instances.Select(item => ToDomain(item, actions[item.Id])).ToList();
    }

    public ServiceResult<FlowInstance> Get(Guid id)
    {
        if (db is null) return ServiceResult<FlowInstance>.Failure("流程实例不存在。", "DATA_001");
        var record = db.FlowInstances.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<FlowInstance>.Failure("流程实例不存在。", "DATA_001");
        var actions = db.FlowActions.AsNoTracking().Where(item => item.FlowInstanceId == id).OrderBy(item => item.OccurredAt).ToList();
        return ServiceResult<FlowInstance>.Success(ToDomain(record, actions));
    }

    public void Track(IEnumerable<FlowInstance> instances)
    {
        if (db is null) return;
        foreach (var instance in instances)
        {
            var record = db.FlowInstances.Local.SingleOrDefault(item => item.Id == instance.Id) ?? db.FlowInstances.SingleOrDefault(item => item.Id == instance.Id);
            if (record is null)
            {
                record = new FlowInstanceRecord { Id = instance.Id, TenantId = TenantId };
                db.FlowInstances.Add(record);
            }
            record.BusinessType = instance.BusinessType;
            record.BusinessId = instance.BusinessId;
            record.BusinessNumber = instance.BusinessNumber;
            record.ApplicantId = instance.ApplicantId;
            record.ProcessDefinitionId = instance.ProcessDefinitionId;
            record.ProcessDefinitionCode = instance.ProcessDefinitionCode;
            record.ProcessDefinitionVersion = instance.ProcessDefinitionVersion;
            record.Attempt = instance.Attempt;
            record.Status = (int)instance.Status;
            record.StartedAt = instance.StartedAt;
            record.CompletedAt = instance.CompletedAt;

            foreach (var action in instance.Actions)
            {
                if (db.FlowActions.Local.Any(item => item.Id == action.Id) || db.FlowActions.Any(item => item.Id == action.Id)) continue;
                db.FlowActions.Add(new FlowActionRecord
                {
                    Id = action.Id,
                    TenantId = TenantId,
                    FlowInstanceId = action.FlowInstanceId,
                    TaskId = action.TaskId,
                    Sequence = action.Sequence,
                    Action = (int)action.Action,
                    ActorId = action.ActorId,
                    ActorName = action.ActorName,
                    FromAssigneeId = action.FromAssigneeId,
                    FromAssigneeName = action.FromAssigneeName,
                    ToAssigneeId = action.ToAssigneeId,
                    ToAssigneeName = action.ToAssigneeName,
                    Comment = action.Comment,
                    OccurredAt = action.OccurredAt
                });
            }
        }
    }

    private static FlowAction NewAction(Guid instanceId, FlowActionType action, Employee actor, Guid? taskId = null, int? sequence = null, string? comment = null, Employee? fromAssignee = null, Employee? toAssignee = null) => new()
    {
        FlowInstanceId = instanceId,
        TaskId = taskId,
        Sequence = sequence,
        Action = action,
        ActorId = actor.Id,
        ActorName = actor.Name,
        FromAssigneeId = fromAssignee?.Id,
        FromAssigneeName = fromAssignee?.Name,
        ToAssigneeId = toAssignee?.Id,
        ToAssigneeName = toAssignee?.Name,
        Comment = comment?.Trim()
    };

    private static FlowInstance ToDomain(FlowInstanceRecord record, IEnumerable<FlowActionRecord> actions)
    {
        var instance = new FlowInstance
        {
            Id = record.Id,
            BusinessType = record.BusinessType,
            BusinessId = record.BusinessId,
            BusinessNumber = record.BusinessNumber,
            ApplicantId = record.ApplicantId,
            ProcessDefinitionId = record.ProcessDefinitionId,
            ProcessDefinitionCode = record.ProcessDefinitionCode,
            ProcessDefinitionVersion = record.ProcessDefinitionVersion,
            Attempt = record.Attempt,
            Status = (FlowInstanceStatus)record.Status,
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt
        };
        instance.Actions.AddRange(actions.Select(item => new FlowAction
        {
            Id = item.Id,
            FlowInstanceId = item.FlowInstanceId,
            TaskId = item.TaskId,
            Sequence = item.Sequence,
            Action = (FlowActionType)item.Action,
            ActorId = item.ActorId,
            ActorName = item.ActorName,
            FromAssigneeId = item.FromAssigneeId,
            FromAssigneeName = item.FromAssigneeName,
            ToAssigneeId = item.ToAssigneeId,
            ToAssigneeName = item.ToAssigneeName,
            Comment = item.Comment,
            OccurredAt = item.OccurredAt
        }));
        return instance;
    }
}
