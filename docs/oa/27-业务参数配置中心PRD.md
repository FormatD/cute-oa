# 业务参数配置中心 PRD

## 1. 产品目标与背景

在中小企业日常业务流转中，各项管理制度和业务规则频繁演进（例如：年度休假额度调整、差旅一线城市酒店报销限额上浮、重大采购比价附件起计门槛变更、公章与合同章外借最长天数管控、财务各类报销科目规范等）。

在早期实现中，上述参数通常硬编码在业务后端控制器、领域模型、前端组件常量或数据库静态 Seed 脚本中，导致以下显著问题：
1. **变更成本高且不可控**：任何参数微调均需研发修改代码并重新构建上线，业务负责人无法自主配置；
2. **缺乏生效时间与版本演进**：无法预设“下季度起生效”或“新财年起生效”，缺乏历史版本记录（v1, v2, v3）；
3. **历史单据被后续规则篡改（缺失快照）**：当差旅报销限额从 400 元上调至 500 元后，若直接读取全局最新配置，会导致上年度已归档单据的合规审计逻辑失真；
4. **缺乏操作审计与排他冲突校验**：无发布人、发布时间审计记录，同一业务域容易因时间重叠导致规则冲突。

**业务参数配置中心（P0-F3）** 的目标是集中收敛全公司各业务域的生效规则，提供具有管理权限控制、草稿/生效状态机、版本记录、生效日期区间排他校验、全流程操作审计、业务单据动态解析与快照冻结（Snapshot）、历史单据规则隔离的统一配置基础设施。

---

## 2. 角色与权限

| 角色 | 能力 |
|---|---|
| 普通员工 / 经办人 | 发起业务单据（休假、报销、差旅、采购、用印）时，系统自动匹配并应用当前生效的业务参数；在单据详情中可查看该单据冻结的配置快照 |
| 部门负责人 / 审批人 | 在待办审批流中查看受当时业务参数约束的审批项与单据规则快照 |
| 系统管理员 | 拥有全域业务参数管理权限：浏览全域配置、新建草稿、基于历史版本升版、编辑草稿、发布生效、立即下线、删除无引用草稿、查看变更审计流水 |
| 审计人员 | 查阅历史版本演进、参数对比、各单据引用快照及操作审计日志 |

### 权限点规范

- `BUSINESS_CONFIG_MANAGE`：业务参数配置中心的维护管理权限（包括新建、编辑、升版、发布、下线、删除）。
- 生产环境安全策略：`BUSINESS_CONFIG_MANAGE` 属于公司核心规则治理权限，必须纳入系统管理员角色，敏感操作记录至 `AuditLogs`。

---

## 3. 六大业务域模型与参数规范

系统统一支持六大核心业务域，各域采用强类型 JSON 结构，并提供默认模板与双向校验：

### 3.1 休假规则域 (`Leave` / `LeavePolicy`)
- **适用场景**：考勤与请假单提交时的假期类型校验、最小扣减单位及年假天数计算。
- **参数结构**：
  - `leaveTypes`: 假期类型定义数组（编码 `type`、名称 `name`、启用 `isEnabled`、最小申请单位 `minUnit: 0.5 | 1.0`、是否需附证明 `requiresAttachment`、证明起计天数 `attachmentThresholdDays`）；
  - `allowCrossYear`: 是否允许年假/调休跨公历年结转（布尔值）；
  - `compTimeValidityDays`: 加班调休有效天数（默认 365 天）；
  - `annualLeaveBonus`: 法定与司龄年假奖励规则（`legalMinStandardProtected` 法定标准兜底保护，`tier1BonusDays` 1~3年司龄奖励天数，`tier2BonusDays` 3~5年奖励天数，`tier3BonusDays` 5年以上奖励天数）。

### 3.2 报销规则域 (`Expense` / `ExpensePolicy`)
- **适用场景**：日常费用报销时的科目控制、单笔上限、发票凭据强制性及超额阻断。
- **参数结构**：
  - `categories`: 报销类别明细数组（名称 `name`、启用 `isEnabled`、单笔限额 `singleLimit` 元、是否必须上传发票 `requiresReceipt`、超额是否必须填写理由 `requiresReasonWhenExceeded`、超额是否阻断提交 `blockWhenExceeded`）。

