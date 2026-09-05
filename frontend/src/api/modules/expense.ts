import type { HttpClient } from '../http'
import { toQuery } from '../http'
import type { ExpenseClaim, ExpenseForm, ListFilters, PagedResponse, PaymentInput } from '../types'

export const createExpenseApi = ({ request }: HttpClient) => ({
  getExpenses: (filters: ListFilters, page: number, pageSize: number) => request<PagedResponse<ExpenseClaim>>(`/expense-claims${toQuery(filters, page, pageSize)}`),
  getExpense: (id: string) => request<ExpenseClaim>(`/expense-claims/${id}`),
  createExpense: (form: ExpenseForm, payeeName: string) => request<ExpenseClaim>('/expense-claims', {
    method: 'POST',
    body: JSON.stringify({
      payeeAccountName: payeeName,
      payeeAccount: '6222026000001234',
      bankName: '模拟银行',
      description: form.description,
      travelRequestId: form.travelRequestId || null,
      copyRecipientIds: form.copyRecipientIds,
      items: [{ expenseDate: form.expenseDate, category: form.category, amount: Number(form.amount), description: form.description, receiptNumber: form.receiptNumber || null, attachments: form.attachments }],
      invoices: form.invoices ?? []
    })
  }),
  submitExpense: (id: string) => request<ExpenseClaim>(`/expense-claims/${id}/submit`, { method: 'POST', body: '{}' }),
  withdrawExpense: (id: string) => request<ExpenseClaim>(`/expense-claims/${id}/withdraw`, { method: 'POST', body: '{}' }),
  registerPayment: (id: string, totalAmount: number, payload: PaymentInput) => request<ExpenseClaim>(`/expense-claims/${id}/payment`, { method: 'POST', body: JSON.stringify({ ...payload, paidAmount: totalAmount }) })
})
