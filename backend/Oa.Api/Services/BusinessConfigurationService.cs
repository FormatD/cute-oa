using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Oa.Api.Domain;
using Oa.Api.Persistence;

namespace Oa.Api.Services;

public sealed class BusinessConfigurationService
{
    private const string TenantId = "demo";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DomainCodeLocks = new();
    private readonly OaDbContext db;
    private readonly DemoData data;

    public BusinessConfigurationService(OaDbContext db, DemoData data)
    {
        this.db = db;
        this.data = data;
    }

    private IDisposable AcquireLock(string domain, string code)
    {
        var key = $"{TenantId}:{domain.Trim()}:{code.Trim()}";
        var sem = DomainCodeLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        sem.Wait();
        IDbContextTransaction? tx = null;
        if (db.Database.IsNpgsql())
        {
            if (db.Database.CurrentTransaction is null)
            {
                tx = db.Database.BeginTransaction();
            }
            db.Database.ExecuteSqlRaw("SELECT pg_advisory_xact_lock(hashtext({0})::bigint)", key);
        }
        return new ActionDisposable(() =>
        {
            try
            {
                tx?.Commit();
            }
            finally
            {
                tx?.Dispose();
                sem.Release();
            }
        });
    }

    private sealed class ActionDisposable(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose()
        {
            Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
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
        {
            LogAudit(actor, "CONFIG_DRAFT_CREATE_FAILED", string.Empty, $"越权创建配置草稿被拒绝：缺少【{OaPermissions.BusinessConfigManage}】权限。");
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");
        }

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
        {
            LogAudit(actor, "CONFIG_DRAFT_CREATE_FAILED", string.Empty, $"创建配置草稿校验失败【{domain} / {code}】：{normalizedJson.Error}");
            return ServiceResult<BusinessConfigurationView>.Failure(normalizedJson.Error!, normalizedJson.Code!);
        }

        if (request.EffectiveTo.HasValue && request.EffectiveTo.Value <= request.EffectiveFrom)
        {
            LogAudit(actor, "CONFIG_DRAFT_CREATE_FAILED", string.Empty, $"创建配置草稿失败【{domain} / {code}】：失效时间必须晚于生效时间。");
            return ServiceResult<BusinessConfigurationView>.Failure("失效时间必须晚于生效时间。", "CONFIG_001");
        }

        using var lockLease = AcquireLock(domain, code);

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
        {
            LogAudit(actor, "CONFIG_DRAFT_UPDATE_FAILED", id.ToString(), $"越权更新配置草稿被拒绝：缺少【{OaPermissions.BusinessConfigManage}】权限。");
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");
        }

        var record = db.BusinessConfigurations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null)
            return ServiceResult<BusinessConfigurationView>.Failure("配置不存在。", "DATA_001");

        if (record.Status != ConfigurationStatus.Draft)
        {
            LogAudit(actor, "CONFIG_DRAFT_UPDATE_FAILED", record.Id.ToString(), $"尝试更新非草稿状态配置【{record.Status}】被拒绝。");
            return ServiceResult<BusinessConfigurationView>.Failure("已发布或已停用的配置不能原地修改，请创建新版本。", "CONFIG_002");
        }

        if (record.ConcurrencyVersion != request.ConcurrencyVersion)
        {
            LogAudit(actor, "CONFIG_DRAFT_UPDATE_FAILED", record.Id.ToString(), $"更新配置草稿并发版本冲突：客户端版本 {request.ConcurrencyVersion} 与当前数据库版本 {record.ConcurrencyVersion} 不匹配。");
            return ServiceResult<BusinessConfigurationView>.Failure("配置已被其他人修改，请刷新后重试。", "CONCURRENCY_001");
        }

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return ServiceResult<BusinessConfigurationView>.Failure("配置名称应为 1–100 个字符。", "CONFIG_001");

        var normalizedJson = BusinessConfigurationValidator.ValidateAndNormalize(record.Domain, request.ContentJson, data);
        if (!normalizedJson.IsSuccess)
        {
            LogAudit(actor, "CONFIG_DRAFT_UPDATE_FAILED", record.Id.ToString(), $"更新配置草稿校验失败：{normalizedJson.Error}");
            return ServiceResult<BusinessConfigurationView>.Failure(normalizedJson.Error!, normalizedJson.Code!);
        }

        if (request.EffectiveTo.HasValue && request.EffectiveTo.Value <= request.EffectiveFrom)
        {
            LogAudit(actor, "CONFIG_DRAFT_UPDATE_FAILED", record.Id.ToString(), "更新配置草稿失败：失效时间必须晚于生效时间。");
            return ServiceResult<BusinessConfigurationView>.Failure("失效时间必须晚于生效时间。", "CONFIG_001");
        }

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
        {
            LogAudit(actor, "CONFIG_VERSION_CREATE_FAILED", sourceId.ToString(), $"越权创建新配置版本被拒绝：缺少【{OaPermissions.BusinessConfigManage}】权限。");
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");
        }

        var source = db.BusinessConfigurations.AsNoTracking().SingleOrDefault(item => item.TenantId == TenantId && item.Id == sourceId);
        if (source is null)
        {
            LogAudit(actor, "CONFIG_VERSION_CREATE_FAILED", sourceId.ToString(), "创建新配置版本失败：源配置不存在。");
            return ServiceResult<BusinessConfigurationView>.Failure("源配置不存在。", "DATA_001");
        }

        using var lockLease = AcquireLock(source.Domain, source.Code);

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
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException)
        {
            LogAudit(actor, "CONFIG_VERSION_CREATE_FAILED", source.Id.ToString(), $"创建新配置版本失败【{source.Domain} / {source.Code}】：版本并发冲突。");
            return ServiceResult<BusinessConfigurationView>.Failure("版本并发冲突，请重试。", "CONCURRENCY_001");
        }

