<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import type {
  AnnualLeaveBonusConfig,
  BusinessConfigurationListItem,
  BusinessConfigurationRecord,
  ConfigurationDomain,
  ConfigurationVersionSummary,
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
} from '../api/types'
import OaDialog from '../components/OaDialog.vue'
import UserSelect from '../components/UserSelect.vue'
import { useBusinessConfigurationStore } from '../stores/business-configurations'
import { useOrganizationStore } from '../stores/organization'

const store = useBusinessConfigurationStore()
const organization = useOrganizationStore()

// Domain tabs
const domainTabs: { key: string; label: string }[] = [
  { key: '', label: '全部域' },
  { key: 'Leave', label: '休假规则' },
  { key: 'Expense', label: '费用报销' },
  { key: 'Travel', label: '差旅标准' },
  { key: 'Procurement', label: '采购限额' },
  { key: 'Seal', label: '用印规则' },
  { key: 'Dictionary', label: '业务字典' }
]

function selectDomainTab(domain: string) {
  store.domainFilter = domain
  void store.search()
}

// Helpers
function formatDate(value?: string | null) {
  if (!value) return '长期有效'
  return new Date(value).toLocaleDateString('zh-CN')
}

function formatDateTime(value?: string | null) {
  if (!value) return '—'
  return new Date(value).toLocaleString('zh-CN')
}

function toLocalInput(value?: string | null) {
  if (!value) return ''
  const date = new Date(value)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}

