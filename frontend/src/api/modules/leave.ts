import type { HttpClient } from '../http'
import { toQuery } from '../http'
import type { LeaveForm, LeaveRequest, ListFilters, PagedResponse } from '../types'

export const createLeaveApi = ({ request }: HttpClient) => ({
  getLeaves: (filters: ListFilters, page: number, pageSize: number) => request<PagedResponse<LeaveRequest>>(`/leave-requests${toQuery(filters, page, pageSize)}`),
  getLeave: (id: string) => request<LeaveRequest>(`/leave-requests/${id}`),
  createLeave: (form: LeaveForm) => request<LeaveRequest>('/leave-requests', { method: 'POST', body: JSON.stringify(form) }),
  submitLeave: (id: string) => request<LeaveRequest>(`/leave-requests/${id}/submit`, { method: 'POST', body: '{}' }),
  withdrawLeave: (id: string) => request<LeaveRequest>(`/leave-requests/${id}/withdraw`, { method: 'POST', body: '{}' })
})
