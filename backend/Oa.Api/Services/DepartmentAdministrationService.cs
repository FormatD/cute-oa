using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class DepartmentAdministrationService(OaDbContext db, DemoData data)
{
    private const string TenantId = IdentityDefaults.TenantId;
    private const int MaxDepth = 8;

    public ServiceResult<IReadOnlyList<DepartmentView>> List(Employee actor)
    {
        if (!CanManage(actor)) return ServiceResult<IReadOnlyList<DepartmentView>>.Failure("无组织架构维护权限。", "AUTH_002");
        return ServiceResult<IReadOnlyList<DepartmentView>>.Success(BuildViews());
    }

    public ServiceResult<DepartmentView> Create(Employee actor, CreateDepartmentRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<DepartmentView>.Failure("无组织架构维护权限。", "AUTH_002");
        var id = request.Id?.Trim().ToLowerInvariant() ?? string.Empty;
        if (id.Length is < 2 or > 64 || id.Any(character => !char.IsLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            return ServiceResult<DepartmentView>.Failure("部门编码应为 2–64 位字母、数字、点、横线或下划线。", "VALIDATION_001");
        if (db.Departments.Any(item => item.Id == id)) return ServiceResult<DepartmentView>.Failure("部门编码已存在。", "DUPLICATE_001");
        var validation = ValidateCommon(id, request.Name, request.ParentId, request.SortOrder, true);
        if (!validation.IsSuccess) return ServiceResult<DepartmentView>.Failure(validation.Error!, validation.Code!);

        var record = new DepartmentRecord { Id = id, TenantId = TenantId, Name = request.Name.Trim(), ParentId = request.ParentId.Trim(), SortOrder = request.SortOrder, IsSystem = false };
        db.Departments.Add(record);
        db.SaveChanges();
        Audit(actor, "DEPARTMENT_CREATED", record.Id, $"创建部门 {record.Name}（{record.Id}）");
        return ServiceResult<DepartmentView>.Success(BuildViews().Single(item => item.Id == record.Id));
    }

    public ServiceResult<DepartmentView> Update(Employee actor, string id, UpdateDepartmentRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<DepartmentView>.Failure("无组织架构维护权限。", "AUTH_002");
        var record = db.Departments.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<DepartmentView>.Failure("部门不存在。", "DATA_001");
        if (record.Version != request.Version) return ServiceResult<DepartmentView>.Failure("部门已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        var normalizedParentId = string.IsNullOrWhiteSpace(request.ParentId) ? null : request.ParentId.Trim();
        if (record.ParentId is null && normalizedParentId is not null) return ServiceResult<DepartmentView>.Failure("公司根节点不能移动到其他部门下。", "STATE_001");
        if (record.ParentId is not null && normalizedParentId is null) return ServiceResult<DepartmentView>.Failure("非根部门必须选择上级部门。", "VALIDATION_001");
        var validation = ValidateCommon(id, request.Name, normalizedParentId, request.SortOrder, false);
        if (!validation.IsSuccess) return ServiceResult<DepartmentView>.Failure(validation.Error!, validation.Code!);

        var all = db.Departments.AsNoTracking().Where(item => item.TenantId == TenantId).ToList();
        if (normalizedParentId is not null && IsDescendant(normalizedParentId, id, all)) return ServiceResult<DepartmentView>.Failure("不能将部门移动到自身或下级部门。", "VALIDATION_001");
        if (normalizedParentId is not null)
        {
            var resultingDepth = DepthOf(normalizedParentId, all) + 1 + SubtreeHeight(id, all);
            if (resultingDepth >= MaxDepth) return ServiceResult<DepartmentView>.Failure($"组织层级最多支持 {MaxDepth} 层。", "VALIDATION_001");
        }

        var hierarchyChanged = record.ParentId != normalizedParentId;
        record.Name = request.Name.Trim();
        record.ParentId = normalizedParentId;
        record.SortOrder = request.SortOrder;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
        if (hierarchyChanged) RevokeTenantSessions("DEPARTMENT_HIERARCHY_CHANGED");
        Audit(actor, "DEPARTMENT_UPDATED", record.Id, $"更新部门 {record.Name}（{record.Id}）");
        return ServiceResult<DepartmentView>.Success(BuildViews().Single(item => item.Id == record.Id));
    }

    public ServiceResult<DepartmentView> Delete(Employee actor, string id, int version)
    {
        if (!CanManage(actor)) return ServiceResult<DepartmentView>.Failure("无组织架构维护权限。", "AUTH_002");
        var record = db.Departments.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<DepartmentView>.Failure("部门不存在。", "DATA_001");
        if (record.Version != version) return ServiceResult<DepartmentView>.Failure("部门已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        if (record.IsSystem) return ServiceResult<DepartmentView>.Failure("系统预置部门不可删除。", "STATE_001");
        if (db.Departments.Any(item => item.ParentId == id)) return ServiceResult<DepartmentView>.Failure("部门存在下级部门，不能删除。", "CONFLICT_001");
        if (db.Users.Any(item => item.DepartmentId == id)) return ServiceResult<DepartmentView>.Failure("部门存在用户，不能删除。", "CONFLICT_001");
        var view = BuildViews().Single(item => item.Id == id);
        db.Departments.Remove(record);
        db.SaveChanges();
        Audit(actor, "DEPARTMENT_DELETED", record.Id, $"删除部门 {record.Name}（{record.Id}）");
        return ServiceResult<DepartmentView>.Success(view);
    }

    private ServiceResult<bool> ValidateCommon(string id, string name, string? parentId, int sortOrder, bool creating)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) return ServiceResult<bool>.Failure("部门名称应为 1–100 个字符。", "VALIDATION_001");
        if (db.Departments.Any(item => item.TenantId == TenantId && item.Id != id && item.Name == name.Trim())) return ServiceResult<bool>.Failure("部门名称已存在。", "DUPLICATE_001");
        if (sortOrder is < 0 or > 9999) return ServiceResult<bool>.Failure("排序值应为 0–9999。", "VALIDATION_001");
        if (creating && string.IsNullOrWhiteSpace(parentId)) return ServiceResult<bool>.Failure("新部门必须选择上级部门。", "VALIDATION_001");
        if (!string.IsNullOrWhiteSpace(parentId) && !db.Departments.Any(item => item.TenantId == TenantId && item.Id == parentId)) return ServiceResult<bool>.Failure("上级部门不存在。", "VALIDATION_001");
        if (parentId == id) return ServiceResult<bool>.Failure("部门不能作为自己的上级。", "VALIDATION_001");
        if (creating && parentId is not null && DepthOf(parentId, db.Departments.AsNoTracking().Where(item => item.TenantId == TenantId).ToList()) + 1 >= MaxDepth) return ServiceResult<bool>.Failure($"组织层级最多支持 {MaxDepth} 层。", "VALIDATION_001");
        return ServiceResult<bool>.Success(true);
    }

    private IReadOnlyList<DepartmentView> BuildViews()
    {
        var records = db.Departments.AsNoTracking().Where(item => item.TenantId == TenantId).ToList();
        var userCounts = db.Users.AsNoTracking().Where(item => item.TenantId == TenantId).GroupBy(item => item.DepartmentId).ToDictionary(group => group.Key, group => group.Count());
        var children = records.ToLookup(item => item.ParentId);
        var result = new List<DepartmentView>();
        var visited = new HashSet<string>();
        void Visit(DepartmentRecord record, int depth)
        {
            if (!visited.Add(record.Id)) return;
            result.Add(new DepartmentView(record.Id, record.Name, record.ParentId, record.ParentId is null ? null : records.SingleOrDefault(item => item.Id == record.ParentId)?.Name, depth, record.SortOrder, record.IsSystem, record.Version, userCounts.GetValueOrDefault(record.Id), children[record.Id].Count(), record.CreatedAt, record.UpdatedAt));
            foreach (var child in children[record.Id].OrderBy(item => item.SortOrder).ThenBy(item => item.Name)) Visit(child, depth + 1);
        }
        foreach (var root in children[null].OrderBy(item => item.SortOrder).ThenBy(item => item.Name)) Visit(root, 0);
        foreach (var orphan in records.Where(item => !visited.Contains(item.Id)).OrderBy(item => item.Name)) Visit(orphan, 0);
        return result;
    }

    private static int DepthOf(string id, IReadOnlyList<DepartmentRecord> records)
    {
        var depth = 0;
        var cursor = records.SingleOrDefault(item => item.Id == id);
        var visited = new HashSet<string>();
        while (cursor?.ParentId is not null && visited.Add(cursor.Id)) { depth++; cursor = records.SingleOrDefault(item => item.Id == cursor.ParentId); }
        return depth;
    }

    private static int SubtreeHeight(string id, IReadOnlyList<DepartmentRecord> records)
    {
        var children = records.Where(item => item.ParentId == id).ToList();
        return children.Count == 0 ? 0 : 1 + children.Max(item => SubtreeHeight(item.Id, records));
    }

    private static bool IsDescendant(string candidateId, string departmentId, IReadOnlyList<DepartmentRecord> records)
    {
        string? cursor = candidateId;
        var visited = new HashSet<string>();
        while (cursor is not null && visited.Add(cursor))
        {
            if (cursor == departmentId) return true;
            cursor = records.SingleOrDefault(item => item.Id == cursor)?.ParentId;
        }
        return false;
    }

    private void RevokeTenantSessions(string reason)
    {
        var userIds = db.Users.AsNoTracking().Where(item => item.TenantId == TenantId).Select(item => item.Id).ToList();
        var now = DateTimeOffset.UtcNow;
        db.AuthSessions.Where(item => userIds.Contains(item.UserId) && item.RevokedAt == null)
            .ExecuteUpdate(setters => setters.SetProperty(item => item.RevokedAt, now).SetProperty(item => item.RevokedReason, reason));
    }

    private bool CanManage(Employee actor) => data.HasPermission(actor, OaPermissions.OrgManage);
    private void Audit(Employee actor, string action, string resourceId, string summary)
    {
        db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "Department", ResourceId = resourceId, Summary = summary });
        db.SaveChanges();
    }
}
