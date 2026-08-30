namespace Oa.Api.Domain;

public sealed record SessionView(
    Guid Id,
    string Device,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    bool IsCurrent);
