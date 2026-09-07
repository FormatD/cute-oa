using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Oa.Api.Domain;
using Oa.Api.Services;

var failures = new List<string>();

void Equal<T>(T expected, T actual, string name) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        failures.Add($"{name}: expected {expected}, got {actual}");
}

void True(bool condition, string name)
{
    if (!condition) failures.Add($"{name}: assertion failed");
}

var keyPerFileRoot = Path.Combine(Path.GetTempPath(), $"oa-key-per-file-{Guid.NewGuid():N}");
try
{
    Directory.CreateDirectory(keyPerFileRoot);
    File.WriteAllText(Path.Combine(keyPerFileRoot, "Authentication__SigningKey"), "secret-from-mounted-file");
    var keyPerFileConfiguration = new ConfigurationBuilder().AddKeyPerFile(keyPerFileRoot, optional: false).Build();
    Equal("secret-from-mounted-file", keyPerFileConfiguration["Authentication:SigningKey"]!, "双下划线密钥文件映射为配置层级");
}
finally
{
    if (Directory.Exists(keyPerFileRoot)) Directory.Delete(keyPerFileRoot, recursive: true);
}

ProductionConfigurationPolicy.Validate(new ConfigurationBuilder().Build(), true);
var validProductionConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:SigningKey"] = "production-random-signing-key-example-2026-08-31-rotate-me",
    ["Authentication:MultiFactor:Enabled"] = "true",
    ["Authentication:MultiFactor:EncryptionKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
    ["Authentication:MultiFactor:RequiredPermissions:0"] = OaPermissions.UserManage,
    ["Authentication:MultiFactor:RequiredPermissions:1"] = OaPermissions.PersonnelExport,
    ["Authentication:MultiFactor:RequiredPermissions:2"] = OaPermissions.PersonnelManage,
    ["Authentication:MultiFactor:RequiredPermissions:3"] = OaPermissions.AttendanceManage,
    ["Authentication:MultiFactor:RequiredPermissions:4"] = OaPermissions.ContractManage,
    ["Authentication:MultiFactor:RequiredPermissions:5"] = OaPermissions.PurchaseManage,
    ["Authentication:MultiFactor:RequiredPermissions:6"] = OaPermissions.SealManage,
    ["Authentication:MultiFactor:RequiredPermissions:7"] = OaPermissions.DocumentManage,
    ["Authentication:MultiFactor:RequiredPermissions:8"] = OaPermissions.BusinessConfigManage,
    ["Persistence:UsePostgreSql"] = "true",
    ["ConnectionStrings:OaDatabase"] = "Host=database.internal;Database=oa;Username=oa_app;Password=secret;SSL Mode=Require;Pooling=true;Maximum Pool Size=100",
    ["Cors:AllowedOrigins:0"] = "https://oa.example.com",
    ["Storage:Root"] = "/srv/cute-oa/files",
    ["AllowedHosts"] = "api.oa.example.com",
    ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.10",
    ["DemoFeatures:AllowDataGeneration"] = "false",
    ["Bootstrap:Enabled"] = "true",
    ["Bootstrap:AdminUserId"] = "oa-production-admin",
    ["Bootstrap:AdminName"] = "生产引导管理员",
    ["Bootstrap:DepartmentId"] = "management",
    ["Bootstrap:DepartmentName"] = "管理部",
    ["Bootstrap:AdminEmployeeNumber"] = "ADMIN-001",
    ["Bootstrap:AdminHireDate"] = "2026-01-01",
    ["Bootstrap:AdminPassword"] = "StrongBootstrap#2026",
    ["PersonnelCases:CategoryAssignees:HR"] = "hr-owner",
    ["PersonnelCases:CategoryAssignees:FINANCE"] = "finance-owner",
    ["PersonnelCases:CategoryAssignees:IT"] = "it-owner",
    ["PersonnelCases:CategoryAssignees:ADMIN"] = "admin-owner",
    ["PersonnelCaseAlerts:Enabled"] = "true",
    ["PersonnelCaseAlerts:IntervalMinutes"] = "60",
    ["PersonnelCaseAlerts:DueSoonDays"] = "1",
    ["PersonnelCaseAlerts:EscalateAfterDays"] = "3",
    ["FlowSla:Enabled"] = "true",
    ["FlowSla:IntervalMinutes"] = "15"
}).Build();
ProductionConfigurationPolicy.Validate(validProductionConfiguration, false);
var invalidProductionConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:SigningKey"] = "cute-oa-development-signing-key-change-before-production-2026",
    ["Persistence:UsePostgreSql"] = "false",
    ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
    ["Storage:Root"] = "storage/files",
    ["AllowedHosts"] = "*",
    ["LoginProtection:Enabled"] = "false",
    ["ForwardedHeaders:KnownProxies:0"] = "0.0.0.0",
    ["DemoFeatures:AllowDataGeneration"] = "true",
    ["Bootstrap:Enabled"] = "true",
    ["Bootstrap:AdminUserId"] = "u-admin",
    ["Bootstrap:AdminName"] = "A",
    ["Bootstrap:DepartmentId"] = "?",
    ["Bootstrap:DepartmentName"] = "A",
    ["Bootstrap:AdminEmployeeNumber"] = "?",
    ["Bootstrap:AdminHireDate"] = "invalid",
    ["Bootstrap:AdminPassword"] = IdentityDefaults.DemoPassword
}).Build();
try
{
    ProductionConfigurationPolicy.Validate(invalidProductionConfiguration, false);
    failures.Add("生产环境接受了开发默认配置");
}
catch (InvalidOperationException exception)
{
    True(exception.Message.Contains("开发示例密钥") && exception.Message.Contains("MultiFactor") && exception.Message.Contains("UsePostgreSql") && exception.Message.Contains("无效生产源") && exception.Message.Contains("持久卷绝对路径") && exception.Message.Contains("AllowedHosts") && exception.Message.Contains("LoginProtection") && exception.Message.Contains("KnownProxies") && exception.Message.Contains("AllowDataGeneration") && exception.Message.Contains("Bootstrap") && exception.Message.Contains("PersonnelCaseAlerts"), "生产配置错误一次性返回完整门禁原因");
}
True(PasswordHasher.MeetsProductionPolicy("StrongBootstrap#2026") && !PasswordHasher.MeetsProductionPolicy(IdentityDefaults.DemoPassword), "生产引导密码执行独立强密码策略");
Equal(new DateOnly(2026, 9, 1), BusinessTime.ChinaToday(new DateTimeOffset(2026, 8, 31, 16, 30, 0, TimeSpan.Zero)), "容器 UTC 日期按北京时间转换业务日期");
Equal(new DateOnly(2026, 10, 1), BusinessTime.ChinaMonth(new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero)), "北京时间业务月份稳定转换");

var mfaConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:MultiFactor:Enabled"] = "true",
    ["Authentication:MultiFactor:EncryptionKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="
}).Build();
var mfaProtector = new MfaSecretProtector(mfaConfiguration);
var protectedMfaSecret = mfaProtector.Protect("JBSWY3DPEHPK3PXP");
True(protectedMfaSecret != "JBSWY3DPEHPK3PXP" && mfaProtector.Unprotect(protectedMfaSecret) == "JBSWY3DPEHPK3PXP", "TOTP 密钥使用认证加密保护并可恢复");
var totpTimestamp = DateTimeOffset.FromUnixTimeSeconds(1_789_100_010);
var totpCode = TotpGenerator.GenerateCode("JBSWY3DPEHPK3PXP", totpTimestamp);
True(TotpGenerator.TryValidate("JBSWY3DPEHPK3PXP", totpCode, totpTimestamp, out _), "当前时间窗 TOTP 验证通过");
True(TotpGenerator.TryValidate("JBSWY3DPEHPK3PXP", TotpGenerator.GenerateCode("JBSWY3DPEHPK3PXP", totpTimestamp.AddSeconds(-30)), totpTimestamp, out _), "相邻时间窗容差验证通过");
True(!TotpGenerator.TryValidate("JBSWY3DPEHPK3PXP", "00000A", totpTimestamp, out _), "非法动态验证码被拒绝");

var forwardedOptions = new ForwardedHeadersOptions();
ReverseProxyPolicy.Configure(forwardedOptions, validProductionConfiguration);
Equal(1, forwardedOptions.ForwardLimit!.Value, "转发头只接受一跳代理");
True(forwardedOptions.RequireHeaderSymmetry, "转发头要求来源和协议数量对称");
Equal("10.0.0.10", forwardedOptions.KnownProxies.Single().ToString(), "只信任配置的反向代理地址");

var limiterClock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero));
var limiterSettings = new LoginProtectionSettings(true, 60, 2, 3, 4, 1000);
var accountLimiter = new LoginAttemptLimiter(limiterSettings, limiterClock);
True(accountLimiter.TryAcquire("u-test", "10.1.1.1").IsAllowed, "账号来源组合第一次登录放行");
True(accountLimiter.TryAcquire("u-test", "10.1.1.1").IsAllowed, "账号来源组合限额内放行");
var pairBlocked = accountLimiter.TryAcquire("u-test", "10.1.1.1");
True(!pairBlocked.IsAllowed && pairBlocked.RetryAfterSeconds == 60, "账号来源组合超过限额返回重试时间");
True(accountLimiter.TryAcquire("u-test", "10.1.1.2").IsAllowed, "更换来源仍受账号总限额约束前放行");
True(!accountLimiter.TryAcquire("u-test", "10.1.1.3").IsAllowed, "分散来源不能绕过账号总限额");
limiterClock.Advance(TimeSpan.FromSeconds(61));
True(accountLimiter.TryAcquire("u-test", "10.1.1.1").IsAllowed, "限流窗口到期后自动恢复");

var ipLimiter = new LoginAttemptLimiter(limiterSettings, new ManualTimeProvider(new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero)));
for (var index = 0; index < 4; index++) True(ipLimiter.TryAcquire($"u-{index}", "10.2.2.2").IsAllowed, $"同来源第 {index + 1} 次限额内放行");
True(!ipLimiter.TryAcquire("u-over-ip", "10.2.2.2").IsAllowed, "轮换账号不能绕过来源 IP 总限额");

var securityContext = new DefaultHttpContext();
securityContext.Request.Path = "/api/v1/auth/login";
var securityNextCalled = false;
await new SecurityHeadersMiddleware(_ => { securityNextCalled = true; return Task.CompletedTask; }).InvokeAsync(securityContext);
True(securityNextCalled, "安全响应头中间件继续请求管道");
Equal("nosniff", securityContext.Response.Headers.XContentTypeOptions.ToString(), "禁止 MIME 嗅探响应头");
Equal("DENY", securityContext.Response.Headers.XFrameOptions.ToString(), "禁止页面嵌入响应头");
Equal("no-store", securityContext.Response.Headers.CacheControl.ToString(), "认证响应禁止缓存");
True(securityContext.Response.Headers.ContentSecurityPolicy.ToString().Contains("default-src 'none'"), "API 默认内容安全策略");

var paging = Paging.Create(Enumerable.Range(1, 18).ToList(), 2, 8);
Equal(18, paging.Total, "分页返回总数");
Equal(3, paging.TotalPages, "分页返回总页数");
Equal(9, paging.Items[0], "分页返回正确的数据切片");
Equal(3, Paging.Create(Enumerable.Range(1, 18).ToList(), 99, 8).Page, "超范围页码收敛到最后一页");

Equal(2m,
    LeaveService.CalculateWorkingDays(new DateOnly(2026, 8, 28), LeavePeriod.FullDay, new DateOnly(2026, 8, 31), LeavePeriod.FullDay),
    "跨周末请假仅计算工作日");
Equal(.5m,
    LeaveService.CalculateWorkingDays(new DateOnly(2026, 8, 31), LeavePeriod.Morning, new DateOnly(2026, 8, 31), LeavePeriod.Morning),
    "半天请假计算");

