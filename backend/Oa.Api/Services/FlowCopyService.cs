using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class FlowCopyService(DemoData data, OaDbContext? db = null)
{
    private const string TenantId = "demo";

    public ServiceResult<IReadOnlyList<string>> Validate(Employee actor, IReadOnlyList<string>? recipientIds)
    {
        var ids = (recipientIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (ids.Count > 20) return ServiceResult<IReadOnlyList<string>>.Failure("抄送人最多选择 20 人。", "FLOW_005");
        if (ids.Contains(actor.Id)) return ServiceResult<IReadOnlyList<string>>.Failure("不能将自己设置为抄送人。", "FLOW_005");
        if (ids.Any(id => data.ActiveEmployees.All(employee => employee.Id != id))) return ServiceResult<IReadOnlyList<string>>.Failure("抄送人不存在或已停用。", "FLOW_005");
        return ServiceResult<IReadOnlyList<string>>.Success(ids);
    }

    public IReadOnlyList<string> LoadRecipientIds(string businessType, Guid businessId)
    {
        if (db is null) return [];
        return db.FlowCopyRecipients.AsNoTracking()
            .Where(item => item.TenantId == TenantId && item.BusinessType == businessType && item.BusinessId == businessId)
            .OrderBy(item => item.CreatedAt)
            .Select(item => item.RecipientId)
            .ToList();
    }

    public void Track(string businessType, Guid businessId, string businessNumber, string applicantName, string title, IReadOnlyList<string> recipientIds)
    {
        if (db is null) return;
        var existing = db.FlowCopyRecipients.Where(item => item.TenantId == TenantId && item.BusinessType == businessType && item.BusinessId == businessId).ToList();
        foreach (var obsolete in existing.Where(item => !recipientIds.Contains(item.RecipientId) && item.AvailableAt is null)) db.FlowCopyRecipients.Remove(obsolete);
        foreach (var recipientId in recipientIds)
        {
            var employee = data.FindEmployee(recipientId)!;
            var record = existing.SingleOrDefault(item => item.RecipientId == recipientId);
            if (record is null)
            {
                record = new FlowCopyRecipientRecord { TenantId = TenantId, BusinessType = businessType, BusinessId = businessId, RecipientId = employee.Id };
                db.FlowCopyRecipients.Add(record);
            }
            record.RecipientName = employee.Name;
            record.BusinessNumber = businessNumber;
            record.ApplicantName = applicantName;
            record.Title = title;
        }
    }

    public IReadOnlyList<Employee> Activate(string businessType, Guid businessId, string status)
    {
        if (db is null) return [];
        var now = DateTimeOffset.UtcNow;
        var records = db.FlowCopyRecipients.Where(item => item.TenantId == TenantId && item.BusinessType == businessType && item.BusinessId == businessId && item.AvailableAt == null).ToList();
        foreach (var item in records) { item.Status = status; item.AvailableAt = now; }
        db.SaveChanges();
        return records.Select(item => data.FindEmployee(item.RecipientId)).Where(item => item is not null).Cast<Employee>().ToList();
    }

    public bool CanView(Employee actor, string businessType, Guid businessId) => db is not null && db.FlowCopyRecipients.AsNoTracking().Any(item =>
        item.TenantId == TenantId && item.BusinessType == businessType && item.BusinessId == businessId && item.RecipientId == actor.Id && item.AvailableAt != null);

    public void DeleteUnavailable(string businessType, Guid businessId)
    {
        if (db is null) return;
        db.FlowCopyRecipients.Where(item => item.TenantId == TenantId && item.BusinessType == businessType && item.BusinessId == businessId && item.AvailableAt == null).ExecuteDelete();
    }

    public IReadOnlyList<FlowCopyView> ListMine(Employee actor) => db is null ? [] : db.FlowCopyRecipients.AsNoTracking()
        .Where(item => item.TenantId == TenantId && item.RecipientId == actor.Id && item.AvailableAt != null)
        .OrderByDescending(item => item.AvailableAt)
        .Select(item => new FlowCopyView(item.Id, item.BusinessType, item.BusinessId, item.BusinessNumber, item.ApplicantName, item.Title, item.Status, item.AvailableAt!.Value, item.ReadAt))
        .ToList();

    public ServiceResult<FlowCopyView> MarkRead(Employee actor, Guid id)
    {
        if (db is null) return ServiceResult<FlowCopyView>.Failure("抄送事项不存在。", "DATA_001");
        var item = db.FlowCopyRecipients.SingleOrDefault(record => record.Id == id && record.TenantId == TenantId && record.RecipientId == actor.Id && record.AvailableAt != null);
        if (item is null) return ServiceResult<FlowCopyView>.Failure("抄送事项不存在或无权限。", "DATA_001");
        item.ReadAt ??= DateTimeOffset.UtcNow;
        db.SaveChanges();
        return ServiceResult<FlowCopyView>.Success(new FlowCopyView(item.Id, item.BusinessType, item.BusinessId, item.BusinessNumber, item.ApplicantName, item.Title, item.Status, item.AvailableAt!.Value, item.ReadAt));
    }
}
