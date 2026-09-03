import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useAnnouncementStore } from './announcements'
import { useAttendanceStore } from './attendance'
import { useAuthStore } from './auth'
import { useCalendarStore } from './calendar'
import { useContractStore } from './contracts'
import { useDashboardStore } from './dashboard'
import { useDetailStore } from './detail'
import { useDocumentStore } from './documents'
import { useEmployeeDirectoryStore } from './employee-directory'
import { useExpenseStore } from './expense'
import { useLeaveStore } from './leave'
import { useOrganizationStore } from './organization'
import { usePersonnelCaseStore } from './personnel-cases'
import { usePersonnelStore } from './personnel'
import { usePurchaseStore } from './purchase'
import { useSealStore } from './seal'
import { useTravelStore } from './travel'
import { useUiStore } from './ui'
import { useWorkflowStore } from './workflow'

export const useWorkspaceStore = defineStore('workspace', () => {
  const auth = useAuthStore()
  const ui = useUiStore()
  const calendar = useCalendarStore()
  const dashboard = useDashboardStore()
  const employeeDirectory = useEmployeeDirectoryStore()
  const leave = useLeaveStore()
  const expense = useExpenseStore()
  const travel = useTravelStore()
  const purchase = usePurchaseStore()
  const seal = useSealStore()
  const workflow = useWorkflowStore()
  const organization = useOrganizationStore()
  const personnel = usePersonnelStore()
  const personnelCases = usePersonnelCaseStore()
  const attendance = useAttendanceStore()
  const contracts = useContractStore()
  const announcements = useAnnouncementStore()
  const documents = useDocumentStore()
  const detail = useDetailStore()

  const initialized = ref(false)
  const loading = ref(false)
  let pendingLoad: Promise<void> | null = null

  async function initialize() {
    await auth.initializeAuth()
    if (auth.authenticated && !initialized.value) await load()
  }

  async function load() {
    if (!auth.authenticated) return
    if (pendingLoad) return pendingLoad

    loading.value = true
    pendingLoad = loadWorkspaceData()
    try {
      await pendingLoad
      initialized.value = true
    } finally {
      loading.value = false
      pendingLoad = null
    }
  }

  async function loadWorkspaceData() {
    ui.error = ''
    try {
      await Promise.all([
        calendar.loadCalendar().catch(() => undefined),
        dashboard.loadSummary(),
        employeeDirectory.loadEmployees()
      ])
      await refreshBusinessData()
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '无法连接 API，请先启动后端服务。'
    }
  }

  async function refreshBusinessData() {
    if (!auth.authenticated) return
    await Promise.all([
      dashboard.loadSummary(),
      leave.loadLeaves(),
      leave.loadInitiated(auth.currentUserId),
      expense.loadExpenses(),
      expense.loadInitiated(auth.currentUserId),
      travel.loadTravels(),
      travel.loadInitiated(auth.currentUserId),
      travel.loadApproved(auth.currentUserId),
      purchase.loadPurchases(),
      purchase.loadInitiated(auth.currentUserId),
      seal.loadSeals(),
      seal.loadInitiated(auth.currentUserId),
      workflow.loadTasks(),
      workflow.loadNotifications(),
      workflow.loadCopies(),
      documents.loadCategories().catch(() => undefined),
      documents.loadDocuments().catch(() => undefined)
    ])
  }

  function reset() {
    calendar.reset()
    dashboard.reset()
    employeeDirectory.reset()
    leave.reset()
    expense.reset()
    travel.reset()
    purchase.reset()
    seal.reset()
    workflow.reset()
    organization.reset()
    personnel.reset()
    personnelCases.reset()
    attendance.reset()
    contracts.reset()
    announcements.reset()
    documents.reset()
    detail.reset()
    initialized.value = false
  }

  return { initialized, loading, initialize, load, refreshBusinessData, reset }
})
