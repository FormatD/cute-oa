# xxx公司 OA

当前开发版本覆盖持久化用户/账号/角色/权限、组织目录、审批流程、请假、出差、费用报销、人事档案、员工生命周期办理清单、法定年假年度额度、基础考勤与劳动合同台账，采用 Vue 3 + TypeScript 前端、ASP.NET Core 后端和 PostgreSQL。

## 目录

- `backend/`：ASP.NET Core API 与领域逻辑
- `frontend/`：Vue 响应式界面
- `tests/`：无需外部测试框架的领域测试
- `tools/`：生产维护所需的受控离线工具；人事档案补录默认只预检
- `docs/oa/`：产品与研发基线

### 前端分层

- `frontend/src/api/http.ts` 统一处理请求、访问令牌刷新和错误响应；`api/client.ts` 为业务 Store 提供共享鉴权客户端；`api/modules/` 按业务域封装接口，页面不直接发起 HTTP 请求。
- `frontend/src/stores/` 使用 Pinia 按业务域维护状态；工作台摘要、员工候选目录和工作日历分别由 `dashboard`、`employee-directory`、`calendar` store 管理。
- `frontend/src/stores/workspace.ts` 只负责登录后的数据初始化、刷新和退出清理；`app.ts` 是兼容现有页面的轻量动作门面，不再持有业务状态或直接调用 API。
- `frontend/src/views/` 只组合组件、路由参数与 store，不保存跨页面共享状态。

## 本地运行

1. 执行 `docker compose up -d postgres minio` 启动依赖服务。
2. 执行 `bash scripts/migrate-postgres.sh` 应用 PostgreSQL 迁移。
3. 执行 `ASPNETCORE_URLS=http://127.0.0.1:5234 dotnet run --project backend/Oa.Api` 启动 API。
4. 执行 `npm install --prefix frontend && npm run dev --prefix frontend` 启动前端。

演示身份只允许在 Development 初始化；生产库若包含演示账号会拒绝启动。空生产库必须通过独立 secret 执行一次性强密码管理员引导，引导完成后关闭开关并轮换 secret。

生产创建账号会同时建立真实人事档案。历史账号缺档时应用拒绝启动，应使用 `tools/Oa.HrProfileImport` 先预检再整批补录，禁止通过查询自动生成或手写临时 SQL；命令和 JSON 模板见[生产部署与文件安全运行手册](./docs/oa/21-生产部署与文件安全运行手册.md)。

开发阶段预置演示组织账号，通过登录页取得短期 JWT；统一初始密码为 `Oa@123456`。生产环境不返回演示账号列表，并对用户管理、人事、考勤和合同维护权限账号强制 TOTP 多因素认证。TOTP 密钥使用独立密钥进行 AES-GCM 加密，动态码和登录挑战不可重放；恢复码只显示一次、只保存摘要并一次性消费。长期会话使用 HttpOnly 刷新 Cookie，刷新令牌只以 SHA-256 摘要保存，访问令牌不写入浏览器本地存储。登录入口同时执行账号、来源 IP、账号与 IP 组合限流，超限返回 429/`AUTH_003` 和 `Retry-After`；账号连续 5 次密码错误会锁定 15 分钟，失败计数使用数据库原子更新。不存在、停用、空密码和锁定账号仍执行同等成本的 PBKDF2 校验，避免仅凭响应耗时识别账号状态。用户可在“登录设备”查看并撤销其他会话；退出、重置密码、停用账号或调整安全角色/角色权限会使相关旧会话立即失效。用户、账号、部门树、岗位、人事档案与生命周期、考勤班次/记录/申诉、劳动合同/事件/预警、角色、权限、数据范围、公告及阅读确认、登录会话和业务单据均持久化到 PostgreSQL，正常重启不会重置。岗位描述职责，安全角色授予权限，两者独立；角色页面支持 17 类服务端操作权限以及请假/报销/出差/采购/人事档案/考勤/劳动合同七类四级数据范围。劳动合同采用独立敏感数据范围，管理链上级不会自动获得下属合同查看权。HR/行政和系统管理员可维护人事档案、考勤、劳动合同与公司公告。本地 PostgreSQL 映射到 `localhost:5433`，避免与常见的系统 PostgreSQL 端口冲突。

