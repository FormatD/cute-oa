using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class AnnouncementService(OaDbContext db, DemoData data)
{
    private const string TenantId = IdentityDefaults.TenantId;

    public ServiceResult<PagedResponse<AnnouncementView>> ListPublished(Employee actor, int? page, int? pageSize)
    {
        var now = DateTimeOffset.UtcNow;
        var query = db.Announcements.AsNoTracking().Where(item => item.TenantId == TenantId && item.Status == "PUBLISHED" && (item.ExpiresAt == null || item.ExpiresAt >= now));
        return ServiceResult<PagedResponse<AnnouncementView>>.Success(Page(query.OrderByDescending(item => item.PublishedAt).ThenByDescending(item => item.CreatedAt), actor, page, pageSize));
    }

    public ServiceResult<AnnouncementView> Get(Employee actor, Guid id)
    {
        var record = db.Announcements.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<AnnouncementView>.Failure("公告不存在。", "DATA_001");
        var visible = record.Status == "PUBLISHED" && (record.ExpiresAt is null || record.ExpiresAt >= DateTimeOffset.UtcNow);
        if (!visible && !CanManage(actor)) return ServiceResult<AnnouncementView>.Failure("公告不存在或已失效。", "DATA_001");
        return ServiceResult<AnnouncementView>.Success(ToView(record, actor));
    }

    public ServiceResult<AnnouncementView> MarkRead(Employee actor, Guid id)
    {
        var record = db.Announcements.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id && item.Status == "PUBLISHED" && (item.ExpiresAt == null || item.ExpiresAt >= DateTimeOffset.UtcNow));
        if (record is null) return ServiceResult<AnnouncementView>.Failure("公告不存在或已失效。", "DATA_001");
        var read = db.AnnouncementReads.SingleOrDefault(item => item.AnnouncementId == id && item.UserId == actor.Id);
        if (read is null)
        {
            read = new AnnouncementReadRecord { TenantId = TenantId, AnnouncementId = id, UserId = actor.Id };
            db.AnnouncementReads.Add(read);
            db.SaveChanges();
            Audit(actor, "ANNOUNCEMENT_READ", record.Id, $"确认已读公告 {record.Title}");
        }
        return ServiceResult<AnnouncementView>.Success(ToView(record, actor));
    }

    public ServiceResult<PagedResponse<AnnouncementView>> ListAdmin(Employee actor, string? status, int? page, int? pageSize)
    {
        if (!CanManage(actor)) return ServiceResult<PagedResponse<AnnouncementView>>.Failure("无公告管理权限。", "AUTH_002");
        var query = db.Announcements.AsNoTracking().Where(item => item.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(item => item.Status == status.Trim().ToUpperInvariant());
        return ServiceResult<PagedResponse<AnnouncementView>>.Success(Page(query.OrderByDescending(item => item.CreatedAt), actor, page, pageSize));
    }

    public ServiceResult<AnnouncementView> Create(Employee actor, SaveAnnouncementRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<AnnouncementView>.Failure("无公告管理权限。", "AUTH_002");
        var validation = Validate(request.Title, request.Content, request.ExpiresAt);
        if (!validation.IsSuccess) return ServiceResult<AnnouncementView>.Failure(validation.Error!, validation.Code!);
        var record = new AnnouncementRecord { TenantId = TenantId, Title = request.Title.Trim(), Content = request.Content.Trim(), ExpiresAt = request.ExpiresAt, CreatedBy = actor.Id };
        db.Announcements.Add(record);
        db.SaveChanges();
        Audit(actor, "ANNOUNCEMENT_CREATED", record.Id, $"创建公告草稿 {record.Title}");
        return ServiceResult<AnnouncementView>.Success(ToView(record, actor));
    }

    public ServiceResult<AnnouncementView> Update(Employee actor, Guid id, SaveAnnouncementRequest request)
    {
        if (!CanManage(actor)) return ServiceResult<AnnouncementView>.Failure("无公告管理权限。", "AUTH_002");
        var record = db.Announcements.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<AnnouncementView>.Failure("公告不存在。", "DATA_001");
        if (record.Status != "DRAFT") return ServiceResult<AnnouncementView>.Failure("只有草稿公告可以编辑。", "STATE_001");
        if (request.Version is null || record.Version != request.Version) return ServiceResult<AnnouncementView>.Failure("公告已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        var validation = Validate(request.Title, request.Content, request.ExpiresAt);
        if (!validation.IsSuccess) return ServiceResult<AnnouncementView>.Failure(validation.Error!, validation.Code!);
        record.Title = request.Title.Trim();
        record.Content = request.Content.Trim();
        record.ExpiresAt = request.ExpiresAt;
        record.Version++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        db.SaveChanges();
        Audit(actor, "ANNOUNCEMENT_UPDATED", record.Id, $"更新公告草稿 {record.Title}");
        return ServiceResult<AnnouncementView>.Success(ToView(record, actor));
    }

    public ServiceResult<AnnouncementView> Publish(Employee actor, Guid id, int version)
    {
        if (!CanManage(actor)) return ServiceResult<AnnouncementView>.Failure("无公告管理权限。", "AUTH_002");
        var record = db.Announcements.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<AnnouncementView>.Failure("公告不存在。", "DATA_001");
        if (record.Version != version) return ServiceResult<AnnouncementView>.Failure("公告已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        if (record.Status != "DRAFT") return ServiceResult<AnnouncementView>.Failure("只有草稿公告可以发布。", "STATE_001");
        if (record.ExpiresAt is not null && record.ExpiresAt <= DateTimeOffset.UtcNow) return ServiceResult<AnnouncementView>.Failure("公告有效期必须晚于发布时间。", "VALIDATION_001");
        record.Status = "PUBLISHED";
        record.PublishedBy = actor.Id;
        record.PublishedAt = DateTimeOffset.UtcNow;
        record.UpdatedAt = record.PublishedAt.Value;
        record.Version++;
        db.SaveChanges();
        Audit(actor, "ANNOUNCEMENT_PUBLISHED", record.Id, $"发布公告 {record.Title}");
        return ServiceResult<AnnouncementView>.Success(ToView(record, actor));
    }

    public ServiceResult<AnnouncementView> Withdraw(Employee actor, Guid id, int version)
    {
        if (!CanManage(actor)) return ServiceResult<AnnouncementView>.Failure("无公告管理权限。", "AUTH_002");
        var record = db.Announcements.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<AnnouncementView>.Failure("公告不存在。", "DATA_001");
        if (record.Version != version) return ServiceResult<AnnouncementView>.Failure("公告已被其他操作更新，请刷新后重试。", "CONCURRENCY_001");
        if (record.Status != "PUBLISHED") return ServiceResult<AnnouncementView>.Failure("只有已发布公告可以撤回。", "STATE_001");
        record.Status = "WITHDRAWN";
        record.WithdrawnBy = actor.Id;
        record.WithdrawnAt = DateTimeOffset.UtcNow;
        record.UpdatedAt = record.WithdrawnAt.Value;
        record.Version++;
        db.SaveChanges();
        Audit(actor, "ANNOUNCEMENT_WITHDRAWN", record.Id, $"撤回公告 {record.Title}");
        return ServiceResult<AnnouncementView>.Success(ToView(record, actor));
    }

    private PagedResponse<AnnouncementView> Page(IQueryable<AnnouncementRecord> query, Employee actor, int? page, int? pageSize)
    {
        var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var total = query.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)size));
        var current = Math.Clamp(page ?? 1, 1, totalPages);
        var items = query.Skip((current - 1) * size).Take(size).ToList().Select(item => ToView(item, actor)).ToList();
        return new PagedResponse<AnnouncementView>(items, total, current, size, totalPages);
    }

    private AnnouncementView ToView(AnnouncementRecord record, Employee actor)
    {
        var names = db.Users.AsNoTracking().Where(item => item.Id == record.CreatedBy || item.Id == record.PublishedBy).ToDictionary(item => item.Id, item => item.Name);
        var readAt = db.AnnouncementReads.AsNoTracking().Where(item => item.AnnouncementId == record.Id && item.UserId == actor.Id).Select(item => (DateTimeOffset?)item.ReadAt).SingleOrDefault();
        return new AnnouncementView(record.Id, record.Title, record.Content, record.Status, record.Version, record.CreatedBy, names.GetValueOrDefault(record.CreatedBy, record.CreatedBy), record.CreatedAt, record.UpdatedAt, record.PublishedBy, record.PublishedBy is null ? null : names.GetValueOrDefault(record.PublishedBy, record.PublishedBy), record.PublishedAt, record.WithdrawnAt, record.ExpiresAt, readAt);
    }

    private static ServiceResult<bool> Validate(string title, string content, DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200) return ServiceResult<bool>.Failure("公告标题应为 1–200 个字符。", "VALIDATION_001");
        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 10000) return ServiceResult<bool>.Failure("公告正文应为 1–10000 个字符。", "VALIDATION_001");
        if (expiresAt is not null && expiresAt <= DateTimeOffset.UtcNow) return ServiceResult<bool>.Failure("公告有效期必须晚于当前时间。", "VALIDATION_001");
        return ServiceResult<bool>.Success(true);
    }

    private bool CanManage(Employee actor) => data.HasPermission(actor, OaPermissions.AnnouncementManage);
    private void Audit(Employee actor, string action, Guid id, string summary)
    {
        db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "Announcement", ResourceId = id.ToString(), Summary = summary });
        db.SaveChanges();
    }
}
