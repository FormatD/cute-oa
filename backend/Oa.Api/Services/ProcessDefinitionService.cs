using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public static class ProcessDefaults
{
    public static readonly Guid LeaveId = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    public static readonly Guid ExpenseId = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    public static readonly Guid TravelId = Guid.Parse("a3333333-3333-3333-3333-333333333333");
    public static readonly Guid PurchaseId = Guid.Parse("a4444444-4444-4444-4444-444444444444");
    public static readonly Guid SealId = Guid.Parse("a5555555-5555-5555-5555-555555555555");

    public static IReadOnlyList<ProcessDefinitionView> All { get; } =
    [
        new(LeaveId, "LEAVE_DEFAULT", "默认请假审批", "Leave", 1, ProcessDefinitionStatus.Published,
        [
            new(Guid.Empty, 1, 3m, ["DIRECT_MANAGER"]),
            new(Guid.Empty, 2, null, ["DIRECT_MANAGER", "ROLE:总经理"])
        ], "system", DateTimeOffset.UnixEpoch, "system", DateTimeOffset.UnixEpoch),
        new(ExpenseId, "EXPENSE_DEFAULT", "默认报销审批", "Expense", 1, ProcessDefinitionStatus.Published,
        [
            new(Guid.Empty, 1, 1000m, ["DIRECT_MANAGER", "ROLE:财务专员"]),
            new(Guid.Empty, 2, 5000m, ["DIRECT_MANAGER", "ROLE:财务专员", "ROLE:财务经理"]),
            new(Guid.Empty, 3, null, ["DIRECT_MANAGER", "ROLE:财务专员", "ROLE:财务经理", "ROLE:总经理"])
        ], "system", DateTimeOffset.UnixEpoch, "system", DateTimeOffset.UnixEpoch),
        new(TravelId, "TRAVEL_DEFAULT", "默认出差审批", "Travel", 1, ProcessDefinitionStatus.Published,
        [
            new(Guid.Empty, 1, 3m, ["DIRECT_MANAGER"]),
            new(Guid.Empty, 2, null, ["DIRECT_MANAGER", "ROLE:总经理"])
        ], "system", DateTimeOffset.UnixEpoch, "system", DateTimeOffset.UnixEpoch),
        new(PurchaseId, "PURCHASE_DEFAULT", "默认采购审批", "Purchase", 1, ProcessDefinitionStatus.Published,
        [
            new(Guid.Empty, 1, 5_000m, ["DIRECT_MANAGER"]),
            new(Guid.Empty, 2, 50_000m, ["DIRECT_MANAGER", "ROLE:财务经理"]),
            new(Guid.Empty, 3, null, ["DIRECT_MANAGER", "ROLE:财务经理", "ROLE:总经理"])
        ], "system", DateTimeOffset.UnixEpoch, "system", DateTimeOffset.UnixEpoch),
        new(SealId, "SEAL_DEFAULT", "默认用章审批", "Seal", 1, ProcessDefinitionStatus.Published,
        [
            new(Guid.Empty, 1, 1m, ["DIRECT_MANAGER"]),
            new(Guid.Empty, 2, 2m, ["DIRECT_MANAGER", "ROLE:HR/行政"]),
            new(Guid.Empty, 3, null, ["DIRECT_MANAGER", "ROLE:HR/行政", "ROLE:总经理"])
        ], "system", DateTimeOffset.UnixEpoch, "system", DateTimeOffset.UnixEpoch)
    ];
}

public sealed class DefaultProcessRouter(DemoData data) : IProcessRouter
{
    public ServiceResult<ResolvedProcess> Resolve(string businessType, Employee applicant, decimal metric, string? category = null)
    {
        var definition = ProcessDefaults.All.SingleOrDefault(item => item.BusinessType == businessType);
        return definition is null
            ? ServiceResult<ResolvedProcess>.Failure("没有可用的已发布流程定义。", "FLOW_001")
            : ProcessRouting.Resolve(definition, data, applicant, metric);
    }
}