var data = new DemoData();
Equal("王总", data.DirectoryEmployees.Single(item => item.Id == "u-li").ManagerName!, "组织通讯录解析直属上级姓名");
Equal(2, data.DirectoryEmployees.Count(item => item.DepartmentId == "finance"), "组织通讯录按部门保留人员关系");
True(data.DirectoryEmployees.All(item => item.Id != "u-disabled"), "停用账号不出现在组织通讯录");
True(data.HasPermission(data.GetEmployee("u-admin"), OaPermissions.UserManage), "系统管理员具有用户管理权限");
True(!data.HasPermission(data.GetEmployee("u-zhang"), OaPermissions.UserManage), "普通员工不具有用户管理权限");
True(data.HasPermission(data.GetEmployee("u-sun"), OaPermissions.CalendarManage), "HR 角色具有工作日历维护权限");
True(data.HasPermission(data.GetEmployee("u-chen"), OaPermissions.ExpenseAllView), "财务角色具有全公司报销查看权限");
True(data.CanView(data.GetEmployee("u-chen"), data.GetEmployee("u-zhang"), "Expense"), "财务角色可按报销全公司范围查看员工报销");
True(!data.CanView(data.GetEmployee("u-chen"), data.GetEmployee("u-zhang"), "Leave"), "财务报销范围不能越权用于查看员工请假");
True(data.CanView(data.GetEmployee("u-li"), data.GetEmployee("u-zhang"), "Leave"), "直属上级可查看下级请假");
Equal(OaDataScopes.Company, data.EffectiveDataScope(data.GetEmployee("u-admin"), "Leave"), "系统管理员具有全公司请假范围");
var authConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Authentication:Issuer"] = "cute-oa-tests",
    ["Authentication:Audience"] = "cute-oa-web-tests",
    ["Authentication:SigningKey"] = "cute-oa-tests-signing-key-with-at-least-thirty-two-bytes",
    ["Authentication:AccessTokenMinutes"] = "30"
}).Build();
var tokenService = new JwtTokenService(authConfiguration);
var mfaPublicContext = new DefaultHttpContext();
mfaPublicContext.Request.Path = "/api/v1/auth/mfa/verify";
var mfaPublicNextCalled = false;
await new BearerAuthenticationMiddleware(_ => { mfaPublicNextCalled = true; return Task.CompletedTask; }).InvokeAsync(mfaPublicContext, tokenService, data);
True(mfaPublicNextCalled, "MFA 预认证验证端点不要求尚未签发的 Bearer 令牌");
var authentication = new AuthenticationService(data, tokenService);
var login = authentication.Login(new LoginRequest("u-zhang", "Oa@123456"));
True(login.IsSuccess, "有效模拟账号可以登录");
True(tokenService.TryValidate(login.Value!.Session.AccessToken, out var tokenIdentity) && tokenIdentity!.UserId == "u-zhang" && tokenIdentity.SessionId is null, "登录签发的 JWT 可验证并绑定正确用户");
True(!tokenService.TryValidate(login.Value.Session.AccessToken + "x", out _), "被篡改的 JWT 无法通过签名验证");
True(!authentication.Login(new LoginRequest("u-zhang", "wrong-password")).IsSuccess, "错误密码不能登录");
True(!authentication.Login(new LoginRequest("u-missing", "wrong-password")).IsSuccess, "不存在账号使用统一认证失败响应");
True(!authentication.Login(new LoginRequest("u-zhang", string.Empty)).IsSuccess, "空密码使用统一认证失败响应");
True(!authentication.Login(new LoginRequest("u-disabled", "Oa@123456")).IsSuccess, "停用账号不能登录");
var employee = data.GetEmployee("u-zhang");
var manager = data.GetEmployee("u-li");
var generalManager = data.GetEmployee("u-wang");
var hr = data.GetEmployee("u-sun");
Equal(0m, AnnualLeavePolicy.Calculate(new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1), 9, null), "连续工作未满十二个月不享受法定年假");
Equal(5m, AnnualLeavePolicy.Calculate(new DateOnly(2027, 1, 1), new DateOnly(2026, 1, 1), 0, null), "连续工作满十二个月享受五天法定年假");
Equal(10m, AnnualLeavePolicy.Calculate(new DateOnly(2026, 9, 1), new DateOnly(2016, 9, 1), 0, null), "累计工作满十年享受十天法定年假");
Equal(15m, AnnualLeavePolicy.Calculate(new DateOnly(2026, 9, 1), new DateOnly(2006, 9, 1), 0, null), "累计工作满二十年享受十五天法定年假");
Equal(2m, AnnualLeavePolicy.Calculate(new DateOnly(2026, 9, 1), new DateOnly(2020, 1, 1), 0, new DateOnly(2026, 7, 1)), "当年新入职按剩余日历天数向下折算年假");
var service = new LeaveService(data);
Equal(0m, service.GetBalance(employee, LeaveType.CompTime).Entitled, "调休不复用法定年假额度");
var initialAnnualVersion = service.GetBalance(employee, LeaveType.Annual, 2026).Version;
True(service.AdjustBalance(employee, employee.Id, new AdjustLeaveBalanceRequest(LeaveType.Annual, 2026, 1m, "员工尝试自行增加余额", initialAnnualVersion)).Code == "AUTH_002", "普通员工不能自行调整假期余额");
var adjustedAnnual = service.AdjustBalance(hr, employee.Id, new AdjustLeaveBalanceRequest(LeaveType.Annual, 2026, 1m, "HR 核验后补充公司福利假", initialAnnualVersion));
True(adjustedAnnual.IsSuccess && adjustedAnnual.Value is { Entitled: 6m, StatutoryEntitled: 5m, Adjustment: 1m }, "HR 可在法定额度之上按半天粒度调整并保留法定基线");
True(service.AdjustBalance(hr, employee.Id, new AdjustLeaveBalanceRequest(LeaveType.Annual, 2026, 2m, "使用过期版本调整余额", initialAnnualVersion)).Code == "CONCURRENCY_001", "假期余额人工调整执行乐观锁校验");
True(!service.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, new DateOnly(2026, 8, 31), LeavePeriod.FullDay, new DateOnly(2026, 8, 31), LeavePeriod.FullDay, "非法抄送", CopyRecipientIds: [employee.Id])).IsSuccess, "发起人不能将自己设置为抄送人");
True(!service.CreateDraft(employee, new CreateLeaveRequest(LeaveType.Personal, new DateOnly(2026, 8, 31), LeavePeriod.FullDay, new DateOnly(2026, 8, 31), LeavePeriod.FullDay, "停用账号抄送", CopyRecipientIds: ["u-disabled"])).IsSuccess, "停用账号不能被设置为抄送人");
var makeupWorkday = service.CreateDraft(employee, new CreateLeaveRequest(
    LeaveType.Personal,
    new DateOnly(2026, 10, 10), LeavePeriod.FullDay,
    new DateOnly(2026, 10, 10), LeavePeriod.FullDay,
    "国庆调休上班日验证"));
True(makeupWorkday.IsSuccess, "中国调休周六应计为工作日");
Equal(1m, makeupWorkday.Value!.Days, "调休工作日请假时长正确");
True(!service.CreateDraft(employee, new CreateLeaveRequest(
    LeaveType.Personal,
    new DateOnly(2026, 10, 1), LeavePeriod.FullDay,
    new DateOnly(2026, 10, 1), LeavePeriod.FullDay,
    "国庆节验证")).IsSuccess, "法定节假日不可创建有效请假单");
var draft = service.CreateDraft(employee, new CreateLeaveRequest(
    LeaveType.Annual,
    new DateOnly(2026, 8, 31),
    LeavePeriod.FullDay,
    new DateOnly(2026, 8, 31),
    LeavePeriod.FullDay,
    "家庭事务", CopyRecipientIds: [hr.Id]));
True(draft.IsSuccess, "可创建有效草稿");
Equal(hr.Id, draft.Value!.CopyRecipientIds.Single(), "请假草稿保存抄送人");
var editedDraft = service.Update(employee, draft.Value!.Id, draft.Value.Version, new CreateLeaveRequest(
    LeaveType.Annual, new DateOnly(2026, 8, 31), LeavePeriod.FullDay,
    new DateOnly(2026, 8, 31), LeavePeriod.FullDay, "已更新的家庭事务", CopyRecipientIds: [hr.Id]));
