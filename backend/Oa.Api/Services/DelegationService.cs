using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class DelegationService(OaDbContext db, DemoData data)
{
    private const string TenantId = "demo";
    private static readonly string[] BusinessTypes = ["All", "Leave", "Expense", "Travel"];

    public IReadOnlyList<FlowDelegationView> ListMine(Employee actor)
    {
        var now = DateTimeOffset.UtcNow;
        return db.FlowDelegations.AsNoTracking()
            .Where(item => item.TenantId == TenantId && item.OwnerId == actor.Id)
            .OrderByDescending(item => item.CreatedAt)
            .AsEnumerable()
            .Select(item => ToView(item, now))
            .ToList();
    }

    public ServiceResult<FlowDelegationView> Create(Employee actor, CreateFlowDelegationRequest request)
    {
        var delegateUser = data.FindEmployee(request.DelegateId);
        if (delegateUser is null || delegateUser.Status != "ACTIVE" || delegateUser.Id == actor.Id)
            return ServiceResult<FlowDelegationView>.Failure("代办人不存在、已停用或与委托人相同。", "FLOW_003");
        if (!BusinessTypes.Contains(request.BusinessType))
            return ServiceResult<FlowDelegationView>.Failure("业务类型仅支持 All、Leave、Expense 或 Travel。");
        if (request.EndAt <= request.StartAt || request.EndAt <= DateTimeOffset.UtcNow || request.EndAt - request.StartAt > TimeSpan.FromDays(180))
            return ServiceResult<FlowDelegationView>.Failure("委托结束时间必须晚于开始时间和当前时间，且跨度不超过 180 天。");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 200)
            return ServiceResult<FlowDelegationView>.Failure("委托原因应为 1–200 个字符。");
        var overlaps = db.FlowDelegations.Any(item => item.TenantId == TenantId && item.OwnerId == actor.Id && item.Status == (int)FlowDelegationStatus.Active &&
            item.StartAt < request.EndAt && request.StartAt < item.EndAt &&
            (item.BusinessType == "All" || request.BusinessType == "All" || item.BusinessType == request.BusinessType));
        if (overlaps) return ServiceResult<FlowDelegationView>.Failure("同一业务范围内存在时间重叠的有效委托。", "FLOW_004");

        var delegation = new FlowDelegationRecord
        {
            TenantId = TenantId,
            OwnerId = actor.Id,
            DelegateId = delegateUser.Id,
            BusinessType = request.BusinessType,
            StartAt = request.StartAt.ToUniversalTime(),
            EndAt = request.EndAt.ToUniversalTime(),
            Reason = request.Reason.Trim(),
            Status = (int)FlowDelegationStatus.Active
        };
        db.FlowDelegations.Add(delegation);
        db.SaveChanges();
        Audit(actor, "FLOW_DELEGATION_CREATED", delegation, $"委托 {delegateUser.Name} 处理 {request.BusinessType} 审批：{delegation.Reason}");
        return ServiceResult<FlowDelegationView>.Success(ToView(delegation, DateTimeOffset.UtcNow));
    }

    public ServiceResult<FlowDelegationView> Cancel(Employee actor, Guid id)
    {
        var delegation = db.FlowDelegations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (delegation is null || delegation.OwnerId != actor.Id)
            return ServiceResult<FlowDelegationView>.Failure("委托不存在或无取消权限。", "DATA_001");
        if (delegation.Status != (int)FlowDelegationStatus.Active)
            return ServiceResult<FlowDelegationView>.Failure("委托已取消。", "STATE_001");
        delegation.Status = (int)FlowDelegationStatus.Cancelled;
        delegation.CancelledAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
        Audit(actor, "FLOW_DELEGATION_CANCELLED", delegation, $"取消 {delegation.BusinessType} 审批委托");
        return ServiceResult<FlowDelegationView>.Success(ToView(delegation, DateTimeOffset.UtcNow));
    }

    private FlowDelegationView ToView(FlowDelegationRecord item, DateTimeOffset now) => new(
        item.Id,
        item.OwnerId,
        data.FindEmployee(item.OwnerId)?.Name ?? item.OwnerId,
        item.DelegateId,
        data.FindEmployee(item.DelegateId)?.Name ?? item.DelegateId,
        item.BusinessType,
        item.StartAt,
        item.EndAt,
        item.Reason,
        (FlowDelegationStatus)item.Status,
        item.Status == (int)FlowDelegationStatus.Active && item.StartAt <= now && now < item.EndAt,
        item.CreatedAt,
        item.CancelledAt);

    private void Audit(Employee actor, string action, FlowDelegationRecord delegation, string summary)
    {
        db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "FlowDelegation", ResourceId = delegation.Id.ToString(), Summary = summary });
        db.SaveChanges();
    }
}
