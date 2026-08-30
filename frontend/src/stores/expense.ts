import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { createExpenseApi } from '../api/modules/expense'
import { createHttpClient } from '../api/http'
import type { ExpenseClaim, ExpenseForm, ListFilters, PaymentInput } from '../api/types'
import { useAuthStore } from './auth'
import { blankLeaveFilters } from './leave'
import { PAGE_SIZE } from './pagination'
import { useUiStore } from './ui'

const blankExpenseForm = (): ExpenseForm => ({ category: '交通', amount: '', expenseDate: '', description: '', receiptNumber: '', attachments: [], copyRecipientIds: [], travelRequestId: '' })
const blankExpenseFilters = (): ListFilters => ({ ...blankLeaveFilters(), minAmount: '', maxAmount: '' })

export const useExpenseStore = defineStore('expense', () => {
  const auth = useAuthStore()
  const ui = useUiStore()
  const api = createExpenseApi(createHttpClient(() => auth.accessToken, auth.clearSession, auth.refreshAccessToken))
  const expenses = ref<ExpenseClaim[]>([])
  const initiatedExpenses = ref<ExpenseClaim[]>([])
  const expenseForm = ref<ExpenseForm>(blankExpenseForm())
  const showExpenseForm = ref(false)
  const expensePage = ref(1)
  const expenseTotal = ref(0)
  const expenseTotalPages = ref(1)
  const expenseFilters = ref<ListFilters>(blankExpenseFilters())
  const expenseDetail = ref<ExpenseClaim | null>(null)
  const pagedExpenses = computed(() => ({ items: expenses.value, currentPage: expensePage.value, totalPages: expenseTotalPages.value, total: expenseTotal.value }))

  async function loadExpenses() {
    try {
      const result = await api.getExpenses(expenseFilters.value, expensePage.value, PAGE_SIZE)
      expenses.value = result.items
      expensePage.value = result.page
      expenseTotal.value = result.total
      expenseTotalPages.value = result.totalPages
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '报销列表加载失败。'
    }
  }

  async function loadInitiated(userId: string) {
    initiatedExpenses.value = (await api.getExpenses({ ...blankExpenseFilters(), applicantId: userId }, 1, 5)).items
  }

  async function loadExpenseDetail(id: string) {
    expenseDetail.value = await api.getExpense(id)
  }

  async function submitExpense(payeeName: string) {
    const amount = Number(expenseForm.value.amount)
    if (!expenseForm.value.expenseDate || !expenseForm.value.description.trim() || !Number.isFinite(amount) || amount <= 0) {
      ui.error = '请填写费用日期、金额和费用说明。'
      return false
    }
    ui.submitting = true
    ui.error = ''
    try {
      const draft = await api.createExpense(expenseForm.value, payeeName)
      await api.submitExpense(draft.id)
      ui.message = '报销申请已提交，审批人已收到待办。'
      showExpenseForm.value = false
      expenseForm.value = blankExpenseForm()
      expensePage.value = 1
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '提交失败。'
      return false
    } finally {
      ui.submitting = false
    }
  }

  async function registerPayment(expense: ExpenseClaim, payment: PaymentInput) {
    ui.error = ''
    try {
      await api.registerPayment(expense.id, expense.totalAmount, payment)
      ui.message = '付款已登记，报销单已完成。'
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '付款登记失败。'
      return false
    }
  }

  async function withdrawExpense(expense: ExpenseClaim) {
    if (!window.confirm(`确认撤回报销单 ${expense.number} 吗？`)) return false
    try {
      await api.withdrawExpense(expense.id)
      ui.message = '报销申请已撤回。'
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '撤回报销申请失败。'
      return false
    }
  }

  function reset() {
    expenses.value = []
    initiatedExpenses.value = []
    expenseDetail.value = null
  }

  return { expenses, initiatedExpenses, expenseForm, showExpenseForm, expensePage, expenseTotal, expenseTotalPages, expenseFilters, expenseDetail, pagedExpenses, loadExpenses, loadInitiated, loadExpenseDetail, submitExpense, registerPayment, withdrawExpense, reset }
})