public sealed class ProcessDefinitionService(OaDbContext db, DemoData data) : IProcessRouter
{
    private const string TenantId = "demo";

    public ServiceResult<IReadOnlyList<ProcessDefinitionView>> List(Employee actor)
    {
        if (!IsAdministrator(actor)) return ServiceResult<IReadOnlyList<ProcessDefinitionView>>.Failure("无流程维护权限。", "AUTH_002");
        EnsureDefaults();
        return ServiceResult<IReadOnlyList<ProcessDefinitionView>>.Success(LoadAll());
    }

    public ServiceResult<ProcessDefinitionView> Create(Employee actor, CreateProcessDefinitionRequest request)
    {
        if (!IsAdministrator(actor)) return ServiceResult<ProcessDefinitionView>.Failure("仅系统管理员可维护流程定义。", "AUTH_002");
        EnsureDefaults();
        var code = request.Code.Trim().ToUpperInvariant();
        if (code.Length is < 3 or > 64 || code.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
            return ServiceResult<ProcessDefinitionView>.Failure("流程编码应为 3–64 位大写字母、数字或下划线。", "VALIDATION_001");
        if (db.ProcessDefinitions.Any(item => item.TenantId == TenantId && item.Code == code))
            return ServiceResult<ProcessDefinitionView>.Failure("流程编码已存在，请从已有版本复制。", "DUPLICATE_001");
        var validation = Validate(request.Name, request.BusinessType, request.Routes, request.Priority, request.DepartmentIds, request.LeaveTypes);
        if (!validation.IsSuccess) return ServiceResult<ProcessDefinitionView>.Failure(validation.Error!, validation.Code!);

        var definition = new ProcessDefinitionRecord { TenantId = TenantId, Code = code, Name = request.Name.Trim(), BusinessType = request.BusinessType, Version = 1, Status = (int)ProcessDefinitionStatus.Draft, Priority = request.Priority, CreatedBy = actor.Id };
        SaveAggregate(definition, request.Routes, request.DepartmentIds, request.LeaveTypes);
        Audit(actor, "PROCESS_DEFINITION_CREATED", definition, $"创建流程 {definition.Code} v{definition.Version} 草稿");
        return ServiceResult<ProcessDefinitionView>.Success(Load(definition.Id)!);
    }

    public ServiceResult<ProcessDefinitionView> Clone(Employee actor, Guid id)
    {
        if (!IsAdministrator(actor)) return ServiceResult<ProcessDefinitionView>.Failure("仅系统管理员可维护流程定义。", "AUTH_002");
        EnsureDefaults();
        var source = Load(id);
        if (source is null) return ServiceResult<ProcessDefinitionView>.Failure("流程定义不存在。", "DATA_001");
        if (db.ProcessDefinitions.Any(item => item.TenantId == TenantId && item.Code == source.Code && item.Status == (int)ProcessDefinitionStatus.Draft))
            return ServiceResult<ProcessDefinitionView>.Failure("该流程已有草稿版本，请先编辑或发布现有草稿。", "STATE_001");
        var version = db.ProcessDefinitions.Where(item => item.TenantId == TenantId && item.Code == source.Code).Max(item => item.Version) + 1;
        var definition = new ProcessDefinitionRecord { TenantId = TenantId, Code = source.Code, Name = source.Name, BusinessType = source.BusinessType, Version = version, Status = (int)ProcessDefinitionStatus.Draft, Priority = source.Priority, CreatedBy = actor.Id };
        SaveAggregate(definition, source.Routes.Select(ToInput).ToList(), source.DepartmentIds, source.LeaveTypes);
        Audit(actor, "PROCESS_DEFINITION_CLONED", definition, $"从 {source.Code} v{source.Version} 复制 v{version} 草稿");
        return ServiceResult<ProcessDefinitionView>.Success(Load(definition.Id)!);
    }

    public ServiceResult<ProcessDefinitionView> Update(Employee actor, Guid id, UpdateProcessDefinitionRequest request)
    {
        if (!IsAdministrator(actor)) return ServiceResult<ProcessDefinitionView>.Failure("仅系统管理员可维护流程定义。", "AUTH_002");
        EnsureDefaults();
        var definition = db.ProcessDefinitions.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (definition is null) return ServiceResult<ProcessDefinitionView>.Failure("流程定义不存在。", "DATA_001");
        if (definition.Status != (int)ProcessDefinitionStatus.Draft) return ServiceResult<ProcessDefinitionView>.Failure("已发布或已归档版本不可修改，请复制新版本。", "STATE_001");
        var validation = Validate(request.Name, definition.BusinessType, request.Routes, request.Priority, request.DepartmentIds, request.LeaveTypes);
        if (!validation.IsSuccess) return ServiceResult<ProcessDefinitionView>.Failure(validation.Error!, validation.Code!);
        definition.Name = request.Name.Trim();
        definition.Priority = request.Priority;
        ReplaceAggregate(definition, request.Routes, request.DepartmentIds, request.LeaveTypes);
        Audit(actor, "PROCESS_DEFINITION_UPDATED", definition, $"更新流程 {definition.Code} v{definition.Version} 草稿");
        return ServiceResult<ProcessDefinitionView>.Success(Load(definition.Id)!);
    }

    public ServiceResult<ProcessDefinitionView> Publish(Employee actor, Guid id)
    {
        if (!IsAdministrator(actor)) return ServiceResult<ProcessDefinitionView>.Failure("仅系统管理员可维护流程定义。", "AUTH_002");
        EnsureDefaults();
        var definition = db.ProcessDefinitions.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (definition is null) return ServiceResult<ProcessDefinitionView>.Failure("流程定义不存在。", "DATA_001");
        if (definition.Status != (int)ProcessDefinitionStatus.Draft) return ServiceResult<ProcessDefinitionView>.Failure("仅草稿版本可以发布。", "STATE_001");
        var candidate = Load(definition.Id)!;
        var conflict = FindPublishConflict(candidate);
        if (conflict is not null)
            return ServiceResult<ProcessDefinitionView>.Failure($"适用范围与已发布流程 {conflict.Code} v{conflict.Version} 冲突，请调整优先级或范围。", "FLOW_003");
        foreach (var current in db.ProcessDefinitions.Where(item => item.TenantId == TenantId && item.Code == definition.Code && item.Status == (int)ProcessDefinitionStatus.Published))
            current.Status = (int)ProcessDefinitionStatus.Archived;
        definition.Status = (int)ProcessDefinitionStatus.Published;
        definition.PublishedBy = actor.Id;
        definition.PublishedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
        Audit(actor, "PROCESS_DEFINITION_PUBLISHED", definition, $"发布流程 {definition.Code} v{definition.Version}");
        return ServiceResult<ProcessDefinitionView>.Success(Load(definition.Id)!);
    }

    public ServiceResult<ResolvedProcess> Resolve(string businessType, Employee applicant, decimal metric, string? category = null)
    {
        EnsureDefaults();
        var definitions = db.ProcessDefinitions.AsNoTracking()
            .Where(item => item.TenantId == TenantId && item.BusinessType == businessType && item.Status == (int)ProcessDefinitionStatus.Published)
            .Select(item => item.Id)
            .ToList()
            .Select(id => Load(id)!)
            .Where(item => Applies(item, applicant, category))
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(Specificity)
            .ThenByDescending(item => item.Version)
            .ThenByDescending(item => item.PublishedAt)
            .ToList();
        var definition = definitions.FirstOrDefault();
        if (definition is null) return ServiceResult<ResolvedProcess>.Failure("没有可用的已发布流程定义。", "FLOW_001");
        var resolved = ProcessRouting.Resolve(definition, data, applicant, metric);
        return resolved.IsSuccess ? ApplyDelegations(resolved.Value!, businessType) : resolved;
    }

    public ServiceResult<ProcessSimulationView> Simulate(Employee actor, Guid id, SimulateProcessRequest request)
    {
        if (!IsAdministrator(actor)) return ServiceResult<ProcessSimulationView>.Failure("无流程维护权限。", "AUTH_002");
        EnsureDefaults();
        var definition = Load(id);
        if (definition is null) return ServiceResult<ProcessSimulationView>.Failure("流程定义不存在。", "DATA_001");
        var applicant = data.FindEmployee(request.ApplicantId);
        if (applicant is null || applicant.Status != "ACTIVE") return ServiceResult<ProcessSimulationView>.Failure("试算申请人不存在或已停用。", "VALIDATION_001");
        if (!Applies(definition, applicant, request.Category)) return ServiceResult<ProcessSimulationView>.Failure("该流程不适用于当前申请人或业务类别。", "FLOW_001");
        var resolved = ProcessRouting.Resolve(definition, data, applicant, request.Metric);
        if (!resolved.IsSuccess) return ServiceResult<ProcessSimulationView>.Failure(resolved.Error!, resolved.Code!);
        var delegated = ApplyDelegations(resolved.Value!, definition.BusinessType);
        if (!delegated.IsSuccess) return ServiceResult<ProcessSimulationView>.Failure(delegated.Error!, delegated.Code!);
        var value = delegated.Value!;
        return ServiceResult<ProcessSimulationView>.Success(new ProcessSimulationView(value.DefinitionId, value.Code, value.Version, value.Approvers, value.RoutingNotes ?? []));
    }

    private ServiceResult<bool> Validate(string name, string businessType, IReadOnlyList<ProcessRouteInput> routes, int priority, IReadOnlyList<string>? departmentIds, IReadOnlyList<string>? leaveTypes)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return ServiceResult<bool>.Failure("流程名称应为 1–100 个字符。");
        if (businessType is not ("Leave" or "Expense" or "Travel" or "Purchase" or "Seal")) return ServiceResult<bool>.Failure("业务类型仅支持 Leave、Expense、Travel、Purchase 或 Seal。");
        if (priority is < 0 or > 1000) return ServiceResult<bool>.Failure("流程优先级应为 0–1000。", "VALIDATION_001");
        var departments = (departmentIds ?? []).Distinct().ToList();
        if (departments.Any(id => data.Departments.All(item => item.Id != id))) return ServiceResult<bool>.Failure("适用部门不存在。", "VALIDATION_001");
        var categories = (leaveTypes ?? []).Distinct().ToList();
        if (businessType is "Expense" or "Travel" or "Purchase" or "Seal" && categories.Count > 0) return ServiceResult<bool>.Failure("非请假流程不能设置请假类型条件。", "VALIDATION_001");
        if (categories.Any(value => !Enum.TryParse<LeaveType>(value, true, out _))) return ServiceResult<bool>.Failure("适用假别包含无效值。", "VALIDATION_001");
        if (routes.Count == 0 || routes[^1].MaxValue is not null || routes.Take(routes.Count - 1).Any(route => route.MaxValue is null or <= 0))
            return ServiceResult<bool>.Failure("流程至少包含一条规则，且仅最后一条可作为无上限规则。");
        var limited = routes.Where(route => route.MaxValue is not null).Select(route => route.MaxValue!.Value).ToList();
        if (!limited.SequenceEqual(limited.OrderBy(value => value).Distinct())) return ServiceResult<bool>.Failure("条件上限必须严格递增且不能重复。");
        if (routes.Any(route => route.ApproverKeys.Count == 0 || route.ApproverKeys.Any(key => !IsValidApproverKey(key))))
            return ServiceResult<bool>.Failure("每条规则都需要有效审批人：直属上级、角色或指定用户。");
        foreach (var route in routes)
        {
            var policies = route.NodePolicies;
            if (policies is not null && policies.Count != route.ApproverKeys.Count)
                return ServiceResult<bool>.Failure("节点时效配置数量必须与审批节点数量一致。", "VALIDATION_001");
            if ((policies ?? []).Any(policy => policy.HandlingHours is < 1 or > 2_160 || policy.ReminderBeforeHours is < 0 or > 720 || policy.ReminderBeforeHours >= policy.HandlingHours || policy.EscalateAfterHours is < 0 or > 2_160))
                return ServiceResult<bool>.Failure("节点办理时限应为 1–2160 小时，提前提醒必须小于办理时限，逾期升级应为 0–2160 小时。", "VALIDATION_001");
            if ((policies ?? []).Any(policy => !IsValidEscalationTarget(policy.EscalationTarget) || !MissingAssigneeActions.All.Contains(policy.MissingAssigneeAction)))
                return ServiceResult<bool>.Failure("节点升级对象或审批人缺失处理方式无效。", "VALIDATION_001");
        }
        return ServiceResult<bool>.Success(true);
    }

