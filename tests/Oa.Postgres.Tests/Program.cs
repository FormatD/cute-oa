using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Oa.Api.Domain;
using Oa.Api.Persistence;
using Oa.Api.Services;
using System.Buffers.Binary;
using System.Data.Common;
using System.Net;
using System.Net.Sockets;

if (FileScanningPolicy.ValidateMode("Disabled", true) != "Disabled" || FileScanningPolicy.ValidateMode("ClamAv", false) != "ClamAv")
    throw new InvalidOperationException("文件扫描环境策略未返回规范模式。");
try
{
    FileScanningPolicy.ValidateMode("Disabled", false);
    throw new InvalidOperationException("生产环境可以关闭恶意文件扫描。");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("非开发环境必须启用", StringComparison.Ordinal)) { }

var scannerFixturePath = Path.Combine(Path.GetTempPath(), $"oa-clamav-protocol-{Guid.NewGuid():N}.pdf");
try
{
    await File.WriteAllBytesAsync(scannerFixturePath, "%PDF-1.4\nscanner protocol fixture"u8.ToArray());
    await using (var cleanServer = new TestClamAvServer("stream: OK", 2))
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileScanning:ClamAv:Host"] = "127.0.0.1",
            ["FileScanning:ClamAv:Port"] = cleanServer.Port.ToString(),
            ["FileScanning:ClamAv:TimeoutSeconds"] = "5"
        }).Build();
        var scanner = new ClamAvFileMalwareScanner(configuration);
        if (!await scanner.IsReadyAsync() || (await scanner.ScanAsync(scannerFixturePath)).Status != FileScanStatus.Clean || cleanServer.StreamedBytes == 0)
            throw new InvalidOperationException("ClamAV PING、INSTREAM 帧或安全响应解析失败。");
    }
    await using (var infectedServer = new TestClamAvServer("stream: Eicar-Test-Signature FOUND", 1))
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileScanning:ClamAv:Host"] = "127.0.0.1",
            ["FileScanning:ClamAv:Port"] = infectedServer.Port.ToString(),
            ["FileScanning:ClamAv:TimeoutSeconds"] = "5"
        }).Build();
        var result = await new ClamAvFileMalwareScanner(configuration).ScanAsync(scannerFixturePath);
        if (result is not { Status: FileScanStatus.Infected, ThreatName: "Eicar-Test-Signature" })
            throw new InvalidOperationException("ClamAV 威胁响应未被正确识别。");
    }
}
finally { if (File.Exists(scannerFixturePath)) File.Delete(scannerFixturePath); }

var connectionString = "Host=localhost;Port=5433;Database=oa_test;Username=oa;Password=oa_dev_password";
var configuredConnectionString = Environment.GetEnvironmentVariable("OA_TEST_CONNECTION");
if (!string.IsNullOrWhiteSpace(configuredConnectionString)) connectionString = configuredConnectionString;
var options = new DbContextOptionsBuilder<OaDbContext>().UseNpgsql(connectionString).Options;

var bootstrapSchema = $"oa_bootstrap_{Guid.NewGuid():N}";
await using (var bootstrapConnection = new NpgsqlConnection(connectionString))
{
    await bootstrapConnection.OpenAsync();
    await using var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{bootstrapSchema}\"", bootstrapConnection);
    await createSchema.ExecuteNonQueryAsync();
}
try
{
    var bootstrapConnectionBuilder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = bootstrapSchema };
    var bootstrapOptions = new DbContextOptionsBuilder<OaDbContext>().UseNpgsql(bootstrapConnectionBuilder.ConnectionString).Options;
    await using var bootstrapDb = new OaDbContext(bootstrapOptions);
    await bootstrapDb.Database.MigrateAsync();
    try
    {
        IdentitySeeder.EnsureProductionReady(bootstrapDb, new ConfigurationBuilder().Build());
        throw new InvalidOperationException("空生产库在未启用安全引导时仍可启动。 ");
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("生产库没有用户", StringComparison.Ordinal)) { }

    const string bootstrapUserId = "oa-production-admin";
    const string bootstrapPassword = "Production#Bootstrap2026";
    var bootstrapConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Bootstrap:Enabled"] = "true",
        ["Bootstrap:AdminUserId"] = bootstrapUserId,
        ["Bootstrap:AdminName"] = "生产引导管理员",
        ["Bootstrap:DepartmentId"] = "management",
        ["Bootstrap:DepartmentName"] = "管理部",
        ["Bootstrap:AdminEmployeeNumber"] = "ADMIN-PG-001",
        ["Bootstrap:AdminHireDate"] = "2026-01-01",
        ["Bootstrap:AdminPassword"] = bootstrapPassword
    }).Build();
    IdentitySeeder.EnsureProductionReady(bootstrapDb, bootstrapConfiguration);
    var bootstrapAccount = await bootstrapDb.UserAccounts.AsNoTracking().SingleAsync(item => item.UserId == bootstrapUserId);
    if (!bootstrapAccount.MustChangePassword ||
        !PasswordHasher.Verify(bootstrapPassword, bootstrapAccount.PasswordSalt, bootstrapAccount.PasswordHash, bootstrapAccount.PasswordIterations) ||
        PasswordHasher.Verify(IdentityDefaults.DemoPassword, bootstrapAccount.PasswordSalt, bootstrapAccount.PasswordHash, bootstrapAccount.PasswordIterations) ||
        !await bootstrapDb.UserRoles.AnyAsync(item => item.UserId == bootstrapUserId && item.RoleCode == "系统管理员" && item.IsPrimary) ||
        !await bootstrapDb.PersonnelProfiles.AnyAsync(item => item.UserId == bootstrapUserId && item.EmployeeNumber == "ADMIN-PG-001" && item.HireDate == new DateOnly(2026, 1, 1)) ||
        !await bootstrapDb.PersonnelEvents.AnyAsync(item => item.UserId == bootstrapUserId && item.EventType == "IMPORTED") ||
        !await bootstrapDb.AuditLogs.AnyAsync(item => item.Action == "PRODUCTION_ADMIN_BOOTSTRAPPED" && item.ResourceId == bootstrapUserId))
        throw new InvalidOperationException("生产引导未原子创建强密码管理员、人事档案、系统角色或审计证据。 ");
    try
    {
        IdentitySeeder.EnsureProductionReady(bootstrapDb, bootstrapConfiguration);
        throw new InvalidOperationException("生产管理员引导可以在已初始化数据库上重复运行。 ");
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("Bootstrap:Enabled=false", StringComparison.Ordinal)) { }
    const string profileGapUserId = "oa-profile-gap";
    const string secondProfileGapUserId = "oa-profile-gap-2";
    var profileGapPassword = PasswordHasher.Hash("ProfileGap#2026");
    bootstrapDb.Users.Add(new UserRecord { Id = profileGapUserId, TenantId = IdentityDefaults.TenantId, Name = "待补档员工", DepartmentId = "management", Status = "ACTIVE" });
    bootstrapDb.Users.Add(new UserRecord { Id = secondProfileGapUserId, TenantId = IdentityDefaults.TenantId, Name = "第二名待补档员工", DepartmentId = "management", Status = "ACTIVE" });
    bootstrapDb.UserRoles.Add(new UserRoleRecord { UserId = profileGapUserId, RoleCode = "员工", IsPrimary = true });
    bootstrapDb.UserRoles.Add(new UserRoleRecord { UserId = secondProfileGapUserId, RoleCode = "员工", IsPrimary = true });
    bootstrapDb.UserAccounts.Add(new UserAccountRecord { UserId = profileGapUserId, PasswordSalt = profileGapPassword.Salt, PasswordHash = profileGapPassword.Hash, PasswordIterations = profileGapPassword.Iterations });
    var secondProfileGapPassword = PasswordHasher.Hash("SecondProfileGap#2026");
    bootstrapDb.UserAccounts.Add(new UserAccountRecord { UserId = secondProfileGapUserId, PasswordSalt = secondProfileGapPassword.Salt, PasswordHash = secondProfileGapPassword.Hash, PasswordIterations = secondProfileGapPassword.Iterations });
    await bootstrapDb.SaveChangesAsync();
    try
    {
        IdentitySeeder.EnsureProductionReady(bootstrapDb, new ConfigurationBuilder().Build());
        throw new InvalidOperationException("生产库存在缺失人事档案的账号时仍可启动。 ");
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("账号缺少人事档案", StringComparison.Ordinal)) { }
    var importer = new PersonnelProfileImportService(bootstrapDb);
    var importRows = new List<PersonnelProfileImportRow>
    {
        new(profileGapUserId, "EMP-RECOVERED-001", new DateOnly(2026, 2, 1), EmploymentTypes.FullTime, PersonnelStatuses.Active, CumulativeWorkStartDate: new DateOnly(2020, 1, 1), WorkEmail: "recovered-1@example.com"),
        new(secondProfileGapUserId, "EMP-RECOVERED-002", new DateOnly(2026, 3, 1), EmploymentTypes.FullTime, PersonnelStatuses.Probation, ProbationEndDate: new DateOnly(2026, 6, 1), WorkEmail: "recovered-2@example.com")
    };
    var invalidImport = importer.Execute(bootstrapUserId, [importRows[0], importRows[1] with { WorkEmail = "invalid-email" }], "pg-invalid-import.json", true);
    if (invalidImport.IsSuccess || await bootstrapDb.PersonnelProfiles.AnyAsync(item => item.UserId == profileGapUserId || item.UserId == secondProfileGapUserId))
        throw new InvalidOperationException("人事档案导入预检失败后仍写入了部分档案。 ");
    var importPreview = importer.Execute(bootstrapUserId, importRows, "pg-profile-import.json", false);
    if (!importPreview.IsSuccess || importPreview.Applied || importPreview.Ready != 2 || await bootstrapDb.PersonnelProfiles.AnyAsync(item => item.UserId == profileGapUserId || item.UserId == secondProfileGapUserId))
        throw new InvalidOperationException("人事档案导入默认预检产生写入或未返回正确摘要。 ");
    var appliedImport = importer.Execute(bootstrapUserId, importRows, "pg-profile-import.json", true);
    if (!appliedImport.IsSuccess || !appliedImport.Applied || appliedImport.RemainingProfiles != 0 ||
        await bootstrapDb.PersonnelProfiles.CountAsync(item => item.UserId == profileGapUserId || item.UserId == secondProfileGapUserId) != 2 ||
        await bootstrapDb.PersonnelEvents.CountAsync(item => (item.UserId == profileGapUserId || item.UserId == secondProfileGapUserId) && item.EventType == "IMPORTED") != 2 ||
        await bootstrapDb.AuditLogs.CountAsync(item => (item.ResourceId == profileGapUserId || item.ResourceId == secondProfileGapUserId) && item.Action == "PERSONNEL_PROFILE_IMPORTED") != 2)
        throw new InvalidOperationException("人事档案导入未原子写入档案、初始事件和审计，或仍有缺档账号。 ");
    IdentitySeeder.EnsureProductionReady(bootstrapDb, new ConfigurationBuilder().Build());
}
finally
{
    await using var bootstrapConnection = new NpgsqlConnection(connectionString);
    await bootstrapConnection.OpenAsync();
    await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{bootstrapSchema}\" CASCADE", bootstrapConnection);
    await dropSchema.ExecuteNonQueryAsync();
}

var attendanceSnapshotMigrationSchema = $"oa_attendance_snapshot_{Guid.NewGuid():N}";
await using (var migrationConnection = new NpgsqlConnection(connectionString))
{
    await migrationConnection.OpenAsync();
    await using var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{attendanceSnapshotMigrationSchema}\"", migrationConnection);
    await createSchema.ExecuteNonQueryAsync();
}
try
{
    var migrationConnectionBuilder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = attendanceSnapshotMigrationSchema };
    var migrationOptions = new DbContextOptionsBuilder<OaDbContext>().UseNpgsql(migrationConnectionBuilder.ConnectionString).Options;
    await using var migrationDb = new OaDbContext(migrationOptions);
    var migrator = migrationDb.Database.GetService<IMigrator>();
    await migrator.MigrateAsync("20260902010000_AddMandatoryInitialPasswordChange");
    IdentitySeeder.EnsureDemoSeeded(migrationDb);
    var legacyMonth = new DateOnly(2026, 6, 1);
    migrationDb.AttendanceRecords.Add(new AttendanceRecordEntity
    {
        TenantId = IdentityDefaults.TenantId,
        UserId = "u-sun",
        EmployeeName = "孙悦",
        DepartmentId = "hr",
        DepartmentName = "行政人事部",
        WorkDate = legacyMonth,
        ShiftCode = "STANDARD",
        ShiftName = "标准班次",
        ScheduledStart = new TimeOnly(9, 0),
        ScheduledEnd = new TimeOnly(18, 0),
        CheckInAt = new DateTimeOffset(2026, 6, 1, 1, 0, 0, TimeSpan.Zero),
        CheckOutAt = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        Status = AttendanceStatuses.Normal,
        WorkedMinutes = 480,
        Source = "IMPORT",
        UpdatedBy = "u-sun"
    });
    migrationDb.AttendanceMonthLocks.Add(new AttendanceMonthLockRecord
    {
        TenantId = IdentityDefaults.TenantId,
        Month = legacyMonth,
        IsLocked = true,
        LockReason = "迁移前历史封账记录",
        LockedBy = "u-sun",
        LockedByName = "孙悦",
        LockedAt = DateTimeOffset.UtcNow
    });
    await migrationDb.SaveChangesAsync();
    await migrator.MigrateAsync();
    migrationDb.ChangeTracker.Clear();
    var migratedSnapshot = await migrationDb.AttendanceMonthSnapshots.AsNoTracking().SingleAsync(item => item.Month == legacyMonth);
    var migratedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(migratedSnapshot.SnapshotJson))).ToLowerInvariant();
    var migratedRows = System.Text.Json.JsonSerializer.Deserialize<List<AttendanceMonthlySummaryView>>(migratedSnapshot.SnapshotJson);
    if (migratedSnapshot is not { Sequence: 1, RowCount: 1 } || migratedSnapshot.SnapshotHash != migratedHash || migratedRows?.SingleOrDefault() is not { UserId: "u-sun", NormalDays: 1, WorkedMinutes: 480 })
        throw new InvalidOperationException("历史封账月份迁移后未生成可校验的第 1 版月报快照。 ");
}
finally
{
    await using var migrationConnection = new NpgsqlConnection(connectionString);
    await migrationConnection.OpenAsync();
    await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{attendanceSnapshotMigrationSchema}\" CASCADE", migrationConnection);
    await dropSchema.ExecuteNonQueryAsync();
}

