<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import OaDialog from '../components/OaDialog.vue'
import type { PersonnelCaseTask, PersonnelTaskStatus, UpdatePersonnelCaseTask } from '../api/types'
import { useAuthStore } from '../stores/auth'
import { useOrganizationStore } from '../stores/organization'
import { usePersonnelCaseStore } from '../stores/personnel-cases'

const props = defineProps<{ id: string }>()
const router = useRouter()
const auth = useAuthStore()
const organization = useOrganizationStore()
const cases = usePersonnelCaseStore()
const taskOpen = ref(false)
const completeOpen = ref(false)
const cancelOpen = ref(false)
const selectedTask = ref<PersonnelCaseTask | null>(null)
const actionText = ref('')
const canManage = computed(() => auth.currentUser?.permissions?.includes('PERSONNEL_MANAGE') === true)
const form = reactive<UpdatePersonnelCaseTask>({ assigneeId: '', dueDate: '', status: 'COMPLETED', completionNote: null, version: 1 })
const typeLabel = (value: string) => ({ ONBOARDING: '入职', REGULARIZATION: '转正', TRANSFER: '调动', OFFBOARDING: '离职' }[value] ?? value)
const statusLabel = (value: string) => ({ OPEN: '办理中', COMPLETED: '已完成', CANCELLED: '已取消', PENDING: '待处理', WAIVED: '已豁免' }[value] ?? value)
const categoryLabel = (value: string) => ({ HR: '人事', IT: '信息技术', ADMIN: '行政资产', FINANCE: '财务', MANAGER: '直属上级', EMPLOYEE: '员工本人' }[value] ?? value)
const canEditTask = (task: PersonnelCaseTask) => cases.detail?.status === 'OPEN' && (canManage.value || task.assigneeId === auth.currentUser?.id)

function openTask(task: PersonnelCaseTask) {
  selectedTask.value = task
  Object.assign(form, { assigneeId: task.assigneeId, dueDate: task.dueDate, status: (canManage.value ? task.status : 'COMPLETED') as PersonnelTaskStatus, completionNote: task.completionNote ?? null, version: task.version })
  cases.error = ''; taskOpen.value = true
}
async function saveTask() {
  if (!selectedTask.value) return
  if (form.status !== 'PENDING' && (form.completionNote?.trim().length ?? 0) < (form.status === 'WAIVED' ? 5 : 2)) { cases.error = form.status === 'WAIVED' ? '豁免原因至少 5 个字符。' : '完成说明至少 2 个字符。'; return }
  if (await cases.updateTask(props.id, selectedTask.value.id, { ...form, completionNote: form.status === 'PENDING' ? null : form.completionNote?.trim() || null })) taskOpen.value = false
}
function openAction(action: 'complete' | 'cancel') { actionText.value = ''; cases.error = ''; if (action === 'complete') completeOpen.value = true; else cancelOpen.value = true }
async function complete() { if (!cases.detail || actionText.value.trim().length < 5) { cases.error = '办结说明至少 5 个字符。'; return }; if (await cases.complete(props.id, cases.detail.version, actionText.value.trim())) completeOpen.value = false }
async function cancel() { if (!cases.detail || actionText.value.trim().length < 5) { cases.error = '取消原因至少 5 个字符。'; return }; if (await cases.cancel(props.id, cases.detail.version, actionText.value.trim())) cancelOpen.value = false }

