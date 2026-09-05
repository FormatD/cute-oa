import type { HttpClient } from '../http'
import type {
  AdjustBudgetRequest,
  Budget,
  BudgetCheckRequest,
  BudgetCheckResult,
  BudgetTransaction,
  CreateBudgetRequest
} from '../types'

export const createBudgetApi = ({ request }: HttpClient) => ({
  getBudgets: (params?: { departmentId?: string; year?: number; month?: number; expenseCategory?: string; projectId?: string }) => {
    const searchParams = new URLSearchParams()
    if (params?.departmentId) searchParams.set('departmentId', params.departmentId)
    if (params?.year) searchParams.set('year', String(params.year))
    if (params?.month !== undefined && params?.month !== null) searchParams.set('month', String(params.month))
    if (params?.expenseCategory) searchParams.set('expenseCategory', params.expenseCategory)
    if (params?.projectId) searchParams.set('projectId', params.projectId)
    const qs = searchParams.toString()
    return request<Budget[]>(`/budgets${qs ? `?${qs}` : ''}`)
  },
  getBudget: (id: string) => request<Budget>(`/budgets/${id}`),
  createBudget: (payload: CreateBudgetRequest) => request<Budget>('/budgets', {
    method: 'POST',
    body: JSON.stringify(payload)
  }),
  adjustBudget: (id: string, payload: AdjustBudgetRequest) => request<Budget>(`/budgets/${id}/adjust`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  }),
  getBudgetTransactions: (id: string) => request<BudgetTransaction[]>(`/budgets/${id}/transactions`),
  checkBudget: (payload: BudgetCheckRequest) => request<BudgetCheckResult>('/budgets/check', {
    method: 'POST',
    body: JSON.stringify(payload)
  })
})
