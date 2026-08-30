import { ref } from 'vue'
import { defineStore } from 'pinia'
import type { ExpenseClaim, FlowTask, LeaveRequest, PaymentInput, TravelRequest } from '../api/types'
import { useAnnouncementStore } from './announcements'
import { useAuthStore } from './auth'
import { useDetailStore } from './detail'
import { useExpenseStore } from './expense'
import { useLeaveStore } from './leave'
import { useOrganizationStore } from './organization'
import { usePersonnelStore } from './personnel'
import { useAttendanceStore } from './attendance'
import { useContractStore } from './contracts'
import { useTravelStore } from './travel'
import { useUiStore } from './ui'
import { useWorkflowStore } from './workflow'
import { useWorkspaceStore } from './workspace'

type TaskAction = 'approve' | 'reject'
type BusinessType = 'leave' | 'expense' | 'travel'

export const useAppStore = defineStore('app', () => {
  const auth = useAuthStore()
  const ui = useUiStore()
  const leave = useLeaveStore()
  const expense = useExpenseStore()
  const travel = useTravelStore()
  const workflow = useWorkflowStore()
  const organization = useOrganizationStore()
  const personnel = usePersonnelStore()
  const attendance = useAttendanceStore()
  const contracts = useContractStore()
  const workspace = useWorkspaceStore()
  const announcements = useAnnouncementStore()
  const detail = useDetailStore()
  const initialized = ref(false)

  async function initialize() {
    await auth.initializeAuth()
    if (!auth.authenticated || initialized.value) return
    initialized.value = true
    await loadData()
  }

  async function login(userId: string, password: string) {
    const signedIn = await auth.login(userId, password)
    if (!signedIn) return false
    initialized.value = true
    await loadData()
    return true
  }

  function resetWorkspace() {
    workspace.reset()
    leave.reset()
    expense.reset()
    travel.reset()
    workflow.reset()
    organization.reset()
    personnel.reset()
    attendance.reset()
    contracts.reset()
    announcements.reset()
    detail.reset()
    initialized.value = false
  }

  function handleUnauthorized() {
    auth.clearSession()
    resetWorkspace()
  }

  async function logout() {
    await auth.logout()
    resetWorkspace()
  }

  async function loadData() {
    ui.error = ''
    try {
      await workspace.loadCommon()
      await Promise.all([
        leave.loadLeaves(),
        leave.loadInitiated(auth.currentUserId),
        expense.loadExpenses(),
        expense.loadInitiated(auth.currentUserId),
        travel.loadTravels(),
        travel.loadInitiated(auth.currentUserId),
        travel.loadApproved(auth.currentUserId),
        workflow.loadTasks(),
        workflow.loadNotifications(),
        workflow.loadCopies()
      ])
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '无法连接 API，请先启动后端服务。'
    }
  }

  async function submitLeave() {
    if (await leave.submitLeave()) await loadData()
  }

  async function submitExpense() {
    if (await expense.submitExpense(auth.currentUser?.name ?? '')) await loadData()
  }

  async function submitTravel() {
    if (await travel.submitTravel()) await loadData()
  }

  async function processTask(task: FlowTask, action: TaskAction, businessType: BusinessType, comment: string) {
    const success = await workflow.processTask(task, action, businessType, comment)
    if (success) await loadData()
    return success
  }

  async function transferTask(task: FlowTask, businessType: BusinessType, assigneeId: string, comment: string) {
    const success = await workflow.transferTask(task, businessType, assigneeId, comment)
    if (success) await loadData()
    return success
  }

  async function registerPayment(item: ExpenseClaim, payment: PaymentInput) {
    const success = await expense.registerPayment(item, payment)
    if (success) await loadData()
    return success
  }

  async function withdrawLeave(item: LeaveRequest) {
    if (await leave.withdrawLeave(item)) await loadData()
  }

  async function withdrawExpense(item: ExpenseClaim) {
    if (await expense.withdrawExpense(item)) await loadData()
  }

  async function withdrawTravel(item: TravelRequest) {
    if (await travel.withdrawTravel(item)) await loadData()
  }

  return {
    initialized,
    initialize,
    login,
    logout,
    resetWorkspace,
    handleUnauthorized,
    loadData,
    submitLeave,
    submitExpense,
    submitTravel,
    processTask,
    transferTask,
    registerPayment,
    withdrawLeave,
    withdrawExpense,
    withdrawTravel
  }
})
