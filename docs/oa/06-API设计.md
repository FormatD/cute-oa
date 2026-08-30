# OA V1.0 API 设计规范

## 1. 通用约定

- 前缀：`/api/v1`；响应格式统一为 `{code, message, data, requestId}`。
- 认证：`Authorization: Bearer <token>`；服务端从令牌取得租户和用户，不信任客户端传入的 `tenantId`。
- 列表：`page` 从 1 开始，`pageSize` 默认 20、最大 100，返回 `items`、`total`、`page`、`pageSize`、`totalPages`；超范围页码收敛到最后一页。
- 时间统一 ISO 8601，数据库以 UTC 保存，展示按租户时区转换；金额使用十进制定点数。
- 写接口带 `Idempotency-Key`；并发更新失败返回版本冲突。

## 2. 核心资源

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/auth/demo-accounts` | 登录页可选模拟账号；仅开发演示使用，不返回密码和敏感人事字段 |
| POST | `/auth/login` | 登录并签发短期 JWT，同时设置 HttpOnly 刷新 Cookie；停用账号与错误密码统一返回 `AUTH_001` |
| POST | `/auth/refresh` | 轮换刷新令牌并签发新的短期 JWT；无需 Bearer，使用 HttpOnly Cookie |
| POST | `/auth/logout` | 撤销 Cookie 对应的服务端会话并清除 Cookie |
| GET | `/auth/sessions` | 分页查询当前账号的登录设备，按最近使用时间倒序 |
| POST | `/auth/sessions/{id}/revoke` | 撤销当前账号的指定其他会话 |
| POST | `/auth/sessions/revoke-others` | 保留当前会话并撤销当前账号的所有其他有效会话 |
| GET | `/me` | 返回当前令牌对应的安全用户资料 |
| GET | `/demo/summary` | 当前用户工作台聚合数据 |
| GET | `/announcements` | 有效已发布公告分页，按发布时间倒序并返回当前用户已读时间 |
| GET | `/announcements/{id}` | 公告详情；普通员工不能读取草稿、撤回或过期公告 |
| POST | `/announcements/{id}/read` | 当前用户确认已读，重复请求保持一条阅读记录 |
| GET | `/org/units` | 组织树/分页查询 |
| GET | `/org/departments` | 只读部门目录 |
| GET | `/org/employees` | 只读员工通讯录，不返回社会工龄等敏感字段 |
| GET | `/org/positions` | 只读启用岗位选项，返回岗位及所属部门 |
| GET | `/admin/departments` | 返回按树形前序排列的部门列表、深度、父部门、用户数、下级数和版本；要求 `ORG_MANAGE` |
| POST | `/admin/departments` | 新建非根部门；编码不可变，名称唯一，最多 8 层 |
| PUT | `/admin/departments/{id}` | 按版本更新名称、上级和排序；禁止形成循环，层级变化撤销租户有效会话 |
| DELETE | `/admin/departments/{id}?version={version}` | 删除无用户、无下级的自定义部门；系统预置部门不可删除 |
| GET | `/admin/positions` | 查询岗位、所属部门、状态、排序、用户数和版本；要求 `ORG_MANAGE` |
| POST | `/admin/positions` | 新建部门岗位；编码不可变，同部门名称唯一 |
| PUT | `/admin/positions/{id}` | 按版本更新名称、部门、排序和状态；已分配岗位不可移动或停用 |
| DELETE | `/admin/positions/{id}?version={version}` | 删除未分配用户的自定义岗位；系统岗位不可删除 |
| GET | `/admin/announcements` | 公告管理分页，支持 `DRAFT/PUBLISHED/WITHDRAWN` 状态筛选；要求 `ANNOUNCEMENT_MANAGE` |
| POST | `/admin/announcements` | 创建公告草稿，可设置未来有效期 |
| PUT | `/admin/announcements/{id}` | 按版本编辑草稿标题、正文和有效期 |
| POST | `/admin/announcements/{id}/publish?version={version}` | 发布草稿；发布后正文不可编辑 |
| POST | `/admin/announcements/{id}/withdraw?version={version}` | 撤回已发布公告，普通员工立即不可见 |
| GET | `/admin/roles` | 查询角色、用户数、操作权限及六类独立数据范围；要求 `USER_MANAGE` |
| GET | `/admin/permissions` | 查询可配置的服务端操作权限定义；要求 `USER_MANAGE` |
| POST | `/admin/roles` | 新建租户自定义角色，编码和名称唯一；可设置 `dataScopes.Leave/Expense` |
| PUT | `/admin/roles` | 按请求体角色编码更新名称、权限和数据范围；安全配置变化撤销受影响用户会话 |
| DELETE | `/admin/roles?code={code}` | 删除未分配用户的自定义角色；系统角色不可删除 |
| GET | `/admin/users` | 用户管理数据库分页；支持关键字、状态、部门筛选，最新创建在前 |
| POST | `/admin/users` | 新建用户、账号和初始角色；初始密码须同时包含字母和数字 |
| PUT | `/admin/users/{id}` | 按版本更新姓名、部门、上级、工龄、状态与角色 |
| POST | `/admin/users/{id}/reset-password` | 重置登录密码并清除失败锁定状态 |
| GET | `/audit-logs` | 审计日志分页查询；仅系统管理员，支持关键字、操作人、资源类型和日期区间筛选 |
| GET/POST | `/process/definitions` | 查询/创建流程定义 |
| PUT | `/process/definitions/{id}` | 编辑草稿版本的名称、优先级、适用部门/假别、条件规则与审批节点；已发布版本拒绝修改 |
| POST | `/process/definitions/{id}/clone` | 从任意历史版本复制递增的新草稿版本 |
| POST | `/process/definitions/{id}/publish` | 发布流程版本 |
| GET | `/flow/tasks/my` | 我的待办 |
| GET | `/flow/tasks/done` | 我的已办请假审批任务 |
| POST | `/flow/tasks/{id}/approve` | 审批通过 |
| POST | `/flow/tasks/{id}/reject` | 审批驳回 |
| POST | `/flow/tasks/{id}/transfer` | 转办请假审批任务；请求体含 `assigneeId`、必填 `comment` |
| GET | `/flow/delegations/my` | 我的审批委托记录，按创建时间倒序 |
| POST | `/flow/delegations` | 创建按时间范围和业务类型生效的审批委托 |
| POST | `/flow/delegations/{id}/cancel` | 委托人取消自己的委托；不回收已生成任务 |
| GET | `/flow/instances/{id}` | 按单据数据权限读取独立流程实例及永久动作轨迹 |
| GET | `/flow/copies/my` | 分页查询抄送给我的已完成/已撤回事项，按可阅读时间倒序 |
| POST | `/flow/copies/{id}/read` | 抄送人标记自己的抄送事项为已阅 |
| POST | `/leave-requests` | 创建请假单 |
| GET | `/leave-requests` | 请假列表；支持筛选、`page`、`pageSize`，按创建时间倒序 |
| POST | `/expense-claims` | 创建报销单 |
| GET | `/expense-claims` | 报销列表；支持请假列表筛选项、金额区间、`page`、`pageSize`，按创建时间倒序 |
| POST | `/expense-claims/{id}/submit` | 提交报销单 |
| POST | `/expense-claims/{id}/payment` | 要求 `EXPENSE_PAY`；登记全额付款及当前付款人上传的凭证 |
| POST | `/expense-tasks/{id}/transfer` | 转办报销审批任务；请求体含 `assigneeId`、必填 `comment` |
| GET | `/expense-tasks/done` | 我的已办报销审批任务 |
| GET/POST | `/travel-requests` | 出差服务端分页列表/创建草稿；支持关键字、状态、申请人和日期筛选 |
| GET/PATCH/DELETE | `/travel-requests/{id}` | 出差详情/按版本编辑/删除允许状态单据 |
| POST | `/travel-requests/{id}/submit` | 提交出差；按日历天数匹配 Travel 流程并阻止有效日期重叠 |
| POST | `/travel-requests/{id}/withdraw` | 首个审批节点未处理时撤回出差 |
| GET | `/travel-tasks/my` | 我的出差待办 |
| GET | `/travel-tasks/done` | 我的已办出差审批任务 |
| POST | `/travel-tasks/{id}/{approve\|reject\|transfer}` | 出差同意、驳回或转办 |
| GET | `/notifications/my` | 当前用户通知列表 |
| GET | `/hr/employees` | 权限化花名册；支持关键字、部门、人事状态、用工类型和分页 |
| GET | `/hr/employees/me` | 当前用户完整人事档案和生命周期事件 |
| GET | `/hr/employees/{id}` | 本人、管理链上级或范围授权用户读取档案详情 |
| PUT | `/hr/employees/{id}` | 要求 `PERSONNEL_MANAGE`；按版本更新档案、组织关系和生命周期 |
| GET/PUT | `/attendance/shifts[/{id}]` | 查询班次；要求 `ATTENDANCE_MANAGE` 按版本维护班次 |
| GET | `/attendance/records` | 按月份、员工、部门、状态和关键字筛选的权限化分页考勤列表 |
| GET | `/attendance/records/{id}` | 本人、管理链上级或 Attendance 范围授权用户查看考勤详情与申诉历史 |
| POST | `/attendance/import` | 要求 `ATTENDANCE_MANAGE`；批量新增/更新打卡并重新识别异常 |
| POST | `/attendance/demo-data/{month}` | 幂等补齐缺失模拟考勤，不覆盖已有记录 |
| POST | `/attendance/records/{id}/appeals` | 员工对本人异常记录提交原因和附件 |
| POST | `/attendance/appeals/{id}/review` | 要求 `ATTENDANCE_MANAGE`；通过或驳回申诉 |
| GET | `/attendance/monthly-summary` | 按相同数据范围返回员工月度汇总 |
| GET | `/attendance/month-lock?month={date}` | 返回月份是否封账及最近封账/解封记录 |
| POST | `/attendance/month-locks/{month}/lock` | 要求 `ATTENDANCE_MANAGE`；无待审申诉时按版本封账 |
| POST | `/attendance/month-locks/{month}/unlock` | 要求 `ATTENDANCE_MANAGE`；按版本和原因解封 |
| GET/POST | `/hr/contracts` | 按独立 Contract 范围分页查询合同台账/创建合同草稿 |
| GET/PUT | `/hr/contracts/{id}` | 本人或 Contract 范围授权用户查看；HR 按版本编辑草稿 |
| POST | `/hr/contracts/{id}/{activate\|renew\|terminate}` | 激活签署合同、创建续签版本或登记终止 |
| GET | `/hr/contracts/alerts/summary` | 返回当前用户权限范围内的未签、到期和法务评估风险聚合 |
| POST | `/hr/contracts/{id}/alerts/acknowledge` | 确认当前 90/60/30/7 天预警档位 |
| POST | `/hr/contracts/demo-data` | 仅开发环境允许，幂等补齐模拟合同 |
| POST | `/files` | 上传附件；multipart 字段为 `file`，白名单类型且单文件最大 20MB |
| GET | `/files/{id}?resourceType={leave\|expense\|travel\|attendance\|contract}&resourceId={id}` | 经关联业务及独立数据权限校验后下载附件，并记录审计 |

## 3. 错误码

| code | 含义 |
|---|---|
| `AUTH_001` | 未登录或令牌无效 |
| `AUTH_002` | 无操作权限 |
| `DATA_001` | 资源不存在或无数据权限 |
| `STATE_001` | 当前状态不允许该操作 |
| `VALIDATION_001` | 字段校验失败 |
| `DUPLICATE_001` | 幂等键或业务编号重复 |
| `CONFLICT_001` | 数据版本冲突 |
| `CONCURRENCY_001` | 部门等管理资源的乐观版本冲突 |
| `FLOW_001` | 无法解析审批人或流程路由 |
| `FLOW_003` | 流程适用范围与同优先级、同具体度的已发布流程冲突 |
| `FILE_001` | 文件类型、大小或安全校验失败 |
| `FILE_002` | 文件大小超出限制 |
| `FILE_003` | 文件保存失败 |
| `FILE_004` | 文件内容缺失 |
| `FILE_005` | 附件不存在、重复或不属于当前用户 |
| `TRAVEL_001` | 出差行程字段、日期或跨度不合法 |
| `TRAVEL_002` | 与本人有效出差日期重叠 |
| `TRAVEL_003` | 同行人不合法 |
| `TRAVEL_004` | 报销关联的出差不存在、不属于本人或未批准 |
| `PERSONNEL_001` | 人事档案字段不合法 |
| `PERSONNEL_002` | 工号重复 |
| `PERSONNEL_003` | 人事生命周期状态与日期不一致 |
| `ATTENDANCE_001` | 班次、月份、打卡时间或申诉字段不合法 |
| `ATTENDANCE_002` | 同一考勤记录已有待审核申诉 |
| `ATTENDANCE_003` | 班次或考勤记录当前状态不允许操作 |
| `ATTENDANCE_004` | 月份已封账，禁止导入、生成、申诉或审核 |
| `ATTENDANCE_005` | 月份仍有待审核申诉，不能封账 |
| `CONTRACT_001` | 合同字段或日期不合法 |
| `CONTRACT_002` | 有效合同日期重叠 |
| `CONTRACT_003` | 试用期规则不合法或重复约定 |
| `CONTRACT_004` | 合同当前状态不允许操作 |
| `CONTRACT_005` | 缺少签署附件或附件归属错误 |
| `SYSTEM_001` | 可重试的系统异常 |

## 4. 提交报销示例

`POST /api/v1/expense-claims/{id}/submit`

服务端重新校验：单据归属、当前状态、明细数量、金额精度、费用日期、发票条件、预算规则和流程可用性。成功返回单据状态、流程实例 ID、首个待办摘要和 `requestId`；通知失败不回滚已提交事务，但进入重试队列。

## 5. 安全要求

资源 ID 不代表访问权限；每个详情、下载、审批和导出接口都要进行服务端数据权限校验。导出和文件下载记录审计，敏感字段按角色脱敏。

付款登记由服务端校验 `EXPENSE_PAY`、单据状态、付款日期、方式、全额金额、流水号和凭证归属。付款凭证先通过 `/files` 上传，随后以文件 ID 写入付款记录；下载时除报销明细附件外，也允许读取该报销记录关联的付款凭证，但必须先通过同一报销详情数据权限。

审计日志查询由服务端强制校验系统管理员角色，前端隐藏菜单不作为授权依据。日期筛选按租户时区（演示租户为中国标准时间）转换后查询 UTC 数据，分页与排序在数据库执行。

流程定义查询和维护仅限系统管理员。创建/更新请求包含 `priority`（0–1000）、`departmentIds` 和请假业务可用的 `leaveTypes`，空范围分别表示全公司和全部假别；部门范围自动包含下级部门。定义由 `process_definition`、适用范围 `process_scope`、条件规则 `process_rule` 和顺序审批节点 `process_node` 组成；默认支持 `DIRECT_MANAGER`、`ROLE:{角色}` 与 `USER:{用户ID}` 三类审批人规则。运行时按优先级、具体度、版本和发布时间依次选择；同业务、同优先级、同具体度且范围相交的不同流程编码禁止同时发布。发布时归档同编码的旧发布版本，已发布/归档版本不可修改。请假和报销提交后保存流程定义 ID、编码和版本，新版本只影响后续提交。

审批委托支持 `All`、`Leave`、`Expense`、`Travel` 四种业务范围，结束时间必须晚于当前时间，单次跨度最多 180 天。同一委托人在时间重叠且业务范围相交时不得创建多条有效规则。任务创建时解析一次委托并保存实际审批人、原审批人和委托 ID；取消规则只影响此后创建的任务。

请假、报销与出差每次提交都创建新的 `flow_instance`，任务绑定当前实例，单据同时返回 `currentFlowInstanceId` 和按提交次数排序的 `flowInstances`。只有当前实例中最小的未处理顺序节点进入待办；同意后激活下一节点，驳回时取消其余未处理节点并终止实例。`flow_action` 只追加提交、同意、驳回、转办和撤回事件，驳回重提不得删除旧实例或旧意见。`GET /flow/instances/{id}` 必须复用关联单据的数据权限，不得仅凭实例 ID 放行。

请假和报销创建/编辑请求可传 `copyRecipientIds`，最多 20 个同租户 ACTIVE 用户，自动去重且不能包含发起人。抄送记录在流程审批完成或申请撤回前不可查询，也不能赋予详情或附件权限；激活后进入 `/flow/copies/my` 并发送站内通知。`read` 接口仅记录抄送事项的独立已阅时间，其他用户调用返回 `DATA_001`。

除健康检查、登录、刷新、退出和开发演示账号列表外，所有接口必须携带 `Authorization: Bearer <token>`。服务端验证 HS256 签名、签发方、受众、有效期、租户、JWT 会话 ID、数据库中的 ACTIVE 账号和有效会话；不再接受 `X-Demo-User` 或客户端传入身份。密码使用每账号随机盐和 PBKDF2-SHA256 校验；连续 5 次失败锁定 15 分钟，成功登录或管理员重置密码后清除失败状态。JWT 签名密钥从环境配置注入。

访问令牌默认 15 分钟且前端仅驻内存。刷新 Cookie 为 HttpOnly、SameSite=Lax、路径限定 `/api/v1/auth`，HTTPS 环境启用 Secure；刷新令牌数据库仅存摘要并每次轮换。退出、密码重置、账号停用或安全角色变化必须撤销服务端会话，使已签发访问令牌立即失效。前端并发刷新合并为单一请求，401 刷新成功后使用原 `Idempotency-Key` 重试一次。

角色数据范围按 `Leave`、`Expense`、`Travel`、`Personnel`、`Attendance`、`Contract` 独立校验。范围支持 `SELF`、`DEPARTMENT`、`DEPARTMENT_AND_CHILDREN`、`COMPANY`；跨用户范围必须具有对应范围查看权限。服务端列表和详情使用同一判定函数，财务报销权限不得用于请假、人事、考勤或合同。劳动合同是敏感例外，直属上下级组织关系不会自动授予下级合同查看权。

劳动合同激活必须有有效签订日期和至少一份当前操作人上传或原合同已保留的签署附件；续签创建新记录并将旧合同标记为 `SUPERSEDED`。后台任务按到期档位向有 `CONTRACT_MANAGE` 且范围覆盖员工的用户发送站内通知，`contract_alert_delivery` 唯一约束负责跨重启去重。生产环境默认关闭模拟数据接口。

人事档案更新由服务端校验 `PERSONNEL_MANAGE`、`Personnel` 数据范围、岗位与部门一致性、管理链循环、用工类型、生命周期日期和乐观版本。`TERMINATED` 同步停用账号并撤销全部会话；恢复在职只启用账号，不恢复旧会话。每次更新向 `personnel_event` 追加事件并写 `PersonnelProfile` 审计日志，不允许覆盖或删除历史事件。

考勤导入由服务端校验 `ATTENDANCE_MANAGE`、`Attendance` 数据范围、员工状态、员工日期唯一性和中国时区打卡日期，再结合默认班次、工作日历和已完成请假计算状态。模拟生成只补缺；申诉仅允许记录本人提交，HR 审核通过保留 `original_status` 并标记 `CORRECTED`。HR 只能在没有待审申诉时封账，封账后导入、模拟生成、员工申诉和 HR 审核均由服务端拒绝；解封要求乐观版本和原因并完整审计。原始打卡、申诉、审核、通知、封账状态和审计均持久化。

用户管理 API 由服务端校验 `USER_MANAGE`，普通员工直接调用返回 `AUTH_002`。用户 ID 全租户唯一且创建后不可修改；更新请求携带 `version`，并发版本不一致返回 `CONFLICT_001`。系统禁止管理员停用自己或移除自身用户管理权限，创建、资料/角色更新和密码重置均写入审计日志。

部门管理 API 由服务端校验 `ORG_MANAGE`，普通员工直接调用返回 `AUTH_002`。创建部门必须选择上级；更新请求携带 `version`，并发版本不一致返回 `CONCURRENCY_001`。系统禁止循环层级、超过 8 层、删除预置部门、删除有下级或有用户的部门。新增、更新和删除均写入 `Department` 审计日志；移动部门会撤销租户现有会话，客户端下一次请求进入重新登录流程。

岗位管理 API 同样校验 `ORG_MANAGE`。岗位必须属于现有部门，同部门名称唯一，状态仅支持 `ACTIVE`/`DISABLED`；已分配岗位不可跨部门移动、停用或删除。用户新建/更新请求可传可空 `positionId`，服务端校验岗位启用且与 `departmentId` 一致；岗位变化视为组织关系变化并撤销该用户的有效会话。岗位增删改写入 `Position` 审计日志。

公告管理 API 校验 `ANNOUNCEMENT_MANAGE`，默认授予 HR/行政和系统管理员。公告标题 1–200 字、正文 1–10000 字，有效期为空表示长期有效，否则必须晚于当前/发布时间。草稿创建和更新、发布、撤回、员工确认已读均写 `Announcement` 审计日志；管理更新、发布和撤回都携带版本并返回 `CONCURRENCY_001` 防止并发覆盖。
