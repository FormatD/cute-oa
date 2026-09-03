<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { FlowTask } from '../api/types'
import OaDialog from '../components/OaDialog.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useUiStore } from '../stores/ui'
import { useWorkflowStore } from '../stores/workflow'

type BusinessType = 'leave' | 'expense' | 'travel' | 'purchase'
type DialogMode = 'decision' | 'transfer'

const app = useAppStore()
const auth = useAuthStore()
const employeeDirectory = useEmployeeDirectoryStore()
const ui = useUiStore()
const workflow = useWorkflowStore()
const router = useRouter()
const processing = ref(false)
const dialogError = ref('')
const dialog = reactive<{ open: boolean; mode: DialogMode; businessType: BusinessType; action: 'approve' | 'reject'; task: FlowTask | null; comment: string; assigneeId: string }>({
  open: false, mode: 'decision', businessType: 'leave', action: 'approve', task: null, comment: '', assigneeId: ''
})
const transferCandidates = computed(() => employeeDirectory.employees.filter(employee => employee.id !== auth.currentUserId))
const dialogTitle = computed(() => dialog.mode === 'transfer' ? '转办审批任务' : dialog.action === 'reject' ? '驳回审批' : '同意审批')
const businessLabel = (type: BusinessType) => type === 'leave' ? '请假' : type === 'expense' ? '报销' : type === 'travel' ? '出差' : '采购'
const dialogDescription = computed(() => `${businessLabel(dialog.businessType)}审批 · 第 ${dialog.task?.sequence ?? '—'} 节点`)

function openDecision(task: FlowTask, businessType: BusinessType, action: 'approve' | 'reject') {
  Object.assign(dialog, { open: true, mode: 'decision', businessType, action, task, comment: '', assigneeId: '' })
  dialogError.value = ''
  ui.error = ''
}

function openTransfer(task: FlowTask, businessType: BusinessType) {
  Object.assign(dialog, { open: true, mode: 'transfer', businessType, action: 'approve', task, comment: '', assigneeId: '' })
  dialogError.value = ''
  ui.error = ''
}

function closeDialog() {
  if (processing.value) return
  dialog.open = false
  dialog.task = null
  dialogError.value = ''
}

