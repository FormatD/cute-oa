import type { HttpClient } from '../http'
import { toQuery } from '../http'
import type { AttendanceAppeal, AttendanceImportItem, AttendanceImportResult, AttendanceMonthLock, AttendanceMonthlySummary, AttendanceRecord, AttendanceShift, PagedResponse, SaveAttendanceShift } from '../types'

export type AttendanceFilters = { keyword: string; userId: string; departmentId: string; status: string; month: string }

export const createAttendanceApi = ({ request }: HttpClient) => ({
  getShifts: () => request<AttendanceShift[]>('/attendance/shifts'),
  updateShift: (id: string, payload: SaveAttendanceShift) => request<AttendanceShift>(`/attendance/shifts/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  getRecords: (filters: AttendanceFilters, page: number, pageSize: number) => request<PagedResponse<AttendanceRecord>>(`/attendance/records${toQuery(filters, page, pageSize)}`),
  getRecord: (id: string) => request<AttendanceRecord>(`/attendance/records/${id}`),
  importRecords: (items: AttendanceImportItem[]) => request<AttendanceImportResult>('/attendance/import', { method: 'POST', body: JSON.stringify({ items, source: 'MANUAL' }) }),
  generateDemoData: (month: string) => request<AttendanceImportResult>(`/attendance/demo-data/${month}`, { method: 'POST' }),
  getMonthlySummary: (month: string, userId = '') => request<AttendanceMonthlySummary[]>(`/attendance/monthly-summary?month=${encodeURIComponent(month)}${userId ? `&userId=${encodeURIComponent(userId)}` : ''}`),
  getMonthLock: (month: string) => request<AttendanceMonthLock>(`/attendance/month-lock?month=${encodeURIComponent(month)}`),
  lockMonth: (month: string, reason: string, version: number) => request<AttendanceMonthLock>(`/attendance/month-locks/${month}/lock`, { method: 'POST', body: JSON.stringify({ reason, version }) }),
  unlockMonth: (month: string, reason: string, version: number) => request<AttendanceMonthLock>(`/attendance/month-locks/${month}/unlock`, { method: 'POST', body: JSON.stringify({ reason, version }) }),
  submitAppeal: (recordId: string, reason: string, attachments: string[]) => request<AttendanceAppeal>(`/attendance/records/${recordId}/appeals`, { method: 'POST', body: JSON.stringify({ reason, attachments }) }),
  reviewAppeal: (appealId: string, approved: boolean, comment: string) => request<AttendanceAppeal>(`/attendance/appeals/${appealId}/review`, { method: 'POST', body: JSON.stringify({ approved, comment }) })
})
