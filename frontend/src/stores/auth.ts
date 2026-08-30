import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { createHttpClient } from '../api/http'
import { createAuthApi } from '../api/modules/auth'
import type { Employee, LoginSession } from '../api/types'

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(null)
  const expiresAt = ref<string | null>(null)
  const authReady = ref(false)
  const authLoading = ref(false)
  const authError = ref('')
  const demoAccounts = ref<Employee[]>([])
  const demoAccountsError = ref('')
  const sessionUser = ref<Employee | null>(null)
  const currentUserId = ref('')
  const authenticated = computed(() => Boolean(accessToken.value && sessionUser.value))
  const currentUser = computed(() => sessionUser.value ?? undefined)
  const api = createAuthApi(createHttpClient(() => accessToken.value, clearSession))
  let refreshPromise: Promise<boolean> | null = null
  let refreshTimer: ReturnType<typeof setTimeout> | null = null

  function clearSession() {
    if (refreshTimer) clearTimeout(refreshTimer)
    refreshTimer = null
    accessToken.value = null
    expiresAt.value = null
    sessionUser.value = null
    currentUserId.value = ''
    localStorage.removeItem('oa_access_token')
    localStorage.removeItem('oa_user_id')
  }

  function applySession(session: LoginSession) {
    accessToken.value = session.accessToken
    expiresAt.value = session.expiresAt
    sessionUser.value = session.user
    currentUserId.value = session.user.id
    if (refreshTimer) clearTimeout(refreshTimer)
    const delay = Math.max(5_000, new Date(session.expiresAt).getTime() - Date.now() - 60_000)
    refreshTimer = setTimeout(() => { void refreshAccessToken() }, delay)
  }

  async function refreshAccessToken() {
    if (refreshPromise) return refreshPromise
    refreshPromise = (async () => {
      try {
        applySession(await api.refresh())
        return true
      } catch {
        clearSession()
        return false
      } finally {
        refreshPromise = null
      }
    })()
    return refreshPromise
  }

  async function initializeAuth() {
    if (authReady.value) return authenticated.value
    await refreshAccessToken()
    authReady.value = true
    return authenticated.value
  }

  async function login(userId: string, password: string) {
    authLoading.value = true
    authError.value = ''
    try {
      applySession(await api.login(userId, password))
      return true
    } catch (cause) {
      clearSession()
      authError.value = cause instanceof Error ? cause.message : '登录失败，请稍后重试。'
      return false
    } finally {
      authReady.value = true
      authLoading.value = false
    }
  }

  async function logout() {
    try {
      await api.logout()
    } finally {
      clearSession()
    }
  }

  async function loadDemoAccounts() {
    demoAccountsError.value = ''
    try {
      demoAccounts.value = await api.getDemoAccounts()
    } catch {
      demoAccounts.value = []
      demoAccountsError.value = '无法读取演示账号，请确认后端服务已启动。'
    }
  }

  return { accessToken, expiresAt, authReady, authLoading, authError, demoAccounts, demoAccountsError, sessionUser, currentUserId, authenticated, currentUser, clearSession, refreshAccessToken, initializeAuth, login, logout, loadDemoAccounts }
})
