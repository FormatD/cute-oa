# MVP 用例测试报告

## 1. 执行范围

本报告覆盖当前已实现的 PostgreSQL 用户/账号/部门树/岗位/人事档案与生命周期、考勤班次/打卡/异常申诉/月报、劳动合同/续签/预警派发、角色/权限、短期 JWT、TOTP MFA、刷新令牌轮换、服务端会话撤销、登录失败锁定、用户与组织架构管理、权限路由与服务端授权，以及请假、报销、流程实例与永久轨迹、审批委托、流程抄送、统一事项中心、角色化工作台、通知、工作日历、附件、幂等和持久化能力。报告不将尚未实现的完整多租户、对象存储和高级流程节点误记为通过。

## 2. 自动化用例覆盖

| 编号 | 用例 | 自动化位置 | 当前结论 |
|---|---|---|---|
| TC-AUTH-001 | 有效账号登录并签发 JWT；错误密码、停用账号和篡改令牌被拒绝 | `Oa.Domain.Tests` + 实时 API 检查 | 通过 |
| TC-AUTH-002 | 未登录访问转登录页、登录后恢复原路由、退出清除会话、普通员工无法进入管理员路由 | 内置浏览器冒烟检查 | 通过 |
| TC-AUTH-003 | 刷新令牌仅存 SHA-256 摘要、轮换后旧令牌被拒绝并撤销会话 | `Oa.Postgres.Tests` + 实时 API 检查 | 通过 |
| TC-AUTH-004 | 登录设备分页/当前标识、单会话撤销、退出后旧 JWT 立即被拒绝 | `Oa.Postgres.Tests` + 实时 API 检查 | 通过 |
| TC-AUTH-005 | 密码重置和安全角色变化撤销既有会话 | `Oa.Postgres.Tests` | 通过 |
| TC-AUTH-006 | 账号、来源 IP、账号+IP 组合固定窗口限流，分散来源/轮换账号不能绕过，超限返回重试时间且窗口到期恢复；生产仅信任明确代理地址和一跳对称转发头 | `Oa.Domain.Tests` + 实时 API 检查 | 通过 |
| TC-AUTH-007 | 敏感权限强制 TOTP 绑定、AES-GCM 密钥密文、恢复码仅摘要存储/一次性消费、时间步与挑战防重放、连续五次错误锁定和管理员重置撤销会话 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` + 前端生产构建 | 通过 |
| TC-IAM-001 | 默认部门、用户、账号、角色和权限初始化并跨 DbContext 保留 | `Oa.Postgres.Tests` | 通过 |
| TC-IAM-002 | 管理员创建/筛选/更新用户和重置密码；普通员工调用被拒绝 | `Oa.Postgres.Tests` | 通过 |
| TC-IAM-003 | 密码随机盐哈希、无效/停用/锁定账号等成本校验、5 个独立 DbContext 同时错误密码的原子累计锁定、重置解锁及新旧密码校验 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-IAM-010 | 新建、管理员重置和生产引导账号强制首次改密；临时密码不签发会话；正式密码强度、禁止复用、最新/过期/单次挑战、审计、会话撤销及 MFA 衔接 | `Oa.Postgres.Tests` + 前端生产构建 | 通过 |
| TC-IAM-004 | 多角色权限聚合、角色替换、管理员自停用保护和用户管理审计 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-IAM-005 | 权限定义查询、自定义角色增删改、重复编码、系统角色/已分配角色删除保护和管理员自锁保护 | `Oa.Postgres.Tests` + 前端生产构建 | 通过 |
| TC-IAM-006 | 角色权限增量更新后持久化，并立即撤销所有受影响用户的有效会话 | `Oa.Postgres.Tests` | 通过 |
| TC-IAM-007 | 请假/报销/出差/人事/考勤/劳动合同四级数据范围持久化、权限配对、部门隔离、多角色聚合及范围变化会话撤销 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-IAM-008 | 财务全公司报销范围不外溢到请假；直属上级组织关系可见性保留 | `Oa.Domain.Tests` | 通过 |
| TC-IAM-009 | 演示身份仅在 Development 初始化；空生产库未引导拒绝、强密码管理员一次性创建、审计、重复引导拒绝及关闭引导后重启校验 | `Oa.Domain.Tests` + 隔离 PostgreSQL schema 集成测试 | 通过 |
| TC-HR-001 | 普通员工仅本人、上级查看管理链下属、HR 全公司花名册 | `Oa.Postgres.Tests` + 内置浏览器冒烟检查 | 通过 |
| TC-HR-002 | 普通员工越权编辑拒绝、HR 编辑、字段/生命周期日期校验和社会工龄同步 | `Oa.Postgres.Tests` | 通过 |
| TC-HR-003 | 人事档案、只追加生命周期事件和审计日志跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-HR-004 | 花名册、独立详情、HR 编辑表单和 390px 无整页横向溢出 | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-HR-005 | 入职/转正/调动/离职标准办理单、员工/上级/HR/财务/IT/行政职责自动分派、显式配置覆盖与无效配置失败关闭、本人/任务负责人权限、任务乐观锁、未完成任务阻止办结及审计持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-HR-006 | 进入离职办理强制计划日期和原因；历史其他日期完成单不放行；匹配日期清单完成后才允许停号 | `Oa.Postgres.Tests` | 通过 |
| TC-HR-007 | 员工办理台账、服务端分页、详情、任务处理、办结/取消弹窗及路由引用 | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-HR-008 | 员工事项临期、逾期、升级接收人规则，通知/审计落库、数据库唯一去重、重复执行及跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-HR-009 | 法定年假 1/10/20 年档位、当年入职折算、年度/假别隔离、跨年拆分、HR 人工调整权限/审计、请假与余额双重乐观锁、并发提交整体回滚及跨 DbContext 持久化 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` + 前端生产构建 | 通过 |
| TC-HR-010 | 两个独立数据库会话同时更新人事档案/员工主数据和同一员工办理任务；首个提交成功，陈旧提交返回 `CONCURRENCY_001`，不静默覆盖或重复处理 | `Oa.Postgres.Tests` | 通过 |
| TC-HR-011 | 生产创建用户缺少真实建档字段时整体拒绝；成功时用户、凭据、角色、档案、初始事件和审计同时持久化；花名册/详情读取不产生写入；生产启动拒绝缺档账号 | `Oa.Postgres.Tests` + 前端生产构建 | 通过 |
| TC-HR-012 | 离线历史档案补录默认预检零写入、混合非法批次零写入、显式应用整批档案/事件/审计一致、已有档案禁止覆盖及 CLI secret 文件端到端执行 | `Oa.Postgres.Tests` + `scripts/verify-hr-profile-import.sh` | 通过 |
| TC-HR-013 | 250 人花名册由 PostgreSQL 完成权限约束、组合筛选、排序和第 3 页分页；当前页关联名称无 N+1，固定两条 SQL，LIKE 通配符按字面量处理 | `Oa.Postgres.Tests` 命令拦截器 | 通过 |
| TC-HR-014 | 花名册导出使用独立权限、复用人事数据范围和筛选；普通员工/负责人越权拒绝，超限失败关闭，UTF-8 BOM 与公式注入防护正确且写审计 | `Oa.Postgres.Tests` + 前端生产构建 | 通过 |
| TC-ATTENDANCE-001 | 普通员工仅本人、上级查看下属、HR 公司范围；普通员工越权生成/导入被拒绝 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-002 | 模拟数据只补缺且重复生成幂等；人工导入更新同日记录并正确识别迟到与宽限分钟 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-003 | 本人申诉、他人代申诉拒绝、待审防重复、HR 审核和原始异常保留 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-004 | 班次权限/乐观锁、月报同步、记录/申诉/审计跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-005 | 独立列表/详情、状态筛选、27 页分页、模拟数据操作和 390px 无整页横向溢出 | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-ATTENDANCE-006 | 普通员工封账拒绝、待审申诉阻止封账、封账后导入/生成/申诉拒绝、解封乐观锁、重新封账及审计跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-007 | 班次、同日考勤、申诉审核和月度封账的数据库并发令牌；独立 DbContext 陈旧提交统一回滚；同一考勤记录待审申诉部分唯一索引 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-008 | 仅公司范围考勤管理员可封账/解封；封账生成只追加 SHA-256 月报快照，按调用人范围读取；安全 CSV 下载与审计；解封重算产生递增版本；快照篡改失败关闭；升级前历史锁迁移补建版本 1 | `Oa.Postgres.Tests` + 前端生产构建 | 通过 |
| TC-CONTRACT-001 | 普通员工越权创建拒绝、本人可查看、直属上级不能仅凭管理链查看下属合同、HR 公司范围 | `Oa.Postgres.Tests` | 通过 |
| TC-CONTRACT-002 | 签订日期/附件激活门槛、附件归属、合同日期重叠和试用期法定上限 | `Oa.Postgres.Tests` | 通过 |
| TC-CONTRACT-003 | 续签创建新记录、旧合同进入替代状态、事件/附件/审计跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-CONTRACT-004 | 30 天预警分档、人工确认、后台派发及重复执行数据库去重 | `Oa.Postgres.Tests` | 通过 |
| TC-CONTRACT-005 | 台账/详情/新建草稿、工作台去重风险数、角色页权限、390px 无横向溢出和浏览器控制台零错误 | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-CONTRACT-006 | 真实 API 下员工只见本人合同，直属上级只见本人且不能读取下属合同；临时测试会话已注销 | 实时 API 检查 | 通过 |
| TC-CONTRACT-007 | `Version` 数据库并发令牌、`btree_gist` 有效日期排斥约束、两个独立服务实例并发激活重叠合同、陈旧草稿更新及并发续签唯一版本链 | `Oa.Postgres.Tests` | 通过 |
| TC-OPS-001 | 模拟数据生产默认关闭、数据库/存储就绪检查和后台预警配置 | 后端构建 + PostgreSQL 集成测试 | 通过（配置级） |
| TC-OPS-002 | PostgreSQL 自定义格式备份、拒绝覆盖既有文件/演示库、随机隔离库恢复、关键计数比对及临时资源清理 | `scripts/verify-backup-restore.sh` | 通过（本地演练） |
| TC-OPS-003 | 开发环境豁免、合规生产配置接受、不安全生产配置在监听前聚合拒绝，以及 API 安全响应头和认证响应禁止缓存 | `Oa.Domain.Tests` + 生产启动检查 + 实时 HTTP 检查 | 通过 |
| TC-OPS-004 | 生产前端同源 API、Compose secret 文件映射、明确可信代理、API/Web 非 root 和只读运行约束、TLS 入口及容器健康链 | `scripts/verify-production-compose.sh` + `scripts/verify-containers.sh` | 通过（本地容器演练） |
| TC-LEAVE-001 | 周末、半天、法定节假日及调休工作日计算 | `Oa.Domain.Tests` | 通过 |
| TC-LEAVE-002 | 草稿编辑、版本冲突、提交、审批、余额冻结/扣减 | `Oa.Domain.Tests` | 通过 |
| TC-LEAVE-003 | 超过 3 天增加总经理节点、禁止越级审批、驳回与撤回 | `Oa.Domain.Tests` | 通过 |
| TC-FLOW-001 | 请假、报销审批任务转办后原审批人不可处理、接收人可继续处理 | `Oa.Domain.Tests` | 通过 |
| TC-LIST-001 | 请假/报销服务端筛选、创建时间倒序、分页切片与超范围页码处理 | `Oa.Domain.Tests` + 实时 API 检查 | 通过 |
| TC-ORG-001 | 部门、员工、岗位及直属上级目录正确，接口不暴露社会工龄 | `Oa.Domain.Tests` + 实时 API 检查 | 通过 |
| TC-ORG-002 | 普通员工越权、重复编码/名称、循环层级、系统部门/下级/用户占用删除保护和乐观锁 | `Oa.Postgres.Tests` | 通过 |
| TC-ORG-003 | 部门创建、移动、删除跨上下文持久化并写审计，层级变化撤销租户会话 | `Oa.Postgres.Tests` | 通过 |
| TC-ORG-004 | 用户直属上级关系禁止形成间接经理循环 | `Oa.Postgres.Tests` | 通过 |
| TC-POSITION-001 | 岗位默认数据、普通员工越权、重复编码/同部门名称、部门匹配和乐观锁 | `Oa.Postgres.Tests` | 通过 |
| TC-POSITION-002 | 已分配岗位的移动、停用、删除保护，系统岗位删除保护及解除分配 | `Oa.Postgres.Tests` | 通过 |
| TC-POSITION-003 | 用户岗位分配、会话撤销和岗位增删改审计跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-ANNOUNCEMENT-001 | HR 管理权限、普通员工越权、标题/正文/有效期校验、草稿隔离和发布后只读 | `Oa.Postgres.Tests` | 通过 |
| TC-ANNOUNCEMENT-002 | 公告按发布时间倒序服务端分页、确认已读幂等及用户隔离 | `Oa.Postgres.Tests` | 通过 |
| TC-ANNOUNCEMENT-003 | 发布/撤回乐观锁、撤回后员工不可见、管理员追溯和全动作审计 | `Oa.Postgres.Tests` | 通过 |
| TC-AUDIT-001 | 系统管理员可筛选、分页查询审计日志，普通员工被服务端拒绝 | `Oa.Postgres.Tests` | 通过 |
| TC-LEAVE-004 | 申请人、审批人和无权限 HR 的详情访问范围 | `Oa.Domain.Tests` | 通过 |
| TC-LEAVE-005 | 独立服务实例并发提交同一年度余额及并发审批同一任务；陈旧操作返回 `CONCURRENCY_001`，单据/任务/余额整体回滚且审计与流程动作不重复 | `Oa.Postgres.Tests` | 通过 |
| TC-EXP-001 | 草稿编辑、版本冲突、金额路由与多级审批 | `Oa.Domain.Tests` | 通过 |
| TC-EXP-002 | 重复票据、撤回、删除草稿、等额付款 | `Oa.Domain.Tests` | 通过 |
| TC-EXP-003 | 报销审批任务关联报销单，支持详情跳转 | `Oa.Postgres.Tests` | 通过 |
| TC-EXP-004 | 付款凭证归属校验、付款记录/文件跨 DbContext 持久化及付款审计 | `Oa.Postgres.Tests` | 通过 |
| TC-DATA-001 | 请假、报销、任务、审计、幂等键跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-DATA-002 | PostgreSQL advisory lock 串行化相同幂等键；第二实例读取首个成功响应；同键不同请求指纹冲突；HR 业务与幂等响应同事务提交/回滚且无半成品 | `Oa.Postgres.Tests` + `scripts/verify-live-hr-idempotency.sh` | 通过 |
| TC-CALENDAR-001 | HR 维护企业非工作日并影响请假创建 | `Oa.Postgres.Tests` | 通过 |
| TC-NOTIFY-001 | 审批待办通知生成与标记已读 | `Oa.Postgres.Tests` | 通过 |
| TC-NOTIFY-002 | 数据库触发器注入站内通知写入故障；请假提交失败且单据、余额、任务、流程、审计和通知全部回滚 | `Oa.Postgres.Tests` | 通过 |
| TC-FILE-001 | PDF 附件上传、请假单关联、跨 DbContext 读取内容 | `Oa.Postgres.Tests` | 通过 |
| TC-FILE-002 | 安全文件扫描放行；伪装扩展名、感染文件与扫描服务不可用均失败关闭，不写元数据、清理隔离文件并持久化安全审计 | `Oa.Postgres.Tests` + `scripts/verify-clamav.sh` | 通过（含官方 ClamAV 容器、EICAR、中断与恢复） |
| TC-FLOW-002 | 请假、报销转办任务跨 DbContext 保留接收人 | `Oa.Postgres.Tests` | 通过 |
| TC-FLOW-003 | 已处理的请假、报销任务进入我的已办并跨 DbContext 保留 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-PROCESS-001 | 默认流程初始化、管理员权限、发布版本只读、复制编辑发布与旧/新单据版本隔离 | `Oa.Postgres.Tests` | 通过 |
| TC-PROCESS-002 | 请假、报销提交绑定已发布流程编码和版本，并按配置解析审批节点 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-PROCESS-003 | 部门范围包含下级部门、假别过滤、优先级覆盖、重叠范围发布拒绝及选择结果跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-DELEGATE-001 | 委托创建/查询、重叠拒绝、越权取消、正常取消与审计持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-DELEGATE-002 | 有效委托替换实际审批人并保留原审批人，重复实际节点去重，取消后新任务恢复 | `Oa.Postgres.Tests` | 通过 |
| TC-INSTANCE-001 | 每次提交生成独立流程实例，任务绑定当前实例，状态随完成/驳回/撤回同步 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-INSTANCE-002 | 驳回重提保留旧实例与意见并创建递增的新实例，跨 DbContext 不丢失 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-INSTANCE-003 | 未来顺序节点不提前进入待办，驳回取消其余未处理节点 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-INSTANCE-004 | 转办和审批动作写入只追加轨迹，并可跨 DbContext 查询 | `Oa.Postgres.Tests` | 通过 |
| TC-COPY-001 | 抄送人校验、草稿保存，请假与报销完成前不激活、完成后生成待阅读和站内通知 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-COPY-002 | 抄送人完成后获得详情/附件只读权限，其他用户不能代读，已阅状态跨 DbContext 保留 | `Oa.Postgres.Tests` | 通过 |
| TC-WEB-001 | 管理后台路由、工作台事项/通知聚合、快捷入口、表格分页、详情入口与移动导航 | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-WEB-002 | 同意、驳回、转办使用系统弹窗；必填校验不触发请求且不再依赖原生 prompt | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-WEB-003 | 工作台最新公告、公告详情确认已读、公告管理必填校验及 390px 响应式布局 | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-WORKITEM-001 | 员工 API 发起并提交请假、主管从统一事项中心审批、待办消失且已办可查 | Playwright + 真实 Vue/API/PostgreSQL | 通过 |
| TC-WORKITEM-002 | 待办/已办/我发起/阅读/风险五页签、角色摘要、服务端筛选分页、详情路由及 390px 折叠菜单无整页横向滚动 | 前端生产构建 + Playwright + `Oa.Postgres.Tests` | 通过 |
| TC-WORKITEM-003 | 普通员工合同风险仅含本人、现有审批只暴露当前节点、聚合摘要不旁路泄露 | Playwright API 检查 + `Oa.Postgres.Tests` | 通过 |
| TC-DOCUMENT-REVISION-001 | 已签收旧版本的制度发布新版本后重新进入待签收列表 | `Oa.Postgres.Tests` | 通过 |

