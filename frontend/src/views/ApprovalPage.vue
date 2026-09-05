<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { WorkItem, WorkItemTab } from '../api/types'
import OaDialog from '../components/OaDialog.vue'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useOrganizationStore } from '../stores/organization'
import { useUiStore } from '../stores/ui'
import { useWorkItemStore } from '../stores/work-items'

type DialogMode = 'decision' | 'transfer'

const router = useRouter()
const auth = useAuthStore()
const employees = useEmployeeDirectoryStore()
const organization = useOrganizationStore()
const ui = useUiStore()
const workItems = useWorkItemStore()
const processing = ref(false)
const dialogError = ref('')
const dialog = reactive<{ open: boolean; mode: DialogMode; action: 'approve' | 'reject'; item: WorkItem | null; comment: string; assigneeId: string }>({
  open: false, mode: 'decision', action: 'approve', item: null, comment: '', assigneeId: ''
})

const tabs: Array<{ key: WorkItemTab; label: string }> = [
  { key: 'pending', label: '待我处理' }, { key: 'processed', label: '我的已办' }, { key: 'initiated', label: '我发起的' },
  { key: 'reading', label: '待阅与已阅' }, { key: 'risk', label: '风险提醒' }
]
const tabCount = (tab: WorkItemTab) => tab === 'pending' ? workItems.summary.pendingCount : tab === 'processed' ? workItems.summary.processedCount : tab === 'initiated' ? workItems.summary.initiatedCount : tab === 'reading' ? workItems.summary.pendingReadCount : workItems.summary.riskCount
const transferCandidates = computed(() => employees.employees.filter(employee => employee.id !== auth.currentUserId))
const dialogTitle = computed(() => dialog.mode === 'transfer' ? '转办审批任务' : dialog.action === 'reject' ? '驳回审批' : '同意审批')
const roleDescription = computed(() => {
  const parts = [`当前角色：${auth.currentUser?.role ?? '员工'}`]
  if (workItems.summary.pendingPersonnelCount) parts.push(`${workItems.summary.pendingPersonnelCount} 项人事办理`)
  if (workItems.summary.pendingAttendanceCount) parts.push(`${workItems.summary.pendingAttendanceCount} 项考勤审核`)
  if (workItems.summary.pendingFinanceCount) parts.push(`${workItems.summary.pendingFinanceCount} 项财务付款`)
  if (workItems.summary.pendingApprovalCount) parts.push(`${workItems.summary.pendingApprovalCount} 项业务审批`)
  return parts.join(' · ')
})

