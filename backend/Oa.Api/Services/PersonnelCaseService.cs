using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class PersonnelCaseService(OaDbContext db, DemoData data, NotificationService? notifications = null, IConfiguration? configuration = null)
{
    private const string TenantId = IdentityDefaults.TenantId;
    private const string DueSoonAlert = "DUE_SOON";
    private const string OverdueAlert = "OVERDUE";
    private const string EscalatedAlert = "ESCALATED";
    private sealed record TaskDefinition(string Code, string Title, string Category, string AssigneeKind, int DueOffsetDays, bool Required = true);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CategoryRolePriorities = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["HR"] = ["HR/行政"],
        ["FINANCE"] = ["财务经理", "财务专员"],
        ["IT"] = ["系统管理员"],
        ["ADMIN"] = ["HR/行政", "系统管理员"]
    };

    private static readonly IReadOnlyDictionary<string, string> CategoryPermissionFallbacks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["HR"] = OaPermissions.PersonnelManage,
        ["FINANCE"] = OaPermissions.ExpensePay,
        ["IT"] = OaPermissions.UserManage,
        ["ADMIN"] = OaPermissions.OrgManage
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<TaskDefinition>> Templates = new Dictionary<string, IReadOnlyList<TaskDefinition>>
    {
        [PersonnelCaseTypes.Onboarding] =
        [
            new("HR_PROFILE", "核验员工档案与入职材料", "HR", "CATEGORY", -1),
            new("CONTRACT", "签署并归档劳动合同", "HR", "CATEGORY", 0),
            new("ACCOUNT", "开通账号、邮箱和业务权限", "IT", "CATEGORY", -1),
            new("EQUIPMENT", "发放办公设备与门禁", "ADMIN", "CATEGORY", 0),
            new("ORIENTATION", "完成入职培训和制度确认", "EMPLOYEE", "EMPLOYEE", 3),
            new("MANAGER_PLAN", "确认岗位目标与试用期计划", "MANAGER", "MANAGER", 3)
        ],
        [PersonnelCaseTypes.Regularization] =
        [
            new("SELF_REVIEW", "提交试用期工作总结", "EMPLOYEE", "EMPLOYEE", -3),
            new("MANAGER_REVIEW", "完成试用期考核评价", "MANAGER", "MANAGER", -1),
            new("HR_DECISION", "核验转正决定与生效日期", "HR", "CATEGORY", 0),
            new("CONTRACT_UPDATE", "核对合同及人事信息变更", "HR", "CATEGORY", 2)
        ],
        [PersonnelCaseTypes.Transfer] =
        [
            new("TRANSFER_APPROVAL", "归档调动依据和生效信息", "HR", "CATEGORY", 0),
            new("OLD_MANAGER_HANDOVER", "完成原岗位工作交接", "MANAGER", "MANAGER", 0),
            new("ACCESS_UPDATE", "调整系统权限和组织通讯录", "IT", "CATEGORY", 1),
            new("ASSET_UPDATE", "核对办公地点、设备和门禁", "ADMIN", "CATEGORY", 2),
            new("NEW_ROLE_CONFIRM", "确认新岗位职责和目标", "EMPLOYEE", "EMPLOYEE", 3)
        ],
        [PersonnelCaseTypes.Offboarding] =
        [
            new("WORK_HANDOVER", "完成工作与文档交接", "MANAGER", "MANAGER", -1),
            new("ASSET_RETURN", "归还设备、门禁和其他资产", "ADMIN", "CATEGORY", 0),
            new("FINANCE_CLEARANCE", "完成借款、报销和财务结清", "FINANCE", "CATEGORY", 0),
            new("ACCOUNT_CLOSE", "停用账号并回收业务权限", "IT", "CATEGORY", 0),
            new("CONTRACT_ARCHIVE", "归档离职文件和合同处理记录", "HR", "CATEGORY", 0),
            new("EMPLOYEE_CONFIRM", "确认个人材料和联系方式", "EMPLOYEE", "EMPLOYEE", 0, false)
        ]
    };

    public ServiceResult<PagedResponse<PersonnelCaseView>> List(Employee actor, string? keyword, string? userId, string? type, string? status, bool? assignedToMe, int? page, int? pageSize)
    {
        var normalizedType = Normalize(type);
        var normalizedStatus = Normalize(status);
        if (normalizedType is not null && !PersonnelCaseTypes.All.Contains(normalizedType) || normalizedStatus is not null && !PersonnelCaseStatuses.All.Contains(normalizedStatus))
            return ServiceResult<PagedResponse<PersonnelCaseView>>.Failure("办理类型或状态不合法。", "PERSONNEL_CASE_001");
        var cases = db.PersonnelCases.AsNoTracking().Where(item => item.TenantId == TenantId).ToList();
        var assignedCaseIds = db.PersonnelCaseTasks.AsNoTracking().Where(item => item.TenantId == TenantId && item.AssigneeId == actor.Id).Select(item => item.PersonnelCaseId).ToHashSet();
        var visible = cases.Where(item => CanView(actor, item, assignedCaseIds))
            .Where(item => string.IsNullOrWhiteSpace(userId) || item.UserId == userId)
            .Where(item => normalizedType is null || item.Type == normalizedType)
            .Where(item => normalizedStatus is null || item.Status == normalizedStatus)
            .Where(item => assignedToMe != true || item.OwnerId == actor.Id || assignedCaseIds.Contains(item.Id))
            .Where(item => string.IsNullOrWhiteSpace(keyword) || Matches(item, keyword.Trim()))
            .OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.CreatedAt)
            .Select(item => ToView(item, false)).ToList();
        return ServiceResult<PagedResponse<PersonnelCaseView>>.Success(Paging.Create(visible, page, pageSize));
    }

    public ServiceResult<PersonnelCaseView> Get(Employee actor, Guid id)
    {
        var item = db.PersonnelCases.AsNoTracking().SingleOrDefault(value => value.TenantId == TenantId && value.Id == id);
        if (item is null) return ServiceResult<PersonnelCaseView>.Failure("员工办理单不存在。", "DATA_001");
        var assigned = db.PersonnelCaseTasks.AsNoTracking().Any(task => task.PersonnelCaseId == id && task.AssigneeId == actor.Id);
        if (!CanView(actor, item, assigned ? new HashSet<Guid> { id } : new HashSet<Guid>())) return ServiceResult<PersonnelCaseView>.Failure("无权查看该员工办理单。", "AUTH_002");
        return ServiceResult<PersonnelCaseView>.Success(ToView(item, true));
    }

    public ServiceResult<PersonnelCaseView> Create(Employee actor, CreatePersonnelCaseRequest request)
    {
        if (!data.HasPermission(actor, OaPermissions.PersonnelManage)) return ServiceResult<PersonnelCaseView>.Failure("无人事办理单维护权限。", "AUTH_002");
        var type = Normalize(request.Type);
        if (type is null || !PersonnelCaseTypes.All.Contains(type)) return ServiceResult<PersonnelCaseView>.Failure("办理类型不合法。", "PERSONNEL_CASE_001");
        if ((request.Notes?.Trim().Length ?? 0) > 1000) return ServiceResult<PersonnelCaseView>.Failure("办理说明不能超过 1000 个字符。", "PERSONNEL_CASE_001");
        var today = BusinessTime.ChinaToday();
        if (request.EffectiveDate < today.AddYears(-10) || request.EffectiveDate > today.AddYears(1)) return ServiceResult<PersonnelCaseView>.Failure("生效日期超出允许范围。", "PERSONNEL_CASE_001");
        var user = db.Users.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == request.UserId);
        var subject = data.FindEmployee(request.UserId);
        if (user is null || subject is null) return ServiceResult<PersonnelCaseView>.Failure("员工不存在。", "DATA_001");
        if (!data.CanView(actor, subject, "Personnel")) return ServiceResult<PersonnelCaseView>.Failure("无权为该员工发起办理单。", "AUTH_002");
        var owner = db.Users.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == request.OwnerId && item.Status == "ACTIVE");
        if (owner is null) return ServiceResult<PersonnelCaseView>.Failure("办理负责人不存在或已停用。", "PERSONNEL_CASE_001");
        if (db.PersonnelCases.Any(item => item.TenantId == TenantId && item.UserId == user.Id && item.Type == type && item.EffectiveDate == request.EffectiveDate))
            return ServiceResult<PersonnelCaseView>.Failure("同一员工、类型和生效日期的办理单已存在。", "DUPLICATE_001");

        var item = BuildCase(actor, user, owner, type, request.EffectiveDate, request.Notes);
        var taskResult = BuildTasks(item, user, owner);
        if (!taskResult.IsSuccess) return ServiceResult<PersonnelCaseView>.Failure(taskResult.Error!, taskResult.Code!);
        var tasks = taskResult.Value!;
        db.PersonnelCases.Add(item);
        db.PersonnelCaseTasks.AddRange(tasks);
        Audit(actor, "PERSONNEL_CASE_CREATED", item, $"创建{TypeName(type)}办理单 {item.Number}");
        NotifyAssignees(actor, item, tasks);
        var saveFailure = SaveCaseChanges<PersonnelCaseView>();
        if (saveFailure is not null) return saveFailure;
        return ServiceResult<PersonnelCaseView>.Success(ToView(item, true));
    }

    public ServiceResult<PersonnelCaseView> EnsureAutomatic(Employee actor, string userId, string type, DateOnly effectiveDate, string notes)
    {
        var existing = db.PersonnelCases.SingleOrDefault(item => item.TenantId == TenantId && item.UserId == userId && item.Type == type && item.EffectiveDate == effectiveDate);
        if (existing is not null) return ServiceResult<PersonnelCaseView>.Success(ToView(existing, true));
        return Create(actor, new CreatePersonnelCaseRequest(userId, type, effectiveDate, actor.Id, notes));
    }

    public ServiceResult<PersonnelCaseView> UpdateTask(Employee actor, Guid caseId, Guid taskId, UpdatePersonnelCaseTaskRequest request)
    {
        var item = db.PersonnelCases.SingleOrDefault(value => value.TenantId == TenantId && value.Id == caseId);
        var task = db.PersonnelCaseTasks.SingleOrDefault(value => value.TenantId == TenantId && value.PersonnelCaseId == caseId && value.Id == taskId);
        if (item is null || task is null) return ServiceResult<PersonnelCaseView>.Failure("办理单或任务不存在。", "DATA_001");
        if (item.Status != PersonnelCaseStatuses.Open) return ServiceResult<PersonnelCaseView>.Failure("已结束的办理单不能修改任务。", "STATE_001");
        if (task.Version != request.Version) return ServiceResult<PersonnelCaseView>.Failure("任务已被其他人更新，请刷新后重试。", "CONCURRENCY_001");
        var canManage = data.HasPermission(actor, OaPermissions.PersonnelManage) && CanViewSubject(actor, item.UserId);
        if (!canManage && task.AssigneeId != actor.Id) return ServiceResult<PersonnelCaseView>.Failure("只能处理分配给自己的任务。", "AUTH_002");
        var status = Normalize(request.Status);
        if (status is null || !PersonnelTaskStatuses.All.Contains(status)) return ServiceResult<PersonnelCaseView>.Failure("任务状态不合法。", "PERSONNEL_CASE_001");
        if (!canManage && (request.AssigneeId != task.AssigneeId || request.DueDate != task.DueDate || status != PersonnelTaskStatuses.Completed || task.Status != PersonnelTaskStatuses.Pending))
            return ServiceResult<PersonnelCaseView>.Failure("任务负责人只能完成自己的待办，不能改派、豁免或重开。", "AUTH_002");
        var note = Normalize(request.CompletionNote);
        if ((status == PersonnelTaskStatuses.Completed && (note?.Length ?? 0) < 2) || (status == PersonnelTaskStatuses.Waived && (note?.Length ?? 0) < 5))
            return ServiceResult<PersonnelCaseView>.Failure("完成说明至少 2 个字符；豁免原因至少 5 个字符。", "PERSONNEL_CASE_001");
        if (status == PersonnelTaskStatuses.Waived && !canManage) return ServiceResult<PersonnelCaseView>.Failure("只有 HR 可以豁免任务。", "AUTH_002");
        if (request.DueDate < item.EffectiveDate.AddDays(-90) || request.DueDate > item.EffectiveDate.AddDays(180)) return ServiceResult<PersonnelCaseView>.Failure("任务截止日期超出办理周期。", "PERSONNEL_CASE_001");
        var assignee = db.Users.AsNoTracking().SingleOrDefault(value => value.TenantId == TenantId && value.Id == request.AssigneeId && value.Status == "ACTIVE");
        if (assignee is null) return ServiceResult<PersonnelCaseView>.Failure("任务负责人不存在或已停用。", "PERSONNEL_CASE_001");

        var assigneeChanged = task.AssigneeId != assignee.Id;
        task.AssigneeId = assignee.Id;
        task.AssigneeName = assignee.Name;
        task.DueDate = request.DueDate;
        task.Status = status;
        task.CompletionNote = status == PersonnelTaskStatuses.Pending ? null : note;
        task.CompletedBy = status == PersonnelTaskStatuses.Pending ? null : actor.Id;
        task.CompletedByName = status == PersonnelTaskStatuses.Pending ? null : actor.Name;
        task.CompletedAt = status == PersonnelTaskStatuses.Pending ? null : DateTimeOffset.UtcNow;
        task.Version++;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        item.Version++;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, status == PersonnelTaskStatuses.Pending ? "PERSONNEL_TASK_REOPENED" : status == PersonnelTaskStatuses.Waived ? "PERSONNEL_TASK_WAIVED" : "PERSONNEL_TASK_COMPLETED", item, $"{task.Title}：{note ?? "重新打开"}");
        if (assigneeChanged && assignee.Id != actor.Id)
            notifications?.Enqueue(assignee.Id, "PERSONNEL_TASK_ASSIGNED", "收到员工办理任务", $"{item.EmployeeName} · {task.Title}，截止 {task.DueDate:yyyy-MM-dd}", "PersonnelCase", item.Id);
        var saveFailure = SaveCaseChanges<PersonnelCaseView>();
        if (saveFailure is not null) return saveFailure;
        return ServiceResult<PersonnelCaseView>.Success(ToView(item, true));
    }

    public ServiceResult<PersonnelCaseView> Complete(Employee actor, Guid id, CompletePersonnelCaseRequest request)
    {
        var item = db.PersonnelCases.SingleOrDefault(value => value.TenantId == TenantId && value.Id == id);
        if (item is null) return ServiceResult<PersonnelCaseView>.Failure("员工办理单不存在。", "DATA_001");
        if (!CanManage(actor, item)) return ServiceResult<PersonnelCaseView>.Failure("无权办结该员工办理单。", "AUTH_002");
        if (item.Status != PersonnelCaseStatuses.Open) return ServiceResult<PersonnelCaseView>.Failure("当前办理单不能重复办结。", "STATE_001");
        if (item.Version != request.Version) return ServiceResult<PersonnelCaseView>.Failure("办理单已更新，请刷新后重试。", "CONCURRENCY_001");
        var comment = request.Comment.Trim();
        if (comment.Length is < 5 or > 500) return ServiceResult<PersonnelCaseView>.Failure("办结说明应为 5–500 个字符。", "PERSONNEL_CASE_001");
        var pending = db.PersonnelCaseTasks.Where(task => task.PersonnelCaseId == id && task.Status == PersonnelTaskStatuses.Pending).Select(task => task.Title).ToList();
        if (pending.Count > 0) return ServiceResult<PersonnelCaseView>.Failure($"仍有 {pending.Count} 项任务未处理：{string.Join('、', pending.Take(3))}", "PERSONNEL_CASE_002");
        item.Status = PersonnelCaseStatuses.Completed;
        item.CompletedBy = actor.Id;
        item.CompletedByName = actor.Name;
        item.CompletedAt = DateTimeOffset.UtcNow;
        item.CompletionComment = comment;
        item.Version++;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "PERSONNEL_CASE_COMPLETED", item, comment);
        if (item.UserId != actor.Id) notifications?.Enqueue(item.UserId, "PERSONNEL_CASE_COMPLETED", $"{TypeName(item.Type)}办理已完成", comment, "PersonnelCase", item.Id);
        var saveFailure = SaveCaseChanges<PersonnelCaseView>();
        if (saveFailure is not null) return saveFailure;
        return ServiceResult<PersonnelCaseView>.Success(ToView(item, true));
    }

    public ServiceResult<PersonnelCaseView> Cancel(Employee actor, Guid id, CancelPersonnelCaseRequest request)
    {
        var item = db.PersonnelCases.SingleOrDefault(value => value.TenantId == TenantId && value.Id == id);
        if (item is null) return ServiceResult<PersonnelCaseView>.Failure("员工办理单不存在。", "DATA_001");
        if (!CanManage(actor, item)) return ServiceResult<PersonnelCaseView>.Failure("无权取消该员工办理单。", "AUTH_002");
        if (item.Status != PersonnelCaseStatuses.Open) return ServiceResult<PersonnelCaseView>.Failure("当前办理单不能取消。", "STATE_001");
        if (item.Version != request.Version) return ServiceResult<PersonnelCaseView>.Failure("办理单已更新，请刷新后重试。", "CONCURRENCY_001");
        var reason = request.Reason.Trim();
        if (reason.Length is < 5 or > 500) return ServiceResult<PersonnelCaseView>.Failure("取消原因应为 5–500 个字符。", "PERSONNEL_CASE_001");
        item.Status = PersonnelCaseStatuses.Cancelled;
        item.CancelledBy = actor.Id;
        item.CancelledByName = actor.Name;
        item.CancelledAt = DateTimeOffset.UtcNow;
        item.CancellationReason = reason;
        item.Version++;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        Audit(actor, "PERSONNEL_CASE_CANCELLED", item, reason);
        var saveFailure = SaveCaseChanges<PersonnelCaseView>();
        if (saveFailure is not null) return saveFailure;
        return ServiceResult<PersonnelCaseView>.Success(ToView(item, true));
    }

    public int DispatchTaskAlerts(DateOnly? dispatchDate = null)
    {
        var today = dispatchDate ?? BusinessTime.ChinaToday();
        var dueSoonDays = Math.Clamp(configuration?.GetValue("PersonnelCaseAlerts:DueSoonDays", 1) ?? 1, 0, 30);
        var escalateAfterDays = Math.Clamp(configuration?.GetValue("PersonnelCaseAlerts:EscalateAfterDays", 3) ?? 3, 1, 90);
        var candidates = (from task in db.PersonnelCaseTasks.AsNoTracking()
                          join item in db.PersonnelCases.AsNoTracking() on task.PersonnelCaseId equals item.Id
                          where task.TenantId == TenantId && task.Status == PersonnelTaskStatuses.Pending && item.Status == PersonnelCaseStatuses.Open && task.DueDate <= today.AddDays(dueSoonDays)
                          select new { Task = task, Case = item }).ToList();
        if (candidates.Count == 0) return 0;

        var taskIds = candidates.Select(candidate => candidate.Task.Id).ToList();
        var existing = db.PersonnelCaseAlertDeliveries.AsNoTracking().Where(item => taskIds.Contains(item.PersonnelCaseTaskId)).ToList()
            .Select(item => (item.PersonnelCaseTaskId, item.RecipientId, item.AlertType)).ToHashSet();
        var activeIds = data.ActiveEmployees.Select(employee => employee.Id).ToHashSet(StringComparer.Ordinal);
        var created = 0;
        foreach (var candidate in candidates)
        {
            var lateDays = today.DayNumber - candidate.Task.DueDate.DayNumber;
            var alertType = lateDays >= escalateAfterDays ? EscalatedAlert : lateDays >= 1 ? OverdueAlert : DueSoonAlert;
            var recipients = AlertRecipients(candidate.Case, candidate.Task, alertType, activeIds);
            var taskCreated = 0;
            foreach (var recipientId in recipients)
            {
                if (existing.Contains((candidate.Task.Id, recipientId, alertType))) continue;
                var (title, content) = AlertContent(candidate.Case, candidate.Task, alertType, lateDays);
                db.Notifications.Add(new NotificationRecord { TenantId = TenantId, RecipientId = recipientId, Type = $"PERSONNEL_TASK_{alertType}", Title = title, Content = content, ResourceType = "PersonnelCase", ResourceId = candidate.Case.Id.ToString() });
                db.PersonnelCaseAlertDeliveries.Add(new PersonnelCaseAlertDeliveryRecord { TenantId = TenantId, PersonnelCaseTaskId = candidate.Task.Id, RecipientId = recipientId, AlertType = alertType });
                existing.Add((candidate.Task.Id, recipientId, alertType));
                created++;
                taskCreated++;
            }
            if (taskCreated > 0)
                db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = "system", Action = "PERSONNEL_TASK_ALERT_DISPATCHED", ResourceType = "PersonnelCase", ResourceId = candidate.Case.Id.ToString(), Summary = $"{candidate.Task.Title}：{alertType}，派发 {taskCreated} 条提醒" });
        }
        if (created > 0)
        {
            try
            {
                db.SaveChanges();
            }
            catch (DbUpdateException exception) when (IsAlertDeliveryUniqueConflict(exception))
            {
                db.ChangeTracker.Clear();
                return 0;
            }
        }
        return created;
    }

    public bool HasCompletedOffboarding(string userId, DateOnly departureDate) => db.PersonnelCases.AsNoTracking().Any(item =>
        item.TenantId == TenantId &&
        item.UserId == userId &&
        item.Type == PersonnelCaseTypes.Offboarding &&
        item.EffectiveDate == departureDate &&
        item.Status == PersonnelCaseStatuses.Completed);

    private ServiceResult<T>? SaveCaseChanges<T>()
    {
        try
        {
            db.SaveChanges();
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ServiceResult<T>.Failure("员工办理单或任务已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        }
        catch (DbUpdateException exception) when (IsCaseUniqueConflict(exception))
        {
            db.ChangeTracker.Clear();
            return ServiceResult<T>.Failure("同一员工、类型和生效日期的办理单已存在。", "DUPLICATE_001");
        }
    }

    private static bool IsCaseUniqueConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_personnel_case_TenantId_UserId_Type_EffectiveDate"
        };

    private static bool IsAlertDeliveryUniqueConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "UX_personnel_case_alert_delivery_task_recipient_type"
        };

    private PersonnelCaseRecord BuildCase(Employee actor, UserRecord user, UserRecord owner, string type, DateOnly effectiveDate, string? notes)
    {
        var department = db.Departments.AsNoTracking().Where(item => item.Id == user.DepartmentId).Select(item => item.Name).SingleOrDefault() ?? user.DepartmentId;
        return new PersonnelCaseRecord
        {
            TenantId = TenantId,
            Number = $"PC-{BusinessTime.ChinaToday():yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(),
            UserId = user.Id,
            EmployeeName = user.Name,
            DepartmentName = department,
            Type = type,
            Title = $"{user.Name} · {TypeName(type)}办理",
            EffectiveDate = effectiveDate,
            OwnerId = owner.Id,
            OwnerName = owner.Name,
            Notes = Normalize(notes),
            CreatedBy = actor.Id,
            CreatedByName = actor.Name
        };
    }

    private ServiceResult<IReadOnlyList<PersonnelCaseTaskRecord>> BuildTasks(PersonnelCaseRecord item, UserRecord user, UserRecord owner)
    {
        var manager = user.ManagerId is null ? null : db.Users.AsNoTracking().SingleOrDefault(value => value.TenantId == TenantId && value.Id == user.ManagerId && value.Status == "ACTIVE");
        var tasks = new List<PersonnelCaseTaskRecord>();
        foreach (var (definition, index) in Templates[item.Type].Select((definition, index) => (definition, index)))
        {
            var assigneeResult = definition.AssigneeKind switch
            {
                "EMPLOYEE" => ServiceResult<UserRecord>.Success(user),
                "MANAGER" => ServiceResult<UserRecord>.Success(manager ?? owner),
                _ => ResolveCategoryAssignee(definition.Category, owner)
            };
            if (!assigneeResult.IsSuccess)
                return ServiceResult<IReadOnlyList<PersonnelCaseTaskRecord>>.Failure(assigneeResult.Error!, assigneeResult.Code!);
            var assignee = assigneeResult.Value!;
            tasks.Add(new PersonnelCaseTaskRecord { TenantId = TenantId, PersonnelCaseId = item.Id, Code = definition.Code, Title = definition.Title, Category = definition.Category, Required = definition.Required, AssigneeId = assignee.Id, AssigneeName = assignee.Name, DueDate = item.EffectiveDate.AddDays(definition.DueOffsetDays), SortOrder = index + 1 });
        }
        return ServiceResult<IReadOnlyList<PersonnelCaseTaskRecord>>.Success(tasks);
    }

    private ServiceResult<UserRecord> ResolveCategoryAssignee(string category, UserRecord owner)
    {
        var configuredId = Normalize(configuration?[$"PersonnelCases:CategoryAssignees:{category}"]);
        if (configuredId is not null)
        {
            var configured = db.Users.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == configuredId && item.Status == "ACTIVE");
            return configured is null
                ? ServiceResult<UserRecord>.Failure($"员工办理职责 {category} 配置的负责人不存在或已停用。", "PERSONNEL_CASE_004")
                : ServiceResult<UserRecord>.Success(configured);
        }

        var active = data.ActiveEmployees;
        if (CategoryRolePriorities.TryGetValue(category, out var roles))
        {
            foreach (var role in roles)
            {
                var candidate = active.Where(employee => data.HasRole(employee, role)).OrderBy(employee => employee.Id, StringComparer.Ordinal).FirstOrDefault();
                if (candidate is not null) return ResolveActiveUser(candidate.Id, category);
            }
        }
        if (CategoryPermissionFallbacks.TryGetValue(category, out var permission))
        {
            var candidate = active.Where(employee => data.HasPermission(employee, permission)).OrderBy(employee => employee.Id, StringComparer.Ordinal).FirstOrDefault();
            if (candidate is not null) return ResolveActiveUser(candidate.Id, category);
        }
        return ServiceResult<UserRecord>.Success(owner);
    }

    private ServiceResult<UserRecord> ResolveActiveUser(string userId, string category)
    {
        var user = db.Users.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == userId && item.Status == "ACTIVE");
        return user is null
            ? ServiceResult<UserRecord>.Failure($"员工办理职责 {category} 的自动负责人不存在或已停用。", "PERSONNEL_CASE_004")
            : ServiceResult<UserRecord>.Success(user);
    }

    private IReadOnlyList<string> AlertRecipients(PersonnelCaseRecord item, PersonnelCaseTaskRecord task, string alertType, IReadOnlySet<string> activeIds)
    {
        var recipients = new HashSet<string>(StringComparer.Ordinal);
        if (activeIds.Contains(task.AssigneeId)) recipients.Add(task.AssigneeId);
        if (alertType is OverdueAlert or EscalatedAlert || recipients.Count == 0)
            if (activeIds.Contains(item.OwnerId)) recipients.Add(item.OwnerId);
        if (alertType == EscalatedAlert && data.FindEmployee(item.UserId) is { } subject)
            foreach (var manager in data.ActiveEmployees.Where(employee => data.HasPermission(employee, OaPermissions.PersonnelManage) && data.CanView(employee, subject, "Personnel")))
                recipients.Add(manager.Id);
        return recipients.ToList();
    }

    private static (string Title, string Content) AlertContent(PersonnelCaseRecord item, PersonnelCaseTaskRecord task, string alertType, int lateDays) => alertType switch
    {
        DueSoonAlert => ("员工办理任务即将到期", $"{item.EmployeeName} · {task.Title} 将于 {task.DueDate:yyyy-MM-dd} 到期，请及时处理。"),
        OverdueAlert => ("员工办理任务已逾期", $"{item.EmployeeName} · {task.Title} 已逾期 {lateDays} 天，请尽快完成或联系办理负责人。"),
        _ => ("员工办理任务逾期升级", $"{item.EmployeeName} · {task.Title} 已逾期 {lateDays} 天，现已升级至办理负责人和 HR 管理人员。")
    };

    private PersonnelCaseView ToView(PersonnelCaseRecord item, bool includeTasks)
    {
        var today = BusinessTime.ChinaToday();
        var allTasks = db.PersonnelCaseTasks.AsNoTracking().Where(task => task.PersonnelCaseId == item.Id).OrderBy(task => task.SortOrder).ToList();
        var tasks = includeTasks ? allTasks.Select(task => new PersonnelCaseTaskView(task.Id, task.Code, task.Title, task.Category, task.Required, task.AssigneeId, task.AssigneeName, task.DueDate, task.Status, item.Status == PersonnelCaseStatuses.Open && task.Status == PersonnelTaskStatuses.Pending && task.DueDate < today, task.CompletionNote, task.CompletedBy, task.CompletedByName, task.CompletedAt, task.SortOrder, task.Version)).ToList() : [];
        var resolved = allTasks.Count(task => task.Status != PersonnelTaskStatuses.Pending);
        var overdue = item.Status == PersonnelCaseStatuses.Open ? allTasks.Count(task => task.Status == PersonnelTaskStatuses.Pending && task.DueDate < today) : 0;
        return new PersonnelCaseView(item.Id, item.Number, item.UserId, item.EmployeeName, item.DepartmentName, item.Type, item.Title, item.EffectiveDate, item.Status, item.OwnerId, item.OwnerName, item.Notes, allTasks.Count, resolved, overdue, item.Version, item.CreatedBy, item.CreatedByName, item.CreatedAt, item.UpdatedAt, item.CompletedBy, item.CompletedByName, item.CompletedAt, item.CompletionComment, item.CancelledBy, item.CancelledByName, item.CancelledAt, item.CancellationReason, tasks);
    }

    private void NotifyAssignees(Employee actor, PersonnelCaseRecord item, IEnumerable<PersonnelCaseTaskRecord> tasks)
    {
        var assignees = tasks.Select(task => task.AssigneeId).Distinct().Where(id => id != actor.Id).ToList();
        foreach (var assignee in assignees)
            notifications?.Enqueue(assignee, "PERSONNEL_TASK_ASSIGNED", "收到员工办理任务", $"{item.EmployeeName} · {TypeName(item.Type)}，生效日期 {item.EffectiveDate:yyyy-MM-dd}", "PersonnelCase", item.Id);
    }

    private bool CanView(Employee actor, PersonnelCaseRecord item, IReadOnlySet<Guid> assignedCaseIds) => actor.Id == item.UserId || actor.Id == item.OwnerId || assignedCaseIds.Contains(item.Id) || CanViewSubject(actor, item.UserId);
    private bool CanViewSubject(Employee actor, string userId) => data.FindEmployee(userId) is { } subject && data.CanView(actor, subject, "Personnel");
    private bool CanManage(Employee actor, PersonnelCaseRecord item) => data.HasPermission(actor, OaPermissions.PersonnelManage) && CanViewSubject(actor, item.UserId);
    private static bool Matches(PersonnelCaseRecord item, string keyword) => item.Number.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.EmployeeName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) || item.DepartmentName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    private static string TypeName(string type) => type switch { PersonnelCaseTypes.Onboarding => "入职", PersonnelCaseTypes.Regularization => "转正", PersonnelCaseTypes.Transfer => "调动", PersonnelCaseTypes.Offboarding => "离职", _ => type };
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void Audit(Employee actor, string action, PersonnelCaseRecord item, string summary) => db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "PersonnelCase", ResourceId = item.Id.ToString(), Summary = summary });
}
