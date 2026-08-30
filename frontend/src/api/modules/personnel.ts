import type { HttpClient } from '../http'
import { toQuery } from '../http'
import type { PagedResponse, PersonnelProfile, UpdatePersonnelProfile } from '../types'

export type PersonnelFilters = { keyword: string; departmentId: string; personnelStatus: string; employmentType: string }

export const createPersonnelApi = ({ request }: HttpClient) => ({
  getEmployees: (filters: PersonnelFilters, page: number, pageSize: number) => request<PagedResponse<PersonnelProfile>>(`/hr/employees${toQuery(filters, page, pageSize)}`),
  getMe: () => request<PersonnelProfile>('/hr/employees/me'),
  getEmployee: (id: string) => request<PersonnelProfile>(`/hr/employees/${encodeURIComponent(id)}`),
  updateEmployee: (id: string, payload: UpdatePersonnelProfile) => request<PersonnelProfile>(`/hr/employees/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) })
})
