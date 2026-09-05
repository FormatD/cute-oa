using Oa.Api.Services;

namespace Oa.Api.Domain;

public enum ProcessDefinitionStatus { Draft, Published, Archived }

public static class MissingAssigneeActions
{
    public const string Block = "BLOCK";
    public const string Skip = "SKIP";
    public const string ProcessAdministrator = "PROCESS_ADMIN";
    public static IReadOnlyList<string> All { get; } = [Block, Skip, ProcessAdministrator];
}

public sealed record ProcessNodePolicyInput(
    int HandlingHours = 24,
    int ReminderBeforeHours = 4,
    int EscalateAfterHours = 24,
    string EscalationTarget = "DIRECT_MANAGER",
    string MissingAssigneeAction = MissingAssigneeActions.Block,
    bool AllowAutoSkip = false);
public sealed record ProcessRouteInput(decimal? MaxValue, IReadOnlyList<string> ApproverKeys, IReadOnlyList<ProcessNodePolicyInput>? NodePolicies = null);
public sealed record CreateProcessDefinitionRequest(string Code, string Name, string BusinessType, IReadOnlyList<ProcessRouteInput> Routes, int Priority = 0, IReadOnlyList<string>? DepartmentIds = null, IReadOnlyList<string>? LeaveTypes = null);
public sealed record UpdateProcessDefinitionRequest(string Name, IReadOnlyList<ProcessRouteInput> Routes, int Priority = 0, IReadOnlyList<string>? DepartmentIds = null, IReadOnlyList<string>? LeaveTypes = null);
public sealed record ProcessNodePolicyView(
    Guid Id,
    int Sequence,
    string ApproverKey,
    int HandlingHours,
    int ReminderBeforeHours,
    int EscalateAfterHours,
    string EscalationTarget,
    string MissingAssigneeAction,
    bool AllowAutoSkip);
public sealed record ProcessRouteView(Guid Id, int Sequence, decimal? MaxValue, IReadOnlyList<string> ApproverKeys, IReadOnlyList<ProcessNodePolicyView>? Nodes = null);
public sealed record ProcessDefinitionView(
    Guid Id,
    string Code,
    string Name,
    string BusinessType,
    int Version,
    ProcessDefinitionStatus Status,
    IReadOnlyList<ProcessRouteView> Routes,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string? PublishedBy,
    DateTimeOffset? PublishedAt,
    int Priority = 0,
    IReadOnlyList<string>? DepartmentIds = null,
    IReadOnlyList<string>? LeaveTypes = null);

public sealed record ProcessNodePolicy(int HandlingHours, int ReminderBeforeHours, int EscalateAfterHours, string EscalationTarget, bool AllowAutoSkip)
{
    public static ProcessNodePolicy Default { get; } = new(24, 4, 24, "DIRECT_MANAGER", false);
}
public sealed record ResolvedApprover(Employee Assignee, Employee OriginalApprover, Guid? DelegationId, ProcessNodePolicy? Policy = null);
public sealed record ResolvedProcess(Guid DefinitionId, string Code, int Version, IReadOnlyList<ResolvedApprover> Approvers, IReadOnlyList<string>? RoutingNotes = null);
public sealed record SimulateProcessRequest(string ApplicantId, decimal Metric, string? Category = null);
public sealed record ProcessSimulationView(Guid DefinitionId, string Code, int Version, IReadOnlyList<ResolvedApprover> Approvers, IReadOnlyList<string> RoutingNotes);
public sealed record ResolvedFlowTask(Guid TaskId, int Sequence, ResolvedApprover Approver);

public interface IProcessRouter
{
    ServiceResult<ResolvedProcess> Resolve(string businessType, Employee applicant, decimal metric, string? category = null);
}
