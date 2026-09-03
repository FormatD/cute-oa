using System.ComponentModel.DataAnnotations.Schema;

namespace Oa.Api.Domain;

public enum DocumentStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}

[Table("document_category")]
public class DocumentCategoryRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = "demo";
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string? DepartmentId { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("knowledge_document")]
public class KnowledgeDocumentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = "demo";
    public string Number { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string TagsJson { get; set; } = "[]";
    public int Version { get; set; } = 1;
    public int Status { get; set; } = (int)DocumentStatus.Draft;
    public bool IsMustRead { get; set; }
    public string? DepartmentId { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string AttachmentsJson { get; set; } = "[]";
    public int ViewCount { get; set; }
    public int DownloadCount { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public string? PublishedByName { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string? ArchivedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsDemo { get; set; }
}

[Table("document_version")]
public class DocumentVersionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = "demo";
    public Guid DocumentId { get; set; }
    public int Version { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ChangeNotes { get; set; }
    public string AttachmentsJson { get; set; } = "[]";
    public DateTimeOffset PublishedAt { get; set; }
    public string PublishedBy { get; set; } = string.Empty;
    public string PublishedByName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Table("document_acknowledgement")]
public class DocumentAcknowledgementRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = "demo";
    public Guid DocumentId { get; set; }
    public int DocumentVersion { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ClientIp { get; set; }
}

public sealed record DocumentCategoryView(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    string? DepartmentId,
    int SortOrder,
    int DocumentCount
);

public sealed record SaveCategoryRequest(
    string Code,
    string Name,
    string? Description,
    Guid? ParentId,
    string? DepartmentId,
    int SortOrder
);

public sealed record KnowledgeDocumentView(
    Guid Id,
    string Number,
    Guid CategoryId,
    string CategoryName,
    string Title,
    string Summary,
    string Content,
    IReadOnlyList<string> Tags,
    int Version,
    DocumentStatus Status,
    bool IsMustRead,
    string? DepartmentId,
    DateOnly EffectiveDate,
    DateOnly? ExpiryDate,
    IReadOnlyList<string> Attachments,
    int ViewCount,
    int DownloadCount,
    string CreatedBy,
    string CreatedByName,
    DateTimeOffset? PublishedAt,
    string? PublishedByName,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool HasAcknowledged,
    DateTimeOffset? AcknowledgedAt
);

public sealed record SaveDocumentRequest(
    string Title,
    Guid CategoryId,
    string Summary,
    string Content,
    IReadOnlyList<string>? Tags,
    bool IsMustRead,
    string? DepartmentId,
    DateOnly EffectiveDate,
    DateOnly? ExpiryDate,
    IReadOnlyList<string>? Attachments
);

public sealed record ReviseDocumentRequest(
    int Version,
    string Title,
    string Summary,
    string Content,
    string? ChangeNotes,
    IReadOnlyList<string>? Attachments
);

public sealed record DocumentVersionView(
    Guid Id,
    int Version,
    string Title,
    string Summary,
    string Content,
    string? ChangeNotes,
    IReadOnlyList<string> Attachments,
    DateTimeOffset PublishedAt,
    string PublishedByName
);

public sealed record DocumentAcknowledgementView(
    Guid Id,
    Guid DocumentId,
    int DocumentVersion,
    string UserId,
    string UserName,
    string? DepartmentName,
    DateTimeOffset AcknowledgedAt
);

public sealed record EmployeeSummary(
    string Id,
    string Name,
    string DepartmentName,
    string? ManagerName
);

public sealed record DocumentAcknowledgementStats(
    int TotalRequired,
    int TotalAcknowledged,
    double AcknowledgedPercentage,
    IReadOnlyList<DocumentAcknowledgementView> AcknowledgedList,
    IReadOnlyList<EmployeeSummary> PendingList
);
