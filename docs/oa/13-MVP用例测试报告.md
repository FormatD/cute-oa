# MVP 用例测试报告

## 1. 执行范围

本报告覆盖当前已实现的 PostgreSQL 用户/账号/部门树/岗位/人事档案与生命周期、考勤班次/打卡/异常申诉/月报、劳动合同/续签/预警派发、角色/权限、短期 JWT、刷新令牌轮换、服务端会话撤销、登录失败锁定、用户与组织架构管理、权限路由与服务端授权，以及请假、报销、流程实例与永久轨迹、审批委托、流程抄送、通知、工作日历、附件、幂等和持久化能力。报告不将尚未实现的 MFA、完整多租户、对象存储和高级流程节点误记为通过。

## 2. 自动化用例覆盖

| 编号 | 用例 | 自动化位置 | 当前结论 |
|---|---|---|---|
| TC-AUTH-001 | 有效账号登录并签发 JWT；错误密码、停用账号和篡改令牌被拒绝 | `Oa.Domain.Tests` + 实时 API 检查 | 通过 |
| TC-AUTH-002 | 未登录访问转登录页、登录后恢复原路由、退出清除会话、普通员工无法进入管理员路由 | 内置浏览器冒烟检查 | 通过 |
| TC-AUTH-003 | 刷新令牌仅存 SHA-256 摘要、轮换后旧令牌被拒绝并撤销会话 | `Oa.Postgres.Tests` + 实时 API 检查 | 通过 |
| TC-AUTH-004 | 登录设备分页/当前标识、单会话撤销、退出后旧 JWT 立即被拒绝 | `Oa.Postgres.Tests` + 实时 API 检查 | 通过 |
| TC-AUTH-005 | 密码重置和安全角色变化撤销既有会话 | `Oa.Postgres.Tests` | 通过 |
| TC-IAM-001 | 默认部门、用户、账号、角色和权限初始化并跨 DbContext 保留 | `Oa.Postgres.Tests` | 通过 |
| TC-IAM-002 | 管理员创建/筛选/更新用户和重置密码；普通员工调用被拒绝 | `Oa.Postgres.Tests` | 通过 |
| TC-IAM-003 | 密码随机盐哈希、5 次失败锁定、重置解锁及新旧密码校验 | `Oa.Postgres.Tests` | 通过 |
| TC-IAM-004 | 多角色权限聚合、角色替换、管理员自停用保护和用户管理审计 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-IAM-005 | 权限定义查询、自定义角色增删改、重复编码、系统角色/已分配角色删除保护和管理员自锁保护 | `Oa.Postgres.Tests` + 前端生产构建 | 通过 |
| TC-IAM-006 | 角色权限增量更新后持久化，并立即撤销所有受影响用户的有效会话 | `Oa.Postgres.Tests` | 通过 |
| TC-IAM-007 | 请假/报销/出差/人事/考勤/劳动合同四级数据范围持久化、权限配对、部门隔离、多角色聚合及范围变化会话撤销 | `Oa.Domain.Tests` + `Oa.Postgres.Tests` | 通过 |
| TC-IAM-008 | 财务全公司报销范围不外溢到请假；直属上级组织关系可见性保留 | `Oa.Domain.Tests` | 通过 |
| TC-HR-001 | 普通员工仅本人、上级查看管理链下属、HR 全公司花名册 | `Oa.Postgres.Tests` + 内置浏览器冒烟检查 | 通过 |
| TC-HR-002 | 普通员工越权编辑拒绝、HR 编辑、字段/生命周期日期校验和社会工龄同步 | `Oa.Postgres.Tests` | 通过 |
| TC-HR-003 | 人事档案、只追加生命周期事件和审计日志跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-HR-004 | 花名册、独立详情、HR 编辑表单和 390px 无整页横向溢出 | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-ATTENDANCE-001 | 普通员工仅本人、上级查看下属、HR 公司范围；普通员工越权生成/导入被拒绝 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-002 | 模拟数据只补缺且重复生成幂等；人工导入更新同日记录并正确识别迟到与宽限分钟 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-003 | 本人申诉、他人代申诉拒绝、待审防重复、HR 审核和原始异常保留 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-004 | 班次权限/乐观锁、月报同步、记录/申诉/审计跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-ATTENDANCE-005 | 独立列表/详情、状态筛选、27 页分页、模拟数据操作和 390px 无整页横向溢出 | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-ATTENDANCE-006 | 普通员工封账拒绝、待审申诉阻止封账、封账后导入/生成/申诉拒绝、解封乐观锁、重新封账及审计跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-CONTRACT-001 | 普通员工越权创建拒绝、本人可查看、直属上级不能仅凭管理链查看下属合同、HR 公司范围 | `Oa.Postgres.Tests` | 通过 |
| TC-CONTRACT-002 | 签订日期/附件激活门槛、附件归属、合同日期重叠和试用期法定上限 | `Oa.Postgres.Tests` | 通过 |
| TC-CONTRACT-003 | 续签创建新记录、旧合同进入替代状态、事件/附件/审计跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-CONTRACT-004 | 30 天预警分档、人工确认、后台派发及重复执行数据库去重 | `Oa.Postgres.Tests` | 通过 |
| TC-CONTRACT-005 | 台账/详情/新建草稿、工作台去重风险数、角色页权限、390px 无横向溢出和浏览器控制台零错误 | 前端生产构建 + 内置浏览器冒烟检查 | 通过 |
| TC-CONTRACT-006 | 真实 API 下员工只见本人合同，直属上级只见本人且不能读取下属合同；临时测试会话已注销 | 实时 API 检查 | 通过 |
| TC-OPS-001 | 模拟数据生产默认关闭、数据库/存储就绪检查和后台预警配置 | 后端构建 + PostgreSQL 集成测试 | 通过（配置级） |
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
| TC-EXP-001 | 草稿编辑、版本冲突、金额路由与多级审批 | `Oa.Domain.Tests` | 通过 |
| TC-EXP-002 | 重复票据、撤回、删除草稿、等额付款 | `Oa.Domain.Tests` | 通过 |
| TC-EXP-003 | 报销审批任务关联报销单，支持详情跳转 | `Oa.Postgres.Tests` | 通过 |
| TC-EXP-004 | 付款凭证归属校验、付款记录/文件跨 DbContext 持久化及付款审计 | `Oa.Postgres.Tests` | 通过 |
| TC-DATA-001 | 请假、报销、任务、审计、幂等键跨 DbContext 持久化 | `Oa.Postgres.Tests` | 通过 |
| TC-CALENDAR-001 | HR 维护企业非工作日并影响请假创建 | `Oa.Postgres.Tests` | 通过 |
| TC-NOTIFY-001 | 审批待办通知生成与标记已读 | `Oa.Postgres.Tests` | 通过 |
| TC-FILE-001 | PDF 附件上传、请假单关联、跨 DbContext 读取内容 | `Oa.Postgres.Tests` | 通过 |
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

## 3. 执行命令

```bash
bash scripts/verify.sh
bash scripts/verify-postgres.sh
```

第二条命令只重建隔离的 `oa_test` 数据库，不会修改演示数据库 `oa`。

## 4. 未验收项

以下需求尚未实现，因此不能纳入通过范围：

- MFA/验证码/IP 限流、字段权限、指定组织范围和完整多租户隔离；
- 对象存储、病毒扫描、文件预览/版本和生命周期管理；
- 会签、加签与审批 SLA；
- 异步导出及高并发/完整安全/API 契约测试；
- 备份恢复、外部监控告警、应用容器化和发布回滚演练；应用已提供存活/就绪端点和后台任务日志，但尚未接入实际生产平台。

这些项目完成后，应新增相应的 API、端到端、安全和运维测试，再进入上线验收。
