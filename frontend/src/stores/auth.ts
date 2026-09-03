import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { createHttpClient } from '../api/http'
import { createAuthApi } from '../api/modules/auth'
import type { Employee, LoginChallengeState, LoginSession, MfaSetup } from '../api/types'

export type LoginProgress = 'AUTHENTICATED' | LoginChallengeState

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
  const mfaState = ref<LoginChallengeState | ''>('')
  const mfaChallengeToken = ref('')
  const mfaChallengeExpiresAt = ref('')
  const mfaSetup = ref<MfaSetup | null>(null)
  const recoveryCodes = ref<string[]>([])
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
    clearMfaChallenge()
    recoveryCodes.value = []
    localStorage.removeItem('oa_access_token')
    localStorage.removeItem('oa_user_id')
  }

  function clearMfaChallenge() {
    mfaState.value = ''
    mfaChallengeToken.value = ''
    mfaChallengeExpiresAt.value = ''
    mfaSetup.value = null
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
      const result = await api.login(userId, password)
      if (result.status === 'AUTHENTICATED') {
        applySession(result.session)
        clearMfaChallenge()
        return 'AUTHENTICATED' as const
      }
      clearSession()
      mfaState.value = result.status
      mfaChallengeToken.value = result.challengeToken
      mfaChallengeExpiresAt.value = result.challengeExpiresAt
      if (result.status === 'MFA_SETUP_REQUIRED') mfaSetup.value = await api.startMfaSetup(result.challengeToken)
      return result.status
    } catch (cause) {
      clearSession()
      authError.value = cause instanceof Error ? cause.message : '登录失败，请稍后重试。'
      return false as const
    } finally {
      authReady.value = true
      authLoading.value = false
    }
  }

  async function verifyMfa(code: string) {
    return completeMfa(async () => {
      const result = await api.verifyMfa(mfaChallengeToken.value, code.trim())
      if (result.status !== 'AUTHENTICATED') throw new Error('多因素认证响应无效。')
      applySession(result.session)
      clearMfaChallenge()
      return true
    })
  }

  async function changeInitialPassword(newPassword: string) {
    authLoading.value = true
    authError.value = ''
    try {
      const result = await api.changeInitialPassword(mfaChallengeToken.value, newPassword)
      if (result.status === 'AUTHENTICATED') {
        applySession(result.session)
        clearMfaChallenge()
        return 'AUTHENTICATED' as const
      }
      mfaState.value = result.status
      mfaChallengeToken.value = result.challengeToken
      mfaChallengeExpiresAt.value = result.challengeExpiresAt
      mfaSetup.value = result.status === 'MFA_SETUP_REQUIRED' ? await api.startMfaSetup(result.challengeToken) : null
      return result.status
    } catch (cause) {
      authError.value = cause instanceof Error ? cause.message : '修改密码失败，请重新登录后再试。'
      return false as const
    } finally {
      authLoading.value = false
      authReady.value = true
    }
  }

  async function confirmMfaSetup(code: string) {
    return completeMfa(async () => {
      const result = await api.confirmMfaSetup(mfaChallengeToken.value, code.trim())
      applySession(result.session)
      recoveryCodes.value = result.recoveryCodes
      clearMfaChallenge()
      return true
    })
  }

  async function completeMfa(action: () => Promise<boolean>) {
    authLoading.value = true
    authError.value = ''
    try {
      return await action()
    } catch (cause) {
      authError.value = cause instanceof Error ? cause.message : '多因素认证失败，请重新登录。'
      return false
    } finally {
      authLoading.value = false
      authReady.value = true
    }
  }

  function acknowledgeRecoveryCodes() { recoveryCodes.value = [] }

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

  return { accessToken, expiresAt, authReady, authLoading, authError, demoAccounts, demoAccountsError, sessionUser, currentUserId, authenticated, currentUser, mfaState, mfaChallengeToken, mfaChallengeExpiresAt, mfaSetup, recoveryCodes, clearSession, clearMfaChallenge, refreshAccessToken, initializeAuth, login, changeInitialPassword, verifyMfa, confirmMfaSetup, acknowledgeRecoveryCodes, logout, loadDemoAccounts }
})
