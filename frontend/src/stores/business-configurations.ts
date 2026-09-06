import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type {
  BusinessConfigurationListItem,
  BusinessConfigurationRecord,
  ConfigurationVersionSummary,
  CreateBusinessConfigurationRequest,
  PublishBusinessConfigurationRequest,
  RetireBusinessConfigurationRequest,
  UpdateBusinessConfigurationRequest
} from '../api/types'
import { PAGE_SIZE } from './pagination'
import { useEffectiveConfigurationStore } from './effective-configurations'

export const useBusinessConfigurationStore = defineStore('businessConfigurations', () => {
  const api = useApiClient().businessConfigurations

  const items = ref<BusinessConfigurationListItem[]>([])
  const total = ref(0)
  const page = ref(1)
  const pageSize = ref(PAGE_SIZE)
  const totalPages = ref(1)

  const domainFilter = ref('')
  const statusFilter = ref('')
  const keyword = ref('')

  const detail = ref<BusinessConfigurationRecord | null>(null)
  const versions = ref<ConfigurationVersionSummary[]>([])

  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')

  async function loadList(targetPage = page.value) {
    loading.value = true
    error.value = ''
    try {
      const result = await api.list({
        domain: domainFilter.value || undefined,
        status: statusFilter.value || undefined,
        keyword: keyword.value.trim() || undefined,
        page: targetPage,
        pageSize: pageSize.value
      })
      items.value = result.items
      total.value = result.total
      page.value = result.page
      totalPages.value = result.totalPages
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '业务配置列表加载失败。'
    } finally {
      loading.value = false
    }
  }

  function search() {
    page.value = 1
    return loadList(1)
  }

  function resetFilters() {
    domainFilter.value = ''
    statusFilter.value = ''
    keyword.value = ''
    page.value = 1
    return loadList(1)
  }

  async function loadDetail(id: string) {
    loading.value = true
    error.value = ''
    try {
      detail.value = await api.get(id)
      return detail.value
    } catch (cause) {
      detail.value = null
      error.value = cause instanceof Error ? cause.message : '业务配置详情加载失败。'
      return null
    } finally {
      loading.value = false
    }
  }

  async function loadVersions(id: string) {
    error.value = ''
    try {
      versions.value = await api.listVersions(id)
      return versions.value
    } catch (cause) {
      versions.value = []
      error.value = cause instanceof Error ? cause.message : '配置版本历史加载失败。'
      return []
    }
  }

  async function execute<T>(action: () => Promise<T>, successMessage: string, refreshList = true): Promise<T | null> {
    saving.value = true
    error.value = ''
    message.value = ''
    try {
      const result = await action()
      message.value = successMessage
      if (refreshList) {
        await loadList(page.value)
      }
      return result
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '操作失败。'
      return null
    } finally {
      saving.value = false
    }
  }

  function createDraft(payload: CreateBusinessConfigurationRequest) {
    return execute(() => api.createDraft(payload), '已成功创建配置草稿。')
  }

  function updateDraft(id: string, payload: UpdateBusinessConfigurationRequest) {
    return execute(() => api.updateDraft(id, payload), '已成功更新配置草稿。')
  }

  function createNewVersion(id: string) {
    return execute(() => api.createNewVersion(id), '已成功基于当前版本创建新草稿。')
  }

  async function publish(id: string, payload?: PublishBusinessConfigurationRequest) {
    const result = await execute(() => api.publish(id, payload), '配置发布成功。')
    if (result) {
      const effectiveStore = useEffectiveConfigurationStore()
      effectiveStore.invalidate()
      await effectiveStore.load(true)
    }
    return result
  }

  async function retire(id: string, payload?: RetireBusinessConfigurationRequest) {
    const result = await execute(() => api.retire(id, payload), '配置已成功下线。')
    if (result) {
      const effectiveStore = useEffectiveConfigurationStore()
      effectiveStore.invalidate()
      await effectiveStore.load(true)
    }
    return result
  }

  function deleteConfiguration(id: string) {
    return execute(() => api.delete(id), '配置草稿已成功删除。')
  }

  return {
    items,
    total,
    page,
    pageSize,
    totalPages,
    domainFilter,
    statusFilter,
    keyword,
    detail,
    versions,
    loading,
    saving,
    error,
    message,
    loadList,
    search,
    resetFilters,
    loadDetail,
    loadVersions,
    createDraft,
    updateDraft,
    createNewVersion,
    publish,
    retire,
    deleteConfiguration
  }
})
