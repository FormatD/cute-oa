using System.Text;
using Npgsql;

namespace Oa.Api.Services;

public static class ProductionConfigurationPolicy
{
    private const string DevelopmentSigningKeyMarker = "development-signing-key";

    public static void Validate(IConfiguration configuration, bool isDevelopment)
    {
        if (isDevelopment) return;

        var errors = new List<string>();
        ValidateAuthentication(configuration, errors);
        ValidatePersistence(configuration, errors);
        ValidateCors(configuration, errors);
        ValidateStorage(configuration, errors);
        ValidateHostFiltering(configuration, errors);
        ValidateLoginProtection(configuration, errors);
        ValidateForwardedHeaders(configuration, errors);
        ValidateBootstrap(configuration, errors);
        ValidatePersonnelCases(configuration, errors);
        ValidateFlowSla(configuration, errors);
        if (configuration.GetValue<bool>("DemoFeatures:AllowDataGeneration"))
            errors.Add("DemoFeatures:AllowDataGeneration 必须为 false");

        if (errors.Count > 0)
            throw new InvalidOperationException($"生产配置校验失败：{string.Join("；", errors)}。");
    }

    private static void ValidateAuthentication(IConfiguration configuration, ICollection<string> errors)
    {
        var signingKey = configuration["Authentication:SigningKey"] ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
            errors.Add("Authentication:SigningKey 必须至少 32 字节");
        else if (signingKey.Contains(DevelopmentSigningKeyMarker, StringComparison.OrdinalIgnoreCase) || signingKey.Contains("change-before-production", StringComparison.OrdinalIgnoreCase))
            errors.Add("Authentication:SigningKey 不能使用开发示例密钥");

        if (!configuration.GetValue<bool>("Authentication:MultiFactor:Enabled"))
        {
            errors.Add("Authentication:MultiFactor:Enabled 必须为 true");
            return;
        }

        var encryptionKey = configuration["Authentication:MultiFactor:EncryptionKey"] ?? string.Empty;
        try
        {
            var decoded = Convert.FromBase64String(encryptionKey);
            if (decoded.Length != 32 || decoded.Distinct().Count() < 8)
                errors.Add("Authentication:MultiFactor:EncryptionKey 必须是高熵 32 字节 Base64 密钥");
        }
        catch (FormatException)
        {
            errors.Add("Authentication:MultiFactor:EncryptionKey 必须是高熵 32 字节 Base64 密钥");
        }

        var requiredPermissions = configuration.GetSection("Authentication:MultiFactor:RequiredPermissions").Get<string[]>() ?? [];
        foreach (var permission in new[] { OaPermissions.UserManage, OaPermissions.PersonnelExport, OaPermissions.PersonnelManage, OaPermissions.AttendanceManage, OaPermissions.ContractManage, OaPermissions.PurchaseManage, OaPermissions.SealManage, OaPermissions.DocumentManage }.Where(permission => !requiredPermissions.Contains(permission)))
            errors.Add($"Authentication:MultiFactor:RequiredPermissions 必须包含 {permission}");
    }

