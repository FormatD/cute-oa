import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { Summary } from '../api/types'
import { useAuthStore } from './auth'

export const useDashboardStore = defineStore('dashboard', () => {
  const auth = useAuthStore()
  const api = useApiClient().system
  const summary = ref<Summary | null>(null)

  async function loadSummary() {
    summary.value = await api.getSummary()
    auth.sessionUser = summary.value.currentUser
    auth.currentUserId = summary.value.currentUser.id
  }

  function decrementPendingReadCount() {
    if (summary.value?.pendingReadCount) summary.value.pendingReadCount--
  }

  function reset() {
    summary.value = null
  }

  return { summary, loadSummary, decrementPendingReadCount, reset }
})
