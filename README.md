# xxx公司 OA

当前开发版本覆盖持久化用户/账号/角色/权限、组织目录、审批流程、请假、出差、费用报销、人事档案、基础考勤与劳动合同台账，采用 Vue 3 + TypeScript 前端、ASP.NET Core 后端和 PostgreSQL。

## 目录

- `backend/`：ASP.NET Core API 与领域逻辑
- `frontend/`：Vue 响应式界面
- `tests/`：无需外部测试框架的领域测试
- `docs/oa/`：产品与研发基线

## 本地运行

1. 执行 `docker compose up -d postgres minio` 启动依赖服务。
2. 执行 `bash scripts/migrate-postgres.sh` 应用 PostgreSQL 迁移。
3. 执行 `ASPNETCORE_URLS=http://127.0.0.1:5234 dotnet run --project backend/Oa.Api` 启动 API。
4. 执行 `npm install --prefix frontend && npm run dev --prefix frontend` 启动前端。

开发阶段预置演示组织账号，通过登录页取得短期 JWT；统一初始密码为 `Oa@123456`。长期会话使用 HttpOnly 刷新 Cookie，刷新令牌只以 SHA-256 摘要保存，访问令牌不写入浏览器本地存储。用户可在“登录设备”查看并撤销其他会话；退出、重置密码、停用账号或调整安全角色/角色权限会使相关旧会话立即失效。用户、账号、部门树、岗位、人事档案与生命周期、考勤班次/记录/申诉、劳动合同/事件/预警、角色、权限、数据范围、公告及阅读确认、登录会话和业务单据均持久化到 PostgreSQL，正常重启不会重置。岗位描述职责，安全角色授予权限，两者独立；角色页面支持 17 类服务端操作权限以及请假/报销/出差/人事档案/考勤/劳动合同六类四级数据范围。劳动合同采用独立敏感数据范围，管理链上级不会自动获得下属合同查看权。HR/行政和系统管理员可维护人事档案、考勤、劳动合同与公司公告。本地 PostgreSQL 映射到 `localhost:5433`，避免与常见的系统 PostgreSQL 端口冲突。

开发签名密钥位于 `appsettings.Development.json`，仅供本机演示。部署到其他环境时必须通过 `Authentication__SigningKey` 注入至少 32 字节的独立密钥，并避免提交生产密钥。

模拟考勤和模拟合同只在 Development 环境开启，生产配置默认关闭。API 提供 `/health` 存活检查和 `/health/ready` 数据库/附件存储就绪检查；合同到期预警由后台任务按 90/60/30/7 天档位幂等派发。生产部署前的剩余门禁见 [人力资源模块生产就绪清单](./docs/oa/20-人力资源模块生产就绪清单.md)。

## 验证

执行 `bash scripts/verify.sh`，依次验证后端编译、请假领域测试、Vue 生产构建和 Docker Compose 配置。

Docker 可用时，执行 `bash scripts/verify-postgres.sh` 验证 PostgreSQL 表结构、身份权限、部门树、岗位、人事档案、考勤和劳动合同约束、密码锁定/重置、刷新令牌轮换与会话撤销，以及请假、出差、报销和出差报销关联的跨 DbContext 持久化。该脚本只重建隔离测试库 `oa_test`，不会重置演示库 `oa`。

## 附件存储

演示环境的附件保存在后端项目的 `storage/files` 目录。上传支持 PDF、JPG、JPEG、PNG、XLS、XLSX、DOC、DOCX，单文件最大 20MB；生产部署应通过 `Storage:Root` 配置挂载持久卷或替换为对象存储适配。
