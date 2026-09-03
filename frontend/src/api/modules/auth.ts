import type { HttpClient } from '../http'
import type { AuthSession, Employee, LoginResult, LoginSession, MfaEnrollmentResult, MfaSetup, MfaStatus, PagedResponse } from '../types'

export const createAuthApi = ({ request }: HttpClient) => ({
  login: (userId: string, password: string) => request<LoginResult>('/auth/login', { method: 'POST', body: JSON.stringify({ userId, password }) }, false),
  changeInitialPassword: (challengeToken: string, newPassword: string) => request<LoginResult>('/auth/password/change-initial', { method: 'POST', body: JSON.stringify({ challengeToken, newPassword }) }, false),
  startMfaSetup: (challengeToken: string) => request<MfaSetup>('/auth/mfa/setup/start', { method: 'POST', body: JSON.stringify({ challengeToken }) }, false),
  confirmMfaSetup: (challengeToken: string, code: string) => request<MfaEnrollmentResult>('/auth/mfa/setup/confirm', { method: 'POST', body: JSON.stringify({ challengeToken, code }) }, false),
  verifyMfa: (challengeToken: string, code: string) => request<LoginResult>('/auth/mfa/verify', { method: 'POST', body: JSON.stringify({ challengeToken, code }) }, false),
  getMfaStatus: () => request<MfaStatus>('/auth/mfa/status'),
  refresh: () => request<LoginSession>('/auth/refresh', { method: 'POST' }, false),
  logout: () => request<void>('/auth/logout', { method: 'POST' }, false),
  getSessions: (page: number, pageSize: number) => request<PagedResponse<AuthSession>>(`/auth/sessions?page=${page}&pageSize=${pageSize}`),
  revokeSession: (id: string) => request<boolean>(`/auth/sessions/${id}/revoke`, { method: 'POST' }),
  revokeOtherSessions: () => request<number>('/auth/sessions/revoke-others', { method: 'POST' }),
  getDemoAccounts: () => request<Employee[]>('/auth/demo-accounts', undefined, false),
  getDemoUsers: () => request<Employee[]>('/auth/demo-users'),
  getMe: () => request<Employee>('/me')
})
