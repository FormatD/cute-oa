import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { AuthSession, MfaStatus } from '../api/types'

export const useSecurityStore = defineStore('security', () => {
  const api = useApiClient().auth
  const sessions = ref<AuthSession[]>([])
  const mfaStatus = ref<MfaStatus | null>(null)
  const page = ref(1)
  const pageSize = 10
  const total = ref(0)
  const totalPages = ref(1)
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')

  async function loadSessions() {
    loading.value = true
    error.value = ''
    try {
      const result = await api.getSessions(page.value, pageSize)
      sessions.value = result.items
      page.value = result.page
      total.value = result.total
      totalPages.value = result.totalPages
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '登录设备加载失败。'
    } finally {
      loading.value = false
    }
  }

  async function loadMfaStatus() {
    try { mfaStatus.value = await api.getMfaStatus() }
    catch (cause) { error.value = cause instanceof Error ? cause.message : '多因素认证状态加载失败。' }
  }

  async function revokeSession(id: string) {
    return execute(async () => { await api.revokeSession(id); message.value = '指定设备已退出登录。' })
  }

  async function revokeOthers() {
    return execute(async () => {
      const count = await api.revokeOtherSessions()
      message.value = count ? `已退出其他 ${count} 个登录设备。` : '没有需要退出的其他设备。'
    })
  }

  async function execute(action: () => Promise<void>) {
    saving.value = true
    error.value = ''
    message.value = ''
    try {
      await action()
      page.value = 1
      await loadSessions()
      return true
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '操作失败。'
      return false
    } finally {
      saving.value = false
    }
  }

  return { sessions, mfaStatus, page, pageSize, total, totalPages, loading, saving, error, message, loadSessions, loadMfaStatus, revokeSession, revokeOthers }
})
