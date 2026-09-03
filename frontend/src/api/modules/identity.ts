import type { HttpClient } from '../http'
import { toQuery } from '../http'
import type { CreateManagedUser, CreateSecurityRole, ManagedUser, PagedResponse, PermissionDefinition, SecurityRole, UpdateManagedUser, UpdateSecurityRole } from '../types'

export const createIdentityApi = ({ request }: HttpClient) => ({
  getRoles: () => request<SecurityRole[]>('/admin/roles'),
  getPermissions: () => request<PermissionDefinition[]>('/admin/permissions'),
  createRole: (payload: CreateSecurityRole) => request<SecurityRole>('/admin/roles', { method: 'POST', body: JSON.stringify(payload) }),
  updateRole: (payload: UpdateSecurityRole) => request<SecurityRole>('/admin/roles', { method: 'PUT', body: JSON.stringify(payload) }),
  deleteRole: (code: string) => request<SecurityRole>(`/admin/roles?code=${encodeURIComponent(code)}`, { method: 'DELETE' }),
  getUsers: (filters: { keyword: string; status: string; departmentId: string }, page: number, pageSize: number) => request<PagedResponse<ManagedUser>>(`/admin/users${toQuery(filters, page, pageSize)}`),
  createUser: (payload: CreateManagedUser) => request<ManagedUser>('/admin/users', { method: 'POST', body: JSON.stringify(payload) }),
  updateUser: (id: string, payload: UpdateManagedUser) => request<ManagedUser>(`/admin/users/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) }),
  resetPassword: (id: string, password: string) => request<ManagedUser>(`/admin/users/${encodeURIComponent(id)}/reset-password`, { method: 'POST', body: JSON.stringify({ password }) }),
  resetMfa: (id: string) => request<ManagedUser>(`/admin/users/${encodeURIComponent(id)}/reset-mfa`, { method: 'POST' })
})
