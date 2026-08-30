using Microsoft.Extensions.Configuration;
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
var authentication = new AuthenticationService(data, tokenService);
var login = authentication.Login(new LoginRequest("u-zhang", "Oa@123456"));
True(login.IsSuccess, "有效模拟账号可以登录");
True(tokenService.TryValidate(login.Value!.Session.AccessToken, out var tokenIdentity) && tokenIdentity!.UserId == "u-zhang" && tokenIdentity.SessionId is null, "登录签发的 JWT 可验证并绑定正确用户");
True(!tokenService.TryValidate(login.Value.Session.AccessToken + "x", out _), "被篡改的 JWT 无法通过签名验证");
True(!authentication.Login(new LoginRequest("u-zhang", "wrong-password")).IsSuccess, "错误密码不能登录");
True(!authentication.Login(new LoginRequest("u-disabled", "Oa@123456")).IsSuccess, "停用账号不能登录");
var employee = data.GetEmployee("u-zhang");
var manager = data.GetEmployee("u-li");
var generalManager = data.GetEmployee("u-wang");
var hr = data.GetEmployee("u-sun");
var service = new LeaveService(data);
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

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("All domain tests passed.");
return 0;