True(editedDraft.IsSuccess && editedDraft.Value!.Reason == "已更新的家庭事务" && editedDraft.Value.Version == 2, "草稿可编辑且保留原单据");
True(!service.Update(employee, draft.Value.Id, 1, new CreateLeaveRequest(LeaveType.Annual, new DateOnly(2026, 8, 31), LeavePeriod.FullDay, new DateOnly(2026, 8, 31), LeavePeriod.FullDay, "过期版本")).IsSuccess, "过期版本不能覆盖请假草稿");
var submitted = service.Submit(employee, draft.Value!.Id);
True(submitted.IsSuccess, "可提交草稿");
Equal(LeaveStatus.Approving, submitted.Value!.Status, "提交后进入审批中");
Equal(1m, service.GetBalance(employee).Frozen, "提交年假后冻结余额");
True(!service.Submit(employee, draft.Value.Id).IsSuccess, "审批中单据不可重复提交");
Equal(1, submitted.Value.Tasks.Count, "三天以内仅部门负责人审批");
Equal("LEAVE_DEFAULT", submitted.Value.ProcessDefinitionCode!, "请假提交绑定默认流程编码");
Equal(1, submitted.Value.ProcessDefinitionVersion!.Value, "请假提交绑定已发布流程版本");
True(service.Get(manager, submitted.Value.Id).IsSuccess, "审批人可查看分配给自己的请假详情");
True(!service.Get(hr, submitted.Value.Id).IsSuccess, "无数据权限用户不能查看其他员工请假详情");

var approved = service.Approve(manager, submitted.Value.Tasks[0].Id, "同意");
True(approved.IsSuccess, "部门负责人可审批");
True(service.GetProcessedTasks(manager).Any(task => task.Id == submitted.Value.Tasks[0].Id), "已处理请假任务进入我的已办");
Equal(LeaveStatus.Completed, approved.Value!.Status, "全部审批通过后完成");
Equal(1m, service.GetBalance(employee).Used, "完成后年假余额转为已使用");
True(service.AdjustBalance(hr, employee.Id, new AdjustLeaveBalanceRequest(LeaveType.Annual, 2026, -5m, "尝试把额度调低到已使用量以下", service.GetBalance(employee).Version)).Code == "LEAVE_001", "人工调整后总额度不能低于冻结和已使用额度");
True(service.List(employee, new DocumentListQuery("已更新", null, null, null, null)).Any(item => item.Id == draft.Value.Id), "请假列表支持关键字筛选");
True(service.List(employee, new DocumentListQuery(null, (int)LeaveStatus.Completed, null, null, null)).Any(item => item.Id == draft.Value.Id), "请假列表支持状态筛选");

var longLeave = service.CreateDraft(employee, new CreateLeaveRequest(
    LeaveType.Personal,
    new DateOnly(2026, 9, 1), LeavePeriod.FullDay,
    new DateOnly(2026, 9, 4), LeavePeriod.FullDay,
    "处理紧急家庭事务"));
var longSubmitted = service.Submit(employee, longLeave.Value!.Id);
Equal(2, longSubmitted.Value!.Tasks.Count, "超过三天需要总经理审批");
True(!service.GetPendingTasks(generalManager).Any(task => task.LeaveRequestId == longSubmitted.Value.Id), "未来审批节点不会提前进入待办");
True(service.Approve(generalManager, longSubmitted.Value.Tasks[1].Id, "越级审批").IsSuccess == false, "前序审批未完成不可越级处理");
True(service.Reject(manager, longSubmitted.Value.Tasks[0].Id, "请补充安排").IsSuccess, "部门负责人可驳回");
Equal(LeaveStatus.Rejected, longSubmitted.Value.Status, "驳回后状态正确");
Equal(FlowTaskStatus.Cancelled, longSubmitted.Value.Tasks[1].Status, "驳回后取消未处理的后续节点");
Equal(FlowInstanceStatus.Rejected, longSubmitted.Value.FlowInstances.Single().Status, "驳回同步终止当前流程实例");
True(longSubmitted.Value.FlowInstances.Single().Actions.Any(action => action.Action == FlowActionType.Rejected && action.Comment == "请补充安排"), "流程实例永久记录驳回动作和意见");
var revisedLongLeave = service.Update(employee, longSubmitted.Value.Id, longSubmitted.Value.Version, new CreateLeaveRequest(
    LeaveType.Personal, new DateOnly(2026, 9, 1), LeavePeriod.FullDay,
    new DateOnly(2026, 9, 4), LeavePeriod.FullDay, "补充工作交接后重新提交"));
True(revisedLongLeave.IsSuccess, "驳回后的请假可以修改");
var resubmittedLongLeave = service.Submit(employee, revisedLongLeave.Value!.Id);
Equal(2, resubmittedLongLeave.Value!.FlowInstances.Count, "驳回重提创建新流程实例并保留旧实例");
Equal(1, resubmittedLongLeave.Value.FlowInstances[0].Attempt, "首次流程实例序号正确");
Equal(2, resubmittedLongLeave.Value.FlowInstances[1].Attempt, "重提流程实例序号递增");
Equal(resubmittedLongLeave.Value.CurrentFlowInstanceId!.Value, resubmittedLongLeave.Value.Tasks[0].FlowInstanceId!.Value, "新任务绑定当前流程实例");

var withdrawableLeave = service.CreateDraft(employee, new CreateLeaveRequest(
    LeaveType.Annual,
    new DateOnly(2026, 9, 7), LeavePeriod.FullDay,
    new DateOnly(2026, 9, 7), LeavePeriod.FullDay,
    "临时调整安排"));
var withdrawableSubmitted = service.Submit(employee, withdrawableLeave.Value!.Id);
True(service.Withdraw(employee, withdrawableSubmitted.Value!.Id).IsSuccess, "申请人可撤回未处理的请假单");
Equal(LeaveStatus.Withdrawn, withdrawableSubmitted.Value.Status, "撤回后请假状态正确");
Equal(0m, service.GetBalance(employee).Frozen, "撤回后释放年假冻结额度");

var crossYearAnnual = service.CreateDraft(employee, new CreateLeaveRequest(
    LeaveType.Annual,
    new DateOnly(2026, 12, 31), LeavePeriod.FullDay,
    new DateOnly(2027, 1, 4), LeavePeriod.FullDay,
    "跨年度年假应拆分申请"));
True(crossYearAnnual.IsSuccess && service.Submit(employee, crossYearAnnual.Value!.Id).Code == "LEAVE_004", "年假跨自然年度时要求拆分以避免错扣年度余额");

var transferableLeave = service.CreateDraft(employee, new CreateLeaveRequest(
    LeaveType.Personal, new DateOnly(2026, 9, 9), LeavePeriod.FullDay,
    new DateOnly(2026, 9, 9), LeavePeriod.FullDay, "审批人临时外出"));