### 3.3 差旅规则域 (`Travel` / `TravelPolicy`)
- **适用场景**：差旅申请与差旅报销时的城市级别识别、职级标准匹配、住宿限额、日常餐补与交通工具合规校验。
- **参数结构**：
  - `cityTiers`: 城市级别划分（如 一线城市 `['北京', '上海', '广州', '深圳']`，二线及其他城市）；
  - `employeeRanks`: 适用职级名录（如 `['基层员工', '骨干员工', '部门负责人', '公司高管']`）；
  - `standards`: 差旅标准矩阵数组（城市级别 `cityTier`、职级 `rank`、住宿每晚限额 `hotelDailyLimit`、每日餐补标准 `mealDailyAllowance`、交通工具标准 `transportationStandard`）。

### 3.4 采购规则域 (`Procurement` / `ProcurementPolicy`)
- **适用场景**：采购立项申请时的品类管理、多方比价附件起计金额门槛、金额分级审批与到货验收流程。
- **参数结构**：
  - `quoteAttachmentThreshold`: 比价报价单附件强制起计金额（元，超过该金额必须上传比价附件）；
  - `defaultPurchaserUserId`: 默认承接采购专员 User ID；
  - `requiresAcceptance`: 采购到货是否强制执行到货验收流程（布尔值）；
  - `acceptanceRoleOrAssignee`: 验收指派角色或负责人（如 `部门负责人`）；
  - `categories`: 采购品类清单（名称 `name`、启用 `isEnabled`）；
  - `amountTiers`: 采购金额审批层级（层级名称 `name`、最高金额上限 `maxAmount` 元，空代表无上限）。

### 3.5 用印规则域 (`Seal` / `SealPolicy`)
- **适用场景**：印章使用、借出外带、文件类型安全风险评估与天数管控。
- **参数结构**：
  - `seals`: 印章名录数组（印章名称 `name`、印章类型 `sealType`、默认保管人 `custodianUserId`、启用 `isEnabled`、是否允许外带借出 `allowOut`、外带最长借出天数 `maxOutDays`）；
  - `documentCategories`: 用印文件类别与风险等级定义（文件类别名称 `name`、风险等级 `riskLevel: 'HIGH' | 'MEDIUM' | 'LOW'`、启用 `isEnabled`）；
  - `riskRules`: 风险计算与加权分值指标（`highRiskMetric`, `mediumRiskMetric`, `lowRiskMetric`）。

### 3.6 业务字典域 (`Dictionary` / 业务自定义编码)
- **适用场景**：系统通用下拉字典（如公告类型、项目类型、客户行业分类等）。
- **参数结构**：
  - `items`: 字典明细项数组（字典项编码 `code`、显示名称 `name`、排序号 `sortOrder`、启用 `isEnabled`、说明备注 `description`）。

---

## 4. 页面范围与交互规范

系统在管理后台提供独立的统一配置中心路由 `/business-configurations`。

### 4.1 业务参数配置中心主页 `/business-configurations`
- **顶部业务域快捷切换选项卡**：全部业务域、休假规则、费用报销、差旅标准、采购限额、印章管理、通用字典，带各域配置条数徽标。
- **检索与状态过滤栏**：
  - 支持关键字模糊检索（配置编码、名称、描述、创建人）；
  - 支持状态过滤（全部、生效中 Effective、待生效 Scheduled、草稿 Draft、已下线 Retired）；
  - 支持快捷一键“＋ 新建配置草稿”与“刷新数据”。
- **主配置列表表格**：
  - 列项：业务域（彩色胶囊标签）、配置编码与名称、当前版本号（v1, v2...）、状态徽标、生效时间区间（起止日期）、单据引用统计（显示冻结该版本的单据笔数）、更新人与更新时间、操作列。
  - 操作按钮组：
    - 对 `Draft` 草稿版本：**编辑**、**发布**、**删除草稿**；
    - 对 `Effective` 生效中版本：**基于此版本升版**、**查看详情**、**版本历史**、**下线**；
    - 对 `Scheduled` 待生效版本：**编辑**、**下线**、**查看详情**、**版本历史**；
    - 对 `Retired` 已下线版本：**基于此版本升版**、**查看详情**、**版本历史**。

### 4.2 新建/编辑配置弹窗
- **基础元数据**：业务域下拉选择（新建时）、配置标识 Code（英文字符）、配置名称 Name、配置描述 Description、生效开始日期与生效结束日期。
- **参数编辑双模式切换**：
  - **可视化结构化表单（默认）**：根据选择的业务域动态加载专属定制表单（增删假期类型、动态表格编辑报销科目、城市职级矩阵配置、采购层级增减、印章外借管控表等）；
  - **原始 JSON 模式**：内置语法高亮代码区，支持直接修改 JSON 并在切回可视化表单时自动双向同步与语法合法性检查。