DemoData data;
await using (var setup = new OaDbContext(options))
{
    await setup.Database.MigrateAsync();
    IdentitySeeder.EnsureDemoSeeded(setup);
    try
    {
        IdentitySeeder.EnsureProductionReady(setup, new ConfigurationBuilder().Build());
        throw new InvalidOperationException("包含演示身份的数据库可以作为生产库启动。 ");
    }
    catch (InvalidOperationException exception) when (exception.Message.Contains("数据库包含演示账号", StringComparison.Ordinal)) { }
    data = new DemoData(setup);
}
var leaveDate = new DateOnly(2030, 1, 1).AddDays(Random.Shared.Next(0, 365));
while (leaveDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) leaveDate = leaveDate.AddDays(1);
Guid requestId;
Guid expenseId;
Guid fileId;
Guid versionedRequestId;
Guid delegatedRequestId;
Guid postCancellationRequestId;
Guid scopedRequestId;
Guid scopedProcessId;
Guid leaveCopyId;
Guid expenseCopyId;
Guid paymentProofId;
Guid announcementId;
Guid travelId;
Guid withdrawnTravelId;
Guid linkedExpenseId;
Guid attendanceRecordId;
Guid attendanceAppealId;
DateOnly attendanceLockMonth;
Guid employmentContractId;
Guid renewedEmploymentContractId;
Guid personnelCaseId;
Guid personnelTaskId;
Guid offboardingCaseId;
Guid personnelAlertTaskId;
Guid purchaseId;
Guid withdrawnPurchaseId;
Guid inOfficeSealId;
Guid outOfficeSealId;
Guid withdrawnSealId;
Guid testDocId;
Guid testCatId;
string purchaseOrderNumber = string.Empty;
string fileRoot;
var idempotencyKey = $"pg-integration-{Guid.NewGuid():N}";
var receiptNumber = $"FP-PG-{Guid.NewGuid():N}";
const string managedUserId = "u-pg-managed";
const string concurrentAuthUserId = "u-pg-concurrent-auth";
const string managedRoleCode = "PG审批观察员";
const string managedDepartmentId = "pg-operations";
const string managedChildDepartmentId = "pg-support";
const string managedPositionId = "pg-hr-partner";
await using (var writeDb = new OaDbContext(options))
{
    var calendar = new WorkCalendarService(writeDb);
    var employee = data.GetEmployee("u-zhang");
    var hr = data.GetEmployee("u-sun");
    var notifications = new NotificationService(writeDb);
    var processes = new ProcessDefinitionService(writeDb, data);
    var administrator = data.GetEmployee("u-admin");
    if (!data.HasPermission(administrator, OaPermissions.UserManage) || data.HasPermission(employee, OaPermissions.UserManage))
        throw new InvalidOperationException("持久化角色未正确解析用户管理权限。");
    var strictIdentityConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["DemoFeatures:AllowDataGeneration"] = "false"
    }).Build();
    var identity = new IdentityAdministrationService(writeDb, data, strictIdentityConfiguration);
    if (identity.List(employee, null, null, null, 1, 20).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权查询用户列表。");
    var seededUsers = identity.List(administrator, null, null, null, 1, 20);
    if (!seededUsers.IsSuccess || seededUsers.Value!.Total != IdentityDefaults.Employees.Count)
        throw new InvalidOperationException("默认用户未完整写入持久化身份目录。");
    var roles = identity.ListRoles(administrator);
    if (!roles.IsSuccess || roles.Value!.Count != IdentityDefaults.RolePermissions.Count || !roles.Value.Any(item => item.Code == "系统管理员" && item.Permissions.Contains(OaPermissions.UserManage)))
        throw new InvalidOperationException("默认角色或权限未完整写入数据库。");
    var permissionDefinitions = identity.ListPermissions(administrator);
    if (!permissionDefinitions.IsSuccess || permissionDefinitions.Value!.Count != OaPermissions.All.Count || identity.ListPermissions(employee).Code != "AUTH_002")
        throw new InvalidOperationException("权限定义查询或服务端授权错误。");
    if (identity.UpdateRole(administrator, new UpdateRoleRequest("系统管理员", "系统管理员", [OaPermissions.ProcessManage])).Code != "STATE_001")
        throw new InvalidOperationException("管理员可以移除自己最后一份用户管理权限。");
    if (identity.CreateRole(administrator, new CreateRoleRequest("PG无效范围", "无效范围", [], new Dictionary<string, string> { ["Expense"] = OaDataScopes.Company })).Code != "VALIDATION_001")
        throw new InvalidOperationException("未授予范围查看权限的角色可以配置跨用户数据范围。");
    if (identity.CreateRole(administrator, new CreateRoleRequest("PG无效导出", "无效导出", [OaPermissions.PersonnelExport])).Code != "VALIDATION_001")
        throw new InvalidOperationException("花名册导出权限可以脱离人事范围查看权限独立配置。");
    var createdRole = identity.CreateRole(administrator, new CreateRoleRequest(managedRoleCode, "集成审批观察员", [OaPermissions.CalendarManage]));
    if (!createdRole.IsSuccess || createdRole.Value!.IsSystem || createdRole.Value.UserCount != 0)
        throw new InvalidOperationException(createdRole.Error ?? "自定义角色无法创建。");
    if (identity.CreateRole(administrator, new CreateRoleRequest(managedRoleCode, "重复角色", [])).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复角色编码。");
    const string missingProfileUserId = "u-pg-missing-profile";
    var missingProfileRejected = identity.Create(administrator, new CreateManagedUserRequest(
        missingProfileUserId, "缺失建档字段", "engineering", "u-li", 1, [managedRoleCode], "StartPass@123"));
    if (missingProfileRejected.Code != "PERSONNEL_001" || writeDb.Users.Any(item => item.Id == missingProfileUserId))
        throw new InvalidOperationException("生产模式创建账号时未要求真实工号、入职日期、用工类型和人事状态，或失败后留下半成品账号。");
    var createdUser = identity.Create(administrator, new CreateManagedUserRequest(
        managedUserId, "集成测试用户", "engineering", "u-li", 4, [managedRoleCode], "StartPass@123",
        EmployeeNumber: "PG-MANAGED-001", HireDate: new DateOnly(2022, 3, 1), EmploymentType: EmploymentTypes.FullTime,
        PersonnelStatus: PersonnelStatuses.Active, CumulativeWorkStartDate: new DateOnly(2020, 3, 1)));
    if (!createdUser.IsSuccess || createdUser.Value!.Status != "ACTIVE" || createdUser.Value.Roles.SingleOrDefault() != managedRoleCode || !createdUser.Value.MustChangePassword)
        throw new InvalidOperationException(createdUser.Error ?? "管理员无法创建持久化用户。");
    if (!writeDb.UserAccounts.Any(item => item.UserId == managedUserId && item.MustChangePassword) ||
        !writeDb.PersonnelProfiles.Any(item => item.UserId == managedUserId && item.EmployeeNumber == "PG-MANAGED-001" && item.HireDate == new DateOnly(2022, 3, 1)) ||
        !writeDb.PersonnelEvents.Any(item => item.UserId == managedUserId && item.EventType == "IMPORTED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceId == managedUserId && item.Action == "USER_CREATED"))
        throw new InvalidOperationException("创建用户未在同一业务操作中建立人事档案、初始事件和审计记录。");
    const string duplicateEmployeeNumberUserId = "u-pg-duplicate-employee-number";
    var duplicateEmployeeNumber = identity.Create(administrator, new CreateManagedUserRequest(
        duplicateEmployeeNumberUserId, "重复工号用户", "engineering", "u-li", 2, [managedRoleCode], "StartPass@123",
        EmployeeNumber: "PG-MANAGED-001", HireDate: new DateOnly(2024, 1, 1), EmploymentType: EmploymentTypes.FullTime,
        PersonnelStatus: PersonnelStatuses.Active));
    if (duplicateEmployeeNumber.Code != "PERSONNEL_002" || writeDb.Users.Any(item => item.Id == duplicateEmployeeNumberUserId))
        throw new InvalidOperationException("重复工号未被拒绝，或失败后留下半成品账号。");
    if (identity.DeleteRole(administrator, managedRoleCode).Code != "CONFLICT_001")
        throw new InvalidOperationException("系统允许删除已分配给用户的角色。");
    if (identity.Create(administrator, new CreateManagedUserRequest(managedUserId, "重复用户", "engineering", "u-li", 4, ["员工"], "StartPass@123")).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复用户 ID。");

    var authConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Authentication:Issuer"] = "cute-oa-pg-tests",
        ["Authentication:Audience"] = "cute-oa-web-pg-tests",
        ["Authentication:SigningKey"] = "cute-oa-pg-tests-signing-key-with-at-least-thirty-two-bytes",
        ["Authentication:AccessTokenMinutes"] = "30",
        ["DemoFeatures:AllowDataGeneration"] = "true"
    }).Build();
    var persistedDirectory = new DemoData(writeDb);
    var personnel = new PersonnelService(writeDb, persistedDirectory, notifications);
    var profileCountBeforeReads = writeDb.PersonnelProfiles.Count();
    var eventCountBeforeReads = writeDb.PersonnelEvents.Count();
    var employeeRoster = personnel.List(persistedDirectory.GetEmployee("u-zhang"), null, null, null, null, 1, 20);
    if (!employeeRoster.IsSuccess || employeeRoster.Value!.Total != 1 || employeeRoster.Value.Items.Single().UserId != "u-zhang")
        throw new InvalidOperationException("普通员工花名册未限制为本人。 ");
    var managerRoster = personnel.List(persistedDirectory.GetEmployee("u-li"), null, null, null, null, 1, 20);
    if (!managerRoster.IsSuccess || managerRoster.Value!.Items.All(item => item.UserId != "u-zhang"))
        throw new InvalidOperationException("直属上级无法查看管理链下属人事档案。 ");
    var hrRoster = personnel.List(persistedDirectory.GetEmployee("u-sun"), null, null, null, null, 1, 100);
    if (!hrRoster.IsSuccess || hrRoster.Value!.Total != IdentityDefaults.Employees.Count + 1)
        throw new InvalidOperationException("HR 未按全公司数据范围读取完整花名册。 ");
    if (personnel.Export(persistedDirectory.GetEmployee("u-zhang"), null, null, null, null).Code != "AUTH_002" ||
        personnel.Export(persistedDirectory.GetEmployee("u-li"), null, null, null, null).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工或仅可查看下属的负责人可以越权导出花名册。 ");
    var limitedExportConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["PersonnelExport:MaxRows"] = "2"
    }).Build();
    var limitedExport = new PersonnelService(writeDb, persistedDirectory, notifications, limitedExportConfiguration)
        .Export(persistedDirectory.GetEmployee("u-sun"), null, null, null, null);
    if (limitedExport.Code != "PERSONNEL_EXPORT_001")
        throw new InvalidOperationException("花名册同步导出超过配置行数后未失败关闭。 ");
    var currentPersonnel = personnel.Get(persistedDirectory.GetEmployee("u-sun"), "u-zhang");
    if (!currentPersonnel.IsSuccess || currentPersonnel.Value!.Events.SingleOrDefault()?.EventType != "IMPORTED")
        throw new InvalidOperationException("默认人事档案或初始化事件未生成。 ");
    if (writeDb.PersonnelProfiles.Count() != profileCountBeforeReads || writeDb.PersonnelEvents.Count() != eventCountBeforeReads)
        throw new InvalidOperationException("读取花名册或档案产生了隐式建档写入。");
    var personnelToday = DateOnly.FromDateTime(DateTime.UtcNow);
    var personnelUpdate = new UpdatePersonnelProfileRequest(
        "engineering", "software-engineer", "u-li", "zhang.chen@xxx.example", "+86 21 5555 0101", "上海办公室", "FULL_TIME", "ACTIVE",
        personnelToday.AddYears(-2), null, personnelToday.AddYears(-2), personnelToday.AddYears(-6), null, null,
        currentPersonnel.Value.Version, personnelToday, "补充工作联系方式并核验累计工作年限");
    if (personnel.Update(persistedDirectory.GetEmployee("u-zhang"), "u-zhang", personnelUpdate).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权编辑自己的人事档案。 ");
    var updatedPersonnel = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate);
    if (!updatedPersonnel.IsSuccess || updatedPersonnel.Value!.CumulativeWorkYears != 6 || updatedPersonnel.Value.WorkEmail != "zhang.chen@xxx.example" || updatedPersonnel.Value.Events.First().EventType != "PROFILE_UPDATED")
        throw new InvalidOperationException(updatedPersonnel.Error ?? "HR 无法更新人事档案或社会工龄。 ");
    var formulaInjectionProfile = writeDb.PersonnelProfiles.Single(item => item.UserId == "u-zhang");
    formulaInjectionProfile.WorkLocation = "  \t=HYPERLINK(\"https://invalid.example\")";
    writeDb.SaveChanges();
    var filteredExport = personnel.Export(persistedDirectory.GetEmployee("u-sun"), "u-zhang", "engineering", PersonnelStatuses.Active, EmploymentTypes.FullTime);
    if (!filteredExport.IsSuccess || filteredExport.Value is not { Count: 1 } ||
        filteredExport.Value.Content.Length < 3 || filteredExport.Value.Content[0] != 0xEF || filteredExport.Value.Content[1] != 0xBB || filteredExport.Value.Content[2] != 0xBF)
        throw new InvalidOperationException(filteredExport.Error ?? "受控花名册导出未按筛选返回 UTF-8 BOM CSV。 ");
    var exportedCsv = System.Text.Encoding.UTF8.GetString(filteredExport.Value.Content);
    if (!exportedCsv.Contains("\"工号\",\"姓名\",\"用户 ID\"", StringComparison.Ordinal) ||
        !exportedCsv.Contains("\"'+86 21 5555 0101\"", StringComparison.Ordinal) ||
        !exportedCsv.Contains("\"'  \t=HYPERLINK(\"\"https://invalid.example\"\")\"", StringComparison.Ordinal) ||
        !writeDb.AuditLogs.Any(item => item.ActorId == "u-sun" && item.Action == "PERSONNEL_ROSTER_EXPORTED" && item.ResourceId == "roster" && item.Summary.Contains("1 行")))
        throw new InvalidOperationException("花名册导出缺少字段、CSV 公式注入防护或审计证据。 ");
    formulaInjectionProfile.WorkLocation = "上海办公室";
    writeDb.SaveChanges();

    var caseService = new PersonnelCaseService(writeDb, persistedDirectory, notifications);
    var onboardingDate = personnelToday.AddDays(5);
    var onboarding = caseService.Create(persistedDirectory.GetEmployee("u-sun"), new CreatePersonnelCaseRequest("u-zhang", PersonnelCaseTypes.Onboarding, onboardingDate, "u-sun", "新员工入职材料、账号、设备和岗位目标办理"));
    if (!onboarding.IsSuccess || onboarding.Value is not { Status: PersonnelCaseStatuses.Open, TaskCount: 6, ResolvedTaskCount: 0 } || caseService.Create(persistedDirectory.GetEmployee("u-sun"), new CreatePersonnelCaseRequest("u-zhang", PersonnelCaseTypes.Onboarding, onboardingDate, "u-sun", "重复办理单")).Code != "DUPLICATE_001")
        throw new InvalidOperationException(onboarding.Error ?? "入职办理单模板、任务数量或重复保护错误。 ");
    if (onboarding.Value.Tasks.Single(task => task.Code == "HR_PROFILE").AssigneeId != "u-sun" ||
        onboarding.Value.Tasks.Single(task => task.Code == "ACCOUNT").AssigneeId != "u-admin" ||
        onboarding.Value.Tasks.Single(task => task.Code == "EQUIPMENT").AssigneeId != "u-sun" ||
        onboarding.Value.Tasks.Single(task => task.Code == "MANAGER_PLAN").AssigneeId != "u-li")
        throw new InvalidOperationException("入职模板未按 HR、IT、行政和直属上级职责自动分派。 ");
    var configuredAssignment = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["PersonnelCases:CategoryAssignees:IT"] = "u-chen"
    }).Build();
    var configuredCaseService = new PersonnelCaseService(writeDb, persistedDirectory, notifications, configuredAssignment);
    var configuredCase = configuredCaseService.Create(persistedDirectory.GetEmployee("u-sun"), new CreatePersonnelCaseRequest(managedUserId, PersonnelCaseTypes.Onboarding, onboardingDate.AddDays(1), "u-sun", "验证生产配置覆盖职责负责人"));
    if (!configuredCase.IsSuccess || configuredCase.Value!.Tasks.Single(task => task.Code == "ACCOUNT").AssigneeId != "u-chen")
        throw new InvalidOperationException(configuredCase.Error ?? "员工办理职责配置未覆盖自动分派结果。 ");
    var invalidAssignment = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["PersonnelCases:CategoryAssignees:IT"] = "u-disabled"
    }).Build();
    var invalidCaseService = new PersonnelCaseService(writeDb, persistedDirectory, notifications, invalidAssignment);
    var invalidConfiguredCase = invalidCaseService.Create(persistedDirectory.GetEmployee("u-sun"), new CreatePersonnelCaseRequest(managedUserId, PersonnelCaseTypes.Transfer, onboardingDate.AddDays(2), "u-sun", "验证停用职责负责人失败关闭"));
    if (invalidConfiguredCase.Code != "PERSONNEL_CASE_004" || writeDb.PersonnelCases.Any(item => item.UserId == managedUserId && item.Type == PersonnelCaseTypes.Transfer && item.EffectiveDate == onboardingDate.AddDays(2)))
        throw new InvalidOperationException("员工办理职责负责人配置无效时未失败关闭，或留下了半成品办理单。 ");

    var alertConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["PersonnelCaseAlerts:DueSoonDays"] = "1",
        ["PersonnelCaseAlerts:EscalateAfterDays"] = "3"
    }).Build();
    var alertCaseService = new PersonnelCaseService(writeDb, persistedDirectory, notifications, alertConfiguration);
    var alertEffectiveDate = onboardingDate.AddDays(3);
    var alertCase = alertCaseService.Create(persistedDirectory.GetEmployee("u-sun"), new CreatePersonnelCaseRequest(managedUserId, PersonnelCaseTypes.Transfer, alertEffectiveDate, "u-sun", "验证临期、逾期和升级提醒幂等派发"));
    if (!alertCase.IsSuccess) throw new InvalidOperationException(alertCase.Error ?? "无法创建员工事项提醒验证办理单。 ");
    var alertTask = alertCase.Value!.Tasks.Single(task => task.Code == "TRANSFER_APPROVAL");
    personnelAlertTaskId = alertTask.Id;
    if (alertCaseService.DispatchTaskAlerts(alertTask.DueDate.AddDays(-1)) < 1 || alertCaseService.DispatchTaskAlerts(alertTask.DueDate.AddDays(-1)) != 0 ||
        !writeDb.PersonnelCaseAlertDeliveries.Any(item => item.PersonnelCaseTaskId == alertTask.Id && item.RecipientId == "u-sun" && item.AlertType == "DUE_SOON"))
        throw new InvalidOperationException("员工事项临期提醒未派发给任务负责人，或重复执行产生了重复提醒。 ");
    if (alertCaseService.DispatchTaskAlerts(alertTask.DueDate.AddDays(1)) < 1 || alertCaseService.DispatchTaskAlerts(alertTask.DueDate.AddDays(1)) != 0 ||
        !writeDb.PersonnelCaseAlertDeliveries.Any(item => item.PersonnelCaseTaskId == alertTask.Id && item.RecipientId == "u-sun" && item.AlertType == "OVERDUE"))
        throw new InvalidOperationException("员工事项逾期提醒未派发给任务负责人和办理负责人，或重复执行产生了重复提醒。 ");
    if (alertCaseService.DispatchTaskAlerts(alertTask.DueDate.AddDays(3)) < 1 || alertCaseService.DispatchTaskAlerts(alertTask.DueDate.AddDays(3)) != 0)
        throw new InvalidOperationException("员工事项逾期升级提醒未派发，或重复执行产生了重复提醒。 ");
    var escalationRecipients = writeDb.PersonnelCaseAlertDeliveries
        .Where(item => item.PersonnelCaseTaskId == alertTask.Id && item.AlertType == "ESCALATED")
        .Select(item => item.RecipientId).ToHashSet(StringComparer.Ordinal);
    if (!escalationRecipients.SetEquals(["u-sun", "u-admin"]) ||
        !writeDb.Notifications.Any(item => item.ResourceId == alertCase.Value.Id.ToString() && item.Type == "PERSONNEL_TASK_ESCALATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceId == alertCase.Value.Id.ToString() && item.Action == "PERSONNEL_TASK_ALERT_DISPATCHED" && item.Summary.Contains("ESCALATED")))
        throw new InvalidOperationException("员工事项升级提醒未覆盖办理负责人和具备数据权限的 HR 管理人员，或缺少通知/审计证据。 ");

    personnelCaseId = onboarding.Value.Id;
    if (!caseService.Get(persistedDirectory.GetEmployee("u-zhang"), personnelCaseId).IsSuccess || caseService.Get(persistedDirectory.GetEmployee("u-chen"), personnelCaseId).Code != "AUTH_002")
        throw new InvalidOperationException("员工本人无法查看办理单或无关员工越权查看。 ");
    var employeeTask = onboarding.Value.Tasks.Single(task => task.Code == "ORIENTATION");
    personnelTaskId = employeeTask.Id;
    var forbiddenTask = onboarding.Value.Tasks.First(task => task.AssigneeId != "u-zhang");
    if (caseService.UpdateTask(persistedDirectory.GetEmployee("u-zhang"), personnelCaseId, forbiddenTask.Id, new UpdatePersonnelCaseTaskRequest(forbiddenTask.AssigneeId, forbiddenTask.DueDate, PersonnelTaskStatuses.Completed, "尝试越权完成", forbiddenTask.Version)).Code != "AUTH_002")
        throw new InvalidOperationException("员工可以处理未分配给自己的办理任务。 ");
    var employeeCompleted = caseService.UpdateTask(persistedDirectory.GetEmployee("u-zhang"), personnelCaseId, employeeTask.Id, new UpdatePersonnelCaseTaskRequest(employeeTask.AssigneeId, employeeTask.DueDate, PersonnelTaskStatuses.Completed, "已完成制度学习并确认", employeeTask.Version));
    if (!employeeCompleted.IsSuccess || caseService.UpdateTask(persistedDirectory.GetEmployee("u-zhang"), personnelCaseId, employeeTask.Id, new UpdatePersonnelCaseTaskRequest(employeeTask.AssigneeId, employeeTask.DueDate, PersonnelTaskStatuses.Completed, "重复完成", employeeTask.Version)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException(employeeCompleted.Error ?? "员工无法完成本人办理任务或任务并发版本未生效。 ");
    if (caseService.Complete(persistedDirectory.GetEmployee("u-sun"), personnelCaseId, new CompletePersonnelCaseRequest(employeeCompleted.Value!.Version, "所有入职事项已完成并核验")).Code != "PERSONNEL_CASE_002")
        throw new InvalidOperationException("存在待办任务时仍可办结员工办理单。 ");
    var workingCase = employeeCompleted.Value;
    foreach (var task in workingCase.Tasks.Where(task => task.Status == PersonnelTaskStatuses.Pending).ToList())
    {
        var updatedCase = caseService.UpdateTask(persistedDirectory.GetEmployee("u-sun"), personnelCaseId, task.Id, new UpdatePersonnelCaseTaskRequest(task.AssigneeId, task.DueDate, PersonnelTaskStatuses.Completed, $"{task.Title}已核验完成", task.Version));
        if (!updatedCase.IsSuccess) throw new InvalidOperationException(updatedCase.Error ?? "HR 无法完成员工办理任务。 ");
        workingCase = updatedCase.Value!;
    }
    var completedOnboarding = caseService.Complete(persistedDirectory.GetEmployee("u-sun"), personnelCaseId, new CompletePersonnelCaseRequest(workingCase.Version, "入职材料、权限、设备和岗位计划均已确认"));
    if (!completedOnboarding.IsSuccess || completedOnboarding.Value is not { Status: PersonnelCaseStatuses.Completed, ResolvedTaskCount: 6 })
        throw new InvalidOperationException(completedOnboarding.Error ?? "入职办理单无法办结。 ");

    var probationProfile = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = PersonnelStatuses.Probation, ProbationEndDate = personnelToday.AddDays(30), RegularizedDate = null, Version = updatedPersonnel.Value.Version, ChangeReason = "调整为试用状态以验证转正办理流程" });
    var regularizedProfile = probationProfile.IsSuccess ? personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = PersonnelStatuses.Active, ProbationEndDate = personnelToday, RegularizedDate = personnelToday, Version = probationProfile.Value!.Version, ChangeReason = "试用期考核通过并正式转正" }) : ServiceResult<PersonnelProfileView>.Failure(probationProfile.Error!, probationProfile.Code!);
    if (!regularizedProfile.IsSuccess || !caseService.List(persistedDirectory.GetEmployee("u-sun"), null, "u-zhang", PersonnelCaseTypes.Regularization, PersonnelCaseStatuses.Open, null, 1, 20).Value!.Items.Any())
        throw new InvalidOperationException(regularizedProfile.Error ?? "转正状态变更未自动生成标准办理清单。 ");
    var terminationBlocked = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = PersonnelStatuses.Terminated, ProbationEndDate = personnelToday, RegularizedDate = personnelToday, DepartureDate = personnelToday, DepartureReason = "测试离职清单前置门禁", Version = regularizedProfile.Value!.Version, ChangeReason = "未进入离职办理直接办结" });
    if (terminationBlocked.Code != "PERSONNEL_CASE_003")
        throw new InvalidOperationException("未进入离职办理或未完成清单时仍可直接停用员工。 ");
    var missingOffboardingPlan = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = PersonnelStatuses.Offboarding, ProbationEndDate = personnelToday, RegularizedDate = personnelToday, DepartureDate = null, DepartureReason = null, Version = regularizedProfile.Value.Version, ChangeReason = "测试缺少计划离职日期" });
    if (missingOffboardingPlan.Code != "PERSONNEL_003")
        throw new InvalidOperationException("进入离职办理时未强制填写计划离职日期和原因。 ");
    var plannedDepartureDate = personnelToday.AddDays(14);
    var offboardingProfile = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = PersonnelStatuses.Offboarding, ProbationEndDate = personnelToday, RegularizedDate = personnelToday, DepartureDate = plannedDepartureDate, DepartureReason = "员工提出离职并确认计划日期", Version = regularizedProfile.Value.Version, ChangeReason = "发起离职办理并生成标准清单" });
    var currentOffboardingSummary = caseService.List(persistedDirectory.GetEmployee("u-sun"), null, "u-zhang", PersonnelCaseTypes.Offboarding, PersonnelCaseStatuses.Open, null, 1, 20).Value?.Items.SingleOrDefault(item => item.EffectiveDate == plannedDepartureDate);
    if (!offboardingProfile.IsSuccess || currentOffboardingSummary is null)
        throw new InvalidOperationException(offboardingProfile.Error ?? "进入离职办理未按计划离职日期自动生成清单。 ");
    offboardingCaseId = currentOffboardingSummary.Id;

    var historicalDepartureDate = plannedDepartureDate.AddDays(-30);
    var historicalOffboarding = caseService.Create(persistedDirectory.GetEmployee("u-sun"), new CreatePersonnelCaseRequest("u-zhang", PersonnelCaseTypes.Offboarding, historicalDepartureDate, "u-sun", "用于验证历史离职清单不能放行本次离职"));
    if (!historicalOffboarding.IsSuccess) throw new InvalidOperationException(historicalOffboarding.Error ?? "历史日期离职清单无法创建。 ");
    var workingHistoricalCase = historicalOffboarding.Value!;
    foreach (var task in workingHistoricalCase.Tasks.Where(task => task.Status == PersonnelTaskStatuses.Pending).ToList())
    {
        var updatedCase = caseService.UpdateTask(persistedDirectory.GetEmployee("u-sun"), workingHistoricalCase.Id, task.Id, new UpdatePersonnelCaseTaskRequest(task.AssigneeId, task.DueDate, PersonnelTaskStatuses.Completed, $"{task.Title}已核验完成", task.Version));
        if (!updatedCase.IsSuccess) throw new InvalidOperationException(updatedCase.Error ?? "历史离职清单任务无法完成。 ");
        workingHistoricalCase = updatedCase.Value!;
    }
    var completedHistoricalCase = caseService.Complete(persistedDirectory.GetEmployee("u-sun"), workingHistoricalCase.Id, new CompletePersonnelCaseRequest(workingHistoricalCase.Version, "历史日期离职事项均已完成并归档"));
    if (!completedHistoricalCase.IsSuccess || caseService.HasCompletedOffboarding("u-zhang", plannedDepartureDate))
        throw new InvalidOperationException(completedHistoricalCase.Error ?? "历史日期离职清单错误放行了本次离职。 ");

    var mismatchedCaseBlocked = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = PersonnelStatuses.Terminated, ProbationEndDate = personnelToday, RegularizedDate = personnelToday, DepartureDate = plannedDepartureDate, DepartureReason = "按计划日期办结离职", Version = offboardingProfile.Value!.Version, ChangeReason = "验证历史办理单不得放行" });
    if (mismatchedCaseBlocked.Code != "PERSONNEL_CASE_003")
        throw new InvalidOperationException("历史已完成离职清单可以越过本次计划日期门禁。 ");

    var workingOffboardingCase = caseService.Get(persistedDirectory.GetEmployee("u-sun"), offboardingCaseId).Value!;
    foreach (var task in workingOffboardingCase.Tasks.Where(task => task.Status == PersonnelTaskStatuses.Pending).ToList())
    {
        var updatedCase = caseService.UpdateTask(persistedDirectory.GetEmployee("u-sun"), workingOffboardingCase.Id, task.Id, new UpdatePersonnelCaseTaskRequest(task.AssigneeId, task.DueDate, PersonnelTaskStatuses.Completed, $"{task.Title}已核验完成", task.Version));
        if (!updatedCase.IsSuccess) throw new InvalidOperationException(updatedCase.Error ?? "本次离职清单任务无法完成。 ");
        workingOffboardingCase = updatedCase.Value!;
    }
    var completedOffboarding = caseService.Complete(persistedDirectory.GetEmployee("u-sun"), offboardingCaseId, new CompletePersonnelCaseRequest(workingOffboardingCase.Version, "本次离职交接、资产、财务和账号事项均已完成"));
    var terminatedProfile = completedOffboarding.IsSuccess
        ? personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = PersonnelStatuses.Terminated, ProbationEndDate = personnelToday, RegularizedDate = personnelToday, DepartureDate = plannedDepartureDate, DepartureReason = "按计划日期完成离职", Version = offboardingProfile.Value.Version, ChangeReason = "离职清单完成后办结离职" })
        : ServiceResult<PersonnelProfileView>.Failure(completedOffboarding.Error!, completedOffboarding.Code!);
    if (!terminatedProfile.IsSuccess || terminatedProfile.Value is not { PersonnelStatus: PersonnelStatuses.Terminated, AccountStatus: "DISABLED" })
        throw new InvalidOperationException(terminatedProfile.Error ?? "匹配计划日期的离职清单完成后仍无法办结离职。 ");
    var restoredProfile = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = PersonnelStatuses.Active, ProbationEndDate = personnelToday, RegularizedDate = personnelToday, DepartureDate = null, DepartureReason = null, Version = terminatedProfile.Value.Version, ChangeReason = "集成测试结束后恢复在职状态" });
    if (!restoredProfile.IsSuccess || restoredProfile.Value is not { PersonnelStatus: PersonnelStatuses.Active, AccountStatus: "ACTIVE" })
        throw new InvalidOperationException(restoredProfile.Error ?? "离职门禁集成测试后无法恢复测试员工状态。 ");
    var invalidLifecycle = personnel.Update(persistedDirectory.GetEmployee("u-sun"), "u-zhang", personnelUpdate with { PersonnelStatus = "PROBATION", ProbationEndDate = null, RegularizedDate = null, Version = restoredProfile.Value.Version, ChangeReason = "测试无效试用状态" });
    if (invalidLifecycle.Code != "PERSONNEL_003")
        throw new InvalidOperationException("缺少试用期结束日期的试用状态未被拒绝。 ");
    var concurrentAuthUser = identity.Create(administrator, new CreateManagedUserRequest(
        concurrentAuthUserId, "并发锁定测试用户", "engineering", "u-li", 2, ["员工"], "ConcurrentPass@123",
        EmployeeNumber: "PG-CONCURRENT-001", HireDate: new DateOnly(2024, 1, 1), EmploymentType: EmploymentTypes.FullTime,
        PersonnelStatus: PersonnelStatuses.Active, CumulativeWorkStartDate: new DateOnly(2024, 1, 1)));
    if (!concurrentAuthUser.IsSuccess) throw new InvalidOperationException(concurrentAuthUser.Error ?? "无法创建并发锁定测试用户。");
    using var concurrentLoginStart = new Barrier(5);
    var concurrentFailures = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() =>
    {
        using var concurrentDb = new OaDbContext(options);
        var concurrentDirectory = new DemoData(concurrentDb);
        var concurrentAuthentication = new AuthenticationService(concurrentDirectory, new JwtTokenService(authConfiguration), concurrentDb);
        if (!concurrentLoginStart.SignalAndWait(TimeSpan.FromSeconds(10))) throw new TimeoutException("并发登录测试未能同时开始。");
        return concurrentAuthentication.Login(new LoginRequest(concurrentAuthUserId, "WrongConcurrentPass123")).IsSuccess;
    })));
    await using (var concurrentVerificationDb = new OaDbContext(options))
    {
        var lockedAccount = await concurrentVerificationDb.UserAccounts.SingleAsync(item => item.UserId == concurrentAuthUserId);
        var concurrentDirectory = new DemoData(concurrentVerificationDb);
        var concurrentAuthentication = new AuthenticationService(concurrentDirectory, new JwtTokenService(authConfiguration), concurrentVerificationDb);
        if (concurrentFailures.Any(success => success) || lockedAccount.LockedUntil <= DateTimeOffset.UtcNow || lockedAccount.FailedLoginCount != 0 || concurrentAuthentication.Login(new LoginRequest(concurrentAuthUserId, "ConcurrentPass@123")).IsSuccess)
            throw new InvalidOperationException("并发错误密码未原子累计到账号锁定，或锁定后仍可登录。");
    }

    var authentication = new AuthenticationService(persistedDirectory, new JwtTokenService(authConfiguration), writeDb, authConfiguration);
    var directInitialLogin = authentication.Login(new LoginRequest(managedUserId, "StartPass@123"));
    var expiredInitialLogin = authentication.BeginLogin(new LoginRequest(managedUserId, "StartPass@123"));
    var expiredChallengeId = Guid.ParseExact(expiredInitialLogin.Value!.Challenge!.ChallengeToken[..32], "N");
    writeDb.MfaChallenges.Where(item => item.Id == expiredChallengeId)
        .ExecuteUpdate(setters => setters.SetProperty(item => item.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    if (authentication.ChangeInitialPassword(new ChangeInitialPasswordRequest(expiredInitialLogin.Value.Challenge.ChallengeToken, "ExpiredChanged@123")).Code != "AUTH_006")
        throw new InvalidOperationException("过期的首次改密挑战仍可修改密码。 ");
    var initialLogin = authentication.BeginLogin(new LoginRequest(managedUserId, "StartPass@123"), new SessionClient("Mozilla/5.0 (Macintosh) Chrome/140", "127.0.0.1"));
    if (directInitialLogin.IsSuccess || !initialLogin.IsSuccess || initialLogin.Value is not { Grant: null, Challenge.State: AuthenticationChallengeStates.PasswordChangeRequired })
        throw new InvalidOperationException("新建账号未被限制为首次登录修改临时密码。 ");
    if (authentication.ChangeInitialPassword(new ChangeInitialPasswordRequest(initialLogin.Value.Challenge!.ChallengeToken, "weak-password")).Code != "VALIDATION_001" ||
        authentication.ChangeInitialPassword(new ChangeInitialPasswordRequest(initialLogin.Value.Challenge.ChallengeToken, "StartPass@123")).Code != "VALIDATION_001")
        throw new InvalidOperationException("首次改密未执行正式密码强度或禁止复用临时密码规则。 ");
    const string initialChangedPassword = "InitialChanged@123";
    var initialPasswordChanged = authentication.ChangeInitialPassword(new ChangeInitialPasswordRequest(initialLogin.Value.Challenge.ChallengeToken, initialChangedPassword), new SessionClient("Mozilla/5.0 (Macintosh) Chrome/140", "127.0.0.1"));
    if (!initialPasswordChanged.IsSuccess || initialPasswordChanged.Value!.Grant?.SessionId is null || initialPasswordChanged.Value.Grant.RefreshToken is null ||
        authentication.ChangeInitialPassword(new ChangeInitialPasswordRequest(initialLogin.Value.Challenge.ChallengeToken, "AnotherChanged@123")).Code != "AUTH_006" ||
        authentication.Login(new LoginRequest(managedUserId, "StartPass@123")).IsSuccess)
        throw new InvalidOperationException(initialPasswordChanged.Error ?? "首次改密未签发会话、旧临时密码仍有效或挑战可被重复使用。 ");
    var storedInitialSession = writeDb.AuthSessions.Single(item => item.Id == initialPasswordChanged.Value.Grant.SessionId);
    if (storedInitialSession.RefreshTokenHash == initialPasswordChanged.Value.Grant.RefreshToken || storedInitialSession.RefreshTokenHash.Length != 64 ||
        writeDb.UserAccounts.AsNoTracking().Single(item => item.UserId == managedUserId).MustChangePassword ||
        !writeDb.AuditLogs.Any(item => item.ResourceId == managedUserId && item.Action == "INITIAL_PASSWORD_CHANGED"))
        throw new InvalidOperationException("刷新令牌未以 SHA-256 摘要持久化。");
    for (var attempt = 0; attempt < 5; attempt++) authentication.Login(new LoginRequest(managedUserId, "WrongPass123"));
    if (writeDb.UserAccounts.Single(item => item.UserId == managedUserId).LockedUntil <= DateTimeOffset.UtcNow || authentication.Login(new LoginRequest(managedUserId, initialChangedPassword)).IsSuccess)
        throw new InvalidOperationException("连续登录失败未锁定账号。");
    var passwordReset = identity.ResetPassword(administrator, managedUserId, new ResetUserPasswordRequest("ResetPass@123"));
    writeDb.Entry(storedInitialSession).Reload();
    if (!passwordReset.IsSuccess || storedInitialSession.RevokedAt is null || authentication.Login(new LoginRequest(managedUserId, initialChangedPassword)).IsSuccess ||
        !writeDb.UserAccounts.AsNoTracking().Single(item => item.UserId == managedUserId).MustChangePassword)
        throw new InvalidOperationException("密码重置未解除锁定或新旧密码校验错误。");
    var obsoleteResetChallenge = authentication.BeginLogin(new LoginRequest(managedUserId, "ResetPass@123")).Value!.Challenge!;
    var resetLogin = authentication.BeginLogin(new LoginRequest(managedUserId, "ResetPass@123"), new SessionClient("Mozilla/5.0 (Macintosh) Safari/605", "127.0.0.1"));
    const string resetChangedPassword = "ResetChanged@123";
    if (authentication.ChangeInitialPassword(new ChangeInitialPasswordRequest(obsoleteResetChallenge.ChallengeToken, resetChangedPassword)).Code != "AUTH_006")
        throw new InvalidOperationException("新的首次改密挑战未使旧挑战失效。 ");
    var resetPasswordChanged = authentication.ChangeInitialPassword(new ChangeInitialPasswordRequest(resetLogin.Value!.Challenge!.ChallengeToken, resetChangedPassword));
    if (!resetPasswordChanged.IsSuccess || resetPasswordChanged.Value!.Grant is null || authentication.Login(new LoginRequest(managedUserId, "ResetPass@123")).IsSuccess)
        throw new InvalidOperationException(resetPasswordChanged.Error ?? "管理员重置后的临时密码未强制修改或修改后未建立会话。 ");
    var firstSession = authentication.Login(new LoginRequest(managedUserId, resetChangedPassword), new SessionClient("Mozilla/5.0 (Macintosh) Safari/605", "127.0.0.1"));
    var secondSession = authentication.Login(new LoginRequest(managedUserId, resetChangedPassword), new SessionClient("Mozilla/5.0 (Windows NT 10.0) Chrome/140", "10.0.0.2"));
    if (!firstSession.IsSuccess || !secondSession.IsSuccess)
        throw new InvalidOperationException("密码重置后无法建立新会话。");
    var sessions = authentication.ListSessions(persistedDirectory.GetEmployee(managedUserId), secondSession.Value!.SessionId, 1, 20);
    if (!sessions.IsSuccess || sessions.Value!.Items.Count(item => item.Status == "ACTIVE") < 2 || sessions.Value.Items.Single(item => item.Id == secondSession.Value.SessionId).IsCurrent is false)
        throw new InvalidOperationException("登录设备列表未正确标记当前会话。");
    if (!authentication.RevokeSession(persistedDirectory.GetEmployee(managedUserId), secondSession.Value.SessionId!.Value, firstSession.Value!.SessionId!.Value).IsSuccess || authentication.Refresh(firstSession.Value.RefreshToken).IsSuccess)
        throw new InvalidOperationException("单设备会话撤销未生效。");
    var refreshCandidate = authentication.Login(new LoginRequest(managedUserId, resetChangedPassword), new SessionClient("Mozilla/5.0 (iPhone) Safari/605", "10.0.0.3"));
    var rotated = authentication.Refresh(refreshCandidate.Value!.RefreshToken, new SessionClient("Mozilla/5.0 (iPhone) Safari/605", "10.0.0.3"));
    if (!rotated.IsSuccess || rotated.Value!.RefreshToken == refreshCandidate.Value.RefreshToken || rotated.Value.SessionId != refreshCandidate.Value.SessionId)
        throw new InvalidOperationException("刷新令牌未轮换或访问令牌未保持会话绑定。");
    var reusedRecord = writeDb.AuthSessions.Single(item => item.Id == refreshCandidate.Value.SessionId);
    var reused = authentication.Refresh(refreshCandidate.Value.RefreshToken);
    writeDb.Entry(reusedRecord).Reload();
    if (reused.IsSuccess || reusedRecord.RevokedReason != "TOKEN_REUSE")
        throw new InvalidOperationException("已轮换刷新令牌可被重复使用或未阻断对应会话。");
    var roleChangeSession = authentication.Login(new LoginRequest(managedUserId, resetChangedPassword));
    var configuredPermissions = new[] { OaPermissions.CalendarManage, OaPermissions.AuditView, OaPermissions.LeaveScopeView };
    var permissionUpdate = identity.UpdateRole(administrator, new UpdateRoleRequest(managedRoleCode, "集成审批观察员（已更新）", configuredPermissions));
    var permissionChangedRecord = writeDb.AuthSessions.Single(item => item.Id == roleChangeSession.Value!.SessionId);
    writeDb.Entry(permissionChangedRecord).Reload();
    if (!permissionUpdate.IsSuccess || !permissionUpdate.Value!.Permissions.Contains(OaPermissions.CalendarManage) || !permissionUpdate.Value.Permissions.Contains(OaPermissions.AuditView) || permissionChangedRecord.RevokedReason != "ROLE_PERMISSIONS_CHANGED")
        throw new InvalidOperationException(permissionUpdate.Error ?? "角色权限变化未持久化或未撤销受影响会话。");
    var scopeChangeSession = authentication.Login(new LoginRequest(managedUserId, resetChangedPassword));
    var scopeUpdate = identity.UpdateRole(administrator, new UpdateRoleRequest(managedRoleCode, "集成审批观察员（已更新）", configuredPermissions, new Dictionary<string, string> { ["Leave"] = OaDataScopes.Department, ["Expense"] = OaDataScopes.Self }));
    var scopeChangedRecord = writeDb.AuthSessions.Single(item => item.Id == scopeChangeSession.Value!.SessionId);
    writeDb.Entry(scopeChangedRecord).Reload();
    var scopedDirectory = new DemoData(writeDb);
    var scopedActor = scopedDirectory.GetEmployee(managedUserId);
    if (!scopeUpdate.IsSuccess || scopeUpdate.Value!.DataScopes["Leave"] != OaDataScopes.Department || scopeChangedRecord.RevokedReason != "ROLE_DATA_SCOPE_CHANGED" || !scopedDirectory.CanView(scopedActor, scopedDirectory.GetEmployee("u-li"), "Leave") || scopedDirectory.CanView(scopedActor, scopedDirectory.GetEmployee("u-chen"), "Leave"))
        throw new InvalidOperationException(scopeUpdate.Error ?? "角色数据范围未生效、未隔离部门或未撤销受影响会话。");
    roleChangeSession = authentication.Login(new LoginRequest(managedUserId, resetChangedPassword));
    var updatedUser = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        "集成测试用户（已更新）", "hr", "u-wang", 5, "ACTIVE", ["HR/行政"], createdUser.Value.Version));
    if (!updatedUser.IsSuccess || !updatedUser.Value!.Permissions.Contains(OaPermissions.CalendarManage) || updatedUser.Value.Permissions.Contains(OaPermissions.UserManage))
        throw new InvalidOperationException(updatedUser.Error ?? "用户资料或角色权限无法更新。");
    var roleChangedRecord = writeDb.AuthSessions.Single(item => item.Id == roleChangeSession.Value!.SessionId);
    writeDb.Entry(roleChangedRecord).Reload();
    if (roleChangedRecord.RevokedReason != "USER_ROLES_CHANGED")
        throw new InvalidOperationException("安全角色变化后既有会话未失效。");
    if (!identity.DeleteRole(administrator, managedRoleCode).IsSuccess || writeDb.Roles.Any(item => item.Code == managedRoleCode))
        throw new InvalidOperationException("未分配的自定义角色无法删除。");
    if (identity.DeleteRole(administrator, "员工").Code != "STATE_001")
        throw new InvalidOperationException("系统预置角色可被删除。");
    var adminView = identity.List(administrator, "u-admin", null, null, 1, 20).Value!.Items.Single();
    if (identity.Update(administrator, administrator.Id, new UpdateManagedUserRequest(
        adminView.Name, adminView.DepartmentId, adminView.ManagerId, adminView.CumulativeWorkYears, "DISABLED", adminView.Roles, adminView.Version)).Code != "STATE_001")
        throw new InvalidOperationException("管理员可以停用自己的账号。");
    var refreshedUsers = identity.List(administrator, "集成测试", "ACTIVE", "hr", 1, 10);
    if (!refreshedUsers.IsSuccess || refreshedUsers.Value!.Total != 1 || refreshedUsers.Value.Items[0].Id != managedUserId)
        throw new InvalidOperationException("用户管理列表筛选或分页结果错误。");

    var mfaConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Authentication:Issuer"] = "cute-oa-pg-tests",
        ["Authentication:Audience"] = "cute-oa-web-pg-tests",
        ["Authentication:SigningKey"] = "cute-oa-pg-tests-signing-key-with-at-least-thirty-two-bytes",
        ["Authentication:MultiFactor:Enabled"] = "true",
        ["Authentication:MultiFactor:EncryptionKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
        ["Authentication:MultiFactor:RequiredPermissions:0"] = OaPermissions.PersonnelManage
    }).Build();
    var mfaDirectory = new DemoData(writeDb);
    var mfaService = new MultiFactorAuthenticationService(writeDb, mfaDirectory, mfaConfiguration, new MfaSecretProtector(mfaConfiguration));
    var mfaAuthentication = new AuthenticationService(mfaDirectory, new JwtTokenService(mfaConfiguration), writeDb, mfaConfiguration, mfaService);
    var setupLogin = mfaAuthentication.BeginLogin(new LoginRequest(managedUserId, resetChangedPassword), new SessionClient("MFA integration test", "127.0.0.1"));
    if (!setupLogin.IsSuccess || setupLogin.Value is not { Grant: null, Challenge.State: MfaChallengeStates.SetupRequired })
        throw new InvalidOperationException("敏感权限账号首次登录未进入 MFA 强制绑定流程。");
    var setup = mfaAuthentication.StartMfaSetup(new MfaSetupStartRequest(setupLogin.Value.Challenge!.ChallengeToken));
    if (!setup.IsSuccess || setup.Value!.Secret.Length != 32 || !setup.Value.ProvisioningUri.StartsWith("otpauth://totp/", StringComparison.Ordinal))
        throw new InvalidOperationException(setup.Error ?? "MFA 绑定密钥或标准 otpauth URI 未生成。");
    var setupCode = TotpGenerator.GenerateCode(setup.Value.Secret, DateTimeOffset.UtcNow);
    var enrollment = mfaAuthentication.ConfirmMfaSetup(new MfaSetupConfirmRequest(setup.Value.ChallengeToken, setupCode), new SessionClient("MFA integration test", "127.0.0.1"));
    var enrolledAccount = writeDb.UserAccounts.AsNoTracking().Single(item => item.UserId == managedUserId);
    if (!enrollment.IsSuccess || enrollment.Value!.RecoveryCodes.Count != 10 || !enrolledAccount.MfaEnabled || enrolledAccount.MfaSecretCiphertext == setup.Value.Secret || enrollment.Value.RecoveryCodes.Any(code => enrolledAccount.RecoveryCodeHashesJson.Contains(code, StringComparison.Ordinal)))
        throw new InvalidOperationException(enrollment.Error ?? "MFA 密钥加密、恢复码摘要或绑定会话错误。");

    var verificationLogin = mfaAuthentication.BeginLogin(new LoginRequest(managedUserId, resetChangedPassword));
    if (!verificationLogin.IsSuccess || verificationLogin.Value is not { Grant: null, Challenge.State: MfaChallengeStates.Required })
        throw new InvalidOperationException("已绑定账号登录未进入 MFA 验证流程。");
    if (mfaAuthentication.VerifyMfa(new MfaVerifyRequest(verificationLogin.Value.Challenge!.ChallengeToken, setupCode)).IsSuccess)
        throw new InvalidOperationException("同一 TOTP 时间步可以重放登录。");
    var nextTimeStepCode = TotpGenerator.GenerateCode(setup.Value.Secret, DateTimeOffset.UtcNow.AddSeconds(30));
    var verifiedSession = mfaAuthentication.VerifyMfa(new MfaVerifyRequest(verificationLogin.Value.Challenge.ChallengeToken, nextTimeStepCode));
    if (!verifiedSession.IsSuccess || mfaAuthentication.VerifyMfa(new MfaVerifyRequest(verificationLogin.Value.Challenge.ChallengeToken, nextTimeStepCode)).IsSuccess)
        throw new InvalidOperationException("TOTP 验证未签发会话或挑战可以重复使用。");

    var recoveryLogin = mfaAuthentication.BeginLogin(new LoginRequest(managedUserId, resetChangedPassword));
    var recoverySession = mfaAuthentication.VerifyMfa(new MfaVerifyRequest(recoveryLogin.Value!.Challenge!.ChallengeToken, enrollment.Value.RecoveryCodes[0]));
    var mfaStatus = mfaAuthentication.GetMfaStatus(mfaDirectory.GetEmployee(managedUserId));
    if (!recoverySession.IsSuccess || !mfaStatus.IsSuccess || mfaStatus.Value is not { Enabled: true, Required: true, RecoveryCodesRemaining: 9 })
        throw new InvalidOperationException("一次性恢复码未登录或未原子消费。");

    var lockedChallenge = mfaAuthentication.BeginLogin(new LoginRequest(managedUserId, resetChangedPassword)).Value!.Challenge!;
    for (var attempt = 0; attempt < 5; attempt++)
        if (mfaAuthentication.VerifyMfa(new MfaVerifyRequest(lockedChallenge.ChallengeToken, "000000")).IsSuccess)
            throw new InvalidOperationException("错误 MFA 验证码被接受。");
    if (mfaAuthentication.VerifyMfa(new MfaVerifyRequest(lockedChallenge.ChallengeToken, TotpGenerator.GenerateCode(setup.Value.Secret, DateTimeOffset.UtcNow.AddSeconds(30)))).IsSuccess)
        throw new InvalidOperationException("MFA 连续失败五次后挑战仍可使用。");

    var resetMfa = identity.ResetMfa(administrator, managedUserId);
    var resetAccount = writeDb.UserAccounts.AsNoTracking().Single(item => item.UserId == managedUserId);
    var setupRequiredAgain = mfaAuthentication.BeginLogin(new LoginRequest(managedUserId, resetChangedPassword));
    if (!resetMfa.IsSuccess || resetMfa.Value!.MfaEnabled || resetAccount.MfaEnabled || resetAccount.MfaSecretCiphertext is not null || setupRequiredAgain.Value?.Challenge?.State != MfaChallengeStates.SetupRequired)
        throw new InvalidOperationException(resetMfa.Error ?? "管理员 MFA 恢复流程未清理密钥、撤销会话或强制重新绑定。");

    var departments = new DepartmentAdministrationService(writeDb, data);
    if (departments.List(employee).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权维护组织架构。");
    var seededDepartments = departments.List(administrator);
    if (!seededDepartments.IsSuccess || seededDepartments.Value!.Count != IdentityDefaults.Departments.Count || !seededDepartments.Value.Any(item => item.Id == "general" && item.Depth == 0 && item.IsSystem))
        throw new InvalidOperationException("默认部门树未完整初始化或根节点信息错误。");
    if (!data.HasPermission(administrator, OaPermissions.OrgManage) || data.HasPermission(employee, OaPermissions.OrgManage))
        throw new InvalidOperationException("组织架构维护权限未正确解析。");

    var createdDepartment = departments.Create(administrator, new CreateDepartmentRequest(managedDepartmentId, "集成运营部", "engineering", 30));
    if (!createdDepartment.IsSuccess || createdDepartment.Value!.Depth != 2 || createdDepartment.Value.IsSystem || createdDepartment.Value.Version != 1)
        throw new InvalidOperationException(createdDepartment.Error ?? "无法创建自定义部门。");
    if (departments.Create(administrator, new CreateDepartmentRequest(managedDepartmentId, "重复编码部门", "engineering", 31)).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复部门编码。");
    if (departments.Create(administrator, new CreateDepartmentRequest("pg-duplicate-name", "集成运营部", "engineering", 31)).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复部门名称。");
    var createdChildDepartment = departments.Create(administrator, new CreateDepartmentRequest(managedChildDepartmentId, "集成支持组", managedDepartmentId, 10));
    if (!createdChildDepartment.IsSuccess || createdChildDepartment.Value!.Depth != 3)
        throw new InvalidOperationException(createdChildDepartment.Error ?? "无法创建下级部门。");
    if (departments.Delete(administrator, managedDepartmentId, createdDepartment.Value.Version).Code != "CONFLICT_001")
        throw new InvalidOperationException("存在下级部门时仍可删除上级部门。");
    if (departments.Update(administrator, managedDepartmentId, new UpdateDepartmentRequest("集成运营部", managedChildDepartmentId, 30, createdDepartment.Value.Version)).Code != "VALIDATION_001")
        throw new InvalidOperationException("组织架构允许形成循环层级。");

    var departmentAdminSession = authentication.Login(new LoginRequest("u-admin", "Oa@123456"));
    if (!departmentAdminSession.IsSuccess) throw new InvalidOperationException("管理员无法建立组织调整验证会话。");
    var movedChildDepartment = departments.Update(administrator, managedChildDepartmentId, new UpdateDepartmentRequest("集成支持组", "sales", 10, createdChildDepartment.Value.Version));
    var revokedDepartmentSession = writeDb.AuthSessions.Single(item => item.Id == departmentAdminSession.Value!.SessionId);
    writeDb.Entry(revokedDepartmentSession).Reload();
    if (!movedChildDepartment.IsSuccess || movedChildDepartment.Value!.ParentId != "sales" || movedChildDepartment.Value.Depth != 2 || movedChildDepartment.Value.Version != 2 || revokedDepartmentSession.RevokedReason != "DEPARTMENT_HIERARCHY_CHANGED")
        throw new InvalidOperationException(movedChildDepartment.Error ?? "部门移动未持久化或未撤销组织权限缓存会话。");
    if (departments.Update(administrator, managedChildDepartmentId, new UpdateDepartmentRequest("过期修改", "sales", 10, 1)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("部门更新未执行乐观锁校验。");
    var engineeringDepartment = departments.List(administrator).Value!.Single(item => item.Id == "engineering");
    if (departments.Delete(administrator, engineeringDepartment.Id, engineeringDepartment.Version).Code != "STATE_001")
        throw new InvalidOperationException("系统预置部门可以被删除。");
    if (!departments.Delete(administrator, managedChildDepartmentId, movedChildDepartment.Value.Version).IsSuccess)
        throw new InvalidOperationException("无下级且无用户的自定义部门无法删除。");

    var assignedToCustomDepartment = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        updatedUser.Value.Name, managedDepartmentId, updatedUser.Value.ManagerId, updatedUser.Value.CumulativeWorkYears, "ACTIVE", updatedUser.Value.Roles, updatedUser.Value.Version));
    if (!assignedToCustomDepartment.IsSuccess || departments.Delete(administrator, managedDepartmentId, createdDepartment.Value.Version).Code != "CONFLICT_001")
        throw new InvalidOperationException(assignedToCustomDepartment.Error ?? "部门用户占用保护未生效。");
    var returnedToHr = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        assignedToCustomDepartment.Value!.Name, "hr", assignedToCustomDepartment.Value.ManagerId, assignedToCustomDepartment.Value.CumulativeWorkYears, "ACTIVE", assignedToCustomDepartment.Value.Roles, assignedToCustomDepartment.Value.Version));
    if (!returnedToHr.IsSuccess || !departments.Delete(administrator, managedDepartmentId, createdDepartment.Value.Version).IsSuccess)
        throw new InvalidOperationException(returnedToHr.Error ?? "自定义部门清理失败。");

    var generalManager = identity.List(administrator, "u-li", null, null, 1, 20).Value!.Items.Single(item => item.Id == "u-li");
    if (identity.Update(administrator, generalManager.Id, new UpdateManagedUserRequest(
        generalManager.Name, generalManager.DepartmentId, "u-zhang", generalManager.CumulativeWorkYears, generalManager.Status, generalManager.Roles, generalManager.Version)).Code != "VALIDATION_001")
        throw new InvalidOperationException("经理关系允许形成间接循环。");
    if (!writeDb.AuditLogs.Any(item => item.ResourceType == "Department" && item.Action == "DEPARTMENT_CREATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Department" && item.Action == "DEPARTMENT_UPDATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Department" && item.Action == "DEPARTMENT_DELETED"))
        throw new InvalidOperationException("部门新增、移动或删除操作未记录审计日志。");
    if (departments.List(administrator).Value!.Count != IdentityDefaults.Departments.Count)
        throw new InvalidOperationException("组织架构集成用例未恢复默认部门数据。");

    var positions = new PositionAdministrationService(writeDb, data);
    if (positions.List(employee).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权维护岗位。");
    var seededPositions = positions.List(administrator);
    if (!seededPositions.IsSuccess || seededPositions.Value!.Count != IdentityDefaults.Positions.Count || !seededPositions.Value.Any(item => item.Id == "software-engineer" && item.DepartmentId == "engineering" && item.IsSystem))
        throw new InvalidOperationException("默认岗位未完整初始化。");
    var createdPosition = positions.Create(administrator, new CreatePositionRequest(managedPositionId, "集成人力伙伴", "hr", 30));
    if (!createdPosition.IsSuccess || createdPosition.Value!.Status != "ACTIVE" || createdPosition.Value.UserCount != 0 || createdPosition.Value.Version != 1)
        throw new InvalidOperationException(createdPosition.Error ?? "无法创建自定义岗位。");
    if (positions.Create(administrator, new CreatePositionRequest(managedPositionId, "重复编码岗位", "hr", 31)).Code != "DUPLICATE_001")
        throw new InvalidOperationException("系统允许创建重复岗位编码。");
    if (positions.Create(administrator, new CreatePositionRequest("pg-duplicate-position", "集成人力伙伴", "hr", 31)).Code != "DUPLICATE_001")
        throw new InvalidOperationException("同一部门允许创建同名岗位。");
    var updatedPosition = positions.Update(administrator, managedPositionId, new UpdatePositionRequest("高级集成人力伙伴", "hr", 35, "ACTIVE", createdPosition.Value.Version));
    if (!updatedPosition.IsSuccess || updatedPosition.Value!.Version != 2 || updatedPosition.Value.SortOrder != 35)
        throw new InvalidOperationException(updatedPosition.Error ?? "岗位名称或排序无法更新。");
    if (positions.Update(administrator, managedPositionId, new UpdatePositionRequest("过期更新", "hr", 35, "ACTIVE", 1)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("岗位更新未执行乐观锁校验。");
    if (identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        returnedToHr.Value!.Name, "hr", returnedToHr.Value.ManagerId, returnedToHr.Value.CumulativeWorkYears, "ACTIVE", returnedToHr.Value.Roles, returnedToHr.Value.Version, "software-engineer")).Code != "VALIDATION_001")
        throw new InvalidOperationException("用户可以分配其他部门的岗位。");
    var positionAssignmentSession = authentication.Login(new LoginRequest(managedUserId, resetChangedPassword));
    var assignedPosition = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        returnedToHr.Value.Name, "hr", returnedToHr.Value.ManagerId, returnedToHr.Value.CumulativeWorkYears, "ACTIVE", returnedToHr.Value.Roles, returnedToHr.Value.Version, managedPositionId));
    var revokedPositionSession = writeDb.AuthSessions.Single(item => item.Id == positionAssignmentSession.Value!.SessionId);
    writeDb.Entry(revokedPositionSession).Reload();
    if (!assignedPosition.IsSuccess || assignedPosition.Value!.PositionId != managedPositionId || assignedPosition.Value.PositionName != "高级集成人力伙伴" || revokedPositionSession.RevokedReason != "USER_ORGANIZATION_CHANGED")
        throw new InvalidOperationException(assignedPosition.Error ?? "用户岗位分配未生效或未撤销旧会话。");
    if (positions.Delete(administrator, managedPositionId, updatedPosition.Value.Version).Code != "CONFLICT_001" ||
        positions.Update(administrator, managedPositionId, new UpdatePositionRequest("高级集成人力伙伴", "engineering", 35, "ACTIVE", updatedPosition.Value.Version)).Code != "CONFLICT_001" ||
        positions.Update(administrator, managedPositionId, new UpdatePositionRequest("高级集成人力伙伴", "hr", 35, "DISABLED", updatedPosition.Value.Version)).Code != "CONFLICT_001")
        throw new InvalidOperationException("已分配岗位的删除、跨部门移动或停用保护未生效。");
    var clearedPosition = identity.Update(administrator, managedUserId, new UpdateManagedUserRequest(
        assignedPosition.Value.Name, "hr", assignedPosition.Value.ManagerId, assignedPosition.Value.CumulativeWorkYears, "ACTIVE", assignedPosition.Value.Roles, assignedPosition.Value.Version, null));
    if (!clearedPosition.IsSuccess || clearedPosition.Value!.PositionId is not null || !positions.Delete(administrator, managedPositionId, updatedPosition.Value.Version).IsSuccess)
        throw new InvalidOperationException(clearedPosition.Error ?? "岗位解除分配或自定义岗位删除失败。");
    var systemPosition = positions.List(administrator).Value!.Single(item => item.Id == "software-engineer");
    if (positions.Delete(administrator, systemPosition.Id, systemPosition.Version).Code != "STATE_001")
        throw new InvalidOperationException("系统预置岗位可以被删除。");
    if (!writeDb.AuditLogs.Any(item => item.ResourceType == "Position" && item.Action == "POSITION_CREATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Position" && item.Action == "POSITION_UPDATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Position" && item.Action == "POSITION_DELETED") ||
        positions.List(administrator).Value!.Count != IdentityDefaults.Positions.Count)
        throw new InvalidOperationException("岗位审计记录不完整或集成用例未恢复默认岗位数据。");

    var announcements = new AnnouncementService(writeDb, data);
    if (announcements.ListAdmin(employee, null, 1, 20).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权维护公告。");
    if (!data.HasPermission(hr, OaPermissions.AnnouncementManage) || announcements.Create(hr, new SaveAnnouncementRequest("", "无效公告", null)).Code != "VALIDATION_001")
        throw new InvalidOperationException("HR 公告管理权限或公告必填校验错误。");
    var expiresAt = DateTimeOffset.UtcNow.AddDays(7);
    var draftAnnouncement = announcements.Create(hr, new SaveAnnouncementRequest("数据库维护通知", "本周六凌晨进行系统维护，请提前保存工作。", expiresAt));
    if (!draftAnnouncement.IsSuccess || draftAnnouncement.Value!.Status != "DRAFT" || draftAnnouncement.Value.Version != 1 || announcements.ListPublished(employee, 1, 20).Value!.Total != 1)
        throw new InvalidOperationException(draftAnnouncement.Error ?? "公告草稿创建或发布前隔离失败。");
    var updatedAnnouncement = announcements.Update(hr, draftAnnouncement.Value.Id, new SaveAnnouncementRequest("数据库维护时间调整", "系统维护调整至本周日凌晨，请提前保存工作。", expiresAt, draftAnnouncement.Value.Version));
    if (!updatedAnnouncement.IsSuccess || updatedAnnouncement.Value!.Version != 2 || announcements.Update(hr, draftAnnouncement.Value.Id, new SaveAnnouncementRequest("过期更新", "过期版本不应保存。", expiresAt, 1)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException(updatedAnnouncement.Error ?? "公告草稿更新或乐观锁校验失败。");
    if (announcements.Publish(hr, draftAnnouncement.Value.Id, 1).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("公告发布未执行乐观锁校验。");
    var publishedAnnouncement = announcements.Publish(hr, draftAnnouncement.Value.Id, updatedAnnouncement.Value.Version);
    if (!publishedAnnouncement.IsSuccess || publishedAnnouncement.Value!.Status != "PUBLISHED" || publishedAnnouncement.Value.Version != 3 || announcements.Update(hr, draftAnnouncement.Value.Id, new SaveAnnouncementRequest("违规编辑", "已发布公告不应编辑。", expiresAt, 3)).Code != "STATE_001")
        throw new InvalidOperationException(publishedAnnouncement.Error ?? "公告发布或发布后只读保护失败。");
    announcementId = publishedAnnouncement.Value.Id;
    var secondDraft = announcements.Create(administrator, new SaveAnnouncementRequest("新员工入职欢迎", "欢迎新同事加入 xxx 公司。", null));
    var secondPublished = announcements.Publish(administrator, secondDraft.Value!.Id, secondDraft.Value.Version);
    var publishedPage = announcements.ListPublished(employee, 1, 1);
    if (!secondPublished.IsSuccess || !publishedPage.IsSuccess || publishedPage.Value!.Total != 3 || publishedPage.Value.Items.Single().Id != secondPublished.Value!.Id || publishedPage.Value.TotalPages != 3)
        throw new InvalidOperationException(secondPublished.Error ?? "公告发布时间倒序或服务端分页错误。");
    var announcementDetail = announcements.Get(employee, announcementId);
    if (!announcementDetail.IsSuccess || announcementDetail.Value!.ReadAt is not null || !announcements.MarkRead(employee, announcementId).IsSuccess || !announcements.MarkRead(employee, announcementId).IsSuccess || writeDb.AnnouncementReads.Count(item => item.AnnouncementId == announcementId && item.UserId == employee.Id) != 1)
        throw new InvalidOperationException("公告详情、确认已读或重复已读幂等失败。");
    if (announcements.Create(hr, new SaveAnnouncementRequest("过期公告", "不允许设置过去时间。", DateTimeOffset.UtcNow.AddMinutes(-1))).Code != "VALIDATION_001")
        throw new InvalidOperationException("公告允许设置过去的有效期。");
    if (announcements.Withdraw(hr, announcementId, 2).Code != "CONCURRENCY_001" || !announcements.Withdraw(hr, announcementId, publishedAnnouncement.Value.Version).IsSuccess || !announcements.Withdraw(administrator, secondPublished.Value.Id, secondPublished.Value.Version).IsSuccess)
        throw new InvalidOperationException("公告撤回或撤回版本校验失败。");
    if (announcements.ListPublished(employee, 1, 20).Value!.Total != 1 || announcements.Get(employee, announcementId).Code != "DATA_001" || !announcements.Get(administrator, announcementId).IsSuccess)
        throw new InvalidOperationException("撤回公告仍向普通员工可见或管理员无法追溯。");
    if (!writeDb.AuditLogs.Any(item => item.ResourceType == "Announcement" && item.Action == "ANNOUNCEMENT_CREATED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Announcement" && item.Action == "ANNOUNCEMENT_PUBLISHED") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Announcement" && item.Action == "ANNOUNCEMENT_WITHDRAWN") ||
        !writeDb.AuditLogs.Any(item => item.ResourceType == "Announcement" && item.Action == "ANNOUNCEMENT_READ"))
        throw new InvalidOperationException("公告关键动作审计日志不完整。");

    var definitions = processes.List(administrator);
    if (!definitions.IsSuccess || definitions.Value!.Count != 5 || definitions.Value.Any(item => item.Status != ProcessDefinitionStatus.Published) || definitions.Value.All(item => item.BusinessType != "Travel") || definitions.Value.All(item => item.BusinessType != "Purchase") || definitions.Value.All(item => item.BusinessType != "Seal"))
        throw new InvalidOperationException("默认请假、报销、出差、采购和用章流程未能初始化为已发布版本。");
    if (processes.List(employee).IsSuccess)
        throw new InvalidOperationException("普通员工可以越权维护流程定义。");
    var fallbackDefinition = new ProcessDefinitionView(Guid.NewGuid(), "FALLBACK_TEST", "异常路由验证", "Leave", 1, ProcessDefinitionStatus.Draft,
        [new ProcessRouteView(Guid.NewGuid(), 1, null, ["DIRECT_MANAGER"], [new ProcessNodePolicyView(Guid.NewGuid(), 1, "DIRECT_MANAGER", 8, 2, 4, "PROCESS_ADMIN", "PROCESS_ADMIN", false)])],
        administrator.Id, DateTimeOffset.UtcNow, null, null);
    var fallbackResolved = ProcessRouting.Resolve(fallbackDefinition, data, data.GetEmployee("u-wang"), 1m);
    if (!fallbackResolved.IsSuccess || fallbackResolved.Value!.Approvers.SingleOrDefault()?.Assignee.Id != "u-admin" || fallbackResolved.Value.RoutingNotes is not { Count: > 0 })
        throw new InvalidOperationException("审批人缺失时未按节点策略转交流程管理员，或未记录路由说明。");
    var duplicateDefinition = fallbackDefinition with
    {
        Id = Guid.NewGuid(),
        Code = "DUPLICATE_TEST",
        Routes = [new ProcessRouteView(Guid.NewGuid(), 1, null, ["DIRECT_MANAGER", "USER:u-li"],
        [
            new ProcessNodePolicyView(Guid.NewGuid(), 1, "DIRECT_MANAGER", 8, 2, 4, "PROCESS_ADMIN", "BLOCK", false),
            new ProcessNodePolicyView(Guid.NewGuid(), 2, "USER:u-li", 8, 2, 4, "PROCESS_ADMIN", "BLOCK", true)
        ])]
    };
    var duplicateResolved = ProcessRouting.Resolve(duplicateDefinition, data, employee, 1m);
    if (!duplicateResolved.IsSuccess || duplicateResolved.Value!.Approvers.Count != 1 || duplicateResolved.Value.RoutingNotes is not { Count: > 0 })
        throw new InvalidOperationException("连续节点命中同一审批人时未按允许自动跳过策略处理。");
    var blockedDuplicate = duplicateDefinition with
    {
        Id = Guid.NewGuid(),
        Code = "DUPLICATE_BLOCK_TEST",
        Routes = [new ProcessRouteView(Guid.NewGuid(), 1, null, ["DIRECT_MANAGER", "USER:u-li"],
        [
            new ProcessNodePolicyView(Guid.NewGuid(), 1, "DIRECT_MANAGER", 8, 2, 4, "PROCESS_ADMIN", "BLOCK", false),
            new ProcessNodePolicyView(Guid.NewGuid(), 2, "USER:u-li", 8, 2, 4, "PROCESS_ADMIN", "BLOCK", false)
        ])]
    };
    if (ProcessRouting.Resolve(blockedDuplicate, data, employee, 1m).Code != "FLOW_001")
        throw new InvalidOperationException("连续节点命中同一审批人且不允许跳过时未阻断流程。");
    var nonConsecutiveDuplicate = duplicateDefinition with
    {
        Id = Guid.NewGuid(),
        Code = "NON_CONSECUTIVE_DUPLICATE_TEST",
        Routes = [new ProcessRouteView(Guid.NewGuid(), 1, null, ["DIRECT_MANAGER", "USER:u-wang", "USER:u-li"],
        [
            new ProcessNodePolicyView(Guid.NewGuid(), 1, "DIRECT_MANAGER", 8, 2, 4, "PROCESS_ADMIN", "BLOCK", false),
            new ProcessNodePolicyView(Guid.NewGuid(), 2, "USER:u-wang", 8, 2, 4, "PROCESS_ADMIN", "BLOCK", false),
            new ProcessNodePolicyView(Guid.NewGuid(), 3, "USER:u-li", 8, 2, 4, "PROCESS_ADMIN", "BLOCK", false)
        ])]
    };
    var nonConsecutiveResolved = ProcessRouting.Resolve(nonConsecutiveDuplicate, data, employee, 1m);
    if (!nonConsecutiveResolved.IsSuccess || nonConsecutiveResolved.Value!.Approvers.Count != 3)
        throw new InvalidOperationException("非连续重复审批人被错误地当作连续节点阻断或跳过。");
    fileRoot = Path.Combine(Path.GetTempPath(), "cute-oa-file-test", Guid.NewGuid().ToString("N"));
    var cleanFileScanner = new TestFileMalwareScanner(FileScanStatus.Clean);
    var fileService = new FileService(writeDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build(), cleanFileScanner);
    var attendance = new AttendanceService(writeDb, persistedDirectory, calendar, notifications, fileService, authConfiguration);
    var attendanceMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
    attendanceLockMonth = attendanceMonth;
    if (attendance.GenerateDemoData(employee, attendanceMonth).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权生成模拟考勤。 ");
    var generatedAttendance = attendance.GenerateDemoData(hr, attendanceMonth);
    if (!generatedAttendance.IsSuccess || generatedAttendance.Value!.Created == 0 || attendance.GenerateDemoData(hr, attendanceMonth).Value!.Created != 0)
        throw new InvalidOperationException(generatedAttendance.Error ?? "模拟考勤未生成或重复生成覆盖了已有数据。 ");
    var employeeAttendance = attendance.List(employee, new AttendanceListQuery(null, null, null, null, attendanceMonth));
    if (employeeAttendance.Count == 0 || employeeAttendance.Any(item => item.UserId != employee.Id))
        throw new InvalidOperationException("普通员工考勤列表未限制为本人。 ");
    var managerAttendance = attendance.List(data.GetEmployee("u-li"), new AttendanceListQuery(null, null, null, null, attendanceMonth));
    if (!managerAttendance.Any(item => item.UserId == employee.Id))
        throw new InvalidOperationException("直属上级无法查看下属考勤。 ");
    var attendanceDate = attendanceMonth;
    while (!calendar.IsWorkingDay(attendanceDate)) attendanceDate = attendanceDate.AddDays(1);
    var forcedLate = new AttendanceImportItem(employee.Id, attendanceDate,
        new DateTimeOffset(attendanceDate.ToDateTime(new TimeOnly(9, 30)), TimeSpan.FromHours(8)),
        new DateTimeOffset(attendanceDate.ToDateTime(new TimeOnly(18, 5)), TimeSpan.FromHours(8)));
    if (attendance.Import(employee, new ImportAttendanceRequest([forcedLate], "MANUAL")).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权录入考勤。 ");
    var importedAttendance = attendance.Import(hr, new ImportAttendanceRequest([forcedLate], "MANUAL"));
    if (!importedAttendance.IsSuccess || importedAttendance.Value!.Updated != 1)
        throw new InvalidOperationException(importedAttendance.Error ?? "HR 无法更新已有考勤。 ");
    var lateRecord = attendance.List(employee, new AttendanceListQuery(null, employee.Id, null, AttendanceStatuses.Late, attendanceMonth)).SingleOrDefault(item => item.WorkDate == attendanceDate);
    if (lateRecord is null || lateRecord.LateMinutes != 25 || lateRecord.Source != "MANUAL")
        throw new InvalidOperationException("迟到识别、宽限分钟或数据来源不正确。 ");
    attendanceRecordId = lateRecord.Id;
    if (attendance.SubmitAppeal(data.GetEmployee("u-li"), attendanceRecordId, new CreateAttendanceAppealRequest("代员工提交申诉")).Code != "DATA_001")
        throw new InvalidOperationException("上级可以代员工提交考勤申诉。 ");
    var submittedAppeal = attendance.SubmitAppeal(employee, attendanceRecordId, new CreateAttendanceAppealRequest("当天地铁故障导致迟到，请核实。"));
    if (!submittedAppeal.IsSuccess || attendance.SubmitAppeal(employee, attendanceRecordId, new CreateAttendanceAppealRequest("重复提交同一待审申诉。" )).Code != "ATTENDANCE_002")
        throw new InvalidOperationException(submittedAppeal.Error ?? "员工考勤申诉或重复保护失败。 ");
    attendanceAppealId = submittedAppeal.Value!.Id;
    var reviewedAppeal = attendance.ReviewAppeal(hr, attendanceAppealId, new ReviewAttendanceAppealRequest(true, "已核实交通故障，本次不计迟到。"));
    var correctedRecord = attendance.Get(employee, attendanceRecordId);
    if (!reviewedAppeal.IsSuccess || correctedRecord.Value!.Status != AttendanceStatuses.Corrected || correctedRecord.Value.OriginalStatus != AttendanceStatuses.Late)
        throw new InvalidOperationException(reviewedAppeal.Error ?? "考勤申诉通过后未修正记录或未保留原始异常。 ");
    var summary = attendance.MonthlySummary(employee, attendanceMonth).Value!.Single();
    if (summary.CorrectedDays < 1 || summary.PendingAppeals != 0)
        throw new InvalidOperationException("考勤月度汇总未反映申诉修正结果。 ");
    var defaultShift = attendance.ListShifts(employee).Single(item => item.IsDefault);
    if (attendance.UpdateShift(employee, defaultShift.Id, new SaveAttendanceShiftRequest(defaultShift.Code, defaultShift.Name, defaultShift.WorkStart, defaultShift.WorkEnd, defaultShift.BreakMinutes, defaultShift.LateToleranceMinutes, defaultShift.EarlyLeaveToleranceMinutes, true, true, defaultShift.Version)).Code != "AUTH_002" ||
        attendance.UpdateShift(hr, defaultShift.Id, new SaveAttendanceShiftRequest(defaultShift.Code, defaultShift.Name, defaultShift.WorkStart, defaultShift.WorkEnd, defaultShift.BreakMinutes, defaultShift.LateToleranceMinutes, defaultShift.EarlyLeaveToleranceMinutes, true, true, defaultShift.Version - 1)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("考勤班次权限或乐观版本校验失败。 ");
    var secondEmployee = data.GetEmployee("u-wang");
    var secondLate = forcedLate with { UserId = secondEmployee.Id };
    var secondImport = attendance.Import(hr, new ImportAttendanceRequest([secondLate], "MANUAL"));
    var secondLateRecord = attendance.List(secondEmployee, new AttendanceListQuery(null, secondEmployee.Id, null, AttendanceStatuses.Late, attendanceMonth)).Single(item => item.WorkDate == attendanceDate);
    var pendingClosureAppeal = attendance.SubmitAppeal(secondEmployee, secondLateRecord.Id, new CreateAttendanceAppealRequest("封账前验证待审申诉拦截。"));
    if (!secondImport.IsSuccess || !pendingClosureAppeal.IsSuccess)
        throw new InvalidOperationException(secondImport.Error ?? pendingClosureAppeal.Error ?? "无法建立考勤封账前置测试数据。 ");
    if (attendance.LockMonth(employee, attendanceMonth, new ChangeAttendanceMonthLockRequest("普通员工尝试封账", 0)).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权封账考勤月份。 ");
    if (attendance.LockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("月报核对完成并申请封账", 0)).Code != "ATTENDANCE_005")
        throw new InvalidOperationException("存在待审核申诉时仍可封账。 ");
    if (!attendance.ReviewAppeal(hr, pendingClosureAppeal.Value!.Id, new ReviewAttendanceAppealRequest(false, "封账前已核验，本次申诉不通过。")).IsSuccess)
        throw new InvalidOperationException("封账前无法办结待审核申诉。 ");
    var lockedMonth = attendance.LockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("本月异常和申诉已处理完毕，月报核对完成。", 0));
    if (!lockedMonth.IsSuccess || lockedMonth.Value is not { IsLocked: true, Version: 1, SnapshotSequence: 1, SnapshotRowCount: > 0 } || lockedMonth.Value.SnapshotHash?.Length != 64)
        throw new InvalidOperationException(lockedMonth.Error ?? "考勤月份无法封账。 ");
    var frozenEmployeeSummary = attendance.MonthlySummary(employee, attendanceMonth);
    if (!frozenEmployeeSummary.IsSuccess || frozenEmployeeSummary.Value!.Single().UserId != employee.Id)
        throw new InvalidOperationException(frozenEmployeeSummary.Error ?? "封账后未按员工数据范围读取不可变月报快照。 ");
    if (attendance.ExportMonthSnapshot(employee, attendanceMonth).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以下载全公司封账月报。 ");
    var firstSnapshotExport = attendance.ExportMonthSnapshot(hr, attendanceMonth);
    if (!firstSnapshotExport.IsSuccess || firstSnapshotExport.Value is not { Sequence: 1, RowCount: > 0 } ||
        firstSnapshotExport.Value.Content.Length < 3 || firstSnapshotExport.Value.Content[0] != 0xEF || firstSnapshotExport.Value.Content[1] != 0xBB || firstSnapshotExport.Value.Content[2] != 0xBF ||
        !System.Text.Encoding.UTF8.GetString(firstSnapshotExport.Value.Content).Contains("\"月份\",\"用户 ID\",\"员工姓名\"", StringComparison.Ordinal) ||
        !writeDb.AuditLogs.Any(item => item.Action == "ATTENDANCE_MONTH_SNAPSHOT_DOWNLOADED" && item.ActorId == hr.Id))
        throw new InvalidOperationException(firstSnapshotExport.Error ?? "封账月报下载内容、权限或审计不正确。 ");
    if (attendance.Import(hr, new ImportAttendanceRequest([forcedLate], "MANUAL")).Code != "ATTENDANCE_004" ||
        attendance.GenerateDemoData(hr, attendanceMonth).Code != "ATTENDANCE_004" ||
        attendance.SubmitAppeal(employee, attendanceRecordId, new CreateAttendanceAppealRequest("封账后不应允许申诉。" )).Code != "ATTENDANCE_004")
        throw new InvalidOperationException("封账后仍可导入、生成或提交考勤申诉。 ");
    if (attendance.UnlockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("使用过期版本尝试解封", 0)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException("考勤解封未校验乐观版本。 ");
    var unlockedMonth = attendance.UnlockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("发现需补充核验的历史考勤，授权临时解封。", lockedMonth.Value.Version));
    if (!unlockedMonth.IsSuccess || unlockedMonth.Value is not { IsLocked: false, Version: 2 })
        throw new InvalidOperationException(unlockedMonth.Error ?? "考勤月份无法解封。 ");
    var relockChange = secondLate with
    {
        CheckInAt = new DateTimeOffset(attendanceDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(8))
    };
    if (!attendance.Import(hr, new ImportAttendanceRequest([relockChange], "MANUAL")).IsSuccess)
        throw new InvalidOperationException("考勤解封后无法补充核验数据。 ");
    var relockedMonth = attendance.LockMonth(hr, attendanceMonth, new ChangeAttendanceMonthLockRequest("补充核验完成，重新确认本月月报。", unlockedMonth.Value.Version));
    if (!relockedMonth.IsSuccess || relockedMonth.Value is not { IsLocked: true, Version: 3, SnapshotSequence: 2 } || relockedMonth.Value.SnapshotHash == lockedMonth.Value.SnapshotHash)
        throw new InvalidOperationException(relockedMonth.Error ?? "考勤月份解封后无法重新封账。 ");
    var currentSnapshot = writeDb.AttendanceMonthSnapshots.Single(item => item.Month == attendanceMonth && item.Sequence == 2);
    var validSnapshotJson = currentSnapshot.SnapshotJson;
    currentSnapshot.SnapshotJson = "[]";
    writeDb.SaveChanges();
    if (attendance.MonthlySummary(hr, attendanceMonth).Code != "ATTENDANCE_SNAPSHOT_002" || attendance.ExportMonthSnapshot(hr, attendanceMonth).Code != "ATTENDANCE_SNAPSHOT_002")
        throw new InvalidOperationException("封账月报快照被篡改后仍可读取或下载。 ");
    currentSnapshot.SnapshotJson = validSnapshotJson;
    writeDb.SaveChanges();
    var contractService = new EmploymentContractService(writeDb, persistedDirectory, fileService, notifications, authConfiguration);
    var contractStart = DateOnly.FromDateTime(DateTime.Today).AddYears(-1);
    var contractEnd = DateOnly.FromDateTime(DateTime.Today).AddDays(20);
    var baseContract = new SaveEmploymentContractRequest(employee.Id, EmploymentContractTypes.FixedTerm, null, contractStart, contractEnd, null, null, "上海办公室", "研发工程师", null, [], "数据库集成测试合同", 1, "建立劳动合同测试草稿");
    if (contractService.Create(employee, baseContract).Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权创建劳动合同。 ");
    var invalidProbation = baseContract with { ProbationStartDate = contractStart, ProbationEndDate = contractStart.AddMonths(3), EndDate = contractStart.AddYears(1), ChangeReason = "验证试用期期限上限" };
    if (contractService.Create(hr, invalidProbation).Code != "CONTRACT_003")
        throw new InvalidOperationException("超过法定上限的试用期未被拒绝。 ");
    var contractDraft = contractService.Create(hr, baseContract);
    if (!contractDraft.IsSuccess || contractService.Activate(hr, contractDraft.Value!.Id, new ActivateEmploymentContractRequest(contractDraft.Value.Version, "核验签署件后激活合同")).Code != "CONTRACT_005")
        throw new InvalidOperationException(contractDraft.Error ?? "未签订或无附件的合同可以激活。 ");
    employmentContractId = contractDraft.Value.Id;
    if (!contractService.Get(employee, employmentContractId).IsSuccess || contractService.Get(data.GetEmployee("u-li"), employmentContractId).Code != "AUTH_002")
        throw new InvalidOperationException("劳动合同本人查看或直属上级敏感数据隔离错误。 ");
    if (contractService.List(data.GetEmployee("u-li"), null, null, null, null, null, 1, 20).Value!.Items.Any(item => item.UserId == employee.Id))
        throw new InvalidOperationException("直属上级通过合同列表读取了下属敏感合同。 ");
    var signedFile = fileService.CreateDemoPdf(hr, "张晨-签署劳动合同.pdf");
    var completedDraft = contractService.Update(hr, employmentContractId, baseContract with { SignedDate = DateOnly.FromDateTime(DateTime.Today), Attachments = [signedFile.Value!.Id.ToString()], Version = contractDraft.Value.Version, ChangeReason = "补充已签署合同扫描件" });
    if (!completedDraft.IsSuccess) throw new InvalidOperationException(completedDraft.Error ?? "HR 无法补充合同签署附件。 ");
    var activatedContract = contractService.Activate(hr, employmentContractId, new ActivateEmploymentContractRequest(completedDraft.Value!.Version, "签署主体和扫描件核验完成"));
    if (!activatedContract.IsSuccess || activatedContract.Value!.Status != EmploymentContractStatuses.Active || activatedContract.Value.DisplayStatus != EmploymentContractStatuses.Expiring)
        throw new InvalidOperationException(activatedContract.Error ?? "合同无法激活或到期展示状态错误。 ");
    var overlapFile = fileService.CreateDemoPdf(hr, "张晨-重叠合同.pdf");
    var contractOverlapDraft = contractService.Create(hr, baseContract with { SignedDate = DateOnly.FromDateTime(DateTime.Today), Attachments = [overlapFile.Value!.Id.ToString()], ChangeReason = "验证有效合同日期重叠保护" });
    if (!contractOverlapDraft.IsSuccess || contractService.Activate(hr, contractOverlapDraft.Value!.Id, new ActivateEmploymentContractRequest(contractOverlapDraft.Value.Version, "验证日期重叠保护逻辑")).Code != "CONTRACT_002")
        throw new InvalidOperationException("系统允许激活日期重叠的有效合同。 ");

    var concurrentContractFile = fileService.CreateDemoPdf(hr, "王芳-并发激活合同.pdf");
    var concurrentContractStart = BusinessTime.ChinaToday().AddMonths(-3);
    var concurrentContractEnd = BusinessTime.ChinaToday().AddMonths(9);
    var concurrentContractRequest = baseContract with
    {
        UserId = secondEmployee.Id,
        SignedDate = BusinessTime.ChinaToday(),
        StartDate = concurrentContractStart,
        EndDate = concurrentContractEnd,
        Attachments = [concurrentContractFile.Value!.Id.ToString()],
        ChangeReason = "验证跨实例并发激活保护"
    };
    var concurrentContractDraftA = contractService.Create(hr, concurrentContractRequest);
    var concurrentContractDraftB = contractService.Create(hr, concurrentContractRequest with { ChangeReason = "验证第二份并发激活草稿" });
    if (!concurrentContractDraftA.IsSuccess || !concurrentContractDraftB.IsSuccess)
        throw new InvalidOperationException(concurrentContractDraftA.Error ?? concurrentContractDraftB.Error ?? "无法创建并发合同草稿。 ");
    var concurrentContractDraftAValue = concurrentContractDraftA.Value!;
    var concurrentContractDraftBValue = concurrentContractDraftB.Value!;
    using (var activationGate = new Barrier(2))
    {
        var activationTasks = new[] { concurrentContractDraftAValue, concurrentContractDraftBValue }.Select(draft => Task.Run(() =>
        {
            using var contractDb = new OaDbContext(options);
            var contractDirectory = new DemoData(contractDb);
            var contractFiles = new FileService(contractDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build(), new TestFileMalwareScanner(FileScanStatus.Clean));
            _ = contractDb.EmploymentContracts.Single(item => item.Id == draft.Id);
            if (!activationGate.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException("并发合同激活测试未能同步启动。 ");
            return new EmploymentContractService(contractDb, contractDirectory, contractFiles, new NotificationService(contractDb), authConfiguration)
                .Activate(contractDirectory.GetEmployee(hr.Id), draft.Id, new ActivateEmploymentContractRequest(draft.Version, "并发核验后激活合同"));
        })).ToArray();
        var activationResults = await Task.WhenAll(activationTasks);
        if (activationResults.Count(result => result.IsSuccess) != 1 || activationResults.Count(result => result.Code == "CONTRACT_002") != 1)
            throw new InvalidOperationException("两个独立服务实例并发激活重叠合同时未稳定保留唯一有效合同。 ");
    }
    await using (var concurrentContractVerificationDb = new OaDbContext(options))
    {
        var concurrentIds = new[] { concurrentContractDraftAValue.Id, concurrentContractDraftBValue.Id };
        var concurrentResourceIds = concurrentIds.Select(id => id.ToString()).ToArray();
        if (await concurrentContractVerificationDb.EmploymentContracts.CountAsync(item => concurrentIds.Contains(item.Id) && item.Status == EmploymentContractStatuses.Active) != 1 ||
            await concurrentContractVerificationDb.EmploymentContracts.CountAsync(item => concurrentIds.Contains(item.Id) && item.Status == EmploymentContractStatuses.Draft) != 1 ||
            await concurrentContractVerificationDb.AuditLogs.CountAsync(item => concurrentResourceIds.Contains(item.ResourceId) && item.Action == "EMPLOYMENT_CONTRACT_ACTIVATED") != 1)
            throw new InvalidOperationException("并发合同激活后的状态或审计未保持单一提交。 ");
    }
    await using (var constraintConnection = new NpgsqlConnection(connectionString))
    {
        await constraintConnection.OpenAsync();
        await using var constraintCommand = new NpgsqlCommand("SELECT COUNT(*) FROM pg_constraint WHERE conname = 'EX_employment_contract_active_period'", constraintConnection);
        if (Convert.ToInt32(await constraintCommand.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("有效劳动合同日期排斥约束未随迁移创建。 ");
    }

    var staleVersionDraft = contractService.Create(hr, concurrentContractRequest with
    {
        StartDate = concurrentContractEnd.AddDays(1),
        EndDate = concurrentContractEnd.AddYears(1),
        ChangeReason = "创建合同版本并发验证草稿"
    });
    if (!staleVersionDraft.IsSuccess) throw new InvalidOperationException(staleVersionDraft.Error ?? "无法创建合同版本并发验证草稿。 ");
    var staleVersionDraftValue = staleVersionDraft.Value!;
    await using (var contractVersionDbA = new OaDbContext(options))
    await using (var contractVersionDbB = new OaDbContext(options))
    {
        var directoryA = new DemoData(contractVersionDbA);
        var directoryB = new DemoData(contractVersionDbB);
        var filesA = new FileService(contractVersionDbA, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build(), new TestFileMalwareScanner(FileScanStatus.Clean));
        var filesB = new FileService(contractVersionDbB, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build(), new TestFileMalwareScanner(FileScanStatus.Clean));
        _ = await contractVersionDbA.EmploymentContracts.SingleAsync(item => item.Id == staleVersionDraftValue.Id);
        _ = await contractVersionDbB.EmploymentContracts.SingleAsync(item => item.Id == staleVersionDraftValue.Id);
        var updateA = new EmploymentContractService(contractVersionDbA, directoryA, filesA, new NotificationService(contractVersionDbA), authConfiguration)
            .Update(directoryA.GetEmployee(hr.Id), staleVersionDraftValue.Id, concurrentContractRequest with { StartDate = concurrentContractEnd.AddDays(1), EndDate = concurrentContractEnd.AddYears(1), Notes = "首个实例更新成功", Version = staleVersionDraftValue.Version, ChangeReason = "首个实例更新合同草稿" });
        var updateB = new EmploymentContractService(contractVersionDbB, directoryB, filesB, new NotificationService(contractVersionDbB), authConfiguration)
            .Update(directoryB.GetEmployee(hr.Id), staleVersionDraftValue.Id, concurrentContractRequest with { StartDate = concurrentContractEnd.AddDays(1), EndDate = concurrentContractEnd.AddYears(1), Notes = "陈旧实例不应覆盖", Version = staleVersionDraftValue.Version, ChangeReason = "陈旧实例更新合同草稿" });
        if (!updateA.IsSuccess || updateB.Code != "CONCURRENCY_001")
            throw new InvalidOperationException("劳动合同数据库乐观版本未拒绝陈旧服务实例更新。 ");
    }
    var contractSummary = contractService.AlertSummary(hr);
    if (contractSummary.Within30Days < 1 || contractSummary.Unacknowledged < 1 || !contractService.AcknowledgeAlert(hr, employmentContractId, new AcknowledgeContractAlertRequest(30)).IsSuccess)
        throw new InvalidOperationException("合同到期预警分档或确认处理错误。 ");
    var dispatchedAlerts = contractService.DispatchDueAlerts();
    if (dispatchedAlerts < 1 || contractService.DispatchDueAlerts() != 0)
        throw new InvalidOperationException("合同到期预警后台派发未执行或重复派发。 ");
    var renewalFile = fileService.CreateDemoPdf(hr, "张晨-续签劳动合同.pdf");
    var renewalStart = contractEnd.AddDays(1);
    var renewalRequest = baseContract with { SignedDate = DateOnly.FromDateTime(DateTime.Today), StartDate = renewalStart, EndDate = renewalStart.AddYears(1), Attachments = [renewalFile.Value!.Id.ToString()], ChangeReason = "劳动合同到期续签", Version = activatedContract.Value.Version };
    using (var renewalGate = new Barrier(2))
    {
        var renewalTasks = Enumerable.Range(0, 2).Select(attempt => Task.Run(() =>
        {
            _ = attempt;
            using var renewalDb = new OaDbContext(options);
            var renewalDirectory = new DemoData(renewalDb);
            var renewalFiles = new FileService(renewalDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build(), new TestFileMalwareScanner(FileScanStatus.Clean));
            _ = renewalDb.EmploymentContracts.Single(item => item.Id == employmentContractId);
            if (!renewalGate.SignalAndWait(TimeSpan.FromSeconds(15))) throw new TimeoutException("并发合同续签测试未能同步启动。 ");
            return new EmploymentContractService(renewalDb, renewalDirectory, renewalFiles, new NotificationService(renewalDb), authConfiguration)
                .Renew(renewalDirectory.GetEmployee(hr.Id), employmentContractId, renewalRequest);
        })).ToArray();
        var renewalResults = await Task.WhenAll(renewalTasks);
        var renewedContract = renewalResults.SingleOrDefault(result => result.IsSuccess);
        var rejectedRenewal = renewalResults.SingleOrDefault(result => !result.IsSuccess);
        if (renewedContract is null || rejectedRenewal?.Code is not ("CONCURRENCY_001" or "CONTRACT_002") || renewedContract.Value!.RenewalOfId != employmentContractId)
            throw new InvalidOperationException(renewedContract?.Error ?? rejectedRenewal?.Error ?? "并发续签未保持唯一版本链。 ");
        renewedEmploymentContractId = renewedContract.Value.Id;
    }
    await using (var renewalVerificationDb = new OaDbContext(options))
    {
        if (!await renewalVerificationDb.EmploymentContracts.AnyAsync(item => item.Id == employmentContractId && item.Status == EmploymentContractStatuses.Superseded) ||
            await renewalVerificationDb.EmploymentContracts.CountAsync(item => item.RenewalOfId == employmentContractId && item.Status == EmploymentContractStatuses.Active) != 1 ||
            await renewalVerificationDb.AuditLogs.CountAsync(item => item.Action == "EMPLOYMENT_CONTRACT_RENEWED" && item.ResourceId == renewedEmploymentContractId.ToString()) != 1)
            throw new InvalidOperationException("并发续签后的原合同状态、续签链或审计不唯一。 ");
    }
    var disabledDemoContracts = new EmploymentContractService(writeDb, persistedDirectory, fileService, notifications, new ConfigurationBuilder().Build());
    if (disabledDemoContracts.GenerateDemoData(hr).Code != "DEMO_DISABLED" || contractService.GenerateDemoData(employee).Code != "AUTH_002")
        throw new InvalidOperationException("模拟合同数据未按环境开关或权限执行失败关闭。 ");
    var generatedContracts = contractService.GenerateDemoData(hr);
    var repeatedContractGeneration = contractService.GenerateDemoData(hr);
    if (!generatedContracts.IsSuccess || generatedContracts.Value!.Created < 1 || !repeatedContractGeneration.IsSuccess || repeatedContractGeneration.Value!.Created != 0)
        throw new InvalidOperationException(generatedContracts.Error ?? repeatedContractGeneration.Error ?? "开发环境模拟合同数据未保留或重复生成不幂等。 ");
    await using var fileContent = new MemoryStream("%PDF-1.4\ndemo-pdf-content"u8.ToArray());
    var uploadedFile = await fileService.UploadAsync(employee, new FormFile(fileContent, 0, fileContent.Length, "file", "medical-proof.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" });
    if (!uploadedFile.IsSuccess) throw new InvalidOperationException(uploadedFile.Error);
    fileId = uploadedFile.Value!.Id;
    var fileCountAfterCleanUpload = writeDb.Files.Count();
    await using var disguisedContent = new MemoryStream("MZ executable renamed as pdf"u8.ToArray());
    var disguisedUpload = await fileService.UploadAsync(employee, new FormFile(disguisedContent, 0, disguisedContent.Length, "file", "disguised.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" });
    if (disguisedUpload.Code != "FILE_001" || writeDb.Files.Count() != fileCountAfterCleanUpload)
        throw new InvalidOperationException("文件内容与扩展名不匹配时仍可落库。");
    var infectedFileService = new FileService(writeDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build(), new TestFileMalwareScanner(FileScanStatus.Infected));
    await using var infectedContent = new MemoryStream("%PDF-1.4\nmalicious-test-content"u8.ToArray());
    var infectedUpload = await infectedFileService.UploadAsync(employee, new FormFile(infectedContent, 0, infectedContent.Length, "file", "infected.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" });
    if (infectedUpload.Code != "FILE_006" || writeDb.Files.Count() != fileCountAfterCleanUpload || Directory.EnumerateFiles(Path.Combine(fileRoot, "storage", "files"), "*.upload").Any())
        throw new InvalidOperationException("恶意附件未被阻断、隔离文件未清理或文件元数据被错误保存。");
    var unavailableFileService = new FileService(writeDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build(), new TestFileMalwareScanner(FileScanStatus.Unavailable));
    await using var unavailableContent = new MemoryStream("%PDF-1.4\nscanner-unavailable-content"u8.ToArray());
    var unavailableUpload = await unavailableFileService.UploadAsync(employee, new FormFile(unavailableContent, 0, unavailableContent.Length, "file", "unavailable.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" });
    if (unavailableUpload.Code != "FILE_007" || writeDb.Files.Count() != fileCountAfterCleanUpload || await unavailableFileService.IsScanningReadyAsync())
        throw new InvalidOperationException("扫描服务不可用时未执行失败关闭策略。");
    var manager = data.GetEmployee("u-li");
    var protectedLeave = new LeaveService(data, writeDb, calendar, notifications, fileService, processes).CreateDraft(manager, new CreateLeaveRequest(LeaveType.Personal, leaveDate, LeavePeriod.FullDay, leaveDate, LeavePeriod.FullDay, "越权附件验证", [fileId.ToString()]));
    if (protectedLeave.IsSuccess || protectedLeave.Code != "FILE_005")
        throw new InvalidOperationException("其他用户可以关联不属于自己的附件。");
    var blockedDate = new DateOnly(2031, 1, 6);
    if (!calendar.Update(hr, blockedDate, new UpdateWorkCalendarRequest(false, "企业团建休息日")).IsSuccess)
        throw new InvalidOperationException("HR 无法维护企业工作日历。");
    var leave = new LeaveService(data, writeDb, calendar, notifications, null, processes);
    var futureAnnualBalance = leave.GetBalance(employee, LeaveType.Annual, leaveDate.Year);
    if (futureAnnualBalance is not { StatutoryEntitled: 10m, Adjustment: 0m, Frozen: 0m, Used: 0m } || leave.GetBalance(employee, LeaveType.CompTime, leaveDate.Year).Entitled != 0m)
        throw new InvalidOperationException("年假未按精确累计工作起始日期和年度计算，或调休错误复用了年假额度。 ");
    if (leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, blockedDate, LeavePeriod.FullDay, blockedDate, LeavePeriod.FullDay, "日历覆盖验证")).IsSuccess)
        throw new InvalidOperationException("企业设置的非工作日仍允许创建有效请假单。");
    var draft = leave.CreateDraft(employee, new CreateLeaveRequest(
        LeaveType.Annual, leaveDate, LeavePeriod.FullDay,
        leaveDate, LeavePeriod.FullDay, "数据库持久化验证", [fileId.ToString()], [hr.Id]));
    if (!draft.IsSuccess) throw new InvalidOperationException(draft.Error);
    var submitted = leave.Submit(employee, draft.Value!.Id);
    if (!submitted.IsSuccess || submitted.Value!.Tasks.Count != 1) throw new InvalidOperationException(submitted.Error ?? "请假提交或审批任务创建失败。");
    var copyService = new FlowCopyService(data, writeDb);
    if (copyService.ListMine(hr).Any(item => item.BusinessId == submitted.Value.Id) || leave.Get(hr, submitted.Value.Id).IsSuccess)
        throw new InvalidOperationException("审批完成前抄送事项被提前暴露给抄送人。");
    var transferredLeave = leave.Transfer(data.GetEmployee("u-li"), submitted.Value.Tasks[0].Id, new TransferTaskRequest("u-wang", "部门负责人外出，转办总经理"));
    if (!transferredLeave.IsSuccess || transferredLeave.Value!.Tasks[0].AssigneeId != "u-wang") throw new InvalidOperationException("请假审批任务转办失败。");
    if (!leave.Approve(data.GetEmployee("u-wang"), submitted.Value.Tasks[0].Id, "已处理转办任务").IsSuccess) throw new InvalidOperationException("转办后的请假任务无法完成。");
    var usedAnnualBalance = leave.GetBalance(employee, LeaveType.Annual, leaveDate.Year);
    if (usedAnnualBalance is not { Frozen: 0m, Used: 1m } || submitted.Value.BalanceYear != leaveDate.Year)
        throw new InvalidOperationException("年假审批完成后未在申请年度释放冻结并扣减已用额度。 ");
    var adjustedAnnualBalance = leave.AdjustBalance(hr, employee.Id, new AdjustLeaveBalanceRequest(LeaveType.Annual, leaveDate.Year, 1m, "核验后增加年度公司福利假", usedAnnualBalance.Version));
    if (!adjustedAnnualBalance.IsSuccess || adjustedAnnualBalance.Value is not { StatutoryEntitled: 10m, Adjustment: 1m, Entitled: 11m } ||
        leave.AdjustBalance(hr, employee.Id, new AdjustLeaveBalanceRequest(LeaveType.Annual, leaveDate.Year, 2m, "验证过期版本不能覆盖", usedAnnualBalance.Version)).Code != "CONCURRENCY_001")
        throw new InvalidOperationException(adjustedAnnualBalance.Error ?? "HR 假期余额调整、法定基线保留或乐观锁失败。 ");

    var concurrentDateA = leaveDate.AddDays(5);
    while (!calendar.IsWorkingDay(concurrentDateA)) concurrentDateA = concurrentDateA.AddDays(1);
    var concurrentDateB = concurrentDateA.AddDays(1);
    while (!calendar.IsWorkingDay(concurrentDateB)) concurrentDateB = concurrentDateB.AddDays(1);
    await using (var balanceDbA = new OaDbContext(options))
    await using (var balanceDbB = new OaDbContext(options))
    {
        var concurrentLeaveA = new LeaveService(data, balanceDbA, new WorkCalendarService(balanceDbA), new NotificationService(balanceDbA), null, new ProcessDefinitionService(balanceDbA, data));
        var concurrentLeaveB = new LeaveService(data, balanceDbB, new WorkCalendarService(balanceDbB), new NotificationService(balanceDbB), null, new ProcessDefinitionService(balanceDbB, data));
        var concurrentDraftA = concurrentLeaveA.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Annual, concurrentDateA, LeavePeriod.FullDay, concurrentDateA, LeavePeriod.FullDay, "年度余额并发事务验证 A"));
        var concurrentDraftB = concurrentLeaveB.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Annual, concurrentDateB, LeavePeriod.FullDay, concurrentDateB, LeavePeriod.FullDay, "年度余额并发事务验证 B"));
        if (!concurrentDraftA.IsSuccess || !concurrentDraftB.IsSuccess) throw new InvalidOperationException("无法创建年度余额并发事务验证草稿。 ");

        var staleBalanceA = concurrentLeaveA.GetBalance(employee, LeaveType.Annual, leaveDate.Year);
        var staleBalanceB = concurrentLeaveB.GetBalance(employee, LeaveType.Annual, leaveDate.Year);
        if (staleBalanceA.Version != staleBalanceB.Version) throw new InvalidOperationException("并发验证未读取到同一余额版本。 ");
        var concurrentSubmittedA = concurrentLeaveA.Submit(employee, concurrentDraftA.Value!.Id);
        var concurrentSubmittedB = concurrentLeaveB.Submit(employee, concurrentDraftB.Value!.Id);
        if (!concurrentSubmittedA.IsSuccess || concurrentSubmittedB.Code != "CONCURRENCY_001")
            throw new InvalidOperationException("陈旧年度余额提交未被数据库乐观锁拒绝。 ");

        await using (var verificationDb = new OaDbContext(options))
        {
            var rolledBackRequest = await verificationDb.LeaveRequests.AsNoTracking().SingleAsync(item => item.Id == concurrentDraftB.Value.Id);
            var balanceAfterConflict = await verificationDb.LeaveBalances.AsNoTracking().SingleAsync(item => item.UserId == employee.Id && item.LeaveType == (int)LeaveType.Annual && item.Year == leaveDate.Year);
            if (rolledBackRequest.Status != (int)LeaveStatus.Draft || rolledBackRequest.BalanceYear is not null ||
                await verificationDb.FlowTasks.AnyAsync(item => item.LeaveRequestId == concurrentDraftB.Value.Id) || balanceAfterConflict.Frozen != 1m)
                throw new InvalidOperationException("余额并发冲突后请假状态、审批任务或冻结额度未整体回滚。 ");
        }

        if (!concurrentLeaveA.Withdraw(employee, concurrentSubmittedA.Value!.Id).IsSuccess)
            throw new InvalidOperationException("并发事务验证单无法撤回并释放冻结额度。 ");
    }
    await using (var postConcurrencyDb = new OaDbContext(options))
    {
        var balanceAfterWithdrawal = await postConcurrencyDb.LeaveBalances.AsNoTracking().SingleAsync(item => item.UserId == employee.Id && item.LeaveType == (int)LeaveType.Annual && item.Year == leaveDate.Year);
        if (balanceAfterWithdrawal.Frozen != 0m || !await postConcurrencyDb.AuditLogs.AnyAsync(item => item.Action == "LEAVE_WITHDRAWN" && item.ResourceType == "LeaveRequest"))
            throw new InvalidOperationException("并发验证单撤回后余额或审计未持久化。 ");
    }

    var concurrentApprovalDate = concurrentDateB.AddDays(1);
    while (!calendar.IsWorkingDay(concurrentApprovalDate)) concurrentApprovalDate = concurrentApprovalDate.AddDays(1);
    Guid concurrentApprovalRequestId;
    Guid concurrentApprovalTaskId;
    await using (var approvalSetupDb = new OaDbContext(options))
    {
        var approvalSetup = new LeaveService(data, approvalSetupDb, new WorkCalendarService(approvalSetupDb), new NotificationService(approvalSetupDb), null, new ProcessDefinitionService(approvalSetupDb, data));
        var approvalDraft = approvalSetup.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, concurrentApprovalDate, LeavePeriod.FullDay, concurrentApprovalDate, LeavePeriod.FullDay, "请假单并发审批事务验证"));
        var approvalSubmitted = approvalDraft.IsSuccess ? approvalSetup.Submit(employee, approvalDraft.Value!.Id) : ServiceResult<LeaveRequest>.Failure(approvalDraft.Error!);
        if (!approvalSubmitted.IsSuccess) throw new InvalidOperationException(approvalSubmitted.Error ?? "无法创建请假单并发审批验证数据。 ");
        concurrentApprovalRequestId = approvalSubmitted.Value!.Id;
        concurrentApprovalTaskId = approvalSubmitted.Value.Tasks.Single().Id;
    }
    await using (var approvalDbA = new OaDbContext(options))
    await using (var approvalDbB = new OaDbContext(options))
    {
        var approvalA = new LeaveService(data, approvalDbA, new WorkCalendarService(approvalDbA), new NotificationService(approvalDbA), null, new ProcessDefinitionService(approvalDbA, data));
        var approvalB = new LeaveService(data, approvalDbB, new WorkCalendarService(approvalDbB), new NotificationService(approvalDbB), null, new ProcessDefinitionService(approvalDbB, data));
        var approvedA = approvalA.Approve(data.GetEmployee("u-li"), concurrentApprovalTaskId, "首个审批提交成功");
        var approvedB = approvalB.Approve(data.GetEmployee("u-li"), concurrentApprovalTaskId, "陈旧审批不应覆盖");
        if (!approvedA.IsSuccess || approvedB.Code != "CONCURRENCY_001")
            throw new InvalidOperationException("请假单陈旧审批未被数据库版本令牌拒绝。 ");
    }
    await using (var approvalVerificationDb = new OaDbContext(options))
    {
        var approvedRecord = await approvalVerificationDb.LeaveRequests.AsNoTracking().SingleAsync(item => item.Id == concurrentApprovalRequestId);
        if (approvedRecord.Status != (int)LeaveStatus.Completed ||
            await approvalVerificationDb.AuditLogs.CountAsync(item => item.ResourceType == "LeaveRequest" && item.ResourceId == concurrentApprovalRequestId.ToString() && item.Action == "LEAVE_APPROVED") != 1 ||
            await approvalVerificationDb.FlowActions.CountAsync(action => action.Action == (int)FlowActionType.Approved && approvalVerificationDb.FlowInstances.Any(instance => instance.Id == action.FlowInstanceId && instance.BusinessId == concurrentApprovalRequestId)) != 1)
            throw new InvalidOperationException("请假单并发审批后状态、审计或流程动作出现重复写入。 ");
    }

    var notificationFailureDate = concurrentApprovalDate.AddDays(1);
    while (!calendar.IsWorkingDay(notificationFailureDate)) notificationFailureDate = notificationFailureDate.AddDays(1);
    var notificationFailureDraft = leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, notificationFailureDate, LeavePeriod.FullDay, notificationFailureDate, LeavePeriod.FullDay, "通知失败事务回滚验证"));
    if (!notificationFailureDraft.IsSuccess) throw new InvalidOperationException(notificationFailureDraft.Error ?? "无法创建通知失败事务回滚草稿。 ");
    await InstallNotificationFailureTriggerAsync(connectionString, notificationFailureDraft.Value!.Id);
    try
    {
        try
        {
            leave.Submit(employee, notificationFailureDraft.Value.Id);
            throw new InvalidOperationException("数据库拒绝通知写入时请假提交仍返回成功。 ");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "P0001" }) { }
    }
    finally
    {
        await RemoveNotificationFailureTriggerAsync(connectionString);
    }
    await using (var notificationRollbackDb = new OaDbContext(options))
    {
        var rolledBack = await notificationRollbackDb.LeaveRequests.AsNoTracking().SingleAsync(item => item.Id == notificationFailureDraft.Value.Id);
        if (rolledBack.Status != (int)LeaveStatus.Draft || rolledBack.BalanceYear is not null ||
            await notificationRollbackDb.FlowTasks.AnyAsync(item => item.LeaveRequestId == rolledBack.Id) ||
            await notificationRollbackDb.AuditLogs.AnyAsync(item => item.ResourceType == "LeaveRequest" && item.ResourceId == rolledBack.Id.ToString() && item.Action == "LEAVE_SUBMITTED") ||
            await notificationRollbackDb.Notifications.AnyAsync(item => item.ResourceType == "LeaveRequest" && item.ResourceId == rolledBack.Id.ToString()))
            throw new InvalidOperationException("站内通知写入失败后请假状态、任务、审计或通知未整体回滚。 ");
    }
    var leaveCopy = copyService.ListMine(hr).SingleOrDefault(item => item.BusinessId == submitted.Value.Id);
    if (leaveCopy is null || !leave.Get(hr, submitted.Value.Id).IsSuccess || leaveCopy.ReadAt is not null)
        throw new InvalidOperationException("请假完成后未激活抄送事项、只读权限或未读状态。");
    if (copyService.MarkRead(employee, leaveCopy.Id).Code != "DATA_001" || !copyService.MarkRead(hr, leaveCopy.Id).IsSuccess)
        throw new InvalidOperationException("抄送事项已阅权限或正常已阅失败。");
    leaveCopyId = leaveCopy.Id;
    requestId = submitted.Value.Id;

    var publishedLeave = definitions.Value.Single(item => item.BusinessType == "Leave");
    var immutableUpdate = processes.Update(administrator, publishedLeave.Id, new UpdateProcessDefinitionRequest(publishedLeave.Name, publishedLeave.Routes.Select(route => new ProcessRouteInput(route.MaxValue, route.ApproverKeys)).ToList()));
    if (immutableUpdate.Code != "STATE_001")
        throw new InvalidOperationException("已发布流程版本仍可直接修改。");
    var cloned = processes.Clone(administrator, publishedLeave.Id);
    if (!cloned.IsSuccess || cloned.Value!.Version != 2 || cloned.Value.Status != ProcessDefinitionStatus.Draft)
        throw new InvalidOperationException("流程定义无法复制为递增的草稿版本。");
    var updatedProcess = processes.Update(administrator, cloned.Value.Id, new UpdateProcessDefinitionRequest("默认请假审批（严格版）",
    [
        new ProcessRouteInput(.5m, ["DIRECT_MANAGER"], [new ProcessNodePolicyInput(8, 2, 3, "DIRECT_MANAGER", "BLOCK", false)]),
        new ProcessRouteInput(null, ["DIRECT_MANAGER", "ROLE:总经理"],
        [
            new ProcessNodePolicyInput(8, 2, 3, "DIRECT_MANAGER", "BLOCK", false),
            new ProcessNodePolicyInput(12, 3, 6, "PROCESS_ADMIN", "BLOCK", true)
        ])
    ]));
    var simulation = updatedProcess.IsSuccess ? processes.Simulate(administrator, cloned.Value.Id, new SimulateProcessRequest(employee.Id, 1m, "Sick")) : ServiceResult<ProcessSimulationView>.Failure(updatedProcess.Error!);
    if (!simulation.IsSuccess || simulation.Value!.Approvers.Count != 2 || simulation.Value.Approvers[0].Assignee.Id != "u-li" || simulation.Value.Approvers[0].Policy?.HandlingHours != 8 ||
        processes.Simulate(employee, cloned.Value.Id, new SimulateProcessRequest(employee.Id, 1m, "Sick")).Code != "AUTH_002")
        throw new InvalidOperationException(simulation.Error ?? "发布前流程试算、节点 SLA 或试算权限错误。");
    if (!processes.Publish(administrator, cloned.Value.Id).IsSuccess)
        throw new InvalidOperationException("流程草稿无法保存或发布。");
    var versionedDate = leaveDate.AddDays(14);
    while (!calendar.IsWorkingDay(versionedDate)) versionedDate = versionedDate.AddDays(1);
    var versionedDraft = leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Sick, versionedDate, LeavePeriod.FullDay, versionedDate, LeavePeriod.FullDay, "流程版本切换验证"));
    if (!versionedDraft.IsSuccess) throw new InvalidOperationException(versionedDraft.Error);
    var versionedSubmission = leave.Submit(employee, versionedDraft.Value!.Id);
    if (!versionedSubmission.IsSuccess || versionedSubmission.Value!.ProcessDefinitionVersion != 2 || versionedSubmission.Value.Tasks.Count != 2)
        throw new InvalidOperationException("新提交请假单未使用最新发布流程版本。");
    var submittedSlas = writeDb.FlowTaskSlas.Where(item => item.FlowInstanceId == versionedSubmission.Value.CurrentFlowInstanceId).OrderBy(item => item.Sequence).ToList();
    if (submittedSlas.Count != 2 || submittedSlas[0].ActivatedAt is null || submittedSlas[0].DueAt is null || submittedSlas[1].ActivatedAt is not null || submittedSlas[0].HandlingHours != 8 || submittedSlas[1].HandlingHours != 12)
        throw new InvalidOperationException($"流程节点 SLA 快照、首节点起算或后续节点延迟激活错误：count={submittedSlas.Count}, nodes={string.Join(';', submittedSlas.Select(item => $"{item.Sequence}/{item.HandlingHours}/{item.ActivatedAt}/{item.DueAt}"))}");
    var slaService = new FlowSlaService(writeDb, data);
    var firstDueAt = submittedSlas[0].DueAt!.Value;
    if (slaService.DispatchAlerts(firstDueAt.AddHours(-1)) != 1 || slaService.DispatchAlerts(firstDueAt.AddHours(-1)) != 0 ||
        slaService.DispatchAlerts(firstDueAt.AddHours(1)) != 1 || slaService.DispatchAlerts(firstDueAt.AddHours(1)) != 0 ||
        slaService.DispatchAlerts(firstDueAt.AddHours(4)) != 1 || slaService.DispatchAlerts(firstDueAt.AddHours(4)) != 0)
        throw new InvalidOperationException("流程即将到期、逾期、升级提醒或扫描去重错误。");
    if (!writeDb.Notifications.Any(item => item.ResourceId == versionedSubmission.Value.Id.ToString() && item.Type == "FLOW_SLA_DUE_SOON" && item.RecipientId == "u-li") ||
        !writeDb.Notifications.Any(item => item.ResourceId == versionedSubmission.Value.Id.ToString() && item.Type == "FLOW_SLA_OVERDUE" && item.RecipientId == "u-li") ||
        !writeDb.Notifications.Any(item => item.ResourceId == versionedSubmission.Value.Id.ToString() && item.Type == "FLOW_SLA_ESCALATED" && item.RecipientId == "u-wang"))
        throw new InvalidOperationException("流程 SLA 提醒或升级接收人不正确。");
    var managerAccount = identity.List(administrator, "u-li", null, null, 1, 20).Value!.Items.Single(item => item.Id == "u-li");
    var disableManager = identity.Update(administrator, managerAccount.Id, new UpdateManagedUserRequest(managerAccount.Name, managerAccount.DepartmentId, managerAccount.ManagerId,
        managerAccount.CumulativeWorkYears, "DISABLED", managerAccount.Roles, managerAccount.Version, managerAccount.PositionId));
    if (disableManager.Code != "CONFLICT_001")
        throw new InvalidOperationException($"审批人存在活动待办时仍可停用，会生成无人可办的僵尸任务：code={disableManager.Code}, error={disableManager.Error}");
    if (leave.GetPendingTasks(data.GetEmployee("u-wang")).Any(task => task.LeaveRequestId == versionedSubmission.Value.Id))
        throw new InvalidOperationException("未来审批节点在前序节点处理前错误进入待办。");
    if (!leave.Reject(data.GetEmployee("u-li"), versionedSubmission.Value.Tasks[0].Id, "请补充工作交接").IsSuccess || versionedSubmission.Value.Tasks[1].Status != FlowTaskStatus.Cancelled)
        throw new InvalidOperationException("驳回未终止流程实例或未取消后续待办。");
    if (writeDb.FlowTaskSlas.Single(item => item.TaskId == versionedSubmission.Value.Tasks[0].Id).CompletedAt is null || writeDb.FlowTaskSlas.Single(item => item.TaskId == versionedSubmission.Value.Tasks[1].Id).CancelledAt is null)
        throw new InvalidOperationException("驳回后 SLA 任务未停止计时或未取消后续节点。");
    var revisedVersioned = leave.Update(employee, versionedSubmission.Value.Id, versionedSubmission.Value.Version, new CreateLeaveRequest(LeaveType.Sick, versionedDate, LeavePeriod.FullDay, versionedDate, LeavePeriod.FullDay, "已补充工作交接"));
    var resubmittedVersioned = revisedVersioned.IsSuccess ? leave.Submit(employee, revisedVersioned.Value!.Id) : ServiceResult<LeaveRequest>.Failure(revisedVersioned.Error!);
    if (!resubmittedVersioned.IsSuccess || resubmittedVersioned.Value!.FlowInstances.Count != 2 || resubmittedVersioned.Value.FlowInstances[0].Status != FlowInstanceStatus.Rejected || resubmittedVersioned.Value.FlowInstances[1].Attempt != 2)
        throw new InvalidOperationException("驳回重提未创建递增的新流程实例或旧轨迹丢失。");
    var resubmittedSlas = writeDb.FlowTaskSlas.Where(item => item.FlowInstanceId == resubmittedVersioned.Value.CurrentFlowInstanceId).OrderBy(item => item.Sequence).ToList();
    if (resubmittedSlas.Count != 2 || !leave.Approve(data.GetEmployee("u-li"), resubmittedVersioned.Value.Tasks[0].Id, "SLA 节点激活验证").IsSuccess ||
        writeDb.FlowTaskSlas.Single(item => item.Id == resubmittedSlas[0].Id).CompletedAt is null || writeDb.FlowTaskSlas.Single(item => item.Id == resubmittedSlas[1].Id).ActivatedAt is null || writeDb.FlowTaskSlas.Single(item => item.Id == resubmittedSlas[1].Id).DueAt is null)
        throw new InvalidOperationException("审批通过后当前 SLA 未结束，或下一节点未独立起算。");
    if (submitted.Value.ProcessDefinitionVersion != 1 || submitted.Value.Tasks.Count != 1)
        throw new InvalidOperationException("发布新流程版本影响了既有请假单的版本或任务。");
    versionedRequestId = resubmittedVersioned.Value.Id;

    var invalidExpenseScope = processes.Create(administrator, new CreateProcessDefinitionRequest(
        "EXPENSE_INVALID_SCOPE", "无效报销假别范围", "Expense",
        [new ProcessRouteInput(null, ["ROLE:财务专员"])], 10, [], ["Sick"]));
    if (invalidExpenseScope.IsSuccess || invalidExpenseScope.Code != "VALIDATION_001")
        throw new InvalidOperationException("报销流程错误接受了请假类型适用条件。");

    var companySickProcess = processes.Create(administrator, new CreateProcessDefinitionRequest(
        "LEAVE_COMPANY_SICK", "公司病假审批", "Leave",
        [new ProcessRouteInput(null, ["ROLE:总经理"])], 15, ["general"], ["Sick"]));
    if (!companySickProcess.IsSuccess || !processes.Publish(administrator, companySickProcess.Value!.Id).IsSuccess)
        throw new InvalidOperationException("公司级病假流程无法创建或发布。");

    var engineeringSickProcess = processes.Create(administrator, new CreateProcessDefinitionRequest(
        "LEAVE_ENGINEERING_SICK", "研发病假审批", "Leave",
        [new ProcessRouteInput(null, ["ROLE:总经理"])], 20, ["engineering"], ["Sick"]));
    if (!engineeringSickProcess.IsSuccess || !processes.Publish(administrator, engineeringSickProcess.Value!.Id).IsSuccess)
        throw new InvalidOperationException("研发病假流程无法创建或发布。");
    scopedProcessId = engineeringSickProcess.Value.Id;

    var companyResolved = processes.Resolve("Leave", data.GetEmployee("u-chen"), 1m, "Sick");
    if (!companyResolved.IsSuccess || companyResolved.Value!.Code != "LEAVE_COMPANY_SICK")
        throw new InvalidOperationException("公司级部门范围未包含财务部下级组织，或病假条件未生效。");

    var scopedDate = leaveDate.AddDays(42);
    while (!calendar.IsWorkingDay(scopedDate)) scopedDate = scopedDate.AddDays(1);
    var scopedDraft = leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Sick, scopedDate, LeavePeriod.FullDay, scopedDate, LeavePeriod.FullDay, "研发病假流程范围验证"));
    var scopedSubmission = scopedDraft.IsSuccess ? leave.Submit(employee, scopedDraft.Value!.Id) : ServiceResult<LeaveRequest>.Failure(scopedDraft.Error!);
    if (!scopedSubmission.IsSuccess || scopedSubmission.Value!.ProcessDefinitionCode != "LEAVE_ENGINEERING_SICK" || scopedSubmission.Value.ProcessDefinitionVersion != 1 || scopedSubmission.Value.Tasks.SingleOrDefault()?.AssigneeId != "u-wang")
        throw new InvalidOperationException("研发病假未选择更高优先级的部门/假别流程或任务快照错误。");
    scopedRequestId = scopedSubmission.Value.Id;

    var conflictingProcess = processes.Create(administrator, new CreateProcessDefinitionRequest(
        "LEAVE_ENGINEERING_SICK_CONFLICT", "研发病假冲突流程", "Leave",
        [new ProcessRouteInput(null, ["DIRECT_MANAGER"])], 20, ["engineering"], ["Sick"]));
    if (!conflictingProcess.IsSuccess || processes.Publish(administrator, conflictingProcess.Value!.Id).Code != "FLOW_003")
        throw new InvalidOperationException("同优先级、同具体度且范围重叠的流程未在发布时拒绝。");
    if (versionedSubmission.Value.ProcessDefinitionCode != "LEAVE_DEFAULT" || versionedSubmission.Value.ProcessDefinitionVersion != 2)
        throw new InvalidOperationException("发布适用范围流程影响了既有单据的流程版本快照。");

    var delegationService = new DelegationService(writeDb, data);
    var delegation = delegationService.Create(manager, new CreateFlowDelegationRequest("u-wang", "Leave", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2), "外出期间委托王总代办请假审批"));
    if (!delegation.IsSuccess || !delegation.Value!.IsEffective || delegationService.ListMine(manager).Count != 1)
        throw new InvalidOperationException("有效审批委托无法创建或查询。");
    if (delegationService.Create(manager, new CreateFlowDelegationRequest("u-chen", "All", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), "重叠委托验证")).Code != "FLOW_004")
        throw new InvalidOperationException("系统允许创建业务范围与时间重叠的审批委托。");
    var delegatedDate = leaveDate.AddDays(28);
    while (!calendar.IsWorkingDay(delegatedDate)) delegatedDate = delegatedDate.AddDays(1);
    var delegatedDraft = leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, delegatedDate, LeavePeriod.FullDay, delegatedDate, LeavePeriod.FullDay, "审批委托生效验证"));
    if (!delegatedDraft.IsSuccess) throw new InvalidOperationException(delegatedDraft.Error);
    var delegatedSubmission = leave.Submit(employee, delegatedDraft.Value!.Id);
    if (!delegatedSubmission.IsSuccess || delegatedSubmission.Value!.Tasks.Count != 1 || delegatedSubmission.Value.Tasks[0].AssigneeId != "u-wang" || delegatedSubmission.Value.Tasks[0].OriginalAssigneeId != "u-li" || delegatedSubmission.Value.Tasks[0].DelegationId != delegation.Value.Id)
        throw new InvalidOperationException("有效委托未替换实际审批人或未保留原审批人快照。");
    if (delegationService.Cancel(employee, delegation.Value.Id).Code != "DATA_001" || !delegationService.Cancel(manager, delegation.Value.Id).IsSuccess)
        throw new InvalidOperationException("审批委托取消权限或正常取消失败。");
    delegatedRequestId = delegatedSubmission.Value.Id;
    var postCancellationDate = leaveDate.AddDays(35);
    while (!calendar.IsWorkingDay(postCancellationDate)) postCancellationDate = postCancellationDate.AddDays(1);
    var postCancellationDraft = leave.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, postCancellationDate, LeavePeriod.FullDay, postCancellationDate, LeavePeriod.FullDay, "审批委托取消验证"));
    if (!postCancellationDraft.IsSuccess) throw new InvalidOperationException(postCancellationDraft.Error);
    var postCancellationSubmission = leave.Submit(employee, postCancellationDraft.Value!.Id);
    if (!postCancellationSubmission.IsSuccess || postCancellationSubmission.Value!.Tasks.Count != 2 || postCancellationSubmission.Value.Tasks[0].AssigneeId != "u-li" || postCancellationSubmission.Value.Tasks[0].DelegationId is not null)
        throw new InvalidOperationException("取消委托后新任务仍错误分配给代办人。");
    postCancellationRequestId = postCancellationSubmission.Value.Id;

    var expense = new ExpenseService(data, writeDb, notifications, null, processes);
    var claim = expense.CreateDraft(employee, new CreateExpenseClaim(
        null, "张晨", "6222026000001234", "模拟银行", "持久化报销验证",
        [new ExpenseItem(new DateOnly(2026, 8, 20), "交通", 120m, "客户拜访", receiptNumber, ["receipt.pdf"])], [hr.Id]));
    if (!claim.IsSuccess) throw new InvalidOperationException(claim.Error);
    var expenseSubmitted = expense.Submit(employee, claim.Value!.Id);
    if (!expenseSubmitted.IsSuccess || expenseSubmitted.Value!.Tasks.Count != 2) throw new InvalidOperationException(expenseSubmitted.Error ?? "报销提交或审批任务创建失败。");
    var transferredExpense = expense.Transfer(data.GetEmployee("u-li"), expenseSubmitted.Value.Tasks[0].Id, new TransferTaskRequest("u-wang", "部门负责人外出，转办总经理"));
    if (!transferredExpense.IsSuccess || transferredExpense.Value!.Tasks[0].AssigneeId != "u-wang") throw new InvalidOperationException("报销审批任务转办失败。");
    if (!expense.Approve(data.GetEmployee("u-wang"), expenseSubmitted.Value.Tasks[0].Id, "已处理转办任务").IsSuccess) throw new InvalidOperationException("转办后的报销任务无法完成。");
    if (copyService.ListMine(hr).Any(item => item.BusinessId == expenseSubmitted.Value.Id))
        throw new InvalidOperationException("报销前序节点通过后抄送事项被提前激活。");
    if (!expense.Approve(data.GetEmployee("u-chen"), expenseSubmitted.Value.Tasks[1].Id, "财务审批通过").IsSuccess)
        throw new InvalidOperationException("报销最终节点无法完成。");
    var expenseCopy = copyService.ListMine(hr).SingleOrDefault(item => item.BusinessId == expenseSubmitted.Value.Id);
    if (expenseCopy is null || !expense.Get(hr, expenseSubmitted.Value.Id).IsSuccess || expenseCopy.ReadAt is not null)
        throw new InvalidOperationException("报销审批完成后未激活抄送事项或只读权限。");
    expenseCopyId = expenseCopy.Id;
    expenseId = expenseSubmitted.Value.Id;

    var finance = data.GetEmployee("u-chen");
    var securedExpense = new ExpenseService(data, writeDb, notifications, fileService, processes);
    var invalidProof = securedExpense.RegisterPayment(finance, expenseId, new RegisterPaymentRequest(DateOnly.FromDateTime(DateTime.Today), "企业网银", "TX-PG-WRONG", expenseSubmitted.Value.TotalAmount, fileId.ToString()));
    if (invalidProof.Code != "FILE_005")
        throw new InvalidOperationException("付款人可以冒用其他员工上传的附件作为付款凭证。");
    await using var paymentContent = new MemoryStream("%PDF-1.4\npayment-proof-content"u8.ToArray());
    var paymentFile = await fileService.UploadAsync(finance, new FormFile(paymentContent, 0, paymentContent.Length, "file", "payment-proof.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" });
    if (!paymentFile.IsSuccess) throw new InvalidOperationException(paymentFile.Error);
    paymentProofId = paymentFile.Value!.Id;
    var payment = securedExpense.RegisterPayment(finance, expenseId, new RegisterPaymentRequest(DateOnly.FromDateTime(DateTime.Today), "企业网银", "TX-PG-VALID", expenseSubmitted.Value.TotalAmount, paymentProofId.ToString()));
    if (!payment.IsSuccess || payment.Value!.Status != ExpenseStatus.Completed || payment.Value.Payment?.ProofFile != paymentProofId.ToString())
        throw new InvalidOperationException(payment.Error ?? "真实付款凭证无法关联报销付款记录。");

    var travel = new TravelService(data, writeDb, notifications, fileService, processes);
    var travelStart = DateOnly.FromDateTime(DateTime.Today).AddDays(-10);
    var travelDraft = travel.CreateDraft(employee, new SaveTravelRequest(
        "客户现场需求确认和方案汇报", 5600m,
        [new TravelItineraryItem("上海", travelStart, travelStart.AddDays(3), "高铁", "客户访谈、方案评审与项目计划确认")],
        ["u-sun"], [fileId.ToString()], ["u-chen"]));
    if (!travelDraft.IsSuccess || travelDraft.Value!.Days != 4 || travelDraft.Value.CompanionNames.SingleOrDefault() != "孙悦")
        throw new InvalidOperationException(travelDraft.Error ?? "出差草稿、日历天数或同行人快照创建失败。");
    var travelSubmitted = travel.Submit(employee, travelDraft.Value.Id);
    if (!travelSubmitted.IsSuccess || travelSubmitted.Value!.Tasks.Count != 2 || travelSubmitted.Value.ProcessDefinitionCode != "TRAVEL_DEFAULT")
        throw new InvalidOperationException(travelSubmitted.Error ?? "超过 3 天出差未按直属上级和总经理两级审批路由。");
    if (!travel.Approve(data.GetEmployee("u-li"), travelSubmitted.Value.Tasks[0].Id, "行程安排合理").IsSuccess ||
        !travel.Approve(data.GetEmployee("u-wang"), travelSubmitted.Value.Tasks[1].Id, "批准出差").IsSuccess || travelSubmitted.Value.Status != TravelStatus.Approved)
        throw new InvalidOperationException("出差两级审批无法完成。");
    var travelCopy = copyService.ListMine(data.GetEmployee("u-chen")).SingleOrDefault(item => item.BusinessType == "Travel" && item.BusinessId == travelSubmitted.Value.Id);
    if (travelCopy is null || !travel.Get(data.GetEmployee("u-chen"), travelSubmitted.Value.Id).IsSuccess)
        throw new InvalidOperationException("出差审批完成后未激活抄送事项或抄送只读权限。");
    travelId = travelSubmitted.Value.Id;

    var shortStart = travelStart.AddDays(20);
    var shortDraft = travel.CreateDraft(employee, new SaveTravelRequest("短期项目启动会", 800m, [new TravelItineraryItem("苏州", shortStart, shortStart, "高铁", "参加项目启动会")]));
    var shortSubmitted = shortDraft.IsSuccess ? travel.Submit(employee, shortDraft.Value!.Id) : ServiceResult<TravelRequest>.Failure(shortDraft.Error!);
    if (!shortSubmitted.IsSuccess || shortSubmitted.Value!.Tasks.Count != 1 || shortSubmitted.Value.Tasks[0].AssigneeId != "u-li" || !travel.Withdraw(employee, shortSubmitted.Value.Id).IsSuccess || shortSubmitted.Value.Status != TravelStatus.Withdrawn)
        throw new InvalidOperationException("3 天内出差路由或未审批撤回失败。");
    withdrawnTravelId = shortSubmitted.Value.Id;

    var overlapDraft = travel.CreateDraft(employee, new SaveTravelRequest("重复时间验证", 100m, [new TravelItineraryItem("上海", travelStart.AddDays(1), travelStart.AddDays(2), "高铁", "验证重复出差")]))!;
    if (!overlapDraft.IsSuccess || travel.Submit(employee, overlapDraft.Value!.Id).Code != "TRAVEL_002")
        throw new InvalidOperationException("系统允许提交与已批准出差日期重叠的申请。");

    var linkedExpenseService = new ExpenseService(data, writeDb, notifications, fileService, processes, null, copyService, travel);
    var linkedExpense = linkedExpenseService.CreateDraft(employee, new CreateExpenseClaim(
        null, "张晨", "6222026000001234", "模拟银行", "上海出差交通费用",
        [new ExpenseItem(travelStart, "交通", 680m, "往返高铁票", $"TRAVEL-{Guid.NewGuid():N}", [fileId.ToString()])], null, travelId));
    if (!linkedExpense.IsSuccess || linkedExpense.Value!.TravelRequestId != travelId || linkedExpense.Value.TravelRequestNumber != travelSubmitted.Value.Number)
        throw new InvalidOperationException(linkedExpense.Error ?? "已批准出差无法关联报销草稿。");
    linkedExpenseId = linkedExpense.Value.Id;
    var invalidTravelLink = linkedExpenseService.CreateDraft(employee, new CreateExpenseClaim(
        null, "张晨", "6222026000001234", "模拟银行", "无效出差关联",
        [new ExpenseItem(travelStart, "交通", 10m, "关联撤回出差", null, [fileId.ToString()])], null, withdrawnTravelId));
    if (invalidTravelLink.Code != "TRAVEL_004") throw new InvalidOperationException("报销可以关联未批准或已撤回的出差申请。");

    var purchaseService = new PurchaseService(data, writeDb, notifications, fileService, processes, null, copyService);
    var purchaseRequiredDate = DateOnly.FromDateTime(DateTime.Today).AddDays(15);
    var invalidItemsDraft = purchaseService.CreateDraft(employee, new SavePurchaseRequest(
        "明细为空测试", "用途", purchaseRequiredDate, "供应商", []));
    if (invalidItemsDraft.Code != "PURCHASE_001") throw new InvalidOperationException("空明细采购申请未被拒绝。");

    var purchaseDraft = purchaseService.CreateDraft(employee, new SavePurchaseRequest(
        "研发部开发终端及外设采购", "为新入职工程师配备开发电脑及外设", purchaseRequiredDate, "戴尔官方旗舰店",
        [
            new SavePurchaseItem("IT设备", "移动工作站", "32G/1T SSD", 2, "台", 8500m, "主力开发机"),
            new SavePurchaseItem("IT设备", "4K显示器", "27寸 Type-C 90W", 2, "台", 2200m, "双屏外设")
        ],
        [fileId.ToString()], ["u-chen"]));
    if (!purchaseDraft.IsSuccess || purchaseDraft.Value!.EstimatedTotal != 21400m || purchaseDraft.Value.Items.Count != 2)
        throw new InvalidOperationException(purchaseDraft.Error ?? "采购草稿创建或总额计算失败。");
    purchaseId = purchaseDraft.Value.Id;

    var highAmountDraft = purchaseService.CreateDraft(employee, new SavePurchaseRequest(
        "服务器采购", "生产集群扩容", purchaseRequiredDate, "华为供应商",
        [new SavePurchaseItem("IT设备", "机架式服务器", "2U 64核", 1, "台", 60000m, "集群扩容")],
        [fileId.ToString()]));
    if (!highAmountDraft.IsSuccess || purchaseService.Submit(employee, highAmountDraft.Value!.Id).Code != "PURCHASE_002")
        throw new InvalidOperationException("5万元及以上采购在不足两份附件时被允许提交。");

    var purchaseSubmitted = purchaseService.Submit(employee, purchaseId);
    if (!purchaseSubmitted.IsSuccess || purchaseSubmitted.Value!.Tasks.Count != 2 || purchaseSubmitted.Value.ProcessDefinitionCode != "PURCHASE_DEFAULT")
        throw new InvalidOperationException(purchaseSubmitted.Error ?? "采购提交审批路由失败，预期生成直属上级与财务经理待办。");
    if (purchaseSubmitted.Value.Tasks[0].AssigneeId != "u-li" || purchaseSubmitted.Value.Tasks[1].AssigneeId != "u-lin")
        throw new InvalidOperationException("采购两级审批人解析不符合预期。");

    var firstTask = purchaseSubmitted.Value.Tasks[0];
    if (!purchaseService.Approve(data.GetEmployee("u-li"), firstTask.Id, "同意采购配置").IsSuccess)
        throw new InvalidOperationException("直属上级审批采购失败。");

    if (purchaseService.Withdraw(employee, purchaseId).IsSuccess)
        throw new InvalidOperationException("已有处理记录的采购申请允许被撤回。");

    var secondTask = purchaseService.Get(employee, purchaseId).Value!.Tasks[1];
    if (!purchaseService.Approve(data.GetEmployee("u-lin"), secondTask.Id, "预算合理，同意采购").IsSuccess)
        throw new InvalidOperationException("财务经理审批采购失败。");
    var approvedPurchase = purchaseService.Get(employee, purchaseId).Value!;
    if (approvedPurchase.Status != PurchaseStatus.Approved)
        throw new InvalidOperationException("全部审批完成后采购状态未进入 Approved。");

    purchaseOrderNumber = $"PO-PG-{Guid.NewGuid():N}";
    var excessiveAmount = approvedPurchase.EstimatedTotal * 1.15m;
    var invalidOrder = purchaseService.RegisterOrder(administrator, purchaseId, new RegisterPurchaseOrderRequest(
        approvedPurchase.Version, "戴尔官方旗舰店", purchaseOrderNumber, excessiveAmount,
        DateOnly.FromDateTime(DateTime.Today), purchaseRequiredDate, "超额采购订单"));
    if (invalidOrder.Code != "PURCHASE_003")
        throw new InvalidOperationException("超过预估金额 110% 的下单登记未被拒绝。");

    var validActualAmount = 21000m;
    var invalidOrderAttachment = purchaseService.RegisterOrder(administrator, purchaseId, new RegisterPurchaseOrderRequest(
        approvedPurchase.Version, "戴尔官方旗舰店", purchaseOrderNumber, validActualAmount,
        DateOnly.FromDateTime(DateTime.Today), purchaseRequiredDate, "使用他人附件下单", [fileId.ToString()]));
    if (invalidOrderAttachment.Code != "FILE_005")
        throw new InvalidOperationException("采购下单人可以冒用他人附件作为订单附件。");

    await using var orderContractContent = new MemoryStream("%PDF-1.4\norder-contract-content"u8.ToArray());
    var orderContractFile = await fileService.UploadAsync(administrator, new FormFile(orderContractContent, 0, orderContractContent.Length, "file", "order-contract.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" });
    if (!orderContractFile.IsSuccess) throw new InvalidOperationException(orderContractFile.Error);

    var registerOrder = purchaseService.RegisterOrder(administrator, purchaseId, new RegisterPurchaseOrderRequest(
        approvedPurchase.Version, "戴尔官方旗舰店", purchaseOrderNumber, validActualAmount,
        DateOnly.FromDateTime(DateTime.Today), purchaseRequiredDate, "按批准配置完成下单", [orderContractFile.Value!.Id.ToString()]));
    if (!registerOrder.IsSuccess || registerOrder.Value!.Status != PurchaseStatus.Ordered || registerOrder.Value.Order?.OrderNumber != purchaseOrderNumber)
        throw new InvalidOperationException(registerOrder.Error ?? "采购下单登记失败。");

    var duplicateOrderNumberReq = purchaseService.RegisterOrder(administrator, purchaseId, new RegisterPurchaseOrderRequest(
        registerOrder.Value.Version, "戴尔", purchaseOrderNumber, validActualAmount,
        DateOnly.FromDateTime(DateTime.Today), purchaseRequiredDate, "重复订单号"));
    if (duplicateOrderNumberReq.Code != "STATE_001" && duplicateOrderNumberReq.Code != "PURCHASE_003")
        throw new InvalidOperationException("已下单单据允许重复下单或未识别订单号重复。");

    var unauthorizedReceipt = purchaseService.Receive(data.GetEmployee("u-chen"), purchaseId, new ReceivePurchaseRequest(
        registerOrder.Value.Version, DateOnly.FromDateTime(DateTime.Today), "ALL_ACCEPTED", "非申请人且无采购权限代收"));
    if (unauthorizedReceipt.Code != "AUTH_002")
        throw new InvalidOperationException("无权限非申请人被允许登记验收。");

    var validReceipt = purchaseService.Receive(employee, purchaseId, new ReceivePurchaseRequest(
        registerOrder.Value.Version, DateOnly.FromDateTime(DateTime.Today), "ALL_ACCEPTED", "工作站及显示器全部通电点亮，验收入库完毕", [fileId.ToString()]));
    if (!validReceipt.IsSuccess || validReceipt.Value!.Status != PurchaseStatus.Received || validReceipt.Value.Receipt?.Result != "ALL_ACCEPTED")
        throw new InvalidOperationException(validReceipt.Error ?? "采购到货验收登记失败。");

    var shortPurchaseDraft = purchaseService.CreateDraft(employee, new SavePurchaseRequest(
        "办公文具采购", "补充日常签字笔和便签", purchaseRequiredDate, "晨光文具",
        [new SavePurchaseItem("办公用品", "中性笔", "0.5mm 黑色", 50, "支", 2.5m, "日常消耗")]));
    if (!shortPurchaseDraft.IsSuccess || shortPurchaseDraft.Value!.EstimatedTotal != 125m)
        throw new InvalidOperationException("小额采购草稿创建失败。");
    var shortPurchaseSubmitted = purchaseService.Submit(employee, shortPurchaseDraft.Value.Id);
    if (!shortPurchaseSubmitted.IsSuccess || shortPurchaseSubmitted.Value!.Tasks.Count != 1)
        throw new InvalidOperationException("5000元以内采购未走单级直属上级审批路由。");
    var withdrawResult = purchaseService.Withdraw(employee, shortPurchaseDraft.Value.Id);
    if (!withdrawResult.IsSuccess || withdrawResult.Value!.Status != PurchaseStatus.Withdrawn)
        throw new InvalidOperationException("未审批小额采购撤回失败。");
    withdrawnPurchaseId = shortPurchaseDraft.Value.Id;

    var deletablePurchaseDraft = purchaseService.CreateDraft(employee, new SavePurchaseRequest(
        "临时测试草稿", "误提交测试", purchaseRequiredDate, null,
        [new SavePurchaseItem("办公用品", "测试用品", null, 1, "个", 10m, null)]));
    if (!purchaseService.Delete(employee, deletablePurchaseDraft.Value!.Id).IsSuccess)
        throw new InvalidOperationException("申请人无法删除自己的草稿。");
    if (purchaseService.Get(employee, deletablePurchaseDraft.Value.Id).IsSuccess)
        throw new InvalidOperationException("已删除的草稿仍能被查询。");

    var sealService = new SealService(data, writeDb, notifications, fileService, processes, null, copyService);
    var today = DateOnly.FromDateTime(DateTime.Today);

    var emptyTitleDraft = sealService.CreateDraft(employee, new SaveSealRequest("", "人事材料", "在职证明", "公章", 1, false, null, null, null, "测试", [], []));
    if (emptyTitleDraft.Code != "SEAL_001") throw new InvalidOperationException("空主题用章申请未被拒绝。");

    var invalidCopiesDraft = sealService.CreateDraft(employee, new SaveSealRequest("份数非法", "人事材料", "在职证明", "公章", 0, false, null, null, null, "测试", [], []));
    if (invalidCopiesDraft.Code != "SEAL_001") throw new InvalidOperationException("用印份数小于1未被拒绝。");

    var invalidOutDraft = sealService.CreateDraft(employee, new SaveSealRequest("外带未指定日期", "人事材料", "在职证明", "公章", 1, true, null, null, null, "测试", [], []));
    if (invalidOutDraft.Code != "SEAL_002") throw new InvalidOperationException("外带未指定日期未被拒绝。");

    var inOfficeDraft = sealService.CreateDraft(employee, new SaveSealRequest(
        "员工出国签证在职及收入证明用印", "人事材料", "在职及收入证明", "合同专用章", 1, false, null, null, null,
        "办理个人旅游签证使用", [fileId.ToString()], ["u-chen"]));
    if (!inOfficeDraft.IsSuccess || inOfficeDraft.Value!.Copies != 1 || inOfficeDraft.Value.IsOut)
        throw new InvalidOperationException(inOfficeDraft.Error ?? "在司用章草稿创建失败。");
    inOfficeSealId = inOfficeDraft.Value.Id;

    var inOfficeSubmitted = sealService.Submit(employee, inOfficeSealId);
    if (!inOfficeSubmitted.IsSuccess || inOfficeSubmitted.Value!.Tasks.Count != 1 || inOfficeSubmitted.Value.ProcessDefinitionCode != "SEAL_DEFAULT")
        throw new InvalidOperationException("低风险在司用章未路由至单级直属上级。");
    if (inOfficeSubmitted.Value.Tasks[0].AssigneeId != "u-li")
        throw new InvalidOperationException("用章审批人解析不符合预期。");

    var inOfficeApprove = sealService.Approve(data.GetEmployee("u-li"), inOfficeSubmitted.Value.Tasks[0].Id, "核实属实，同意盖章");
    if (!inOfficeApprove.IsSuccess || inOfficeApprove.Value!.Status != SealStatus.Approved)
        throw new InvalidOperationException("在司用章单级审批完成未进入 Approved 状态。");

    var unauthorizedExecution = sealService.RegisterExecution(data.GetEmployee("u-chen"), inOfficeSealId, new RegisterSealExecutionRequest(
        inOfficeApprove.Value.Version, today, "陈助理", "越权登记盖章", []));
    if (unauthorizedExecution.Code != "AUTH_002")
        throw new InvalidOperationException("无 SEAL_MANAGE 权限员工被允许登记用印执行。");

    var authorizedExecution = sealService.RegisterExecution(hr, inOfficeSealId, new RegisterSealExecutionRequest(
        inOfficeApprove.Value.Version, today, "孙行政", "核对原件一致，已在指定位置加盖合同章", []));
    if (!authorizedExecution.IsSuccess || authorizedExecution.Value!.Status != SealStatus.Executed || authorizedExecution.Value.Execution?.OperatorName != "孙行政")
        throw new InvalidOperationException(authorizedExecution.Error ?? "在司用印执行登记失败。");

    var outOfficeDraft = sealService.CreateDraft(employee, new SaveSealRequest(
        "外省重点战略合作框架协议外带用印", "合同协议", "战略合作框架协议", "公章", 4, true,
        today, today.AddDays(3), "张晨", "赴杭州合作方现场签署并加盖公章", [fileId.ToString()], ["u-chen"]));
    if (!outOfficeDraft.IsSuccess || !outOfficeDraft.Value!.IsOut)
        throw new InvalidOperationException("外带用章草稿创建失败。");
    outOfficeSealId = outOfficeDraft.Value.Id;

    var outOfficeSubmitted = sealService.Submit(employee, outOfficeSealId);
    if (!outOfficeSubmitted.IsSuccess || outOfficeSubmitted.Value!.Tasks.Count != 3)
        throw new InvalidOperationException("外带公章未触发三级审批路由（上级+HR+总经理）。");
    if (outOfficeSubmitted.Value.Tasks[0].AssigneeId != "u-li" || outOfficeSubmitted.Value.Tasks[1].AssigneeId != "u-sun" || outOfficeSubmitted.Value.Tasks[2].AssigneeId != "u-wang")
        throw new InvalidOperationException("外带公章三级审批人顺序不符合预期。");

    var outApprove1 = sealService.Approve(data.GetEmployee("u-li"), outOfficeSubmitted.Value.Tasks[0].Id, "同意直属部门外带");
    if (!outApprove1.IsSuccess || outApprove1.Value!.Status != SealStatus.Approving)
        throw new InvalidOperationException("第一节点审批后状态异常。");

    var outApprove2 = sealService.Approve(hr, outOfficeSubmitted.Value.Tasks[1].Id, "印章外带台账已登记，请注意安全");
    if (!outApprove2.IsSuccess || outApprove2.Value!.Status != SealStatus.Approving)
        throw new InvalidOperationException("第二节点审批后状态异常。");

    var outApprove3 = sealService.Approve(data.GetEmployee("u-wang"), outOfficeSubmitted.Value.Tasks[2].Id, "同意公章借出外带");
    if (!outApprove3.IsSuccess || outApprove3.Value!.Status != SealStatus.Approved)
        throw new InvalidOperationException("总经理审批后外带用章未进入 Approved 状态。");

    var registerOut = sealService.RegisterExecution(hr, outOfficeSealId, new RegisterSealExecutionRequest(
        outApprove3.Value.Version, today, "张晨", "公章交接完毕，领用出库", []));
    if (!registerOut.IsSuccess || registerOut.Value!.Status != SealStatus.Out)
        throw new InvalidOperationException("外带借出出库登记后状态未进入 Out。");

    var futureReturn = sealService.RegisterReturn(hr, outOfficeSealId, new RegisterSealReturnRequest(
        registerOut.Value.Version, today.AddDays(2), "INTACT", "孙行政", "未来归还测试", []));
    if (futureReturn.Code != "SEAL_004")
        throw new InvalidOperationException("未来归还日期未被拒绝。");

    var registerReturn = sealService.RegisterReturn(hr, outOfficeSealId, new RegisterSealReturnRequest(
        registerOut.Value.Version, today, "INTACT", "孙行政", "印面完好，字迹清晰，归还入库", []));
    if (!registerReturn.IsSuccess || registerReturn.Value!.Status != SealStatus.Returned || registerReturn.Value.Return?.SealCondition != "INTACT")
        throw new InvalidOperationException(registerReturn.Error ?? "外带归还登记后状态未进入 Returned。");

    var twoTierDraft = sealService.CreateDraft(employee, new SaveSealRequest(
        "招投标文件盖章", "招投标文件", "智慧政务平台投标书", "公章", 2, false, null, null, null, "参与项目招投标", []));
    if (!twoTierDraft.IsSuccess) throw new InvalidOperationException("二类重要文档草稿创建失败。");
    var twoTierSubmitted = sealService.Submit(employee, twoTierDraft.Value!.Id);
    if (!twoTierSubmitted.IsSuccess || twoTierSubmitted.Value!.Tasks.Count != 2 || twoTierSubmitted.Value.Tasks[0].AssigneeId != "u-li" || twoTierSubmitted.Value.Tasks[1].AssigneeId != "u-sun")
        throw new InvalidOperationException("招投标文件未按规则路由至直属上级与HR两级审批。");

    var shortSealDraft = sealService.CreateDraft(employee, new SaveSealRequest(
        "误提交用章", "其他", "临时文件", "公章", 1, false, null, null, null, "临时测试", []));
    var shortSealSubmitted = sealService.Submit(employee, shortSealDraft.Value!.Id);
    var withdrawSealResult = sealService.Withdraw(employee, shortSealDraft.Value.Id);
    if (!withdrawSealResult.IsSuccess || withdrawSealResult.Value!.Status != SealStatus.Withdrawn)
        throw new InvalidOperationException("用章申请撤回失败。");
    withdrawnSealId = shortSealDraft.Value.Id;

    var deletableSealDraft = sealService.CreateDraft(employee, new SaveSealRequest(
        "删除测试草稿", "其他", "删除测试文件", "公章", 1, false, null, null, null, "删除测试", []));
    if (!sealService.Delete(employee, deletableSealDraft.Value!.Id).IsSuccess)
        throw new InvalidOperationException("用章草稿删除失败。");
    if (sealService.Get(employee, deletableSealDraft.Value.Id).IsSuccess)
        throw new InvalidOperationException("已删除的用章草稿仍能被查询。");

    var docService = new KnowledgeDocumentService(data, writeDb);

    // 1. Category tests
    var unauthCat = docService.SaveCategory(employee, new SaveCategoryRequest("POLICY_SYS", "系统制度", "描述", null, null, 10));
    if (unauthCat.Code != "AUTH_002") throw new InvalidOperationException("普通员工创建文档分类未被拦截。");

    var catResult = docService.SaveCategory(hr, new SaveCategoryRequest("POLICY_SYS", "企业规章制度", "全公司通用治理制度", null, null, 10));
    if (!catResult.IsSuccess) throw new InvalidOperationException("HR创建分类失败：" + catResult.Error);
    testCatId = catResult.Value!.Id;

    var catList = docService.ListCategories(employee);
    if (!catList.IsSuccess || !catList.Value!.Any(c => c.Id == testCatId))
        throw new InvalidOperationException("员工未能查询到新建的公开分类。");

    // 2. Draft document tests
    var unauthDoc = docService.CreateDraft(employee, new SaveDocumentRequest("测试制度", testCatId, "摘要", "正文", [], false, null, today, null, []));
    if (unauthDoc.Code != "AUTH_002") throw new InvalidOperationException("普通员工编制制度草稿未被拦截。");

    var emptyTitleDoc = docService.CreateDraft(hr, new SaveDocumentRequest("", testCatId, "摘要", "正文", [], false, null, today, null, []));
    if (emptyTitleDoc.Code != "DOC_001") throw new InvalidOperationException("空标题制度草稿未被校验拦截。");

    var validDraft = docService.CreateDraft(hr, new SaveDocumentRequest(
        "企业员工廉洁合规行为守则", testCatId, "规范全体员工在日常履职中的廉洁自律与反商业贿赂行为准则。",
        "### 第一条 适用范围\n全体正式及试用期在职员工。\n\n### 第二条 行为红线\n严禁收受商业贿赂或私自侵占公司商业机会。",
        ["合规", "廉洁", "行为规范"], true, null, today, null, []));
    if (!validDraft.IsSuccess || validDraft.Value!.Status != DocumentStatus.Draft)
        throw new InvalidOperationException("HR编制制度草稿失败：" + validDraft.Error);
    testDocId = validDraft.Value!.Id;

    // 3. Draft cannot be acknowledged
    var unpubAck = docService.AcknowledgeDocument(employee, testDocId, "127.0.0.1");
    if (unpubAck.Code != "DOC_004") throw new InvalidOperationException("未发布草稿制度允许签署确认。");

    // 4. Publish document
    var publishResult = docService.PublishDocument(hr, testDocId);
    if (!publishResult.IsSuccess || publishResult.Value!.Status != DocumentStatus.Published || publishResult.Value.Version != 1)
        throw new InvalidOperationException("制度文档发布失败：" + publishResult.Error);

    // 5. Query published documents by employee
    var empList = docService.ListDocuments(employee, keyword: "廉洁", mustReadOnly: true);
    if (!empList.IsSuccess || !empList.Value!.Items.Any(d => d.Id == testDocId))
        throw new InvalidOperationException("员工按关键字未能检索到已发布的必读制度。");

    // 6. Acknowledge document by employee
    var ackResult = docService.AcknowledgeDocument(employee, testDocId, "192.168.1.100");
    if (!ackResult.IsSuccess || ackResult.Value!.UserId != employee.Id || ackResult.Value.DocumentVersion != 1)
        throw new InvalidOperationException("员工签署确认制度失败：" + ackResult.Error);

    // Idempotent acknowledge
    var dupAck = docService.AcknowledgeDocument(employee, testDocId, "192.168.1.100");
    if (!dupAck.IsSuccess || dupAck.Value!.Id != ackResult.Value.Id)
        throw new InvalidOperationException("员工重复签署同一版本未幂等返回。");

    // 7. Check stats
    var statsResult = docService.GetAcknowledgementStats(hr, testDocId);
    if (!statsResult.IsSuccess || statsResult.Value!.TotalAcknowledged < 1 || statsResult.Value.AcknowledgedList.All(a => a.UserId != employee.Id))
        throw new InvalidOperationException("制度签署看板统计数据不正确。");

    // 8. Revise document to v2
    var reviseResult = docService.ReviseDocument(hr, testDocId, new ReviseDocumentRequest(
        1, "企业员工廉洁合规行为守则（2026修订版）",
        "补充对礼品礼金申报登记限额的详细要求。",
        "### 第一条 适用范围\n全体正式及试用期在职员工。\n\n### 第二条 行为红线\n严禁收受商业贿赂或私自侵占公司商业机会。\n\n### 第三条 礼品申报\n单次价值超过 200 元的商务礼品须于 3 日内向行政人事部登记报备。",
        "新增第三条礼品申报流程细则", []));
    if (!reviseResult.IsSuccess || reviseResult.Value!.Version != 2 || reviseResult.Value.Status != DocumentStatus.Published)
        throw new InvalidOperationException("制度版本修订升级失败：" + reviseResult.Error);

    var pendingAfterRevision = docService.ListDocuments(employee, mustReadOnly: true, pendingAckOnly: true);
    if (!pendingAfterRevision.IsSuccess || pendingAfterRevision.Value!.Items.All(item => item.Id != testDocId))
        throw new InvalidOperationException("员工已签署旧版本后，新发布版本没有重新进入待签收列表。");

    var versionsList = docService.ListVersions(employee, testDocId);
    if (!versionsList.IsSuccess || versionsList.Value!.Count < 2)
        throw new InvalidOperationException("版本历史列表未包含升级记录。");

    // 9. Employee acknowledges v2
    var ackV2 = docService.AcknowledgeDocument(employee, testDocId, "192.168.1.100");
    if (!ackV2.IsSuccess || ackV2.Value!.DocumentVersion != 2)
        throw new InvalidOperationException("员工签署确认新版本 v2 失败：" + ackV2.Error);

    // 10. Deletable draft test
    var tempDraft = docService.CreateDraft(hr, new SaveDocumentRequest("待删除临时制度", testCatId, "临时摘要", "临时正文", [], false, null, today, null, []));
    if (!tempDraft.IsSuccess || !docService.DeleteDraft(hr, tempDraft.Value!.Id).IsSuccess)
        throw new InvalidOperationException("临时草稿删除失败。");

    // 11. Department Isolation Tests
    var engManager = data.GetEmployee("u-li");
    var finEmployee = data.GetEmployee("u-chen");
    var finManager = data.GetEmployee("u-lin");

    // 11.1 Department Category isolation
    var engCatResult = docService.SaveCategory(engManager, new SaveCategoryRequest("TECH_GUIDE_PG", "研发专属技术规范", "研发部内部技术规范", null, "engineering", 20));
    if (!engCatResult.IsSuccess) throw new InvalidOperationException("研发部负责人创建部门分类失败：" + engCatResult.Error);
    var engCatId = engCatResult.Value!.Id;

    var crossDeptCat = docService.SaveCategory(engManager, new SaveCategoryRequest("FIN_GUIDE_PG", "财务专属规范", "试图为财务创建", null, "finance", 20));
    if (crossDeptCat.Code != "AUTH_002") throw new InvalidOperationException("部门负责人跨部门创建分类未被拒绝。");

    var globalCatByDept = docService.SaveCategory(engManager, new SaveCategoryRequest("CORP_SYS_PG", "公司级通用分类", "试图创建公司级分类", null, null, 20));
    if (globalCatByDept.Code != "AUTH_002") throw new InvalidOperationException("部门负责人越权创建公司级全局分类未被拒绝。");

    // Finance employee should not see engineering category
    var finCatList = docService.ListCategories(finEmployee);
    if (finCatList.Value!.Any(c => c.Id == engCatId))
        throw new InvalidOperationException("财务员工看到了研发部专属保密分类。");

    // Engineering employee should see engineering category
    var engCatList = docService.ListCategories(employee);
    if (!engCatList.Value!.Any(c => c.Id == engCatId))
        throw new InvalidOperationException("研发员工未能看到研发部专属分类。");

    // 11.2 Department Document creation isolation
    var engDocResult = docService.CreateDraft(engManager, new SaveDocumentRequest(
        "研发部核心代码安全与密钥管理规范", engCatId, "规范研发部核心代码、生产环境私钥与证书保管规则。",
        "### 第一条 密钥红线\n生产私钥严禁提交至公共仓库。\n\n### 第二条 访问控制\n核心仓库仅研发部成员可读写。",
        ["研发", "密钥", "机密"], true, "engineering", today, null, []));
    if (!engDocResult.IsSuccess || engDocResult.Value!.Status != DocumentStatus.Draft)
        throw new InvalidOperationException("研发负责人编制部门制度草稿失败：" + engDocResult.Error);
    var engDocId = engDocResult.Value!.Id;

    var crossDeptDoc = docService.CreateDraft(engManager, new SaveDocumentRequest(
        "财务凭证审计指引", engCatId, "试图为财务编制", "正文", [], false, "finance", today, null, []));
    if (crossDeptDoc.Code != "AUTH_002") throw new InvalidOperationException("部门负责人跨部门编制草稿未被拒绝。");

    var corpDocByDept = docService.CreateDraft(engManager, new SaveDocumentRequest(
        "公司级考勤规范", engCatId, "试图编制全公司规范", "正文", [], false, null, today, null, []));
    if (corpDocByDept.Code != "AUTH_002") throw new InvalidOperationException("部门负责人越权编制公司通用制度未被拒绝。");

    // Publish engineering department document
    var publishEngDoc = docService.PublishDocument(engManager, engDocId);
    if (!publishEngDoc.IsSuccess || publishEngDoc.Value!.Status != DocumentStatus.Published)
        throw new InvalidOperationException("研发负责人发布本部门制度失败：" + publishEngDoc.Error);

    // 11.3 Cross-department viewing & access isolation
    var crossViewDoc = docService.GetDocument(finEmployee, engDocId);
    if (crossViewDoc.Code != "DOC_003")
        throw new InvalidOperationException("财务员工越权查阅了研发部专属保密制度。");

    var finDocList = docService.ListDocuments(finEmployee, keyword: "核心代码安全");
    if (finDocList.Value!.Items.Any(d => d.Id == engDocId))
        throw new InvalidOperationException("财务员工列表检索到了研发部专属保密制度。");

    var crossRevise = docService.ReviseDocument(finManager, engDocId, new ReviseDocumentRequest(1, "篡改研发标题", "摘要", "正文", "越权修改", []));
    if (crossRevise.Code != "AUTH_002")
        throw new InvalidOperationException("财务经理跨部门修订研发制度未被拒绝。");

    var crossArchive = docService.ArchiveDocument(finManager, engDocId);
    if (crossArchive.Code != "AUTH_002")
        throw new InvalidOperationException("财务经理跨部门归档研发制度未被拒绝。");

    // 11.4 Department must-read & acknowledgement isolation
    var finPendingList = docService.ListDocuments(finEmployee, mustReadOnly: true, pendingAckOnly: true);
    if (finPendingList.Value!.Items.Any(d => d.Id == engDocId))
        throw new InvalidOperationException("研发部专属必读制度错误出现在财务员工待签署列表中。");

    var crossAck = docService.AcknowledgeDocument(finEmployee, engDocId, "192.168.2.1");
    if (crossAck.Code != "AUTH_002")
        throw new InvalidOperationException("非本部门员工签署部门专属制度未被拒绝。");

    var engPendingList = docService.ListDocuments(employee, mustReadOnly: true, pendingAckOnly: true);
    if (!engPendingList.Value!.Items.Any(d => d.Id == engDocId))
        throw new InvalidOperationException("研发部员工待签署列表中未包含本部门专属必读制度。");

    var engAck = docService.AcknowledgeDocument(employee, engDocId, "192.168.1.100");
    if (!engAck.IsSuccess || engAck.Value!.UserId != employee.Id)
        throw new InvalidOperationException("研发员工签署本部门必读制度失败：" + engAck.Error);

    var engStats = docService.GetAcknowledgementStats(engManager, engDocId);
    if (!engStats.IsSuccess)
        throw new InvalidOperationException("研发负责人查看本部门制度签收看板失败：" + engStats.Error);

    var crossStats = docService.GetAcknowledgementStats(finManager, engDocId);
    if (crossStats.Code != "AUTH_002")
        throw new InvalidOperationException("财务经理跨部门查看研发部制度签收看板未被拒绝。");

    var globalStats = docService.GetAcknowledgementStats(hr, engDocId);
    if (!globalStats.IsSuccess)
        throw new InvalidOperationException("全局管理员查看部门制度签收看板失败：" + globalStats.Error);

    // 12. Document Category Hierarchy & Circular Check
    var subCatResult = docService.SaveCategory(engManager, new SaveCategoryRequest("TECH_BE_PG", "后端架构与规范", "研发二级子类", engCatId, "engineering", 21));
    if (!subCatResult.IsSuccess)
        throw new InvalidOperationException("研发负责人创建子分类失败：" + subCatResult.Error);
    var subCatId = subCatResult.Value!.Id;

    var circularCat = docService.SaveCategory(engManager, new SaveCategoryRequest("TECH_GUIDE_PG", "研发专属技术规范", "试图形成自循环层级", subCatId, "engineering", 20), engCatId);
    if (circularCat.Code != "CAT_003")
        throw new InvalidOperationException("分类层级自循环检测未拦截。");

    // 13. Ordinary Employee Creates & Edits Department Document
    var empDraftResult = docService.CreateDraft(employee, new SaveDocumentRequest(
        "前端开发代码风格规范", subCatId, "规范前端Vue组件开发与TypeScript书写规范。",
        "### 规范1\n采用 Composition API 编写组件。\n\n### 规范2\n所有 API 必须强类型定义。",
        ["前端", "代码规范"], false, "engineering", today, null, []));
    if (!empDraftResult.IsSuccess || empDraftResult.Value!.Status != DocumentStatus.Draft)
        throw new InvalidOperationException("研发普通员工编制部门制度草稿失败：" + empDraftResult.Error);
    var empDocId = empDraftResult.Value.Id;

    var crossEmpDraft = docService.CreateDraft(finEmployee, new SaveDocumentRequest(
        "越权编制研发规范", subCatId, "试图跨部门编制", "正文", [], false, "engineering", today, null, []));
    if (crossEmpDraft.Code != "AUTH_002")
        throw new InvalidOperationException("财务员工跨部门编制研发草稿未被拦截。");

    var updateEmpDraft = docService.UpdateDraft(employee, empDocId, new SaveDocumentRequest(
        "前端开发代码风格规范（更新草稿）", subCatId, "更新摘要内容",
        "### 规范1\n采用 Composition API 编写组件。\n\n### 规范2\n所有 API 必须强类型定义与接口注解。",
        ["前端", "代码规范"], false, "engineering", today, null, []));
    if (!updateEmpDraft.IsSuccess)
        throw new InvalidOperationException("研发员工更新本部门草稿失败：" + updateEmpDraft.Error);

    // Publish employee's draft by manager
    var pubEmpDoc = docService.PublishDocument(engManager, empDocId);
    if (!pubEmpDoc.IsSuccess || pubEmpDoc.Value!.Version != 1)
        throw new InvalidOperationException("部门主管发布员工草稿失败：" + pubEmpDoc.Error);

    // 14. Ordinary Employee Revises Published Department Document
    var empRevise = docService.ReviseDocument(employee, empDocId, new ReviseDocumentRequest(
        1, "前端开发代码风格规范（v2.0）", "新增Pinia状态管理与规范细则",
        "### 规范1\n采用 Composition API 编写组件。\n\n### 规范2\n所有 API 必须强类型定义与接口注解。\n\n### 规范3\n状态管理全面使用 Pinia Store。",
        "升级至v2.0增加Pinia指引", []));
    if (!empRevise.IsSuccess || empRevise.Value!.Version != 2)
        throw new InvalidOperationException("研发普通员工修订发布新版本失败：" + empRevise.Error);

    // 15. Version Compare & Diff
    var diffResult = docService.CompareVersions(employee, empDocId, 1, 2);
    if (!diffResult.IsSuccess || diffResult.Value!.SourceVersion != 1 || diffResult.Value.TargetVersion != 2)
        throw new InvalidOperationException("版本差异对比失败：" + diffResult.Error);
    if (diffResult.Value.AddedLines == 0)
        throw new InvalidOperationException("版本差异分析未检测到新增行。");
    if (!diffResult.Value.ContentDiff.Any(d => d.Type == "added" && d.Text.Contains("规范3")))
        throw new InvalidOperationException("版本差异未包含新增的规范3行。");

    // 16. Version Rollback
    var rollbackResult = docService.RollbackDocument(employee, empDocId, new RollbackDocumentRequest(
        1, 2, "回退至v1.0：暂缓引入Pinia规范要求"));
    if (!rollbackResult.IsSuccess || rollbackResult.Value!.Version != 3)
        throw new InvalidOperationException("版本回退生成递增版本失败：" + rollbackResult.Error);
    if (!rollbackResult.Value.Content.Contains("规范2") || rollbackResult.Value.Content.Contains("规范3"))
        throw new InvalidOperationException("回退生成的版本正文快照不符合目标版本v1.0。");

    // 17. Manager Moves Document Category (Reorganize Structure)
    var moveResult = docService.MoveDocumentCategory(engManager, empDocId, engCatId);
    if (!moveResult.IsSuccess || moveResult.Value!.CategoryId != engCatId)
        throw new InvalidOperationException("部门主管调整文档所属分类失败：" + moveResult.Error);

    var empMove = docService.MoveDocumentCategory(employee, empDocId, subCatId);
    if (empMove.Code != "AUTH_002")
        throw new InvalidOperationException("普通员工越权调整文档组织结构分类未被拦截。");

    // 18. Document Deletion by Manager vs Employee
    var empDelete = docService.DeleteDocument(employee, empDocId);
    if (empDelete.Code != "AUTH_002")
        throw new InvalidOperationException("普通员工越权删除已发布的部门制度未被拦截。");

    var mgrDelete = docService.DeleteDocument(engManager, empDocId);
    if (!mgrDelete.IsSuccess)
        throw new InvalidOperationException("部门主管删除本部门制度失败：" + mgrDelete.Error);

    var getDeleted = docService.GetDocument(engManager, empDocId);
    if (getDeleted.Code != "DOC_003")
        throw new InvalidOperationException("已删除的制度仍可被查询到。");

    var idempotency = new IdempotencyService(writeDb);
    idempotency.Store(employee.Id, "POST:/api/v1/leave-requests", idempotencyKey, 201, "{\"id\":\"cached\"}");
    if (idempotency.Find(employee.Id, "POST:/api/v1/leave-requests", idempotencyKey) is not { StatusCode: 201 })
        throw new InvalidOperationException("幂等键未能持久化或重新读取。");
}