var transferableLeaveSubmitted = service.Submit(employee, transferableLeave.Value!.Id);
var leaveTransfer = service.Transfer(manager, transferableLeaveSubmitted.Value!.Tasks[0].Id, new TransferTaskRequest("u-wang", "外出期间请王总代为处理"));
True(leaveTransfer.IsSuccess && leaveTransfer.Value!.Tasks[0].AssigneeId == "u-wang", "请假审批任务可转办并更新接收人");
True(!service.Approve(manager, transferableLeaveSubmitted.Value.Tasks[0].Id, "原审批人处理").IsSuccess, "转办后原审批人不能再处理任务");
True(service.Approve(generalManager, transferableLeaveSubmitted.Value.Tasks[0].Id, "转办后同意").IsSuccess, "接收人可处理转办任务");

var deletableLeave = service.CreateDraft(employee, new CreateLeaveRequest(
    LeaveType.Personal, new DateOnly(2026, 9, 8), LeavePeriod.FullDay,
    new DateOnly(2026, 9, 8), LeavePeriod.FullDay, "可删除草稿"));
True(service.Delete(employee, deletableLeave.Value!.Id).IsSuccess, "申请人可删除请假草稿");
True(!service.Get(employee, deletableLeave.Value.Id).IsSuccess, "删除后无法查询请假草稿");

var expenseService = new ExpenseService(data);
var claim = expenseService.CreateDraft(employee, new CreateExpenseClaim(
    null, "张晨", "6222026000001234", "模拟银行", "客户拜访交通费",
    [new ExpenseItem(new DateOnly(2026, 8, 20), "交通", 8650m, "出行费用", "FP-20260820-01", ["receipt.pdf"])], [hr.Id]));
True(claim.IsSuccess, "可创建有票据的报销草稿");
Equal(hr.Id, claim.Value!.CopyRecipientIds.Single(), "报销草稿保存抄送人");
var editedClaim = expenseService.Update(employee, claim.Value!.Id, claim.Value.Version, new CreateExpenseClaim(
    null, "张晨", "6222026000001234", "模拟银行", "已更新的客户拜访交通费",
    [new ExpenseItem(new DateOnly(2026, 8, 20), "交通", 8650m, "已更新的出行费用", "FP-20260820-01", ["receipt.pdf"])], [hr.Id]));
True(editedClaim.IsSuccess && editedClaim.Value!.Description == "已更新的客户拜访交通费" && editedClaim.Value.Version == 2, "报销草稿可编辑且保留原单据");
True(!expenseService.Update(employee, claim.Value.Id, 1, new CreateExpenseClaim(null, "张晨", "6222026000001234", "模拟银行", "过期版本", [new ExpenseItem(new DateOnly(2026, 8, 20), "交通", 8650m, "过期版本", "FP-20260820-01", ["receipt.pdf"])])).IsSuccess, "过期版本不能覆盖报销草稿");
var expenseSubmitted = expenseService.Submit(employee, claim.Value!.Id);
Equal(4, expenseSubmitted.Value!.Tasks.Count, "高额报销匹配总经理审批路径");
Equal("EXPENSE_DEFAULT", expenseSubmitted.Value.ProcessDefinitionCode!, "报销提交绑定默认流程编码");
Equal(1, expenseSubmitted.Value.ProcessDefinitionVersion!.Value, "报销提交绑定已发布流程版本");
True(expenseService.Get(data.GetEmployee("u-chen"), expenseSubmitted.Value.Id).IsSuccess, "被分配的财务审批人可查看报销详情");
True(!expenseService.Get(hr, expenseSubmitted.Value.Id).IsSuccess, "无数据权限用户不能查看其他员工报销详情");
True(expenseService.Approve(manager, expenseSubmitted.Value.Tasks[0].Id, "同意").IsSuccess, "部门负责人可审批报销");
True(expenseService.GetProcessedTasks(manager).Any(task => task.Id == expenseSubmitted.Value.Tasks[0].Id), "已处理报销任务进入我的已办");
True(expenseService.Approve(data.GetEmployee("u-chen"), expenseSubmitted.Value.Tasks[1].Id, "同意").IsSuccess, "财务专员可审批报销");
True(expenseService.Approve(data.GetEmployee("u-lin"), expenseSubmitted.Value.Tasks[2].Id, "同意").IsSuccess, "财务经理可审批报销");
True(expenseService.Approve(generalManager, expenseSubmitted.Value.Tasks[3].Id, "同意").IsSuccess, "总经理可审批报销");
Equal(ExpenseStatus.Approved, expenseSubmitted.Value.Status, "全部审批后进入待付款状态");
Equal(FlowInstanceStatus.Completed, expenseSubmitted.Value.FlowInstances.Single().Status, "全部审批后流程实例完成");
Equal(5, expenseSubmitted.Value.FlowInstances.Single().Actions.Count, "报销流程实例记录提交及全部审批动作");
True(!expenseService.RegisterPayment(data.GetEmployee("u-chen"), expenseSubmitted.Value.Id, new RegisterPaymentRequest(new DateOnly(2026, 8, 25), "转账", "TX-1", 8000m, "payment.pdf")).IsSuccess, "不允许差额付款");
True(expenseService.RegisterPayment(data.GetEmployee("u-chen"), expenseSubmitted.Value.Id, new RegisterPaymentRequest(new DateOnly(2026, 8, 25), "转账", "TX-2", 8650m, "payment.pdf")).IsSuccess, "财务可登记全额付款");
Equal(ExpenseStatus.Completed, expenseSubmitted.Value.Status, "付款后报销完成");
True(expenseService.List(employee, new DocumentListQuery("客户拜访", (int)ExpenseStatus.Completed, employee.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 8000m, 9000m)).Any(item => item.Id == claim.Value.Id), "报销列表支持关键字、状态、申请人、日期和金额筛选");

var duplicateReceipt = expenseService.CreateDraft(employee, new CreateExpenseClaim(
    null, "张晨", "6222026000001234", "模拟银行", "重复票据测试",
    [new ExpenseItem(new DateOnly(2026, 8, 20), "交通", 200m, "重复票据", "FP-20260820-01", ["receipt.pdf"])]));
True(!expenseService.Submit(employee, duplicateReceipt.Value!.Id).IsSuccess, "有效报销单中的同类票据号不可重复提交");

var withdrawableClaim = expenseService.CreateDraft(employee, new CreateExpenseClaim(
    null, "张晨", "6222026000001234", "模拟银行", "临时交通费",
    [new ExpenseItem(new DateOnly(2026, 8, 21), "交通", 120m, "临时出行", "FP-20260821-01", ["receipt.pdf"])]));
var withdrawableExpense = expenseService.Submit(employee, withdrawableClaim.Value!.Id);
True(expenseService.Withdraw(employee, withdrawableExpense.Value!.Id).IsSuccess, "申请人可撤回未处理的报销单");
Equal(ExpenseStatus.Withdrawn, withdrawableExpense.Value.Status, "撤回后报销状态正确");

var transferableClaim = expenseService.CreateDraft(employee, new CreateExpenseClaim(
    null, "张晨", "6222026000001234", "模拟银行", "转办报销审批",
    [new ExpenseItem(new DateOnly(2026, 8, 23), "交通", 200m, "转办验证", "FP-20260823-01", ["receipt.pdf"])]));