    private bool IsValidApproverKey(string key)
    {
        if (key == "DIRECT_MANAGER") return true;
        if (key.StartsWith("ROLE:", StringComparison.Ordinal)) return data.ActiveEmployees.Any(item => data.HasRole(item, key[5..]));
        if (key.StartsWith("USER:", StringComparison.Ordinal)) return data.ActiveEmployees.Any(item => item.Id == key[5..]);
        return false;
    }

    private bool IsValidEscalationTarget(string key) => key is "DIRECT_MANAGER" or "PROCESS_ADMIN" || IsValidApproverKey(key);

    private void EnsureDefaults()
    {
        foreach (var source in ProcessDefaults.All.Where(source => !db.ProcessDefinitions.Any(item => item.TenantId == TenantId && item.Code == source.Code)))
        {
            var definition = new ProcessDefinitionRecord { Id = source.Id, TenantId = TenantId, Code = source.Code, Name = source.Name, BusinessType = source.BusinessType, Version = source.Version, Status = (int)source.Status, Priority = source.Priority, CreatedBy = source.CreatedBy, CreatedAt = DateTimeOffset.UtcNow, PublishedBy = source.PublishedBy, PublishedAt = DateTimeOffset.UtcNow };
            SaveAggregate(definition, source.Routes.Select(ToInput).ToList(), source.DepartmentIds, source.LeaveTypes);
        }
    }

