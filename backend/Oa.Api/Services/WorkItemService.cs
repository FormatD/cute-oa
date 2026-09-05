using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class WorkItemService(
    OaDbContext db,
    DemoData data,
    LeaveService leave,
    ExpenseService expense,
    TravelService travel,
    PurchaseService purchase,
    SealService seal,
    FlowCopyService copies,
    AnnouncementService announcements,
    KnowledgeDocumentService documents,
    EmploymentContractService contracts)
{
    private const string TenantId = IdentityDefaults.TenantId;

    public WorkItemResponse List(Employee actor, WorkItemQuery query)
    {
        var all = Build(actor);
        var summary = Summarize(all);
        var tab = WorkItemTabs.All.Contains(query.Tab ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? query.Tab!.ToLowerInvariant()
            : WorkItemTabs.Pending;
        IEnumerable<WorkItemView> filtered = all.Where(item => item.Tab == tab);

        if (!string.IsNullOrWhiteSpace(query.BusinessType))
            filtered = filtered.Where(item => item.BusinessType.Equals(query.BusinessType.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Status))
            filtered = filtered.Where(item => item.Status.Equals(query.Status.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.ApplicantId))
            filtered = filtered.Where(item => item.ApplicantId == query.ApplicantId.Trim());
        if (!string.IsNullOrWhiteSpace(query.DepartmentId))
        {
            var departmentName = data.Departments.SingleOrDefault(item => item.Id == query.DepartmentId.Trim())?.Name;
            filtered = filtered.Where(item => departmentName is not null && item.DepartmentName == departmentName);
        }
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            filtered = filtered.Where(item => item.Number.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.ApplicantName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.DepartmentName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
        if (query.StartDate is not null)
            filtered = filtered.Where(item => ChinaDate(item.OccurredAt) >= query.StartDate);
        if (query.EndDate is not null)
            filtered = filtered.Where(item => ChinaDate(item.OccurredAt) <= query.EndDate);

        var ordered = tab switch
        {
            WorkItemTabs.Pending => filtered.OrderBy(item => UrgencyRank(item.Urgency)).ThenBy(item => item.DueDate).ThenByDescending(item => item.OccurredAt).ThenBy(item => item.Id, StringComparer.Ordinal),
            WorkItemTabs.Reading => filtered.OrderBy(item => item.IsRead).ThenByDescending(item => item.OccurredAt).ThenBy(item => item.Id, StringComparer.Ordinal),
            WorkItemTabs.Risk => filtered.OrderBy(item => UrgencyRank(item.Urgency)).ThenBy(item => item.DueDate).ThenByDescending(item => item.OccurredAt).ThenBy(item => item.Id, StringComparer.Ordinal),
            _ => filtered.OrderByDescending(item => item.ProcessedAt ?? item.OccurredAt).ThenBy(item => item.Id, StringComparer.Ordinal)
        };

        var items = ordered.ToList();
        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (decimal)pageSize));
        var page = Math.Clamp(query.Page ?? 1, 1, totalPages);
        return new WorkItemResponse(items.Skip((page - 1) * pageSize).Take(pageSize).ToList(), items.Count, page, pageSize, totalPages, summary);
    }

    public WorkItemSummary GetSummary(Employee actor) => Summarize(Build(actor));

    public WorkItemOverview GetOverview(Employee actor, int requestedTake = 5)
    {
        var all = Build(actor);
        var take = Math.Clamp(requestedTake, 1, 20);
        return new WorkItemOverview(
            all.Where(item => item.Tab == WorkItemTabs.Pending).OrderBy(item => UrgencyRank(item.Urgency)).ThenBy(item => item.DueDate).ThenByDescending(item => item.OccurredAt).ThenBy(item => item.Id, StringComparer.Ordinal).Take(take).ToList(),
            all.Where(item => item.Tab == WorkItemTabs.Initiated).OrderByDescending(item => item.OccurredAt).ThenBy(item => item.Id, StringComparer.Ordinal).Take(take).ToList(),
            all.Where(item => item.Tab == WorkItemTabs.Reading && !item.IsRead).OrderByDescending(item => item.OccurredAt).ThenBy(item => item.Id, StringComparer.Ordinal).Take(take).ToList(),
            Summarize(all));
    }

    private List<WorkItemView> Build(Employee actor)
    {
        var items = new List<WorkItemView>();
        AddApprovals(actor, items);
        AddFinance(actor, items);
        AddInitiated(actor, items);
        AddPersonnel(actor, items);
        AddAttendance(actor, items);
        AddReading(actor, items);
        AddContractRisks(actor, items);
        return items;
    }

    private void AddApprovals(Employee actor, List<WorkItemView> items)
    {
        foreach (var task in leave.GetPendingTasks(actor))
        {
            var parent = leave.Get(actor, task.LeaveRequestId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Pending, "leave", parent.Id, task.Id, parent.Number, $"{LeaveTypeName(parent.Type)} · {parent.Reason}", parent.ApplicantId, parent.ApplicantName, Department(parent.ApplicantId), parent.Status.ToString(), task.Sequence, parent.CreatedAt, null, $"/leave/{parent.Id}", true);
        }
        foreach (var task in leave.GetProcessedTasks(actor))
        {
            var parent = leave.Get(actor, task.LeaveRequestId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Processed, "leave", parent.Id, task.Id, parent.Number, $"{LeaveTypeName(parent.Type)} · {parent.Reason}", parent.ApplicantId, parent.ApplicantName, Department(parent.ApplicantId), task.Status.ToString(), task.Sequence, parent.CreatedAt, task.ProcessedAt, $"/leave/{parent.Id}", false);
        }

        foreach (var task in expense.GetPendingTasks(actor))
        {
            var parent = expense.Get(actor, task.ExpenseClaimId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Pending, "expense", parent.Id, task.Id, parent.Number, parent.Description ?? $"费用报销 ¥{parent.TotalAmount:N2}", parent.ApplicantId, parent.ApplicantName, parent.DepartmentName, parent.Status.ToString(), task.Sequence, parent.CreatedAt, null, $"/expense/{parent.Id}", true);
        }
        foreach (var task in expense.GetProcessedTasks(actor))
        {
            var parent = expense.Get(actor, task.ExpenseClaimId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Processed, "expense", parent.Id, task.Id, parent.Number, parent.Description ?? $"费用报销 ¥{parent.TotalAmount:N2}", parent.ApplicantId, parent.ApplicantName, parent.DepartmentName, task.Status.ToString(), task.Sequence, parent.CreatedAt, task.ProcessedAt, $"/expense/{parent.Id}", false);
        }

        foreach (var task in travel.GetPendingTasks(actor))
        {
            var parent = travel.Get(actor, task.TravelRequestId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Pending, "travel", parent.Id, task.Id, parent.Number, parent.Purpose, parent.ApplicantId, parent.ApplicantName, parent.DepartmentName, parent.Status.ToString(), task.Sequence, parent.CreatedAt, null, $"/travel/{parent.Id}", true);
        }
        foreach (var task in travel.GetProcessedTasks(actor))
        {
            var parent = travel.Get(actor, task.TravelRequestId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Processed, "travel", parent.Id, task.Id, parent.Number, parent.Purpose, parent.ApplicantId, parent.ApplicantName, parent.DepartmentName, task.Status.ToString(), task.Sequence, parent.CreatedAt, task.ProcessedAt, $"/travel/{parent.Id}", false);
        }

        foreach (var task in purchase.GetPendingTasks(actor))
        {
            var parent = purchase.Get(actor, task.PurchaseRequestId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Pending, "purchase", parent.Id, task.Id, parent.Number, parent.Title, parent.ApplicantId, parent.ApplicantName, parent.DepartmentName, parent.Status.ToString(), task.Sequence, parent.CreatedAt, null, $"/purchase/{parent.Id}", true);
        }
        foreach (var task in purchase.GetProcessedTasks(actor))
        {
            var parent = purchase.Get(actor, task.PurchaseRequestId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Processed, "purchase", parent.Id, task.Id, parent.Number, parent.Title, parent.ApplicantId, parent.ApplicantName, parent.DepartmentName, task.Status.ToString(), task.Sequence, parent.CreatedAt, task.ProcessedAt, $"/purchase/{parent.Id}", false);
        }

        foreach (var task in seal.GetPendingTasks(actor))
        {
            var parent = seal.Get(actor, task.SealRequestId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Pending, "seal", parent.Id, task.Id, parent.Number, parent.Title, parent.ApplicantId, parent.ApplicantName, parent.DepartmentName, parent.Status.ToString(), task.Sequence, parent.CreatedAt, null, $"/seal/{parent.Id}", true);
        }
        foreach (var task in seal.GetProcessedTasks(actor))
        {
            var parent = seal.Get(actor, task.SealRequestId).Value;
            if (parent is not null) AddApproval(items, WorkItemTabs.Processed, "seal", parent.Id, task.Id, parent.Number, parent.Title, parent.ApplicantId, parent.ApplicantName, parent.DepartmentName, task.Status.ToString(), task.Sequence, parent.CreatedAt, task.ProcessedAt, $"/seal/{parent.Id}", false);
        }
    }

    private void AddInitiated(Employee actor, List<WorkItemView> items)
    {
        foreach (var item in db.LeaveRequests.AsNoTracking().Where(item => item.TenantId == TenantId && item.ApplicantId == actor.Id))
            items.Add(Initiated("leave", item.Id, item.Number, $"{LeaveTypeName((LeaveType)item.Type)} · {item.Reason}", actor, ((LeaveStatus)item.Status).ToString(), item.UpdatedAt, $"/leave/{item.Id}"));
        foreach (var item in db.ExpenseClaims.AsNoTracking().Where(item => item.TenantId == TenantId && item.ApplicantId == actor.Id))
            items.Add(Initiated("expense", item.Id, item.Number, item.Description ?? $"费用报销 ¥{item.TotalAmount:N2}", actor, ((ExpenseStatus)item.Status).ToString(), item.UpdatedAt, $"/expense/{item.Id}"));
        foreach (var item in db.TravelRequests.AsNoTracking().Where(item => item.TenantId == TenantId && item.ApplicantId == actor.Id))
            items.Add(Initiated("travel", item.Id, item.Number, item.Purpose, actor, ((TravelStatus)item.Status).ToString(), item.UpdatedAt, $"/travel/{item.Id}"));
        foreach (var item in db.PurchaseRequests.AsNoTracking().Where(item => item.TenantId == TenantId && item.ApplicantId == actor.Id))
            items.Add(Initiated("purchase", item.Id, item.Number, item.Title, actor, ((PurchaseStatus)item.Status).ToString(), item.UpdatedAt, $"/purchase/{item.Id}"));
        foreach (var item in db.SealRequests.AsNoTracking().Where(item => item.TenantId == TenantId && item.ApplicantId == actor.Id))
            items.Add(Initiated("seal", item.Id, item.Number, item.Title, actor, ((SealStatus)item.Status).ToString(), item.UpdatedAt, $"/seal/{item.Id}"));
    }

    private void AddFinance(Employee actor, List<WorkItemView> items)
    {
        if (!data.HasPermission(actor, OaPermissions.ExpensePay)) return;
        foreach (var item in expense.List(actor).Where(item => item.Status == ExpenseStatus.Approved && item.Payment is null))
            items.Add(new WorkItemView($"finance:expense:{item.Id}", WorkItemTabs.Pending, WorkItemCategories.Finance, "expense", item.Id, null,
                item.Number, item.Description ?? $"待付款报销 ¥{item.TotalAmount:N2}", item.ApplicantId, item.ApplicantName, item.DepartmentName,
                item.Status.ToString(), "财务付款", item.CreatedAt, null, null, "NORMAL", $"/expense/{item.Id}", true, true, "PAYMENT"));
    }

    private void AddPersonnel(Employee actor, List<WorkItemView> items)
    {
        var records = db.PersonnelCaseTasks.AsNoTracking()
            .Where(task => task.TenantId == TenantId && (task.AssigneeId == actor.Id || task.CompletedBy == actor.Id))
            .Join(db.PersonnelCases.AsNoTracking().Where(item => item.TenantId == TenantId), task => task.PersonnelCaseId, item => item.Id, (task, item) => new { Task = task, Case = item })
            .ToList();
        foreach (var row in records)
        {
            var pending = row.Task.AssigneeId == actor.Id && row.Task.Status == PersonnelTaskStatuses.Pending && row.Case.Status == PersonnelCaseStatuses.Open;
            var processed = row.Task.CompletedBy == actor.Id && row.Task.Status != PersonnelTaskStatuses.Pending;
            if (!pending && !processed) continue;
            items.Add(new WorkItemView(
                $"personnel:{row.Task.Id}", pending ? WorkItemTabs.Pending : WorkItemTabs.Processed, WorkItemCategories.Personnel, "personnel",
                row.Case.Id, row.Task.Id, row.Case.Number, row.Task.Title, row.Case.UserId, row.Case.EmployeeName, row.Case.DepartmentName,
                row.Task.Status, row.Task.Category, row.Case.UpdatedAt, row.Task.CompletedAt, row.Task.DueDate, Urgency(row.Task.DueDate),
                $"/hr/personnel-cases/{row.Case.Id}", pending, true, pending ? "COMPLETE" : "VIEW"));
        }
    }

    private void AddAttendance(Employee actor, List<WorkItemView> items)
    {
        var rows = db.AttendanceAppeals.AsNoTracking().Where(item => item.TenantId == TenantId)
            .Join(db.AttendanceRecords.AsNoTracking().Where(item => item.TenantId == TenantId), appeal => appeal.AttendanceRecordId, record => record.Id, (appeal, record) => new { Appeal = appeal, Record = record })
            .ToList();
        foreach (var row in rows)
        {
            if (row.Appeal.SubmittedBy == actor.Id)
            {
                items.Add(new WorkItemView($"attendance-initiated:{row.Appeal.Id}", WorkItemTabs.Initiated, WorkItemCategories.Attendance, "attendance", row.Record.Id, row.Appeal.Id,
                    row.Record.WorkDate.ToString("yyyyMMdd"), $"考勤申诉 · {row.Appeal.Reason}", actor.Id, actor.Name, row.Record.DepartmentName, row.Appeal.Status, "考勤申诉",
                    row.Appeal.SubmittedAt, row.Appeal.ReviewedAt, null, "NORMAL", $"/attendance/{row.Record.Id}", false, true, "VIEW"));
            }
            if (!data.HasPermission(actor, OaPermissions.AttendanceManage)) continue;
            var subject = data.FindEmployee(row.Record.UserId);
            if (subject is null || !data.CanView(actor, subject, "Attendance")) continue;
            var pending = row.Appeal.Status == AttendanceAppealStatuses.Pending;
            var processed = row.Appeal.ReviewedBy == actor.Id && !pending;
            if (!pending && !processed) continue;
            items.Add(new WorkItemView($"attendance:{row.Appeal.Id}", pending ? WorkItemTabs.Pending : WorkItemTabs.Processed, WorkItemCategories.Attendance, "attendance", row.Record.Id, row.Appeal.Id,
                row.Record.WorkDate.ToString("yyyyMMdd"), $"考勤申诉 · {row.Appeal.Reason}", row.Record.UserId, row.Record.EmployeeName, row.Record.DepartmentName, row.Appeal.Status, "考勤申诉审核",
                row.Appeal.SubmittedAt, row.Appeal.ReviewedAt, null, "NORMAL", $"/attendance/{row.Record.Id}", pending, true, pending ? "REVIEW" : "VIEW"));
        }
    }

    private void AddReading(Employee actor, List<WorkItemView> items)
    {
        foreach (var item in copies.ListMine(actor))
            items.Add(new WorkItemView($"copy:{item.Id}", WorkItemTabs.Reading, WorkItemCategories.Copy, item.BusinessType.ToLowerInvariant(), item.BusinessId, item.Id,
                item.BusinessNumber, item.Title, null, item.ApplicantName, string.Empty, item.Status, "审批抄送", item.AvailableAt, null, null, "NORMAL",
                BusinessRoute(item.BusinessType, item.BusinessId), false, item.ReadAt is not null, item.ReadAt is null ? "READ" : "VIEW"));

        for (var page = 1; ; page++)
        {
            var published = announcements.ListPublished(actor, page, 100).Value;
            if (published is null) break;
            foreach (var item in published.Items)
                items.Add(new WorkItemView($"announcement:{item.Id}", WorkItemTabs.Reading, WorkItemCategories.Announcement, "announcement", item.Id, null,
                    $"ANN-{item.Id.ToString()[..8]}", item.Title, item.CreatedBy, item.CreatedByName, string.Empty, item.Status, "公司公告", item.PublishedAt ?? item.CreatedAt, null, null, "NORMAL",
                    $"/announcements/{item.Id}", false, item.ReadAt is not null, item.ReadAt is null ? "READ" : "VIEW"));
            if (page >= published.TotalPages) break;
        }

        for (var page = 1; ; page++)
        {
            var mustRead = documents.ListDocuments(actor, status: DocumentStatus.Published, mustReadOnly: true, page: page, pageSize: 100).Value;
            if (mustRead is null) break;
            foreach (var item in mustRead.Items)
                items.Add(new WorkItemView($"document:{item.Id}:v{item.Version}", WorkItemTabs.Reading, WorkItemCategories.Document, "document", item.Id, null,
                    item.Number, item.Title, item.CreatedBy, item.CreatedByName, DepartmentName(item.DepartmentId), item.Status.ToString(), $"制度签收 V{item.Version}", item.PublishedAt ?? item.CreatedAt, null,
                    item.ExpiryDate, item.ExpiryDate is null ? "NORMAL" : Urgency(item.ExpiryDate.Value), $"/documents/{item.Id}", false, item.HasAcknowledged, item.HasAcknowledged ? "VIEW" : "ACKNOWLEDGE"));
            if (page >= mustRead.TotalPages) break;
        }
    }

    private void AddContractRisks(Employee actor, List<WorkItemView> items)
    {
        for (var page = 1; ; page++)
        {
            var result = contracts.List(actor, null, null, null, null, null, page, 100).Value;
            if (result is null) break;
            foreach (var item in result.Items.Where(IsContractRisk))
                items.Add(new WorkItemView($"contract:{item.Id}", WorkItemTabs.Risk, WorkItemCategories.Contract, "contract", item.Id, null, item.ContractNumber,
                    $"{item.EmployeeName} · 劳动合同", item.UserId, item.EmployeeName, item.DepartmentName, item.DisplayStatus, "合同风险",
                    item.UpdatedAt, null, item.EndDate, item.DaysUntilEnd is < 0 ? "OVERDUE" : item.DaysUntilEnd is <= 7 ? "DUE_SOON" : "NORMAL",
                    $"/hr/contracts/{item.Id}", false, item.CurrentAlertAcknowledged, "VIEW"));
            if (page >= result.TotalPages) break;
        }
    }

    private static void AddApproval(List<WorkItemView> items, string tab, string businessType, Guid resourceId, Guid taskId, string number, string title,
        string applicantId, string applicantName, string departmentName, string status, int sequence, DateTimeOffset occurredAt, DateTimeOffset? processedAt, string route, bool canProcess)
        => items.Add(new WorkItemView($"approval:{businessType}:{taskId}", tab, WorkItemCategories.Approval, businessType, resourceId, taskId, number, title,
            applicantId, applicantName, departmentName, status, $"第 {sequence} 级审批", occurredAt, processedAt, null, "NORMAL", route, canProcess, true, canProcess ? "APPROVE" : "VIEW"));

    private static WorkItemView Initiated(string businessType, Guid id, string number, string title, Employee actor, string status, DateTimeOffset occurredAt, string route)
        => new($"initiated:{businessType}:{id}", WorkItemTabs.Initiated, WorkItemCategories.Approval, businessType, id, null, number, title, actor.Id, actor.Name,
            actor.DepartmentName, status, null, occurredAt, null, null, "NORMAL", route, false, true, "VIEW");

    private static WorkItemSummary Summarize(IReadOnlyCollection<WorkItemView> items) => new(
        items.Count(item => item.Tab == WorkItemTabs.Pending),
        items.Count(item => item.Tab == WorkItemTabs.Pending && item.Category == WorkItemCategories.Approval),
        items.Count(item => item.Tab == WorkItemTabs.Pending && item.Category == WorkItemCategories.Personnel),
        items.Count(item => item.Tab == WorkItemTabs.Pending && item.Category == WorkItemCategories.Attendance),
        items.Count(item => item.Tab == WorkItemTabs.Pending && item.Category == WorkItemCategories.Finance),
        items.Count(item => item.Tab == WorkItemTabs.Processed),
        items.Count(item => item.Tab == WorkItemTabs.Initiated),
        items.Count(item => item.Tab == WorkItemTabs.Reading && !item.IsRead),
        items.Count(item => item.Tab == WorkItemTabs.Risk));

    private string Department(string userId) => data.FindEmployee(userId)?.DepartmentName ?? string.Empty;
    private string DepartmentName(string? departmentId) => departmentId is null ? "全公司" : data.Departments.SingleOrDefault(item => item.Id == departmentId)?.Name ?? departmentId;
    private static DateOnly ChinaDate(DateTimeOffset value) => DateOnly.FromDateTime(value.ToOffset(BusinessTime.ChinaOffset).DateTime);
    private static int UrgencyRank(string urgency) => urgency switch { "OVERDUE" => 0, "DUE_SOON" => 1, _ => 2 };
    private static string Urgency(DateOnly dueDate)
    {
        var days = dueDate.DayNumber - BusinessTime.ChinaToday().DayNumber;
        return days < 0 ? "OVERDUE" : days <= 1 ? "DUE_SOON" : "NORMAL";
    }
    private static bool IsContractRisk(EmploymentContractView item) => item.OpenEndedReviewRequired
        || item.DisplayStatus is EmploymentContractStatuses.Expired or EmploymentContractStatuses.Expiring
        || item.CurrentAlertThreshold is not null && !item.CurrentAlertAcknowledged;
    private static string LeaveTypeName(LeaveType type) => type switch { LeaveType.Annual => "年假", LeaveType.Personal => "事假", LeaveType.Sick => "病假", LeaveType.CompTime => "调休", _ => "请假" };
    private static string BusinessRoute(string businessType, Guid id) => businessType.ToUpperInvariant() switch
    {
        "LEAVE" => $"/leave/{id}",
        "EXPENSE" => $"/expense/{id}",
        "TRAVEL" => $"/travel/{id}",
        "PURCHASE" => $"/purchase/{id}",
        "SEAL" => $"/seal/{id}",
        _ => "/workbench"
    };
}
