<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import OaDialog from '../components/OaDialog.vue'
import type { EmploymentContractType, SaveEmploymentContract } from '../api/types'
import { useAuthStore } from '../stores/auth'
import { useContractStore } from '../stores/contracts'
import { useFileStore } from '../stores/files'
import { useOrganizationStore } from '../stores/organization'

const router = useRouter()
const auth = useAuthStore()
const contracts = useContractStore()
const organization = useOrganizationStore()
const files = useFileStore()
const createOpen = ref(false)
const uploading = ref(false)
const today = new Date().toISOString().slice(0, 10)
const nextYear = `${new Date().getFullYear() + 1}-${today.slice(5)}`
const form = reactive<SaveEmploymentContract>({ userId: '', contractType: 'FIXED_TERM', signedDate: null, startDate: today, endDate: nextYear, probationStartDate: null, probationEndDate: null, workLocation: '总部办公室', positionName: '', projectDescription: null, attachments: [], notes: null, version: 1, changeReason: '新建劳动合同草稿' })
const canManage = computed(() => auth.currentUser?.permissions?.includes('CONTRACT_MANAGE') === true)
const summaryCards = computed(() => [
  { label: '未归档书面合同', value: contracts.alertSummary.missingWrittenContract, risk: true },
  { label: '已到期', value: contracts.alertSummary.expired, risk: true },
  { label: '7 天内到期', value: contracts.alertSummary.within7Days, risk: true },
  { label: '30 天内到期', value: contracts.alertSummary.within30Days, risk: false },
  { label: '待法务评估', value: contracts.alertSummary.openEndedReviewRequired, risk: false },
  { label: '未确认预警', value: contracts.alertSummary.unacknowledged, risk: true }
])
const typeLabel = (value: string) => ({ FIXED_TERM: '固定期限', OPEN_ENDED: '无固定期限', PROJECT_BASED: '以完成任务为期限' }[value] ?? value)
const statusLabel = (value: string) => ({ DRAFT: '草稿', ACTIVE: '有效', EXPIRING: '即将到期', EXPIRED: '已到期', TERMINATED: '已终止', SUPERSEDED: '已续签替代' }[value] ?? value)

function openCreate() {
  const employee = organization.directoryEmployees[0]
  Object.assign(form, { userId: employee?.id ?? '', contractType: 'FIXED_TERM', signedDate: null, startDate: today, endDate: nextYear, probationStartDate: null, probationEndDate: null, workLocation: '总部办公室', positionName: employee?.positionName ?? '未分配岗位', projectDescription: null, attachments: [], notes: null, version: 1, changeReason: '新建劳动合同草稿' })
  contracts.error = ''; contracts.message = ''; createOpen.value = true
}
function employeeChanged() { form.positionName = organization.directoryEmployees.find(item => item.id === form.userId)?.positionName ?? '未分配岗位' }
function typeChanged() { if (form.contractType === 'OPEN_ENDED') form.endDate = null; else if (!form.endDate) form.endDate = nextYear; if (form.contractType !== 'PROJECT_BASED') form.projectDescription = null }
async function upload(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return
  uploading.value = true
  try { form.attachments.push(await files.uploadFile(file)) } catch (cause) { contracts.error = cause instanceof Error ? cause.message : '合同附件上传失败。' }
  finally { uploading.value = false; (event.target as HTMLInputElement).value = '' }
}
async function save() {
  if (!form.userId || !form.positionName.trim() || !form.workLocation.trim() || !form.changeReason.trim()) { contracts.error = '请完整填写员工、岗位、工作地点和变更说明。'; return }
  if (await contracts.create({ ...form, signedDate: form.signedDate || null, endDate: form.endDate || null, probationStartDate: form.probationStartDate || null, probationEndDate: form.probationEndDate || null, projectDescription: form.projectDescription || null, notes: form.notes || null })) createOpen.value = false
}

