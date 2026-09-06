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

export const createEmptyBundle = (): EffectiveBusinessConfigurationBundle => ({
  leaveTypes: [],
  expenseCategories: [],
  travelCityTiers: [],
  travelEmployeeRanks: [],
  travelStandards: [],
  procurementCategories: [],
  procurementAmountTiers: [],
  procurementQuoteThreshold: 0,
  seals: [],
  sealDocumentCategories: [],
  contractTypes: [],
  attachmentTypes: [],
  approvalCommentPresets: [],
  announcementTypes: []
})

export const useEffectiveConfigurationStore = defineStore('effectiveConfigurations', () => {
  const bundle = ref<EffectiveBusinessConfigurationBundle>(createEmptyBundle())
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
      error.value = ''
    } catch (cause) {
      bundle.value = createEmptyBundle()
      loaded.value = false
      error.value = cause instanceof Error ? cause.message : '加载业务有效配置失败。'
    } finally {
      loading.value = false
    }
  }

  function invalidate() {
    loaded.value = false
  }

  function clear() {
    bundle.value = createEmptyBundle()
    loaded.value = false
    loading.value = false
    error.value = ''
  }

  if (typeof window !== 'undefined') {
    window.addEventListener('focus', () => {
      load(true).catch(() => {})
    })
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') {
        load(true).catch(() => {})
      }
    })
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
    invalidate,
    clear
  }
})