var atomicIdempotencyKey = $"pg-hr-atomic-{Guid.NewGuid():N}";
var atomicIdempotencyRoute = "POST:/api/v1/hr/personnel-cases";
var atomicIdempotencyHash = IdempotencyService.Fingerprint(new { userId = "u-chen", type = PersonnelCaseTypes.Onboarding, effectiveDate = DateOnly.FromDateTime(DateTime.Today).AddDays(180) });
var atomicCaseDate = DateOnly.FromDateTime(DateTime.Today).AddDays(180);
Guid atomicCaseId;
await using (var atomicDbA = new OaDbContext(options))
{
    var idempotencyA = new IdempotencyService(atomicDbA);
    using var leaseA = idempotencyA.Acquire("u-sun", atomicIdempotencyRoute, atomicIdempotencyKey);
    var waiterStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var waiterAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var waiter = Task.Run(async () =>
    {
        await using var atomicDbB = new OaDbContext(options);
        var idempotencyB = new IdempotencyService(atomicDbB);
        waiterStarted.SetResult();
        using var leaseB = idempotencyB.Acquire("u-sun", atomicIdempotencyRoute, atomicIdempotencyKey);
        waiterAcquired.SetResult();
        return idempotencyB.Lookup("u-sun", atomicIdempotencyRoute, atomicIdempotencyKey, atomicIdempotencyHash);
    });
    await waiterStarted.Task;
    await Task.Delay(150);
    if (waiterAcquired.Task.IsCompleted)
        throw new InvalidOperationException("相同幂等键未通过 PostgreSQL advisory lock 串行化。 ");

    using (var transaction = idempotencyA.BeginTransaction())
    {
        var directory = new DemoData(atomicDbA);
        var created = new PersonnelCaseService(atomicDbA, directory, new NotificationService(atomicDbA)).Create(
            directory.GetEmployee("u-sun"),
            new CreatePersonnelCaseRequest("u-chen", PersonnelCaseTypes.Onboarding, atomicCaseDate, "u-sun", "验证 HR 业务与幂等响应原子提交"));
        if (!created.IsSuccess) throw new InvalidOperationException(created.Error ?? "无法建立 HR 原子幂等测试数据。 ");
        atomicCaseId = created.Value!.Id;
        idempotencyA.Store("u-sun", atomicIdempotencyRoute, atomicIdempotencyKey, StatusCodes.Status201Created, $"{{\"id\":\"{atomicCaseId}\"}}", atomicIdempotencyHash);
        transaction.Commit();
    }
    leaseA.Dispose();
    var replay = await waiter;
    if (replay is not { Record.StatusCode: StatusCodes.Status201Created, HashConflict: false } ||
        !idempotencyA.Lookup("u-sun", atomicIdempotencyRoute, atomicIdempotencyKey, IdempotencyService.Fingerprint(new { different = true })).HashConflict)
        throw new InvalidOperationException("并发重放未读取首个 HR 响应，或不同请求内容复用了同一幂等键。 ");
}
await using (var atomicVerificationDb = new OaDbContext(options))
{
    if (await atomicVerificationDb.PersonnelCases.CountAsync(item => item.Id == atomicCaseId) != 1 ||
        await atomicVerificationDb.IdempotencyKeys.CountAsync(item => item.Key == atomicIdempotencyKey) != 1)
        throw new InvalidOperationException("HR 业务与幂等响应未在同一事务中持久化。 ");
}