onMounted(async () => { await Promise.all([organization.loadOrganization(), contracts.loadContracts()]) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">EMPLOYMENT CONTRACTS</p><h1>劳动合同台账</h1><p>集中管理书面合同、续签历史、到期预警和法务评估提示。</p></div><div v-if="canManage" class="heading-actions"><button class="secondary" :disabled="contracts.saving" @click="contracts.generateDemoData">生成模拟合同</button><button class="primary-action" @click="openCreate">＋ 新建合同</button></div></div>
  <p v-if="contracts.message" class="notice success">{{ contracts.message }}</p><p v-if="contracts.error" class="notice error">{{ contracts.error }}</p>
  <section class="contract-summary"><article v-for="card in summaryCards" :key="card.label" class="panel" :class="{ 'risk-card': card.risk && card.value }"><small>{{ card.label }}</small><strong>{{ card.value }}</strong></article></section>
  <form class="filter-bar" @submit.prevent="contracts.search"><input v-model="contracts.filters.keyword" placeholder="合同编号、员工、部门或用户 ID"><select v-model="contracts.filters.departmentId"><option value="">全部部门</option><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select><select v-model="contracts.filters.contractType"><option value="">全部合同类型</option><option value="FIXED_TERM">固定期限</option><option value="OPEN_ENDED">无固定期限</option><option value="PROJECT_BASED">以完成任务为期限</option></select><select v-model="contracts.filters.status"><option value="">全部状态</option><option v-for="status in ['DRAFT','ACTIVE','EXPIRING','EXPIRED','TERMINATED','SUPERSEDED']" :key="status" :value="status">{{ statusLabel(status) }}</option></select><select v-model="contracts.filters.expiryDays"><option value="">全部到期时间</option><option value="7">7 天内</option><option value="30">30 天内</option><option value="60">60 天内</option><option value="90">90 天内</option></select><button type="submit">查询</button><button class="secondary" type="button" @click="contracts.resetFilters">重置</button></form>
  <section class="panel"><div class="section-title"><div><p class="eyebrow">CONTRACT REGISTER</p><h2>合同清单</h2></div></div><p v-if="contracts.loading" class="empty">正在加载劳动合同…</p><template v-else-if="contracts.contracts.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>合同编号 / 员工</th><th>部门 / 岗位</th><th>合同类型</th><th>合同期限</th><th>到期提示</th><th>状态</th><th>更新时间</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="item in contracts.contracts" :key="item.id"><td><strong>{{ item.employeeName }}</strong><small>{{ item.contractNumber }}</small></td><td>{{ item.departmentName }}<small>{{ item.positionName }}</small></td><td>{{ typeLabel(item.contractType) }}</td><td>{{ item.startDate }}<small>至 {{ item.endDate || '无固定期限' }}</small></td><td><span v-if="item.daysUntilEnd !== null && item.daysUntilEnd !== undefined" :class="{ 'risk-text': item.daysUntilEnd <= 30 }">{{ item.daysUntilEnd < 0 ? `已到期 ${-item.daysUntilEnd} 天` : `${item.daysUntilEnd} 天` }}</span><span v-else>—</span><small v-if="item.openEndedReviewRequired">需法务评估</small></td><td><em :class="{ archived: ['TERMINATED','SUPERSEDED'].includes(item.displayStatus), warning: ['EXPIRING','EXPIRED'].includes(item.displayStatus) }">{{ statusLabel(item.displayStatus) }}</em></td><td>{{ new Date(item.updatedAt).toLocaleString('zh-CN') }}</td><td><button class="secondary" @click="router.push(`/hr/contracts/${item.id}`)">查看详情</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ contracts.total }} 条</span><div><button class="secondary" :disabled="contracts.page === 1" @click="contracts.changePage(-1)">上一页</button><b>{{ contracts.page }} / {{ contracts.totalPages }}</b><button class="secondary" :disabled="contracts.page === contracts.totalPages" @click="contracts.changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前权限或筛选条件下暂无劳动合同。</p></section>

  <OaDialog :open="createOpen" title="新建劳动合同" description="先保存草稿；填写签订日期并上传签署件后，可在详情页激活。" submit-label="保存草稿" :busy="contracts.saving || uploading" @close="createOpen = false" @submit="save"><div class="dialog-grid"><label class="dialog-field">员工<select v-model="form.userId" @change="employeeChanged"><option v-for="employee in organization.directoryEmployees" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.departmentName }}</option></select></label><label class="dialog-field">合同类型<select v-model="form.contractType" @change="typeChanged"><option value="FIXED_TERM">固定期限</option><option value="OPEN_ENDED">无固定期限</option><option value="PROJECT_BASED">以完成任务为期限</option></select></label><label class="dialog-field">签订日期<input v-model="form.signedDate" type="date" :max="today"></label><label class="dialog-field">开始日期<input v-model="form.startDate" type="date"></label><label v-if="form.contractType !== 'OPEN_ENDED'" class="dialog-field">结束日期<input v-model="form.endDate" type="date"></label><label class="dialog-field">工作地点<input v-model="form.workLocation" maxlength="100"></label><label class="dialog-field">合同岗位<input v-model="form.positionName" maxlength="100"></label><label class="dialog-field">试用期开始<input v-model="form.probationStartDate" type="date"></label><label class="dialog-field">试用期结束<input v-model="form.probationEndDate" type="date"></label><label v-if="form.contractType === 'PROJECT_BASED'" class="dialog-field">任务说明<textarea v-model="form.projectDescription" maxlength="500"></textarea></label></div><label class="dialog-field">签署附件（最多 10 个）<input type="file" accept=".pdf,.jpg,.jpeg,.png,.doc,.docx" :disabled="uploading || form.attachments.length >= 10" @change="upload"><small>{{ uploading ? '正在上传…' : `已关联 ${form.attachments.length} 个附件` }}</small></label><label class="dialog-field">备注<textarea v-model="form.notes" maxlength="1000"></textarea></label><label class="dialog-field">建档说明<textarea v-model="form.changeReason" maxlength="500"></textarea></label></OaDialog>
</template>
