using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class PositionAdministrationService(OaDbContext db, DemoData data)
{
    private const string TenantId = IdentityDefaults.TenantId;

    public IReadOnlyList<PositionOption> ListOptions() => db.Positions.AsNoTracking()
        .Where(item => item.TenantId == TenantId && item.Status == "ACTIVE")
        .OrderBy(item => item.DepartmentId).ThenBy(item => item.SortOrder).ThenBy(item => item.Name)
        .Select(item => new PositionOption(item.Id, item.Name, item.DepartmentId, db.Departments.Where(department => department.Id == item.DepartmentId).Select(department => department.Name).Single()))
        .ToList();

    public ServiceResult<IReadOnlyList<PositionView>> List(Employee actor)
    {
        if (!CanManage(actor)) return ServiceResult<IReadOnlyList<PositionView>>.Failure("无岗位维护权限。", "AUTH_002");
        return ServiceResult<IReadOnlyList<PositionView>>.Success(BuildViews());
    }

    public ServiceResult<PositionView> Create(Employee actor, CreatePositionRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<PositionView>.Failure("无岗位维护权限。", "AUTH_002");
        var id = request.Id?.Trim().ToLowerInvariant() ?? string.Empty;
        if (id.Length is < 2 or > 64 || id.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            return ServiceResult<PositionView>.Failure("岗位编码应为 2–64 位字母、数字、点、横线或下划线。", "VALIDATION_001");
        if (db.Positions.Any(item => item.Id == id)) return ServiceResult<PositionView>.Failure("岗位编码已存在。", "DUPLICATE_001");
        var validation = Validate(id, request.Name, request.DepartmentId, request.SortOrder, "ACTIVE");
        if (!validation.IsSuccess) return ServiceResult<PositionView>.Failure(validation.Error!, validation.Code!);
        var position = new PositionRecord { Id = id, TenantId = TenantId, Name = request.Name.Trim(), DepartmentId = request.DepartmentId.Trim(), SortOrder = request.SortOrder, Status = "ACTIVE", IsSystem = false };
        db.Positions.Add(position);
        db.SaveChanges();
        Audit(actor, "POSITION_CREATED", position.Id, $"创建岗位 {position.Name}（{position.Id}）");
        return ServiceResult<PositionView>.Success(BuildViews().Single(item => item.Id == position.Id));
    }

    public ServiceResult<PositionView> Update(Employee actor, string id, UpdatePositionRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<PositionView>.Failure("无岗位维护权限。", "AUTH_002");
        var position = db.Positions.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (position is null) return ServiceResult<PositionView>.Failure("岗位不存在。", "DATA_001");
        if (position.Version != request.Version) return ServiceResult<PositionView>.Failure("岗位已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        var validation = Validate(id, request.Name, request.DepartmentId, request.SortOrder, request.Status);
        if (!validation.IsSuccess) return ServiceResult<PositionView>.Failure(validation.Error!, validation.Code!);
        var assigned = db.Users.Any(item => item.PositionId == id);
        if (assigned && (position.DepartmentId != request.DepartmentId || request.Status != "ACTIVE"))
            return ServiceResult<PositionView>.Failure("岗位已分配给用户，不能变更所属部门或停用。", "CONFLICT_001");
        position.Name = request.Name.Trim();
        position.DepartmentId = request.DepartmentId.Trim();
        position.SortOrder = request.SortOrder;
        position.Status = request.Status;
        position.Version++;
        position.UpdatedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
        Audit(actor, "POSITION_UPDATED", position.Id, $"更新岗位 {position.Name}（{position.Id}）");
        return ServiceResult<PositionView>.Success(BuildViews().Single(item => item.Id == position.Id));
    }

    public ServiceResult<PositionView> Delete(Employee actor, string id, int version)
    {
        if (!CanManage(actor)) return ServiceResult<PositionView>.Failure("无岗位维护权限。", "AUTH_002");
        var position = db.Positions.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (position is null) return ServiceResult<PositionView>.Failure("岗位不存在。", "DATA_001");
        if (position.Version != version) return ServiceResult<PositionView>.Failure("岗位已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        if (position.IsSystem) return ServiceResult<PositionView>.Failure("系统预置岗位不可删除。", "STATE_001");
        if (db.Users.Any(item => item.PositionId == id)) return ServiceResult<PositionView>.Failure("岗位已分配给用户，不能删除。", "CONFLICT_001");
        var view = BuildViews().Single(item => item.Id == id);
        db.Positions.Remove(position);
        db.SaveChanges();
        Audit(actor, "POSITION_DELETED", position.Id, $"删除岗位 {position.Name}（{position.Id}）");
        return ServiceResult<PositionView>.Success(view);
    }

    private ServiceResult<bool> Validate(string id, string name, string departmentId, int sortOrder, string status)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return ServiceResult<bool>.Failure("岗位名称应为 1–100 个字符。", "VALIDATION_001");
        if (string.IsNullOrWhiteSpace(departmentId) || !db.Departments.Any(item => item.TenantId == TenantId && item.Id == departmentId.Trim())) return ServiceResult<bool>.Failure("所属部门不存在。", "VALIDATION_001");
        if (db.Positions.Any(item => item.TenantId == TenantId && item.Id != id && item.DepartmentId == departmentId.Trim() && item.Name == name.Trim())) return ServiceResult<bool>.Failure("该部门已存在同名岗位。", "DUPLICATE_001");
        if (sortOrder is < 0 or > 9999) return ServiceResult<bool>.Failure("排序值应为 0–9999。", "VALIDATION_001");
        if (status is not ("ACTIVE" or "DISABLED")) return ServiceResult<bool>.Failure("岗位状态仅支持 ACTIVE 或 DISABLED。", "VALIDATION_001");
        return ServiceResult<bool>.Success(true);
    }

    private IReadOnlyList<PositionView> BuildViews()
    {
        var departmentNames = db.Departments.AsNoTracking().Where(item => item.TenantId == TenantId).ToDictionary(item => item.Id, item => item.Name);
        var userCounts = db.Users.AsNoTracking().Where(item => item.PositionId != null).GroupBy(item => item.PositionId!).ToDictionary(group => group.Key, group => group.Count());
        return db.Positions.AsNoTracking().Where(item => item.TenantId == TenantId).OrderBy(item => item.DepartmentId).ThenBy(item => item.SortOrder).ThenBy(item => item.Name).ToList()
            .Select(item => new PositionView(item.Id, item.Name, item.DepartmentId, departmentNames.GetValueOrDefault(item.DepartmentId, item.DepartmentId), item.SortOrder, item.Status, item.IsSystem, item.Version, userCounts.GetValueOrDefault(item.Id), item.CreatedAt, item.UpdatedAt)).ToList();
    }

    private bool CanManage(Employee actor) => data.HasPermission(actor, OaPermissions.OrgManage);
    private void Audit(Employee actor, string action, string resourceId, string summary)
    {
        db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "Position", ResourceId = resourceId, Summary = summary });
        db.SaveChanges();
    }
}
