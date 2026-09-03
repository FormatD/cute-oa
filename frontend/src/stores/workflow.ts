import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { FlowCopy, FlowTask, Notification } from '../api/types'
import { useDashboardStore } from './dashboard'
import { PAGE_SIZE, paginate } from './pagination'
import { useUiStore } from './ui'

export const useWorkflowStore = defineStore('workflow', () => {
  const ui = useUiStore()
  const dashboard = useDashboardStore()
  const client = useApiClient()
  const api = client.workflow
  const systemApi = client.system
  const tasks = ref<FlowTask[]>([])
  const expenseTasks = ref<FlowTask[]>([])
  const processedTasks = ref<FlowTask[]>([])
  const processedExpenseTasks = ref<FlowTask[]>([])
  const travelTasks = ref<FlowTask[]>([])
  const processedTravelTasks = ref<FlowTask[]>([])
  const purchaseTasks = ref<FlowTask[]>([])
  const processedPurchaseTasks = ref<FlowTask[]>([])
  const sealTasks = ref<FlowTask[]>([])
  const processedSealTasks = ref<FlowTask[]>([])
  const notifications = ref<Notification[]>([])
  const copies = ref<FlowCopy[]>([])
  const leaveTaskPage = ref(1)
  const expenseTaskPage = ref(1)
  const travelTaskPage = ref(1)
  const purchaseTaskPage = ref(1)
  const sealTaskPage = ref(1)
  const notificationPage = ref(1)
  const processedTaskPage = ref(1)
  const copyPage = ref(1)
  const copyTotal = ref(0)
  const copyTotalPages = ref(1)
  const pagedLeaveTasks = computed(() => paginate([...tasks.value].reverse(), leaveTaskPage.value))
  const pagedExpenseTasks = computed(() => paginate([...expenseTasks.value].reverse(), expenseTaskPage.value))
  const pagedTravelTasks = computed(() => paginate([...travelTasks.value].reverse(), travelTaskPage.value))
  const pagedPurchaseTasks = computed(() => paginate([...purchaseTasks.value].reverse(), purchaseTaskPage.value))
  const pagedSealTasks = computed(() => paginate([...sealTasks.value].reverse(), sealTaskPage.value))
  const processedTaskItems = computed(() => [
    ...processedTasks.value.map(task => ({ ...task, businessType: '请假', route: 'leave', resourceId: task.leaveRequestId })),
    ...processedExpenseTasks.value.map(task => ({ ...task, businessType: '报销', route: 'expense', resourceId: task.expenseClaimId })),
    ...processedTravelTasks.value.map(task => ({ ...task, businessType: '出差', route: 'travel', resourceId: task.travelRequestId })),
    ...processedPurchaseTasks.value.map(task => ({ ...task, businessType: '采购', route: 'purchase', resourceId: task.purchaseRequestId })),
    ...processedSealTasks.value.map(task => ({ ...task, businessType: '用章', route: 'seal', resourceId: task.sealRequestId }))
  ])
  const pagedProcessedTasks = computed(() => paginate(processedTaskItems.value, processedTaskPage.value))
  const pagedNotifications = computed(() => paginate([...notifications.value].sort((a, b) => b.createdAt.localeCompare(a.createdAt)), notificationPage.value))
  const pagedCopies = computed(() => ({ items: copies.value, currentPage: copyPage.value, totalPages: copyTotalPages.value, total: copyTotal.value }))
  const unreadCopies = computed(() => copies.value.filter(item => !item.readAt))

  async function loadTasks() {
    const [pendingLeaves, completedLeaves, pendingExpenses, completedExpenses, pendingTravels, completedTravels, pendingPurchases, completedPurchases, pendingSeals, completedSeals] = await Promise.all([
      api.getLeaveTasks(), api.getProcessedLeaveTasks(), api.getExpenseTasks(), api.getProcessedExpenseTasks(), api.getTravelTasks(), api.getProcessedTravelTasks(), api.getPurchaseTasks(), api.getProcessedPurchaseTasks(), api.getSealTasks(), api.getProcessedSealTasks()
    ])
    tasks.value = pendingLeaves
    processedTasks.value = completedLeaves
    expenseTasks.value = pendingExpenses
    processedExpenseTasks.value = completedExpenses
    travelTasks.value = pendingTravels
    processedTravelTasks.value = completedTravels
    purchaseTasks.value = pendingPurchases
    processedPurchaseTasks.value = completedPurchases
    sealTasks.value = pendingSeals
    processedSealTasks.value = completedSeals
  }

  async function loadNotifications() {
    notifications.value = await systemApi.getNotifications()
  }

  async function loadCopies() {
    try {
      const result = await api.getCopies(copyPage.value, PAGE_SIZE)
      copies.value = result.items
      copyPage.value = result.page
      copyTotal.value = result.total
      copyTotalPages.value = result.totalPages
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '待我阅读列表加载失败。'
    }
  }

  async function markNotificationRead(notification: Notification) {
    if (notification.readAt) return
    try {
      await systemApi.markNotificationRead(notification.id)
      notification.readAt = new Date().toISOString()
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '无法更新通知状态。'
    }
  }

  async function markCopyRead(item: FlowCopy) {
    if (item.readAt) return false
    try {
      const updated = await api.markCopyRead(item.id)
      item.readAt = updated.readAt
      dashboard.decrementPendingReadCount()
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '无法更新抄送事项阅读状态。'
      return false
    }
  }

  async function processTask(task: FlowTask, action: 'approve' | 'reject', businessType: 'leave' | 'expense' | 'travel' | 'purchase' | 'seal', comment: string) {
    const normalizedComment = comment.trim()
    if (action === 'reject' && !normalizedComment) {
      ui.error = '驳回必须填写处理意见。'
      return false
    }
    ui.error = ''
    try {
      if (businessType === 'leave') await api.processLeaveTask(task.id, action, normalizedComment)
      else if (businessType === 'expense') await api.processExpenseTask(task.id, action, normalizedComment)
      else if (businessType === 'travel') await api.processTravelTask(task.id, action, normalizedComment)
      else if (businessType === 'purchase') await api.processPurchaseTask(task.id, action, normalizedComment)
      else await api.processSealTask(task.id, action, normalizedComment)
      const label = businessType === 'leave' ? '请假' : businessType === 'expense' ? '报销' : businessType === 'travel' ? '出差' : businessType === 'purchase' ? '采购' : '用章'
      ui.message = action === 'approve' ? `${label}审批已通过。` : `${label}申请已驳回。`
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : `处理${businessType === 'seal' ? '用章' : '业务'}待办失败。`
      return false
    }
  }

  async function transferTask(task: FlowTask, businessType: 'leave' | 'expense' | 'travel' | 'purchase' | 'seal', assigneeId: string, comment: string) {
    const normalizedAssigneeId = assigneeId.trim()
    const normalizedComment = comment.trim()
    if (!normalizedAssigneeId || !normalizedComment) {
      ui.error = '请选择转办人并填写转办意见。'
      return false
    }
    ui.error = ''
    try {
      if (businessType === 'leave') await api.transferLeaveTask(task.id, normalizedAssigneeId, normalizedComment)
      else if (businessType === 'expense') await api.transferExpenseTask(task.id, normalizedAssigneeId, normalizedComment)
      else if (businessType === 'travel') await api.transferTravelTask(task.id, normalizedAssigneeId, normalizedComment)
      else if (businessType === 'purchase') await api.transferPurchaseTask(task.id, normalizedAssigneeId, normalizedComment)
      else await api.transferSealTask(task.id, normalizedAssigneeId, normalizedComment)
      const label = businessType === 'leave' ? '请假' : businessType === 'expense' ? '报销' : businessType === 'travel' ? '出差' : businessType === 'purchase' ? '采购' : '用章'
      ui.message = `${label}审批任务已转办。`
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : `转办${businessType}审批任务失败。`
      return false
    }
  }

  function reset() {
    tasks.value = []
    expenseTasks.value = []
    processedTasks.value = []
    processedExpenseTasks.value = []
    travelTasks.value = []
    processedTravelTasks.value = []
    purchaseTasks.value = []
    processedPurchaseTasks.value = []
    sealTasks.value = []
    processedSealTasks.value = []
    notifications.value = []
    copies.value = []
  }

  return { tasks, expenseTasks, travelTasks, purchaseTasks, sealTasks, processedTasks, processedExpenseTasks, processedTravelTasks, processedPurchaseTasks, processedSealTasks, notifications, copies, leaveTaskPage, expenseTaskPage, travelTaskPage, purchaseTaskPage, sealTaskPage, notificationPage, processedTaskPage, copyPage, copyTotal, copyTotalPages, pagedLeaveTasks, pagedExpenseTasks, pagedTravelTasks, pagedPurchaseTasks, pagedSealTasks, pagedProcessedTasks, pagedNotifications, pagedCopies, unreadCopies, loadTasks, loadNotifications, loadCopies, markNotificationRead, markCopyRead, processTask, transferTask, reset }
})