### 4.3 发布确认与生效弹窗
- 管理员点击“发布”时弹出模态框：
  - **发布模式**：
    - `立即生效`（更新 `EffectiveFrom = 当日`，并将同 Domain+Code 的老有效版本优雅截断至昨日）；
    - `定时生效`（指定未来生效起始日期，进入 `Scheduled` 待生效状态）；
  - **防冲突排他检测**：若检测到与现有生效版本的时间区间发生重叠，弹窗予以醒目提示并要求管理员确认覆盖排他策略。

### 4.4 版本历史与对比弹窗
- 点击“版本历史”弹窗，集中列出该配置的所有历史迭代记录（v1, v2, v3...）；
- 展现版本号、状态、生效区间、创建人、发布人、单据引用统计；
- 操作支持：
  - **查看参数**：直接弹出详情弹窗回显该版本的规则；
  - **基于此版本升版**：以该历史版本作为蓝本，直接新建更高版本号的 Draft 草稿。

### 4.5 配置详情弹窗（统一 UI 回显与 JSON 快照）
- **元数据信息面板**：
  - 业务域、配置标识 Code、版本状态（带色状态徽标与版本号 vX）、生效日期区间；
  - 单据引用统计（如“被 12 笔业务单据冻结快照”）；
  - 创建信息、发布信息、更新信息、配置描述。
- **参数展示双模式自由切换（重点改进）**：
  - **结构化 UI 回显模式（默认激活）**：与新建/编辑时的可视化表单保持同等专业视觉语义，采用卡片、标签徽标和只读表格，直观呈现 6 大域的全部规则：
    - `Leave`：跨年结转与调休有效期规则卡片、法定保护与司龄阶梯奖励卡片、假期类型名录只读表格；
    - `Expense`：费用报销类别与限额管控表格（单笔限额、发票要求、超限填写理由、超限阻断提交）；
    - `Travel`：城市级别分组徽标卡片、适用职级徽标卡片、差旅标准矩阵表格（住宿上限、餐补、交通工具）；
    - `Procurement`：采购风控与流程参数卡片、采购品类名录卡片、金额审批层级矩阵表格；
    - `Seal`：印章名录只读表格（保管人、外带天数）、文件类别与风险等级表格（高/中/低彩色徽标）、风险计算指标；
    - `Dictionary`：字典项明细表格（排序、编码、名称、启用状态、说明）；
  - **原始 JSON 快照模式**：只读暗色代码块展示格式化 JSON，支持一键“复制 JSON”并附带复制成功反馈。

---

## 5. 核心业务规则与状态机

### 5.1 配置版本生命周期状态机

```
               [ 新建草稿 / 基于历史升版 ]
                         │
                         ▼
                     ┌───────┐
                     │ Draft │ ◄──── (编辑/校验)
                     └───┬───┘
                         │ 
        ┌────────────────┴────────────────┐
        │ [发布: 立即生效]                │ [发布: 定时未来生效]
        ▼                                 ▼
 ┌──────────────┐                  ┌─────────────┐
 │  Effective   │                  │  Scheduled  │
 └───┬──────────┘                  └───┬─────────┘
     │                                 │
     │ (到达生效日期 / 新版本排他生效)    │ (到达生效日期)
     │                                 │
     ▼                                 ▼
 ┌──────────────┐               ┌──────────────┐
 │   Retired    │               │  Effective   │
 └──────────────┘               └──────────────┘
```

1. **草稿（Draft）**：
   - 处于编制状态，仅具备管理权限的人员可见；
   - 允许反复修改元数据与参数内容；
   - 若无业务单据引用，允许物理删除。
2. **待生效（Scheduled）**：
   - 已完成发布审核，但 `EffectiveFrom > 当天日期`；
   - 在到达生效日前不被新单据解析使用；
   - 到达生效日期时，系统动态解析自动生效。
3. **生效中（Effective）**：
   - 处于当前有效日期区间（`EffectiveFrom <= 今天 <= EffectiveTo`）；
   - 同一业务域与配置编码，在任意时间点**有且仅有一个**生效中版本；
   - **发布即锁定（Immutable）**：生效中的版本不可再直接修改内容，如需调整必须通过“基于此版本升版”生成新版本 Draft。
4. **已下线（Retired）**：
   - 已被新版本替代或管理员主动执行“立即下线”；
   - 不再匹配给新发起的业务单据；
   - 历史单据保留引用的快照与版本号，确保追溯不变。

### 5.2 排他生效与时间重叠检测