    private static void ValidatePersistence(IConfiguration configuration, ICollection<string> errors)
    {
        if (!configuration.GetValue<bool>("Persistence:UsePostgreSql"))
        {
            errors.Add("Persistence:UsePostgreSql 必须为 true");
            return;
        }

        var connectionString = configuration.GetConnectionString("OaDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add("ConnectionStrings:OaDatabase 不能为空");
            return;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (builder.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
                errors.Add("ConnectionStrings:OaDatabase 必须启用 SSL Mode=Require/VerifyCA/VerifyFull");
            if (string.Equals(builder.Username, "postgres", StringComparison.OrdinalIgnoreCase))
                errors.Add("ConnectionStrings:OaDatabase 不得使用 postgres 超级用户");
            if (!builder.Pooling || builder.MaxPoolSize is < 1 or > 200)
                errors.Add("ConnectionStrings:OaDatabase 必须启用连接池且 MaxPoolSize 为 1–200");
        }
        catch (ArgumentException)
        {
            errors.Add("ConnectionStrings:OaDatabase 格式无效");
        }
    }

    private static void ValidateCors(IConfiguration configuration, ICollection<string> errors)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0)
        {
            errors.Add("Cors:AllowedOrigins 至少配置一个 HTTPS 前端源");
            return;
        }

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                uri.IsLoopback ||
                uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                errors.Add($"Cors:AllowedOrigins 包含无效生产源 {origin}");
            }
        }
    }

    private static void ValidateStorage(IConfiguration configuration, ICollection<string> errors)
    {
        var storageRoot = configuration["Storage:Root"]?.Trim();
        if (string.IsNullOrWhiteSpace(storageRoot) || !Path.IsPathFullyQualified(storageRoot))
            errors.Add("Storage:Root 必须是持久卷绝对路径");
    }

    private static void ValidateHostFiltering(IConfiguration configuration, ICollection<string> errors)
    {
        var hosts = (configuration["AllowedHosts"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Any(host => host == "*" || host.Contains('*')))
            errors.Add("AllowedHosts 必须配置明确主机名且不能使用通配符");
    }

    private static void ValidateLoginProtection(IConfiguration configuration, ICollection<string> errors)
    {
        var settings = LoginProtectionSettings.From(configuration);
        if (!settings.Enabled) errors.Add("LoginProtection:Enabled 必须为 true");
        foreach (var error in settings.Validate()) errors.Add($"LoginProtection:{error}");
    }

    private static void ValidateForwardedHeaders(IConfiguration configuration, ICollection<string> errors)
    {
        var proxies = ReverseProxyPolicy.ConfiguredValues(configuration);
        if (proxies.Count == 0)
        {
            errors.Add("ForwardedHeaders:KnownProxies 至少配置一个可信反向代理地址");
            return;
        }
        foreach (var proxy in proxies.Where(proxy => !ReverseProxyPolicy.TryParse(proxy, out _)))
            errors.Add($"ForwardedHeaders:KnownProxies 包含无效地址 {proxy}");
    }

    private static void ValidateBootstrap(IConfiguration configuration, ICollection<string> errors)
    {
        if (!configuration.GetValue<bool>("Bootstrap:Enabled")) return;

        var userId = configuration["Bootstrap:AdminUserId"]?.Trim() ?? string.Empty;
        var name = configuration["Bootstrap:AdminName"]?.Trim() ?? string.Empty;
        var departmentId = configuration["Bootstrap:DepartmentId"]?.Trim() ?? string.Empty;
        var departmentName = configuration["Bootstrap:DepartmentName"]?.Trim() ?? string.Empty;
        var employeeNumber = configuration["Bootstrap:AdminEmployeeNumber"]?.Trim() ?? string.Empty;
        var hireDate = configuration["Bootstrap:AdminHireDate"]?.Trim() ?? string.Empty;
        var password = configuration["Bootstrap:AdminPassword"] ?? string.Empty;
        if (userId.Length is < 2 or > 64 || userId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not '@'))
            errors.Add("Bootstrap:AdminUserId 必须是 2–64 位字母、数字或 -_.@");
        else if (IdentityDefaults.Employees.Any(employee => employee.Id.Equals(userId, StringComparison.Ordinal)))
            errors.Add("Bootstrap:AdminUserId 不能使用演示账号 ID");
        if (name.Length is < 2 or > 100)
            errors.Add("Bootstrap:AdminName 必须是 2–100 个字符");
        if (departmentId.Length is < 2 or > 64 || departmentId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            errors.Add("Bootstrap:DepartmentId 必须是 2–64 位字母、数字或 -_.");
        if (departmentName.Length is < 2 or > 100)
            errors.Add("Bootstrap:DepartmentName 必须是 2–100 个字符");
        if (employeeNumber.Length is < 2 or > 32 || employeeNumber.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
            errors.Add("Bootstrap:AdminEmployeeNumber 必须是 2–32 位字母、数字或横线");
        if (!DateOnly.TryParseExact(hireDate, "yyyy-MM-dd", out var parsedHireDate) || parsedHireDate > BusinessTime.ChinaToday().AddDays(90))
            errors.Add("Bootstrap:AdminHireDate 必须是 yyyy-MM-dd 且不能晚于当前日期后 90 天");
        if (!PasswordHasher.MeetsProductionPolicy(password) || password.Equals(IdentityDefaults.DemoPassword, StringComparison.Ordinal))
            errors.Add("Bootstrap:AdminPassword 必须是 12–128 位且包含大小写字母、数字和特殊字符，并且不能使用演示密码");
    }

    private static void ValidatePersonnelCases(IConfiguration configuration, ICollection<string> errors)
    {
        foreach (var category in new[] { "HR", "FINANCE", "IT", "ADMIN" })
        {
            var userId = configuration[$"PersonnelCases:CategoryAssignees:{category}"]?.Trim() ?? string.Empty;
            if (userId.Length is < 2 or > 64 || userId.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not '@'))
                errors.Add($"PersonnelCases:CategoryAssignees:{category} 必须配置有效负责人用户 ID");
        }
        if (!configuration.GetValue<bool>("PersonnelCaseAlerts:Enabled"))
            errors.Add("PersonnelCaseAlerts:Enabled 必须为 true");
        var interval = configuration.GetValue("PersonnelCaseAlerts:IntervalMinutes", 60);
        var dueSoon = configuration.GetValue("PersonnelCaseAlerts:DueSoonDays", 1);
        var escalation = configuration.GetValue("PersonnelCaseAlerts:EscalateAfterDays", 3);
        var maximumExportRows = configuration.GetValue("PersonnelExport:MaxRows", 10_000);
        if (interval is < 5 or > 1_440) errors.Add("PersonnelCaseAlerts:IntervalMinutes 必须为 5–1440");
        if (dueSoon is < 0 or > 30) errors.Add("PersonnelCaseAlerts:DueSoonDays 必须为 0–30");
        if (escalation is < 1 or > 90) errors.Add("PersonnelCaseAlerts:EscalateAfterDays 必须为 1–90");
        if (maximumExportRows is < 1 or > 10_000) errors.Add("PersonnelExport:MaxRows 必须为 1–10000");
    }

    private static void ValidateFlowSla(IConfiguration configuration, ICollection<string> errors)
    {
        if (!configuration.GetValue<bool>("FlowSla:Enabled")) errors.Add("FlowSla:Enabled 必须为 true");
        var interval = configuration.GetValue("FlowSla:IntervalMinutes", 15);
        if (interval is < 1 or > 1_440) errors.Add("FlowSla:IntervalMinutes 必须为 1–1440");
    }
}
