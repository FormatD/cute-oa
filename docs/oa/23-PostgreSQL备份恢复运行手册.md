# PostgreSQL 备份恢复运行手册

## 1. 目标与安全边界

本手册用于单节点 Docker Compose 部署的逻辑备份和隔离恢复验证。原则是“不直接覆盖正在运行的 `oa` 数据库”：恢复始终创建新的 `oa_restore_*` 数据库，校验通过后再由维护窗口切换应用连接串。

仓库提供：

- `scripts/backup-postgres.sh`：使用 `pg_dump --format=custom` 生成不含 owner/privilege 的自定义格式备份；
- `scripts/restore-postgres-isolated.sh`：只接受项目内备份文件和不存在的 `oa_restore_*` 目标库；
- `scripts/verify-backup-restore.sh`：备份、随机隔离恢复、关键计数比对并自动清理。

安全限制：

- 备份只能写入项目 `backups/`，文件名不能包含路径，默认权限由 `umask 077` 限制；
- 已存在备份文件拒绝覆盖；
- 恢复目标不以 `oa_restore_` 开头、目标库已存在或试图使用 `oa` 时立即拒绝；
- 恢复失败自动删除未完成的隔离库；自动演练只删除本次随机生成的隔离库和 `verify-*` 临时备份。

## 2. 创建备份

默认备份演示数据库 `oa`：

```bash
bash scripts/backup-postgres.sh
```

指定不含路径的文件名：

```bash
bash scripts/backup-postgres.sh oa-before-release-20260831.dump
```

如需备份同一 Compose PostgreSQL 中的其他数据库，可设置 `OA_BACKUP_DATABASE`；数据库名和用户只允许字母、数字、下划线：

```bash
OA_BACKUP_DATABASE=oa_staging OA_BACKUP_USER=oa bash scripts/backup-postgres.sh oa-staging-20260831.dump
```

脚本在完成后调用 `pg_restore --list` 验证归档可读取。生产备份必须在脚本完成后加密并复制到独立故障域，不能只保留在应用主机或仓库目录。

## 3. 隔离恢复

恢复到一个尚不存在的隔离库：

```bash
bash scripts/restore-postgres-isolated.sh oa-before-release-20260831.dump oa_restore_release_20260831
```

恢复后至少校验：

1. `__EFMigrationsHistory` 数量及最新迁移；
2. `oa_user`、`personnel_profile`、`employment_contract`、`personnel_case` 数量；
3. 随机抽取员工档案、合同附件元数据、考勤封账和生命周期办理单详情；
4. 使用隔离应用实例执行 HR、员工、部门负责人和系统管理员权限用例；
5. 记录备份开始、恢复完成时间，计算实际 RPO/RTO。

校验不通过时不得切换连接串，应保留日志、删除隔离库并重新获取可靠备份。

## 4. 自动恢复演练

```bash
bash scripts/verify-backup-restore.sh
```

脚本比较源库和恢复库的迁移、用户、人事档案、劳动合同和员工办理单计数。2026-08-31 在员工办理与 MFA 增量迁移后再次演练，结果为 `27|8|8|8|2`，校验通过后确认未残留 `oa_restore_*` 数据库或临时备份。

该结果只证明当前本地 Compose 链路可用。上线前仍须在预发布环境连续执行两次备份和至少一次恢复，保存命令输出、耗时、备份大小、校验记录和责任人签字。

## 5. 生产切换与回滚

1. 停止写流量并等待后台任务结束，记录切换时间点；
2. 创建切换前备份并恢复到新的 `oa_restore_*` 数据库；
3. 完成结构、计数、抽样和权限验收；
4. 通过密钥管理系统更新 `ConnectionStrings__OaDatabase` 指向已验证的新库；
5. 启动单实例，等待 `/health/ready` 为 200 后再放量；
6. 保留旧数据库为只读回滚点，超过审批后的保留期再由 DBA 处理，不在应用脚本中自动删除；
7. 回滚应用版本时不得回退数据库迁移；如迁移不可向前兼容，必须依据变更前备份恢复到另一个新库并重新验收。

托管 PostgreSQL 应优先使用服务商快照、PITR、跨区复制和密钥管理能力；本仓库脚本只作为逻辑备份补充，不能替代平台级灾备。
