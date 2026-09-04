using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class KnowledgeDocumentService
{
    private const string TenantId = "demo";
    private readonly DemoData _data;
    private readonly OaDbContext _db;
    private readonly NotificationService _notifications;
    private readonly FileService? _files;

    public KnowledgeDocumentService(
        DemoData data,
        OaDbContext db,
        NotificationService? notifications = null,
        FileService? files = null)
    {
        _data = data;
        _db = db;
        _notifications = notifications ?? new NotificationService(db);
        _files = files;
    }

    // -------------------------------------------------------------
    // Category Management
    // -------------------------------------------------------------

    public ServiceResult<IReadOnlyList<DocumentCategoryView>> ListCategories(Employee actor)
    {
        var hasGlobalManage = _data.HasPermission(actor, OaPermissions.DocumentManage) && _data.EffectiveDataScope(actor, "Document") == OaDataScopes.Company;
        var categories = _db.DocumentCategories.AsNoTracking()
            .Where(item => item.TenantId == TenantId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name)
            .ToList();

        var visible = categories.Where(c => hasGlobalManage || _data.CanAccessDepartmentDocument(actor, c.DepartmentId)).ToList();
        var allDocs = _db.KnowledgeDocuments.AsNoTracking()
            .Where(item => item.TenantId == TenantId)
            .ToList();

        var authorizedDocs = allDocs.Where(d =>
        {
            if (!_data.CanAccessDepartmentDocument(actor, d.DepartmentId)) return false;
            if (d.Status == (int)DocumentStatus.Published) return true;
            return _data.CanManageDepartmentDocument(actor, d.DepartmentId);
        });

        var counts = authorizedDocs
            .GroupBy(item => item.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionary(k => k.CategoryId, v => v.Count);

        var result = visible.Select(c => new DocumentCategoryView(
            c.Id,
            c.Code,
            c.Name,
            c.Description,
            c.ParentId,
            c.DepartmentId,
            c.SortOrder,
            counts.GetValueOrDefault(c.Id, 0)
        )).ToList();

        return ServiceResult<IReadOnlyList<DocumentCategoryView>>.Success(result);
    }

    public ServiceResult<DocumentCategoryView> SaveCategory(Employee actor, SaveCategoryRequest request, Guid? id = null)
    {
        var deptId = string.IsNullOrWhiteSpace(request.DepartmentId) ? null : request.DepartmentId.Trim();
        if (!_data.CanManageDepartmentDocument(actor, deptId))
            return ServiceResult<DocumentCategoryView>.Failure(deptId is null ? "仅全公司管理员可创建公司通用分类。" : "无权为其他部门创建分类。", "AUTH_002");

        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 64)
            return ServiceResult<DocumentCategoryView>.Failure("分类编码应为 1–64 个字符。", "DOC_001");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            return ServiceResult<DocumentCategoryView>.Failure("分类名称应为 1–100 个字符。", "DOC_001");

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var desc = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        DocumentCategoryRecord record;
        if (id.HasValue && id.Value != Guid.Empty)
        {
            record = _db.DocumentCategories.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id.Value)!;
            if (record is null) return ServiceResult<DocumentCategoryView>.Failure("分类不存在。", "DOC_002");
            if (!_data.CanManageDepartmentDocument(actor, record.DepartmentId) || !_data.CanManageDepartmentDocument(actor, deptId))
                return ServiceResult<DocumentCategoryView>.Failure("无权维护该分类所属部门。", "AUTH_002");

            record.Name = name;
            record.Description = desc;
            record.ParentId = request.ParentId;
            record.DepartmentId = deptId;
            record.SortOrder = request.SortOrder;
            record.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            if (_db.DocumentCategories.Any(item => item.TenantId == TenantId && item.Code == code))
                return ServiceResult<DocumentCategoryView>.Failure("分类编码已存在。", "DUPLICATE_001");

            record = new DocumentCategoryRecord
            {
                TenantId = TenantId,
                Code = code,
                Name = name,
                Description = desc,
                ParentId = request.ParentId,
                DepartmentId = deptId,
                SortOrder = request.SortOrder,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.DocumentCategories.Add(record);
        }

        try
        {
            _db.SaveChanges();
            Audit(actor, id.HasValue ? "DOCUMENT_CATEGORY_UPDATED" : "DOCUMENT_CATEGORY_CREATED", record.Id, $"维护文档分类：{record.Name}（{record.Code}）");
            return ServiceResult<DocumentCategoryView>.Success(new DocumentCategoryView(record.Id, record.Code, record.Name, record.Description, record.ParentId, record.DepartmentId, record.SortOrder, 0));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ServiceResult<DocumentCategoryView>.Failure("分类编码重复。", "DUPLICATE_001");
        }
    }

    public ServiceResult<bool> DeleteCategory(Employee actor, Guid id)
    {
        var record = _db.DocumentCategories.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<bool>.Failure("分类不存在。", "DOC_002");

        if (!_data.CanManageDepartmentDocument(actor, record.DepartmentId))
            return ServiceResult<bool>.Failure("无权删除该部门分类。", "AUTH_002");

        if (_db.KnowledgeDocuments.Any(item => item.TenantId == TenantId && item.CategoryId == id))
            return ServiceResult<bool>.Failure("该分类下存在文档，不能删除。", "DOC_002");

        _db.DocumentCategories.Remove(record);
        _db.SaveChanges();
        Audit(actor, "DOCUMENT_CATEGORY_DELETED", id, $"删除文档分类：{record.Name}（{record.Code}）");
        return ServiceResult<bool>.Success(true);
    }

    // -------------------------------------------------------------
    // Document Management
    // -------------------------------------------------------------

    public ServiceResult<PagedResponse<KnowledgeDocumentView>> ListDocuments(
        Employee actor,
        string? keyword = null,
        Guid? categoryId = null,
        string? tag = null,
        DocumentStatus? status = null,
        bool? mustReadOnly = null,
        bool? pendingAckOnly = null,
        int page = 1,
        int pageSize = 20)
    {
        var hasGlobalManage = _data.HasPermission(actor, OaPermissions.DocumentManage) && _data.EffectiveDataScope(actor, "Document") == OaDataScopes.Company;
        var query = _db.KnowledgeDocuments.AsNoTracking().Where(item => item.TenantId == TenantId);

        if (!hasGlobalManage)
        {
            var actorDept = actor.DepartmentId;
            var canManageOwnDept = _data.CanManageDepartmentDocument(actor, actorDept);
            if (canManageOwnDept)
            {
                if (status.HasValue)
                {
                    query = query.Where(item =>
                        (item.DepartmentId == actorDept && item.Status == (int)status.Value) ||
                        (item.DepartmentId == null && item.Status == (int)DocumentStatus.Published && (int)status.Value == (int)DocumentStatus.Published));
                }
                else
                {
                    query = query.Where(item =>
                        item.DepartmentId == actorDept ||
                        (item.DepartmentId == null && item.Status == (int)DocumentStatus.Published));
                }
            }
            else
            {
                query = query.Where(item => item.Status == (int)DocumentStatus.Published
                    && (item.DepartmentId == null || item.DepartmentId == actorDept));
            }
        }
        else if (status.HasValue)
        {
            query = query.Where(item => item.Status == (int)status.Value);
        }

        if (categoryId.HasValue && categoryId.Value != Guid.Empty)
        {
            query = query.Where(item => item.CategoryId == categoryId.Value);
        }

        if (mustReadOnly == true)
        {
            query = query.Where(item => item.IsMustRead);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim().ToLowerInvariant();
            query = query.Where(item => item.Title.ToLower().Contains(k)
                || item.Number.ToLower().Contains(k)
                || item.Summary.ToLower().Contains(k)
                || item.TagsJson.ToLower().Contains(k));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = tag.Trim();
            query = query.Where(item => item.TagsJson.Contains(t));
        }

        // Pending acknowledgement filter for actor
        if (pendingAckOnly == true)
        {
            var acknowledgedDocIds = _db.DocumentAcknowledgements.AsNoTracking()
                .Where(a => a.TenantId == TenantId && a.UserId == actor.Id)
                .Select(a => a.DocumentId)
                .ToHashSet();
            query = query.Where(item => item.IsMustRead && item.Status == (int)DocumentStatus.Published && !acknowledgedDocIds.Contains(item.Id));
            if (!hasGlobalManage)
            {
                query = query.Where(item => item.DepartmentId == null || item.DepartmentId == actor.DepartmentId);
            }
        }

        var total = query.Count();
        var ps = Math.Clamp(pageSize, 1, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)ps));
        var p = Math.Clamp(page, 1, totalPages);

        var categories = _db.DocumentCategories.AsNoTracking()
            .Where(item => item.TenantId == TenantId)
            .ToDictionary(k => k.Id, v => v.Name);

        var acks = _db.DocumentAcknowledgements.AsNoTracking()
            .Where(a => a.TenantId == TenantId && a.UserId == actor.Id)
            .ToList()
            .GroupBy(a => a.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.AcknowledgedAt).First());

        var items = query
            .OrderByDescending(item => item.PublishedAt ?? item.CreatedAt)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToList();

        var views = items.Select(doc =>
        {
            var ack = acks.GetValueOrDefault(doc.Id);
            return ToView(doc, categories.GetValueOrDefault(doc.CategoryId, "未分类"), ack?.DocumentVersion == doc.Version, ack?.AcknowledgedAt);
        }).ToList();

        return ServiceResult<PagedResponse<KnowledgeDocumentView>>.Success(new PagedResponse<KnowledgeDocumentView>(views, total, p, ps, totalPages));
    }

    public ServiceResult<KnowledgeDocumentView> GetDocument(Employee actor, Guid id)
    {
        var doc = _db.KnowledgeDocuments.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (doc is null) return ServiceResult<KnowledgeDocumentView>.Failure("文档不存在。", "DOC_003");

        var canAccess = _data.CanAccessDepartmentDocument(actor, doc.DepartmentId);
        if (!canAccess)
            return ServiceResult<KnowledgeDocumentView>.Failure("文档仅限指定部门查阅。", "DOC_003");

        var canManage = _data.CanManageDepartmentDocument(actor, doc.DepartmentId);
        if (!canManage && doc.Status != (int)DocumentStatus.Published)
            return ServiceResult<KnowledgeDocumentView>.Failure("文档未发布或已归档。", "DOC_003");

        // Increment view count
        doc.ViewCount++;
        _db.SaveChanges();

        var categoryName = _db.DocumentCategories.AsNoTracking().Where(c => c.Id == doc.CategoryId).Select(c => c.Name).FirstOrDefault() ?? "未分类";
        var ack = _db.DocumentAcknowledgements.AsNoTracking()
            .Where(a => a.TenantId == TenantId && a.DocumentId == doc.Id && a.UserId == actor.Id)
            .OrderByDescending(a => a.AcknowledgedAt)
            .FirstOrDefault();

        return ServiceResult<KnowledgeDocumentView>.Success(ToView(doc, categoryName, ack?.DocumentVersion == doc.Version, ack?.AcknowledgedAt));
    }

    public ServiceResult<KnowledgeDocumentView> CreateDraft(Employee actor, SaveDocumentRequest request)
    {
        var deptId = string.IsNullOrWhiteSpace(request.DepartmentId) ? null : request.DepartmentId.Trim();
        if (!_data.CanManageDepartmentDocument(actor, deptId))
            return ServiceResult<KnowledgeDocumentView>.Failure(
                deptId is null ? "仅全公司管理员可编制公司级通用制度。" : "无权为其他部门编制制度文档。", "AUTH_002");

        var validation = ValidateInput(request);
        if (!validation.IsSuccess) return ServiceResult<KnowledgeDocumentView>.Failure(validation.Error!, validation.Code ?? "DOC_001");

        var category = _db.DocumentCategories.SingleOrDefault(c => c.TenantId == TenantId && c.Id == request.CategoryId);
        if (category is null) return ServiceResult<KnowledgeDocumentView>.Failure("指定分类不存在。", "DOC_002");
        if (category.DepartmentId != null && category.DepartmentId != deptId)
            return ServiceResult<KnowledgeDocumentView>.Failure("文档所属部门必须与分类部门一致。", "DOC_001");

        var attachments = NormalizeAttachments(actor, request.Attachments);
        if (attachments is null) return ServiceResult<KnowledgeDocumentView>.Failure("包含无效或越权附件。", "FILE_005");

        var now = DateTimeOffset.UtcNow;
        var number = GenerateNumber();
        var tags = (request.Tags ?? []).Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();

        var record = new KnowledgeDocumentRecord
        {
            TenantId = TenantId,
            Number = number,
            CategoryId = request.CategoryId,
            Title = request.Title.Trim(),
            Summary = request.Summary.Trim(),
            Content = request.Content.Trim(),
            TagsJson = JsonSerializer.Serialize(tags),
            Version = 1,
            Status = (int)DocumentStatus.Draft,
            IsMustRead = request.IsMustRead,
            DepartmentId = deptId,
            EffectiveDate = request.EffectiveDate,
            ExpiryDate = request.ExpiryDate,
            AttachmentsJson = JsonSerializer.Serialize(attachments),
            CreatedBy = actor.Id,
            CreatedByName = actor.Name,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.KnowledgeDocuments.Add(record);
        _db.SaveChanges();
        Audit(actor, "DOCUMENT_CREATED", record.Id, $"编制制度草稿：{record.Title}（{record.Number}）");

        return ServiceResult<KnowledgeDocumentView>.Success(ToView(record, category.Name, false, null));
    }

    public ServiceResult<KnowledgeDocumentView> UpdateDraft(Employee actor, Guid id, SaveDocumentRequest request)
    {
        var record = _db.KnowledgeDocuments.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<KnowledgeDocumentView>.Failure("文档不存在。", "DOC_003");
        if (record.Status != (int)DocumentStatus.Draft)
            return ServiceResult<KnowledgeDocumentView>.Failure("仅草稿状态文档可直接编辑，已发布文档请执行修订新版本。", "DOC_004");

        var newDeptId = string.IsNullOrWhiteSpace(request.DepartmentId) ? null : request.DepartmentId.Trim();
        if (!_data.CanManageDepartmentDocument(actor, record.DepartmentId) || !_data.CanManageDepartmentDocument(actor, newDeptId))
            return ServiceResult<KnowledgeDocumentView>.Failure("无权维护该部门的文档。", "AUTH_002");

        var validation = ValidateInput(request);
        if (!validation.IsSuccess) return ServiceResult<KnowledgeDocumentView>.Failure(validation.Error!, validation.Code ?? "DOC_001");

        var category = _db.DocumentCategories.SingleOrDefault(c => c.TenantId == TenantId && c.Id == request.CategoryId);
        if (category is null) return ServiceResult<KnowledgeDocumentView>.Failure("指定分类不存在。", "DOC_002");
        if (category.DepartmentId != null && category.DepartmentId != newDeptId)
            return ServiceResult<KnowledgeDocumentView>.Failure("文档所属部门必须与分类部门一致。", "DOC_001");

        var attachments = NormalizeAttachments(actor, request.Attachments);
        if (attachments is null) return ServiceResult<KnowledgeDocumentView>.Failure("包含无效或越权附件。", "FILE_005");

        var tags = (request.Tags ?? []).Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();

        record.CategoryId = request.CategoryId;
        record.Title = request.Title.Trim();
        record.Summary = request.Summary.Trim();
        record.Content = request.Content.Trim();
        record.TagsJson = JsonSerializer.Serialize(tags);
        record.IsMustRead = request.IsMustRead;
        record.DepartmentId = newDeptId;
        record.EffectiveDate = request.EffectiveDate;
        record.ExpiryDate = request.ExpiryDate;
        record.AttachmentsJson = JsonSerializer.Serialize(attachments);
        record.UpdatedAt = DateTimeOffset.UtcNow;

        _db.SaveChanges();
        Audit(actor, "DOCUMENT_UPDATED", record.Id, $"更新制度草稿：{record.Title}（{record.Number}）");

        return ServiceResult<KnowledgeDocumentView>.Success(ToView(record, category.Name, false, null));
    }

    public ServiceResult<bool> DeleteDraft(Employee actor, Guid id)
    {
        var record = _db.KnowledgeDocuments.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<bool>.Failure("文档不存在。", "DOC_003");
        if (record.Status != (int)DocumentStatus.Draft)
            return ServiceResult<bool>.Failure("仅草稿状态文档可删除。", "DOC_004");

        if (!_data.CanManageDepartmentDocument(actor, record.DepartmentId))
            return ServiceResult<bool>.Failure("无权删除该部门的文档。", "AUTH_002");

        _db.KnowledgeDocuments.Remove(record);
        _db.SaveChanges();
        Audit(actor, "DOCUMENT_DELETED", id, $"删除制度草稿：{record.Title}（{record.Number}）");
        return ServiceResult<bool>.Success(true);
    }

    public ServiceResult<KnowledgeDocumentView> PublishDocument(Employee actor, Guid id)
    {
        var record = _db.KnowledgeDocuments.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<KnowledgeDocumentView>.Failure("文档不存在。", "DOC_003");
        if (record.Status != (int)DocumentStatus.Draft)
            return ServiceResult<KnowledgeDocumentView>.Failure("仅草稿状态文档可执行发布。", "DOC_004");

        if (!_data.CanManageDepartmentDocument(actor, record.DepartmentId))
            return ServiceResult<KnowledgeDocumentView>.Failure("无权发布该部门的文档。", "AUTH_002");

        var now = DateTimeOffset.UtcNow;
        record.Status = (int)DocumentStatus.Published;
        record.PublishedAt = now;
        record.PublishedBy = actor.Id;
        record.PublishedByName = actor.Name;
        record.UpdatedAt = now;

        // Archive version 1
        _db.DocumentVersions.Add(new DocumentVersionRecord
        {
            TenantId = TenantId,
            DocumentId = record.Id,
            Version = record.Version,
            Title = record.Title,
            Summary = record.Summary,
            Content = record.Content,
            ChangeNotes = "首发版本发布",
            AttachmentsJson = record.AttachmentsJson,
            PublishedAt = now,
            PublishedBy = actor.Id,
            PublishedByName = actor.Name,
            CreatedAt = now
        });

        // If must read, enqueue notification for applicable active employees
        if (record.IsMustRead)
        {
            var targetEmployees = _data.ActiveEmployees
                .Where(e => record.DepartmentId == null || e.DepartmentId == record.DepartmentId)
                .ToList();

            foreach (var emp in targetEmployees)
            {
                _notifications.Enqueue(emp.Id, "DOCUMENT_MUST_READ", $"必读制度通知：《{record.Title}》已发布", $"请您仔细阅读并完成在线签署确认", "KnowledgeDocument", record.Id);
            }
        }

        _db.SaveChanges();
        Audit(actor, "DOCUMENT_PUBLISHED", record.Id, $"正式发布制度文档：{record.Title}（{record.Number} v{record.Version}）");

        var categoryName = _db.DocumentCategories.AsNoTracking().Where(c => c.Id == record.CategoryId).Select(c => c.Name).FirstOrDefault() ?? "未分类";
        return ServiceResult<KnowledgeDocumentView>.Success(ToView(record, categoryName, false, null));
    }

    public ServiceResult<KnowledgeDocumentView> ReviseDocument(Employee actor, Guid id, ReviseDocumentRequest request)
    {
        var record = _db.KnowledgeDocuments.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<KnowledgeDocumentView>.Failure("文档不存在。", "DOC_003");
        if (record.Status != (int)DocumentStatus.Published)
            return ServiceResult<KnowledgeDocumentView>.Failure("仅已发布文档可执行版本修订。", "DOC_004");
        if (record.Version != request.Version)
            return ServiceResult<KnowledgeDocumentView>.Failure("文档已被其他人更新，请刷新重试。", "CONCURRENCY_001");

        if (!_data.CanManageDepartmentDocument(actor, record.DepartmentId))
            return ServiceResult<KnowledgeDocumentView>.Failure("无权修订该部门的文档。", "AUTH_002");

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 100)
            return ServiceResult<KnowledgeDocumentView>.Failure("文档标题应为 1–100 个字符。", "DOC_001");
        if (string.IsNullOrWhiteSpace(request.Summary) || request.Summary.Trim().Length > 300)
            return ServiceResult<KnowledgeDocumentView>.Failure("文档摘要应为 1–300 个字符。", "DOC_001");
        if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Trim().Length > 20000)
            return ServiceResult<KnowledgeDocumentView>.Failure("文档正文应为 1–20000 个字符。", "DOC_001");

        var attachments = NormalizeAttachments(actor, request.Attachments);
        if (attachments is null) return ServiceResult<KnowledgeDocumentView>.Failure("包含无效或越权附件。", "FILE_005");

        var now = DateTimeOffset.UtcNow;
        record.Version++;
        record.Title = request.Title.Trim();
        record.Summary = request.Summary.Trim();
        record.Content = request.Content.Trim();
        record.AttachmentsJson = JsonSerializer.Serialize(attachments);
        record.UpdatedAt = now;
        record.PublishedAt = now;
        record.PublishedBy = actor.Id;
        record.PublishedByName = actor.Name;

        // Record new version
        _db.DocumentVersions.Add(new DocumentVersionRecord
        {
            TenantId = TenantId,
            DocumentId = record.Id,
            Version = record.Version,
            Title = record.Title,
            Summary = record.Summary,
            Content = record.Content,
            ChangeNotes = string.IsNullOrWhiteSpace(request.ChangeNotes) ? $"修订升级为 v{record.Version}.0" : request.ChangeNotes.Trim(),
            AttachmentsJson = record.AttachmentsJson,
            PublishedAt = now,
            PublishedBy = actor.Id,
            PublishedByName = actor.Name,
            CreatedAt = now
        });

        if (record.IsMustRead)
        {
            var targetEmployees = _data.ActiveEmployees
                .Where(e => record.DepartmentId == null || e.DepartmentId == record.DepartmentId)
                .ToList();

            foreach (var emp in targetEmployees)
            {
                _notifications.Enqueue(emp.Id, "DOCUMENT_REVISED", $"制度修订通知：《{record.Title}》已发布 v{record.Version} 新版本", $"制度内容有更新，请查看修订说明并重新签收确认", "KnowledgeDocument", record.Id);
            }
        }

        _db.SaveChanges();
        Audit(actor, "DOCUMENT_REVISED", record.Id, $"修订制度文档：{record.Title}（{record.Number} 升至 v{record.Version}）");

        var categoryName = _db.DocumentCategories.AsNoTracking().Where(c => c.Id == record.CategoryId).Select(c => c.Name).FirstOrDefault() ?? "未分类";
        return ServiceResult<KnowledgeDocumentView>.Success(ToView(record, categoryName, false, null));
    }

    public ServiceResult<bool> ArchiveDocument(Employee actor, Guid id)
    {
        var record = _db.KnowledgeDocuments.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<bool>.Failure("文档不存在。", "DOC_003");
        if (record.Status != (int)DocumentStatus.Published)
            return ServiceResult<bool>.Failure("仅已发布文档可归档。", "DOC_004");

        if (!_data.CanManageDepartmentDocument(actor, record.DepartmentId))
            return ServiceResult<bool>.Failure("无权归档该部门的文档。", "AUTH_002");

        record.Status = (int)DocumentStatus.Archived;
        record.ArchivedAt = DateTimeOffset.UtcNow;
        record.ArchivedBy = actor.Id;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        _db.SaveChanges();
        Audit(actor, "DOCUMENT_ARCHIVED", id, $"归档下线制度文档：{record.Title}（{record.Number}）");
        return ServiceResult<bool>.Success(true);
    }

    // -------------------------------------------------------------
    // Acknowledgement & Sign-off
    // -------------------------------------------------------------

    public ServiceResult<DocumentAcknowledgementView> AcknowledgeDocument(Employee actor, Guid id, string? clientIp = null)
    {
        var record = _db.KnowledgeDocuments.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<DocumentAcknowledgementView>.Failure("文档不存在。", "DOC_003");
        if (record.Status != (int)DocumentStatus.Published)
            return ServiceResult<DocumentAcknowledgementView>.Failure("仅已发布制度支持阅读确认。", "DOC_004");
        if (!record.IsMustRead)
            return ServiceResult<DocumentAcknowledgementView>.Failure("该文档未要求强制阅读确认。", "DOC_004");

        if (record.DepartmentId != null && !_data.CanAccessDepartmentDocument(actor, record.DepartmentId))
            return ServiceResult<DocumentAcknowledgementView>.Failure("您不在该制度的适用部门范围内。", "AUTH_002");

        // Check if already acknowledged for this exact version
        var existing = _db.DocumentAcknowledgements.SingleOrDefault(a =>
            a.TenantId == TenantId &&
            a.DocumentId == record.Id &&
            a.DocumentVersion == record.Version &&
            a.UserId == actor.Id);

        if (existing is not null)
        {
            return ServiceResult<DocumentAcknowledgementView>.Success(new DocumentAcknowledgementView(
                existing.Id, existing.DocumentId, existing.DocumentVersion, existing.UserId, existing.UserName, existing.DepartmentName, existing.AcknowledgedAt));
        }

        var ack = new DocumentAcknowledgementRecord
        {
            TenantId = TenantId,
            DocumentId = record.Id,
            DocumentVersion = record.Version,
            UserId = actor.Id,
            UserName = actor.Name,
            DepartmentId = actor.DepartmentId,
            DepartmentName = actor.DepartmentName,
            AcknowledgedAt = DateTimeOffset.UtcNow,
            ClientIp = clientIp?.Trim()
        };

        _db.DocumentAcknowledgements.Add(ack);
        _db.SaveChanges();
        Audit(actor, "DOCUMENT_ACKNOWLEDGED", record.Id, $"签署确认遵守制度：《{record.Title}》v{record.Version}");

        return ServiceResult<DocumentAcknowledgementView>.Success(new DocumentAcknowledgementView(
            ack.Id, ack.DocumentId, ack.DocumentVersion, ack.UserId, ack.UserName, ack.DepartmentName, ack.AcknowledgedAt));
    }

    public ServiceResult<DocumentAcknowledgementStats> GetAcknowledgementStats(Employee actor, Guid id)
    {
        var record = _db.KnowledgeDocuments.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<DocumentAcknowledgementStats>.Failure("文档不存在。", "DOC_003");

        if (!_data.CanManageDepartmentDocument(actor, record.DepartmentId))
            return ServiceResult<DocumentAcknowledgementStats>.Failure("无权查看该部门文档的签署统计。", "AUTH_002");

        var targetEmployees = _data.ActiveEmployees
            .Where(e => record.DepartmentId == null || e.DepartmentId == record.DepartmentId)
            .ToList();

        var acks = _db.DocumentAcknowledgements.AsNoTracking()
            .Where(a => a.TenantId == TenantId && a.DocumentId == record.Id && a.DocumentVersion == record.Version)
            .OrderByDescending(a => a.AcknowledgedAt)
            .ToList();

        var ackUserIds = acks.Select(a => a.UserId).ToHashSet();
        var pendingEmployees = targetEmployees
            .Where(e => !ackUserIds.Contains(e.Id))
            .Select(e => new EmployeeSummary(e.Id, e.Name, e.DepartmentName, e.ManagerId is null ? null : _data.FindEmployee(e.ManagerId)?.Name))
            .ToList();

        var ackViews = acks.Select(a => new DocumentAcknowledgementView(
            a.Id, a.DocumentId, a.DocumentVersion, a.UserId, a.UserName, a.DepartmentName, a.AcknowledgedAt
        )).ToList();

        var totalReq = targetEmployees.Count;
        var totalAck = acks.Count;
        var pct = totalReq == 0 ? 100.0 : Math.Round((double)totalAck / totalReq * 100, 1);

        return ServiceResult<DocumentAcknowledgementStats>.Success(new DocumentAcknowledgementStats(
            totalReq, totalAck, pct, ackViews, pendingEmployees
        ));
    }

    public ServiceResult<IReadOnlyList<DocumentVersionView>> ListVersions(Employee actor, Guid id)
    {
        var record = _db.KnowledgeDocuments.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null) return ServiceResult<IReadOnlyList<DocumentVersionView>>.Failure("文档不存在。", "DOC_003");

        if (!_data.CanAccessDepartmentDocument(actor, record.DepartmentId))
            return ServiceResult<IReadOnlyList<DocumentVersionView>>.Failure("文档不存在或无权访问。", "DOC_003");

        var canManage = _data.CanManageDepartmentDocument(actor, record.DepartmentId);
        if (!canManage && record.Status != (int)DocumentStatus.Published && record.Status != (int)DocumentStatus.Archived)
            return ServiceResult<IReadOnlyList<DocumentVersionView>>.Failure("无权查看版本历史。", "DOC_003");

        var versions = _db.DocumentVersions.AsNoTracking()
            .Where(v => v.TenantId == TenantId && v.DocumentId == id)
            .OrderByDescending(v => v.Version)
            .ToList();

        var views = versions.Select(v => new DocumentVersionView(
            v.Id,
            v.Version,
            v.Title,
            v.Summary,
            v.Content,
            v.ChangeNotes,
            DeserializeList(v.AttachmentsJson),
            v.PublishedAt,
            v.PublishedByName
        )).ToList();

        return ServiceResult<IReadOnlyList<DocumentVersionView>>.Success(views);
    }

    // -------------------------------------------------------------
    // Attachment Access & Download Record
    // -------------------------------------------------------------

    public bool CanAccessAttachment(Employee actor, Guid documentId, Guid fileId)
    {
        var record = _db.KnowledgeDocuments.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == documentId);
        if (record is null) return false;

        if (!_data.CanAccessDepartmentDocument(actor, record.DepartmentId))
            return false;

        var canManage = _data.CanManageDepartmentDocument(actor, record.DepartmentId);
        if (!canManage && record.Status != (int)DocumentStatus.Published)
            return false;

        var files = DeserializeList(record.AttachmentsJson);
        if (files.Contains(fileId.ToString()))
        {
            record.DownloadCount++;
            _db.SaveChanges();
            Audit(actor, "DOCUMENT_ATTACHMENT_DOWNLOADED", documentId, $"下载制度附件：{fileId}（{record.Title}）");
            return true;
        }

        // Check version archives
        var versionHasFile = _db.DocumentVersions.AsNoTracking()
            .Where(v => v.TenantId == TenantId && v.DocumentId == documentId)
            .Any(v => v.AttachmentsJson.Contains(fileId.ToString()));

        if (versionHasFile)
        {
            record.DownloadCount++;
            _db.SaveChanges();
            Audit(actor, "DOCUMENT_ATTACHMENT_DOWNLOADED", documentId, $"下载历史版本制度附件：{fileId}（{record.Title}）");
            return true;
        }

        return false;
    }

    // -------------------------------------------------------------
    // Demo Data Generation
    // -------------------------------------------------------------

    public ServiceResult<object> GenerateDemoData(Employee actor)
    {
        if (!_data.HasPermission(actor, OaPermissions.DocumentManage))
            return ServiceResult<object>.Failure("只有具备知识库管理权限的人员可以生成示例数据。", "AUTH_002");

        var created = 0;
        var skipped = 0;

        // Categories
        var catHr = GetOrCreateCategory("HR_POLICY", "人事制度与合规", "入转调离、考勤休假、员工守则与用工合规制度", null, 10);
        var catFin = GetOrCreateCategory("FIN_POLICY", "财务规范与报销", "费用报销、差旅标准、借款与发票入账规范", null, 20);
        var catTech = GetOrCreateCategory("TECH_GUIDE", "技术与研发指引", "研发规范、架构设计原则、信息安全与代码保密指引", "engineering", 30);
        var catTemplate = GetOrCreateCategory("TEMPLATES", "常用办公模板", "各类请示公文、常用审批单据及标准合同范本", null, 40);

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Doc 1: 员工手册与合规守则 (Must-Read, all staff)
        if (!_db.KnowledgeDocuments.Any(d => d.TenantId == TenantId && d.Number == "DOC-DEMO-001"))
        {
            var doc1 = new KnowledgeDocumentRecord
            {
                TenantId = TenantId,
                Number = "DOC-DEMO-001",
                CategoryId = catHr.Id,
                Title = "员工手册与行为合规守则（2026版）",
                Summary = "本手册详细规定了工作作息、请假考勤、保密守则、反舞弊准则及日常办公行为规范，是企业用工治理的核心制度。",
                Content = @"### 第一章 总则与企业价值观
全体员工应当秉持诚信、专业、协作、创新的价值观，严格遵守国家法律法规及本公司各项规章制度。

### 第二章 工作作息与考勤纪律
1. **工作时间**：每周一至周五 09:00 至 18:00，午休 12:00 至 13:30。
2. **打卡规范**：每日须通过考勤系统完成上下班打卡，迟到或早退超过 30 分钟按异常处理并须发起申诉。
3. **请假流程**：事假、病假、调休和年假均须提前在 OA 请假模块提交审批，批准后方可休假。

### 第三章 商业秘密与信息安全
1. 不得以任何形式向第三方泄露公司的商业数据、财务报表、客户名录或代码资产。
2. 离职时须完整交接公司资产及文档，继续履行保密协议规定的保密义务。

### 第四章 签署确认效力
本制度经职工代表大会审议通过，自发布之日起对全体在职员工生效。员工在线签署确认具有法律效力，表明已完整知晓并承诺遵守。",
                TagsJson = JsonSerializer.Serialize(new[] { "员工手册", "合规", "考勤", "全员必读" }),
                Version = 1,
                Status = (int)DocumentStatus.Published,
                IsMustRead = true,
                DepartmentId = null,
                EffectiveDate = today.AddDays(-30),
                ExpiryDate = null,
                AttachmentsJson = "[]",
                ViewCount = 42,
                DownloadCount = 18,
                CreatedBy = actor.Id,
                CreatedByName = actor.Name,
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-30),
                PublishedBy = actor.Id,
                PublishedByName = actor.Name,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                IsDemo = true
            };
            _db.KnowledgeDocuments.Add(doc1);
            _db.DocumentVersions.Add(new DocumentVersionRecord
            {
                TenantId = TenantId,
                DocumentId = doc1.Id,
                Version = 1,
                Title = doc1.Title,
                Summary = doc1.Summary,
                Content = doc1.Content,
                ChangeNotes = "2026年职工大会审议发布版本",
                AttachmentsJson = "[]",
                PublishedAt = doc1.PublishedAt!.Value,
                PublishedBy = actor.Id,
                PublishedByName = actor.Name,
                CreatedAt = doc1.CreatedAt
            });

            // Seed partial acknowledgements: u-li (engineering mgr), u-chen (finance) acknowledged
            _db.DocumentAcknowledgements.Add(new DocumentAcknowledgementRecord
            {
                TenantId = TenantId,
                DocumentId = doc1.Id,
                DocumentVersion = 1,
                UserId = "u-li",
                UserName = "李薇",
                DepartmentId = "engineering",
                DepartmentName = "研发部",
                AcknowledgedAt = DateTimeOffset.UtcNow.AddDays(-28),
                ClientIp = "127.0.0.1"
            });
            _db.DocumentAcknowledgements.Add(new DocumentAcknowledgementRecord
            {
                TenantId = TenantId,
                DocumentId = doc1.Id,
                DocumentVersion = 1,
                UserId = "u-chen",
                UserName = "陈敏",
                DepartmentId = "finance",
                DepartmentName = "财务部",
                AcknowledgedAt = DateTimeOffset.UtcNow.AddDays(-25),
                ClientIp = "127.0.0.1"
            });
            created++;
        }
        else skipped++;

        // Doc 2: 差旅标准与费用报销管理办法 (All staff)
        if (!_db.KnowledgeDocuments.Any(d => d.TenantId == TenantId && d.Number == "DOC-DEMO-002"))
        {
            var doc2 = new KnowledgeDocumentRecord
            {
                TenantId = TenantId,
                Number = "DOC-DEMO-002",
                CategoryId = catFin.Id,
                Title = "差旅标准与费用报销管理实施细则",
                Summary = "规范全公司员工差旅交通、住宿限额标准、报销审批时限及发票审核报送要求。",
                Content = @"### 一、差旅住宿与交通标准
| 城市类别 | 普通员工住宿限额 | 部门负责人住宿限额 | 交通工具乘坐标准 |
|---|---|---|---|
| 一线城市（北上广深） | ¥400 / 晚 | ¥600 / 晚 | 高铁二等座 / 经济舱（8折以下） |
| 省会及新一线城市 | ¥300 / 晚 | ¥450 / 晚 | 高铁二等座 |
| 其他三四线城市 | ¥220 / 晚 | ¥350 / 晚 | 高铁二等座 / 硬卧 |

### 二、餐饮与市内交通补贴
- 餐饮伙食补助：标准为 ¥100 / 天，按实际出差自然天数包干核算。
- 市内交通：凭出租车/网约车合规发票实报实销，单日上限 ¥80。

### 三、报销提报要求
1. 出差任务结束后 5 个工作日内，须在 OA 出差模块关联报销单。
2. 发票抬头必须为公司全称及纳税人识别号，电子发票原文件须在附件完整上传。",
                TagsJson = JsonSerializer.Serialize(new[] { "财务", "报销", "差旅标准", "发票" }),
                Version = 1,
                Status = (int)DocumentStatus.Published,
                IsMustRead = false,
                DepartmentId = null,
                EffectiveDate = today.AddDays(-60),
                ExpiryDate = null,
                AttachmentsJson = "[]",
                ViewCount = 88,
                DownloadCount = 35,
                CreatedBy = actor.Id,
                CreatedByName = actor.Name,
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-60),
                PublishedBy = actor.Id,
                PublishedByName = actor.Name,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-60),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-60),
                IsDemo = true
            };
            _db.KnowledgeDocuments.Add(doc2);
            _db.DocumentVersions.Add(new DocumentVersionRecord
            {
                TenantId = TenantId,
                DocumentId = doc2.Id,
                Version = 1,
                Title = doc2.Title,
                Summary = doc2.Summary,
                Content = doc2.Content,
                ChangeNotes = "财务部统一发布版本",
                AttachmentsJson = "[]",
                PublishedAt = doc2.PublishedAt!.Value,
                PublishedBy = actor.Id,
                PublishedByName = actor.Name,
                CreatedAt = doc2.CreatedAt
            });
            created++;
        }
        else skipped++;

        // Doc 3: 信息安全与代码保密指引 (Engineering department must-read)
        if (!_db.KnowledgeDocuments.Any(d => d.TenantId == TenantId && d.Number == "DOC-DEMO-003"))
        {
            var doc3 = new KnowledgeDocumentRecord
            {
                TenantId = TenantId,
                Number = "DOC-DEMO-003",
                CategoryId = catTech.Id,
                Title = "研发部信息安全规范与代码保密管理规范",
                Summary = "针对研发人员的代码仓库访问、生产环境权限、敏感密钥管理及开源依赖引入的安全合规指南。",
                Content = @"### 一、生产凭证与密钥防线
1. 任何开发人员严禁将数据库账号密码、API 签名私钥、JWT Secret 提交至公共 Git 仓库。
2. 生产环境密钥必须通过环境变量或受保护 Key-Per-File 挂载机制注入。

### 二、开源合规与供应链安全
- 引入第三方 npm/NuGet 依赖须校验开源许可证（避免 GPL 强传染性风险）。
- 系统持续启用 ClamAV 恶意文件扫描，所有上传附件一律经过杀毒阻断检查。

### 三、研发签署要求
本规范适用于研发中心全体工程师及测试人员，首次入职及版本更新须完成在线阅读确认。",
                TagsJson = JsonSerializer.Serialize(new[] { "信息安全", "代码保密", "研发规范", "部门必读" }),
                Version = 1,
                Status = (int)DocumentStatus.Published,
                IsMustRead = true,
                DepartmentId = "engineering",
                EffectiveDate = today.AddDays(-15),
                ExpiryDate = null,
                AttachmentsJson = "[]",
                ViewCount = 26,
                DownloadCount = 12,
                CreatedBy = actor.Id,
                CreatedByName = actor.Name,
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-15),
                PublishedBy = actor.Id,
                PublishedByName = actor.Name,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-15),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-15),
                IsDemo = true
            };
            _db.KnowledgeDocuments.Add(doc3);
            _db.DocumentVersions.Add(new DocumentVersionRecord
            {
                TenantId = TenantId,
                DocumentId = doc3.Id,
                Version = 1,
                Title = doc3.Title,
                Summary = doc3.Summary,
                Content = doc3.Content,
                ChangeNotes = "研发中心信息安全委员会发布",
                AttachmentsJson = "[]",
                PublishedAt = doc3.PublishedAt!.Value,
                PublishedBy = actor.Id,
                PublishedByName = actor.Name,
                CreatedAt = doc3.CreatedAt
            });
            created++;
        }
        else skipped++;

        _db.SaveChanges();
        return ServiceResult<object>.Success(new { created, skipped });
    }

    // -------------------------------------------------------------
    // Helper Methods
    // -------------------------------------------------------------

    private DocumentCategoryRecord GetOrCreateCategory(string code, string name, string desc, string? deptId, int sort)
    {
        var existing = _db.DocumentCategories.SingleOrDefault(c => c.TenantId == TenantId && c.Code == code);
        if (existing is not null) return existing;

        var cat = new DocumentCategoryRecord
        {
            TenantId = TenantId,
            Code = code,
            Name = name,
            Description = desc,
            DepartmentId = deptId,
            SortOrder = sort,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.DocumentCategories.Add(cat);
        _db.SaveChanges();
        return cat;
    }

    private string GenerateNumber()
    {
        var prefix = $"DOC-{DateTime.UtcNow:yyyyMM}-";
        var last = _db.KnowledgeDocuments.AsNoTracking()
            .Where(d => d.TenantId == TenantId && d.Number.StartsWith(prefix))
            .OrderByDescending(d => d.Number)
            .Select(d => d.Number)
            .FirstOrDefault();

        var seq = 1;
        if (last is not null && last.Length >= prefix.Length + 4 && int.TryParse(last[prefix.Length..], out var parsed))
        {
            seq = parsed + 1;
        }
        return $"{prefix}{seq:D4}";
    }

    private static ServiceResult<bool> ValidateInput(SaveDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 100)
            return ServiceResult<bool>.Failure("文档标题应为 1–100 个字符。", "DOC_001");
        if (request.CategoryId == Guid.Empty)
            return ServiceResult<bool>.Failure("请选择所属目录分类。", "DOC_001");
        if (string.IsNullOrWhiteSpace(request.Summary) || request.Summary.Trim().Length > 300)
            return ServiceResult<bool>.Failure("文档摘要应为 1–300 个字符。", "DOC_001");
        if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Trim().Length > 20000)
            return ServiceResult<bool>.Failure("文档正文应为 1–20000 个字符。", "DOC_001");
        if (request.ExpiryDate.HasValue && request.ExpiryDate.Value < request.EffectiveDate)
            return ServiceResult<bool>.Failure("失效日期不能早于生效日期。", "DOC_001");

        return ServiceResult<bool>.Success(true);
    }

    private IReadOnlyList<string>? NormalizeAttachments(Employee actor, IReadOnlyList<string>? attachments)
    {
        if (attachments is null || attachments.Count == 0) return [];
        var distinct = attachments.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
        if (distinct.Count > 10) return null;
        if (_files is not null && !_files.AreOwnedBy(actor, distinct)) return null;
        return distinct;
    }

    private static IReadOnlyList<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static KnowledgeDocumentView ToView(KnowledgeDocumentRecord doc, string categoryName, bool hasAck, DateTimeOffset? ackAt)
    {
        return new KnowledgeDocumentView(
            doc.Id,
            doc.Number,
            doc.CategoryId,
            categoryName,
            doc.Title,
            doc.Summary,
            doc.Content,
            DeserializeList(doc.TagsJson),
            doc.Version,
            (DocumentStatus)doc.Status,
            doc.IsMustRead,
            doc.DepartmentId,
            doc.EffectiveDate,
            doc.ExpiryDate,
            DeserializeList(doc.AttachmentsJson),
            doc.ViewCount,
            doc.DownloadCount,
            doc.CreatedBy,
            doc.CreatedByName,
            doc.PublishedAt,
            doc.PublishedByName,
            doc.ArchivedAt,
            doc.CreatedAt,
            doc.UpdatedAt,
            hasAck,
            ackAt
        );
    }

    private void Audit(Employee actor, string action, Guid resourceId, string summary)
    {
        _db.AuditLogs.Add(new AuditRecord
        {
            TenantId = TenantId,
            ActorId = actor.Id,
            Action = action,
            ResourceType = "KnowledgeDocument",
            ResourceId = resourceId.ToString(),
            Summary = summary,
            OccurredAt = DateTimeOffset.UtcNow
        });
    }
}