- 管理员发布新版本时，系统在服务端执行重叠检测事务：
  ```csharp
  // 查找同 Domain + Code 下存在日期区间交叉的其他已发布/已生效版本
  var overlap = await db.BusinessConfigurations
      .AnyAsync(c => c.Domain == domain 
                  && c.Code == code 
                  && c.Id != currentId 
                  && (c.Status == Effective || c.Status == Scheduled)
                  && c.EffectiveFrom <= newEffectiveTo 
                  && (c.EffectiveTo == null || c.EffectiveTo >= newEffectiveFrom));
  ```
- 若选择“立即生效”，系统会自动将原有 `Effective` 版本的 `EffectiveTo` 截断为昨天，并将其流转为 `Retired`，实现无缝平滑更替。

### 5.3 业务单据提交时的动态解析与快照冻结（Snapshot）

为彻底解决历史单据被后续规则篡改的问题，系统在五大业务单据提交服务中注入了动态规则解析器：
1. **单据创建/提交阶段**：
   - 调用 `BusinessConfigurationService.ResolveEffectiveConfigAsync(domain, code, submitDate)`；
   - 若匹配到生效版本，单据主表同时持久化记录四个快照字段：
     - `ConfigVersionId`：引用的配置主键 GUID；
     - `ConfigVersionNumber`：引用的版本号（如 1, 2...）；
     - `ConfigSnapshotJson`：提交当下该版本的完整 JSON 字符串；
     - `ConfigResolvedAt`：解析冻结的时间戳。
2. **审批与历史详情查看阶段**：
   - 前后端直接读取单据自身存储的 `ConfigSnapshotJson`；
   - 即使用户在配置中心修改或下线了该配置，单据当时的审批依据与规则快照完全不变。
3. **审批驳回后经办人重新提交阶段**：
   - 驳回单据处于重提编辑态，经办人修改数据后重新点击“提交”；
   - 系统重新执行 `ResolveEffectiveConfigAsync`，获取当下的最新生效版本并重新写入快照字段，确保重提时遵循最新公司制度。

### 5.4 单据引用锁定与防删除保护

- 配置中心严格禁止误删正被业务单据依赖的版本；
- 删除草稿或配置版本时，服务端统计关联业务单据：
  ```csharp
  var refCount = await CountReferencesAsync(configId);
  if (refCount > 0)
  {
      throw new DomainException("CONFIG_004", $"该配置版本已被 {refCount} 笔业务单据引用并冻结快照，禁止删除！");
  }
  ```

---

## 6. 数据库设计

### 6.1 `BusinessConfigurations` 业务配置表

| 字段名 | 类型 | 允许空 | 说明 |
|---|---|---|---|
| `Id` | `uuid` | 否 | 主键 GUID |
| `Domain` | `varchar(32)` | 否 | 业务域：Leave / Expense / Travel / Procurement / Seal / Dictionary |
| `Code` | `varchar(64)` | 否 | 配置标识（同 Domain 下标识语义唯一） |
| `Name` | `varchar(128)` | 否 | 配置友好显示名称 |
| `Description` | `varchar(500)` | 是 | 配置说明及变更备忘 |
| `Version` | `integer` | 否 | 版本号（从 1 开始严格自增） |
| `Status` | `varchar(32)` | 否 | 状态：Draft / Scheduled / Effective / Retired |
| `ContentJson` | `text` | 否 | 结构化规则 JSON 内容（UTF-8） |
| `EffectiveFrom` | `date` | 否 | 生效开始日期（包含当天） |
| `EffectiveTo` | `date` | 是 | 生效结束日期（为空表示长期有效） |
| `CreatedByUserId` | `varchar(64)` | 否 | 创建人用户 ID |
| `CreatedByName` | `varchar(64)` | 否 | 创建人姓名 |
| `CreatedAt` | `timestamptz` | 否 | 创建时间 |
| `PublishedByUserId` | `varchar(64)` | 是 | 发布审核人用户 ID |
| `PublishedByName` | `varchar(64)` | 是 | 发布审核人姓名 |
| `PublishedAt` | `timestamptz` | 是 | 发布时间 |
| `UpdatedByUserId` | `varchar(64)` | 否 | 最后更新人用户 ID |
| `UpdatedByName` | `varchar(64)` | 否 | 最后更新人姓名 |
| `UpdatedAt` | `timestamptz` | 否 | 最后更新时间 |
| `xmin` | `uint` | 否 | PostgreSQL 并发版本控制乐观锁标记 |

**索引设计**：
- 组合索引：`idx_biz_config_domain_code_ver` (`Domain`, `Code`, `Version`)
- 状态与生效日期检索索引：`idx_biz_config_lookup` (`Domain`, `Code`, `Status`, `EffectiveFrom`, `EffectiveTo`)

### 6.2 业务单据快照关联字段扩展

