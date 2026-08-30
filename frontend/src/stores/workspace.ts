import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { createHttpClient } from '../api/http'
import { createAuthApi } from '../api/modules/auth'
import { createSystemApi } from '../api/modules/system'
import type { Employee, Summary, WorkCalendarEntry } from '../api/types'
import { useAuthStore } from './auth'
import { paginate } from './pagination'

export const useWorkspaceStore = defineStore('workspace', () => {
  const auth = useAuthStore()
  const http = createHttpClient(() => auth.accessToken, auth.clearSession, auth.refreshAccessToken)
  const authApi = createAuthApi(http)
  const systemApi = createSystemApi(http)
  const employees = ref<Employee[]>([])
  const summary = ref<Summary | null>(null)
  const calendarEntries = ref<WorkCalendarEntry[]>([])
  const calendarYear = new Date().getFullYear()
  const calendarPage = ref(1)
  const pagedCalendarEntries = computed(() => paginate([...calendarEntries.value].sort((a, b) => b.date.localeCompare(a.date)), calendarPage.value))

  async function loadCommon() {
    const [users, dashboard] = await Promise.all([authApi.getDemoUsers(), systemApi.getSummary()])
    employees.value = users
    summary.value = dashboard
    auth.sessionUser = dashboard.currentUser
    auth.currentUserId = dashboard.currentUser.id
    calendarEntries.value = await systemApi.getWorkCalendar(calendarYear).catch(() => [])
  }

  function reset() {
    employees.value = []
    summary.value = null
    calendarEntries.value = []
  }

  return { employees, summary, calendarEntries, calendarYear, calendarPage, pagedCalendarEntries, loadCommon, reset }
})