onMounted(async () => { await Promise.all([organization.loadOrganization(), cases.loadDetail(props.id)]) })
watch(() => props.id, id => { void cases.loadDetail(id) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">PERSONNEL CASE DETAIL</p><h1>员工办理单详情</h1><p>任务处理、改派、豁免和办结均保留责任人与时间记录。</p></div><div class="heading-actions"><button class="secondary" @click="router.push('/hr/personnel-cases')">← 返回办理台账</button><button v-if="canManage && cases.detail?.status === 'OPEN'" class="secondary" @click="openAction('cancel')">取消办理单</button><button v-if="canManage && cases.detail?.status === 'OPEN'" @click="openAction('complete')">确认办结</button></div></div>
  <p v-if="cases.message" class="notice success">{{ cases.message }}</p><p v-if="cases.error" class="notice error">{{ cases.error }}</p><p v-if="cases.loading" class="panel empty">正在加载员工办理单…</p>
  <template v-else-if="cases.detail"><section class="panel"><div class="detail-status"><strong>{{ cases.detail.title }}</strong><em :class="{ archived: cases.detail.status !== 'OPEN', warning: cases.detail.overdueTaskCount > 0 }">{{ statusLabel(cases.detail.status) }}</em></div><dl class="detail-grid"><div><dt>办理单号</dt><dd>{{ cases.detail.number }}</dd></div><div><dt>办理类型</dt><dd>{{ typeLabel(cases.detail.type) }}</dd></div><div><dt>员工</dt><dd><button class="text-link" @click="router.push(`/hr/employees/${cases.detail?.userId}`)">{{ cases.detail.employeeName }}</button></dd></div><div><dt>部门</dt><dd>{{ cases.detail.departmentName }}</dd></div><div><dt>生效日期</dt><dd>{{ cases.detail.effectiveDate }}</dd></div><div><dt>总负责人</dt><dd>{{ cases.detail.ownerName }}</dd></div><div><dt>任务进度</dt><dd>{{ cases.detail.resolvedTaskCount }} / {{ cases.detail.taskCount }}</dd></div><div><dt>逾期任务</dt><dd :class="{ 'risk-text': cases.detail.overdueTaskCount > 0 }">{{ cases.detail.overdueTaskCount }} 项</dd></div><div class="wide"><dt>办理说明</dt><dd>{{ cases.detail.notes || '无' }}</dd></div><div v-if="cases.detail.completionComment" class="wide"><dt>办结记录</dt><dd>{{ cases.detail.completionComment }} · {{ cases.detail.completedByName }} · {{ cases.detail.completedAt && new Date(cases.detail.completedAt).toLocaleString('zh-CN') }}</dd></div><div v-if="cases.detail.cancellationReason" class="wide"><dt>取消记录</dt><dd>{{ cases.detail.cancellationReason }} · {{ cases.detail.cancelledByName }} · {{ cases.detail.cancelledAt && new Date(cases.detail.cancelledAt).toLocaleString('zh-CN') }}</dd></div></dl></section>
    <section class="panel"><div class="section-title"><div><p class="eyebrow">CHECKLIST TASKS</p><h2>办理任务</h2></div></div><div class="table-wrap"><table class="data-table personnel-task-table"><thead><tr><th>任务</th><th>责任类别</th><th>负责人</th><th>截止日期</th><th>状态</th><th>完成记录</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="task in cases.detail.tasks" :key="task.id"><td><strong>{{ task.title }}</strong><small>{{ task.required ? '必办' : '可选' }} · {{ task.code }}</small></td><td>{{ categoryLabel(task.category) }}</td><td>{{ task.assigneeName }}</td><td :class="{ 'risk-text': task.isOverdue }">{{ task.dueDate }}<small v-if="task.isOverdue">已逾期</small></td><td><em :class="{ archived: task.status !== 'PENDING', warning: task.isOverdue }">{{ statusLabel(task.status) }}</em></td><td><span>{{ task.completionNote || '—' }}</span><small v-if="task.completedAt">{{ task.completedByName }} · {{ new Date(task.completedAt).toLocaleString('zh-CN') }}</small></td><td><button v-if="canEditTask(task)" class="secondary" @click="openTask(task)">{{ task.status === 'PENDING' ? '处理' : '调整' }}</button><span v-else class="muted">只读</span></td></tr></tbody></table></div></section>
  </template>

  <OaDialog :open="taskOpen" :title="selectedTask?.title || '处理任务'" description="任务完成、豁免、重开和改派都会写入审计；非 HR 只能完成分配给自己的任务。" submit-label="保存任务" :busy="cases.saving" @close="taskOpen = false" @submit="saveTask"><div class="dialog-grid"><label class="dialog-field">负责人<select v-model="form.assigneeId" :disabled="!canManage"><option v-for="employee in organization.directoryEmployees" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.departmentName }}</option></select></label><label class="dialog-field">截止日期<input v-model="form.dueDate" type="date" :disabled="!canManage"></label><label class="dialog-field">任务状态<select v-model="form.status"><option v-if="canManage" value="PENDING">待处理 / 重新打开</option><option value="COMPLETED">已完成</option><option v-if="canManage" value="WAIVED">已豁免</option></select></label></div><label v-if="form.status !== 'PENDING'" class="dialog-field">{{ form.status === 'WAIVED' ? '豁免原因' : '完成说明' }}<textarea v-model="form.completionNote" maxlength="500"></textarea></label></OaDialog>
  <OaDialog :open="completeOpen" title="确认办结员工办理单" description="所有任务必须已完成或有理由豁免。办结后不能再修改任务。" submit-label="确认办结" :busy="cases.saving" @close="completeOpen = false" @submit="complete"><label class="dialog-field">办结说明<textarea v-model="actionText" maxlength="500" placeholder="至少 5 个字符"></textarea></label></OaDialog>
  <OaDialog :open="cancelOpen" title="取消员工办理单" description="取消后保留任务和审计记录，不能继续处理。" submit-label="确认取消" :busy="cases.saving" danger @close="cancelOpen = false" @submit="cancel"><label class="dialog-field">取消原因<textarea v-model="actionText" maxlength="500" placeholder="至少 5 个字符"></textarea></label></OaDialog>
</template>
