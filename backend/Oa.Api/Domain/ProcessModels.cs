using Oa.Api.Services;

namespace Oa.Api.Domain;

public enum ProcessDefinitionStatus { Draft, Published, Archived }

public sealed record ProcessRouteInput(decimal? MaxValue, IReadOnlyList<string> ApproverKeys);
public sealed record CreateProcessDefinitionRequest(string Code, string Name, string BusinessType, IReadOnlyList<ProcessRouteInput> Routes, int Priority = 0, IReadOnlyList<string>? DepartmentIds = null, IReadOnlyList<string>? LeaveTypes = null);
public sealed record UpdateProcessDefinitionRequest(string Name, IReadOnlyList<ProcessRouteInput> Routes, int Priority = 0, IReadOnlyList<string>? DepartmentIds = null, IReadOnlyList<string>? LeaveTypes = null);
public sealed record ProcessRouteView(Guid Id, int Sequence, decimal? MaxValue, IReadOnlyList<string> ApproverKeys);
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

public sealed record ResolvedApprover(Employee Assignee, Employee OriginalApprover, Guid? DelegationId);
public sealed record ResolvedProcess(Guid DefinitionId, string Code, int Version, IReadOnlyList<ResolvedApprover> Approvers);

public interface IProcessRouter
{
    ServiceResult<ResolvedProcess> Resolve(string businessType, Employee applicant, decimal metric, string? category = null);
}
