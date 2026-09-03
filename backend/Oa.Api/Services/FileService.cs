using Oa.Api.Domain;
using Oa.Api.Persistence;
using System.Text;

namespace Oa.Api.Services;

public sealed record FileDescriptor(Guid Id, string Name, string ContentType, long Size, DateTimeOffset CreatedAt);

public sealed class FileService
{
    private const string TenantId = "demo";
    private const long MaxFileSize = 20 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    private readonly OaDbContext db;
    private readonly string storageRoot;
    private readonly IFileMalwareScanner malwareScanner;
    private readonly ILogger<FileService>? logger;

    public FileService(OaDbContext db, IWebHostEnvironment environment, IConfiguration configuration, IFileMalwareScanner malwareScanner, ILogger<FileService>? logger = null)
    {
        this.db = db;
        this.malwareScanner = malwareScanner;
        this.logger = logger;
        var configuredRoot = configuration["Storage:Root"];
        storageRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(environment.ContentRootPath, "storage", "files")
            : Path.GetFullPath(configuredRoot, environment.ContentRootPath);
    }

    public async Task<ServiceResult<FileDescriptor>> UploadAsync(Employee actor, IFormFile file, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(file.FileName ?? string.Empty).Trim();
        var extension = Path.GetExtension(name);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || !AllowedTypes.TryGetValue(extension, out var contentType))
            return ServiceResult<FileDescriptor>.Failure("仅允许上传 PDF、图片、Office 文档。", "FILE_001");
        if (file.Length <= 0 || file.Length > MaxFileSize)
            return ServiceResult<FileDescriptor>.Failure("文件大小必须在 1 字节至 20MB 之间。", "FILE_002");