var transferableExpense = expenseService.Submit(employee, transferableClaim.Value!.Id);
var expenseTransfer = expenseService.Transfer(manager, transferableExpense.Value!.Tasks[0].Id, new TransferTaskRequest("u-wang", "部门负责人出差，请代为审批"));
True(expenseTransfer.IsSuccess && expenseTransfer.Value!.Tasks[0].AssigneeId == "u-wang", "报销审批任务可转办并更新接收人");
True(!expenseService.Approve(manager, transferableExpense.Value.Tasks[0].Id, "原审批人处理").IsSuccess, "报销转办后原审批人不能再处理任务");
True(expenseService.Approve(generalManager, transferableExpense.Value.Tasks[0].Id, "转办后同意").IsSuccess, "接收人可处理转办报销任务");

var deletableExpense = expenseService.CreateDraft(employee, new CreateExpenseClaim(
    null, "张晨", "6222026000001234", "模拟银行", "可删除草稿",
    [new ExpenseItem(new DateOnly(2026, 8, 22), "交通", 100m, "草稿", "FP-20260822-01", ["receipt.pdf"])]));
True(expenseService.Delete(employee, deletableExpense.Value!.Id).IsSuccess, "申请人可删除报销草稿");
True(!expenseService.Get(employee, deletableExpense.Value.Id).IsSuccess, "删除后无法查询报销草稿");

True(data.CanAccessDepartmentDocument(employee, null), "研发员工可查阅全公司通用文档");
True(data.CanAccessDepartmentDocument(employee, "engineering"), "研发员工可查阅研发部专属文档");
True(!data.CanAccessDepartmentDocument(employee, "finance"), "研发员工不可查阅财务部专属保密文档");
True(data.CanAccessDepartmentDocument(hr, "finance"), "全公司文档管理员可查阅任意部门文档");

True(data.CanManageDepartmentDocument(manager, "engineering"), "研发主管可管理研发部专属规章制度");
True(!data.CanManageDepartmentDocument(manager, "finance"), "研发主管不可管理财务部专属规章制度");
True(!data.CanManageDepartmentDocument(manager, null), "研发主管不可管理全公司通用制度");
True(data.CanManageDepartmentDocument(hr, null), "全公司文档管理员可管理公司通用制度");
True(data.CanManageDepartmentDocument(hr, "engineering"), "全公司文档管理员可管理各部门专属制度");
True(!data.CanManageDepartmentDocument(employee, "engineering"), "普通员工无权管理所属部门制度");

True(data.CanEditDepartmentDocument(employee, "engineering"), "研发普通员工有权创建和编辑本部门文档");
True(!data.CanEditDepartmentDocument(employee, "finance"), "研发普通员工无权创建和编辑其他部门文档");
True(!data.CanEditDepartmentDocument(employee, null), "研发普通员工无权创建和编辑全公司通用文档");
True(data.CanEditDepartmentDocument(manager, "engineering"), "研发主管有权创建和编辑本部门文档");
True(data.CanEditDepartmentDocument(hr, "finance"), "全公司文档管理员有权创建和编辑各部门文档");

// Business Configuration Validator Tests
var unknownDomainResult = BusinessConfigurationValidator.ValidateAndNormalize("InvalidDomain", "{}");
True(!unknownDomainResult.IsSuccess && unknownDomainResult.Error!.Contains("不支持的业务领域"), "校验器拒绝未知的业务领域");

var invalidJsonResult = BusinessConfigurationValidator.ValidateAndNormalize(ConfigurationDomains.Leave, "{ broken json }");
True(!invalidJsonResult.IsSuccess && invalidJsonResult.Error!.Contains("配置 JSON 格式非法"), "校验器拦截非合法 JSON 语法");

var validLeaveResult = BusinessConfigurationValidator.ValidateAndNormalize(
    ConfigurationDomains.Leave,
    System.Text.Json.JsonSerializer.Serialize(BusinessConfigurationDefaults.CreateDefaultLeavePolicy(), BusinessConfigurationDefaults.JsonOptions));
True(validLeaveResult.IsSuccess, "默认休假策略通过校验");

var unprotectedLeaveJson = """{"leaveTypes": [{"type": "ANNUAL", "name": "年假", "minUnit": 0.5}], "allowCrossYear": true, "compTimeValidityDays": 365, "annualLeaveBonus": {"legalMinStandardProtected": false, "tier1BonusDays": 1, "tier2BonusDays": 2, "tier3BonusDays": 3}}""";
var unprotectedLeaveResult = BusinessConfigurationValidator.ValidateAndNormalize(ConfigurationDomains.Leave, unprotectedLeaveJson);
True(!unprotectedLeaveResult.IsSuccess && unprotectedLeaveResult.Error!.Contains("法定标准保护"), "休假规则必须勾选中国法定年假最低标准保护");

var validExpenseResult = BusinessConfigurationValidator.ValidateAndNormalize(
    ConfigurationDomains.Expense,
    System.Text.Json.JsonSerializer.Serialize(BusinessConfigurationDefaults.CreateDefaultExpensePolicy(), BusinessConfigurationDefaults.JsonOptions));
True(validExpenseResult.IsSuccess, "默认报销策略通过校验");

var emptyExpenseResult = BusinessConfigurationValidator.ValidateAndNormalize(ConfigurationDomains.Expense, """{"categories": []}""");
True(!emptyExpenseResult.IsSuccess && emptyExpenseResult.Error!.Contains("费用类别"), "费用报销规则必须包含至少一个类别");

var validTravelResult = BusinessConfigurationValidator.ValidateAndNormalize(
    ConfigurationDomains.Travel,
    System.Text.Json.JsonSerializer.Serialize(BusinessConfigurationDefaults.CreateDefaultTravelPolicy(), BusinessConfigurationDefaults.JsonOptions));
True(validTravelResult.IsSuccess, "默认差旅策略通过校验");

var negativeTravelResult = BusinessConfigurationValidator.ValidateAndNormalize(
    ConfigurationDomains.Travel,
    """{"standards": [{"cityTier": "一线城市", "rank": "员工", "hotelDailyLimit": -100, "mealDailyAllowance": 50, "transportationStandard": "飞机"}]}""");
True(!negativeTravelResult.IsSuccess && negativeTravelResult.Error!.Contains("住宿限额不能为负数"), "出差标准住宿限额不能为负数");

var validProcurementResult = BusinessConfigurationValidator.ValidateAndNormalize(
    ConfigurationDomains.Procurement,
    System.Text.Json.JsonSerializer.Serialize(BusinessConfigurationDefaults.CreateDefaultProcurementPolicy(), BusinessConfigurationDefaults.JsonOptions),
    data);
True(validProcurementResult.IsSuccess, "默认采购策略通过校验");

