namespace Oa.Api.Domain;

public sealed record AnnouncementView(
    Guid Id,
    string Title,
    string Content,
    string Status,
    int Version,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? PublishedBy,
    string? PublishedByName,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? WithdrawnAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ReadAt);

public sealed record SaveAnnouncementRequest(string Title, string Content, DateTimeOffset? ExpiresAt, int? Version = null);