        Directory.CreateDirectory(storageRoot);
        var id = Guid.NewGuid();
        var storedName = $"{id:N}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(storageRoot, storedName);
        var quarantine = Path.Combine(storageRoot, $".{id:N}.upload");
        try
        {
            await using (var output = new FileStream(quarantine, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var input = file.OpenReadStream())
                await input.CopyToAsync(output, cancellationToken);

            if (!FileContentValidator.MatchesExtension(quarantine, extension))
            {
                logger?.LogWarning("File upload {FileId} was blocked because its content does not match extension {Extension}", id, extension);
                Audit(actor, "FILE_VALIDATION_BLOCKED", id, $"文件内容与扩展名不匹配，已阻断上传 {name}");
                await db.SaveChangesAsync(cancellationToken);
                return ServiceResult<FileDescriptor>.Failure("文件内容与扩展名不匹配，已拒绝上传。", "FILE_001");
            }

            var scan = await malwareScanner.ScanAsync(quarantine, cancellationToken);
            if (scan.Status != FileScanStatus.Clean)
            {
                var infected = scan.Status == FileScanStatus.Infected;
                if (infected)
                    logger?.LogWarning("File upload {FileId} was blocked by {Scanner}; threat={ThreatName}", id, malwareScanner.Provider, scan.ThreatName ?? "unknown");
                else
                    logger?.LogError("File upload {FileId} was rejected because scanner {Scanner} is unavailable; detail={Detail}", id, malwareScanner.Provider, scan.Detail ?? "unknown");
                Audit(actor, infected ? "FILE_SCAN_BLOCKED" : "FILE_SCAN_FAILED", id,
                    infected ? $"恶意文件扫描已阻断上传 {name}" : $"扫描服务不可用，已拒绝上传 {name}");
                await db.SaveChangesAsync(cancellationToken);
                return ServiceResult<FileDescriptor>.Failure(
                    infected ? "文件未通过安全扫描，已拒绝上传。" : "文件安全扫描暂时不可用，请稍后重试。",
                    infected ? "FILE_006" : "FILE_007");
            }

            File.Move(quarantine, destination);

            var record = new FileRecord { Id = id, TenantId = TenantId, OwnerId = actor.Id, OriginalName = name, StoredName = storedName, ContentType = contentType, Size = file.Length };
            db.Files.Add(record);
            Audit(actor, "FILE_UPLOADED", id, $"上传文件 {name}");
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<FileDescriptor>.Success(ToDescriptor(record));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(quarantine)) File.Delete(quarantine);
            if (File.Exists(destination)) File.Delete(destination);
            return ServiceResult<FileDescriptor>.Failure("文件保存失败，请稍后重试。", "FILE_003");
        }
        catch
        {
            if (File.Exists(destination)) File.Delete(destination);
            throw;
        }
        finally { if (File.Exists(quarantine)) File.Delete(quarantine); }
    }

    public ServiceResult<FileDescriptor> CreateDemoPdf(Employee actor, string requestedName)
    {
        var name = Path.GetFileName(requestedName).Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "demo-employment-contract.pdf";
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) name += ".pdf";
        if (name.Length > 255) return ServiceResult<FileDescriptor>.Failure("模拟文件名过长。", "FILE_001");
        Directory.CreateDirectory(storageRoot);
        var id = Guid.NewGuid();
        var storedName = $"{id:N}.pdf";
        var destination = Path.Combine(storageRoot, storedName);
        var content = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
        try
        {
            File.WriteAllBytes(destination, content);
            var record = new FileRecord { Id = id, TenantId = TenantId, OwnerId = actor.Id, OriginalName = name, StoredName = storedName, ContentType = "application/pdf", Size = content.Length };
            db.Files.Add(record);
            Audit(actor, "FILE_DEMO_CREATED", id, $"生成模拟文件 {name}");
            db.SaveChanges();
            return ServiceResult<FileDescriptor>.Success(ToDescriptor(record));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(destination)) File.Delete(destination);
            return ServiceResult<FileDescriptor>.Failure("模拟文件保存失败。", "FILE_003");
        }
    }

    public ServiceResult<(FileDescriptor Descriptor, Stream Content)> Open(Employee actor, Guid id)
    {
        var record = db.Files.SingleOrDefault(item => item.Id == id && item.TenantId == TenantId);
        if (record is null) return ServiceResult<(FileDescriptor, Stream)>.Failure("文件不存在。", "DATA_001");
        var path = Path.Combine(storageRoot, record.StoredName);
        if (!File.Exists(path)) return ServiceResult<(FileDescriptor, Stream)>.Failure("文件内容不存在。", "FILE_004");
        Audit(actor, "FILE_DOWNLOADED", id, $"下载文件 {record.OriginalName}");
        db.SaveChanges();
        return ServiceResult<(FileDescriptor, Stream)>.Success((ToDescriptor(record), new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)));
    }

    public bool AreOwnedBy(Employee actor, IEnumerable<string> ids)
    {
        var parsed = ids.Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).ToList();
        return parsed.All(id => id != Guid.Empty) && parsed.Distinct().Count() == parsed.Count &&
            db.Files.Count(file => file.TenantId == TenantId && file.OwnerId == actor.Id && parsed.Contains(file.Id)) == parsed.Count;
    }

    public bool IsStorageReady()
    {
        var probe = Path.Combine(storageRoot, $".oa-readiness-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(storageRoot);
            File.WriteAllText(probe, "ready");
            return File.Exists(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
        finally { if (File.Exists(probe)) File.Delete(probe); }
    }

    public bool IsScanningEnabled => malwareScanner.Enabled;
    public string ScanningProvider => malwareScanner.Provider;
    public Task<bool> IsScanningReadyAsync(CancellationToken cancellationToken = default) => malwareScanner.IsReadyAsync(cancellationToken);

    private void Audit(Employee actor, string action, Guid fileId, string summary) => db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "File", ResourceId = fileId.ToString(), Summary = summary });
    private static FileDescriptor ToDescriptor(FileRecord record) => new(record.Id, record.OriginalName, record.ContentType, record.Size, record.CreatedAt);
}