function openDecision(item: WorkItem, action: 'approve' | 'reject') {
  Object.assign(dialog, { open: true, mode: 'decision', action, item, comment: '', assigneeId: '' })
  dialogError.value = ''; ui.error = ''
}
function openTransfer(item: WorkItem) {
  Object.assign(dialog, { open: true, mode: 'transfer', action: 'approve', item, comment: '', assigneeId: '' })
  dialogError.value = ''; ui.error = ''
}
function closeDialog() { if (!processing.value) { dialog.open = false; dialog.item = null; dialogError.value = '' } }
async function submitDialog() {
  if (!dialog.item) return
  if (dialog.mode === 'decision' && dialog.action === 'reject' && !dialog.comment.trim()) { dialogError.value = '驳回必须填写处理意见。'; return }
  if (dialog.mode === 'transfer' && (!dialog.assigneeId || !dialog.comment.trim())) { dialogError.value = '请选择转办人并填写转办意见。'; return }
  processing.value = true
  const success = dialog.mode === 'transfer'
    ? await workItems.transferApproval(dialog.item, dialog.assigneeId, dialog.comment)
    : await workItems.processApproval(dialog.item, dialog.action, dialog.comment)
  processing.value = false
  if (success) closeDialog(); else dialogError.value = ui.error || '操作失败，请刷新后重试。'
}
async function openItem(item: WorkItem) {
  if (item.category === 'COPY' && !await workItems.markCopyRead(item)) return
  await router.push(item.route)
}
function businessLabel(value: string) {
  return ({ leave: '请假', expense: '报销', travel: '出差', purchase: '采购', seal: '用章', personnel: '人事办理', attendance: '考勤申诉', announcement: '公司公告', document: '制度文档', contract: '劳动合同' } as Record<string, string>)[value] ?? value
}
function statusLabel(value: string) {
  return ({ Pending: '待处理', Approved: '已同意', Rejected: '已驳回', Draft: '草稿', Approving: '审批中', Completed: '已完成', Withdrawn: '已撤回', PENDING: '待处理', APPROVED: '已同意', REJECTED: '已驳回', COMPLETED: '已完成', PUBLISHED: '已发布', ACTIVE: '有效', EXPIRING: '即将到期', EXPIRED: '已到期' } as Record<string, string>)[value] ?? value
}
function actionLabel(item: WorkItem) {
  if (item.actionType === 'COMPLETE') return '去办理'
  if (item.actionType === 'REVIEW') return '去审核'
  if (item.actionType === 'PAYMENT') return '去付款'
  if (item.actionType === 'ACKNOWLEDGE') return '去签收'
  if (item.actionType === 'READ') return '阅读'
  return '查看详情'
}
function dateText(value?: string | null) { return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—' }
onMounted(() => { void organization.loadOrganization(); void workItems.load() })
</script>

<template>
  <div class="page-heading">
    <div><p class="eyebrow">WORK ITEM CENTER</p><h1>事项中心</h1><p>{{ roleDescription }}，仅展示您权限范围内的事项。</p></div>
    <button class="secondary" :disabled="workItems.loading" @click="workItems.load()">{{ workItems.loading ? '刷新中…' : '刷新' }}</button>
  </div>

  <section class="work-item-overview">
    <article><small>业务审批</small><strong>{{ workItems.summary.pendingApprovalCount }}</strong></article><article><small>人事办理</small><strong>{{ workItems.summary.pendingPersonnelCount }}</strong></article><article><small>考勤审核</small><strong>{{ workItems.summary.pendingAttendanceCount }}</strong></article><article><small>财务付款</small><strong>{{ workItems.summary.pendingFinanceCount }}</strong></article><article><small>未读事项</small><strong>{{ workItems.summary.pendingReadCount }}</strong></article><article><small>风险提醒</small><strong>{{ workItems.summary.riskCount }}</strong></article>
  </section>

  <nav class="work-item-tabs" aria-label="事项分类"><button v-for="tab in tabs" :key="tab.key" :class="{ active: workItems.activeTab === tab.key }" @click="workItems.selectTab(tab.key)">{{ tab.label }} <b v-if="tabCount(tab.key)">{{ tabCount(tab.key) }}</b></button></nav>

  <form class="filter-bar" @submit.prevent="workItems.search">
    <input v-model="workItems.filters.keyword" aria-label="关键词" placeholder="单号、标题、申请人或部门">
    <select v-model="workItems.filters.businessType" aria-label="事项类型"><option value="">全部类型</option><option value="leave">请假</option><option value="expense">报销</option><option value="travel">出差</option><option value="purchase">采购</option><option value="seal">用章</option><option value="personnel">人事办理</option><option value="attendance">考勤申诉</option><option value="announcement">公司公告</option><option value="document">制度文档</option><option value="contract">劳动合同</option></select>
    <select v-model="workItems.filters.status" aria-label="状态"><option value="">全部状态</option><option value="Pending">待处理</option><option value="Approving">审批中</option><option value="Approved">已同意</option><option value="Rejected">已驳回</option><option value="Completed">已完成</option><option value="Draft">草稿</option><option value="PUBLISHED">已发布</option><option value="EXPIRING">即将到期</option><option value="EXPIRED">已到期</option></select>
    <select v-model="workItems.filters.applicantId" aria-label="发起人"><option value="">全部发起人</option><option v-for="employee in employees.employees" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.departmentName }}</option></select>
    <select v-model="workItems.filters.departmentId" aria-label="部门"><option value="">全部部门</option><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select>
    <label>开始日期<input v-model="workItems.filters.startDate" type="date"></label><label>结束日期<input v-model="workItems.filters.endDate" type="date"></label>
    <button type="submit">查询</button><button class="secondary" type="button" @click="workItems.resetFilters">重置</button>
  </form>

  <section class="panel work-item-panel">
    <div class="table-wrap"><table class="data-table work-item-table"><thead><tr><th>事项</th><th>类型</th><th>申请人 / 部门</th><th>状态 / 节点</th><th>时间</th><th>操作</th></tr></thead><tbody>
      <tr v-for="item in workItems.items" :key="item.id" :class="{ unread: workItems.activeTab === 'reading' && !item.isRead }">
        <td><strong>{{ item.title }}</strong><small>{{ item.number }}<template v-if="item.dueDate"> · 截止 {{ item.dueDate }}</template></small></td>
        <td><span class="type-chip">{{ businessLabel(item.businessType) }}</span><em v-if="item.urgency !== 'NORMAL'" :class="{ holiday: item.urgency === 'OVERDUE' }">{{ item.urgency === 'OVERDUE' ? '已逾期' : '即将到期' }}</em></td>
        <td>{{ item.applicantName || '—' }}<small>{{ item.departmentName || '—' }}</small></td><td><em>{{ statusLabel(item.status) }}</em><small>{{ item.currentNode || '—' }}</small></td><td>{{ dateText(item.processedAt || item.occurredAt) }}</td>
        <td class="task-actions"><button class="secondary" @click="openItem(item)">{{ actionLabel(item) }}</button><template v-if="item.category === 'APPROVAL' && item.canProcess"><button @click="openDecision(item, 'approve')">同意</button><button class="secondary" @click="openDecision(item, 'reject')">驳回</button><button class="secondary" @click="openTransfer(item)">转办</button></template></td>
      </tr>
    </tbody></table></div>
    <p v-if="workItems.loading" class="empty">正在加载事项…</p><p v-else-if="!workItems.items.length" class="empty">当前分类暂无符合条件的事项。</p>
    <div class="pagination"><span>共 {{ workItems.total }} 条</span><div><button class="secondary" :disabled="workItems.page === 1" @click="workItems.goToPage(workItems.page - 1)">上一页</button><b>{{ workItems.page }} / {{ workItems.totalPages }}</b><button class="secondary" :disabled="workItems.page === workItems.totalPages" @click="workItems.goToPage(workItems.page + 1)">下一页</button></div></div>
  </section>

  <OaDialog :open="dialog.open" :title="dialogTitle" :description="dialog.item ? `${businessLabel(dialog.item.businessType)} · ${dialog.item.number}` : ''" :submit-label="dialog.mode === 'transfer' ? '确认转办' : dialog.action === 'reject' ? '确认驳回' : '确认同意'" :busy="processing" :danger="dialog.mode === 'decision' && dialog.action === 'reject'" @close="closeDialog" @submit="submitDialog">
    <div v-if="dialog.item" class="operation-summary"><strong>{{ dialog.item.title }}</strong><span>{{ dialog.item.currentNode }}</span></div>
    <label v-if="dialog.mode === 'transfer'" class="dialog-field">转办人<select v-model="dialog.assigneeId"><option value="">请选择在职用户</option><option v-for="employee in transferCandidates" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.role }} · {{ employee.departmentName }}</option></select></label>
    <label class="dialog-field">{{ dialog.mode === 'transfer' ? '转办意见' : dialog.action === 'reject' ? '驳回意见' : '审批意见（可选）' }}<textarea v-model="dialog.comment" maxlength="500" :placeholder="dialog.action === 'reject' ? '说明驳回原因，便于申请人修改' : '可填写处理意见'" /></label><p v-if="dialogError" class="dialog-error">{{ dialogError }}</p>
  </OaDialog>
