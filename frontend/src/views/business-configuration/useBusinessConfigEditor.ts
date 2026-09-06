import type {
  AnnualLeaveBonusConfig,
  ConfigurationDomain,
  DictionaryConfig,
  DictionaryItemConfig,
  ExpenseCategoryPolicyConfig,
  ExpensePolicyConfig,
  LeavePolicyConfig,
  LeaveTypePolicyConfig,
  ProcurementAmountTierConfig,
  ProcurementCategoryPolicyConfig,
  ProcurementPolicyConfig,
  SealDocumentCategoryConfig,
  SealPolicyConfig,
  SealRegistryItemConfig,
  SealRiskRulesConfig,
  TravelCityTierConfig,
  TravelPolicyConfig,
  TravelStandardItemConfig
} from '../../api/types'

export function formatDate(value?: string | null): string {
  if (!value) return '长期有效'
  return new Date(value).toLocaleDateString('zh-CN')
}

export function formatDateTime(value?: string | null): string {
  if (!value) return '—'
  return new Date(value).toLocaleString('zh-CN')
}

export function toLocalInput(value?: string | null): string {
  if (!value) return ''
  const date = new Date(value)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}

export function domainLabel(domain: string): string {
  const map: Record<string, string> = {
    Leave: '休假规则',
    Expense: '费用报销',
    Travel: '差旅标准',
    Procurement: '采购限额',
    Seal: '用印规则',
    Dictionary: '业务字典'
  }
  return map[domain] || domain
}

export function statusLabel(status: string): string {
  const map: Record<string, string> = {
    DRAFT: '草稿',
    SCHEDULED: '待生效',
    EFFECTIVE: '生效中',
    RETIRED: '已下线'
  }
  return map[status] || status
}

export function statusClass(status: string): string {
  const map: Record<string, string> = {
    DRAFT: 'status-draft',
    SCHEDULED: 'status-scheduled',
    EFFECTIVE: 'status-effective',
    RETIRED: 'status-retired'
  }
  return map[status] || ''
}

export function getRiskBadgeClass(level?: string): string {
  switch (level?.toUpperCase()) {
    case 'HIGH':
      return 'risk-high'
    case 'MEDIUM':
      return 'risk-medium'
    case 'LOW':
    default:
      return 'risk-low'
  }
}

export function getRiskLevelLabel(level?: string): string {
  switch (level?.toUpperCase()) {
    case 'HIGH':
      return '高风险 (HIGH)'
    case 'MEDIUM':
      return '中风险 (MEDIUM)'
    case 'LOW':
      return '低风险 (LOW)'
    default:
      return level || '普通'
  }
}