        LogAudit(actor, "CONFIG_VERSION_CREATED", entity.Id.ToString(),
            $"基于 v{source.Version} 创建【{entity.Domain} / {entity.Code}】v{entity.Version} 新版本草稿");

        return ServiceResult<BusinessConfigurationView>.Success(ToView(entity, 0));
    }

    public ServiceResult<BusinessConfigurationView> Publish(Employee actor, Guid id, PublishBusinessConfigurationRequest? request = null)
    {
        if (!data.HasPermission(actor, OaPermissions.BusinessConfigManage))
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", id.ToString(), $"越权发布配置被拒绝：缺少【{OaPermissions.BusinessConfigManage}】权限。");
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");
        }

        if (request?.ConcurrencyVersion == null || request.ConcurrencyVersion.Value <= 0)
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", id.ToString(), "发布配置失败：并发版本号为必填项且必须大于 0。");
            return ServiceResult<BusinessConfigurationView>.Failure("并发版本号为必填项且必须大于 0。", "CONCURRENCY_001");
        }

        var record = db.BusinessConfigurations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null)
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", id.ToString(), "发布配置失败：配置不存在。");
            return ServiceResult<BusinessConfigurationView>.Failure("配置不存在。", "DATA_001");
        }

        // 状态机严格约束：只有草稿状态可发布
        if (record.Status != ConfigurationStatus.Draft)
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(),
                $"尝试对非草稿状态【{record.Status}】配置【{record.Domain} / {record.Code}】v{record.Version} 进行发布被拒绝。");
            return ServiceResult<BusinessConfigurationView>.Failure("只有草稿状态的配置才可发布。", "CONFIG_002");
        }

        if (record.ConcurrencyVersion != request.ConcurrencyVersion.Value)
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(),
                $"发布配置并发版本冲突：客户端版本 {request.ConcurrencyVersion.Value} 与当前数据库版本 {record.ConcurrencyVersion} 不匹配。");
            return ServiceResult<BusinessConfigurationView>.Failure("配置已被其他人修改，请刷新后重试。", "CONCURRENCY_001");
        }

        var normalizedJson = BusinessConfigurationValidator.ValidateAndNormalize(record.Domain, record.ContentJson, data);
        if (!normalizedJson.IsSuccess)
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(),
                $"发布【{record.Domain} / {record.Code}】v{record.Version} 校验失败：{normalizedJson.Error}");
            return ServiceResult<BusinessConfigurationView>.Failure(normalizedJson.Error!, normalizedJson.Code!);
        }

        var effectiveFrom = (request.EffectiveFrom ?? record.EffectiveFrom).ToUniversalTime();
        var effectiveTo = (request.EffectiveTo ?? record.EffectiveTo)?.ToUniversalTime();

        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom)
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(), "发布配置失败：失效时间必须晚于生效时间。");
            return ServiceResult<BusinessConfigurationView>.Failure("失效时间必须晚于生效时间。", "CONFIG_001");
        }

        using var lockLease = AcquireLock(record.Domain, record.Code);

        db.Entry(record).Reload();
        if (record.ConcurrencyVersion != request.ConcurrencyVersion.Value)
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(),
                $"发布配置并发版本冲突：锁后重载版本 {record.ConcurrencyVersion} 与请求版本 {request.ConcurrencyVersion.Value} 不匹配。");
            return ServiceResult<BusinessConfigurationView>.Failure("配置已被其他人修改，请刷新后重试。", "CONCURRENCY_001");
        }

        if (record.Status != ConfigurationStatus.Draft)
        {
            LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(),
                $"发布配置失败：锁后配置已被变更为【{record.Status}】。");
            return ServiceResult<BusinessConfigurationView>.Failure("只有草稿状态的配置才可发布。", "CONFIG_002");
        }

        var now = DateTimeOffset.UtcNow;
        var newStatus = effectiveFrom <= now && (effectiveTo == null || effectiveTo > now)
            ? ConfigurationStatus.Effective
            : ConfigurationStatus.Scheduled;

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
                if (other.Status == ConfigurationStatus.Effective)
                {
                    // If other was published after this draft was authored, this draft is based on a stale state!
                    if (other.PublishedAt.HasValue && other.PublishedAt.Value >= record.CreatedAt)
                    {
                        LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(),
                            $"检测到并发发布冲突：配置【{record.Domain} / {record.Code}】在草稿编制后已有更新版本 v{other.Version} 发布生效。");
                        return ServiceResult<BusinessConfigurationView>.Failure(
                            $"检测到并发发布冲突：配置【{record.Domain} / {record.Code}】在草稿编制后已有更新版本 v{other.Version} 发布生效，请刷新后重试。",
                            "CONCURRENCY_001");
                    }

                    if (effectiveFrom <= other.EffectiveFrom)
                    {
                        LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(),
                            $"生效起始时间必须晚于已生效版本 v{other.Version} 的起始时间（{other.EffectiveFrom:yyyy-MM-dd HH:mm}）。");
                        return ServiceResult<BusinessConfigurationView>.Failure(
                            $"生效起始时间必须晚于已生效版本 v{other.Version} 的起始时间（{other.EffectiveFrom:yyyy-MM-dd HH:mm}）。",
                            "CONFIG_003");
                    }

                    other.EffectiveTo = effectiveFrom;
                    if (newStatus == ConfigurationStatus.Effective)
                    {
                        other.Status = ConfigurationStatus.Retired;
                    }
                    other.UpdatedAt = now;
                    other.UpdatedBy = actor.Id;
                    other.UpdatedByName = actor.Name;
                    other.ConcurrencyVersion++;
                }
                else
                {
                    LogAudit(actor, "CONFIG_PUBLISH_FAILED", record.Id.ToString(),
                        $"生效时间区间与已发布版本 v{other.Version}（{other.EffectiveFrom:yyyy-MM-dd HH:mm} 至 {(other.EffectiveTo.HasValue ? other.EffectiveTo.Value.ToString("yyyy-MM-dd HH:mm") : "永久")}）冲突。");
                    return ServiceResult<BusinessConfigurationView>.Failure(
                        $"生效时间区间与已发布版本 v{other.Version}（{other.EffectiveFrom:yyyy-MM-dd HH:mm} 至 {(other.EffectiveTo.HasValue ? other.EffectiveTo.Value.ToString("yyyy-MM-dd HH:mm") : "永久")}）冲突。",
                        "CONFIG_003");
                }
            }
        }

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
        {
            LogAudit(actor, "CONFIG_RETIRE_FAILED", id.ToString(), $"越权停用配置被拒绝：缺少【{OaPermissions.BusinessConfigManage}】权限。");
            return ServiceResult<BusinessConfigurationView>.Failure("无权访问业务参数配置中心。", "AUTH_002");
        }

        if (request?.ConcurrencyVersion == null || request.ConcurrencyVersion.Value <= 0)
        {
            LogAudit(actor, "CONFIG_RETIRE_FAILED", id.ToString(), "停用配置失败：并发版本号为必填项且必须大于 0。");
            return ServiceResult<BusinessConfigurationView>.Failure("并发版本号为必填项且必须大于 0。", "CONCURRENCY_001");
        }

        var record = db.BusinessConfigurations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null)
        {
            LogAudit(actor, "CONFIG_RETIRE_FAILED", id.ToString(), "停用配置失败：配置不存在。");
            return ServiceResult<BusinessConfigurationView>.Failure("配置不存在。", "DATA_001");
        }

        // 状态机严格约束：草稿不能下线，Retired 不能重复下线
        if (record.Status == ConfigurationStatus.Draft)
        {
            LogAudit(actor, "CONFIG_RETIRE_FAILED", record.Id.ToString(), "停用配置失败：草稿状态的配置不能下线，请直接删除。");
            return ServiceResult<BusinessConfigurationView>.Failure("草稿状态的配置不能下线，请直接删除。", "CONFIG_002");
        }

        if (record.Status == ConfigurationStatus.Retired)
        {
            LogAudit(actor, "CONFIG_RETIRE_FAILED", record.Id.ToString(), "停用配置失败：该配置版本已处于停用状态，不可重复下线。");
            return ServiceResult<BusinessConfigurationView>.Failure("该配置版本已处于停用状态，不可重复下线。", "CONFIG_002");
        }

        if (record.Status != ConfigurationStatus.Effective && record.Status != ConfigurationStatus.Scheduled)
        {
            LogAudit(actor, "CONFIG_RETIRE_FAILED", record.Id.ToString(), $"停用配置失败：当前状态【{record.Status}】不允许下线。");
            return ServiceResult<BusinessConfigurationView>.Failure("当前状态不允许下线。", "CONFIG_002");
        }

        if (record.ConcurrencyVersion != request.ConcurrencyVersion.Value)
        {
            LogAudit(actor, "CONFIG_RETIRE_FAILED", record.Id.ToString(), $"停用配置并发版本冲突：客户端版本 {request.ConcurrencyVersion.Value} 与当前版本 {record.ConcurrencyVersion} 不匹配。");
            return ServiceResult<BusinessConfigurationView>.Failure("配置已被其他人修改，请刷新后重试。", "CONCURRENCY_001");
        }

        using var lockLease = AcquireLock(record.Domain, record.Code);
        db.Entry(record).Reload();
        if (record.ConcurrencyVersion != request.ConcurrencyVersion.Value)
        {
            LogAudit(actor, "CONFIG_RETIRE_FAILED", record.Id.ToString(), $"停用配置并发版本冲突：锁后版本 {record.ConcurrencyVersion} 与客户端版本 {request.ConcurrencyVersion.Value} 不匹配。");
            return ServiceResult<BusinessConfigurationView>.Failure("配置已被其他人修改，请刷新后重试。", "CONCURRENCY_001");
        }

        if (record.Status != ConfigurationStatus.Effective && record.Status != ConfigurationStatus.Scheduled)
        {
            LogAudit(actor, "CONFIG_RETIRE_FAILED", record.Id.ToString(), $"停用配置失败：锁后状态【{record.Status}】不允许下线。");
            return ServiceResult<BusinessConfigurationView>.Failure("当前状态不允许下线。", "CONFIG_002");
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset effectiveTo;
        if (request.EffectiveTo.HasValue)
        {
            effectiveTo = request.EffectiveTo.Value.ToUniversalTime();
            if (effectiveTo < record.EffectiveFrom)
            {
                LogAudit(actor, "CONFIG_RETIRE_FAILED", record.Id.ToString(), "停用配置失败：下线失效时间不能早于生效起始时间。");
                return ServiceResult<BusinessConfigurationView>.Failure("下线失效时间不能早于生效起始时间。", "CONFIG_001");
            }
        }
        else
        {
            effectiveTo = now < record.EffectiveFrom ? record.EffectiveFrom : now;
        }

        record.Status = ConfigurationStatus.Retired;
        record.EffectiveTo = effectiveTo;
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
        {
            LogAudit(actor, "CONFIG_DELETE_FAILED", id.ToString(), $"越权删除配置被拒绝：缺少【{OaPermissions.BusinessConfigManage}】权限。");
            return ServiceResult<bool>.Failure("无权访问业务参数配置中心。", "AUTH_002");
        }

        var record = db.BusinessConfigurations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == id);
        if (record is null)
        {
            LogAudit(actor, "CONFIG_DELETE_FAILED", id.ToString(), "删除配置失败：配置不存在。");
            return ServiceResult<bool>.Failure("配置不存在。", "DATA_001");
        }

        if (record.Status != ConfigurationStatus.Draft)
        {
            LogAudit(actor, "CONFIG_DELETE_FAILED", record.Id.ToString(), $"删除配置失败：只能删除草稿状态的配置，当前状态为【{record.Status}】。");
            return ServiceResult<bool>.Failure("只能删除草稿状态的配置。", "CONFIG_002");
        }

        var refCount = CountReferences(id);
        if (refCount > 0)
        {
            LogAudit(actor, "CONFIG_DELETE_FAILED", record.Id.ToString(), $"删除配置失败：该配置版本已被业务单据引用（引用数：{refCount}），不可删除。");
            return ServiceResult<bool>.Failure("该配置版本已被业务单据引用，不可删除。", "CONFIG_004");
        }

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
        var asOf = (asOfDate ?? DateTimeOffset.UtcNow).ToUniversalTime();

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

    public int ActivateScheduledConfigurations(DateTimeOffset? asOfDate = null)
    {
        var asOf = (asOfDate ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var pendingScheduled = db.BusinessConfigurations.AsNoTracking()
            .Where(item => item.TenantId == TenantId && item.Status == ConfigurationStatus.Scheduled && item.EffectiveFrom <= asOf)
            .OrderBy(item => item.EffectiveFrom)
            .ThenBy(item => item.Version)
            .Select(item => new { item.Id, item.Domain, item.Code })
            .ToList();

        if (pendingScheduled.Count == 0) return 0;

        var now = DateTimeOffset.UtcNow;
        var activatedCount = 0;
        foreach (var pending in pendingScheduled)
        {
            using var lockLease = AcquireLock(pending.Domain, pending.Code);
            var current = db.BusinessConfigurations.SingleOrDefault(item => item.TenantId == TenantId && item.Id == pending.Id);
            if (current == null || current.Status != ConfigurationStatus.Scheduled || current.EffectiveFrom > asOf)
            {
                continue;
            }

            var oldEffectives = db.BusinessConfigurations
                .Where(other => other.TenantId == TenantId && other.Domain == current.Domain && other.Code == current.Code &&
                                other.Status == ConfigurationStatus.Effective && other.Id != current.Id)
                .ToList();

            foreach (var old in oldEffectives)
            {
                old.Status = ConfigurationStatus.Retired;
                if (old.EffectiveTo == null || old.EffectiveTo > current.EffectiveFrom)
                {
                    old.EffectiveTo = current.EffectiveFrom;
                }
                old.UpdatedAt = now;
                old.UpdatedBy = "system";
                old.UpdatedByName = "定时生效任务";
                old.ConcurrencyVersion++;
            }

            current.Status = ConfigurationStatus.Effective;
            current.UpdatedAt = now;
            current.UpdatedBy = "system";
            current.UpdatedByName = "定时生效任务";
            current.ConcurrencyVersion++;
            activatedCount++;

            LogAudit(new Employee("system", "系统定时任务", "系统管理员", "general", "总经办", null, 0, "ACTIVE"),
                "CONFIG_SCHEDULED_ACTIVATED", current.Id.ToString(),
                $"定时生效【{current.Domain} / {current.Code}】v{current.Version}，状态切换为 Effective");

            db.SaveChanges();
        }

        return activatedCount;
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

    public EffectiveBusinessConfigurationBundle GetEffectiveBundle(DateTimeOffset? asOfDate = null)
    {
        var asOf = (asOfDate ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var bundle = new EffectiveBusinessConfigurationBundle();

        // 1. Leave
        var leaveRecord = GetEffective(ConfigurationDomains.Leave, "LeavePolicy", asOf);
        var leavePolicy = leaveRecord is not null
            ? JsonSerializer.Deserialize<LeavePolicyConfig>(leaveRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions)
            : BusinessConfigurationDefaults.CreateDefaultLeavePolicy();
        if (leavePolicy is not null)
        {
            bundle.LeaveTypes = leavePolicy.LeaveTypes
                .Where(t => t.IsEnabled)
                .Select(t => new EffectiveLeaveTypeOption
                {
                    Code = t.Type,
                    Name = t.Name,
                    MinUnit = t.MinUnit,
                    RequiresAttachment = t.RequiresAttachment,
                    AttachmentThresholdDays = t.AttachmentThresholdDays
                })
                .ToList();
        }

        // 2. Expense
        var expenseRecord = GetEffective(ConfigurationDomains.Expense, "ExpensePolicy", asOf);
        var expensePolicy = expenseRecord is not null
            ? JsonSerializer.Deserialize<ExpensePolicyConfig>(expenseRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions)
            : BusinessConfigurationDefaults.CreateDefaultExpensePolicy();
        if (expensePolicy is not null)
        {
            bundle.ExpenseCategories = expensePolicy.Categories
                .Where(c => c.IsEnabled)
                .Select(c => new EffectiveExpenseCategoryOption
                {
                    Code = c.Name,
                    Name = c.Name,
                    SingleLimit = c.SingleLimit,
                    RequiresReceipt = c.RequiresReceipt,
                    RequiresReasonWhenExceeded = c.RequiresReasonWhenExceeded,
                    BlockWhenExceeded = c.BlockWhenExceeded
                })
                .ToList();
        }

        // 3. Travel
        var travelRecord = GetEffective(ConfigurationDomains.Travel, "TravelPolicy", asOf);
        var travelPolicy = travelRecord is not null
            ? JsonSerializer.Deserialize<TravelPolicyConfig>(travelRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions)
            : BusinessConfigurationDefaults.CreateDefaultTravelPolicy();
        if (travelPolicy is not null)
        {
            bundle.TravelCityTiers = travelPolicy.CityTiers;
            bundle.TravelEmployeeRanks = travelPolicy.EmployeeRanks;
            bundle.TravelStandards = travelPolicy.Standards;
        }

        // 4. Procurement
        var procRecord = GetEffective(ConfigurationDomains.Procurement, "ProcurementPolicy", asOf);
        var procPolicy = procRecord is not null
            ? JsonSerializer.Deserialize<ProcurementPolicyConfig>(procRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions)
            : BusinessConfigurationDefaults.CreateDefaultProcurementPolicy();
        if (procPolicy is not null)
        {
            bundle.ProcurementCategories = procPolicy.Categories
                .Where(c => c.IsEnabled)
                .Select(c => new EffectiveProcurementCategoryOption { Code = c.Name, Name = c.Name })
                .ToList();
            bundle.ProcurementAmountTiers = procPolicy.AmountTiers
                .Select(t => new EffectiveProcurementAmountTierOption { Name = t.Name, MaxAmount = t.MaxAmount })
                .ToList();
            bundle.ProcurementQuoteThreshold = procPolicy.QuoteAttachmentThreshold;
        }

        // 5. Seal
        var sealRecord = GetEffective(ConfigurationDomains.Seal, "SealPolicy", asOf);
        var sealPolicy = sealRecord is not null
            ? JsonSerializer.Deserialize<SealPolicyConfig>(sealRecord.ContentJson, BusinessConfigurationDefaults.JsonOptions)
            : BusinessConfigurationDefaults.CreateDefaultSealPolicy();
        if (sealPolicy is not null)
        {
            bundle.Seals = sealPolicy.Seals
                .Where(s => s.IsEnabled)
                .Select(s => new EffectiveSealOption
                {
                    Code = s.Name,
                    Name = s.Name,
                    SealType = s.SealType,
                    AllowOut = s.AllowOut,
                    MaxOutDays = s.MaxOutDays
                })
                .ToList();
            bundle.SealDocumentCategories = sealPolicy.DocumentCategories
                .Where(d => d.IsEnabled)
                .Select(d => new EffectiveSealDocCategoryOption
                {
                    Code = d.Name,
                    Name = d.Name,
                    RiskLevel = d.RiskLevel
                })
                .ToList();
        }

        // 6. Dictionaries
        bundle.ContractTypes = ResolveDictionaryOptions("ContractType", asOf, BusinessConfigurationDefaults.CreateDefaultContractTypeDict);
        bundle.AttachmentTypes = ResolveDictionaryOptions("AttachmentType", asOf, BusinessConfigurationDefaults.CreateDefaultAttachmentTypeDict);
        bundle.ApprovalCommentPresets = ResolveDictionaryOptions("ApprovalCommentPreset", asOf, BusinessConfigurationDefaults.CreateDefaultApprovalCommentPresetDict);
        bundle.AnnouncementTypes = ResolveDictionaryOptions("AnnouncementType", asOf, BusinessConfigurationDefaults.CreateDefaultAnnouncementTypeDict);

        return bundle;
    }

    private List<EffectiveDictionaryOption> ResolveDictionaryOptions(string code, DateTimeOffset asOf, Func<DictionaryConfig> defaultFactory)
    {
        var record = GetEffective(ConfigurationDomains.Dictionary, code, asOf);
        DictionaryConfig? dict = null;
        if (record is not null)
        {
            try
            {
                dict = JsonSerializer.Deserialize<DictionaryConfig>(record.ContentJson, BusinessConfigurationDefaults.JsonOptions);
            }
            catch { }
        }
        dict ??= defaultFactory();
        return dict.Items
            .Where(i => i.IsEnabled)
            .OrderBy(i => i.SortOrder)
            .Select(i => new EffectiveDictionaryOption { Code = i.Code, Name = i.Name, SortOrder = i.SortOrder })
            .ToList();
    }
}