async function submitDialog() {
  if (!dialog.task) return
  if (dialog.mode === 'decision' && dialog.action === 'reject' && !dialog.comment.trim()) {
    dialogError.value = '驳回必须填写处理意见。'
    return
  }
  if (dialog.mode === 'transfer' && (!dialog.assigneeId || !dialog.comment.trim())) {
    dialogError.value = '请选择转办人并填写转办意见。'
    return
  }
  processing.value = true
  dialogError.value = ''
  const success = dialog.mode === 'transfer'
    ? await app.transferTask(dialog.task, dialog.businessType, dialog.assigneeId, dialog.comment)
    : await app.processTask(dialog.task, dialog.action, dialog.businessType, dialog.comment)
  processing.value = false
  if (success) closeDialog()
  else dialogError.value = ui.error || '操作失败，请刷新后重试。'
}
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">APPROVAL CENTER</p><h1>审批中心</h1><p>集中处理待办审批和业务通知。</p></div></div>

  <section v-if="workflow.tasks.length" class="panel approval-panel">
    <div class="section-title"><div><p class="eyebrow">LEAVE APPROVAL</p><h2>待我审批的请假</h2></div></div>
    <div class="table-wrap"><table class="data-table"><thead><tr><th>审批事项</th><th>当前节点</th><th>操作</th></tr></thead><tbody><tr v-for="task in workflow.pagedLeaveTasks.items" :key="task.id"><td>请假审批</td><td>第 {{ task.sequence }} 节点</td><td class="task-actions"><button class="secondary" @click="task.leaveRequestId && router.push(`/leave/${task.leaveRequestId}`)">详情</button><button @click="openDecision(task, 'leave', 'approve')">同意</button><button class="secondary" @click="openDecision(task, 'leave', 'reject')">驳回</button><button class="secondary" @click="openTransfer(task, 'leave')">转办</button></td></tr></tbody></table></div>
    <div class="pagination"><span>共 {{ workflow.pagedLeaveTasks.total }} 条</span><div><button class="secondary" :disabled="workflow.pagedLeaveTasks.currentPage === 1" @click="workflow.leaveTaskPage--">上一页</button><b>{{ workflow.pagedLeaveTasks.currentPage }} / {{ workflow.pagedLeaveTasks.totalPages }}</b><button class="secondary" :disabled="workflow.pagedLeaveTasks.currentPage === workflow.pagedLeaveTasks.totalPages" @click="workflow.leaveTaskPage++">下一页</button></div></div>
  </section>

  <section v-if="workflow.expenseTasks.length" class="panel approval-panel">
    <div class="section-title"><div><p class="eyebrow">EXPENSE APPROVAL</p><h2>待我审批的报销</h2></div></div>
    <div class="table-wrap"><table class="data-table"><thead><tr><th>审批事项</th><th>当前节点</th><th>操作</th></tr></thead><tbody><tr v-for="task in workflow.pagedExpenseTasks.items" :key="task.id"><td>报销审批</td><td>第 {{ task.sequence }} 节点</td><td class="task-actions"><button class="secondary" :disabled="!task.expenseClaimId" @click="task.expenseClaimId && router.push(`/expense/${task.expenseClaimId}`)">详情</button><button @click="openDecision(task, 'expense', 'approve')">同意</button><button class="secondary" @click="openDecision(task, 'expense', 'reject')">驳回</button><button class="secondary" @click="openTransfer(task, 'expense')">转办</button></td></tr></tbody></table></div>
    <div class="pagination"><span>共 {{ workflow.pagedExpenseTasks.total }} 条</span><div><button class="secondary" :disabled="workflow.pagedExpenseTasks.currentPage === 1" @click="workflow.expenseTaskPage--">上一页</button><b>{{ workflow.pagedExpenseTasks.currentPage }} / {{ workflow.pagedExpenseTasks.totalPages }}</b><button class="secondary" :disabled="workflow.pagedExpenseTasks.currentPage === workflow.pagedExpenseTasks.totalPages" @click="workflow.expenseTaskPage++">下一页</button></div></div>
  </section>

  <section v-if="workflow.travelTasks.length" class="panel approval-panel">
    <div class="section-title"><div><p class="eyebrow">TRAVEL APPROVAL</p><h2>待我审批的出差</h2></div></div>
    <div class="table-wrap"><table class="data-table"><thead><tr><th>审批事项</th><th>当前节点</th><th>操作</th></tr></thead><tbody><tr v-for="task in workflow.pagedTravelTasks.items" :key="task.id"><td>出差审批</td><td>第 {{ task.sequence }} 节点</td><td class="task-actions"><button class="secondary" :disabled="!task.travelRequestId" @click="task.travelRequestId && router.push(`/travel/${task.travelRequestId}`)">详情</button><button @click="openDecision(task, 'travel', 'approve')">同意</button><button class="secondary" @click="openDecision(task, 'travel', 'reject')">驳回</button><button class="secondary" @click="openTransfer(task, 'travel')">转办</button></td></tr></tbody></table></div>
    <div class="pagination"><span>共 {{ workflow.pagedTravelTasks.total }} 条</span><div><button class="secondary" :disabled="workflow.pagedTravelTasks.currentPage === 1" @click="workflow.travelTaskPage--">上一页</button><b>{{ workflow.pagedTravelTasks.currentPage }} / {{ workflow.pagedTravelTasks.totalPages }}</b><button class="secondary" :disabled="workflow.pagedTravelTasks.currentPage === workflow.pagedTravelTasks.totalPages" @click="workflow.travelTaskPage++">下一页</button></div></div>
  </section>

  <section v-if="workflow.purchaseTasks.length" class="panel approval-panel">
    <div class="section-title"><div><p class="eyebrow">PURCHASE APPROVAL</p><h2>待我审批的采购</h2></div></div>
    <div class="table-wrap"><table class="data-table"><thead><tr><th>审批事项</th><th>当前节点</th><th>操作</th></tr></thead><tbody><tr v-for="task in workflow.pagedPurchaseTasks.items" :key="task.id"><td>采购审批</td><td>第 {{ task.sequence }} 节点</td><td class="task-actions"><button class="secondary" :disabled="!task.purchaseRequestId" @click="task.purchaseRequestId && router.push(`/purchase/${task.purchaseRequestId}`)">详情</button><button @click="openDecision(task, 'purchase', 'approve')">同意</button><button class="secondary" @click="openDecision(task, 'purchase', 'reject')">驳回</button><button class="secondary" @click="openTransfer(task, 'purchase')">转办</button></td></tr></tbody></table></div>
    <div class="pagination"><span>共 {{ workflow.pagedPurchaseTasks.total }} 条</span><div><button class="secondary" :disabled="workflow.pagedPurchaseTasks.currentPage === 1" @click="workflow.purchaseTaskPage--">上一页</button><b>{{ workflow.pagedPurchaseTasks.currentPage }} / {{ workflow.pagedPurchaseTasks.totalPages }}</b><button class="secondary" :disabled="workflow.pagedPurchaseTasks.currentPage === workflow.pagedPurchaseTasks.totalPages" @click="workflow.purchaseTaskPage++">下一页</button></div></div>
  </section>

  <section v-if="workflow.pagedProcessedTasks.total" class="panel approval-panel">
    <div class="section-title"><div><p class="eyebrow">PROCESSED</p><h2>我的已办</h2></div></div>
    <div class="table-wrap"><table class="data-table"><thead><tr><th>业务类型</th><th>处理节点</th><th>处理结果</th><th>处理意见</th><th>操作</th></tr></thead><tbody><tr v-for="task in workflow.pagedProcessedTasks.items" :key="`${task.route}-${task.id}`"><td>{{ task.businessType }}</td><td>第 {{ task.sequence }} 节点</td><td><em>{{ task.status }}</em></td><td>{{ task.comment || '—' }}</td><td><button class="secondary" :disabled="!task.resourceId" @click="task.resourceId && router.push(`/${task.route}/${task.resourceId}`)">查看单据</button></td></tr></tbody></table></div>
    <div class="pagination"><span>共 {{ workflow.pagedProcessedTasks.total }} 条</span><div><button class="secondary" :disabled="workflow.pagedProcessedTasks.currentPage === 1" @click="workflow.processedTaskPage--">上一页</button><b>{{ workflow.pagedProcessedTasks.currentPage }} / {{ workflow.pagedProcessedTasks.totalPages }}</b><button class="secondary" :disabled="workflow.pagedProcessedTasks.currentPage === workflow.pagedProcessedTasks.totalPages" @click="workflow.processedTaskPage++">下一页</button></div></div>
  </section>

  <section v-if="workflow.notifications.length" class="panel approval-panel">
    <div class="section-title"><div><p class="eyebrow">NOTIFICATIONS</p><h2>我的通知</h2></div></div>
    <div class="table-wrap"><table class="data-table"><thead><tr><th>通知标题</th><th>通知内容</th><th>时间</th><th>操作</th></tr></thead><tbody><tr v-for="item in workflow.pagedNotifications.items" :key="item.id"><td>{{ item.title }}</td><td>{{ item.content }}</td><td>{{ item.createdAt.slice(0, 10) }}</td><td class="task-actions"><button class="secondary" @click="router.push(`/approval/${item.id}`)">详情</button><button v-if="!item.readAt" class="secondary" @click="workflow.markNotificationRead(item)">标为已读</button><em v-else>已读</em></td></tr></tbody></table></div>
    <div class="pagination"><span>共 {{ workflow.pagedNotifications.total }} 条</span><div><button class="secondary" :disabled="workflow.pagedNotifications.currentPage === 1" @click="workflow.notificationPage--">上一页</button><b>{{ workflow.pagedNotifications.currentPage }} / {{ workflow.pagedNotifications.totalPages }}</b><button class="secondary" :disabled="workflow.pagedNotifications.currentPage === workflow.pagedNotifications.totalPages" @click="workflow.notificationPage++">下一页</button></div></div>
  </section>

  <p v-if="!workflow.tasks.length && !workflow.expenseTasks.length && !workflow.travelTasks.length && !workflow.purchaseTasks.length && !workflow.pagedProcessedTasks.total && !workflow.notifications.length" class="empty">暂无待办、已办或通知。</p>

  <OaDialog :open="dialog.open" :title="dialogTitle" :description="dialogDescription" :submit-label="dialog.mode === 'transfer' ? '确认转办' : dialog.action === 'reject' ? '确认驳回' : '确认同意'" :busy="processing" :danger="dialog.mode === 'decision' && dialog.action === 'reject'" @close="closeDialog" @submit="submitDialog">
    <div class="operation-summary"><strong>{{ businessLabel(dialog.businessType) }}审批</strong><span>第 {{ dialog.task?.sequence }} 节点</span></div>
    <label v-if="dialog.mode === 'transfer'" class="dialog-field">转办人<select v-model="dialog.assigneeId"><option value="">请选择 ACTIVE 用户</option><option v-for="employee in transferCandidates" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.role }} · {{ employee.departmentName }}</option></select></label>
    <label class="dialog-field">{{ dialog.mode === 'transfer' ? '转办意见' : dialog.action === 'reject' ? '驳回意见' : '审批意见（可选）' }}<textarea v-model="dialog.comment" maxlength="500" :placeholder="dialog.mode === 'transfer' ? '说明转办原因和交接事项' : dialog.action === 'reject' ? '说明驳回原因，便于申请人修改' : '可填写审批意见'" /></label>
    <p v-if="dialogError" class="dialog-error">{{ dialogError }}</p>
  </OaDialog>
</template>
