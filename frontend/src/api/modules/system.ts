import type { HttpClient } from '../http'
import { toQuery } from '../http'
import type { AuditFilters, AuditLog, CreateProcessDefinition, Notification, PagedResponse, ProcessDefinition, Summary, UpdateProcessDefinition, WorkCalendarEntry } from '../types'

export const createSystemApi = ({ request }: HttpClient) => ({
  getSummary: () => request<Summary>('/demo/summary'),
  getAuditLogs: (filters: AuditFilters, page: number, pageSize: number) => request<PagedResponse<AuditLog>>(`/audit-logs${toQuery(filters, page, pageSize)}`),
  getProcessDefinitions: () => request<ProcessDefinition[]>('/process/definitions'),
  createProcessDefinition: (payload: CreateProcessDefinition) => request<ProcessDefinition>('/process/definitions', { method: 'POST', body: JSON.stringify(payload) }),
  cloneProcessDefinition: (id: string) => request<ProcessDefinition>(`/process/definitions/${id}/clone`, { method: 'POST', body: '{}' }),
  updateProcessDefinition: (id: string, payload: UpdateProcessDefinition) => request<ProcessDefinition>(`/process/definitions/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  publishProcessDefinition: (id: string) => request<ProcessDefinition>(`/process/definitions/${id}/publish`, { method: 'POST', body: '{}' }),
  getNotifications: () => request<Notification[]>('/notifications/my'),
  markNotificationRead: (id: string) => request(`/notifications/${id}/read`, { method: 'POST', body: '{}' }),
  getWorkCalendar: (year: number) => request<WorkCalendarEntry[]>(`/work-calendar/${year}`)
})