</template>

<style scoped>
.work-item-overview { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 12px; margin: 16px 0; }.work-item-overview article { padding: 15px 17px; border: 1px solid #e4e9f1; border-radius: 8px; background: #fff; }.work-item-overview small, .work-item-overview strong { display: block; }.work-item-overview small { color: #758298; font-size: .75rem; }.work-item-overview strong { margin-top: 8px; color: #26354d; font-size: 1.45rem; }
.work-item-tabs { display: flex; gap: 4px; overflow-x: auto; border-bottom: 1px solid #dde4ee; }.work-item-tabs button { border: 0; border-bottom: 2px solid transparent; padding: 11px 15px; background: transparent; color: #66758b; white-space: nowrap; }.work-item-tabs button.active { border-bottom-color: #3478e8; color: #286acb; font-weight: 700; }.work-item-tabs b { margin-left: 5px; border-radius: 99px; padding: 2px 6px; background: #eaf2ff; font-size: .68rem; }.filter-bar { margin-top: 14px; }.work-item-panel { margin-top: 0; }.work-item-table { min-width: 1040px; }.work-item-table td:first-child { min-width: 230px; }.work-item-table strong, .work-item-table small { display: block; }.work-item-table small { margin-top: 4px; color: #8b97a9; font-size: .69rem; }.work-item-table tr.unread td:first-child { box-shadow: inset 3px 0 #3478e8; }.type-chip { display: inline-block; margin-right: 6px; color: #4d5c72; }
@media (max-width: 900px) { .work-item-overview { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
</style>
