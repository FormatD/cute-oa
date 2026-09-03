using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oa.Api.Persistence;
using Oa.Api.Services;

const int MaximumInputBytes = 5 * 1024 * 1024;

if (args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
{
    PrintUsage();
    return 0;
}

var apply = args.Contains("--apply", StringComparer.Ordinal);
var inputPath = ReadOption(args, "--file");
if (string.IsNullOrWhiteSpace(inputPath)) return Fail("必须通过 --file 指定 JSON 导入文件。", 2);
inputPath = Path.GetFullPath(inputPath);
if (!File.Exists(inputPath)) return Fail("导入文件不存在。", 2);
if (!HasPrivatePermissions(inputPath)) return Fail("导入文件权限过宽；仅允许文件所有者读写，请设置为 0600。", 2);
var inputInfo = new FileInfo(inputPath);
if (inputInfo.Length is < 2 or > MaximumInputBytes) return Fail($"导入文件必须为 2–{MaximumInputBytes} 字节。", 2);

var actorId = Environment.GetEnvironmentVariable("OA_HR_IMPORT_ACTOR_ID")?.Trim();
if (string.IsNullOrWhiteSpace(actorId)) return Fail("必须设置 OA_HR_IMPORT_ACTOR_ID。", 2);
var connectionFile = Environment.GetEnvironmentVariable("OA_HR_IMPORT_CONNECTION_FILE")?.Trim();
if (string.IsNullOrWhiteSpace(connectionFile) || !Path.IsPathFullyQualified(connectionFile) || !File.Exists(connectionFile))
    return Fail("OA_HR_IMPORT_CONNECTION_FILE 必须指向存在的绝对 secret 文件。", 2);
if (!HasPrivatePermissions(connectionFile)) return Fail("数据库连接 secret 权限过宽；仅允许文件所有者读写，请设置为 0600。", 2);
var connectionString = (await File.ReadAllTextAsync(connectionFile)).Trim();
if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Length > 8_192)
    return Fail("数据库连接 secret 为空或超出限制。", 2);

List<PersonnelProfileImportRow>? rows;
try
{
    await using var input = File.OpenRead(inputPath);
    rows = await JsonSerializer.DeserializeAsync<List<PersonnelProfileImportRow>>(input, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });
}
catch (JsonException exception)
{
    return Fail($"JSON 格式无效：{exception.Message}", 2);
}
catch (IOException exception)
{
    return Fail($"无法读取导入文件：{exception.Message}", 2);
}
if (rows is null) return Fail("导入文件必须是 JSON 数组。", 2);

try
{
    var options = new DbContextOptionsBuilder<OaDbContext>().UseNpgsql(connectionString).Options;
    await using var db = new OaDbContext(options);
    if (!await db.Database.CanConnectAsync()) return Fail("无法连接数据库。", 3);
    var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
    if (pendingMigrations.Count > 0) return Fail($"数据库存在 {pendingMigrations.Count} 个未应用迁移，禁止导入。", 3);

    var result = new PersonnelProfileImportService(db).Execute(actorId, rows, Path.GetFileName(inputPath), apply);
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"导入预检失败：{result.Code}，共 {result.Issues.Count} 个问题。");
        foreach (var issue in result.Issues.Take(50))
            Console.Error.WriteLine($"- 行 {issue.Row} / 用户 {issue.UserId ?? "-"} / {issue.Field}: {issue.Message}");
        if (result.Issues.Count > 50) Console.Error.WriteLine($"- 其余 {result.Issues.Count - 50} 个问题已省略。");
        return 4;
    }

    if (result.Applied)
    {
        Console.WriteLine($"导入成功：已原子补录 {result.Ready} 份人事档案；数据库剩余缺档账号 {result.RemainingProfiles} 个。");
    }
    else
    {
        Console.WriteLine($"预检通过：{result.Ready} 行可导入；数据库当前缺档账号 {result.RemainingProfiles} 个。未写入任何数据；确认后增加 --apply。 ");
    }
    return 0;
}
catch (Exception exception)
{
    return Fail($"导入工具执行失败：{exception.GetType().Name}: {exception.Message}", 5);
}

static string? ReadOption(IReadOnlyList<string> arguments, string name)
{
    for (var index = 0; index < arguments.Count; index++)
        if (arguments[index] == name && index + 1 < arguments.Count) return arguments[index + 1];
    return null;
}

static int Fail(string message, int code)
{
    Console.Error.WriteLine(message);
    return code;
}

static bool HasPrivatePermissions(string path)
{
    if (OperatingSystem.IsWindows()) return true;
    var mode = File.GetUnixFileMode(path);
    const UnixFileMode forbidden = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                   UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
    return (mode & forbidden) == 0;
}

static void PrintUsage()
{
    Console.WriteLine("""
        人事档案离线补录工具（默认只预检）

        必需环境变量：
          OA_HR_IMPORT_ACTOR_ID          具备 PERSONNEL_MANAGE 权限的有效用户 ID
          OA_HR_IMPORT_CONNECTION_FILE   保存 PostgreSQL 连接串的绝对 secret 文件路径

        用法：
          dotnet run --project tools/Oa.HrProfileImport -- --file <profiles.json>
          dotnet run --project tools/Oa.HrProfileImport -- --file <profiles.json> --apply

        --apply 会在一个数据库事务中写入整批档案、初始事件和审计；任一失败整批回滚。
        """);
}