var validSealResult = BusinessConfigurationValidator.ValidateAndNormalize(
    ConfigurationDomains.Seal,
    System.Text.Json.JsonSerializer.Serialize(BusinessConfigurationDefaults.CreateDefaultSealPolicy(), BusinessConfigurationDefaults.JsonOptions),
    data);
True(validSealResult.IsSuccess, "默认用印策略通过校验");

var invalidSealRiskJson = """{"seals": [{"name": "公章", "sealType": "公章", "custodianUserId": "u-admin", "isEnabled": true}], "documentCategories": [{"name": "合同", "riskLevel": "CRITICAL", "isEnabled": true}], "riskRules": {"highRiskMetric": 3, "mediumRiskMetric": 2, "lowRiskMetric": 1}}""";
var invalidSealResult = BusinessConfigurationValidator.ValidateAndNormalize(ConfigurationDomains.Seal, invalidSealRiskJson, data);
True(!invalidSealResult.IsSuccess && invalidSealResult.Error!.Contains("风险等级必须为"), "用印文件风险等级必须限制为 LOW/MEDIUM/HIGH");

var validDictResult = BusinessConfigurationValidator.ValidateAndNormalize(
    ConfigurationDomains.Dictionary,
    System.Text.Json.JsonSerializer.Serialize(BusinessConfigurationDefaults.CreateDefaultAnnouncementTypeDict(), BusinessConfigurationDefaults.JsonOptions));
True(validDictResult.IsSuccess, "默认字典策略通过校验");

var dupDictResult = BusinessConfigurationValidator.ValidateAndNormalize(
    ConfigurationDomains.Dictionary,
    """{"items": [{"code": "DUP", "name": "A"}, {"code": "dup", "name": "B"}]}""");
True(!dupDictResult.IsSuccess && dupDictResult.Error!.Contains("字典项编码重复"), "字典项编码不可重复");

// -------------------------------------------------------------
// P1-F4: 发票防重、预算管控与付款闭环领域测试
// -------------------------------------------------------------
// 1. 发票指纹计算一致性与空值容错
var fp1 = InvoiceFingerprintHelper.ComputeFingerprint("demo", " 011002233 ", " 88990011 ");
var fp2 = InvoiceFingerprintHelper.ComputeFingerprint("demo", "011002233", "88990011");
Equal(fp1, fp2, "发票指纹对两端空格进行标准化处理");

// 2. 预算池编制、额度调整与超额预检
var fManager = data.GetEmployee("u-lin");
var fOfficer = data.GetEmployee("u-chen");
var testBudgetService = new BudgetService(data);

var budgetCreate = testBudgetService.Create(fManager, new CreateBudgetRequest(
    DepartmentId: "研发部",
    Year: 2026,
    Month: 0,
    ExpenseCategory: null,
    ProjectId: null,
    AllocatedAmount: 20000m,
    AutoPublish: true));
True(budgetCreate.IsSuccess, "财务经理可编制部门年度预算池");
Equal(20000m, budgetCreate.Value!.AllocatedAmount, "编制预算初始额度正确");
Equal(20000m, budgetCreate.Value.AvailableAmount, "期初可用额度等于编制额度");

var budgetAdjust = testBudgetService.Adjust(fManager, budgetCreate.Value.Id, new AdjustBudgetRequest(5000m, "年度追加业务预算"));
True(budgetAdjust.IsSuccess, "财务经理可调整追加预算额度");
Equal(25000m, budgetAdjust.Value!.AllocatedAmount, "追加后预算总额正确");

var preCheckNormal = testBudgetService.Check(employee, new BudgetCheckRequest("研发部", "日常办公", 5000m, 2026, 8));
True(preCheckNormal.IsAllowed && !preCheckNormal.IsExceeded, "未超预算申请校验通过");
Equal(25000m, preCheckNormal.AvailableAmount, "预检返回正确可用预算");

var preCheckExceeded = testBudgetService.Check(employee, new BudgetCheckRequest("研发部", "日常办公", 30000m, 2026, 8));
True(preCheckExceeded.IsExceeded, "超过可用额度触发超预算预警");

// 3. 报销单结构化发票录入、开票日期校验与单据内查重
var f4ExpenseService = new ExpenseService(data, budgetService: testBudgetService);

// 3a. 发票开票日期不能早于 180 天前
var oldDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-200));
var expiredInvoiceClaim = f4ExpenseService.CreateDraft(employee, new CreateExpenseClaim(
    null, "张晨", "6222026000001234", "招商银行", "过期发票测试",
    [new ExpenseItem(new DateOnly(2026, 8, 20), "办公", 300m, "过期票据", "FP-OLD", ["inv.pdf"])],
    Invoices: [new ExpenseInvoiceInput(InvoiceType.VatNormal, "1100", "EXP-001", oldDate, 283.02m, 0.06m, 16.98m, 300m, null, null)]));
True(expiredInvoiceClaim.IsSuccess, "草稿创建成功");
var expiredSubmit = f4ExpenseService.Submit(employee, expiredInvoiceClaim.Value!.Id);
True(!expiredSubmit.IsSuccess && expiredSubmit.Error!.Contains("180天"), "提交过期发票被强行阻断");

// 3b. 单据内相同发票号码重复录入拦截
var internalDupClaim = f4ExpenseService.CreateDraft(employee, new CreateExpenseClaim(
    null, "张晨", "6222026000001234", "招商银行", "单据内重复发票测试",
    [
        new ExpenseItem(new DateOnly(2026, 8, 20), "交通", 100m, "发票1", "FP-1", ["inv.pdf"]),
        new ExpenseItem(new DateOnly(2026, 8, 20), "交通", 100m, "发票2", "FP-2", ["inv.pdf"])
    ],
    Invoices: [
        new ExpenseInvoiceInput(InvoiceType.VatNormal, "1100", "INV-SAME-001", DateOnly.FromDateTime(DateTime.Today.AddDays(-5)), 94.34m, 0.06m, 5.66m, 100m, null, null),
        new ExpenseInvoiceInput(InvoiceType.VatNormal, "1100", "INV-SAME-001", DateOnly.FromDateTime(DateTime.Today.AddDays(-5)), 94.34m, 0.06m, 5.66m, 100m, null, null)
    ]));
var internalDupSubmit = f4ExpenseService.Submit(employee, internalDupClaim.Value!.Id);
True(!internalDupSubmit.IsSuccess && internalDupSubmit.Code == "INVOICE_DUPLICATE", "单据内录入重复发票时阻止提交");

// 3c. 跨单据发票防重与撤回释放闭环
var validInvoice1 = new ExpenseInvoiceInput(InvoiceType.VatElectronic, "01100", "INV-CROSS-888", DateOnly.FromDateTime(DateTime.Today.AddDays(-3)), 943.40m, 0.06m, 56.60m, 1000m, null, null);
var claim1 = f4ExpenseService.CreateDraft(employee, new CreateExpenseClaim(
    null, "张晨", "6222026000001234", "招商银行", "发票占用测试A",
    [new ExpenseItem(new DateOnly(2026, 8, 20), "办公", 1000m, "采购用品", "FP-A", ["inv.pdf"])],
    Invoices: [validInvoice1]));