在 `LeaveRequests`、`ExpenseRequests`、`TravelRequests`、`ProcurementRequests`、`SealRequests` 五大单据实体表中扩展以下字段：
- `ConfigVersionId` (`uuid`, nullable)
- `ConfigVersionNumber` (`integer`, nullable)
- `ConfigSnapshotJson` (`text`, nullable)
- `ConfigResolvedAt` (`timestamptz`, nullable)

---

## 7. API 接口设计

所有接口遵循 RESTful 规范，前缀为 `/api/v1/business-configurations`：

| HTTP 方法 | 路径 | 权限要求 | 说明 |
|---|---|---|---|
| `GET` | `/` | 登录用户 | 分页多条件查询配置列表（支持按 domain、status、keyword 筛选） |
| `GET` | `/{id}` | 登录用户 | 查询指定配置版本的元数据、JSON 及单据引用统计 |
| `GET` | `/effective` | 登录用户 | 实时查询指定业务域与编码在指定日期的最新生效规则 |
| `GET` | `/{id}/history` | 登录用户 | 查询同业务域与编码的所有历史演进版本列表 |
| `POST` | `/` | `BUSINESS_CONFIG_MANAGE` | 创建新配置草稿（版本号从 1 开始，状态为 Draft） |
| `POST` | `/{id}/branch` | `BUSINESS_CONFIG_MANAGE` | 基于指定版本升版（生成更高自增版本号的 Draft 草稿） |
| `PUT` | `/{id}` | `BUSINESS_CONFIG_MANAGE` | 编辑 Draft 状态的配置元数据与参数 JSON |
| `POST` | `/{id}/publish` | `BUSINESS_CONFIG_MANAGE` | 发布配置（支持指定生效起止日期与立即/定时发布） |
| `POST` | `/{id}/retire` | `BUSINESS_CONFIG_MANAGE` | 立即下线指定生效版本 |
| `DELETE` | `/{id}` | `BUSINESS_CONFIG_MANAGE` | 删除配置（仅允许无引用的草稿版本） |

---

## 8. 异常错误码规范

| 错误码 | HTTP 状态码 | 触发场景说明 |
|---|---|---|
| `CONFIG_001` | 400 | 基础参数校验失败（编码为空、名称过长、JSON 语法不合法等） |
| `CONFIG_002` | 404 | 目标业务配置记录不存在 |
| `CONFIG_003` | 409 | 业务配置标识版本冲突，或已存在相同版本号 |
| `CONFIG_004` | 400 | 非法操作流转（如尝试编辑已生效版本、删除已产生业务引用的配置） |
| `CONFIG_005` | 409 | 发布生效日期与现有生效中/待生效版本产生重叠冲突 |
| `CONFIG_006` | 400 | 业务域参数特定内容校验失败（如差旅矩阵缺少职级、用印印章天数超出范围等） |
| `AUTH_002` | 403 | 用户缺少 `BUSINESS_CONFIG_MANAGE` 权限 |

---

## 9. 验收标准与质量基线

1. **管理与状态流转闭环**：
   - 管理员可正常新建草稿、在结构化表单与 JSON 间无损切换保存；
   - 执行发布后，状态转为 `Effective` 或 `Scheduled`，生效时间区间正确更新；
   - 自动排他检测生效，旧生效版本被优雅截断下线。
2. **单据动态快照与隔离验证**：
   - 经办人新建请假/报销/差旅/采购/用印单据，提交后数据库单据记录中的 `ConfigVersionNumber` 与 `ConfigSnapshotJson` 必须准确固化；
   - 配置中心随后将该配置升版并发布新规则后，已提交的单据详情内展示的参数快照保持不变；
   - 驳回单据重新编辑提交后，单据快照自动刷新为最新版本。
3. **防删除与合规保障**：
   - 只要单据引用计数 `referenceCount > 0`，任何人员删除该配置版本均被服务端强行拦截并返回错误码 `CONFIG_004`；
   - 所有创建、修改、升版、发布、下线动作必须被写入审计流水。
4. **前端视觉与响应式标准**：
   - 查看配置详情模态框支持**结构化 UI 回显**与**原始 JSON 快照**一键无缝切换，默认以优雅的只读表单呈现；
   - 在 390px 移动端设备视口下无横向滚动条溢出，表格支持容器内横向滑动。
5. **自动化质量门禁**：
   - 领域单元测试 `dotnet test backend/Oa.Domain.Tests` 全部通过；
   - 数据库集成测试 `bash scripts/verify-postgres.sh` 全部通过；
   - 完整校验脚本 `bash scripts/verify.sh` 始终绿灯。
