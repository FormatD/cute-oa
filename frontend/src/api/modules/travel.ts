import type { HttpClient } from '../http'
import type { ListFilters, PagedResponse, TravelForm, TravelRequest } from '../types'
import { toQuery } from '../http'

export const createTravelApi = ({ request }: HttpClient) => ({
  getTravels: (filters: ListFilters, page: number, pageSize: number) => request<PagedResponse<TravelRequest>>(`/travel-requests${toQuery(filters, page, pageSize)}`),
  getTravel: (id: string) => request<TravelRequest>(`/travel-requests/${id}`),
  createTravel: (form: TravelForm) => request<TravelRequest>('/travel-requests', { method: 'POST', body: JSON.stringify({ ...form, estimatedBudget: Number(form.estimatedBudget) }) }),
  submitTravel: (id: string) => request<TravelRequest>(`/travel-requests/${id}/submit`, { method: 'POST', body: '{}' }),
  withdrawTravel: (id: string) => request<TravelRequest>(`/travel-requests/${id}/withdraw`, { method: 'POST', body: '{}' })
})
