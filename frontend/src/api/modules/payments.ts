import type { HttpClient } from '../http'
import type {
  CreatePaymentTransactionRequest,
  ExpenseInvoiceListItem,
  PagedResponse,
  PaymentTransactionListItem,
  PurchaseReconciliation,
  PurchaseReconciliationItem,
  ValidateInvoiceResult
} from '../types'

export const createPaymentApi = ({ request, download }: HttpClient) => ({
  registerExpensePayment: (id: string, payload: CreatePaymentTransactionRequest) =>
    request<PaymentTransactionListItem>(`/expense-claims/${id}/payments`, {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  registerPurchasePayment: (id: string, payload: CreatePaymentTransactionRequest) =>
    request<PaymentTransactionListItem>(`/purchase-requests/${id}/payments`, {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  getPayments: (businessType: string, businessId: string) =>
    request<PaymentTransactionListItem[]>(`/payments/${businessType}/${businessId}`),
  getPurchaseReconciliation: (purchaseRequestId: string) =>
    request<PurchaseReconciliation>(`/purchase-requests/${purchaseRequestId}/reconciliation`),
  validateInvoice: (payload: { invoiceType: string; invoiceCode?: string | null; invoiceNumber: string; currentExpenseClaimId?: string | null }) =>
    request<ValidateInvoiceResult>('/expense-claims/invoices/validate', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  getFinanceInvoices: (params?: { keyword?: string; type?: string; startDate?: string; endDate?: string; departmentId?: string; page?: number; pageSize?: number }) => {
    const searchParams = new URLSearchParams()
    if (params?.keyword) searchParams.set('keyword', params.keyword)
    if (params?.type) searchParams.set('type', params.type)
    if (params?.startDate) searchParams.set('startDate', params.startDate)
    if (params?.endDate) searchParams.set('endDate', params.endDate)
    if (params?.departmentId) searchParams.set('departmentId', params.departmentId)
    if (params?.page) searchParams.set('page', String(params.page))
    if (params?.pageSize) searchParams.set('pageSize', String(params.pageSize))
    const qs = searchParams.toString()
    return request<PagedResponse<ExpenseInvoiceListItem>>(`/finance/invoices${qs ? `?${qs}` : ''}`)
  },
  getFinancePayments: (params?: { keyword?: string; businessType?: string; startDate?: string; endDate?: string; departmentId?: string; page?: number; pageSize?: number }) => {
    const searchParams = new URLSearchParams()
    if (params?.keyword) searchParams.set('keyword', params.keyword)
    if (params?.businessType) searchParams.set('businessType', params.businessType)
    if (params?.startDate) searchParams.set('startDate', params.startDate)
    if (params?.endDate) searchParams.set('endDate', params.endDate)
    if (params?.departmentId) searchParams.set('departmentId', params.departmentId)
    if (params?.page) searchParams.set('page', String(params.page))
    if (params?.pageSize) searchParams.set('pageSize', String(params.pageSize))
    const qs = searchParams.toString()
    return request<PagedResponse<PaymentTransactionListItem>>(`/finance/payments${qs ? `?${qs}` : ''}`)
  },
  getFinanceReconciliations: (params?: { keyword?: string; startDate?: string; endDate?: string; departmentId?: string; status?: string; page?: number; pageSize?: number }) => {
    const searchParams = new URLSearchParams()
    if (params?.keyword) searchParams.set('keyword', params.keyword)
    if (params?.startDate) searchParams.set('startDate', params.startDate)
    if (params?.endDate) searchParams.set('endDate', params.endDate)
    if (params?.departmentId) searchParams.set('departmentId', params.departmentId)
    if (params?.status) searchParams.set('status', params.status)
    if (params?.page) searchParams.set('page', String(params.page))
    if (params?.pageSize) searchParams.set('pageSize', String(params.pageSize))
    const qs = searchParams.toString()
    return request<PagedResponse<PurchaseReconciliationItem>>(`/finance/reconciliations${qs ? `?${qs}` : ''}`)
  },
  getExpenseInvoices: (expenseClaimId: string) =>
    request<ExpenseInvoiceListItem[]>(`/expense-claims/${expenseClaimId}/invoices`),
  exportExpensesCsv: (params?: { applicantId?: string; departmentId?: string; startDate?: string; endDate?: string; paymentStatus?: string }) => {
    const searchParams = new URLSearchParams()
    if (params?.applicantId) searchParams.set('applicantId', params.applicantId)
    if (params?.departmentId) searchParams.set('departmentId', params.departmentId)
    if (params?.startDate) searchParams.set('startDate', params.startDate)
    if (params?.endDate) searchParams.set('endDate', params.endDate)
    if (params?.paymentStatus) searchParams.set('paymentStatus', params.paymentStatus)
    const qs = searchParams.toString()
    return download(`/finance/export/expenses${qs ? `?${qs}` : ''}`, 'expense_reconciliation.csv')
  },
  exportPurchasesCsv: (params?: { applicantId?: string; departmentId?: string; startDate?: string; endDate?: string; paymentStatus?: string }) => {
    const searchParams = new URLSearchParams()
    if (params?.applicantId) searchParams.set('applicantId', params.applicantId)
    if (params?.departmentId) searchParams.set('departmentId', params.departmentId)
    if (params?.startDate) searchParams.set('startDate', params.startDate)
    if (params?.endDate) searchParams.set('endDate', params.endDate)
    if (params?.paymentStatus) searchParams.set('paymentStatus', params.paymentStatus)
    const qs = searchParams.toString()
    return download(`/finance/export/purchases${qs ? `?${qs}` : ''}`, 'purchase_reconciliation.csv')
  }
})
