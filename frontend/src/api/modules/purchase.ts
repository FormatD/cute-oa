import type { HttpClient } from '../http'
import type { ListFilters, PagedResponse, PurchaseForm, PurchaseOrderInput, PurchaseReceiptInput, PurchaseRequest } from '../types'
import { toQuery } from '../http'

export const createPurchaseApi = ({ request }: HttpClient) => ({
  getPurchases: (filters: ListFilters, page: number, pageSize: number) => request<PagedResponse<PurchaseRequest>>(`/purchase-requests${toQuery(filters, page, pageSize)}`),
  getPurchase: (id: string) => request<PurchaseRequest>(`/purchase-requests/${id}`),
  createPurchase: (form: PurchaseForm) => request<PurchaseRequest>('/purchase-requests', {
    method: 'POST',
    body: JSON.stringify({
      ...form,
      items: form.items.map(item => ({ ...item, quantity: Number(item.quantity), estimatedUnitPrice: Number(item.estimatedUnitPrice) }))
    })
  }),
  submitPurchase: (id: string) => request<PurchaseRequest>(`/purchase-requests/${id}/submit`, { method: 'POST', body: '{}' }),
  withdrawPurchase: (id: string) => request<PurchaseRequest>(`/purchase-requests/${id}/withdraw`, { method: 'POST', body: '{}' }),
  registerOrder: (id: string, input: PurchaseOrderInput) => request<PurchaseRequest>(`/purchase-requests/${id}/order`, { method: 'POST', body: JSON.stringify({ ...input, actualAmount: Number(input.actualAmount) }) }),
  receivePurchase: (id: string, input: PurchaseReceiptInput) => request<PurchaseRequest>(`/purchase-requests/${id}/receipt`, { method: 'POST', body: JSON.stringify(input) }),
  generateDemoData: () => request<{ created: number; skipped: number }>('/purchase-requests/demo-data', { method: 'POST', body: '{}' })
})
