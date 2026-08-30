import type { HttpClient } from '../http'
import type { AuthSession, Employee, LoginSession, PagedResponse } from '../types'

export const createAuthApi = ({ request }: HttpClient) => ({
  login: (userId: string, password: string) => request<LoginSession>('/auth/login', { method: 'POST', body: JSON.stringify({ userId, password }) }, false),
  refresh: () => request<LoginSession>('/auth/refresh', { method: 'POST' }, false),
  logout: () => request<void>('/auth/logout', { method: 'POST' }, false),
  getSessions: (page: number, pageSize: number) => request<PagedResponse<AuthSession>>(`/auth/sessions?page=${page}&pageSize=${pageSize}`),
  revokeSession: (id: string) => request<boolean>(`/auth/sessions/${id}/revoke`, { method: 'POST' }),
  revokeOtherSessions: () => request<number>('/auth/sessions/revoke-others', { method: 'POST' }),
  getDemoAccounts: () => request<Employee[]>('/auth/demo-accounts', undefined, false),
  getDemoUsers: () => request<Employee[]>('/auth/demo-users'),
  getMe: () => request<Employee>('/me')
})