    private IReadOnlyList<ProcessDefinitionView> LoadAll() => db.ProcessDefinitions.AsNoTracking().Where(item => item.TenantId == TenantId)
        .OrderBy(item => item.BusinessType).ThenByDescending(item => item.Version).Select(item => item.Id).ToList().Select(id => Load(id)!).ToList();

    private ProcessDefinitionView? Load(Guid id)
    {
        var definition = db.ProcessDefinitions.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (definition is null) return null;
        var rules = db.ProcessRules.AsNoTracking().Where(item => item.ProcessDefinitionId == id).OrderBy(item => item.Sequence).ToList();
        var ruleIds = rules.Select(item => item.Id).ToList();
        var nodes = db.ProcessNodes.AsNoTracking().Where(item => ruleIds.Contains(item.ProcessRuleId)).OrderBy(item => item.Sequence).ToLookup(item => item.ProcessRuleId);
        var scopes = db.ProcessScopes.AsNoTracking().Where(item => item.ProcessDefinitionId == id).ToList();
        return new ProcessDefinitionView(definition.Id, definition.Code, definition.Name, definition.BusinessType, definition.Version, (ProcessDefinitionStatus)definition.Status,
            rules.Select(rule => new ProcessRouteView(rule.Id, rule.Sequence, rule.MaxValue, nodes[rule.Id].Select(node => node.AssigneeKey).ToList(),
                nodes[rule.Id].Select(node => new ProcessNodePolicyView(node.Id, node.Sequence, node.AssigneeKey, node.HandlingHours, node.ReminderBeforeHours, node.EscalateAfterHours, node.EscalationTarget, node.MissingAssigneeAction, node.AllowAutoSkip)).ToList())).ToList(),
            definition.CreatedBy, definition.CreatedAt, definition.PublishedBy, definition.PublishedAt, definition.Priority,
            scopes.Where(item => item.ScopeType == "Department").Select(item => item.Value).OrderBy(value => value).ToList(),
            scopes.Where(item => item.ScopeType == "LeaveType").Select(item => item.Value).OrderBy(value => value).ToList());
    }

