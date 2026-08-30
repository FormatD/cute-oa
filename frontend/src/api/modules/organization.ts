import type { HttpClient } from '../http'
import type { CreateDepartment, CreatePosition, Department, DirectoryEmployee, ManagedDepartment, ManagedPosition, PositionOption, UpdateDepartment, UpdatePosition } from '../types'

export const createOrganizationApi = ({ request }: HttpClient) => ({
  getDepartments: () => request<Department[]>('/org/departments'),
  getDirectoryEmployees: () => request<DirectoryEmployee[]>('/org/employees'),
  getPositions: () => request<PositionOption[]>('/org/positions'),
  getManagedDepartments: () => request<ManagedDepartment[]>('/admin/departments'),
  createDepartment: (payload: CreateDepartment) => request<ManagedDepartment>('/admin/departments', { method: 'POST', body: JSON.stringify(payload) }),
  updateDepartment: (id: string, payload: UpdateDepartment) => request<ManagedDepartment>(`/admin/departments/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteDepartment: (id: string, version: number) => request<ManagedDepartment>(`/admin/departments/${encodeURIComponent(id)}?version=${version}`, { method: 'DELETE' }),
  getManagedPositions: () => request<ManagedPosition[]>('/admin/positions'),
  createPosition: (payload: CreatePosition) => request<ManagedPosition>('/admin/positions', { method: 'POST', body: JSON.stringify(payload) }),
  updatePosition: (id: string, payload: UpdatePosition) => request<ManagedPosition>(`/admin/positions/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deletePosition: (id: string, version: number) => request<ManagedPosition>(`/admin/positions/${encodeURIComponent(id)}?version=${version}`, { method: 'DELETE' })
})
