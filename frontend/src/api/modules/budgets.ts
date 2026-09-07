import type { HttpClient } from '../http'
import type {
  AdjustBudgetRequest,
  Budget,
  BudgetCheckRequest,
  BudgetCheckResult,
  BudgetQuery,
  BudgetStatusChangeRequest,
  BudgetTransaction,
  CreateBudgetRequest,
  PagedResponse
} from '../types'

export const createBudgetApi = ({ request, download }: HttpClient) => ({
  getBudgets: (params?: BudgetQuery) => {
    const searchParams = new URLSearchParams()
    if (params?.departmentId) searchParams.set('departmentId', params.departmentId)
    if (params?.year) searchParams.set('year', String(params.year))
    if (params?.month !== undefined && params?.month !== null) searchParams.set('month', String(params.month))
    if (params?.expenseCategory) searchParams.set('expenseCategory', params.expenseCategory)
    if (params?.projectId) searchParams.set('projectId', params.projectId)
    if (params?.status) searchParams.set('status', params.status)
    if (params?.page) searchParams.set('page', String(params.page))
    if (params?.pageSize) searchParams.set('pageSize', String(params.pageSize))
    const qs = searchParams.toString()
    if (params?.page !== undefined || params?.pageSize !== undefined) {
      return request<PagedResponse<Budget>>(`/budgets${qs ? `?${qs}` : ''}`)
    }
    return request<Budget[]>(`/budgets${qs ? `?${qs}` : ''}`)
  },
  getBudget: (id: string) => request<Budget>(`/budgets/${id}`),
  createBudget: (payload: CreateBudgetRequest) => request<Budget>('/budgets', {
    method: 'POST',
    body: JSON.stringify(payload)
  }),
  publishBudget: (id: string, payload?: BudgetStatusChangeRequest) => request<Budget>(`/budgets/${id}/publish`, {
    method: 'POST',
    body: JSON.stringify(payload ?? {})
  }),
  freezeBudget: (id: string, payload?: BudgetStatusChangeRequest) => request<Budget>(`/budgets/${id}/freeze`, {
    method: 'POST',
    body: JSON.stringify(payload ?? {})
  }),
  unfreezeBudget: (id: string, payload?: BudgetStatusChangeRequest) => request<Budget>(`/budgets/${id}/unfreeze`, {
    method: 'POST',
    body: JSON.stringify(payload ?? {})
  }),
  closeBudget: (id: string, payload?: BudgetStatusChangeRequest) => request<Budget>(`/budgets/${id}/close`, {
    method: 'POST',
    body: JSON.stringify(payload ?? {})
  }),
  adjustBudget: (id: string, payload: AdjustBudgetRequest) => request<Budget>(`/budgets/${id}/adjust`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  }),
  getBudgetTransactions: (id: string, page?: number, pageSize?: number) => {
    const searchParams = new URLSearchParams()
    if (page) searchParams.set('page', String(page))
    if (pageSize) searchParams.set('pageSize', String(pageSize))
    const qs = searchParams.toString()
    return request<PagedResponse<BudgetTransaction>>(`/budgets/${id}/transactions${qs ? `?${qs}` : ''}`)
  },
  checkBudget: (payload: BudgetCheckRequest) => request<BudgetCheckResult>('/budgets/check', {
    method: 'POST',
    body: JSON.stringify(payload)
  }),
  exportBudgetsCsv: (params?: { year?: number; departmentId?: string }) => {
    const searchParams = new URLSearchParams()
    if (params?.year) searchParams.set('year', String(params.year))
    if (params?.departmentId) searchParams.set('departmentId', params.departmentId)
    const qs = searchParams.toString()
    return download(`/finance/export/budgets${qs ? `?${qs}` : ''}`, 'budget_export.csv')
  }
})
