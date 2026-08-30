import type { HttpClient } from '../http'
import type { Announcement, PagedResponse, SaveAnnouncement } from '../types'

export const createAnnouncementApi = ({ request }: HttpClient) => ({
  getPublished: (page: number, pageSize: number) => request<PagedResponse<Announcement>>(`/announcements?page=${page}&pageSize=${pageSize}`),
  getOne: (id: string) => request<Announcement>(`/announcements/${encodeURIComponent(id)}`),
  markRead: (id: string) => request<Announcement>(`/announcements/${encodeURIComponent(id)}/read`, { method: 'POST' }),
  getAdmin: (status: string, page: number, pageSize: number) => request<PagedResponse<Announcement>>(`/admin/announcements?status=${encodeURIComponent(status)}&page=${page}&pageSize=${pageSize}`),
  create: (payload: SaveAnnouncement) => request<Announcement>('/admin/announcements', { method: 'POST', body: JSON.stringify(payload) }),
  update: (id: string, payload: SaveAnnouncement) => request<Announcement>(`/admin/announcements/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) }),
  publish: (id: string, version: number) => request<Announcement>(`/admin/announcements/${encodeURIComponent(id)}/publish?version=${version}`, { method: 'POST' }),
  withdraw: (id: string, version: number) => request<Announcement>(`/admin/announcements/${encodeURIComponent(id)}/withdraw?version=${version}`, { method: 'POST' })
})