var rollbackIdempotencyKey = $"pg-hr-rollback-{Guid.NewGuid():N}";
var rollbackCaseDate = atomicCaseDate.AddDays(1);
Guid rollbackCaseId;
await using (var rollbackDb = new OaDbContext(options))
{
    var idempotency = new IdempotencyService(rollbackDb);
    using var lease = idempotency.Acquire("u-sun", atomicIdempotencyRoute, rollbackIdempotencyKey);
    using var transaction = idempotency.BeginTransaction();
    var directory = new DemoData(rollbackDb);
    var created = new PersonnelCaseService(rollbackDb, directory, new NotificationService(rollbackDb)).Create(
        directory.GetEmployee("u-sun"),
        new CreatePersonnelCaseRequest("u-chen", PersonnelCaseTypes.Transfer, rollbackCaseDate, "u-sun", "验证幂等响应失败时 HR 业务整体回滚"));
    if (!created.IsSuccess) throw new InvalidOperationException(created.Error ?? "无法建立 HR 幂等回滚测试数据。 ");
    rollbackCaseId = created.Value!.Id;
    idempotency.Store("u-sun", atomicIdempotencyRoute, rollbackIdempotencyKey, StatusCodes.Status201Created, $"{{\"id\":\"{rollbackCaseId}\"}}", IdempotencyService.Fingerprint(new { rollbackCaseId }));
    transaction.Rollback();
}
await using (var rollbackVerificationDb = new OaDbContext(options))
{
    if (await rollbackVerificationDb.PersonnelCases.AnyAsync(item => item.Id == rollbackCaseId) ||
        await rollbackVerificationDb.IdempotencyKeys.AnyAsync(item => item.Key == rollbackIdempotencyKey))
        throw new InvalidOperationException("HR 业务或幂等响应在事务回滚后留下了半成品。 ");
}

var concurrencyConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["DemoFeatures:AllowDataGeneration"] = "true"
}).Build();
var concurrencyToday = DateOnly.FromDateTime(DateTime.Today);

await using (var personnelDbA = new OaDbContext(options))
await using (var personnelDbB = new OaDbContext(options))
{
    const string subjectId = "u-chen";
    var userA = await personnelDbA.Users.SingleAsync(item => item.Id == subjectId);
    var profileA = await personnelDbA.PersonnelProfiles.SingleAsync(item => item.UserId == subjectId);
    var userB = await personnelDbB.Users.SingleAsync(item => item.Id == subjectId);
    var profileB = await personnelDbB.PersonnelProfiles.SingleAsync(item => item.UserId == subjectId);
    var directoryA = new DemoData(personnelDbA);
    var directoryB = new DemoData(personnelDbB);
    var serviceA = new PersonnelService(personnelDbA, directoryA, new NotificationService(personnelDbA));
    var serviceB = new PersonnelService(personnelDbB, directoryB, new NotificationService(personnelDbB));
    var baseRequest = new UpdatePersonnelProfileRequest(
        userA.DepartmentId, userA.PositionId, userA.ManagerId, profileA.WorkEmail, profileA.WorkPhone, "上海一号办公区", profileA.EmploymentType, profileA.PersonnelStatus,
        profileA.HireDate, profileA.ProbationEndDate, profileA.RegularizedDate, profileA.CumulativeWorkStartDate, profileA.DepartureDate, profileA.DepartureReason,
        profileA.Version, concurrencyToday, "验证人事档案数据库级并发保护");
    var firstUpdate = serviceA.Update(directoryA.GetEmployee("u-sun"), subjectId, baseRequest);
    var staleUpdate = serviceB.Update(directoryB.GetEmployee("u-sun"), subjectId, baseRequest with { WorkLocation = "陈旧会话办公区", ChangeReason = "陈旧会话不应覆盖人事档案" });
    if (!firstUpdate.IsSuccess || staleUpdate.Code != "CONCURRENCY_001")
        throw new InvalidOperationException(firstUpdate.Error ?? "两个独立数据库会话未能阻止人事档案静默覆盖。 ");
}

