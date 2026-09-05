using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class BusinessConfigurationService
{
    private const string TenantId = "demo";
    private readonly OaDbContext db;
    private readonly DemoData data;

    public BusinessConfigurationService(OaDbContext db, DemoData data)
    {
        this.db = db;
        this.data = data;
    }

    public ServiceResult<PagedResponse<BusinessConfigurationListItem>> List(Employee actor, BusinessConfigurationFilterQuery query)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            return ServiceResult<PagedResponse<BusinessConfigurationListItem>>.Failure("无权访问业务参数配置中心。", "AUTH_002");

        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Max(1, query.Page);

        var source = db.BusinessConfigurations.AsNoTracking().Where(item => item.TenantId == TenantId);

        if (!string.IsNullOrWhiteSpace(query.Domain))
            source = source.Where(item => item.Domain == query.Domain.Trim());

        if (!string.IsNullOrWhiteSpace(query.Code))
            source = source.Where(item => item.Code == query.Code.Trim());

        if (!string.IsNullOrWhiteSpace(query.Status))
            source = source.Where(item => item.Status == query.Status.Trim());

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = $"%{EscapeLike(query.Keyword.Trim())}%";
            source = source.Where(item => EF.Functions.ILike(item.Name, pattern, "\\") ||
                                          EF.Functions.ILike(item.Code, pattern, "\\") ||
                                          (item.Description != null && EF.Functions.ILike(item.Description, pattern, "\\")));
        }

        if (query.EffectiveAsOf.HasValue)
        {
            var asOf = query.EffectiveAsOf.Value;
            source = source.Where(item => item.Status != ConfigurationStatus.Draft &&
                                          item.EffectiveFrom <= asOf &&
                                          (item.EffectiveTo == null || item.EffectiveTo > asOf));
        }

        var total = source.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (decimal)pageSize));
        page = Math.Min(page, totalPages);

        var records = source
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Version)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var recordIds = records.Select(item => item.Id).ToList();
        var referenceCounts = GetReferenceCounts(recordIds);

        var items = records.Select(item => new BusinessConfigurationListItem(
            item.Id,
            item.TenantId,
            item.Domain,
            item.Code,
            item.Name,
            item.Description,
            item.Version,
            item.Status,
            item.EffectiveFrom,
            item.EffectiveTo,
            item.CreatedByName,
            item.CreatedAt,
            item.UpdatedByName,
            item.UpdatedAt,
            item.PublishedByName,
            item.PublishedAt,
            item.ConcurrencyVersion,
            referenceCounts.GetValueOrDefault(item.Id, 0)
        )).ToList();

        return ServiceResult<PagedResponse<BusinessConfigurationListItem>>.Success(
            new PagedResponse<BusinessConfigurationListItem>(items, total, page, pageSize, totalPages));
    }

    public ServiceResult<BusinessConfigurationView> Get(Employee actor, Guid id)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");

        var record = db.BusinessConfigurations.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null)
            return ServiceResult<BusinessConfigurationView>.Failure("配置不存在。", "DATA_001");

        var refCount = CountReferences(id);
        return ServiceResult<BusinessConfigurationView>.Success(ToView(record, refCount));
    }

    public ServiceResult<BusinessConfigurationView> CreateDraft(Employee actor, CreateBusinessConfigurationRequest request)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");

        var domain = request.Domain.Trim();
        if (!ConfigurationDomains.All.Contains(domain))
            return ServiceResult<BusinessConfigurationView>.Failure($"不支持的业务领域：{domain}。", "CONFIG_001");

        var code = request.Code.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64)
            return ServiceResult<BusinessConfigurationView>.Failure("配置编码应为 1–64 个字符。", "CONFIG_001");

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return ServiceResult<BusinessConfigurationView>.Failure("配置名称应为 1–100 个字符。", "CONFIG_001");

        var normalizedJson = BusinessConfigurationValidator.ValidateAndNormalize(domain, request.ContentJson, data);
        if (!normalizedJson.IsSuccess)
            return ServiceResult<BusinessConfigurationView>.Failure(normalizedJson.Error!, normalizedJson.Code!);

        if (request.EffectiveTo.HasValue && request.EffectiveTo.Value <= request.EffectiveFrom)
            return ServiceResult<BusinessConfigurationView>.Failure("失效时间必须晚于生效时间。", "CONFIG_001");

        var maxVersion = db.BusinessConfigurations
            .Where(item => item.TenantId == TenantId && item.Domain == domain && item.Code == code)
            .Select(item => (int?)item.Version)
            .Max() ?? 0;

        var now = DateTimeOffset.UtcNow;
        var entity = new BusinessConfigurationRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Domain = domain,
            Code = code,
            Name = name,
            Description = request.Description?.Trim(),
            Version = maxVersion + 1,
            Status = ConfigurationStatus.Draft,
            EffectiveFrom = request.EffectiveFrom.ToUniversalTime(),
            EffectiveTo = request.EffectiveTo?.ToUniversalTime(),
            ContentJson = normalizedJson.Value!,
            CreatedBy = actor.Id,
            CreatedByName = actor.Name,
            CreatedAt = now,
            UpdatedBy = actor.Id,
            UpdatedByName = actor.Name,
            UpdatedAt = now,
            ConcurrencyVersion = 1
        };

        db.BusinessConfigurations.Add(entity);
        db.SaveChanges();

        LogAudit(actor, "CONFIG_DRAFT_CREATED", entity.Id.ToString(),
            $"创建【{entity.Domain} / {entity.Code}】v{entity.Version} 配置草稿：{entity.Name}");

        return ServiceResult<BusinessConfigurationView>.Success(ToView(entity, 0));
    }

    public ServiceResult<BusinessConfigurationView> UpdateDraft(Employee actor, Guid id, UpdateBusinessConfigurationRequest request)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");

        var record = db.BusinessConfigurations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null)
            return ServiceResult<BusinessConfigurationView>.Failure("配置不存在。", "DATA_001");

        if (record.Status != ConfigurationStatus.Draft)
            return ServiceResult<BusinessConfigurationView>.Failure("已发布或已停用的配置不能原地修改，请创建新版本。", "CONFIG_002");

        if (record.ConcurrencyVersion != request.ConcurrencyVersion)
            return ServiceResult<BusinessConfigurationView>.Failure("配置已被其他人修改，请刷新后重试。", "CONCURRENCY_001");

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return ServiceResult<BusinessConfigurationView>.Failure("配置名称应为 1–100 个字符。", "CONFIG_001");

        var normalizedJson = BusinessConfigurationValidator.ValidateAndNormalize(record.Domain, request.ContentJson, data);
        if (!normalizedJson.IsSuccess)
            return ServiceResult<BusinessConfigurationView>.Failure(normalizedJson.Error!, normalizedJson.Code!);

        if (request.EffectiveTo.HasValue && request.EffectiveTo.Value <= request.EffectiveFrom)
            return ServiceResult<BusinessConfigurationView>.Failure("失效时间必须晚于生效时间。", "CONFIG_001");

        var now = DateTimeOffset.UtcNow;
        record.Name = name;
        record.Description = request.Description?.Trim();
        record.EffectiveFrom = request.EffectiveFrom.ToUniversalTime();
        record.EffectiveTo = request.EffectiveTo?.ToUniversalTime();
        record.ContentJson = normalizedJson.Value!;
        record.UpdatedBy = actor.Id;
        record.UpdatedByName = actor.Name;
        record.UpdatedAt = now;
        record.ConcurrencyVersion++;

        db.SaveChanges();

        LogAudit(actor, "CONFIG_DRAFT_UPDATED", record.Id.ToString(),
            $"更新【{record.Domain} / {record.Code}】v{record.Version} 配置草稿：{record.Name}");

        return ServiceResult<BusinessConfigurationView>.Success(ToView(record, CountReferences(id)));
    }

    public ServiceResult<BusinessConfigurationView> CreateNewVersion(Employee actor, Guid sourceId)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");

        var source = db.BusinessConfigurations.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == sourceId);
        if (source is null)
            return ServiceResult<BusinessConfigurationView>.Failure("源配置不存在。", "DATA_001");

        var maxVersion = db.BusinessConfigurations
            .Where(item => item.TenantId == TenantId && item.Domain == source.Domain && item.Code == source.Code)
            .Select(item => (int?)item.Version)
            .Max() ?? 0;

        var now = DateTimeOffset.UtcNow;
        var entity = new BusinessConfigurationRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Domain = source.Domain,
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            Version = maxVersion + 1,
            Status = ConfigurationStatus.Draft,
            EffectiveFrom = now,
            EffectiveTo = null,
            ContentJson = source.ContentJson,
            CreatedBy = actor.Id,
            CreatedByName = actor.Name,
            CreatedAt = now,
            UpdatedBy = actor.Id,
            UpdatedByName = actor.Name,
            UpdatedAt = now,
            ConcurrencyVersion = 1
        };

        db.BusinessConfigurations.Add(entity);
        db.SaveChanges();

        LogAudit(actor, "CONFIG_VERSION_CREATED", entity.Id.ToString(),
            $"基于 v{source.Version} 创建【{entity.Domain} / {entity.Code}】v{entity.Version} 新版本草稿");

        return ServiceResult<BusinessConfigurationView>.Success(ToView(entity, 0));
    }

    public ServiceResult<BusinessConfigurationView> Publish(Employee actor, Guid id, PublishBusinessConfigurationRequest? request = null)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");

        var record = db.BusinessConfigurations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null)
            return ServiceResult<BusinessConfigurationView>.Failure("配置不存在。", "DATA_001");

        if (request?.ConcurrencyVersion.HasValue == true && record.ConcurrencyVersion != request.ConcurrencyVersion.Value)
            return ServiceResult<BusinessConfigurationView>.Failure("配置已被其他人修改，请刷新后重试。", "CONCURRENCY_001");

        var normalizedJson = BusinessConfigurationValidator.ValidateAndNormalize(record.Domain, record.ContentJson, data);
        if (!normalizedJson.IsSuccess)
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(),
                $"发布【{record.Domain} / {record.Code}】v{record.Version} 校验失败：{normalizedJson.Error}");
            return ServiceResult<BusinessConfigurationView>.Failure(normalizedJson.Error!, normalizedJson.Code!);
        }

        var effectiveFrom = (request?.EffectiveFrom ?? record.EffectiveFrom).ToUniversalTime();
        var effectiveTo = (request?.EffectiveTo ?? record.EffectiveTo)?.ToUniversalTime();

        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom)
            return ServiceResult<BusinessConfigurationView>.Failure("失效时间必须晚于生效时间。", "CONFIG_001");

        // Range overlap check with other published versions
        var otherPublished = db.BusinessConfigurations
            .Where(item => item.TenantId == TenantId && item.Domain == record.Domain && item.Code == record.Code && item.Id != record.Id)
            .Where(item => item.Status == ConfigurationStatus.Effective || item.Status == ConfigurationStatus.Scheduled)
            .ToList();

        var effectiveEnd = effectiveTo ?? DateTimeOffset.MaxValue;
        foreach (var other in otherPublished)
        {
            var otherEnd = other.EffectiveTo ?? DateTimeOffset.MaxValue;
            // Overlap: StartA < EndB && EndA > StartB
            if (effectiveFrom < otherEnd && effectiveEnd > other.EffectiveFrom)
            {
                // If publishing an immediate version, we can auto-cap the old EFFECTIVE version if start matches or is later
                if (effectiveFrom >= other.EffectiveFrom && other.Status == ConfigurationStatus.Effective && other.EffectiveTo == null)
                {
                    other.EffectiveTo = effectiveFrom;
                    other.Status = ConfigurationStatus.Retired;
                    other.UpdatedAt = DateTimeOffset.UtcNow;
                    other.UpdatedBy = actor.Id;
                    other.UpdatedByName = actor.Name;
                    other.ConcurrencyVersion++;
                }
                else
                {
                    return ServiceResult<BusinessConfigurationView>.Failure(
                        $"生效时间区间与已发布版本 v{other.Version}（{other.EffectiveFrom:yyyy-MM-dd HH:mm} 至 {(other.EffectiveTo.HasValue ? other.EffectiveTo.Value.ToString("yyyy-MM-dd HH:mm") : "永久")}）冲突。",
                        "CONFIG_003");
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var newStatus = effectiveFrom <= now && (effectiveTo == null || effectiveTo > now)
            ? ConfigurationStatus.Effective
            : ConfigurationStatus.Scheduled;

        // If new status is Effective, retire any active versions that have ended
        if (newStatus == ConfigurationStatus.Effective)
        {
            foreach (var existingEffective in otherPublished.Where(item => item.Status == ConfigurationStatus.Effective))
            {
                if (existingEffective.EffectiveTo == null || existingEffective.EffectiveTo > effectiveFrom)
                    existingEffective.EffectiveTo = effectiveFrom;
                existingEffective.Status = ConfigurationStatus.Retired;
                existingEffective.UpdatedAt = now;
                existingEffective.UpdatedBy = actor.Id;
                existingEffective.UpdatedByName = actor.Name;
                existingEffective.ConcurrencyVersion++;
            }
        }

        record.Status = newStatus;
        record.EffectiveFrom = effectiveFrom;
        record.EffectiveTo = effectiveTo;
        record.PublishedBy = actor.Id;
        record.PublishedByName = actor.Name;
        record.PublishedAt = now;
        record.UpdatedBy = actor.Id;
        record.UpdatedByName = actor.Name;
        record.UpdatedAt = now;
        record.ConcurrencyVersion++;

        db.SaveChanges();

        LogAudit(actor, "CONFIG_PUBLISHED", record.Id.ToString(),
            $"发布【{record.Domain} / {record.Code}】v{record.Version} 为【{record.Status}】状态，生效开始：{record.EffectiveFrom:yyyy-MM-dd HH:mm}");

        return ServiceResult<BusinessConfigurationView>.Success(ToView(record, CountReferences(id)));
    }

    public ServiceResult<BusinessConfigurationView> Retire(Employee actor, Guid id, RetireBusinessConfigurationRequest? request = null)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");

        var record = db.BusinessConfigurations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null)
            return ServiceResult<BusinessConfigurationView>.Failure("配置不存在。", "DATA_001");

        if (request?.ConcurrencyVersion.HasValue == true && record.ConcurrencyVersion != request.ConcurrencyVersion.Value)
            return ServiceResult<BusinessConfigurationView>.Failure("配置已被其他人修改，请刷新后重试。", "CONCURRENCY_001");

        var now = DateTimeOffset.UtcNow;
        record.Status = ConfigurationStatus.Retired;
        record.EffectiveTo = request?.EffectiveTo ?? now;
        record.UpdatedBy = actor.Id;
        record.UpdatedByName = actor.Name;
        record.UpdatedAt = now;
        record.ConcurrencyVersion++;

        db.SaveChanges();

        LogAudit(actor, "CONFIG_RETIRED", record.Id.ToString(),
            $"停用【{record.Domain} / {record.Code}】v{record.Version} 配置版本");

        return ServiceResult<BusinessConfigurationView>.Success(ToView(record, CountReferences(id)));
    }

    public ServiceResult<bool> Delete(Employee actor, Guid id)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            return ServiceResult<bool>.Failure("无权访问业务参数配置中心。", "AUTH_002");

        var record = db.BusinessConfigurations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null)
            return ServiceResult<bool>.Failure("配置不存在。", "DATA_001");

        if (record.Status != ConfigurationStatus.Draft)
            return ServiceResult<bool>.Failure("只能删除草稿状态的配置。", "CONFIG_002");

        var refCount = CountReferences(id);
        if (refCount > 0)
            return ServiceResult<bool>.Failure("该配置版本已被业务单据引用，不可删除。", "CONFIG_004");

        db.BusinessConfigurations.Remove(record);
        db.SaveChanges();

        LogAudit(actor, "CONFIG_DELETED", id.ToString(),
            $"删除【{record.Domain} / {record.Code}】v{record.Version} 草稿配置");

        return ServiceResult<bool>.Success(true);
    }

    public ServiceResult<IReadOnlyList<ConfigurationVersionSummary>> ListVersions(Employee actor, Guid id)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
            return ServiceResult<IReadOnlyList<ConfigurationVersionSummary>>.Failure("无权访问业务参数配置中心。", "AUTH_002");

        var target = db.BusinessConfigurations.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (target is null)
            return ServiceResult<IReadOnlyList<ConfigurationVersionSummary>>.Failure("配置不存在。", "DATA_001");

        var versions = db.BusinessConfigurations.AsNoTracking()
            .Where(item => item.TenantId == TenantId && item.Domain == target.Domain && item.Code == target.Code)
            .OrderByDescending(item => item.Version)
            .ToList();

        var refCounts = GetReferenceCounts(versions.Select(v => v.Id).ToList());

        var items = versions.Select(v => new ConfigurationVersionSummary(
            v.Id,
            v.Version,
            v.Status,
            v.EffectiveFrom,
            v.EffectiveTo,
            v.PublishedByName,
            v.PublishedAt,
            v.CreatedAt,
            refCounts.GetValueOrDefault(v.Id, 0)
        )).ToList();

        return ServiceResult<IReadOnlyList<ConfigurationVersionSummary>>.Success(items);
    }

    public BusinessConfigurationView? GetEffective(string domain, string code, DateTimeOffset? asOfDate = null)
    {
        var asOf = asOfDate ?? DateTimeOffset.UtcNow;

        // Auto-activate any scheduled version whose effectiveFrom has arrived
        var pendingScheduled = db.BusinessConfigurations
            .Where(item => item.TenantId == TenantId && item.Domain == domain && item.Code == code && item.Status == ConfigurationStatus.Scheduled && item.EffectiveFrom <= DateTimeOffset.UtcNow)
            .ToList();

        if (pendingScheduled.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var item in pendingScheduled)
            {
                // retire old effective
                var oldEffectives = db.BusinessConfigurations
                    .Where(other => other.TenantId == TenantId && other.Domain == domain && other.Code == code && other.Status == ConfigurationStatus.Effective && other.Id != item.Id)
                    .ToList();
                foreach (var old in oldEffectives)
                {
                    old.Status = ConfigurationStatus.Retired;
                    old.EffectiveTo = item.EffectiveFrom;
                    old.UpdatedAt = now;
                }
                item.Status = ConfigurationStatus.Effective;
                item.UpdatedAt = now;
            }
            db.SaveChanges();
        }

        var candidates = db.BusinessConfigurations.AsNoTracking()
            .Where(item => item.TenantId == TenantId && item.Domain == domain && item.Code == code)
            .Where(item => (item.Status == ConfigurationStatus.Effective || item.Status == ConfigurationStatus.Scheduled) &&
                           item.EffectiveFrom <= asOf &&
                           (item.EffectiveTo == null || item.EffectiveTo > asOf))
            .OrderByDescending(item => item.EffectiveFrom)
            .ThenByDescending(item => item.Version)
            .ToList();

        var selected = candidates.FirstOrDefault();
        return selected is null ? null : ToView(selected, CountReferences(selected.Id));
    }

    public void EnsureDefaultConfigurations()
    {
        BusinessConfigurationDefaults.EnsureDefaultConfigurations(db, TenantId);
    }

    public int CountReferences(Guid configId)
    {
        var leave = db.LeaveRequests.Count(item => item.TenantId == TenantId && item.ConfigVersionId == configId);
        var expense = db.ExpenseClaims.Count(item => item.TenantId == TenantId && item.ConfigVersionId == configId);
        var travel = db.TravelRequests.Count(item => item.TenantId == TenantId && item.ConfigVersionId == configId);
        var purchase = db.PurchaseRequests.Count(item => item.TenantId == TenantId && item.ConfigVersionId == configId);
        var seal = db.SealRequests.Count(item => item.TenantId == TenantId && item.ConfigVersionId == configId);
        return leave + expense + travel + purchase + seal;
    }

    private Dictionary<Guid, int> GetReferenceCounts(List<Guid> configIds)
    {
        if (configIds.Count == 0) return [];

        var leaveCounts = db.LeaveRequests.Where(item => item.TenantId == TenantId && item.ConfigVersionId.HasValue && configIds.Contains(item.ConfigVersionId.Value))
            .GroupBy(item => item.ConfigVersionId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToList();
        var expenseCounts = db.ExpenseClaims.Where(item => item.TenantId == TenantId && item.ConfigVersionId.HasValue && configIds.Contains(item.ConfigVersionId.Value))
            .GroupBy(item => item.ConfigVersionId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToList();
        var travelCounts = db.TravelRequests.Where(item => item.TenantId == TenantId && item.ConfigVersionId.HasValue && configIds.Contains(item.ConfigVersionId.Value))
            .GroupBy(item => item.ConfigVersionId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToList();
        var purchaseCounts = db.PurchaseRequests.Where(item => item.TenantId == TenantId && item.ConfigVersionId.HasValue && configIds.Contains(item.ConfigVersionId.Value))
            .GroupBy(item => item.ConfigVersionId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToList();
        var sealCounts = db.SealRequests.Where(item => item.TenantId == TenantId && item.ConfigVersionId.HasValue && configIds.Contains(item.ConfigVersionId.Value))
            .GroupBy(item => item.ConfigVersionId!.Value).Select(g => new { Id = g.Key, Count = g.Count() }).ToList();

        var dict = new Dictionary<Guid, int>();
        foreach (var item in leaveCounts.Concat(expenseCounts).Concat(travelCounts).Concat(purchaseCounts).Concat(sealCounts))
        {
            dict[item.Id] = dict.GetValueOrDefault(item.Id, 0) + item.Count;
        }
        return dict;
    }

    private static BusinessConfigurationView ToView(BusinessConfigurationRecord r, int refCount) => new(
        r.Id,
        r.TenantId,
        r.Domain,
        r.Code,
        r.Name,
        r.Description,
        r.Version,
        r.Status,
        r.EffectiveFrom,
        r.EffectiveTo,
        r.ContentJson,
        r.CreatedBy,
        r.CreatedByName,
        r.CreatedAt,
        r.UpdatedBy,
        r.UpdatedByName,
        r.UpdatedAt,
        r.PublishedBy,
        r.PublishedByName,
        r.PublishedAt,
        r.ConcurrencyVersion,
        refCount
    );

    private void LogAudit(Employee actor, string action, string resourceId, string summary)
    {
        db.AuditLogs.Add(new AuditRecord
        {
            TenantId = TenantId,
            ActorId = actor.Id,
            Action = action,
            ResourceType = "BusinessConfiguration",
            ResourceId = resourceId,
            Summary = summary,
            OccurredAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
