import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { useApiClient } from '../api/client'
import type {
  EffectiveBusinessConfigurationBundle,
  EffectiveDictionaryOption,
  EffectiveExpenseCategoryOption,
  EffectiveLeaveTypeOption,
  EffectiveProcurementAmountTierOption,
  EffectiveProcurementCategoryOption,
  EffectiveSealDocCategoryOption,
  EffectiveSealOption,
  TravelCityTierConfig,
  TravelStandardItemConfig
} from '../api/types'

const createDefaultBundle = (): EffectiveBusinessConfigurationBundle => ({
  leaveTypes: [
    { code: 'Annual', name: '年假', minUnit: 0.5, requiresAttachment: false },
    { code: 'Personal', name: '事假', minUnit: 0.5, requiresAttachment: false },
    { code: 'Sick', name: '病假', minUnit: 0.5, requiresAttachment: true, attachmentThresholdDays: 2.0 },
    { code: 'CompTime', name: '调休', minUnit: 0.5, requiresAttachment: false },
    { code: 'Marriage', name: '婚假', minUnit: 1.0, requiresAttachment: false },
    { code: 'Maternity', name: '产假', minUnit: 1.0, requiresAttachment: false },
    { code: 'Paternity', name: '陪产假', minUnit: 1.0, requiresAttachment: false },
    { code: 'Bereavement', name: '丧假', minUnit: 1.0, requiresAttachment: false }
  ],
  expenseCategories: [
    { code: '交通', name: '交通', singleLimit: 5000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
    { code: '住宿', name: '住宿', singleLimit: 3000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
    { code: '餐饮招待', name: '餐饮招待', singleLimit: 2000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
    { code: '办公', name: '办公', singleLimit: 10000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
    { code: '通讯', name: '通讯', singleLimit: 1000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
    { code: '培训', name: '培训', singleLimit: 20000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false },
    { code: '其他', name: '其他', singleLimit: 5000, requiresReceipt: true, requiresReasonWhenExceeded: true, blockWhenExceeded: false }
  ],
  travelCityTiers: [
    { tierName: '一线城市', cities: ['北京', '上海', '广州', '深圳'] },
    { tierName: '二线城市', cities: ['杭州', '南京', '成都', '武汉', '西安', '苏州', '天津', '重庆'] },
    { tierName: '其他城市', cities: [] }
  ],
  travelEmployeeRanks: ['员工', '部门负责人', '总经理'],
  travelStandards: [
    { rank: '员工', cityTier: '一线城市', hotelDailyLimit: 450, mealDailyAllowance: 100, transportationStandard: '高铁二等座/飞机经济舱' },
    { rank: '部门负责人', cityTier: '一线城市', hotelDailyLimit: 650, mealDailyAllowance: 150, transportationStandard: '高铁一等座/飞机经济舱' },
    { rank: '总经理', cityTier: '一线城市', hotelDailyLimit: 900, mealDailyAllowance: 200, transportationStandard: '高铁商务座/飞机公务舱' },
    { rank: '员工', cityTier: '二线城市', hotelDailyLimit: 350, mealDailyAllowance: 80, transportationStandard: '高铁二等座/飞机经济舱' },
    { rank: '部门负责人', cityTier: '二线城市', hotelDailyLimit: 500, mealDailyAllowance: 120, transportationStandard: '高铁一等座/飞机经济舱' },
    { rank: '总经理', cityTier: '二线城市', hotelDailyLimit: 700, mealDailyAllowance: 160, transportationStandard: '高铁商务座/飞机公务舱' },
    { rank: '员工', cityTier: '其他城市', hotelDailyLimit: 260, mealDailyAllowance: 60, transportationStandard: '高铁二等座/飞机经济舱' },
    { rank: '部门负责人', cityTier: '其他城市', hotelDailyLimit: 380, mealDailyAllowance: 100, transportationStandard: '高铁一等座/飞机经济舱' },
    { rank: '总经理', cityTier: '其他城市', hotelDailyLimit: 500, mealDailyAllowance: 130, transportationStandard: '高铁商务座/飞机公务舱' }
  ],
  procurementCategories: [
    { code: '办公用品', name: '办公用品' },
    { code: 'IT设备', name: 'IT设备' },
    { code: '软件服务', name: '软件服务' },
    { code: '行政物资', name: '行政物资' },
    { code: '市场物料', name: '市场物料' },
    { code: '生产物料', name: '生产物料' },
    { code: '专业服务', name: '专业服务' },
    { code: '其他', name: '其他' }
  ],
  procurementAmountTiers: [
    { name: '常规采购', maxAmount: 10000 },
    { name: '重要采购', maxAmount: 50000 },
    { name: '重大采购', maxAmount: null }
  ],
  procurementQuoteThreshold: 5000,
  seals: [
    { code: '公章', name: '公章', sealType: '公章', allowOut: true, maxOutDays: 7 },
    { code: '合同专用章', name: '合同专用章', sealType: '合同专用章', allowOut: true, maxOutDays: 7 },
    { code: '财务专用章', name: '财务专用章', sealType: '财务专用章', allowOut: false, maxOutDays: 0 },
    { code: '法人章', name: '法人章', sealType: '法人章', allowOut: false, maxOutDays: 0 },
    { code: '人事专用章', name: '人事专用章', sealType: '人事专用章', allowOut: true, maxOutDays: 5 },
    { code: '其他', name: '其他', sealType: '其他', allowOut: true, maxOutDays: 3 }
  ],
  sealDocumentCategories: [
    { code: '合同协议', name: '合同协议', riskLevel: 'MEDIUM' },
    { code: '招投标文件', name: '招投标文件', riskLevel: 'MEDIUM' },
    { code: '公文函件', name: '公文函件', riskLevel: 'LOW' },
    { code: '资质证明', name: '资质证明', riskLevel: 'LOW' },
    { code: '财务报表', name: '财务报表', riskLevel: 'MEDIUM' },
    { code: '人事材料', name: '人事材料', riskLevel: 'LOW' },
    { code: '其他', name: '其他', riskLevel: 'LOW' }
  ],
  contractTypes: [
    { code: 'FIXED_TERM', name: '固定期限', sortOrder: 1 },
    { code: 'OPEN_ENDED', name: '无固定期限', sortOrder: 2 },
    { code: 'PROJECT_BASED', name: '以完成任务为期限', sortOrder: 3 }
  ],
  attachmentTypes: [
    { code: 'ID_CARD', name: '身份证件', sortOrder: 1 },
    { code: 'DIPLOMA', name: '学历学位证书', sortOrder: 2 },
    { code: 'TITLE_CERT', name: '职称及职业资格证', sortOrder: 3 },
    { code: 'DISCHARGE_PROOF', name: '离职证明', sortOrder: 4 },
    { code: 'HEALTH_REPORT', name: '体检报告', sortOrder: 5 },
    { code: 'BANK_CARD', name: '工资卡信息', sortOrder: 6 },
    { code: 'ENTRY_FORM', name: '入职登记表', sortOrder: 7 },
    { code: 'OTHER', name: '其他材料', sortOrder: 8 }
  ],
  approvalCommentPresets: [
    { code: 'AGREE', name: '同意', sortOrder: 1 },
    { code: 'CONFIRMED', name: '已核对，同意', sortOrder: 2 },
    { code: 'CONDITIONAL', name: '同意，请注意控制预算与合规留痕', sortOrder: 3 },
    { code: 'NEED_INFO', name: '请补充必要佐证材料后再报', sortOrder: 4 },
    { code: 'OVER_BUDGET', name: '超预算或超标准，暂缓推进', sortOrder: 5 },
    { code: 'REJECT', name: '不符合要求，退回修改', sortOrder: 6 }
  ],
  announcementTypes: [
    { code: 'COMPANY_NEWS', name: '公司新闻', sortOrder: 1 },
    { code: 'OFFICE_NOTICE', name: '行政通知', sortOrder: 2 },
    { code: 'SYSTEM_UPDATE', name: '系统维护', sortOrder: 3 },
    { code: 'POLICY', name: '规章制度', sortOrder: 4 }
  ]
})

export const useEffectiveConfigurationStore = defineStore('effectiveConfigurations', () => {
  const bundle = ref<EffectiveBusinessConfigurationBundle>(createDefaultBundle())
  const loaded = ref(false)
  const loading = ref(false)
  const error = ref('')
  const api = useApiClient()

  const leaveTypes = computed(() => bundle.value.leaveTypes)
  const expenseCategories = computed(() => bundle.value.expenseCategories)
  const travelCityTiers = computed(() => bundle.value.travelCityTiers)
  const travelEmployeeRanks = computed(() => bundle.value.travelEmployeeRanks)
  const travelStandards = computed(() => bundle.value.travelStandards)
  const procurementCategories = computed(() => bundle.value.procurementCategories)
  const procurementAmountTiers = computed(() => bundle.value.procurementAmountTiers)
  const procurementQuoteThreshold = computed(() => bundle.value.procurementQuoteThreshold)
  const seals = computed(() => bundle.value.seals)
  const sealDocumentCategories = computed(() => bundle.value.sealDocumentCategories)
  const contractTypes = computed(() => bundle.value.contractTypes)
  const attachmentTypes = computed(() => bundle.value.attachmentTypes)
  const approvalCommentPresets = computed(() => bundle.value.approvalCommentPresets)
  const announcementTypes = computed(() => bundle.value.announcementTypes)

  async function load(force = false) {
    if (loaded.value && !force) return
    loading.value = true
    error.value = ''
    try {
      const data = await api.businessConfigurations.getEffectiveOptions()
      bundle.value = data
      loaded.value = true
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '加载业务有效配置失败。'
      // Keep default bundle intact as resilient fallback
    } finally {
      loading.value = false
    }
  }

  function clear() {
    bundle.value = createDefaultBundle()
    loaded.value = false
    loading.value = false
    error.value = ''
  }

  return {
    bundle,
    loaded,
    loading,
    error,
    leaveTypes,
    expenseCategories,
    travelCityTiers,
    travelEmployeeRanks,
    travelStandards,
    procurementCategories,
    procurementAmountTiers,
    procurementQuoteThreshold,
    seals,
    sealDocumentCategories,
    contractTypes,
    attachmentTypes,
    approvalCommentPresets,
    announcementTypes,
    load,
    clear
  }
})
