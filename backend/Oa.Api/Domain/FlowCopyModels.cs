namespace Oa.Api.Domain;

public sealed record FlowCopyView(
    Guid Id,
    string BusinessType,
    Guid BusinessId,
    string BusinessNumber,
    string ApplicantName,
    string Title,
    string Status,
    DateTimeOffset AvailableAt,
    DateTimeOffset? ReadAt);
