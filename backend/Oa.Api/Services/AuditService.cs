using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed record AuditLogQuery(string? Keyword, string? ActorId, string? ResourceType, DateOnly? StartDate, DateOnly? EndDate, int? Page, int? PageSize);
public sealed record AuditLogView(Guid Id, string ActorId, string ActorName, string Action, string ResourceType, string ResourceId, string Summary, DateTimeOffset OccurredAt);

public sealed class AuditService(OaDbContext db, DemoData data)
{
    private const string TenantId = "demo";

    public ServiceResult<PagedResponse<AuditLogView>> List(Employee actor, AuditLogQuery query)
    {
        if (!data.HasPermission(actor, OaPermissions.AuditView)) return ServiceResult<PagedResponse<AuditLogView>>.Failure("无审计日志查看权限。", "AUTH_002");
        var records = db.AuditLogs.Where(item => item.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim();
            records = records.Where(item => item.Action.Contains(keyword) || item.Summary.Contains(keyword) || item.ResourceId.Contains(keyword));
        }
        if (!string.IsNullOrWhiteSpace(query.ActorId)) records = records.Where(item => item.ActorId == query.ActorId);
        if (!string.IsNullOrWhiteSpace(query.ResourceType)) records = records.Where(item => item.ResourceType == query.ResourceType);
        if (query.StartDate is { } startDate)
        {
            var start = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
            records = records.Where(item => item.OccurredAt >= start);
        }
        if (query.EndDate is { } endDate)
        {
            var endExclusive = new DateTimeOffset(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
            records = records.Where(item => item.OccurredAt < endExclusive);
        }

        var pageSize = Math.Clamp(query.PageSize ?? 20, 1, 100);
        var total = records.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)pageSize));
        var page = Math.Clamp(query.Page ?? 1, 1, totalPages);
        var pageRecords = records.OrderByDescending(item => item.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        var items = pageRecords.Select(item => new AuditLogView(item.Id, item.ActorId, data.FindEmployee(item.ActorId)?.Name ?? item.ActorId, item.Action, item.ResourceType, item.ResourceId, item.Summary, item.OccurredAt)).ToList();
        return ServiceResult<PagedResponse<AuditLogView>>.Success(new PagedResponse<AuditLogView>(items, total, page, pageSize, totalPages));
    }
}
