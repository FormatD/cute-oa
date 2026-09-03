import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { ContractFilters } from '../api/modules/contracts'
import type { ContractAlertSummary, EmploymentContract, SaveEmploymentContract } from '../api/types'
import { PAGE_SIZE } from './pagination'

const blankFilters = (): ContractFilters => ({ keyword: '', departmentId: '', contractType: '', status: '', expiryDays: '' })
const blankSummary = (): ContractAlertSummary => ({ missingWrittenContract: 0, expired: 0, within7Days: 0, within30Days: 0, within60Days: 0, within90Days: 0, openEndedReviewRequired: 0, unacknowledged: 0, atRiskContracts: 0 })

export const useContractStore = defineStore('contracts', () => {
  const api = useApiClient().contracts
  const contracts = ref<EmploymentContract[]>([])
  const detail = ref<EmploymentContract | null>(null)
  const alertSummary = ref<ContractAlertSummary>(blankSummary())
  const filters = ref<ContractFilters>(blankFilters())
  const page = ref(1)
  const total = ref(0)
  const totalPages = ref(1)
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')

  async function loadContracts() {
    loading.value = true; error.value = ''
    try {
      const [result, summary] = await Promise.all([api.getContracts(filters.value, page.value, PAGE_SIZE), api.getAlertSummary()])
      contracts.value = result.items; page.value = result.page; total.value = result.total; totalPages.value = result.totalPages; alertSummary.value = summary
    } catch (cause) { contracts.value = []; error.value = cause instanceof Error ? cause.message : '劳动合同加载失败。' }
    finally { loading.value = false }
  }

  async function loadDetail(id: string) {
    loading.value = true; error.value = ''; message.value = ''; detail.value = null
    try { detail.value = await api.getContract(id) }
    catch (cause) { error.value = cause instanceof Error ? cause.message : '劳动合同详情加载失败。' }
    finally { loading.value = false }
  }

  async function create(payload: SaveEmploymentContract) { return mutate(async () => { detail.value = await api.createContract(payload); message.value = '合同草稿已创建。'; await loadContracts() }, '合同创建失败。') }
  async function update(id: string, payload: SaveEmploymentContract) { return mutate(async () => { detail.value = await api.updateContract(id, payload); message.value = '合同草稿已更新。' }, '合同更新失败。') }
  async function activate(id: string, version: number, reason: string) { return mutate(async () => { detail.value = await api.activateContract(id, version, reason); message.value = '合同已激活并归档。' }, '合同激活失败。') }
  async function renew(id: string, payload: SaveEmploymentContract) { return mutate(async () => { detail.value = await api.renewContract(id, payload); message.value = '合同已续签并生成新版本。' }, '合同续签失败。') }
  async function terminate(id: string, version: number, terminationDate: string, reason: string) { return mutate(async () => { detail.value = await api.terminateContract(id, version, terminationDate, reason); message.value = '合同终止信息已登记。' }, '合同终止失败。') }
  async function acknowledge(id: string, thresholdDays: number) { return mutate(async () => { await api.acknowledgeAlert(id, thresholdDays); await loadDetail(id); message.value = '到期预警已确认。' }, '预警确认失败。') }
  async function generateDemoData() { return mutate(async () => { const result = await api.generateDemoData(); message.value = `模拟合同已补齐：新增 ${result.created} 份，保留已有 ${result.skipped} 份。`; await loadContracts() }, '模拟合同生成失败。') }

  async function mutate(action: () => Promise<void>, fallback: string) {
    saving.value = true; error.value = ''; message.value = ''
    try { await action(); return true } catch (cause) { error.value = cause instanceof Error ? cause.message : fallback; return false } finally { saving.value = false }
  }
  function search() { page.value = 1; void loadContracts() }
  function resetFilters() { filters.value = blankFilters(); search() }
  function changePage(offset: number) { page.value += offset; void loadContracts() }
  function reset() { contracts.value = []; detail.value = null; alertSummary.value = blankSummary(); filters.value = blankFilters(); page.value = 1; total.value = 0; totalPages.value = 1; error.value = ''; message.value = '' }

  return { contracts, detail, alertSummary, filters, page, total, totalPages, loading, saving, error, message, loadContracts, loadDetail, create, update, activate, renew, terminate, acknowledge, generateDemoData, search, resetFilters, changePage, reset }
})