## 3. 执行命令

```bash
bash scripts/verify.sh
bash scripts/verify-postgres.sh
npm run test:e2e --prefix frontend
bash scripts/verify-backup-restore.sh
bash scripts/verify-containers.sh
bash scripts/verify-clamav.sh
```

第二条命令只重建隔离的 `oa_test` 数据库；第三条把演示库只读备份到项目内临时文件，恢复到随机 `oa_restore_*` 数据库并校验后清理。两者都不会重置演示数据库 `oa`。第四条要求本地已有 `cute-oa-api:local`、`cute-oa-web:local` 镜像，只创建并清理带随机后缀的临时容器、匿名卷和网络。第五条使用独立 `oa_clamav_test_*` 数据库、临时卷和官方 ClamAV 容器，验证安全 PDF、DOCX 内 EICAR、扫描中断 503、审计及自动恢复，结束后清理全部临时资源。

## 4. 未验收项

以下需求尚未实现，因此不能纳入通过范围：

- WebAuthn/硬件安全密钥、企业 SSO、字段权限、指定组织范围和完整多租户隔离；TOTP MFA 与应用内登录限流已完成，分布式共享限流仍待多实例部署时补充；
- 对象存储、文件预览/版本和生命周期管理，以及预发布目标环境 ClamAV/EICAR 运维签字；本地官方 ClamAV 容器、实际上传、隔离、审计、失败关闭和恢复回归已通过；
- 会签、加签与通用审批 SLA；员工生命周期办理任务已具备自然日临期、逾期和单级升级提醒，但尚无节假日 SLA 与多级升级链；
- 超过 10000 行的大批量异步导出、系统性高并发/负载测试，以及完整安全/API 契约测试；当前已提供受控同步花名册导出，核心 HR 写操作已具备双会话并发回归，但尚未进行目标容量压测；
- 预发布备份恢复签字、外部监控告警、镜像仓库扫描/签名和发布回滚演练；应用已提供生产容器模板、存活/就绪端点和后台任务日志，但尚未接入实际生产平台。

这些项目完成后，应新增相应的 API、端到端、安全和运维测试，再进入上线验收。
