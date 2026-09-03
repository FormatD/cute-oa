import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { ListFilters, SealExecutionInput, SealForm, SealRequest, SealReturnInput } from '../api/types'
import { PAGE_SIZE } from './pagination'
import { useUiStore } from './ui'

const blankFilters = (): ListFilters => ({ keyword: '', status: '', applicantId: '', startDate: '', endDate: '' })
const today = () => new Date().toISOString().slice(0, 10)
const futureDate = (days: number) => { const date = new Date(); date.setDate(date.getDate() + days); return date.toISOString().slice(0, 10) }

export const blankSealForm = (): SealForm => ({
  title: '',
  documentCategory: '合同协议',
  documentName: '',
  sealType: '公章',
  copies: '1',
  isOut: false,
  outStartDate: today(),
  outEndDate: futureDate(3),
  outCustodian: '',
  reason: '',
  attachments: [],
  copyRecipientIds: []
})

export const blankSealExecution = (version = 1): SealExecutionInput => ({
  version,
  executedDate: today(),
  operatorName: '',
  notes: '',
  attachments: []
})

export const blankSealReturn = (version = 1): SealReturnInput => ({
  version,
  returnDate: today(),
  sealCondition: 'INTACT',
  receiverName: '',
  notes: '',
  attachments: []
})

export const useSealStore = defineStore('seal', () => {
  const ui = useUiStore()
  const api = useApiClient().seal
  const seals = ref<SealRequest[]>([])
  const initiatedSeals = ref<SealRequest[]>([])
  const sealDetail = ref<SealRequest | null>(null)
  const sealForm = ref<SealForm>(blankSealForm())
  const executionForm = ref<SealExecutionInput>(blankSealExecution())
  const returnForm = ref<SealReturnInput>(blankSealReturn())
  const showSealForm = ref(false)
  const filters = ref<ListFilters>(blankFilters())
  const page = ref(1)
  const total = ref(0)
  const totalPages = ref(1)
  const submitting = ref(false)
  const operationBusy = ref(false)

  const paged = computed(() => ({ items: seals.value, currentPage: page.value, totalPages: totalPages.value, total: total.value }))

  async function loadSeals() {
    const result = await api.getSeals(filters.value, page.value, PAGE_SIZE)
    seals.value = result.items
    page.value = result.page
    total.value = result.total
    totalPages.value = result.totalPages
  }

  async function loadInitiated(userId: string) {
    initiatedSeals.value = (await api.getSeals({ ...blankFilters(), applicantId: userId }, 1, 5)).items
  }

  async function loadSealDetail(id: string) {
    sealDetail.value = await api.getSeal(id)
    executionForm.value = blankSealExecution(sealDetail.value.version)
    returnForm.value = blankSealReturn(sealDetail.value.version)
  }

  function validateForm() {
    const form = sealForm.value
    if (!form.title.trim() || !form.documentCategory.trim() || !form.documentName.trim() || !form.sealType.trim() || !form.reason.trim()) {
      return '请填写用印主题、文件类别、文件名称、印章类型和事由。'
    }
    const copiesNum = Number(form.copies)
    if (!Number.isInteger(copiesNum) || copiesNum < 1 || copiesNum > 100) {
      return '用印份数应为 1–100 之间的整数。'
    }
    if (form.isOut) {
      if (!form.outStartDate || !form.outEndDate) return '外带借出必须指定预计借出日期与归还日期。'
      if (form.outEndDate < form.outStartDate) return '预计归还日期不能早于借出日期。'
      const start = new Date(form.outStartDate).getTime()
      const end = new Date(form.outEndDate).getTime()
      const diffDays = Math.round((end - start) / (1000 * 60 * 60 * 24))
      if (diffDays > 30) return '外带借用周期最长不得超过 30 天。'
      if (!form.outCustodian.trim()) return '外带保管人不能为空。'
    }
    return ''
  }

  async function submitSeal() {
    ui.error = ''
    const validation = validateForm()
    if (validation) { ui.error = validation; return false }
    submitting.value = true
    try {
      const draft = await api.createSeal(sealForm.value)
      await api.submitSeal(draft.id)
      ui.message = '用章申请已提交。'
      sealForm.value = blankSealForm()
      showSealForm.value = false
      await loadSeals()
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '提交用章申请失败。'
      return false
    } finally {
      submitting.value = false
    }
  }

  async function withdrawSeal(item: SealRequest) {
    operationBusy.value = true
    ui.error = ''
    try {
      await api.withdrawSeal(item.id)
      ui.message = '用章申请已撤回。'
      await Promise.all([loadSeals(), loadSealDetail(item.id)])
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '撤回用章申请失败。'
      return false
    } finally {
      operationBusy.value = false
    }
  }

  async function registerExecution(item: SealRequest) {
    if (!executionForm.value.executedDate || !executionForm.value.operatorName.trim()) {
      ui.error = '请填写执行日期与经办人姓名。'
      return false
    }
    operationBusy.value = true
    ui.error = ''
    try {
      sealDetail.value = await api.registerExecution(item.id, { ...executionForm.value, version: item.version })
      ui.message = item.isOut ? '外带借出已登记。' : '用印盖章已登记。'
      executionForm.value = blankSealExecution(sealDetail.value.version)
      await loadSeals()
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '登记用印/借出失败。'
      return false
    } finally {
      operationBusy.value = false
    }
  }

  async function registerReturn(item: SealRequest) {
    if (!returnForm.value.returnDate || !returnForm.value.receiverName.trim()) {
      ui.error = '请填写归还日期与接收人姓名。'
      return false
    }
    operationBusy.value = true
    ui.error = ''
    try {
      sealDetail.value = await api.registerReturn(item.id, { ...returnForm.value, version: item.version })
      ui.message = '外带归还已登记。'
      returnForm.value = blankSealReturn(sealDetail.value.version)
      await loadSeals()
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '登记外带归还失败。'
      return false
    } finally {
      operationBusy.value = false
    }
  }

  async function generateDemoData() {
    operationBusy.value = true
    ui.error = ''
    try {
      const result = await api.generateDemoData()
      ui.message = result.created ? '已生成用章演示草稿。' : '用章演示数据已存在，未重复生成。'
      await loadSeals()
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '生成用章演示数据失败。'
      return false
    } finally {
      operationBusy.value = false
    }
  }

  function reset() {
    seals.value = []
    initiatedSeals.value = []
    sealDetail.value = null
    sealForm.value = blankSealForm()
    executionForm.value = blankSealExecution()
    returnForm.value = blankSealReturn()
    showSealForm.value = false
    filters.value = blankFilters()
    page.value = 1
    total.value = 0
    totalPages.value = 1
  }

  return {
    seals,
    initiatedSeals,
    sealDetail,
    sealForm,
    executionForm,
    returnForm,
    showSealForm,
    filters,
    page,
    total,
    totalPages,
    submitting,
    operationBusy,
    paged,
    loadSeals,
    loadInitiated,
    loadSealDetail,
    submitSeal,
    withdrawSeal,
    registerExecution,
    registerReturn,
    generateDemoData,
    reset
  }
})
