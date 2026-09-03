import type { HttpClient } from '../http'
import type { ListFilters, PagedResponse, SealExecutionInput, SealForm, SealRequest, SealReturnInput } from '../types'
import { toQuery } from '../http'

export const createSealApi = ({ request }: HttpClient) => ({
  getSeals: (filters: ListFilters, page: number, pageSize: number) => request<PagedResponse<SealRequest>>(`/seal-requests${toQuery(filters, page, pageSize)}`),
  getSeal: (id: string) => request<SealRequest>(`/seal-requests/${id}`),
  createSeal: (form: SealForm) => request<SealRequest>('/seal-requests', {
    method: 'POST',
    body: JSON.stringify({
      ...form,
      copies: Number(form.copies),
      outStartDate: form.isOut ? form.outStartDate : null,
      outEndDate: form.isOut ? form.outEndDate : null,
      outCustodian: form.isOut ? form.outCustodian : null
    })
  }),
  submitSeal: (id: string) => request<SealRequest>(`/seal-requests/${id}/submit`, { method: 'POST', body: '{}' }),
  withdrawSeal: (id: string) => request<SealRequest>(`/seal-requests/${id}/withdraw`, { method: 'POST', body: '{}' }),
  registerExecution: (id: string, input: SealExecutionInput) => request<SealRequest>(`/seal-requests/${id}/execution`, { method: 'POST', body: JSON.stringify(input) }),
  registerReturn: (id: string, input: SealReturnInput) => request<SealRequest>(`/seal-requests/${id}/return`, { method: 'POST', body: JSON.stringify(input) }),
  generateDemoData: () => request<{ created: number; skipped: number }>('/seal-requests/demo-data', { method: 'POST', body: '{}' })
})
