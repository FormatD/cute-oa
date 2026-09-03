import type { HttpClient } from '../http'
import { toFilterQuery, toQuery } from '../http'
import type { AdjustLeaveBalance, CreatePersonnelCase, LeaveBalance, LeaveBalanceType, PagedResponse, PersonnelCase, UpdatePersonnelCaseTask, PersonnelProfile, UpdatePersonnelProfile } from '../types'

export type PersonnelFilters = { keyword: string; departmentId: string; personnelStatus: string; employmentType: string }
export type PersonnelCaseFilters = { keyword: string; userId: string; type: string; status: string; assignedToMe: boolean }

export const createPersonnelApi = ({ request, download }: HttpClient) => ({
  getEmployees: (filters: PersonnelFilters, page: number, pageSize: number) => request<PagedResponse<PersonnelProfile>>(`/hr/employees${toQuery(filters, page, pageSize)}`),
  exportEmployees: (filters: PersonnelFilters) => download(`/hr/employees/export${toFilterQuery(filters)}`, 'employee-roster.csv'),
  getMe: () => request<PersonnelProfile>('/hr/employees/me'),
  getEmployee: (id: string) => request<PersonnelProfile>(`/hr/employees/${encodeURIComponent(id)}`),
  updateEmployee: (id: string, payload: UpdatePersonnelProfile) => request<PersonnelProfile>(`/hr/employees/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) }),
  getLeaveBalance: (id: string, type: LeaveBalanceType, year: number) => request<LeaveBalance>(`/hr/leave-balances/${encodeURIComponent(id)}?type=${type}&year=${year}`),
  adjustLeaveBalance: (id: string, payload: AdjustLeaveBalance) => request<LeaveBalance>(`/hr/leave-balances/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) }),
  getCases: (filters: PersonnelCaseFilters, page: number, pageSize: number) => request<PagedResponse<PersonnelCase>>(`/hr/personnel-cases${toQuery(filters, page, pageSize)}`),
  getCase: (id: string) => request<PersonnelCase>(`/hr/personnel-cases/${encodeURIComponent(id)}`),
  createCase: (payload: CreatePersonnelCase) => request<PersonnelCase>('/hr/personnel-cases', { method: 'POST', body: JSON.stringify(payload) }),
  updateCaseTask: (caseId: string, taskId: string, payload: UpdatePersonnelCaseTask) => request<PersonnelCase>(`/hr/personnel-cases/${encodeURIComponent(caseId)}/tasks/${encodeURIComponent(taskId)}`, { method: 'PUT', body: JSON.stringify(payload) }),
  completeCase: (id: string, version: number, comment: string) => request<PersonnelCase>(`/hr/personnel-cases/${encodeURIComponent(id)}/complete`, { method: 'POST', body: JSON.stringify({ version, comment }) }),
  cancelCase: (id: string, version: number, reason: string) => request<PersonnelCase>(`/hr/personnel-cases/${encodeURIComponent(id)}/cancel`, { method: 'POST', body: JSON.stringify({ version, reason }) })
})
