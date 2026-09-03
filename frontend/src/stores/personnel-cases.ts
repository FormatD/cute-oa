import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { PersonnelCaseFilters } from '../api/modules/personnel'
import type { CreatePersonnelCase, PersonnelCase, UpdatePersonnelCaseTask } from '../api/types'
import { PAGE_SIZE } from './pagination'

const blankFilters = (): PersonnelCaseFilters => ({ keyword: '', userId: '', type: '', status: '', assignedToMe: false })

export const usePersonnelCaseStore = defineStore('personnel-cases', () => {
  const api = useApiClient().personnel
  const cases = ref<PersonnelCase[]>([])
  const detail = ref<PersonnelCase | null>(null)
  const filters = ref<PersonnelCaseFilters>(blankFilters())
  const page = ref(1)
  const total = ref(0)
  const totalPages = ref(1)
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')

  async function loadCases() {
    loading.value = true; error.value = ''
    try {
      const result = await api.getCases(filters.value, page.value, PAGE_SIZE)
      cases.value = result.items; page.value = result.page; total.value = result.total; totalPages.value = result.totalPages
    } catch (cause) { cases.value = []; error.value = cause instanceof Error ? cause.message : '员工办理清单加载失败。' }
    finally { loading.value = false }
  }

  async function loadDetail(id: string) {
    loading.value = true; error.value = ''; message.value = ''; detail.value = null
    try { detail.value = await api.getCase(id) }
    catch (cause) { error.value = cause instanceof Error ? cause.message : '员工办理单详情加载失败。' }
    finally { loading.value = false }
  }

  async function create(payload: CreatePersonnelCase) { return mutate(async () => { detail.value = await api.createCase(payload); message.value = '员工办理单已创建并分派任务。'; await loadCases() }, '员工办理单创建失败。') }
  async function updateTask(caseId: string, taskId: string, payload: UpdatePersonnelCaseTask) { return mutate(async () => { detail.value = await api.updateCaseTask(caseId, taskId, payload); message.value = payload.status === 'PENDING' ? '任务已重新打开。' : payload.status === 'WAIVED' ? '任务已豁免并记录原因。' : '任务已完成。' }, '办理任务更新失败。') }
  async function complete(id: string, version: number, comment: string) { return mutate(async () => { detail.value = await api.completeCase(id, version, comment); message.value = '员工办理单已办结。' }, '办理单办结失败。') }
  async function cancel(id: string, version: number, reason: string) { return mutate(async () => { detail.value = await api.cancelCase(id, version, reason); message.value = '员工办理单已取消并保留记录。' }, '办理单取消失败。') }

  async function mutate(action: () => Promise<void>, fallback: string) {
    saving.value = true; error.value = ''; message.value = ''
    try { await action(); return true } catch (cause) { error.value = cause instanceof Error ? cause.message : fallback; return false } finally { saving.value = false }
  }
  function search() { page.value = 1; void loadCases() }
  function resetFilters() { filters.value = blankFilters(); search() }
  function changePage(offset: number) { page.value += offset; void loadCases() }
  function reset() { cases.value = []; detail.value = null; filters.value = blankFilters(); page.value = 1; total.value = 0; totalPages.value = 1; error.value = ''; message.value = '' }

  return { cases, detail, filters, page, total, totalPages, loading, saving, error, message, loadCases, loadDetail, create, updateTask, complete, cancel, search, resetFilters, changePage, reset }
})