    private void SaveAggregate(ProcessDefinitionRecord definition, IReadOnlyList<ProcessRouteInput> routes, IReadOnlyList<string>? departmentIds, IReadOnlyList<string>? leaveTypes)
    {
        db.ProcessDefinitions.Add(definition);
        AddRoutes(definition, routes);
        AddScopes(definition, departmentIds, leaveTypes);
        db.SaveChanges();
    }

    private void ReplaceAggregate(ProcessDefinitionRecord definition, IReadOnlyList<ProcessRouteInput> routes, IReadOnlyList<string>? departmentIds, IReadOnlyList<string>? leaveTypes)
    {
        var ruleIds = db.ProcessRules.Where(item => item.ProcessDefinitionId == definition.Id).Select(item => item.Id).ToList();
        db.ProcessNodes.Where(item => ruleIds.Contains(item.ProcessRuleId)).ExecuteDelete();
        db.ProcessRules.Where(item => item.ProcessDefinitionId == definition.Id).ExecuteDelete();
        db.ProcessScopes.Where(item => item.ProcessDefinitionId == definition.Id).ExecuteDelete();
        AddRoutes(definition, routes);
        AddScopes(definition, departmentIds, leaveTypes);
        db.SaveChanges();
    }

    private void AddRoutes(ProcessDefinitionRecord definition, IReadOnlyList<ProcessRouteInput> routes)
    {
        foreach (var (route, routeIndex) in routes.Select((value, index) => (value, index)))
        {
            var rule = new ProcessRuleRecord { ProcessDefinitionId = definition.Id, Sequence = routeIndex + 1, MaxValue = route.MaxValue };
            db.ProcessRules.Add(rule);
            db.ProcessNodes.AddRange(route.ApproverKeys.Select((key, nodeIndex) =>
            {
                var policy = route.NodePolicies is { Count: > 0 } ? route.NodePolicies[nodeIndex] : new ProcessNodePolicyInput();
                return new ProcessNodeRecord
                {
                    ProcessRuleId = rule.Id,
                    Sequence = nodeIndex + 1,
                    AssigneeKey = key,
                    HandlingHours = policy.HandlingHours,
                    ReminderBeforeHours = policy.ReminderBeforeHours,
                    EscalateAfterHours = policy.EscalateAfterHours,
                    EscalationTarget = policy.EscalationTarget.Trim().ToUpperInvariant(),
                    MissingAssigneeAction = policy.MissingAssigneeAction.Trim().ToUpperInvariant(),
                    AllowAutoSkip = policy.AllowAutoSkip
                };
            }));
        }
    }