开发签名密钥位于 `appsettings.Development.json`，仅供本机演示。部署到其他环境时必须分别通过 `Authentication__SigningKey` 和 `Authentication__MultiFactor__EncryptionKey` 注入 JWT 签名密钥与 32 字节 Base64 MFA 加密密钥，并避免提交或复用生产密钥。非开发环境会在监听端口前校验签名密钥、MFA 配置、PostgreSQL TLS/账号/连接池、HTTPS CORS、明确的 `AllowedHosts`、登录限流、可信反向代理、绝对存储路径和模拟数据开关；不合规时聚合报错并拒绝启动。

模拟考勤和模拟合同只在 Development 环境开启，生产配置默认关闭。API 提供统一安全响应头、`/health` 存活检查和 `/health/ready` 数据库、附件存储及恶意文件扫描就绪检查；合同到期预警由后台任务按 90/60/30/7 天档位幂等派发。生产部署前的剩余门禁见 [人力资源模块生产就绪清单](./docs/oa/20-人力资源模块生产就绪清单.md)。

员工办理模板按员工本人、直属上级以及 HR、财务、IT、行政职责自动分派；生产部署可通过 `PersonnelCases__CategoryAssignees__*` 固定责任人，配置指向停用或不存在账号时失败关闭。后台任务默认每小时按自然日派发提前 1 天临期、逾期 1 天和逾期 3 天升级站内提醒，并以任务、接收人、提醒类型的数据库唯一记录保证重启和重复扫描不重复发送。

## 验证

执行 `bash scripts/verify.sh`，依次验证后端编译、请假领域测试、Vue 生产构建和 Docker Compose 配置。

Docker 可用时，执行 `bash scripts/verify-postgres.sh` 验证 PostgreSQL 表结构、身份权限、部门树、岗位、人事档案、离线补档 CLI 的默认预检/整批应用/禁止覆盖、考勤和劳动合同约束、密码锁定/重置、刷新令牌轮换与会话撤销，以及请假、出差、报销、出差报销关联和采购申请（审批/下单/验收）的跨 DbContext 持久化。该脚本只重建隔离测试库 `oa_test`，不会重置演示库 `oa`。

执行 `bash scripts/verify-backup-restore.sh` 可把演示库只读备份到项目内临时文件，恢复到随机 `oa_restore_*` 隔离库，核对迁移和关键业务计数后自动清理；脚本明确拒绝覆盖演示库。生产演练步骤见 [PostgreSQL 备份恢复运行手册](./docs/oa/23-PostgreSQL备份恢复运行手册.md)。

## 生产容器候选

仓库提供 API 与 Web 多阶段 Dockerfile、`compose.production.yml` 和 `deploy/production.env.example`。API 与 Nginx 都以非 root 用户、只读根文件系统运行；生产前端默认通过当前 HTTPS 源访问 `/api/v1`。数据库连接、JWT 签名密钥和 TLS 私钥只通过 secret 文件挂载，生产 Compose 使用外部 PostgreSQL，不会创建或重置生产数据库。

先运行 `bash scripts/verify-production-compose.sh` 校验配置与前端生产产物；构建 `cute-oa-api:local`、`cute-oa-web:local` 后运行 `bash scripts/verify-containers.sh`，验证 TLS 入口、安全响应头和健康检查代理链；准备官方 ClamAV 镜像后运行 `bash scripts/verify-clamav.sh`，验证安全上传、EICAR 阻断、扫描中断失败关闭与恢复。完整部署步骤与网段调整要求见 [生产部署与文件安全运行手册](./docs/oa/21-生产部署与文件安全运行手册.md)。

## 附件存储

演示环境的附件保存在后端项目的 `storage/files` 目录。上传支持 PDF、JPG、JPEG、PNG、XLS、XLSX、DOC、DOCX，单文件最大 20MB；服务端会校验文件头或 Office 容器结构，不能只靠修改扩展名绕过。生产部署应通过 `Storage__Root` 配置挂载持久卷或替换为对象存储适配。

开发环境通过 `FileScanning__Mode=Disabled` 显式关闭扫描。非开发环境只接受 `FileScanning__Mode=ClamAv`：上传内容先写入不可关联的隔离临时文件，经 ClamAV 判定为安全后才进入正式存储；发现威胁返回 `FILE_006`，扫描服务不可用返回 HTTP 503/`FILE_007`，两种情况都删除临时文件并写安全审计。部署参数和验收步骤见 [生产部署与文件安全运行手册](./docs/oa/21-生产部署与文件安全运行手册.md)。