function domainLabel(domain: string) {
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

function statusLabel(status: string) {
  const map: Record<string, string> = {
    DRAFT: '草稿',
    SCHEDULED: '待生效',
    EFFECTIVE: '生效中',
    RETIRED: '已下线'
  }
  return map[status] || status
}

function statusClass(status: string) {
  const map: Record<string, string> = {
    DRAFT: 'status-draft',
    SCHEDULED: 'status-scheduled',
    EFFECTIVE: 'status-effective',
    RETIRED: 'status-retired'
  }
  return map[status] || ''
}

// ---------------- Editor (Create / Edit Draft) ----------------
const editorOpen = ref(false)
const editingId = ref<string | null>(null)
const editingVersion = ref(1)
const editingConcurrencyVersion = ref(0)
const editorMode = ref<'visual' | 'json'>('visual')
const validationError = ref('')

const form = reactive({
  domain: 'Leave' as ConfigurationDomain,
  code: '',
  name: '',
  description: '',
  effectiveFrom: '',
  effectiveTo: '',
  contentJson: ''
})

// Visual domain state
const leaveForm = reactive<LeavePolicyConfig>({
  leaveTypes: [],
  allowCrossYear: true,
  compTimeValidityDays: 365,
  annualLeaveBonus: {
    legalMinStandardProtected: true,
    tier1BonusDays: 1,
    tier2BonusDays: 2,
    tier3BonusDays: 3
  }
})

const expenseForm = reactive<ExpensePolicyConfig>({
  categories: []
})

const travelForm = reactive<TravelPolicyConfig>({
  cityTiers: [],
  employeeRanks: [],
  standards: []
})
// Helper for editing travel city tiers comma-separated
const travelTierInputs = reactive<{ tierName: string; citiesStr: string }[]>([])
const travelRanksStr = ref('')

const procurementForm = reactive<ProcurementPolicyConfig>({
  categories: [],
  quoteAttachmentThreshold: 5000,
  amountTiers: [],
  defaultPurchaserUserId: '',
  requiresAcceptance: true,
  acceptanceRoleOrAssignee: ''
})

const sealForm = reactive<SealPolicyConfig>({
  seals: [],
  documentCategories: [],
  riskRules: {
    highRiskMetric: 3,
    mediumRiskMetric: 2,
    lowRiskMetric: 1
  }
})

const dictionaryForm = reactive<DictionaryConfig>({
  items: []
})

function getDefaultConfigForDomain(domain: string): { code: string; name: string; description: string; json: string } {
  switch (domain) {
    case 'Leave':
      return {
        code: 'LeavePolicy',
        name: '全员休假规则',
        description: '休假类型定义、最小额度单位及法定与司龄年假规则',
        json: JSON.stringify({
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
        }, null, 2)
      }
    case 'Expense':
      return {
        code: 'ExpensePolicy',
        name: '日常费用报销规则',
        description: '费用类别单笔限额、发票凭据与超额管控标准',
        json: JSON.stringify({
          categories: [
            { name: '差旅费', isEnabled: true, singleLimit: 2000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
            { name: '交通费', isEnabled: true, singleLimit: 500, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
            { name: '办公用品', isEnabled: true, singleLimit: 1000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
            { name: '招待费', isEnabled: true, singleLimit: 3000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false }
          ]
        }, null, 2)
      }
    case 'Travel':
      return {
        code: 'TravelPolicy',
        name: '差旅报销标准与城市限额',
        description: '城市级别划分、各职级住宿限额、餐补及交通工具标准',
        json: JSON.stringify({
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
        }, null, 2)
      }
    case 'Procurement':
      return {
        code: 'ProcurementPolicy',
        name: '采购限额与比价规则',
        description: '采购类别、比价附件起计金额阈值与验收规范',
        json: JSON.stringify({
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
        }, null, 2)
      }
    case 'Seal':
      return {
        code: 'SealPolicy',
        name: '印章管控与外借风险规则',
        description: '印章登记、用印文件类别风险等级与外借天数管控',
        json: JSON.stringify({
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
        }, null, 2)
      }
    case 'Dictionary':
    default:
      return {
        code: 'AnnouncementType',
        name: '公司公告类型字典',
        description: '公司各类公告发布时可供选择的业务分类字典',
        json: JSON.stringify({
          items: [
            { code: 'NOTICE', name: '日常通知', sortOrder: 1, isEnabled: true, description: '全员日常事务与通知' },
            { code: 'REGULATION', name: '规章制度', sortOrder: 2, isEnabled: true, description: '全员规章制度下发' },
            { code: 'PERSONNEL', name: '人事任命', sortOrder: 3, isEnabled: true, description: '组织变动与高管任命' }
          ]
        }, null, 2)
      }
  }
}

// Case-agnostic property extractor helper (supports camelCase, PascalCase, or mixed)
function prop<T = any>(obj: any, ...keys: string[]): T | undefined {
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

function parseLeaveConfig(data: any): LeavePolicyConfig {
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

function parseExpenseConfig(data: any): ExpensePolicyConfig {
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

function parseTravelConfig(data: any): TravelPolicyConfig {
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

function parseProcurementConfig(data: any): ProcurementPolicyConfig {
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

function parseSealConfig(data: any): SealPolicyConfig {
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

function parseDictionaryConfig(data: any): DictionaryConfig {
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

function normalizeDomainConfig(domain: string, rawData: any): any {
  if (!rawData || typeof rawData !== 'object') return null
  switch (domain) {
    case 'Leave': return parseLeaveConfig(rawData)
    case 'Expense': return parseExpenseConfig(rawData)
    case 'Travel': return parseTravelConfig(rawData)
    case 'Procurement': return parseProcurementConfig(rawData)
    case 'Seal': return parseSealConfig(rawData)
    case 'Dictionary': return parseDictionaryConfig(rawData)
    default: return rawData
  }
}

function parseJsonToVisual(domain: string, jsonStr: string) {
  try {
    const data = JSON.parse(jsonStr || '{}')
    const normalized = normalizeDomainConfig(domain, data)
    if (!normalized) return

    switch (domain) {
      case 'Leave':
        leaveForm.leaveTypes = normalized.leaveTypes
        leaveForm.allowCrossYear = normalized.allowCrossYear
        leaveForm.compTimeValidityDays = normalized.compTimeValidityDays
        leaveForm.annualLeaveBonus = normalized.annualLeaveBonus
        break
      case 'Expense':
        expenseForm.categories = normalized.categories
        break
      case 'Travel':
        travelForm.cityTiers = normalized.cityTiers
        travelForm.employeeRanks = normalized.employeeRanks
        travelForm.standards = normalized.standards
        travelTierInputs.splice(0, travelTierInputs.length, ...travelForm.cityTiers.map((t: TravelCityTierConfig) => ({
          tierName: t.tierName,
          citiesStr: (t.cities || []).join(', ')
        })))
        travelRanksStr.value = travelForm.employeeRanks.join(', ')
        break
      case 'Procurement':
        procurementForm.categories = normalized.categories
        procurementForm.quoteAttachmentThreshold = normalized.quoteAttachmentThreshold
        procurementForm.amountTiers = normalized.amountTiers
        procurementForm.defaultPurchaserUserId = normalized.defaultPurchaserUserId
        procurementForm.requiresAcceptance = normalized.requiresAcceptance
        procurementForm.acceptanceRoleOrAssignee = normalized.acceptanceRoleOrAssignee
        break
      case 'Seal':
        sealForm.seals = normalized.seals
        sealForm.documentCategories = normalized.documentCategories
        sealForm.riskRules = normalized.riskRules
        break
      case 'Dictionary':
        dictionaryForm.items = normalized.items
        break
    }
  } catch {
    // If parse fails, stay in JSON mode
    editorMode.value = 'json'
  }
}

function syncVisualToJson(): string {
  let obj: unknown = {}
  switch (form.domain) {
    case 'Leave':
      obj = {
        leaveTypes: leaveForm.leaveTypes.map(t => ({
          type: (t.type || '').trim(),
          name: (t.name || '').trim(),
          isEnabled: Boolean(t.isEnabled),
          minUnit: Number(t.minUnit) || 0.5,
          requiresAttachment: Boolean(t.requiresAttachment),
          attachmentThresholdDays: t.attachmentThresholdDays !== null && t.attachmentThresholdDays !== undefined && t.attachmentThresholdDays !== ('' as unknown)
            ? Number(t.attachmentThresholdDays)
            : null
        })),
        allowCrossYear: Boolean(leaveForm.allowCrossYear),
        compTimeValidityDays: Number(leaveForm.compTimeValidityDays) || 365,
        annualLeaveBonus: {
          legalMinStandardProtected: Boolean(leaveForm.annualLeaveBonus.legalMinStandardProtected),
          tier1BonusDays: Number(leaveForm.annualLeaveBonus.tier1BonusDays) || 0,
          tier2BonusDays: Number(leaveForm.annualLeaveBonus.tier2BonusDays) || 0,
          tier3BonusDays: Number(leaveForm.annualLeaveBonus.tier3BonusDays) || 0
        }
      }
      break
    case 'Expense':
      obj = {
        categories: expenseForm.categories.map(c => ({
          name: (c.name || '').trim(),
          isEnabled: Boolean(c.isEnabled),
          singleLimit: c.singleLimit !== null && c.singleLimit !== undefined && c.singleLimit !== ('' as unknown)
            ? Number(c.singleLimit)
            : null,
          requiresReceipt: Boolean(c.requiresReceipt),
          requiresReasonWhenExceeded: Boolean(c.requiresReasonWhenExceeded),
          blockWhenExceeded: Boolean(c.blockWhenExceeded)
        }))
      }
      break
    case 'Travel': {
      const cityTiers = travelTierInputs.map(ti => ({
        tierName: ti.tierName.trim(),
        cities: ti.citiesStr.split(/[,，]/).map(s => s.trim()).filter(Boolean)
      }))
      const employeeRanks = travelRanksStr.value.split(/[,，]/).map(s => s.trim()).filter(Boolean)
      obj = {
        cityTiers,
        employeeRanks,
        standards: travelForm.standards.map(s => ({
          cityTier: (s.cityTier || '').trim(),
          rank: (s.rank || '').trim(),
          hotelDailyLimit: Number(s.hotelDailyLimit) || 0,
          mealDailyAllowance: Number(s.mealDailyAllowance) || 0,
          transportationStandard: (s.transportationStandard || '').trim()
        }))
      }
      break
    }
    case 'Procurement':
      obj = {
        categories: procurementForm.categories.map(c => ({
          name: (c.name || '').trim(),
          isEnabled: Boolean(c.isEnabled)
        })),
        quoteAttachmentThreshold: Number(procurementForm.quoteAttachmentThreshold) || 0,
        amountTiers: procurementForm.amountTiers.map(t => ({
          name: (t.name || '').trim(),
          maxAmount: t.maxAmount !== null && t.maxAmount !== undefined && t.maxAmount !== ('' as unknown)
            ? Number(t.maxAmount)
            : null
        })),
        defaultPurchaserUserId: (procurementForm.defaultPurchaserUserId || '').trim(),
        requiresAcceptance: Boolean(procurementForm.requiresAcceptance),
        acceptanceRoleOrAssignee: (procurementForm.acceptanceRoleOrAssignee || '').trim()
      }
      break
    case 'Seal':
      obj = {
        seals: sealForm.seals.map(s => ({
          name: (s.name || '').trim(),
          sealType: (s.sealType || '').trim(),
          custodianUserId: (s.custodianUserId || '').trim(),
          isEnabled: Boolean(s.isEnabled),
          allowOut: Boolean(s.allowOut),
          maxOutDays: Number(s.maxOutDays) || 0
        })),
        documentCategories: sealForm.documentCategories.map(dc => ({
          name: (dc.name || '').trim(),
          isEnabled: Boolean(dc.isEnabled),
          riskLevel: dc.riskLevel || 'LOW'
        })),
        riskRules: {
          highRiskMetric: Number(sealForm.riskRules.highRiskMetric) || 3,
          mediumRiskMetric: Number(sealForm.riskRules.mediumRiskMetric) || 2,
          lowRiskMetric: Number(sealForm.riskRules.lowRiskMetric) || 1
        }
      }
      break
    case 'Dictionary':
      obj = {
        items: dictionaryForm.items.map((it, idx) => ({
          code: (it.code || '').trim(),
          name: (it.name || '').trim(),
          sortOrder: typeof it.sortOrder === 'number' ? it.sortOrder : idx + 1,
          isEnabled: Boolean(it.isEnabled),
          description: (it.description || '').trim() || null
        }))
      }
      break
  }
  return JSON.stringify(obj, null, 2)
}

async function loadBaseConfigForDomain(domain: ConfigurationDomain) {
  // Check if an existing configuration exists in current list for this domain
  const existing = store.items.find(i => i.domain === domain && i.status === 'EFFECTIVE')
    || store.items.find(i => i.domain === domain)
  if (existing) {
    const full = await store.loadDetail(existing.id)
    if (full && full.contentJson) {
      form.code = full.code
      form.name = `${full.name} (新草稿)`
      form.description = full.description || ''
      form.contentJson = full.contentJson
      parseJsonToVisual(domain, full.contentJson)
      return
    }
  }
  const def = getDefaultConfigForDomain(domain)
  form.code = def.code
  form.name = def.name
  form.description = def.description
  form.contentJson = def.json
  parseJsonToVisual(domain, def.json)
}

function resetToDefaultTemplate() {
  const def = getDefaultConfigForDomain(form.domain)
  form.code = def.code
  form.name = def.name
  form.description = def.description
  form.contentJson = def.json
  parseJsonToVisual(form.domain, def.json)
  store.message = `已载入【${domainLabel(form.domain)}】的出厂默认配置模板。`
}

async function copyFromExistingConfig() {
  const existing = store.items.find(i => i.domain === form.domain && i.status === 'EFFECTIVE')
    || store.items.find(i => i.domain === form.domain)
  if (existing) {
    const full = await store.loadDetail(existing.id)
    if (full && full.contentJson) {
      form.contentJson = full.contentJson
      form.code = full.code
      parseJsonToVisual(form.domain, full.contentJson)
      store.message = `已成功复制当前【${domainLabel(form.domain)}】生效版本 (v${full.version}) 的配置内容。`
      return
    }
  }
  store.message = `当前业务域【${domainLabel(form.domain)}】暂无已有生效配置可供复制。`
}

async function handleDomainChange() {
  if (editingId.value) return // code/domain locked when editing
  await loadBaseConfigForDomain(form.domain)
}

function switchEditorMode(mode: 'visual' | 'json') {
  if (mode === 'json') {
    form.contentJson = syncVisualToJson()
    editorMode.value = 'json'
  } else {
    try {
      JSON.parse(form.contentJson)
      parseJsonToVisual(form.domain, form.contentJson)
      editorMode.value = 'visual'
      validationError.value = ''
    } catch {
      validationError.value = '当前原始 JSON 格式不合法，请先修正语法错误后再切换到可视化表单。'
    }
  }
}

async function openCreate() {
  editingId.value = null
  editingVersion.value = 1
  editingConcurrencyVersion.value = 0
  editorMode.value = 'visual'
  validationError.value = ''
  store.error = ''
  store.message = ''

  form.domain = (store.domainFilter as ConfigurationDomain) || 'Leave'
  form.effectiveFrom = toLocalInput(new Date().toISOString())
  form.effectiveTo = ''

  await loadBaseConfigForDomain(form.domain)
  editorOpen.value = true
}

async function openEdit(item: BusinessConfigurationListItem | BusinessConfigurationRecord) {
  validationError.value = ''
  store.error = ''
  store.message = ''

  const fullRecord = await store.loadDetail(item.id)
  if (!fullRecord) return

  editingId.value = fullRecord.id
  editingVersion.value = fullRecord.version
  editingConcurrencyVersion.value = fullRecord.concurrencyVersion
  editorMode.value = 'visual'

  form.domain = fullRecord.domain as ConfigurationDomain
  form.code = fullRecord.code
  form.name = fullRecord.name
  form.description = fullRecord.description || ''
  form.effectiveFrom = toLocalInput(fullRecord.effectiveFrom)
  form.effectiveTo = toLocalInput(fullRecord.effectiveTo)
  form.contentJson = fullRecord.contentJson

  parseJsonToVisual(form.domain, fullRecord.contentJson)
  editorOpen.value = true
}

async function saveDraft() {
  validationError.value = ''
  store.error = ''
  store.message = ''

  if (!form.name.trim()) {
    validationError.value = '请填写配置名称。'
    return
  }
  if (!editingId.value && !form.code.trim()) {
    validationError.value = '请填写配置标识。'
    return
  }
  if (!form.effectiveFrom) {
    validationError.value = '请指定生效起始时间。'
    return
  }
  if (form.effectiveTo && new Date(form.effectiveTo) <= new Date(form.effectiveFrom)) {
    validationError.value = '生效失效时间必须晚于生效起始时间。'
    return
  }

  // Get final JSON
  let finalJson = form.contentJson
  if (editorMode.value === 'visual') {
    finalJson = syncVisualToJson()
  } else {
    try {
      JSON.parse(finalJson)
    } catch {
      validationError.value = '原始 JSON 格式不合法，请检查后再保存。'
      return
    }
  }

  const effectiveFromIso = new Date(form.effectiveFrom).toISOString()
  const effectiveToIso = form.effectiveTo ? new Date(form.effectiveTo).toISOString() : null

  let success = false
  if (editingId.value) {
    const result = await store.updateDraft(editingId.value, {
      name: form.name.trim(),
      description: form.description.trim() || null,
      effectiveFrom: effectiveFromIso,
      effectiveTo: effectiveToIso,
      contentJson: finalJson,
      concurrencyVersion: editingConcurrencyVersion.value
    })
    success = !!result
  } else {
    const result = await store.createDraft({
      domain: form.domain,
      code: form.code.trim(),
      name: form.name.trim(),
      description: form.description.trim() || null,
      effectiveFrom: effectiveFromIso,
      effectiveTo: effectiveToIso,
      contentJson: finalJson
    })
    success = !!result
  }

  if (success) {
    editorOpen.value = false
  }
}

// ---------------- Publish Modal ----------------
const publishModalOpen = ref(false)
const publishTarget = ref<BusinessConfigurationListItem | null>(null)
const publishForm = reactive({
  publishType: 'immediate' as 'immediate' | 'scheduled',
  effectiveFrom: '',
  effectiveTo: ''
})

function openPublish(item: BusinessConfigurationListItem) {
  publishTarget.value = item
  publishForm.publishType = 'immediate'
  publishForm.effectiveFrom = toLocalInput(item.effectiveFrom)
  publishForm.effectiveTo = toLocalInput(item.effectiveTo)
  validationError.value = ''
  store.error = ''
  store.message = ''
  publishModalOpen.value = true
}

async function confirmPublish() {
  if (!publishTarget.value) return
  validationError.value = ''
  store.error = ''

  let fromIso: string | null = null
  let toIso: string | null = null

  if (publishForm.publishType === 'immediate') {
    fromIso = new Date().toISOString()
  } else {
    if (!publishForm.effectiveFrom) {
      validationError.value = '指定生效时间不能为空。'
      return
    }
    fromIso = new Date(publishForm.effectiveFrom).toISOString()
  }

  if (publishForm.effectiveTo) {
    toIso = new Date(publishForm.effectiveTo).toISOString()
    if (new Date(toIso) <= new Date(fromIso)) {
      validationError.value = '失效时间必须晚于生效时间。'
      return
    }
  }

  const result = await store.publish(publishTarget.value.id, {
    effectiveFrom: fromIso,
    effectiveTo: toIso,
    concurrencyVersion: publishTarget.value.concurrencyVersion
  })

  if (result) {
    publishModalOpen.value = false
    publishTarget.value = null
  }
}

// ---------------- Retire Modal ----------------
const retireModalOpen = ref(false)
const retireTarget = ref<BusinessConfigurationListItem | null>(null)
const retireForm = reactive({
  effectiveTo: ''
})

function openRetire(item: BusinessConfigurationListItem) {
  retireTarget.value = item
  retireForm.effectiveTo = toLocalInput(new Date().toISOString())
  validationError.value = ''
  store.error = ''
  store.message = ''
  retireModalOpen.value = true
}

async function confirmRetire() {
  if (!retireTarget.value) return
  const toIso = retireForm.effectiveTo ? new Date(retireForm.effectiveTo).toISOString() : new Date().toISOString()
  const result = await store.retire(retireTarget.value.id, {
    effectiveTo: toIso,
    concurrencyVersion: retireTarget.value.concurrencyVersion
  })
  if (result) {
    retireModalOpen.value = false
    retireTarget.value = null
  }
}

// ---------------- Create New Version ----------------
async function handleCreateNewVersion(item: BusinessConfigurationListItem) {
  store.error = ''
  store.message = ''
  const newRecord = await store.createNewVersion(item.id)
  if (newRecord) {
    await openEdit(newRecord)
  }
}

// ---------------- Delete Modal ----------------
const deleteModalOpen = ref(false)
const deleteTarget = ref<BusinessConfigurationListItem | null>(null)

function openDelete(item: BusinessConfigurationListItem) {
  deleteTarget.value = item
  deleteModalOpen.value = true
}

async function confirmDelete() {
  if (!deleteTarget.value) return
  if (deleteTarget.value.status !== 'DRAFT') {
    store.error = '仅草稿状态的配置允许删除。'
    deleteModalOpen.value = false
    return
  }
  if (deleteTarget.value.referenceCount > 0) {
    store.error = `此配置已被历史单据引用（引用数：${deleteTarget.value.referenceCount}），禁止删除。`
    deleteModalOpen.value = false
    return
  }

  const success = await store.deleteConfiguration(deleteTarget.value.id)
  if (success) {
    deleteModalOpen.value = false
    deleteTarget.value = null
  }
}

// ---------------- Version History Modal ----------------
const versionHistoryModalOpen = ref(false)
const historyTarget = ref<BusinessConfigurationListItem | null>(null)

async function openVersionHistory(item: BusinessConfigurationListItem) {
  historyTarget.value = item
  await store.loadVersions(item.id)
  versionHistoryModalOpen.value = true
}

async function handleBranchFromHistory(ver: ConfigurationVersionSummary) {
  versionHistoryModalOpen.value = false
  store.error = ''
  store.message = ''
  const newRecord = await store.createNewVersion(ver.id)
  if (newRecord) {
    await openEdit(newRecord)
  }
}

// ---------------- Details Modal ----------------
const detailModalOpen = ref(false)
const detailRecord = ref<BusinessConfigurationRecord | null>(null)
const detailMode = ref<'visual' | 'json'>('visual')

const detailParsed = computed(() => {
  if (!detailRecord.value?.contentJson) return null
  try {
    const raw = JSON.parse(detailRecord.value.contentJson)
    return normalizeDomainConfig(detailRecord.value.domain, raw) || raw
  } catch {
    return null
  }
})

const formattedJson = computed(() => {
  if (!detailRecord.value?.contentJson) return ''
  try {
    return JSON.stringify(JSON.parse(detailRecord.value.contentJson), null, 2)
  } catch {
    return detailRecord.value.contentJson
  }
})

async function openDetail(id: string) {
  const item = await store.loadDetail(id)
  if (item) {
    detailRecord.value = item
    detailMode.value = 'visual'
    detailModalOpen.value = true
  }
}

function getRiskBadgeClass(level?: string) {
  switch (level?.toUpperCase()) {
    case 'HIGH': return 'risk-high'
    case 'MEDIUM': return 'risk-medium'
    case 'LOW':
    default: return 'risk-low'
  }
}

function getRiskLevelLabel(level?: string) {
  switch (level?.toUpperCase()) {
    case 'HIGH': return '高风险 (HIGH)'
    case 'MEDIUM': return '中风险 (MEDIUM)'
    case 'LOW': return '低风险 (LOW)'
    default: return level || '普通'
  }
}

const copySuccess = ref(false)
async function copyJson() {
  if (!formattedJson.value) return
  try {
    await navigator.clipboard.writeText(formattedJson.value)
    copySuccess.value = true
    setTimeout(() => { copySuccess.value = false }, 2000)
  } catch {
    // fallback
  }
}

function getUserDisplayName(userId?: string | null): string {
  if (!userId) return '系统自动指派'
  const emp = organization.directoryEmployees.find(e => e.id === userId)
  if (emp) {
    return `${emp.name} (${emp.departmentName || '员工'} · ${emp.id})`
  }
  return userId
}

// Lifecycle
onMounted(() => {
  store.message = ''
  store.error = ''
  void store.loadList(1)
  if (!organization.organizationLoaded) {
    void organization.loadOrganization()
  }
})
</script>

<template>
  <div class="page-heading">
    <div>
      <p class="eyebrow">BUSINESS CONFIGURATION</p>
      <h1>业务参数配置中心</h1>
      <p>集中管理各业务域（休假、报销、差旅、采购、印章、字典）生效规则与版本历史。已生效配置不可直接篡改，历史单据保留快照审计追溯。</p>
    </div>
    <button class="primary-action" @click="openCreate">＋ 新建配置草稿</button>
  </div>

  <p v-if="store.message" class="notice success">{{ store.message }}</p>
  <p v-if="store.error" class="notice error">{{ store.error }}</p>

  <!-- Domain Navigation Tabs -->
  <div class="domain-tabs-nav">
    <button
      v-for="tab in domainTabs"
      :key="tab.key"
      class="domain-tab-btn"
      :class="{ active: store.domainFilter === tab.key }"
      @click="selectDomainTab(tab.key)"
    >
      {{ tab.label }}
    </button>
  </div>

  <section class="panel">
    <!-- Filter Bar -->
    <form class="filter-bar" @submit.prevent="store.search">
      <label>
        状态
        <select v-model="store.statusFilter">
          <option value="">全部状态</option>
          <option value="EFFECTIVE">生效中</option>
          <option value="SCHEDULED">待生效</option>
          <option value="DRAFT">草稿</option>
          <option value="RETIRED">已下线</option>
        </select>
      </label>

      <label>
        关键字搜索
        <input v-model="store.keyword" placeholder="搜索配置标识 / 名称 / 描述" />
      </label>

      <button type="submit">查询</button>
      <button
        v-if="store.domainFilter || store.statusFilter || store.keyword"
        class="secondary"
        type="button"
        @click="store.resetFilters"
      >
        重置
      </button>
    </form>

    <!-- Table -->
    <p v-if="store.loading" class="empty">正在加载业务配置…</p>
    <template v-else-if="store.items.length">
      <div class="table-wrap">
        <table class="data-table config-table">
          <thead>
            <tr>
              <th>业务域</th>
              <th>配置标识与名称</th>
              <th>版本</th>
              <th>状态</th>
              <th>生效周期</th>
              <th>引用单据</th>
              <th>更新信息</th>
              <th class="action-cell">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in store.items" :key="item.id">
              <td>
                <span class="domain-tag">{{ domainLabel(item.domain) }}</span>
              </td>
              <td>
                <strong>{{ item.name }}</strong>
                <small class="code-badge">{{ item.code }}</small>
                <small v-if="item.description" class="desc-text">{{ item.description }}</small>
              </td>
              <td>
                <span class="version-badge">v{{ item.version }}</span>
              </td>
              <td>
                <span class="status-badge" :class="statusClass(item.status)">
                  {{ statusLabel(item.status) }}
                </span>
              </td>
              <td>
                <span>{{ formatDate(item.effectiveFrom) }}</span>
                <small class="expiry-date">至 {{ formatDate(item.effectiveTo) }}</small>
              </td>
              <td>
                <span v-if="item.referenceCount > 0" class="ref-count-tag" title="被历史业务单据引用的数量">
                  {{ item.referenceCount }} 笔
                </span>
                <span v-else class="text-muted">无引用</span>
              </td>
              <td>
                <span>{{ item.updatedByName || item.createdByName }}</span>
                <small>{{ formatDateTime(item.updatedAt) }}</small>
              </td>
              <td class="task-actions config-actions">
                <button class="secondary" @click="openDetail(item.id)">详情</button>
                <button v-if="item.status === 'DRAFT'" class="secondary" @click="openEdit(item)">编辑</button>
                <button v-if="item.status === 'DRAFT'" class="primary-btn" @click="openPublish(item)">发布</button>
                <button
                  v-if="item.status === 'EFFECTIVE' || item.status === 'SCHEDULED' || item.status === 'RETIRED'"
                  class="secondary"
                  title="基于此版本克隆出新的草稿升版"
                  @click="handleCreateNewVersion(item)"
                >
                  新版本
                </button>
                <button
                  v-if="item.status === 'EFFECTIVE' || item.status === 'SCHEDULED'"
                  class="warning-outline"
                  @click="openRetire(item)"
                >
                  下线
                </button>
                <button class="secondary" @click="openVersionHistory(item)">历史</button>
                <button
                  v-if="item.status === 'DRAFT'"
                  class="danger-outline"
                  title="删除草稿"
                  @click="openDelete(item)"
                >
                  删除
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <div class="pagination">
        <span>共 {{ store.total }} 条配置</span>
        <div>
          <button
            class="secondary"
            :disabled="store.page === 1"
            @click="store.loadList(store.page - 1)"
          >
            上一页
          </button>
          <b>{{ store.page }} / {{ store.totalPages }}</b>
          <button
            class="secondary"
            :disabled="store.page === store.totalPages"
            @click="store.loadList(store.page + 1)"
          >
            下一页
          </button>
        </div>
      </div>
    </template>
    <p v-else class="empty">当前筛选条件下暂无业务配置。</p>
  </section>

  <!-- ---------------- Editor Drawer / Dialog ---------------- -->
  <OaDialog
    :open="editorOpen"
    :title="editingId ? `编辑配置草稿 (v${editingVersion})` : '新建业务配置草稿'"
    :description="editingId ? `${form.domain} - ${form.code}` : '配置先保存为草稿，发布后正式生效并锁定历史版本不可直接修改。'"
    submit-label="保存草稿"
    :busy="store.saving"
    width="920px"
    @close="editorOpen = false"
    @submit="saveDraft"
  >
    <div class="form-grid-2">
      <label class="dialog-field">
        业务域
        <select v-model="form.domain" :disabled="!!editingId" @change="handleDomainChange">
          <option value="Leave">休假规则 (Leave)</option>
          <option value="Expense">费用报销 (Expense)</option>
          <option value="Travel">差旅标准 (Travel)</option>
          <option value="Procurement">采购限额 (Procurement)</option>
          <option value="Seal">用印规则 (Seal)</option>
          <option value="Dictionary">业务字典 (Dictionary)</option>
        </select>
      </label>

      <label class="dialog-field">
        配置唯一标识 (Code)
        <input
          v-model="form.code"
          :disabled="!!editingId"
          maxlength="64"
          placeholder="例如: LeavePolicy, ExpensePolicy"
        />
      </label>
    </div>

    <div class="form-grid-2">
      <label class="dialog-field">
        配置名称
        <input v-model="form.name" maxlength="128" placeholder="请输入直观名称，如：全员休假规则" />
      </label>

      <label class="dialog-field">
        生效起始时间
        <input v-model="form.effectiveFrom" type="datetime-local" />
      </label>
    </div>

    <div class="form-grid-2">
      <label class="dialog-field">
        生效失效时间 (可选)
        <input v-model="form.effectiveTo" type="datetime-local" />
        <small>留空表示长期有效</small>
      </label>

      <label class="dialog-field">
        配置描述说明
        <input v-model="form.description" maxlength="500" placeholder="简述该配置适用范围或变更背景" />
      </label>
    </div>

    <!-- Parameter Config Header & Switch -->
    <div class="param-section-heading">
      <div>
        <strong>业务参数规则配置</strong>
        <small>支持通过结构化可视化表单配置，或直接编辑原始 JSON</small>
      </div>
      <div class="mode-switch-group">
        <button
          v-if="!editingId"
          type="button"
          class="switch-btn"
          title="从系统当前已有的生效配置复制所有参数内容"
          @click="copyFromExistingConfig"
        >
          📋 复制已有配置
        </button>
        <button
          v-if="!editingId"
          type="button"
          class="switch-btn"
          title="重置为系统出厂预设模板"
          @click="resetToDefaultTemplate"
        >
          🔄 默认模板
        </button>
        <button
          type="button"
          class="switch-btn"
          :class="{ active: editorMode === 'visual' }"
          @click="switchEditorMode('visual')"
        >
          可视化表单
        </button>
        <button
          type="button"
          class="switch-btn"
          :class="{ active: editorMode === 'json' }"
          @click="switchEditorMode('json')"
        >
          原始 JSON
        </button>
      </div>
    </div>

    <!-- Visual Domain Editors -->
    <div v-if="editorMode === 'visual'" class="visual-editor-container">
      <!-- 1. LEAVE -->
      <template v-if="form.domain === 'Leave'">
        <div class="card-section">
          <div class="section-subheading">通用规则与年假奖励</div>
          <div class="form-row-checks">
            <label class="checkbox-inline">
              <input v-model="leaveForm.allowCrossYear" type="checkbox" />
              允许年假跨年结转
            </label>
            <label class="dialog-field compact">
              调休有效期天数
              <input v-model.number="leaveForm.compTimeValidityDays" type="number" min="1" max="1000" />
            </label>
          </div>

          <div class="annual-bonus-box">
            <div class="annual-bonus-header">
              <strong>司龄年假阶梯奖励 (在法定年假基础上额外奖励)</strong>
              <label class="checkbox-inline highlight">
                <input v-model="leaveForm.annualLeaveBonus.legalMinStandardProtected" type="checkbox" />
                依法保护最低带薪休假标准
              </label>
            </div>
            <div class="bonus-tiers-grid">
              <label class="dialog-field">
                1 - 5 年司龄奖励 (天)
                <input v-model.number="leaveForm.annualLeaveBonus.tier1BonusDays" type="number" min="0" step="0.5" />
              </label>
              <label class="dialog-field">
                5 - 10 年司龄奖励 (天)
                <input v-model.number="leaveForm.annualLeaveBonus.tier2BonusDays" type="number" min="0" step="0.5" />
              </label>
              <label class="dialog-field">
                10 年以上司龄奖励 (天)
                <input v-model.number="leaveForm.annualLeaveBonus.tier3BonusDays" type="number" min="0" step="0.5" />
              </label>
            </div>
          </div>
        </div>

        <div class="card-section">
          <div class="section-subheading-flex">
            <span>假期类型列表</span>
            <button
              type="button"
              class="add-row-btn"
              @click="leaveForm.leaveTypes.push({ type: 'NEW_TYPE', name: '新假期', isEnabled: true, minUnit: 0.5, requiresAttachment: false, attachmentThresholdDays: null })"
            >
              ＋ 添加假期类型
            </button>
          </div>
          <div class="table-wrap compact">
            <table class="inner-table">
              <thead>
                <tr>
                  <th>假期编码</th>
                  <th>假期名称</th>
                  <th>启用</th>
                  <th>最小单位(天)</th>
                  <th>需附证明</th>
                  <th>证明起计天数</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(lt, idx) in leaveForm.leaveTypes" :key="idx">
                  <td><input v-model="lt.type" placeholder="如 ANNUAL" /></td>
                  <td><input v-model="lt.name" placeholder="如 年假" /></td>
                  <td><input v-model="lt.isEnabled" type="checkbox" /></td>
                  <td>
                    <select v-model.number="lt.minUnit">
                      <option :value="0.5">0.5 天</option>
                      <option :value="1">1.0 天</option>
                    </select>
                  </td>
                  <td><input v-model="lt.requiresAttachment" type="checkbox" /></td>
                  <td>
                    <input
                      v-model.number="lt.attachmentThresholdDays"
                      type="number"
                      placeholder="留空即必填"
                      step="0.5"
                      :disabled="!lt.requiresAttachment"
                    />
                  </td>
                  <td>
                    <button type="button" class="del-row-btn" @click="leaveForm.leaveTypes.splice(idx, 1)">删除</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 2. EXPENSE -->
      <template v-else-if="form.domain === 'Expense'">
        <div class="card-section">
          <div class="section-subheading-flex">
            <span>费用报销类别与限额规则</span>
            <button
              type="button"
              class="add-row-btn"
              @click="expenseForm.categories.push({ name: '新费用类别', isEnabled: true, singleLimit: 1000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false })"
            >
              ＋ 添加报销类别
            </button>
          </div>
          <div class="table-wrap compact">
            <table class="inner-table">
              <thead>
                <tr>
                  <th>类别名称</th>
                  <th>启用</th>
                  <th>单笔限额 (元, 空为无限制)</th>
                  <th>发票凭证</th>
                  <th>超限填理由</th>
                  <th>超限阻断提交</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(cat, idx) in expenseForm.categories" :key="idx">
                  <td><input v-model="cat.name" placeholder="如 办公用品" /></td>
                  <td><input v-model="cat.isEnabled" type="checkbox" /></td>
                  <td><input v-model.number="cat.singleLimit" type="number" placeholder="留空无上限" /></td>
                  <td><input v-model="cat.requiresReceipt" type="checkbox" /></td>
                  <td><input v-model="cat.requiresReasonWhenExceeded" type="checkbox" /></td>
                  <td><input v-model="cat.blockWhenExceeded" type="checkbox" /></td>
                  <td>
                    <button type="button" class="del-row-btn" @click="expenseForm.categories.splice(idx, 1)">删除</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 3. TRAVEL -->
      <template v-else-if="form.domain === 'Travel'">
        <div class="card-section">
          <div class="section-subheading-flex">
            <span>城市级别与职级划分</span>
            <button
              type="button"
              class="add-row-btn"
              @click="travelTierInputs.push({ tierName: '新城市级别', citiesStr: '' })"
            >
              ＋ 添加城市级别
            </button>
          </div>
          <div v-for="(ti, idx) in travelTierInputs" :key="idx" class="travel-tier-row">
            <input v-model="ti.tierName" class="tier-name-input" placeholder="级别名，如 一线城市" />
            <input v-model="ti.citiesStr" class="tier-cities-input" placeholder="包含城市（逗号分隔，如: 北京, 上海, 广州）" />
            <button type="button" class="del-row-btn" @click="travelTierInputs.splice(idx, 1)">删除</button>
          </div>

          <label class="dialog-field" style="margin-top: 12px;">
            适用员工职级 (逗号分隔)
            <input v-model="travelRanksStr" placeholder="例如: 基层员工, 骨干员工, 部门负责人, 公司高管" />
          </label>
        </div>

        <div class="card-section">
          <div class="section-subheading-flex">
            <span>差旅标准矩阵 (住宿、餐补、交通)</span>
            <button
              type="button"
              class="add-row-btn"
              @click="travelForm.standards.push({ cityTier: travelTierInputs[0]?.tierName || '一线城市', rank: '基层员工', hotelDailyLimit: 400, mealDailyAllowance: 100, transportationStandard: '高铁二等座' })"
            >
              ＋ 添加标准行
            </button>
          </div>
          <div class="table-wrap compact">
            <table class="inner-table">
              <thead>
                <tr>
                  <th>城市级别</th>
                  <th>职级</th>
                  <th>住宿限额 (元/晚)</th>
                  <th>餐补标准 (元/天)</th>
                  <th>交通工具标准</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(std, idx) in travelForm.standards" :key="idx">
                  <td><input v-model="std.cityTier" placeholder="城市级别" /></td>
                  <td><input v-model="std.rank" placeholder="职级" /></td>
                  <td><input v-model.number="std.hotelDailyLimit" type="number" /></td>
                  <td><input v-model.number="std.mealDailyAllowance" type="number" /></td>
                  <td><input v-model="std.transportationStandard" placeholder="如 高铁二等座/经济舱" /></td>
                  <td>
                    <button type="button" class="del-row-btn" @click="travelForm.standards.splice(idx, 1)">删除</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 4. PROCUREMENT -->
      <template v-else-if="form.domain === 'Procurement'">
        <div class="card-section">
          <div class="section-subheading">采购风控与流程参数</div>
          <div class="form-grid-2">
            <label class="dialog-field">
              报价比价附件起计金额阈值 (元)
              <input v-model.number="procurementForm.quoteAttachmentThreshold" type="number" />
              <small>超过此金额必须上传比价报价单附件</small>
            </label>

            <label class="dialog-field">
              默认采购专员
              <UserSelect
                v-model="procurementForm.defaultPurchaserUserId"
                placeholder="请选择默认承办采购专员"
              />
              <small>发起采购申请时自动指派的采购承办人员</small>
            </label>
          </div>

          <div class="form-grid-2" style="margin-top: 10px;">
            <label class="checkbox-inline">
              <input v-model="procurementForm.requiresAcceptance" type="checkbox" />
              采购到货强制需要验收流程
            </label>

            <label class="dialog-field">
              验收角色或指定人
              <input v-model="procurementForm.acceptanceRoleOrAssignee" placeholder="例如: 部门负责人" />
            </label>
          </div>
        </div>

        <div class="card-section">
          <div class="section-subheading-flex">
            <span>采购类别</span>
            <button
              type="button"
              class="add-row-btn"
              @click="procurementForm.categories.push({ name: '新类别', isEnabled: true })"
            >
              ＋ 添加类别
            </button>
          </div>
          <div class="table-wrap compact">
            <table class="inner-table">
              <thead>
                <tr>
                  <th>类别名称</th>
                  <th>启用</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(cat, idx) in procurementForm.categories" :key="idx">
                  <td><input v-model="cat.name" placeholder="类别名称" /></td>
                  <td><input v-model="cat.isEnabled" type="checkbox" /></td>
                  <td>
                    <button type="button" class="del-row-btn" @click="procurementForm.categories.splice(idx, 1)">删除</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card-section">
          <div class="section-subheading-flex">
            <span>采购金额审批层级</span>
            <button
              type="button"
              class="add-row-btn"
              @click="procurementForm.amountTiers.push({ name: '新金额层级', maxAmount: 10000 })"
            >
              ＋ 添加层级
            </button>
          </div>
          <div class="table-wrap compact">
            <table class="inner-table">
              <thead>
                <tr>
                  <th>层级名称</th>
                  <th>最高金额上限 (元, 空为无上限)</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(tier, idx) in procurementForm.amountTiers" :key="idx">
                  <td><input v-model="tier.name" placeholder="如 小额采购" /></td>
                  <td><input v-model.number="tier.maxAmount" type="number" placeholder="留空无上限" /></td>
                  <td>
                    <button type="button" class="del-row-btn" @click="procurementForm.amountTiers.splice(idx, 1)">删除</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 5. SEAL -->
      <template v-else-if="form.domain === 'Seal'">
        <div class="card-section">
          <div class="section-subheading-flex">
            <span>印章登记名录</span>
            <button
              type="button"
              class="add-row-btn"
              @click="sealForm.seals.push({ name: '新印章', sealType: 'COMPANY_OFFICIAL', custodianUserId: 'admin', isEnabled: true, allowOut: true, maxOutDays: 3 })"
            >
              ＋ 添加印章
            </button>
          </div>
          <div class="table-wrap compact">
            <table class="inner-table">
              <thead>
                <tr>
                  <th>印章名称</th>
                  <th>印章类型</th>
                  <th style="min-width: 140px;">保管人</th>
                  <th>启用</th>
                  <th>支持外借</th>
                  <th>外借最长天数</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(s, idx) in sealForm.seals" :key="idx">
                  <td><input v-model="s.name" placeholder="公章名称" /></td>
                  <td>
                    <select v-model="s.sealType">
                      <option value="COMPANY_OFFICIAL">公章 (COMPANY_OFFICIAL)</option>
                      <option value="CONTRACT">合同专用章 (CONTRACT)</option>
                      <option value="LEGAL_REPRESENTATIVE">法人章 (LEGAL_REPRESENTATIVE)</option>
                      <option value="FINANCE">财务专用章 (FINANCE)</option>
                    </select>
                  </td>
                  <td style="min-width: 140px;">
                    <UserSelect
                      v-model="s.custodianUserId"
                      compact
                      placeholder="选择保管人"
                    />
                  </td>
                  <td><input v-model="s.isEnabled" type="checkbox" /></td>
                  <td><input v-model="s.allowOut" type="checkbox" /></td>
                  <td><input v-model.number="s.maxOutDays" type="number" min="0" max="90" /></td>
                  <td>
                    <button type="button" class="del-row-btn" @click="sealForm.seals.splice(idx, 1)">删除</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card-section">
          <div class="section-subheading-flex">
            <span>用印文件类别与风险等级定义</span>
            <button
              type="button"
              class="add-row-btn"
              @click="sealForm.documentCategories.push({ name: '新文件类别', riskLevel: 'LOW', isEnabled: true })"
            >
              ＋ 添加类别
            </button>
          </div>
          <div class="table-wrap compact">
            <table class="inner-table">
              <thead>
                <tr>
                  <th>文件类别</th>
                  <th>风险等级</th>
                  <th>启用</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(dc, idx) in sealForm.documentCategories" :key="idx">
                  <td><input v-model="dc.name" placeholder="如 工商变更" /></td>
                  <td>
                    <select v-model="dc.riskLevel">
                      <option value="HIGH">高风险 (HIGH)</option>
                      <option value="MEDIUM">中风险 (MEDIUM)</option>
                      <option value="LOW">低风险 (LOW)</option>
                    </select>
                  </td>
                  <td><input v-model="dc.isEnabled" type="checkbox" /></td>
                  <td>
                    <button type="button" class="del-row-btn" @click="sealForm.documentCategories.splice(idx, 1)">删除</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 6. DICTIONARY -->
      <template v-else-if="form.domain === 'Dictionary'">
        <div class="card-section">
          <div class="section-subheading-flex">
            <span>字典项配置列表</span>
            <button
              type="button"
              class="add-row-btn"
              @click="dictionaryForm.items.push({ code: 'ITEM_CODE', name: '字典项名称', sortOrder: dictionaryForm.items.length + 1, isEnabled: true, description: '' })"
            >
              ＋ 添加字典项
            </button>
          </div>
          <div class="table-wrap compact">
            <table class="inner-table">
              <thead>
                <tr>
                  <th>字典编码</th>
                  <th>字典名称</th>
                  <th>排序</th>
                  <th>启用</th>
                  <th>描述说明</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(it, idx) in dictionaryForm.items" :key="idx">
                  <td><input v-model="it.code" placeholder="编码" /></td>
                  <td><input v-model="it.name" placeholder="名称" /></td>
                  <td><input v-model.number="it.sortOrder" type="number" style="width: 60px;" /></td>
                  <td><input v-model="it.isEnabled" type="checkbox" /></td>
                  <td><input v-model="it.description" placeholder="描述" /></td>
                  <td>
                    <button type="button" class="del-row-btn" @click="dictionaryForm.items.splice(idx, 1)">删除</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>
    </div>

    <!-- Raw JSON Editor -->
    <div v-else class="raw-json-editor-container">
      <textarea
        v-model="form.contentJson"
        rows="16"
        class="raw-json-textarea"
        placeholder="请输入符合 JSON 规范的参数对象"
      ></textarea>
    </div>

    <p v-if="validationError || store.error" class="dialog-error">{{ validationError || store.error }}</p>
  </OaDialog>

  <!-- ---------------- Publish Dialog ---------------- -->
  <OaDialog
    :open="publishModalOpen"
    :title="`发布业务配置 (v${publishTarget?.version})`"
    :description="`${publishTarget?.name} (${publishTarget?.domain} - ${publishTarget?.code})`"
    submit-label="确认发布"
    :busy="store.saving"
    @close="publishModalOpen = false"
    @submit="confirmPublish"
  >
    <div class="publish-mode-selector">
      <label class="radio-label">
        <input v-model="publishForm.publishType" type="radio" value="immediate" />
        <strong>立即生效</strong>
        <small>从当前时间开始生效，新提交业务单据即刻使用此版本规则。</small>
      </label>
      <label class="radio-label">
        <input v-model="publishForm.publishType" type="radio" value="scheduled" />
        <strong>指定未来时间生效 (SCHEDULED)</strong>
        <small>在指定到达生效时间点前为待生效状态，到达生效时间后自动接管。</small>
      </label>
    </div>

    <div v-if="publishForm.publishType === 'scheduled'" class="dialog-field" style="margin-top: 12px;">
      指定生效时间
      <input v-model="publishForm.effectiveFrom" type="datetime-local" />
    </div>

    <div class="dialog-field" style="margin-top: 12px;">
      指定失效时间 (可选)
      <input v-model="publishForm.effectiveTo" type="datetime-local" />
      <small>留空表示长期有效，到达失效时间后配置自动转为下线。</small>
    </div>

    <div class="operation-warning" style="margin-top: 16px;">
      发布后，该版本状态将锁定为生效/待生效，<strong>不可直接修改</strong>。如需变更规则，须基于此版本升版创建新草稿。
    </div>

    <p v-if="validationError || store.error" class="dialog-error">{{ validationError || store.error }}</p>
  </OaDialog>

  <!-- ---------------- Retire Dialog ---------------- -->
  <OaDialog
    :open="retireModalOpen"
    :title="`下线业务配置 (v${retireTarget?.version})`"
    :description="retireTarget?.name"
    submit-label="确认下线"
    :danger="true"
    :busy="store.saving"
    @close="retireModalOpen = false"
    @submit="confirmRetire"
  >
    <p class="operation-warning">
      下线后，新提交的业务单据将不再匹配此版本配置；<strong>已引用此版本的历史业务单据仍保留原规则快照，不受影响</strong>。
    </p>

    <div class="dialog-field" style="margin-top: 12px;">
      下线生效时间
      <input v-model="retireForm.effectiveTo" type="datetime-local" />
      <small>默认为当前时间</small>
    </div>

    <p v-if="store.error" class="dialog-error">{{ store.error }}</p>
  </OaDialog>

  <!-- ---------------- Delete Confirmation Dialog ---------------- -->
  <OaDialog
    :open="deleteModalOpen"
    title="删除配置草稿"
    :description="deleteTarget?.name"
    :submit-label="deleteTarget?.referenceCount ? '不可删除' : '确认删除'"
    :danger="true"
    :busy="store.saving"
    @close="deleteModalOpen = false"
    @submit="confirmDelete"
  >
    <template v-if="deleteTarget?.referenceCount && deleteTarget.referenceCount > 0">
      <div class="operation-warning danger">
        ⚠️ <strong>禁止删除：</strong>该配置草稿已被历史单据引用（引用记录数：{{ deleteTarget.referenceCount }}）。
        为保证业务数据合规性与不可篡改审计追踪，系统已锁定此配置禁止删除。
      </div>
    </template>
    <template v-else-if="deleteTarget?.status !== 'DRAFT'">
      <div class="operation-warning danger">
        ⚠️ <strong>禁止删除：</strong>当前状态为 {{ statusLabel(deleteTarget?.status || '') }}，系统仅允许删除未生效的 DRAFT 草稿配置。已生效或已下线配置需保留版本记录。
      </div>
    </template>
    <template v-else>
      <p class="operation-warning">
        确定要彻底删除该配置草稿（v{{ deleteTarget?.version }}）吗？删除后不可恢复。
      </p>
    </template>
    <p v-if="store.error" class="dialog-error">{{ store.error }}</p>
  </OaDialog>

  <!-- ---------------- Version History Dialog ---------------- -->
  <OaDialog
    :open="versionHistoryModalOpen"
    :title="`版本历史 - ${historyTarget?.name}`"
    :description="`${historyTarget?.domain} · ${historyTarget?.code}`"
    submit-label="关闭"
    width="840px"
    @close="versionHistoryModalOpen = false"
    @submit="versionHistoryModalOpen = false"
  >
    <div v-if="store.versions.length" class="table-wrap compact">
      <table class="data-table">
        <thead>
          <tr>
            <th>版本号</th>
            <th>状态</th>
            <th>生效周期</th>
            <th>发布人 / 发布时间</th>
            <th>单据引用</th>
            <th class="action-cell">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="ver in store.versions" :key="ver.id">
            <td><strong>v{{ ver.version }}</strong></td>
            <td>
              <span class="status-badge" :class="statusClass(ver.status)">
                {{ statusLabel(ver.status) }}
              </span>
            </td>
            <td>
              <span>{{ formatDate(ver.effectiveFrom) }}</span>
              <small class="expiry-date">至 {{ formatDate(ver.effectiveTo) }}</small>
            </td>
            <td>
              <span>{{ ver.publishedByName || '—' }}</span>
              <small>{{ formatDateTime(ver.publishedAt) }}</small>
            </td>
            <td>
              <span v-if="ver.referenceCount > 0" class="ref-count-tag">{{ ver.referenceCount }} 笔</span>
              <span v-else class="text-muted">无引用</span>
            </td>
            <td class="task-actions">
              <button class="secondary" @click="openDetail(ver.id)">查看参数</button>
              <button class="primary-btn" @click="handleBranchFromHistory(ver)">基于此版本升版</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-else class="empty">暂无历史版本记录。</p>
  </OaDialog>

  <!-- ---------------- Detail Dialog ---------------- -->
  <OaDialog
    :open="detailModalOpen"
    :title="`配置详情 - ${detailRecord?.name} (v${detailRecord?.version})`"
    :description="`${detailRecord?.domain} · ${detailRecord?.code}`"
    submit-label="关闭"
    width="880px"
    @close="detailModalOpen = false"
    @submit="detailModalOpen = false"
  >
    <dl v-if="detailRecord" class="detail-grid">
      <div>
        <dt>业务域</dt>
        <dd>{{ domainLabel(detailRecord.domain) }} ({{ detailRecord.domain }})</dd>
      </div>
      <div>
        <dt>配置标识</dt>
        <dd><code>{{ detailRecord.code }}</code></dd>
      </div>
      <div>
        <dt>版本状态</dt>
        <dd>
          <span class="status-badge" :class="statusClass(detailRecord.status)">
            {{ statusLabel(detailRecord.status) }} (v{{ detailRecord.version }})
          </span>
        </dd>
      </div>
      <div>
        <dt>单据引用统计</dt>
        <dd>
          <span v-if="detailRecord.referenceCount > 0" class="ref-count-tag">
            被 {{ detailRecord.referenceCount }} 笔单据冻结快照
          </span>
          <span v-else class="text-muted">暂无业务单据引用</span>
        </dd>
      </div>
      <div>
        <dt>生效区间</dt>
        <dd>{{ formatDate(detailRecord.effectiveFrom) }} 至 {{ formatDate(detailRecord.effectiveTo) }}</dd>
      </div>
      <div>
        <dt>创建信息</dt>
        <dd>{{ detailRecord.createdByName }} · {{ formatDateTime(detailRecord.createdAt) }}</dd>
      </div>
      <div>
        <dt>发布信息</dt>
        <dd>{{ detailRecord.publishedByName || '未发布' }} · {{ formatDateTime(detailRecord.publishedAt) }}</dd>
      </div>
      <div>
        <dt>更新信息</dt>
        <dd>{{ detailRecord.updatedByName }} · {{ formatDateTime(detailRecord.updatedAt) }}</dd>
      </div>
      <div class="wide">
        <dt>配置描述</dt>
        <dd>{{ detailRecord.description || '无' }}</dd>
      </div>
    </dl>

    <!-- Parameter Config Header & Switch -->
    <div class="param-section-heading">
      <div>
        <strong>业务参数生效配置</strong>
        <small>当前版本所固化的生效规则与快照回显</small>
      </div>
      <div class="detail-switch-bar">
        <div class="mode-switch-group">
          <button
            type="button"
            class="switch-btn"
            :class="{ active: detailMode === 'visual' }"
            @click="detailMode = 'visual'"
          >
            结构化 UI 回显
          </button>
          <button
            type="button"
            class="switch-btn"
            :class="{ active: detailMode === 'json' }"
            @click="detailMode = 'json'"
          >
            原始 JSON 快照
          </button>
        </div>
        <button type="button" class="copy-json-btn" @click="copyJson">
          {{ copySuccess ? '✓ 已复制！' : '复制 JSON' }}
        </button>
      </div>
    </div>

    <!-- Mode 1: Visual UI Echo -->
    <div v-if="detailMode === 'visual'" class="detail-visual-wrapper">
      <div v-if="!detailParsed" class="parse-fallback-note">
        <span>无法以结构化形式解析此配置内容，请切换至“原始 JSON 快照”查看。</span>
        <button type="button" class="secondary" @click="detailMode = 'json'">切换为 JSON 查看</button>
      </div>

      <!-- 1. LEAVE -->
      <template v-else-if="detailRecord?.domain === 'Leave'">
        <div class="card-section">
          <div class="section-subheading">通用结转与年假奖励规则</div>
          <div class="detail-kv-grid">
            <div class="detail-kv-item">
              <span class="kv-label">跨年结转</span>
              <span class="kv-value">
                <span class="status-pill" :class="detailParsed.allowCrossYear ? 'status-enabled' : 'status-disabled'">
                  {{ detailParsed.allowCrossYear ? '允许跨年结转' : '当年清零，禁止结转' }}
                </span>
              </span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">加班调休有效期</span>
              <span class="kv-value"><strong>{{ detailParsed.compTimeValidityDays ?? 365 }}</strong> 天</span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">法定最低标准保护</span>
              <span class="kv-value">
                <span class="status-pill" :class="detailParsed.annualLeaveBonus?.legalMinStandardProtected !== false ? 'status-enabled' : 'status-disabled'">
                  {{ detailParsed.annualLeaveBonus?.legalMinStandardProtected !== false ? '强制法定最低标准' : '未开启' }}
                </span>
              </span>
            </div>
          </div>

          <div class="bonus-tiers-readonly" style="margin-top: 10px;">
            <span class="bonus-title">司龄年假奖励阶梯：</span>
            <div class="bonus-tags-group">
              <span class="tier-pill">1~3年司龄: +{{ detailParsed.annualLeaveBonus?.tier1BonusDays ?? 0 }} 天</span>
              <span class="tier-pill">3~5年司龄: +{{ detailParsed.annualLeaveBonus?.tier2BonusDays ?? 0 }} 天</span>
              <span class="tier-pill">5年以上司龄: +{{ detailParsed.annualLeaveBonus?.tier3BonusDays ?? 0 }} 天</span>
            </div>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">假期类型定义名录 (共 {{ (detailParsed.leaveTypes || []).length }} 项)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>假期编码</th>
                  <th>假期名称</th>
                  <th>状态</th>
                  <th>最小单位</th>
                  <th>需附证明</th>
                  <th>证明门槛说明</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(lt, idx) in (detailParsed.leaveTypes || [])" :key="idx">
                  <td><code>{{ lt.type }}</code></td>
                  <td><strong>{{ lt.name }}</strong></td>
                  <td>
                    <span class="status-pill" :class="lt.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ lt.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                  <td>{{ lt.minUnit }} 天</td>
                  <td>{{ lt.requiresAttachment ? '是' : '否' }}</td>
                  <td>
                    <span v-if="lt.requiresAttachment">
                      {{ lt.attachmentThresholdDays != null ? `超过 ${lt.attachmentThresholdDays} 天必须上传` : '申请即必传证明' }}
                    </span>
                    <span v-else class="text-muted">无需证明</span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.leaveTypes?.length">
                  <td colspan="6" class="text-center text-muted">暂无假期类型配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 2. EXPENSE -->
      <template v-else-if="detailRecord?.domain === 'Expense'">
        <div class="card-section">
          <div class="section-subheading">费用报销类别与限额管控标准 (共 {{ (detailParsed.categories || []).length }} 项)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>类别名称</th>
                  <th>状态</th>
                  <th>单笔限额标准</th>
                  <th>发票凭证</th>
                  <th>超限填理由</th>
                  <th>超限阻断策略</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(cat, idx) in (detailParsed.categories || [])" :key="idx">
                  <td><strong>{{ cat.name }}</strong></td>
                  <td>
                    <span class="status-pill" :class="cat.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ cat.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                  <td>
                    <span v-if="cat.singleLimit != null" class="amount-text">¥{{ Number(cat.singleLimit).toLocaleString() }}</span>
                    <span v-else class="text-muted">不设上限</span>
                  </td>
                  <td>{{ cat.requiresReceipt ? '必须附发票' : '可选' }}</td>
                  <td>{{ cat.requiresReasonWhenExceeded ? '超限必填' : '不强制' }}</td>
                  <td>
                    <span class="risk-badge" :class="cat.blockWhenExceeded ? 'risk-high' : 'risk-low'">
                      {{ cat.blockWhenExceeded ? '超限直接阻断' : '超限允许提交' }}
                    </span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.categories?.length">
                  <td colspan="6" class="text-center text-muted">暂无报销类别配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 3. TRAVEL -->
      <template v-else-if="detailRecord?.domain === 'Travel'">
        <div class="card-section">
          <div class="section-subheading">城市级别划分与适用职级</div>
          <div class="travel-tiers-display">
            <div v-for="(tier, idx) in (detailParsed.cityTiers || [])" :key="idx" class="travel-tier-card">
              <div class="tier-card-header">
                <strong>{{ tier.tierName }}</strong>
                <span class="tag-count">({{ (tier.cities || []).length }} 个城市)</span>
              </div>
              <div class="tier-card-cities">
                <span v-for="c in (tier.cities || [])" :key="c" class="tag-badge">{{ c }}</span>
                <span v-if="!tier.cities?.length" class="text-muted">未配置城市</span>
              </div>
            </div>
          </div>

          <div class="ranks-display" style="margin-top: 12px;">
            <span class="kv-label">适用员工职级体系：</span>
            <div class="tag-list-inline">
              <span v-for="r in (detailParsed.employeeRanks || [])" :key="r" class="rank-pill">{{ r }}</span>
              <span v-if="!detailParsed.employeeRanks?.length" class="text-muted">全员通用</span>
            </div>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">差旅报销标准矩阵 (共 {{ (detailParsed.standards || []).length }} 条)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>城市级别</th>
                  <th>适用职级</th>
                  <th>住宿每晚限额</th>
                  <th>每日餐补标准</th>
                  <th>交通工具标准</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(std, idx) in (detailParsed.standards || [])" :key="idx">
                  <td><strong>{{ std.cityTier }}</strong></td>
                  <td><span class="rank-pill">{{ std.rank }}</span></td>
                  <td>
                    <span v-if="std.hotelDailyLimit != null" class="amount-text">¥{{ Number(std.hotelDailyLimit).toLocaleString() }} / 晚</span>
                    <span v-else class="text-muted">-</span>
                  </td>
                  <td>
                    <span v-if="std.mealDailyAllowance != null" class="amount-text">¥{{ Number(std.mealDailyAllowance).toLocaleString() }} / 天</span>
                    <span v-else class="text-muted">-</span>
                  </td>
                  <td>{{ std.transportationStandard || '-' }}</td>
                </tr>
                <tr v-if="!detailParsed.standards?.length">
                  <td colspan="5" class="text-center text-muted">暂无差旅标准配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 4. PROCUREMENT -->
      <template v-else-if="detailRecord?.domain === 'Procurement'">
        <div class="card-section">
          <div class="section-subheading">采购风控与流程参数</div>
          <div class="detail-kv-grid">
            <div class="detail-kv-item">
              <span class="kv-label">报价比价起计门槛</span>
              <span class="kv-value">
                <strong v-if="detailParsed.quoteAttachmentThreshold != null" class="amount-text">
                  ≥ ¥{{ Number(detailParsed.quoteAttachmentThreshold).toLocaleString() }}
                </strong>
                <span v-else class="text-muted">不限</span>
                <small class="hint-inline">（超额必须上传比价单）</small>
              </span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">默认采购专员</span>
              <span class="kv-value">
                <span v-if="detailParsed.defaultPurchaserUserId" class="user-chip">
                  <span class="user-avatar-mini">{{ detailParsed.defaultPurchaserUserId.slice(0, 1).toUpperCase() }}</span>
                  <strong>{{ getUserDisplayName(detailParsed.defaultPurchaserUserId) }}</strong>
                </span>
                <span v-else class="text-muted">系统自动指派</span>
              </span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">强制到货验收流程</span>
              <span class="kv-value">
                <span class="status-pill" :class="detailParsed.requiresAcceptance ? 'status-enabled' : 'status-disabled'">
                  {{ detailParsed.requiresAcceptance ? '强制要求' : '无需独立验收' }}
                </span>
              </span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">验收负责人/角色</span>
              <span class="kv-value">{{ detailParsed.acceptanceRoleOrAssignee || '部门负责人' }}</span>
            </div>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">采购品类定义 (共 {{ (detailParsed.categories || []).length }} 项)</div>
          <div class="tag-list-inline">
            <span
              v-for="(cat, idx) in (detailParsed.categories || [])"
              :key="idx"
              class="tag-badge"
              :class="{ 'tag-disabled': !cat.isEnabled }"
            >
              {{ cat.name }}
              <span v-if="!cat.isEnabled" class="text-muted"> (停用)</span>
            </span>
            <span v-if="!detailParsed.categories?.length" class="text-muted">暂无采购品类</span>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">采购金额审批层级 (共 {{ (detailParsed.amountTiers || []).length }} 级)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>层级名称</th>
                  <th>最高金额上限 (元)</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(tier, idx) in (detailParsed.amountTiers || [])" :key="idx">
                  <td><strong>{{ tier.name }}</strong></td>
                  <td>
                    <span v-if="tier.maxAmount != null" class="amount-text">≤ ¥{{ Number(tier.maxAmount).toLocaleString() }}</span>
                    <span v-else class="text-muted">无上限 (重大采购决策)</span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.amountTiers?.length">
                  <td colspan="2" class="text-center text-muted">暂无金额层级配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 5. SEAL -->
      <template v-else-if="detailRecord?.domain === 'Seal'">
        <div class="card-section">
          <div class="section-subheading">印章名录与外带管控 (共 {{ (detailParsed.seals || []).length }} 枚)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>印章名称</th>
                  <th>印章类型</th>
                  <th>指定保管人</th>
                  <th>状态</th>
                  <th>外带借出</th>
                  <th>最长借出天数</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(s, idx) in (detailParsed.seals || [])" :key="idx">
                  <td><strong>{{ s.name }}</strong></td>
                  <td><code>{{ s.sealType }}</code></td>
                  <td>
                    <span v-if="s.custodianUserId" class="user-chip">
                      <span class="user-avatar-mini">{{ s.custodianUserId.slice(0, 1).toUpperCase() }}</span>
                      {{ getUserDisplayName(s.custodianUserId) }}
                    </span>
                    <span v-else class="text-muted">-</span>
                  </td>
                  <td>
                    <span class="status-pill" :class="s.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ s.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                  <td>
                    <span class="status-pill" :class="s.allowOut ? 'status-enabled' : 'status-disabled'">
                      {{ s.allowOut ? '允许外带' : '禁止外借' }}
                    </span>
                  </td>
                  <td>
                    <span v-if="s.allowOut"><strong>{{ s.maxOutDays ?? 0 }}</strong> 天</span>
                    <span v-else class="text-muted">-</span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.seals?.length">
                  <td colspan="6" class="text-center text-muted">暂无印章配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">用印文件类别与风险等级定义 (共 {{ (detailParsed.documentCategories || []).length }} 项)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>文件类别</th>
                  <th>风险等级</th>
                  <th>状态</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(dc, idx) in (detailParsed.documentCategories || [])" :key="idx">
                  <td><strong>{{ dc.name }}</strong></td>
                  <td>
                    <span class="risk-badge" :class="getRiskBadgeClass(dc.riskLevel)">
                      {{ getRiskLevelLabel(dc.riskLevel) }}
                    </span>
                  </td>
                  <td>
                    <span class="status-pill" :class="dc.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ dc.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.documentCategories?.length">
                  <td colspan="3" class="text-center text-muted">暂无文件类别配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">综合风险评估指标分值</div>
          <div class="detail-kv-grid">
            <div class="detail-kv-item">
              <span class="kv-label">高风险指标分值</span>
              <span class="kv-value"><strong class="text-danger">{{ detailParsed.riskRules?.highRiskMetric ?? 3 }}</strong></span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">中风险指标分值</span>
              <span class="kv-value"><strong class="text-warning">{{ detailParsed.riskRules?.mediumRiskMetric ?? 2 }}</strong></span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">低风险指标分值</span>
              <span class="kv-value"><strong class="text-success">{{ detailParsed.riskRules?.lowRiskMetric ?? 1 }}</strong></span>
            </div>
          </div>
        </div>
      </template>

      <!-- 6. DICTIONARY -->
      <template v-else-if="detailRecord?.domain === 'Dictionary'">
        <div class="card-section">
          <div class="section-subheading">业务字典枚举项清单 (共 {{ (detailParsed.items || []).length }} 项)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>排序</th>
                  <th>项编码</th>
                  <th>显示名称</th>
                  <th>状态</th>
                  <th>说明备注</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(it, idx) in (detailParsed.items || [])" :key="idx">
                  <td>{{ it.sortOrder ?? (Number(idx) + 1) }}</td>
                  <td><code>{{ it.code }}</code></td>
                  <td><strong>{{ it.name }}</strong></td>
                  <td>
                    <span class="status-pill" :class="it.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ it.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                  <td>{{ it.description || '-' }}</td>
                </tr>
                <tr v-if="!detailParsed.items?.length">
                  <td colspan="5" class="text-center text-muted">暂无字典项配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- Other / Custom -->
      <template v-else>
        <div class="card-section">
          <div class="section-subheading">自定义配置参数快照</div>
          <pre class="json-code-block"><code>{{ formattedJson }}</code></pre>
        </div>
      </template>
    </div>

    <!-- Mode 2: Raw JSON Snapshot -->
    <div v-else class="detail-json-wrapper">
      <pre class="json-code-block"><code>{{ formattedJson }}</code></pre>
    </div>
  </OaDialog>
</template>

<style scoped>
.domain-tabs-nav {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 14px;
}

.domain-tab-btn {
  border: 1px solid #d9e0ea;
  border-radius: 6px;
  padding: 8px 16px;
  background: #fff;
  color: #4b5563;
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.15s ease;
}

.domain-tab-btn:hover {
  background: #f3f4f6;
  border-color: #cbd5e1;
}

.domain-tab-btn.active {
  background: #3478e8;
  border-color: #3478e8;
  color: #fff;
}

.domain-tag {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 4px;
  background: #eff6ff;
  color: #1d4ed8;
  font-size: 0.78rem;
  font-weight: 600;
}

.code-badge {
  display: block;
  font-family: monospace;
  color: #64748b;
  font-size: 0.75rem;
  margin-top: 2px;
}

.desc-text {
  display: block;
  color: #94a3b8;
  font-size: 0.74rem;
  margin-top: 2px;
  max-width: 260px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.version-badge {
  display: inline-block;
  padding: 2px 7px;
  border-radius: 999px;
  background: #e0e7ff;
  color: #3730a3;
  font-size: 0.76rem;
  font-weight: 700;
}

.status-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 4px;
  font-size: 0.76rem;
  font-weight: 600;
}

.status-effective {
  background: #dcfce7;
  color: #15803d;
}

.status-scheduled {
  background: #e0f2fe;
  color: #0369a1;
}

.status-draft {
  background: #fef3c7;
  color: #b45309;
}

.status-retired {
  background: #f1f5f9;
  color: #64748b;
}

.expiry-date {
  display: block;
  color: #94a3b8;
  font-size: 0.72rem;
  margin-top: 2px;
}

.ref-count-tag {
  display: inline-block;
  padding: 2px 6px;
  border-radius: 4px;
  background: #f0fdf4;
  color: #166534;
  font-size: 0.75rem;
  font-weight: 600;
}

.text-muted {
  color: #94a3b8;
  font-size: 0.76rem;
}

.config-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.primary-btn {
  border: 0;
  border-radius: 4px;
  padding: 6px 10px;
  background: #3478e8;
  color: #fff;
  font-size: 0.76rem;
  cursor: pointer;
}

.primary-btn:hover {
  background: #2563eb;
}

.warning-outline {
  border: 1px solid #f59e0b;
  border-radius: 4px;
  padding: 6px 10px;
  background: #fff;
  color: #b45309;
  font-size: 0.76rem;
  cursor: pointer;
}

.warning-outline:hover {
  background: #fef3c7;
}

.danger-outline {
  border: 1px solid #ef4444;
  border-radius: 4px;
  padding: 6px 10px;
  background: #fff;
  color: #dc2626;
  font-size: 0.76rem;
  cursor: pointer;
}

.danger-outline:hover {
  background: #fee2e2;
}

/* Forms in Dialog */
.form-grid-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-bottom: 8px;
}

.param-section-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 16px;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid #e2e8f0;
}

.param-section-heading strong {
  display: block;
  font-size: 0.9rem;
  color: #1e293b;
}

.param-section-heading small {
  color: #64748b;
  font-size: 0.75rem;
}

.mode-switch-group {
  display: flex;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  overflow: hidden;
}

.switch-btn {
  border: 0;
  padding: 5px 12px;
  background: #f8fafc;
  color: #64748b;
  font-size: 0.78rem;
  cursor: pointer;
}

.switch-btn.active {
  background: #3478e8;
  color: #fff;
  font-weight: 600;
}

.visual-editor-container {
  display: flex;
  flex-direction: column;
  gap: 16px;
  max-height: 480px;
  overflow-y: auto;
  padding-right: 4px;
}

.card-section {
  padding: 12px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: #f8fafc;
}

.section-subheading {
  font-size: 0.82rem;
  font-weight: 700;
  color: #334155;
  margin-bottom: 10px;
}

.section-subheading-flex {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.82rem;
  font-weight: 700;
  color: #334155;
  margin-bottom: 10px;
}

.add-row-btn {
  border: 1px solid #3478e8;
  border-radius: 4px;
  padding: 4px 10px;
  background: #fff;
  color: #3478e8;
  font-size: 0.75rem;
  cursor: pointer;
}

.add-row-btn:hover {
  background: #eff6ff;
}

.del-row-btn {
  border: 0;
  padding: 4px 8px;
  background: transparent;
  color: #ef4444;
  font-size: 0.75rem;
  cursor: pointer;
}

.del-row-btn:hover {
  text-decoration: underline;
}

.form-row-checks {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 18px;
  margin-bottom: 12px;
}

.checkbox-inline {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  color: #334155;
  cursor: pointer;
}

.checkbox-inline.highlight {
  color: #0369a1;
  font-weight: 600;
}

.annual-bonus-box {
  padding: 10px;
  border: 1px dashed #cbd5e1;
  border-radius: 6px;
  background: #fff;
}

.annual-bonus-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.bonus-tiers-grid {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 8px;
}

.inner-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.78rem;
  background: #fff;
}

.inner-table th {
  padding: 6px 8px;
  background: #f1f5f9;
  color: #475569;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
}

.inner-table td {
  padding: 6px 8px;
  border-bottom: 1px solid #f1f5f9;
}

.inner-table input,
.inner-table select {
  width: 100%;
  box-sizing: border-box;
  padding: 4px 6px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 0.76rem;
}

.inner-table input[type='checkbox'] {
  width: auto;
}

.travel-tier-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
  align-items: center;
}

.tier-name-input {
  width: 140px;
  padding: 6px 8px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 0.78rem;
}

.tier-cities-input {
  flex: 1;
  padding: 6px 8px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 0.78rem;
}

.raw-json-editor-container {
  display: flex;
  flex-direction: column;
}

.raw-json-textarea {
  width: 100%;
  box-sizing: border-box;
  font-family: monospace;
  font-size: 0.8rem;
  line-height: 1.4;
  padding: 10px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  background: #1e293b;
  color: #f8fafc;
}

.publish-mode-selector {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.radio-label {
  display: grid;
  grid-template-columns: auto 1fr;
  grid-template-rows: auto auto;
  column-gap: 8px;
  padding: 10px;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  cursor: pointer;
}

.radio-label input {
  grid-row: 1 / span 2;
  margin-top: 3px;
}

.radio-label strong {
  font-size: 0.85rem;
  color: #1e293b;
}

.radio-label small {
  color: #64748b;
  font-size: 0.75rem;
}

.json-viewer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 14px;
  margin-bottom: 6px;
}

.json-code-block {
  max-height: 280px;
  overflow: auto;
  padding: 12px;
  border-radius: 6px;
  background: #0f172a;
  color: #38bdf8;
  font-family: monospace;
  font-size: 0.78rem;
  line-height: 1.45;
  margin: 0;
}

/* Detail Modal Visual Echo Styles */
.detail-switch-bar {
  display: flex;
  align-items: center;
  gap: 8px;
}

.copy-json-btn {
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  padding: 5px 12px;
  background: #fff;
  color: #475569;
  font-size: 0.78rem;
  cursor: pointer;
  white-space: nowrap;
  transition: all 0.15s ease;
}

.copy-json-btn:hover {
  background: #f1f5f9;
  border-color: #94a3b8;
}

.detail-visual-wrapper {
  display: flex;
  flex-direction: column;
  gap: 12px;
  max-height: 480px;
  overflow-y: auto;
  padding-right: 4px;
}

.detail-json-wrapper {
  margin-top: 4px;
}

.detail-kv-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 12px;
}

.detail-kv-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.kv-label {
  font-size: 0.75rem;
  color: #64748b;
}

.kv-value {
  font-size: 0.85rem;
  color: #1e293b;
}

.status-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.74rem;
  font-weight: 600;
}

.status-pill.status-enabled {
  background: #dcfce7;
  color: #15803d;
}

.status-pill.status-disabled {
  background: #f1f5f9;
  color: #94a3b8;
}

.risk-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.74rem;
  font-weight: 600;
}

.risk-high {
  background: #fee2e2;
  color: #dc2626;
}

.risk-medium {
  background: #fef3c7;
  color: #d97706;
}

.risk-low {
  background: #ecfdf5;
  color: #059669;
}

.readonly-table th {
  background: #f8fafc;
  color: #475569;
  font-weight: 600;
}

.readonly-table td {
  color: #1e293b;
}

.amount-text {
  font-weight: 700;
  color: #1d4ed8;
}

.travel-tiers-display {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.travel-tier-card {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 10px;
  background: #fff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
}

.tier-card-header {
  display: flex;
  align-items: center;
  gap: 6px;
  min-width: 140px;
}

.tag-count {
  color: #94a3b8;
  font-size: 0.72rem;
}

.tier-card-cities {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  flex: 1;
}

.tag-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  background: #eff6ff;
  color: #2563eb;
  font-size: 0.75rem;
  font-weight: 500;
}

.tag-badge.tag-disabled {
  background: #f1f5f9;
  color: #94a3b8;
}

.rank-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  background: #f0fdf4;
  color: #166534;
  font-size: 0.75rem;
  font-weight: 500;
}

.tag-list-inline {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 6px;
}

.bonus-tiers-readonly {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 0.8rem;
  color: #334155;
}

.bonus-tags-group {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.tier-pill {
  padding: 2px 8px;
  border-radius: 4px;
  background: #fdf4ff;
  color: #a21caf;
  font-size: 0.75rem;
  font-weight: 500;
}

.text-danger {
  color: #dc2626;
}

.text-warning {
  color: #d97706;
}

.text-success {
  color: #15803d;
}

.text-center {
  text-align: center;
}

.hint-inline {
  color: #94a3b8;
  font-size: 0.72rem;
  margin-left: 4px;
}

.parse-fallback-note {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px;
  background: #fffbeb;
  border: 1px solid #fef3c7;
  border-radius: 6px;
  color: #92400e;
  font-size: 0.82rem;
}

.user-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 2px 8px;
  background: #f1f5f9;
  border-radius: 999px;
  font-size: 0.78rem;
  color: #1e293b;
}

.user-avatar-mini {
  width: 18px;
  height: 18px;
  border-radius: 50%;
  background: #3478e8;
  color: #fff;
  font-size: 0.65rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
}

/* Responsive styles down to 390px */
@media (max-width: 768px) {
  .form-grid-2 {
    grid-template-columns: 1fr;
  }
  .bonus-tiers-grid {
    grid-template-columns: 1fr;
  }
  .domain-tabs-nav {
    overflow-x: auto;
    flex-wrap: nowrap;
    padding-bottom: 6px;
  }
  .domain-tab-btn {
    white-space: nowrap;
  }
  .config-actions {
    flex-direction: column;
    min-width: 70px;
  }
  .travel-tier-row {
    flex-direction: column;
    align-items: stretch;
  }
  .tier-name-input {
    width: 100%;
  }
}

@media (max-width: 480px) {
  .page-heading {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }
  .page-heading button {
    width: 100%;
  }
  .param-section-heading {
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
  }
  .mode-switch-group {
    width: 100%;
  }
  .switch-btn {
    flex: 1;
    text-align: center;
  }
  .detail-switch-bar {
    width: 100%;
    flex-direction: column;
    align-items: stretch;
  }
  .copy-json-btn {
    width: 100%;
    text-align: center;
  }
  .travel-tier-card {
    flex-direction: column;
    align-items: flex-start;
    gap: 6px;
  }
  .tier-card-header {
    min-width: auto;
  }
}
</style>