Guid concurrentPersonnelCaseId;
Guid concurrentPersonnelTaskId;
await using (var caseSetupDb = new OaDbContext(options))
{
    var directory = new DemoData(caseSetupDb);
    var created = new PersonnelCaseService(caseSetupDb, directory, new NotificationService(caseSetupDb)).Create(
        directory.GetEmployee("u-sun"),
        new CreatePersonnelCaseRequest("u-chen", PersonnelCaseTypes.Transfer, concurrencyToday.AddDays(45), "u-sun", "验证员工办理任务数据库级并发保护"));
    if (!created.IsSuccess) throw new InvalidOperationException(created.Error ?? "无法建立员工办理任务并发测试数据。 ");
    concurrentPersonnelCaseId = created.Value!.Id;
    concurrentPersonnelTaskId = created.Value.Tasks.Single(task => task.Code == "TRANSFER_APPROVAL").Id;
}
await using (var caseDbA = new OaDbContext(options))
await using (var caseDbB = new OaDbContext(options))
{
    var caseA = await caseDbA.PersonnelCases.SingleAsync(item => item.Id == concurrentPersonnelCaseId);
    var taskA = await caseDbA.PersonnelCaseTasks.SingleAsync(item => item.Id == concurrentPersonnelTaskId);
    _ = await caseDbB.PersonnelCases.SingleAsync(item => item.Id == concurrentPersonnelCaseId);
    var taskB = await caseDbB.PersonnelCaseTasks.SingleAsync(item => item.Id == concurrentPersonnelTaskId);
    var directoryA = new DemoData(caseDbA);
    var directoryB = new DemoData(caseDbB);
    var firstResult = new PersonnelCaseService(caseDbA, directoryA, new NotificationService(caseDbA)).UpdateTask(
        directoryA.GetEmployee("u-sun"), caseA.Id, taskA.Id,
        new UpdatePersonnelCaseTaskRequest(taskA.AssigneeId, taskA.DueDate, PersonnelTaskStatuses.Completed, "第一个实例完成任务", taskA.Version));
    var staleResult = new PersonnelCaseService(caseDbB, directoryB, new NotificationService(caseDbB)).UpdateTask(
        directoryB.GetEmployee("u-sun"), concurrentPersonnelCaseId, taskB.Id,
        new UpdatePersonnelCaseTaskRequest(taskB.AssigneeId, taskB.DueDate, PersonnelTaskStatuses.Completed, "第二个实例陈旧提交", taskB.Version));
    if (!firstResult.IsSuccess || staleResult.Code != "CONCURRENCY_001")
        throw new InvalidOperationException(firstResult.Error ?? "两个独立数据库会话未能阻止员工办理任务重复处理。 ");
}

