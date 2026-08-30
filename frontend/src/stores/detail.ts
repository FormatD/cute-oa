import { ref } from 'vue'
import { defineStore } from 'pinia'
import type { DetailView, Notification, WorkCalendarEntry } from '../api/types'
import { useExpenseStore } from './expense'
import { useLeaveStore } from './leave'
import { useTravelStore } from './travel'
import { useUiStore } from './ui'
import { useWorkflowStore } from './workflow'
import { useWorkspaceStore } from './workspace'

export const useDetailStore = defineStore('detail', () => {
  const ui = useUiStore()
  const leave = useLeaveStore()
  const expense = useExpenseStore()
  const travel = useTravelStore()
  const workflow = useWorkflowStore()
  const workspace = useWorkspaceStore()
  const detailLoading = ref(false)
  const notificationDetail = ref<Notification | null>(null)
  const calendarDetail = ref<WorkCalendarEntry | null>(null)

  function reset() {
    notificationDetail.value = null
    calendarDetail.value = null
    detailLoading.value = false
  }

  async function loadDetail(view: DetailView) {
    detailLoading.value = true
    ui.error = ''
    leave.leaveDetail = null
    expense.expenseDetail = null
    travel.travelDetail = null
    notificationDetail.value = null
    calendarDetail.value = null

    try {
      if (view.module === 'leave') await leave.loadLeaveDetail(view.id)
      if (view.module === 'expense') await expense.loadExpenseDetail(view.id)
      if (view.module === 'travel') await travel.loadTravelDetail(view.id)
      if (view.module === 'notification') {
        notificationDetail.value = workflow.notifications.find(item => item.id === view.id) ?? null
        if (!notificationDetail.value) {
          await workflow.loadNotifications()
          notificationDetail.value = workflow.notifications.find(item => item.id === view.id) ?? null
        }
      }
      if (view.module === 'calendar') {
        calendarDetail.value = workspace.calendarEntries.find(item => item.date === view.id) ?? null
      }
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '详情加载失败。'
    } finally {
      detailLoading.value = false
    }
  }

  return { detailLoading, notificationDetail, calendarDetail, loadDetail, reset }
})
