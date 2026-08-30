import { ref } from 'vue'
import { defineStore } from 'pinia'
import { createHttpClient } from '../api/http'
import { createPersonnelApi, type PersonnelFilters } from '../api/modules/personnel'
import type { PersonnelProfile, UpdatePersonnelProfile } from '../api/types'
import { useAuthStore } from './auth'
import { PAGE_SIZE } from './pagination'

const blankFilters = (): PersonnelFilters => ({ keyword: '', departmentId: '', personnelStatus: '', employmentType: '' })

export const usePersonnelStore = defineStore('personnel', () => {
  const auth = useAuthStore()
  const api = createPersonnelApi(createHttpClient(() => auth.accessToken, auth.clearSession, auth.refreshAccessToken))
  const employees = ref<PersonnelProfile[]>([])
  const detail = ref<PersonnelProfile | null>(null)
  const filters = ref<PersonnelFilters>(blankFilters())
  const page = ref(1)
  const total = ref(0)
  const totalPages = ref(1)
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')

  async function loadEmployees() {
    loading.value = true
    error.value = ''
    try {
      const result = await api.getEmployees(filters.value, page.value, PAGE_SIZE)
      employees.value = result.items
      page.value = result.page
      total.value = result.total
      totalPages.value = result.totalPages
    } catch (cause) {
      employees.value = []
      error.value = cause instanceof Error ? cause.message : '花名册加载失败。'
    } finally {
      loading.value = false
    }
  }

  async function loadDetail(userId: string) {
    loading.value = true
    error.value = ''
    detail.value = null
    try {
      detail.value = await api.getEmployee(userId)
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '人事档案加载失败。'
    } finally {
      loading.value = false
    }
  }

  async function update(userId: string, payload: UpdatePersonnelProfile) {
    saving.value = true
    error.value = ''
    message.value = ''
    try {
      detail.value = await api.updateEmployee(userId, payload)
      message.value = '人事档案已更新，变更历史已记录。'
      await loadEmployees()
      return true
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '人事档案保存失败。'
      return false
    } finally {
      saving.value = false
    }
  }

  function search() { page.value = 1; void loadEmployees() }
  function resetFilters() { filters.value = blankFilters(); search() }
  function changePage(offset: number) { page.value += offset; void loadEmployees() }
  function reset() { employees.value = []; detail.value = null; filters.value = blankFilters(); page.value = 1; total.value = 0; totalPages.value = 1 }

  return { employees, detail, filters, page, total, totalPages, loading, saving, error, message, loadEmployees, loadDetail, update, search, resetFilters, changePage, reset }
})