var attendanceConcurrencyMonth = new DateOnly(concurrencyToday.Year, concurrencyToday.Month, 1).AddMonths(-1);
var attendanceConcurrencyDate = attendanceConcurrencyMonth;
await using (var dateDb = new OaDbContext(options))
{
    var dateCalendar = new WorkCalendarService(dateDb);
    while (!dateCalendar.IsWorkingDay(attendanceConcurrencyDate)) attendanceConcurrencyDate = attendanceConcurrencyDate.AddDays(1);
}
Guid concurrentAttendanceRecordId;
await using (var attendanceSetupDb = new OaDbContext(options))
{
    var directory = new DemoData(attendanceSetupDb);
    var service = new AttendanceService(
        attendanceSetupDb, directory, new WorkCalendarService(attendanceSetupDb), new NotificationService(attendanceSetupDb),
        new FileService(attendanceSetupDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)),
        concurrencyConfiguration);
    var latePunch = new AttendanceImportItem("u-chen", attendanceConcurrencyDate,
        new DateTimeOffset(attendanceConcurrencyDate.ToDateTime(new TimeOnly(9, 35)), TimeSpan.FromHours(8)),
        new DateTimeOffset(attendanceConcurrencyDate.ToDateTime(new TimeOnly(18, 5)), TimeSpan.FromHours(8)));
    var imported = service.Import(directory.GetEmployee("u-sun"), new ImportAttendanceRequest([latePunch], "MANUAL"));
    if (!imported.IsSuccess) throw new InvalidOperationException(imported.Error ?? "无法建立考勤并发测试记录。 ");
    concurrentAttendanceRecordId = attendanceSetupDb.AttendanceRecords.Single(item => item.UserId == "u-chen" && item.WorkDate == attendanceConcurrencyDate).Id;
}
await using (var attendanceDbA = new OaDbContext(options))
await using (var attendanceDbB = new OaDbContext(options))
{
    _ = await attendanceDbA.AttendanceRecords.SingleAsync(item => item.Id == concurrentAttendanceRecordId);
    _ = await attendanceDbB.AttendanceRecords.SingleAsync(item => item.Id == concurrentAttendanceRecordId);
    var directoryA = new DemoData(attendanceDbA);
    var directoryB = new DemoData(attendanceDbB);
    var serviceA = new AttendanceService(attendanceDbA, directoryA, new WorkCalendarService(attendanceDbA), new NotificationService(attendanceDbA), new FileService(attendanceDbA, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var serviceB = new AttendanceService(attendanceDbB, directoryB, new WorkCalendarService(attendanceDbB), new NotificationService(attendanceDbB), new FileService(attendanceDbB, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var punchA = new AttendanceImportItem("u-chen", attendanceConcurrencyDate, new DateTimeOffset(attendanceConcurrencyDate.ToDateTime(new TimeOnly(9, 30)), TimeSpan.FromHours(8)), new DateTimeOffset(attendanceConcurrencyDate.ToDateTime(new TimeOnly(18, 5)), TimeSpan.FromHours(8)));
    var punchB = punchA with { CheckInAt = new DateTimeOffset(attendanceConcurrencyDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(8)) };
    var firstImport = serviceA.Import(directoryA.GetEmployee("u-sun"), new ImportAttendanceRequest([punchA], "MANUAL"));
    var staleImport = serviceB.Import(directoryB.GetEmployee("u-sun"), new ImportAttendanceRequest([punchB], "MANUAL"));
    if (!firstImport.IsSuccess || staleImport.Code != "CONCURRENCY_001")
        throw new InvalidOperationException(firstImport.Error ?? "两个独立数据库会话未能阻止考勤记录静默覆盖。 ");
}

Guid concurrentAppealId;
await using (var appealSetupDb = new OaDbContext(options))
{
    var directory = new DemoData(appealSetupDb);
    var service = new AttendanceService(appealSetupDb, directory, new WorkCalendarService(appealSetupDb), new NotificationService(appealSetupDb), new FileService(appealSetupDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var appeal = service.SubmitAppeal(directory.GetEmployee("u-chen"), concurrentAttendanceRecordId, new CreateAttendanceAppealRequest("验证两个审核实例不能重复处理同一申诉。"));
    if (!appeal.IsSuccess) throw new InvalidOperationException(appeal.Error ?? "无法建立考勤申诉并发测试数据。 ");
    concurrentAppealId = appeal.Value!.Id;
}
await using (var appealDbA = new OaDbContext(options))
await using (var appealDbB = new OaDbContext(options))
{
    _ = await appealDbA.AttendanceAppeals.SingleAsync(item => item.Id == concurrentAppealId);
    _ = await appealDbB.AttendanceAppeals.SingleAsync(item => item.Id == concurrentAppealId);
    _ = await appealDbA.AttendanceRecords.SingleAsync(item => item.Id == concurrentAttendanceRecordId);
    _ = await appealDbB.AttendanceRecords.SingleAsync(item => item.Id == concurrentAttendanceRecordId);
    var directoryA = new DemoData(appealDbA);
    var directoryB = new DemoData(appealDbB);
    var serviceA = new AttendanceService(appealDbA, directoryA, new WorkCalendarService(appealDbA), new NotificationService(appealDbA), new FileService(appealDbA, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var serviceB = new AttendanceService(appealDbB, directoryB, new WorkCalendarService(appealDbB), new NotificationService(appealDbB), new FileService(appealDbB, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var firstReview = serviceA.ReviewAppeal(directoryA.GetEmployee("u-sun"), concurrentAppealId, new ReviewAttendanceAppealRequest(true, "首个审核实例确认通过。"));
    var staleReview = serviceB.ReviewAppeal(directoryB.GetEmployee("u-sun"), concurrentAppealId, new ReviewAttendanceAppealRequest(false, "陈旧审核实例尝试驳回。"));
    if (!firstReview.IsSuccess || staleReview.Code != "CONCURRENCY_001")
        throw new InvalidOperationException(firstReview.Error ?? "两个独立数据库会话仍可重复处理同一考勤申诉。 ");
}

await using (var monthSetupDb = new OaDbContext(options))
{
    var directory = new DemoData(monthSetupDb);
    var service = new AttendanceService(monthSetupDb, directory, new WorkCalendarService(monthSetupDb), new NotificationService(monthSetupDb), new FileService(monthSetupDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var locked = service.LockMonth(directory.GetEmployee("u-sun"), attendanceConcurrencyMonth, new ChangeAttendanceMonthLockRequest("并发测试前确认月度封账。", 0));
    if (!locked.IsSuccess) throw new InvalidOperationException(locked.Error ?? "无法建立月度封账并发测试数据。 ");
}
await using (var monthDbA = new OaDbContext(options))
await using (var monthDbB = new OaDbContext(options))
{
    var lockA = await monthDbA.AttendanceMonthLocks.SingleAsync(item => item.Month == attendanceConcurrencyMonth);
    var lockB = await monthDbB.AttendanceMonthLocks.SingleAsync(item => item.Month == attendanceConcurrencyMonth);
    var directoryA = new DemoData(monthDbA);
    var directoryB = new DemoData(monthDbB);
    var serviceA = new AttendanceService(monthDbA, directoryA, new WorkCalendarService(monthDbA), new NotificationService(monthDbA), new FileService(monthDbA, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var serviceB = new AttendanceService(monthDbB, directoryB, new WorkCalendarService(monthDbB), new NotificationService(monthDbB), new FileService(monthDbB, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var firstUnlock = serviceA.UnlockMonth(directoryA.GetEmployee("u-sun"), attendanceConcurrencyMonth, new ChangeAttendanceMonthLockRequest("首个实例授权解封。", lockA.Version));
    var staleUnlock = serviceB.UnlockMonth(directoryB.GetEmployee("u-sun"), attendanceConcurrencyMonth, new ChangeAttendanceMonthLockRequest("陈旧实例不应重复解封。", lockB.Version));
    if (!firstUnlock.IsSuccess || staleUnlock.Code != "CONCURRENCY_001")
        throw new InvalidOperationException(firstUnlock.Error ?? "两个独立数据库会话仍可重复更新月度封账。 ");
}

await using (var shiftDbA = new OaDbContext(options))
await using (var shiftDbB = new OaDbContext(options))
{
    var shiftA = await shiftDbA.AttendanceShifts.SingleAsync(item => item.IsDefault && item.IsEnabled);
    var shiftB = await shiftDbB.AttendanceShifts.SingleAsync(item => item.Id == shiftA.Id);
    var directoryA = new DemoData(shiftDbA);
    var directoryB = new DemoData(shiftDbB);
    var serviceA = new AttendanceService(shiftDbA, directoryA, new WorkCalendarService(shiftDbA), new NotificationService(shiftDbA), new FileService(shiftDbA, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var serviceB = new AttendanceService(shiftDbB, directoryB, new WorkCalendarService(shiftDbB), new NotificationService(shiftDbB), new FileService(shiftDbB, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, concurrencyConfiguration, new TestFileMalwareScanner(FileScanStatus.Clean)), concurrencyConfiguration);
    var firstShift = serviceA.UpdateShift(directoryA.GetEmployee("u-sun"), shiftA.Id, new SaveAttendanceShiftRequest(shiftA.Code, shiftA.Name, shiftA.WorkStart, shiftA.WorkEnd, shiftA.BreakMinutes, shiftA.LateToleranceMinutes + 1, shiftA.EarlyLeaveToleranceMinutes, true, true, shiftA.Version));
    var staleShift = serviceB.UpdateShift(directoryB.GetEmployee("u-sun"), shiftB.Id, new SaveAttendanceShiftRequest(shiftB.Code, shiftB.Name, shiftB.WorkStart, shiftB.WorkEnd, shiftB.BreakMinutes, shiftB.LateToleranceMinutes + 2, shiftB.EarlyLeaveToleranceMinutes, true, true, shiftB.Version));
    if (!firstShift.IsSuccess || staleShift.Code != "CONCURRENCY_001")
        throw new InvalidOperationException(firstShift.Error ?? "两个独立数据库会话仍可静默覆盖考勤班次。 ");
}

await using (var readDb = new OaDbContext(options))
{
    var persistedUser = await readDb.Users.SingleOrDefaultAsync(item => item.Id == managedUserId);
    var persistedAccount = await readDb.UserAccounts.SingleOrDefaultAsync(item => item.UserId == managedUserId);
    if (persistedUser is not { Name: "集成测试用户（已更新）", DepartmentId: "hr", PositionId: null, ManagerId: "u-wang", CumulativeWorkYears: 5, Version: 6 } ||
        persistedAccount is null || persistedAccount.PasswordHash == "ResetChanged@123" || persistedAccount.PasswordSalt == "ResetChanged@123" ||
        !PasswordHasher.Verify("ResetChanged@123", persistedAccount.PasswordSalt, persistedAccount.PasswordHash, persistedAccount.PasswordIterations) ||
        persistedAccount.MustChangePassword || persistedAccount.LockedUntil is not null || persistedAccount.FailedLoginCount != 0)
        throw new InvalidOperationException("用户资料、密码哈希或账号锁定状态未跨 DbContext 持久化。");
    if (await readDb.UserRoles.CountAsync(item => item.UserId == managedUserId && item.RoleCode == "HR/行政" && item.IsPrimary) != 1 ||
        await readDb.UserRoles.AnyAsync(item => item.UserId == managedUserId && item.RoleCode == "员工"))
        throw new InvalidOperationException("用户角色替换未跨 DbContext 持久化。");
    var reloadedDirectory = new DemoData(readDb);
    var managedEmployee = reloadedDirectory.GetEmployee(managedUserId);
    if (!reloadedDirectory.HasRole(managedEmployee, "HR/行政") || !reloadedDirectory.HasPermission(managedEmployee, OaPermissions.CalendarManage) || reloadedDirectory.HasPermission(managedEmployee, OaPermissions.UserManage))
        throw new InvalidOperationException("重建身份目录后未能从角色解析有效权限。");
    if (await readDb.AuditLogs.CountAsync(item => item.ResourceType == "User" && item.ResourceId == managedUserId) < 3)
        throw new InvalidOperationException("用户创建、更新或密码重置未完整生成审计记录。");
    var persistedPersonnel = await readDb.PersonnelProfiles.SingleOrDefaultAsync(item => item.UserId == "u-zhang");
    if (persistedPersonnel is not { WorkEmail: "zhang.chen@xxx.example", WorkLocation: "上海办公室", PersonnelStatus: PersonnelStatuses.Active, DepartureDate: null, Version: 7 } ||
        await readDb.PersonnelEvents.CountAsync(item => item.UserId == "u-zhang") != 7 ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "PersonnelProfile" && item.ResourceId == "u-zhang"))
        throw new InvalidOperationException("人事档案、生命周期事件或审计日志未跨 DbContext 持久化。");
    if (await readDb.PersonnelCases.CountAsync(item => item.UserId == "u-zhang") != 4 ||
        await readDb.PersonnelCaseTasks.CountAsync(item => item.PersonnelCaseId == personnelCaseId) != 6 ||
        !await readDb.PersonnelCaseTasks.AnyAsync(item => item.Id == personnelTaskId && item.Status == PersonnelTaskStatuses.Completed) ||
        await readDb.PersonnelCaseAlertDeliveries.CountAsync(item => item.PersonnelCaseTaskId == personnelAlertTaskId) != 4 ||
        !await readDb.PersonnelCases.AnyAsync(item => item.Id == offboardingCaseId && item.Type == PersonnelCaseTypes.Offboarding && item.Status == PersonnelCaseStatuses.Completed) ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "PersonnelCase" && item.ResourceId == personnelCaseId.ToString() && item.Action == "PERSONNEL_CASE_COMPLETED"))
        throw new InvalidOperationException("员工办理单、任务状态或办结审计未跨 DbContext 持久化。");
    if (await readDb.Announcements.CountAsync() != 3 || await readDb.AnnouncementReads.CountAsync(item => item.AnnouncementId == announcementId && item.UserId == "u-zhang") != 1 || await readDb.AuditLogs.CountAsync(item => item.ResourceType == "Announcement") < 8)
        throw new InvalidOperationException("公告、已读确认或公告审计未跨 DbContext 持久化。");

    var reloaded = new LeaveService(data, readDb);
    var employee = data.GetEmployee("u-zhang");
    var request = reloaded.List(employee).SingleOrDefault(x => x.Id == requestId);
    if (request is null || request.Status != LeaveStatus.Completed || request.Tasks.Count != 1 || request.Tasks[0].AssigneeId != "u-wang")
        throw new InvalidOperationException("重建 DbContext 后未能恢复请假单或审批任务。");
    var persistedAnnualBalance = await readDb.LeaveBalances.SingleOrDefaultAsync(item => item.UserId == "u-zhang" && item.LeaveType == (int)LeaveType.Annual && item.Year == leaveDate.Year);
    if (request.BalanceYear != leaveDate.Year || persistedAnnualBalance is not { StatutoryEntitled: 10m, Adjustment: 1m, Entitled: 11m, Frozen: 0m, Used: 1m } ||
        !await readDb.AuditLogs.AnyAsync(item => item.Action == "LEAVE_BALANCE_ADJUSTED" && item.ResourceId == $"u-zhang:{LeaveType.Annual}:{leaveDate.Year}"))
        throw new InvalidOperationException("年假余额年度、法定额度、冻结或使用量未跨 DbContext 持久化。 ");
    if (request.ProcessDefinitionVersion != 1 || request.ProcessDefinitionCode != "LEAVE_DEFAULT")
        throw new InvalidOperationException("请假单绑定的流程版本快照未持久化。");
    var versionedRequest = reloaded.List(employee).SingleOrDefault(item => item.Id == versionedRequestId);
    if (versionedRequest?.ProcessDefinitionVersion != 2 || versionedRequest.Tasks.Count != 2 || versionedRequest.FlowInstances.Count != 2 || versionedRequest.CurrentFlowInstanceId != versionedRequest.FlowInstances[1].Id)
        throw new InvalidOperationException("最新发布流程版本及其新单据任务未跨 DbContext 保留。");
    if (versionedRequest.FlowInstances[0].Status != FlowInstanceStatus.Rejected || !versionedRequest.FlowInstances[0].Actions.Any(action => action.Action == FlowActionType.Rejected && action.Comment == "请补充工作交接") || versionedRequest.FlowInstances[1].Status != FlowInstanceStatus.Running)
        throw new InvalidOperationException("旧流程实例状态、驳回意见或重提实例未跨 DbContext 永久保留。");
    if (versionedRequest.Tasks.Any(task => task.FlowInstanceId != versionedRequest.CurrentFlowInstanceId))
        throw new InvalidOperationException("重提后的审批任务未绑定当前流程实例。");
    var delegatedRequest = reloaded.List(employee).SingleOrDefault(item => item.Id == delegatedRequestId);
    if (delegatedRequest?.Tasks.SingleOrDefault() is not { AssigneeId: "u-wang", OriginalAssigneeId: "u-li", DelegationId: not null })
        throw new InvalidOperationException("委托任务的实际审批人、原审批人或委托标识未持久化。");
    var postCancellationRequest = reloaded.List(employee).SingleOrDefault(item => item.Id == postCancellationRequestId);
    if (postCancellationRequest?.Tasks.FirstOrDefault() is not { AssigneeId: "u-li", DelegationId: null })
        throw new InvalidOperationException("委托取消后的任务分配未跨 DbContext 保留。");
    if (!reloaded.GetProcessedTasks(data.GetEmployee("u-wang")).Any(task => task.Id == request.Tasks[0].Id))
        throw new InvalidOperationException("重建 DbContext 后已处理请假任务未进入我的已办。");
    if (request.Attachments.SingleOrDefault() != fileId.ToString())
        throw new InvalidOperationException("请假单未能持久化关联的附件标识。");
    var fileService = new FileService(readDb, new TestWebHostEnvironment { ContentRootPath = fileRoot, WebRootPath = fileRoot }, new ConfigurationBuilder().AddInMemoryCollection().Build(), new TestFileMalwareScanner(FileScanStatus.Clean));
    var opened = fileService.Open(employee, fileId);
    if (!opened.IsSuccess || opened.Value!.Content.Length != "%PDF-1.4\ndemo-pdf-content"u8.Length)
        throw new InvalidOperationException("附件元数据或文件内容未能跨 DbContext 读取。");
    await opened.Value.Content.DisposeAsync();
    var openedPayment = fileService.Open(data.GetEmployee("u-chen"), paymentProofId);
    if (!openedPayment.IsSuccess || openedPayment.Value!.Content.Length != "%PDF-1.4\npayment-proof-content"u8.Length)
        throw new InvalidOperationException("付款凭证文件未能跨 DbContext 读取。");
    await openedPayment.Value.Content.DisposeAsync();
    if (!await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "File" && item.Action == "FILE_SCAN_BLOCKED") ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "File" && item.Action == "FILE_SCAN_FAILED") ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "File" && item.Action == "FILE_VALIDATION_BLOCKED"))
        throw new InvalidOperationException("文件伪装、恶意文件阻断或扫描服务异常未跨 DbContext 保留安全审计记录。");
    if (await readDb.AuditLogs.CountAsync(x => x.ResourceType == "LeaveRequest" && x.ResourceId == requestId.ToString()) < 2)
        throw new InvalidOperationException("请假创建与提交未生成审计记录。");
    var reloadedExpense = new ExpenseService(data, readDb).List(employee).SingleOrDefault(x => x.Id == expenseId);
    if (reloadedExpense is null || reloadedExpense.Status != ExpenseStatus.Completed || reloadedExpense.Items.Count != 1 || reloadedExpense.Tasks.Count != 2 || reloadedExpense.Tasks[0].AssigneeId != "u-wang")
        throw new InvalidOperationException("重建 DbContext 后未能恢复报销单、明细或审批任务。");
    if (reloadedExpense.Payment is not { PaymentMethod: "企业网银", TransactionNumber: "TX-PG-VALID" } persistedPayment || persistedPayment.ProofFile != paymentProofId.ToString())
        throw new InvalidOperationException("付款方式、流水号或付款凭证未跨 DbContext 持久化。");
    if (reloadedExpense.Tasks.Any(task => task.ExpenseClaimId != expenseId))
        throw new InvalidOperationException("报销审批任务未保留关联报销单标识，详情页无法跳转。");
    if (!new ExpenseService(data, readDb).GetProcessedTasks(data.GetEmployee("u-wang")).Any(task => task.Id == reloadedExpense.Tasks[0].Id))
        throw new InvalidOperationException("重建 DbContext 后已处理报销任务未进入我的已办。");
    if (reloadedExpense.FlowInstances.SingleOrDefault() is not { Status: FlowInstanceStatus.Completed } expenseInstance || !expenseInstance.Actions.Any(action => action.Action == FlowActionType.Transferred) || expenseInstance.Actions.Count(action => action.Action == FlowActionType.Approved) != 2)
        throw new InvalidOperationException("报销流程实例的转办或审批动作未跨 DbContext 保留。");
    if (await readDb.AuditLogs.CountAsync(x => x.ResourceType == "ExpenseClaim" && x.ResourceId == expenseId.ToString()) < 2)
        throw new InvalidOperationException("报销创建与提交未生成审计记录。");
    if (!await readDb.AuditLogs.AnyAsync(x => x.ResourceType == "ExpenseClaim" && x.ResourceId == expenseId.ToString() && x.Action == "EXPENSE_PAYMENT_REGISTERED"))
        throw new InvalidOperationException("付款登记未生成审计记录。");
    var reloadedTravelService = new TravelService(data, readDb);
    var reloadedTravel = reloadedTravelService.List(employee).SingleOrDefault(item => item.Id == travelId);
    if (reloadedTravel is null || reloadedTravel.Status != TravelStatus.Approved || reloadedTravel.Tasks.Count != 2 || reloadedTravel.Itinerary.SingleOrDefault()?.Destination != "上海" || reloadedTravel.CompanionNames.SingleOrDefault() != "孙悦" || reloadedTravel.Attachments.SingleOrDefault() != fileId.ToString())
        throw new InvalidOperationException("出差申请、行程、同行人、附件或审批任务未跨 DbContext 持久化。");
    if (reloadedTravel.FlowInstances.SingleOrDefault() is not { Status: FlowInstanceStatus.Completed } travelInstance || travelInstance.Actions.Count(action => action.Action == FlowActionType.Approved) != 2)
        throw new InvalidOperationException("出差流程实例或审批动作轨迹未跨 DbContext 持久化。");
    if (reloadedTravelService.List(employee).SingleOrDefault(item => item.Id == withdrawnTravelId)?.Status != TravelStatus.Withdrawn || await readDb.AuditLogs.CountAsync(item => item.ResourceType == "TravelRequest" && item.ResourceId == travelId.ToString()) < 4)
        throw new InvalidOperationException("出差撤回状态或审计记录未持久化。");
    var reloadedLinkedExpense = new ExpenseService(data, readDb).List(employee).SingleOrDefault(item => item.Id == linkedExpenseId);
    if (reloadedLinkedExpense?.TravelRequestId != travelId || reloadedLinkedExpense.TravelRequestNumber != reloadedTravel.Number)
        throw new InvalidOperationException("报销与出差申请的关联未跨 DbContext 持久化。");
    var reloadedPurchaseService = new PurchaseService(data, readDb);
    var reloadedPurchase = reloadedPurchaseService.Get(employee, purchaseId);
    if (!reloadedPurchase.IsSuccess || reloadedPurchase.Value!.Status != PurchaseStatus.Received || reloadedPurchase.Value.Order?.OrderNumber != purchaseOrderNumber || reloadedPurchase.Value.Receipt?.Result != "ALL_ACCEPTED")
        throw new InvalidOperationException("采购申请、订单、验收或最终状态未跨 DbContext 持久化。");
    if (reloadedPurchase.Value.Tasks.Count != 2 || reloadedPurchase.Value.Tasks.All(task => task.Status != FlowTaskStatus.Approved))
        throw new InvalidOperationException("采购审批任务未跨 DbContext 持久化为 Approved。");
    if (reloadedPurchase.Value.FlowInstances.SingleOrDefault() is not { Status: FlowInstanceStatus.Completed } purchaseFlowInstance || purchaseFlowInstance.Actions.Count < 3)
        throw new InvalidOperationException("采购流程实例或动作轨迹未跨 DbContext 持久化。");
    if (!await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "PurchaseRequest" && item.ResourceId == purchaseId.ToString() && item.Action == "PURCHASE_ORDERED") ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "PurchaseRequest" && item.ResourceId == purchaseId.ToString() && item.Action == "PURCHASE_RECEIVED"))
        throw new InvalidOperationException("采购下单或验收审计日志未跨 DbContext 持久化。");
    if (reloadedPurchaseService.Get(employee, withdrawnPurchaseId).Value?.Status != PurchaseStatus.Withdrawn)
        throw new InvalidOperationException("已撤回采购申请状态未跨 DbContext 持久化。");

    var reloadedSealService = new SealService(data, readDb);
    var reloadedInOffice = reloadedSealService.Get(employee, inOfficeSealId);
    if (!reloadedInOffice.IsSuccess || reloadedInOffice.Value!.Status != SealStatus.Executed || reloadedInOffice.Value.Execution?.OperatorName != "孙行政")
        throw new InvalidOperationException("在司用章申请、执行记录或最终状态未跨 DbContext 持久化。");
    if (reloadedInOffice.Value.Tasks.Count != 1 || reloadedInOffice.Value.Tasks[0].Status != FlowTaskStatus.Approved)
        throw new InvalidOperationException("在司用章单级任务未跨 DbContext 持久化为 Approved。");

    var reloadedOutOffice = reloadedSealService.Get(employee, outOfficeSealId);
    if (!reloadedOutOffice.IsSuccess || reloadedOutOffice.Value!.Status != SealStatus.Returned || reloadedOutOffice.Value.Execution?.OperatorName != "张晨" || reloadedOutOffice.Value.Return?.SealCondition != "INTACT")
        throw new InvalidOperationException("外带用章借出、归还或最终状态未跨 DbContext 持久化。");
    if (reloadedOutOffice.Value.Tasks.Count != 3 || reloadedOutOffice.Value.Tasks.All(task => task.Status != FlowTaskStatus.Approved))
        throw new InvalidOperationException("外带用章三级任务未跨 DbContext 持久化为 Approved。");
    if (reloadedOutOffice.Value.FlowInstances.SingleOrDefault() is not { Status: FlowInstanceStatus.Completed } sealFlowInstance || sealFlowInstance.Actions.Count < 4)
        throw new InvalidOperationException("外带用章流程实例或动作轨迹未跨 DbContext 持久化。");
    if (!await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "SealRequest" && item.ResourceId == inOfficeSealId.ToString() && item.Action == "SEAL_EXECUTED") ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "SealRequest" && item.ResourceId == outOfficeSealId.ToString() && item.Action == "SEAL_RETURNED"))
        throw new InvalidOperationException("用章执行或归还审计日志未跨 DbContext 持久化。");
    if (reloadedSealService.Get(employee, withdrawnSealId).Value?.Status != SealStatus.Withdrawn)
        throw new InvalidOperationException("已撤回用章申请状态未跨 DbContext 持久化。");

    var persistedDoc = await readDb.KnowledgeDocuments.SingleOrDefaultAsync(item => item.Id == testDocId);
    if (persistedDoc is not { Status: (int)DocumentStatus.Published, Version: 2, IsMustRead: true })
        throw new InvalidOperationException("制度文档发布状态、修订版本或必读标记未跨 DbContext 持久化。");
    if (await readDb.DocumentVersions.CountAsync(item => item.DocumentId == testDocId) != 2)
        throw new InvalidOperationException("制度文档两级版本快照历史未跨 DbContext 持久化。");
    if (await readDb.DocumentAcknowledgements.CountAsync(item => item.DocumentId == testDocId && item.UserId == employee.Id) != 2)
        throw new InvalidOperationException("员工两级版本签署记录未跨 DbContext 持久化。");
    if (!await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "KnowledgeDocument" && item.ResourceId == testDocId.ToString() && item.Action == "DOCUMENT_PUBLISHED") ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "KnowledgeDocument" && item.ResourceId == testDocId.ToString() && item.Action == "DOCUMENT_ACKNOWLEDGED"))
        throw new InvalidOperationException("制度发布或签署审计日志未跨 DbContext 持久化。");
    if (!await readDb.IdempotencyKeys.AnyAsync(x => x.Key == idempotencyKey))
        throw new InvalidOperationException("幂等键跨 DbContext 未保留。");
    if (!await readDb.WorkCalendarEntries.AnyAsync(x => x.Date == new DateOnly(2031, 1, 6) && !x.IsWorkingDay))
        throw new InvalidOperationException("企业工作日历覆盖未持久化。");
    var persistedAttendance = await readDb.AttendanceRecords.SingleOrDefaultAsync(item => item.Id == attendanceRecordId);
    var persistedAppeal = await readDb.AttendanceAppeals.SingleOrDefaultAsync(item => item.Id == attendanceAppealId);
    if (persistedAttendance is not { Status: AttendanceStatuses.Corrected, OriginalStatus: AttendanceStatuses.Late } || persistedAppeal is not { Status: AttendanceAppealStatuses.Approved } ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "AttendanceAppeal" && item.ResourceId == attendanceAppealId.ToString()))
        throw new InvalidOperationException("考勤记录、原始异常、申诉结果或审计日志未跨 DbContext 持久化。");
    var persistedMonthLock = await readDb.AttendanceMonthLocks.SingleOrDefaultAsync(item => item.Month == attendanceLockMonth);
    if (persistedMonthLock is not { IsLocked: true, Version: 3 } ||
        await readDb.AttendanceMonthSnapshots.CountAsync(item => item.Month == attendanceLockMonth) != 2 ||
        await readDb.AttendanceMonthSnapshots.AnyAsync(item => item.Month == attendanceLockMonth && (item.SnapshotHash.Length != 64 || item.RowCount <= 0)) ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "AttendanceMonthLock" && item.ResourceId == attendanceLockMonth.ToString("yyyy-MM") && item.Action == "ATTENDANCE_MONTH_LOCKED") ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "AttendanceMonthLock" && item.ResourceId == attendanceLockMonth.ToString("yyyy-MM") && item.Action == "ATTENDANCE_MONTH_UNLOCKED"))
        throw new InvalidOperationException("考勤月度封账状态、版本或审计记录未跨 DbContext 持久化。");
    var persistedContract = await readDb.EmploymentContracts.SingleOrDefaultAsync(item => item.Id == employmentContractId);
    var persistedRenewal = await readDb.EmploymentContracts.SingleOrDefaultAsync(item => item.Id == renewedEmploymentContractId);
    if (persistedContract is not { Status: EmploymentContractStatuses.Superseded } || persistedRenewal is not { Status: EmploymentContractStatuses.Active } || persistedRenewal.RenewalOfId != employmentContractId ||
        await readDb.EmploymentContractEvents.CountAsync(item => item.ContractId == employmentContractId) < 4 ||
        await readDb.ContractAlertAcknowledgements.CountAsync(item => item.ContractId == employmentContractId && item.ThresholdDays == 30) != 1 ||
        await readDb.ContractAlertDeliveries.CountAsync(item => item.ContractId == employmentContractId && item.ThresholdDays == 30) < 1 ||
        !await readDb.Notifications.AnyAsync(item => item.ResourceType == "EmploymentContract" && item.ResourceId == employmentContractId.ToString() && item.Type == "CONTRACT_EXPIRY_30") ||
        !await readDb.AuditLogs.AnyAsync(item => item.ResourceType == "EmploymentContract" && item.ResourceId == renewedEmploymentContractId.ToString() && item.Action == "EMPLOYMENT_CONTRACT_RENEWED"))
        throw new InvalidOperationException("劳动合同续签链、事件、预警确认或审计日志未跨 DbContext 持久化。");
    if (await readDb.ProcessDefinitions.CountAsync(item => item.Code == "LEAVE_DEFAULT") != 2 || await readDb.ProcessDefinitions.CountAsync(item => item.Code == "LEAVE_DEFAULT" && item.Status == (int)ProcessDefinitionStatus.Published) != 1)
        throw new InvalidOperationException("流程定义版本归档和唯一当前发布版本未持久化。");
    var scopedDefinition = await readDb.ProcessDefinitions.SingleOrDefaultAsync(item => item.Id == scopedProcessId);
    if (scopedDefinition is not { Priority: 20, Status: (int)ProcessDefinitionStatus.Published } ||
        await readDb.ProcessScopes.CountAsync(item => item.ProcessDefinitionId == scopedProcessId) != 2 ||
        !await readDb.ProcessScopes.AnyAsync(item => item.ProcessDefinitionId == scopedProcessId && item.ScopeType == "Department" && item.Value == "engineering") ||
        !await readDb.ProcessScopes.AnyAsync(item => item.ProcessDefinitionId == scopedProcessId && item.ScopeType == "LeaveType" && item.Value == "Sick"))
        throw new InvalidOperationException("流程优先级或部门/假别适用范围未跨 DbContext 持久化。");
    var scopedRequest = reloaded.List(employee).SingleOrDefault(item => item.Id == scopedRequestId);
    if (scopedRequest?.ProcessDefinitionCode != "LEAVE_ENGINEERING_SICK" || scopedRequest.ProcessDefinitionVersion != 1)
        throw new InvalidOperationException("适用范围流程选择结果未持久化到请假单快照。");
    if (await readDb.FlowDelegations.CountAsync(item => item.OwnerId == "u-li" && item.Status == (int)FlowDelegationStatus.Cancelled) != 1 || await readDb.AuditLogs.CountAsync(item => item.ResourceType == "FlowDelegation") < 2)
        throw new InvalidOperationException("审批委托取消状态或审计记录未持久化。");
    if (await readDb.FlowInstances.CountAsync() < 2 || await readDb.FlowActions.CountAsync() < 4)
        throw new InvalidOperationException("独立流程实例或动作轨迹未写入持久化表。");
    var reloadedCopies = new FlowCopyService(data, readDb).ListMine(data.GetEmployee("u-sun"));
    if (reloadedCopies.SingleOrDefault(item => item.Id == leaveCopyId)?.ReadAt is null || reloadedCopies.SingleOrDefault(item => item.Id == expenseCopyId)?.ReadAt is not null)
        throw new InvalidOperationException("抄送事项及其独立已阅/未读状态未跨 DbContext 保留。");
    if (!new LeaveService(data, readDb).Get(data.GetEmployee("u-sun"), requestId).IsSuccess || !new ExpenseService(data, readDb).Get(data.GetEmployee("u-sun"), expenseId).IsSuccess)
        throw new InvalidOperationException("抄送人的请假或报销只读数据权限未跨 DbContext 保留。");

    var workDirectory = new DemoData(readDb);
    var workNotifications = new NotificationService(readDb);
    var workCopies = new FlowCopyService(workDirectory, readDb);
    var workService = new WorkItemService(
        readDb,
        workDirectory,
        new LeaveService(workDirectory, readDb),
        new ExpenseService(workDirectory, readDb),
        new TravelService(workDirectory, readDb),
        new PurchaseService(workDirectory, readDb),
        new SealService(workDirectory, readDb),
        workCopies,
        new AnnouncementService(readDb, workDirectory),
        new KnowledgeDocumentService(workDirectory, readDb),
        new EmploymentContractService(readDb, workDirectory, fileService, workNotifications, new ConfigurationBuilder().Build()),
        new FlowInstanceService(readDb));
    var employeeInitiated = workService.List(workDirectory.GetEmployee("u-zhang"), new WorkItemQuery(WorkItemTabs.Initiated, null, null, null, null, null, null, null, 1, 100));
    if (employeeInitiated.Items.All(item => item.ResourceId != requestId) || employeeInitiated.Items.Any(item => item.ApplicantId != "u-zhang"))
        throw new InvalidOperationException("事项中心未汇总员工本人发起的请假，或泄露了他人发起事项。");
    var managerPending = workService.List(workDirectory.GetEmployee("u-li"), new WorkItemQuery(WorkItemTabs.Pending, null, null, null, null, null, null, null, 1, 100));
    if (managerPending.Summary.PendingCount < managerPending.Items.Count || managerPending.Items.Any(item => !item.CanProcess) || managerPending.Items.Where(item => item.Category == WorkItemCategories.Approval).Any(item => item.DueAt is null))
        throw new InvalidOperationException("事项中心待办汇总、当前节点或可处理标记不正确。");
    var employeeRisks = workService.List(workDirectory.GetEmployee("u-zhang"), new WorkItemQuery(WorkItemTabs.Risk, null, null, null, null, null, null, null, 1, 100));
    if (employeeRisks.Items.Any(item => item.ApplicantId != "u-zhang"))
        throw new InvalidOperationException("普通员工在事项中心看到了权限范围外的劳动合同风险。");
    var hrReading = workService.List(workDirectory.GetEmployee("u-sun"), new WorkItemQuery(WorkItemTabs.Reading, null, null, null, null, null, null, null, 1, 1));
    if (hrReading.Total < hrReading.Items.Count || hrReading.PageSize != 1 || hrReading.Summary.PendingReadCount < 1)
        throw new InvalidOperationException("事项中心阅读事项未按服务端分页，或未汇总未读抄送、公告和制度。");
    var auditService = new AuditService(readDb, data);
    var auditResult = auditService.List(data.GetEmployee("u-admin"), new AuditLogQuery("提交", "u-zhang", "LeaveRequest", null, null, 1, 1));
    if (!auditResult.IsSuccess || auditResult.Value is null || auditResult.Value.Total < 1 || auditResult.Value.Items.Count != 1 || auditResult.Value.PageSize != 1)
        throw new InvalidOperationException("系统管理员无法按操作人、资源类型和关键字分页查询审计日志。");
    var deniedAuditResult = auditService.List(employee, new AuditLogQuery(null, null, null, null, null, 1, 20));
    if (deniedAuditResult.IsSuccess || deniedAuditResult.Code != "AUTH_002")
        throw new InvalidOperationException("普通员工可以越权查询审计日志。");
    var manager = data.GetEmployee("u-li");
    var notificationService = new NotificationService(readDb);
    var notification = notificationService.List(manager).FirstOrDefault(item => item.ResourceId == requestId.ToString());
    if (notification is null || !notificationService.MarkRead(manager, notification.Id).IsSuccess)
        throw new InvalidOperationException("待办通知未生成或无法标记已读。");
    if (notificationService.List(data.GetEmployee("u-sun")).Count(item => item.Type == "FLOW_COPY" && (item.ResourceId == requestId.ToString() || item.ResourceId == expenseId.ToString())) != 2)
        throw new InvalidOperationException("请假或报销流程完成后未向抄送人发送站内通知。");
}

const int performanceRosterSize = 250;
await using (var performanceSetupDb = new OaDbContext(options))
{
    var baseHireDate = new DateOnly(2026, 8, 1);
    for (var index = 0; index < performanceRosterSize; index++)
    {
        var userId = $"u-roster-perf-{index:000}";
        performanceSetupDb.Users.Add(new UserRecord
        {
            Id = userId,
            TenantId = IdentityDefaults.TenantId,
            Name = $"性能员工 {index:000}",
            DepartmentId = "engineering",
            ManagerId = "u-li",
            Status = "ACTIVE"
        });
        performanceSetupDb.PersonnelProfiles.Add(new PersonnelProfileRecord
        {
            UserId = userId,
            TenantId = IdentityDefaults.TenantId,
            EmployeeNumber = $"PERF-{index:000}",
            EmploymentType = EmploymentTypes.FullTime,
            PersonnelStatus = PersonnelStatuses.Active,
            HireDate = baseHireDate.AddDays(-index)
        });
    }
    await performanceSetupDb.SaveChangesAsync();
}
var rosterCommandCounter = new CountingCommandInterceptor();
var performanceOptions = new DbContextOptionsBuilder<OaDbContext>().UseNpgsql(connectionString).AddInterceptors(rosterCommandCounter).Options;
await using (var performanceDb = new OaDbContext(performanceOptions))
{
    var performanceDirectory = new DemoData(performanceDb);
    rosterCommandCounter.Reset();
    var rosterPage = new PersonnelService(performanceDb, performanceDirectory).List(performanceDirectory.GetEmployee("u-sun"), "性能员工", "engineering", PersonnelStatuses.Active, EmploymentTypes.FullTime, 3, 25);
    if (!rosterPage.IsSuccess || rosterPage.Value is not { Total: performanceRosterSize, Page: 3, PageSize: 25 } pageValue || pageValue.Items.Count != 25 || pageValue.Items[0].UserId != "u-roster-perf-050")
        throw new InvalidOperationException("大花名册未在数据库中正确筛选、排序和分页。");
    if (rosterCommandCounter.CommandCount != 2 || rosterCommandCounter.Commands.All(command => !command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException($"花名册查询发生全量加载或 N+1 查询；预期 2 条含 LIMIT 的 SQL，实际 {rosterCommandCounter.CommandCount} 条。");

    rosterCommandCounter.Reset();
    var literalWildcard = new PersonnelService(performanceDb, performanceDirectory).List(performanceDirectory.GetEmployee("u-sun"), "性能员工%", "engineering", null, null, 1, 20);
    if (!literalWildcard.IsSuccess || literalWildcard.Value!.Total != 0 || rosterCommandCounter.CommandCount != 2)
        throw new InvalidOperationException("花名册关键字未按字面量处理 SQL LIKE 通配符，或空结果查询次数异常。");
}

Console.WriteLine("PostgreSQL persistence integration passed.");

static async Task InstallNotificationFailureTriggerAsync(string connectionString, Guid resourceId)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand($$"""
        CREATE OR REPLACE FUNCTION oa_test_reject_selected_notification() RETURNS trigger AS $body$
        BEGIN
            IF NEW."ResourceType" = 'LeaveRequest' AND NEW."ResourceId" = '{{resourceId}}' THEN
                RAISE EXCEPTION 'injected notification failure';
            END IF;
            RETURN NEW;
        END;
        $body$ LANGUAGE plpgsql;
        CREATE TRIGGER oa_test_reject_selected_notification
        BEFORE INSERT ON notification
        FOR EACH ROW EXECUTE FUNCTION oa_test_reject_selected_notification();
        """, connection);
    await command.ExecuteNonQueryAsync();
}

static async Task RemoveNotificationFailureTriggerAsync(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("DROP TRIGGER IF EXISTS oa_test_reject_selected_notification ON notification; DROP FUNCTION IF EXISTS oa_test_reject_selected_notification();", connection);
    await command.ExecuteNonQueryAsync();
}

file sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "Oa.Postgres.Tests";
    public string WebRootPath { get; set; } = string.Empty;
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = string.Empty;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
}

file sealed class CountingCommandInterceptor : DbCommandInterceptor
{
    private readonly List<string> commands = [];
    public int CommandCount => commands.Count;
    public IReadOnlyList<string> Commands => commands;

    public void Reset() => commands.Clear();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        commands.Add(command.CommandText);
        return result;
    }
}

file sealed class TestFileMalwareScanner(FileScanStatus result) : IFileMalwareScanner
{
    public string Provider => "test";
    public bool Enabled => true;
    public Task<FileScanResult> ScanAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileScanResult(result, result == FileScanStatus.Infected ? "Test.Signature" : null));
    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) => Task.FromResult(result != FileScanStatus.Unavailable);
}

file sealed class TestClamAvServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource cancellation = new();
    private readonly string scanResponse;
    private readonly int connectionCount;
    private readonly Task serverTask;
    private long streamedBytes;

    public TestClamAvServer(string scanResponse, int connectionCount)
    {
        this.scanResponse = scanResponse;
        this.connectionCount = connectionCount;
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        serverTask = ServeAsync();
    }

    public int Port { get; }
    public long StreamedBytes => Interlocked.Read(ref streamedBytes);

    private async Task ServeAsync()
    {
        for (var connection = 0; connection < connectionCount; connection++)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
            await using var stream = client.GetStream();
            var command = await ReadCommandAsync(stream, cancellation.Token);
            if (command == "zPING")
            {
                await stream.WriteAsync("PONG\0"u8.ToArray(), cancellation.Token);
                continue;
            }
            if (command != "zINSTREAM") throw new InvalidOperationException($"未知 ClamAV 测试命令：{command}");
            var lengthBuffer = new byte[sizeof(int)];
            while (true)
            {
                await ReadExactlyAsync(stream, lengthBuffer, cancellation.Token);
                var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
                if (length == 0) break;
                var content = new byte[length];
                await ReadExactlyAsync(stream, content, cancellation.Token);
                Interlocked.Add(ref streamedBytes, length);
            }
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes($"{scanResponse}\0"), cancellation.Token);
        }
    }

    private static async Task<string> ReadCommandAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var value = new byte[1];
        while (await stream.ReadAsync(value, cancellationToken) == 1 && value[0] != 0) buffer.WriteByte(value[0]);
        return System.Text.Encoding.ASCII.GetString(buffer.ToArray());
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    public async ValueTask DisposeAsync()
    {
        listener.Stop();
        cancellation.Cancel();
        try { await serverTask; }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException) { }
        cancellation.Dispose();
    }
}
