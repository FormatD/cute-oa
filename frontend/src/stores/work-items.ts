import { reactive, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { FlowTask, WorkItem, WorkItemFilters, WorkItemSummary, WorkItemTab } from '../api/types'
import { useDashboardStore } from './dashboard'
import { useUiStore } from './ui'
import { useWorkflowStore } from './workflow'

type ApprovalBusinessType = 'leave' | 'expense' | 'travel' | 'purchase' | 'seal'

const emptySummary = (): WorkItemSummary => ({ pendingCount: 0, pendingApprovalCount: 0, pendingPersonnelCount: 0, pendingAttendanceCount: 0, pendingFinanceCount: 0, processedCount: 0, initiatedCount: 0, pendingReadCount: 0, riskCount: 0 })
const emptyFilters = (): WorkItemFilters => ({ businessType: '', keyword: '', status: '', applicantId: '', departmentId: '', startDate: '', endDate: '' })

export const useWorkItemStore = defineStore('work-items', () => {
  const api = useApiClient().workItems
  const workflow = useWorkflowStore()
  const dashboard = useDashboardStore()
  const ui = useUiStore()
  const activeTab = ref<WorkItemTab>('pending')
  const items = ref<WorkItem[]>([])
  const workbenchPending = ref<WorkItem[]>([])
  const workbenchInitiated = ref<WorkItem[]>([])
  const workbenchReading = ref<WorkItem[]>([])
  const summary = ref<WorkItemSummary>(emptySummary())
  const filters = reactive<WorkItemFilters>(emptyFilters())
  const page = ref(1)
  const pageSize = ref(20)
  const total = ref(0)
  const totalPages = ref(1)
  const loading = ref(false)
  const workbenchLoaded = ref(false)

  async function load(resetPage = false) {
    if (resetPage) page.value = 1
    loading.value = true
    try {
      const response = await api.list(activeTab.value, filters, page.value, pageSize.value)
      items.value = response.items
      summary.value = response.summary
      page.value = response.page
      total.value = response.total
      totalPages.value = response.totalPages
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '事项中心加载失败。'
    } finally {
      loading.value = false
    }
  }

  async function selectTab(tab: WorkItemTab) {
    activeTab.value = tab
    await load(true)
  }

  async function loadWorkbench() {
    try {
      const overview = await api.overview(5)
      workbenchPending.value = overview.pending
      workbenchInitiated.value = overview.initiated
      workbenchReading.value = overview.reading
      summary.value = overview.summary
      workbenchLoaded.value = true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '工作台事项加载失败。'
    }
  }

  async function search() { await load(true) }

  async function resetFilters() {
    Object.assign(filters, emptyFilters())
    await load(true)
  }

  async function goToPage(nextPage: number) {
    if (nextPage < 1 || nextPage > totalPages.value || nextPage === page.value) return
    page.value = nextPage
    await load()
  }

  function asFlowTask(item: WorkItem): FlowTask {
    return { id: item.taskId!, assigneeName: '', sequence: Number(item.currentNode?.match(/\d+/)?.[0] ?? 1), status: item.status }
  }

  async function processApproval(item: WorkItem, action: 'approve' | 'reject', comment: string) {
    if (!item.taskId || !isApprovalBusiness(item.businessType)) return false
    const success = await workflow.processTask(asFlowTask(item), action, item.businessType, comment)
    if (success) await refreshAfterMutation()
    return success
  }

  async function transferApproval(item: WorkItem, assigneeId: string, comment: string) {
    if (!item.taskId || !isApprovalBusiness(item.businessType)) return false
    const success = await workflow.transferTask(asFlowTask(item), item.businessType, assigneeId, comment)
    if (success) await refreshAfterMutation()
    return success
  }

  async function markCopyRead(item: WorkItem) {
    if (item.category !== 'COPY' || item.isRead || !item.taskId) return true
    try {
      await useApiClient().workflow.markCopyRead(item.taskId)
      await refreshAfterMutation()
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '无法更新阅读状态。'
      return false
    }
  }

  async function refreshAfterMutation() {
    await Promise.all([load(), loadWorkbench(), dashboard.loadSummary()])
  }

  function reset() {
    activeTab.value = 'pending'
    items.value = []
    workbenchPending.value = []
    workbenchInitiated.value = []
    workbenchReading.value = []
    summary.value = emptySummary()
    Object.assign(filters, emptyFilters())
    page.value = 1
    total.value = 0
    totalPages.value = 1
    workbenchLoaded.value = false
  }

  return { activeTab, items, workbenchPending, workbenchInitiated, workbenchReading, summary, filters, page, pageSize, total, totalPages, loading, workbenchLoaded, load, loadWorkbench, selectTab, search, resetFilters, goToPage, processApproval, transferApproval, markCopyRead, reset }
})

function isApprovalBusiness(value: string): value is ApprovalBusinessType {
  return ['leave', 'expense', 'travel', 'purchase', 'seal'].includes(value)
}
