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

    public FileService(OaDbContext db, IWebHostEnvironment environment, IConfiguration configuration)
    {
        this.db = db;
        var configuredRoot = configuration["Storage:Root"];
        storageRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(environment.ContentRootPath, "storage", "files")
            : Path.GetFullPath(configuredRoot, environment.ContentRootPath);
    }

    public ServiceResult<FileDescriptor> Upload(Employee actor, IFormFile file)
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
        try
        {
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var input = file.OpenReadStream())
                input.CopyTo(output);

            var record = new FileRecord { Id = id, TenantId = TenantId, OwnerId = actor.Id, OriginalName = name, StoredName = storedName, ContentType = contentType, Size = file.Length };
            db.Files.Add(record);
            Audit(actor, "FILE_UPLOADED", id, $"上传文件 {name}");
            db.SaveChanges();
            return ServiceResult<FileDescriptor>.Success(ToDescriptor(record));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(destination)) File.Delete(destination);
            return ServiceResult<FileDescriptor>.Failure("文件保存失败，请稍后重试。", "FILE_003");
        }
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

    private void Audit(Employee actor, string action, Guid fileId, string summary) => db.AuditLogs.Add(new AuditRecord { TenantId = TenantId, ActorId = actor.Id, Action = action, ResourceType = "File", ResourceId = fileId.ToString(), Summary = summary });
    private static FileDescriptor ToDescriptor(FileRecord record) => new(record.Id, record.OriginalName, record.ContentType, record.Size, record.CreatedAt);
}