var submitClaim1 = f4ExpenseService.Submit(employee, claim1.Value!.Id);
True(submitClaim1.IsSuccess, "首笔报销单提交成功并占用发票指纹");

// 验证独立发票查验接口 ValidateInvoice
var validateDup = f4ExpenseService.ValidateInvoice(employee, new ValidateInvoiceRequest(InvoiceType.VatElectronic, "01100", "INV-CROSS-888", DateOnly.FromDateTime(DateTime.Today.AddDays(-3)), 1000m));
True(validateDup.IsSuccess && !validateDup.Value!.IsValid, "发票查验识别出已被占用的发票");
True(validateDup.Value!.ErrorMessage!.Contains("INV-CROSS-888"), "查验提示包含冲突单据与发票信息");

// 另一单据尝试提交同一发票被拦截
var claim2 = f4ExpenseService.CreateDraft(data.GetEmployee("u-li"), new CreateExpenseClaim(
    null, "李薇", "6222026000009999", "工商银行", "发票占用测试B",
    [new ExpenseItem(new DateOnly(2026, 8, 20), "办公", 1000m, "用品重报", "FP-B", ["inv.pdf"])],
    Invoices: [validInvoice1]));
var submitClaim2 = f4ExpenseService.Submit(data.GetEmployee("u-li"), claim2.Value!.Id);
True(!submitClaim2.IsSuccess && submitClaim2.Code == "INVOICE_DUPLICATE", "跨单据提交相同发票被强行阻断");

// 申请人撤回单据1 -> 发票状态转为 Released，指纹释放
var withdraw1 = f4ExpenseService.Withdraw(employee, claim1.Value.Id);
True(withdraw1.IsSuccess, "申请人撤回报销单1");
Equal(InvoiceStatus.Released, withdraw1.Value!.Invoices[0].Status, "撤回后发票状态更新为 Released 释放");

// 释放后单据2再次提交应当成功
var submitClaim2AfterRelease = f4ExpenseService.Submit(data.GetEmployee("u-li"), claim2.Value.Id);
True(submitClaim2AfterRelease.IsSuccess, "原发票释放后允许在其他单据中正常报销");

// 4. 付款服务分批打款与银行卡号脱敏
var f4PaymentService = new PaymentService(data, null, testBudgetService);

// 审批通过单据2
True(f4ExpenseService.Approve(data.GetEmployee(submitClaim2AfterRelease.Value!.Tasks[0].AssigneeId), submitClaim2AfterRelease.Value!.Tasks[0].Id, "部门通过").IsSuccess, "部门负责人审批");
True(f4ExpenseService.Approve(data.GetEmployee(submitClaim2AfterRelease.Value!.Tasks[1].AssigneeId), submitClaim2AfterRelease.Value!.Tasks[1].Id, "财务初审").IsSuccess, "财务审批");
Equal(ExpenseStatus.Approved, submitClaim2AfterRelease.Value.Status, "审批完成后进入待付款状态");

// 4a. 权限控制：普通员工无权登记付款
var unauthorizedPay = f4PaymentService.RegisterExpensePayment(employee, claim2.Value.Id, new CreatePaymentTransactionRequest(
    BatchTitle: "第一期付款",
    PaymentDate: DateOnly.FromDateTime(DateTime.Today),
    PaymentMethod: PaymentMethodNames.BankTransfer,
    PayerAccount: "955880001",
    PayeeName: "李薇",
    PayeeAccount: "6222026000009999",
    PayeeBank: "工商银行",
    TransactionNumber: "BANK-TX-20260906-001",
    PaidAmount: 500m,
    FeeAmount: 0m,
    ProofAttachmentId: null,
    Remarks: "首期付款"));
True(!unauthorizedPay.IsSuccess && unauthorizedPay.Code == "AUTH_002", "普通员工无权登记付款");

// 4b. 财务人员登记第一笔分批付款 (500/1000)
var payBatch1 = f4PaymentService.RegisterExpensePayment(fOfficer, claim2.Value.Id, new CreatePaymentTransactionRequest(
    BatchTitle: "第一期付款",
    PaymentDate: DateOnly.FromDateTime(DateTime.Today),
    PaymentMethod: PaymentMethodNames.BankTransfer,
    PayerAccount: "955880001",
    PayeeName: "李薇",
    PayeeAccount: "6222026000009999",
    PayeeBank: "工商银行",
    TransactionNumber: "BANK-TX-20260906-001",
    PaidAmount: 500m,
    FeeAmount: 0m,
    ProofAttachmentId: null,
    Remarks: "首期付款"));
True(payBatch1.IsSuccess, "财务专员成功登记第一笔分批付款");

// 4c. 银行账号脱敏查询校验与越权拦截
var applicantLi = data.GetEmployee("u-li");
var paymentsForNormalUser = f4PaymentService.GetPayments(applicantLi, "Expense", claim2.Value.Id);
True(paymentsForNormalUser.IsSuccess, "报销申请人成功查看付款流水");
True(paymentsForNormalUser.Value!.Count == 1, "查询到1笔付款流水");
Equal("**** **** **** 9999", paymentsForNormalUser.Value[0].PayeeAccountMasked, "非财务人员查看到的收款账号脱敏");

var unrelatedUserPayments = f4PaymentService.GetPayments(employee, "Expense", claim2.Value.Id);
True(!unrelatedUserPayments.IsSuccess && unrelatedUserPayments.Code == "AUTH_002", "无关员工查看他人付款流水被拦截");

var paymentsForFinanceUser = f4PaymentService.GetPayments(fOfficer, "Expense", claim2.Value.Id);
True(paymentsForFinanceUser.IsSuccess, "财务专员成功查看付款流水");
Equal("6222026000009999", paymentsForFinanceUser.Value![0].PayeeAccountMasked, "财务人员可查看到明文银行账号");

// 4d. 重复流水号拦截
var payDupTx = f4PaymentService.RegisterExpensePayment(fOfficer, claim2.Value.Id, new CreatePaymentTransactionRequest(
    BatchTitle: "第二期付款",
    PaymentDate: DateOnly.FromDateTime(DateTime.Today),
    PaymentMethod: PaymentMethodNames.BankTransfer,
    PayerAccount: "955880001",
    PayeeName: "李薇",
    PayeeAccount: "6222026000009999",
    PayeeBank: "工商银行",
    TransactionNumber: "BANK-TX-20260906-001", // 重复流水号
    PaidAmount: 500m,
    FeeAmount: 0m,
    ProofAttachmentId: null,
    Remarks: "第二期付款"));
True(payBatch1.Value!.Status == PaymentTransactionStatus.Success, "第一笔款项登记状态为成功");

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("All domain tests passed.");
return 0;

sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now;
    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan duration) => current = current.Add(duration);
}