export function getDefaultConfigForDomain(domain: string): {
  code: string
  name: string
  description: string
  json: string
} {
  switch (domain) {
    case 'Leave':
      return {
        code: 'LeavePolicy',
        name: '全员休假规则',
        description: '休假类型定义、最小额度单位及法定与司龄年假规则',
        json: JSON.stringify(
          {
            leaveTypes: [
              { type: 'ANNUAL', name: '年假', isEnabled: true, minUnit: 0.5, requiresAttachment: false, attachmentThresholdDays: null },
              { type: 'SICK', name: '病假', isEnabled: true, minUnit: 0.5, requiresAttachment: true, attachmentThresholdDays: 1 },
              { type: 'CASUAL', name: '事假', isEnabled: true, minUnit: 0.5, requiresAttachment: false, attachmentThresholdDays: null },
              { type: 'COMPENSATORY', name: '调休', isEnabled: true, minUnit: 0.5, requiresAttachment: false, attachmentThresholdDays: null }
            ],
            allowCrossYear: true,
            compTimeValidityDays: 365,
            annualLeaveBonus: {
              legalMinStandardProtected: true,
              tier1BonusDays: 1,
              tier2BonusDays: 2,
              tier3BonusDays: 3
            }
          },
          null,
          2
        )
      }
    case 'Expense':
      return {
        code: 'ExpensePolicy',
        name: '日常费用报销规则',
        description: '费用类别单笔限额、发票凭据与超额管控标准',
        json: JSON.stringify(
          {
            categories: [
              { name: '差旅费', isEnabled: true, singleLimit: 2000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
              { name: '交通费', isEnabled: true, singleLimit: 500, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
              { name: '办公用品', isEnabled: true, singleLimit: 1000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
              { name: '招待费', isEnabled: true, singleLimit: 3000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false }
            ]
          },
          null,
          2
        )
      }
    case 'Travel':
      return {
        code: 'TravelPolicy',
        name: '差旅报销标准与城市限额',
        description: '城市级别划分、各职级住宿限额、餐补及交通工具标准',
        json: JSON.stringify(
          {
            cityTiers: [
              { tierName: '一线城市', cities: ['北京', '上海', '广州', '深圳'] },
              { tierName: '二线及其他城市', cities: ['杭州', '成都', '武汉', '南京', '其他'] }
            ],
            employeeRanks: ['基层员工', '骨干员工', '部门负责人', '公司高管'],
            standards: [
              { cityTier: '一线城市', rank: '基层员工', hotelDailyLimit: 450, mealDailyAllowance: 120, transportationStandard: '高铁二等座 / 经济舱' },
              { cityTier: '一线城市', rank: '骨干员工', hotelDailyLimit: 550, mealDailyAllowance: 150, transportationStandard: '高铁二等座 / 经济舱' },
              { cityTier: '一线城市', rank: '部门负责人', hotelDailyLimit: 700, mealDailyAllowance: 180, transportationStandard: '高铁一等座 / 公务舱' },
              { cityTier: '二线及其他城市', rank: '基层员工', hotelDailyLimit: 320, mealDailyAllowance: 100, transportationStandard: '高铁二等座 / 经济舱' },
              { cityTier: '二线及其他城市', rank: '骨干员工', hotelDailyLimit: 400, mealDailyAllowance: 120, transportationStandard: '高铁二等座 / 经济舱' },
              { cityTier: '二线及其他城市', rank: '部门负责人', hotelDailyLimit: 550, mealDailyAllowance: 150, transportationStandard: '高铁一等座 / 经济舱' }
            ]
          },
          null,
          2
        )
      }
    case 'Procurement':
      return {
        code: 'ProcurementPolicy',
        name: '采购限额与比价规则',
        description: '采购类别、比价附件起计金额阈值与验收规范',
        json: JSON.stringify(
          {
            categories: [
              { name: '办公设备', isEnabled: true },
              { name: '电子耗材', isEnabled: true },
              { name: '软件服务', isEnabled: true },
              { name: '办公家具', isEnabled: true }
            ],
            quoteAttachmentThreshold: 5000,
            amountTiers: [
              { name: '小额采购', maxAmount: 5000 },
              { name: '中额采购', maxAmount: 50000 },
              { name: '重大采购', maxAmount: null }
            ],
            defaultPurchaserUserId: 'admin',
            requiresAcceptance: true,
            acceptanceRoleOrAssignee: '部门负责人'
          },
          null,
          2
        )
      }
    case 'Seal':
      return {
        code: 'SealPolicy',
        name: '印章管控与外借风险规则',
        description: '印章登记、用印文件类别风险等级与外借天数管控',
        json: JSON.stringify(
          {
            seals: [
              { name: '公司公章', sealType: 'COMPANY_OFFICIAL', custodianUserId: 'admin', isEnabled: true, allowOut: true, maxOutDays: 3 },
              { name: '合同专用章', sealType: 'CONTRACT', custodianUserId: 'admin', isEnabled: true, allowOut: true, maxOutDays: 5 },
              { name: '财务专用章', sealType: 'FINANCE', custodianUserId: 'admin', isEnabled: true, allowOut: false, maxOutDays: 0 },
              { name: '法人章', sealType: 'LEGAL_REPRESENTATIVE', custodianUserId: 'admin', isEnabled: true, allowOut: false, maxOutDays: 0 }
            ],
            documentCategories: [
              { name: '工商登记与法人变更', riskLevel: 'HIGH', isEnabled: true },
              { name: '诉讼仲裁及保全文件', riskLevel: 'HIGH', isEnabled: true },
              { name: '重大商务合同（≥50万）', riskLevel: 'HIGH', isEnabled: true },
              { name: '常规商务合同', riskLevel: 'MEDIUM', isEnabled: true },
              { name: '员工在职证明与日常文书', riskLevel: 'LOW', isEnabled: true }
            ],
            riskRules: {
              highRiskMetric: 3,
              mediumRiskMetric: 2,
              lowRiskMetric: 1
            }
          },
          null,
          2
        )
      }
    case 'Dictionary':
    default:
      return {
        code: 'AnnouncementType',
        name: '公司公告类型字典',
        description: '公司各类公告发布时可供选择的业务分类字典',
        json: JSON.stringify(
          {
            items: [
              { code: 'NOTICE', name: '日常通知', sortOrder: 1, isEnabled: true, description: '全员日常事务与通知' },
              { code: 'REGULATION', name: '规章制度', sortOrder: 2, isEnabled: true, description: '全员规章制度下发' },
              { code: 'PERSONNEL', name: '人事任命', sortOrder: 3, isEnabled: true, description: '组织变动与高管任命' }
            ]
          },
          null,
          2
        )
      }
  }
}

export function prop<T = any>(obj: any, ...keys: string[]): T | undefined {
  if (!obj || typeof obj !== 'object') return undefined
  for (const k of keys) {
    if (obj[k] !== undefined) return obj[k]
  }
  const lowerKeys = keys.map(k => k.toLowerCase())
  for (const objKey of Object.keys(obj)) {
    if (lowerKeys.includes(objKey.toLowerCase()) && obj[objKey] !== undefined) {
      return obj[objKey]
    }
  }
  return undefined
}

export function parseLeaveConfig(data: any): LeavePolicyConfig {
  const rawList = prop<any[]>(data, 'leaveTypes', 'LeaveTypes') ?? []
  const leaveTypes: LeaveTypePolicyConfig[] = Array.isArray(rawList)
    ? rawList.map(item => ({
        type: String(prop(item, 'type', 'Type') ?? '').trim(),
        name: String(prop(item, 'name', 'Name') ?? '').trim(),
        isEnabled: prop(item, 'isEnabled', 'IsEnabled') !== false,
        minUnit: Number(prop(item, 'minUnit', 'MinUnit')) || 0.5,
        requiresAttachment: Boolean(prop(item, 'requiresAttachment', 'RequiresAttachment')),
        attachmentThresholdDays:
          prop(item, 'attachmentThresholdDays', 'AttachmentThresholdDays') != null &&
          prop(item, 'attachmentThresholdDays', 'AttachmentThresholdDays') !== ''
            ? Number(prop(item, 'attachmentThresholdDays', 'AttachmentThresholdDays'))
            : null
      }))
    : []

  const rawBonus = prop<any>(data, 'annualLeaveBonus', 'AnnualLeaveBonus')
  const annualLeaveBonus: AnnualLeaveBonusConfig = {
    legalMinStandardProtected: prop(rawBonus, 'legalMinStandardProtected', 'LegalMinStandardProtected') !== false,
    tier1BonusDays: Number(prop(rawBonus, 'tier1BonusDays', 'Tier1BonusDays')) || 0,
    tier2BonusDays: Number(prop(rawBonus, 'tier2BonusDays', 'Tier2BonusDays')) || 0,
    tier3BonusDays: Number(prop(rawBonus, 'tier3BonusDays', 'Tier3BonusDays')) || 0
  }

  return {
    leaveTypes,
    allowCrossYear: prop(data, 'allowCrossYear', 'AllowCrossYear') !== false,
    compTimeValidityDays: Number(prop(data, 'compTimeValidityDays', 'CompTimeValidityDays')) || 365,
    annualLeaveBonus
  }
}

export function parseExpenseConfig(data: any): ExpensePolicyConfig {
  const rawList = prop<any[]>(data, 'categories', 'Categories') ?? []
  const categories: ExpenseCategoryPolicyConfig[] = Array.isArray(rawList)
    ? rawList.map(item => ({
        name: String(prop(item, 'name', 'Name') ?? '').trim(),
        isEnabled: prop(item, 'isEnabled', 'IsEnabled') !== false,
        singleLimit:
          prop(item, 'singleLimit', 'SingleLimit') != null && prop(item, 'singleLimit', 'SingleLimit') !== ''
            ? Number(prop(item, 'singleLimit', 'SingleLimit'))
            : null,
        requiresReceipt: Boolean(prop(item, 'requiresReceipt', 'RequiresReceipt')),
        requiresReasonWhenExceeded: Boolean(prop(item, 'requiresReasonWhenExceeded', 'RequiresReasonWhenExceeded')),
        blockWhenExceeded: Boolean(prop(item, 'blockWhenExceeded', 'BlockWhenExceeded'))
      }))
    : []
  return { categories }
}

export function parseTravelConfig(data: any): TravelPolicyConfig {
  const rawTiers = prop<any[]>(data, 'cityTiers', 'CityTiers') ?? []
  const cityTiers: TravelCityTierConfig[] = Array.isArray(rawTiers)
    ? rawTiers.map(t => ({
        tierName: String(prop(t, 'tierName', 'TierName') ?? '').trim(),
        cities: Array.isArray(prop(t, 'cities', 'Cities'))
          ? (prop(t, 'cities', 'Cities') as string[]).map(c => String(c).trim()).filter(Boolean)
          : []
      }))
    : []

  const rawRanks = prop<any[]>(data, 'employeeRanks', 'EmployeeRanks') ?? []
  const employeeRanks: string[] = Array.isArray(rawRanks)
    ? rawRanks.map(r => String(r).trim()).filter(Boolean)
    : []

  const rawStandards = prop<any[]>(data, 'standards', 'Standards') ?? []
  const standards: TravelStandardItemConfig[] = Array.isArray(rawStandards)
    ? rawStandards.map(s => ({
        cityTier: String(prop(s, 'cityTier', 'CityTier') ?? '').trim(),
        rank: String(prop(s, 'rank', 'Rank') ?? '').trim(),
        hotelDailyLimit: Number(prop(s, 'hotelDailyLimit', 'HotelDailyLimit')) || 0,
        mealDailyAllowance: Number(prop(s, 'mealDailyAllowance', 'MealDailyAllowance')) || 0,
        transportationStandard: String(prop(s, 'transportationStandard', 'TransportationStandard') ?? '').trim()
      }))
    : []

  return { cityTiers, employeeRanks, standards }
}

export function parseProcurementConfig(data: any): ProcurementPolicyConfig {
  const rawCats = prop<any[]>(data, 'categories', 'Categories') ?? []
  const categories: ProcurementCategoryPolicyConfig[] = Array.isArray(rawCats)
    ? rawCats.map(c => ({
        name: String(prop(c, 'name', 'Name') ?? '').trim(),
        isEnabled: prop(c, 'isEnabled', 'IsEnabled') !== false
      }))
    : []

  const rawTiers = prop<any[]>(data, 'amountTiers', 'AmountTiers') ?? []
  const amountTiers: ProcurementAmountTierConfig[] = Array.isArray(rawTiers)
    ? rawTiers.map(t => ({
        name: String(prop(t, 'name', 'Name') ?? '').trim(),
        maxAmount:
          prop(t, 'maxAmount', 'MaxAmount') != null && prop(t, 'maxAmount', 'MaxAmount') !== ''
            ? Number(prop(t, 'maxAmount', 'MaxAmount'))
            : null
      }))
    : []

  return {
    categories,
    quoteAttachmentThreshold: Number(prop(data, 'quoteAttachmentThreshold', 'QuoteAttachmentThreshold')) || 0,
    amountTiers,
    defaultPurchaserUserId: String(prop(data, 'defaultPurchaserUserId', 'DefaultPurchaserUserId') ?? '').trim(),
    requiresAcceptance: prop(data, 'requiresAcceptance', 'RequiresAcceptance') !== false,
    acceptanceRoleOrAssignee: String(prop(data, 'acceptanceRoleOrAssignee', 'AcceptanceRoleOrAssignee') ?? '').trim()
  }
}

export function parseSealConfig(data: any): SealPolicyConfig {
  const rawSeals = prop<any[]>(data, 'seals', 'Seals') ?? []
  const seals: SealRegistryItemConfig[] = Array.isArray(rawSeals)
    ? rawSeals.map(s => ({
        name: String(prop(s, 'name', 'Name') ?? '').trim(),
        sealType: String(prop(s, 'sealType', 'SealType') ?? '').trim(),
        custodianUserId: String(prop(s, 'custodianUserId', 'CustodianUserId') ?? '').trim(),
        isEnabled: prop(s, 'isEnabled', 'IsEnabled') !== false,
        allowOut: Boolean(prop(s, 'allowOut', 'AllowOut')),
        maxOutDays: Number(prop(s, 'maxOutDays', 'MaxOutDays')) || 0
      }))
    : []

  const rawDocs = prop<any[]>(data, 'documentCategories', 'DocumentCategories') ?? []
  const documentCategories: SealDocumentCategoryConfig[] = Array.isArray(rawDocs)
    ? rawDocs.map(dc => ({
        name: String(prop(dc, 'name', 'Name') ?? '').trim(),
        isEnabled: prop(dc, 'isEnabled', 'IsEnabled') !== false,
        riskLevel: String(prop(dc, 'riskLevel', 'RiskLevel') ?? 'LOW').toUpperCase()
      }))
    : []

  const rawRules = prop<any>(data, 'riskRules', 'RiskRules')
  const riskRules: SealRiskRulesConfig = {
    highRiskMetric: Number(prop(rawRules, 'highRiskMetric', 'HighRiskMetric')) || 3,
    mediumRiskMetric: Number(prop(rawRules, 'mediumRiskMetric', 'MediumRiskMetric')) || 2,
    lowRiskMetric: Number(prop(rawRules, 'lowRiskMetric', 'LowRiskMetric')) || 1
  }

  return { seals, documentCategories, riskRules }
}

export function parseDictionaryConfig(data: any): DictionaryConfig {
  const rawItems = prop<any[]>(data, 'items', 'Items') ?? []
  const items: DictionaryItemConfig[] = Array.isArray(rawItems)
    ? rawItems.map((it, idx) => ({
        code: String(prop(it, 'code', 'Code') ?? '').trim(),
        name: String(prop(it, 'name', 'Name') ?? '').trim(),
        sortOrder: Number(prop(it, 'sortOrder', 'SortOrder')) || idx + 1,
        isEnabled: prop(it, 'isEnabled', 'IsEnabled') !== false,
        description: prop(it, 'description', 'Description') ? String(prop(it, 'description', 'Description')).trim() : null
      }))
    : []
  return { items }
}

export function normalizeDomainConfig(domain: string, rawData: any): any {
  if (!rawData || typeof rawData !== 'object') return null
  switch (domain) {
    case 'Leave':
      return parseLeaveConfig(rawData)
    case 'Expense':
      return parseExpenseConfig(rawData)
    case 'Travel':
      return parseTravelConfig(rawData)
    case 'Procurement':
      return parseProcurementConfig(rawData)
    case 'Seal':
      return parseSealConfig(rawData)
    case 'Dictionary':
      return parseDictionaryConfig(rawData)
    default:
      return rawData
  }
}

export function validateConfigDraft(
  form: {
    name: string
    code: string
    effectiveFrom: string
    effectiveTo?: string | null
    isEditing?: boolean
  },
  editorMode: 'visual' | 'json',
  contentJson: string
): string | null {
  if (!form.name.trim()) {
    return '请填写配置名称。'
  }
  if (!form.isEditing && !form.code.trim()) {
    return '请填写配置标识。'
  }
  if (!form.effectiveFrom) {
    return '请指定生效起始时间。'
  }
  if (form.effectiveTo && new Date(form.effectiveTo) <= new Date(form.effectiveFrom)) {
    return '生效失效时间必须晚于生效起始时间。'
  }

  if (editorMode === 'json') {
    try {
      JSON.parse(contentJson)
    } catch {
      return '原始 JSON 格式不合法，请检查后再保存。'
    }
  }

  return null
}

export function validateConfigPublish(
  publishType: 'immediate' | 'scheduled',
  effectiveFrom: string,
  effectiveTo?: string | null
): string | null {
  let fromIso: string | null = null
  if (publishType === 'immediate') {
    fromIso = new Date().toISOString()
  } else {
    if (!effectiveFrom) {
      return '指定生效时间不能为空。'
    }
    fromIso = new Date(effectiveFrom).toISOString()
  }

  if (effectiveTo) {
    const toIso = new Date(effectiveTo).toISOString()
    if (new Date(toIso) <= new Date(fromIso)) {
      return '失效时间必须晚于生效时间。'
    }
  }

  return null
}
