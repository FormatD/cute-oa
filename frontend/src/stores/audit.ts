import { reactive, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { AuditLog } from '../api/types'
import { PAGE_SIZE } from './pagination'

const blankFilters = () => ({ keyword: '', actorId: '', resourceType: '', startDate: '', endDate: '' })

export const useAuditStore = defineStore('audit', () => {
  const api = useApiClient().system
  const filters = reactive(blankFilters())
  const items = ref<AuditLog[]>([])
  const page = ref(1)
  const total = ref(0)
  const totalPages = ref(1)
  const loading = ref(false)
  const error = ref('')

  async function load() {
    loading.value = true
    error.value = ''
    try {
      const result = await api.getAuditLogs(filters, page.value, PAGE_SIZE)
      items.value = result.items
      page.value = result.page
      total.value = result.total
      totalPages.value = result.totalPages
    } catch (cause) {
      items.value = []
      error.value = cause instanceof Error ? cause.message : '审计日志加载失败。'
    } finally {
      loading.value = false
    }
  }

  function search() {
    page.value = 1
    void load()
  }

  function resetFilters() {
    Object.assign(filters, blankFilters())
    search()
  }

  function changePage(offset: number) {
    page.value += offset
    void load()
  }

  return { filters, items, page, total, totalPages, loading, error, load, search, resetFilters, changePage }
})