    private void AddScopes(ProcessDefinitionRecord definition, IReadOnlyList<string>? departmentIds, IReadOnlyList<string>? leaveTypes)
    {
        db.ProcessScopes.AddRange((departmentIds ?? []).Distinct().Select(value => new ProcessScopeRecord { ProcessDefinitionId = definition.Id, ScopeType = "Department", Value = value }));
        db.ProcessScopes.AddRange((leaveTypes ?? []).Distinct().Select(value => new ProcessScopeRecord { ProcessDefinitionId = definition.Id, ScopeType = "LeaveType", Value = value }));
    }

    private bool Applies(ProcessDefinitionView definition, Employee applicant, string? category)
    {
        var departments = definition.DepartmentIds ?? [];
        if (departments.Count > 0 && departments.All(scope => !data.IsDepartmentWithin(applicant.DepartmentId, scope))) return false;
        var leaveTypes = definition.LeaveTypes ?? [];
        return leaveTypes.Count == 0 || category is not null && leaveTypes.Contains(category, StringComparer.OrdinalIgnoreCase);
    }

    private static int Specificity(ProcessDefinitionView definition) =>
        ((definition.DepartmentIds?.Count ?? 0) > 0 ? 1 : 0) + ((definition.LeaveTypes?.Count ?? 0) > 0 ? 1 : 0);

