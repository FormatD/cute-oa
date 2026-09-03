using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed record Notification(Guid Id, string Type, string Title, string Content, string ResourceType, string ResourceId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed class NotificationService(OaDbContext db)
{
    private const string TenantId = "demo";

    public void Create(string recipientId, string type, string title, string content, string resourceType, Guid resourceId)
        => Create(recipientId, type, title, content, resourceType, resourceId.ToString());

    public void Create(string recipientId, string type, string title, string content, string resourceType, string resourceId)
    {
        Enqueue(recipientId, type, title, content, resourceType, resourceId);
        db.SaveChanges();
    }

    public void Enqueue(string recipientId, string type, string title, string content, string resourceType, Guid resourceId)
        => Enqueue(recipientId, type, title, content, resourceType, resourceId.ToString());

    public void Enqueue(string recipientId, string type, string title, string content, string resourceType, string resourceId)
        => db.Notifications.Add(new NotificationRecord { TenantId = TenantId, RecipientId = recipientId, Type = type, Title = title, Content = content, ResourceType = resourceType, ResourceId = resourceId });

    public IReadOnlyList<Notification> List(Employee actor) => db.Notifications.Where(item => item.TenantId == TenantId && item.RecipientId == actor.Id)
        .OrderByDescending(item => item.CreatedAt).Select(item => new Notification(item.Id, item.Type, item.Title, item.Content, item.ResourceType, item.ResourceId, item.CreatedAt, item.ReadAt)).ToList();

    public ServiceResult<Notification> MarkRead(Employee actor, Guid id)
    {
        var item = db.Notifications.SingleOrDefault(notification => notification.Id == id && notification.TenantId == TenantId && notification.RecipientId == actor.Id);
        if (item is null) return ServiceResult<Notification>.Failure("通知不存在或无权限。", "DATA_001");
        item.ReadAt ??= DateTimeOffset.UtcNow;
        db.SaveChanges();
        return ServiceResult<Notification>.Success(new Notification(item.Id, item.Type, item.Title, item.Content, item.ResourceType, item.ResourceId, item.CreatedAt, item.ReadAt));
    }
}
