import { defineStore } from 'pinia'
import type { ExpenseClaim, FlowTask, LeaveRequest, PaymentInput, PurchaseRequest, SealRequest, TravelRequest } from '../api/types'
import { useAuthStore } from './auth'
import { useExpenseStore } from './expense'
import { useLeaveStore } from './leave'
import { usePurchaseStore } from './purchase'
import { useSealStore } from './seal'
import { useTravelStore } from './travel'
import { useWorkflowStore } from './workflow'
import { useWorkspaceStore } from './workspace'

type TaskAction = 'approve' | 'reject'
type BusinessType = 'leave' | 'expense' | 'travel' | 'purchase' | 'seal'

/**
 * Compatibility facade for cross-module UI actions.
 * Domain state and API calls live in their own stores; workspace lifecycle lives
 * in useWorkspaceStore. This store only coordinates successful mutations.
 */
export const useAppStore = defineStore('app', () => {
  const auth = useAuthStore()
  const workspace = useWorkspaceStore()
  const leave = useLeaveStore()
  const expense = useExpenseStore()
  const travel = useTravelStore()
  const purchase = usePurchaseStore()
  const seal = useSealStore()
  const workflow = useWorkflowStore()

  async function login(userId: string, password: string) {
    const progress = await auth.login(userId, password)
    if (progress === 'AUTHENTICATED') await workspace.load()
    return progress
  }

  async function verifyMfa(code: string) {
    if (!await auth.verifyMfa(code)) return false
    await workspace.load()
    return true
  }

  async function confirmMfaSetup(code: string) {
    if (!await auth.confirmMfaSetup(code)) return false
    await workspace.load()
    return true
  }

  async function logout() {
    await auth.logout()
    workspace.reset()
  }

  async function refreshAfter(success: boolean) {
    if (success) await workspace.refreshBusinessData()
    return success
  }

  async function submitLeave() { return refreshAfter(await leave.submitLeave()) }
  async function submitExpense() { return refreshAfter(await expense.submitExpense(auth.currentUser?.name ?? '')) }
  async function submitTravel() { return refreshAfter(await travel.submitTravel()) }
  async function submitPurchase() { return refreshAfter(await purchase.submitPurchase()) }
  async function submitSeal() { return refreshAfter(await seal.submitSeal()) }

  async function processTask(task: FlowTask, action: TaskAction, businessType: BusinessType, comment: string) {
    return refreshAfter(await workflow.processTask(task, action, businessType, comment))
  }

  async function transferTask(task: FlowTask, businessType: BusinessType, assigneeId: string, comment: string) {
    return refreshAfter(await workflow.transferTask(task, businessType, assigneeId, comment))
  }

  async function registerPayment(item: ExpenseClaim, payment: PaymentInput) {
    return refreshAfter(await expense.registerPayment(item, payment))
  }

  async function withdrawLeave(item: LeaveRequest) { return refreshAfter(await leave.withdrawLeave(item)) }
  async function withdrawExpense(item: ExpenseClaim) { return refreshAfter(await expense.withdrawExpense(item)) }
  async function withdrawTravel(item: TravelRequest) { return refreshAfter(await travel.withdrawTravel(item)) }
  async function withdrawPurchase(item: PurchaseRequest) { return refreshAfter(await purchase.withdrawPurchase(item)) }
  async function registerPurchaseOrder(item: PurchaseRequest) { return refreshAfter(await purchase.registerOrder(item)) }
  async function receivePurchase(item: PurchaseRequest) { return refreshAfter(await purchase.receive(item)) }
  async function withdrawSeal(item: SealRequest) { return refreshAfter(await seal.withdrawSeal(item)) }
  async function registerSealExecution(item: SealRequest) { return refreshAfter(await seal.registerExecution(item)) }
  async function registerSealReturn(item: SealRequest) { return refreshAfter(await seal.registerReturn(item)) }

  return {
    initialize: workspace.initialize,
    resetWorkspace: workspace.reset,
    handleUnauthorized: auth.clearSession,
    loadData: workspace.load,
    login,
    verifyMfa,
    confirmMfaSetup,
    logout,
    submitLeave,
    submitExpense,
    submitTravel,
    submitPurchase,
    submitSeal,
    processTask,
    transferTask,
    registerPayment,
    withdrawLeave,
    withdrawExpense,
    withdrawTravel,
    withdrawPurchase,
    registerPurchaseOrder,
    receivePurchase,
    withdrawSeal,
    registerSealExecution,
    registerSealReturn
  }
})