    private ProcessDefinitionView? FindPublishConflict(ProcessDefinitionView candidate)
    {
        return LoadAll().Where(item => item.Status == ProcessDefinitionStatus.Published && item.BusinessType == candidate.BusinessType && item.Code != candidate.Code && item.Priority == candidate.Priority && Specificity(item) == Specificity(candidate))
            .FirstOrDefault(item => DepartmentScopesOverlap(item.DepartmentIds ?? [], candidate.DepartmentIds ?? []) && ValueScopesOverlap(item.LeaveTypes ?? [], candidate.LeaveTypes ?? []));
    }

    private bool DepartmentScopesOverlap(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count == 0 || second.Count == 0) return true;
        return first.Any(left => second.Any(right => data.IsDepartmentWithin(left, right) || data.IsDepartmentWithin(right, left)));
    }

    private static bool ValueScopesOverlap(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        first.Count == 0 || second.Count == 0 || first.Intersect(second, StringComparer.OrdinalIgnoreCase).Any();

    private static ProcessRouteInput ToInput(ProcessRouteView route) => new(route.MaxValue, route.ApproverKeys,
        route.Nodes?.Select(node => new ProcessNodePolicyInput(node.HandlingHours, node.ReminderBeforeHours, node.EscalateAfterHours, node.EscalationTarget, node.MissingAssigneeAction, node.AllowAutoSkip)).ToList());

    private void Audit(Employee actor, string action, ProcessDefinitionRecord definition, string summary)
    {
        db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "ProcessDefinition", ResourceId = definition.Id.ToString(), Summary = summary });
        db.SaveChanges();
    }

    private bool IsAdministrator(Employee actor) => data.HasPermission(actor, OaPermissions.ProcessManage);

    private ServiceResult<ResolvedProcess> ApplyDelegations(ResolvedProcess process, string businessType)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerIds = process.Approvers.Select(item => item.OriginalApprover.Id).ToList();
        var delegations = db.FlowDelegations.AsNoTracking()
            .Where(item => item.TenantId == TenantId && ownerIds.Contains(item.OwnerId) && item.Status == (int)FlowDelegationStatus.Active &&
                item.StartAt <= now && now < item.EndAt && (item.BusinessType == "All" || item.BusinessType == businessType))
            .OrderByDescending(item => item.BusinessType == businessType)
            .ThenByDescending(item => item.CreatedAt)
            .ToList()
            .GroupBy(item => item.OwnerId)
            .ToDictionary(group => group.Key, group => group.First());
        var effective = new List<ResolvedApprover>();
        var notes = (process.RoutingNotes ?? []).ToList();
        foreach (var node in process.Approvers)
        {
            var original = node.OriginalApprover;
            var delegation = delegations.GetValueOrDefault(original.Id);
            var assignee = delegation is null ? original : data.FindEmployee(delegation.DelegateId);
            if (assignee is null || assignee.Status != "ACTIVE")
                return ServiceResult<ResolvedProcess>.Failure($"审批人 {original.Name} 的代办人无法解析。", "FLOW_001");
            if (effective.LastOrDefault()?.Assignee.Id != assignee.Id)
                effective.Add(new ResolvedApprover(assignee, original, delegation?.Id, node.Policy));
            else if ((node.Policy ?? ProcessNodePolicy.Default).AllowAutoSkip)
                notes.Add($"代办后连续节点均命中 {assignee.Name}，已按节点配置自动跳过。");
            else
                return ServiceResult<ResolvedProcess>.Failure($"代办后连续节点均命中 {assignee.Name}，且未允许自动跳过。", "FLOW_001");
        }
        return ServiceResult<ResolvedProcess>.Success(process with { Approvers = effective, RoutingNotes = notes });
    }
}

public static class ProcessRouting
{
    public static ServiceResult<ResolvedProcess> Resolve(ProcessDefinitionView definition, DemoData data, Employee applicant, decimal metric)
    {
        var route = definition.Routes.OrderBy(item => item.Sequence).FirstOrDefault(item => item.MaxValue is null || metric <= item.MaxValue);
        if (route is null) return ServiceResult<ResolvedProcess>.Failure("没有匹配当前业务数据的流程规则。", "FLOW_001");
        var approvers = new List<ResolvedApprover>();
        var notes = new List<string>();
        var nodes = route.Nodes is { Count: > 0 }
            ? route.Nodes
            : route.ApproverKeys.Select((key, index) => new ProcessNodePolicyView(Guid.Empty, index + 1, key, 24, 4, 24, "DIRECT_MANAGER", MissingAssigneeActions.Block, false)).ToList();
        foreach (var node in nodes.OrderBy(item => item.Sequence))
        {
            var key = node.ApproverKey;
            Employee? approver = key switch
            {
                "DIRECT_MANAGER" => applicant.ManagerId is null ? null : data.FindEmployee(applicant.ManagerId),
                _ when key.StartsWith("ROLE:", StringComparison.Ordinal) => data.ActiveEmployees.FirstOrDefault(item => data.HasRole(item, key[5..])),
                _ when key.StartsWith("USER:", StringComparison.Ordinal) => data.FindEmployee(key[5..]),
                _ => null
            };
            if (approver is null || approver.Status != "ACTIVE")
            {
                if (node.MissingAssigneeAction == MissingAssigneeActions.Skip)
                {
                    notes.Add($"审批人规则 {key} 无法解析，已按配置跳过节点。");
                    continue;
                }
                if (node.MissingAssigneeAction == MissingAssigneeActions.ProcessAdministrator)
                {
                    approver = data.ActiveEmployees.FirstOrDefault(item => data.HasPermission(item, OaPermissions.ProcessManage));
                    if (approver is not null) notes.Add($"审批人规则 {key} 无法解析，已转流程管理员 {approver.Name}。");
                }
            }
            if (approver is null || approver.Status != "ACTIVE") return ServiceResult<ResolvedProcess>.Failure($"无法解析审批人规则 {key}，且未找到可用的异常路由。", "FLOW_001");
            if (approvers.LastOrDefault()?.Assignee.Id != approver.Id)
                approvers.Add(new ResolvedApprover(approver, approver, null, new ProcessNodePolicy(node.HandlingHours, node.ReminderBeforeHours, node.EscalateAfterHours, node.EscalationTarget, node.AllowAutoSkip)));
            else if (node.AllowAutoSkip)
                notes.Add($"连续节点均命中 {approver.Name}，已按节点配置自动跳过。");
            else
                return ServiceResult<ResolvedProcess>.Failure($"连续节点均命中 {approver.Name}，且未允许自动跳过。", "FLOW_001");
        }
        if (approvers.Count == 0) return ServiceResult<ResolvedProcess>.Failure("流程未解析到有效审批人。", "FLOW_001");
        return ServiceResult<ResolvedProcess>.Success(new ResolvedProcess(definition.Id, definition.Code, definition.Version, approvers, notes));
    }
}
