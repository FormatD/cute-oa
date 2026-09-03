<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import OaDialog from '../components/OaDialog.vue'
import type { CreatePersonnelCase, PersonnelCaseType } from '../api/types'
import { useAuthStore } from '../stores/auth'
import { useOrganizationStore } from '../stores/organization'
import { usePersonnelCaseStore } from '../stores/personnel-cases'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const organization = useOrganizationStore()
const cases = usePersonnelCaseStore()
const createOpen = ref(false)
const today = new Date().toISOString().slice(0, 10)
const canManage = computed(() => auth.currentUser?.permissions?.includes('PERSONNEL_MANAGE') === true)
const form = reactive<CreatePersonnelCase>({ userId: '', type: 'ONBOARDING', effectiveDate: today, ownerId: '', notes: null })
const typeLabel = (value: string) => ({ ONBOARDING: '入职', REGULARIZATION: '转正', TRANSFER: '调动', OFFBOARDING: '离职' }[value] ?? value)
const statusLabel = (value: string) => ({ OPEN: '办理中', COMPLETED: '已办结', CANCELLED: '已取消' }[value] ?? value)

function openCreate() {
  Object.assign(form, { userId: organization.directoryEmployees[0]?.id ?? '', type: 'ONBOARDING' as PersonnelCaseType, effectiveDate: today, ownerId: auth.currentUser?.id ?? '', notes: null })
  cases.error = ''; createOpen.value = true
}
async function save() {
  if (!form.userId || !form.ownerId) { cases.error = '请选择员工和办理负责人。'; return }
  if (await cases.create({ ...form, notes: form.notes?.trim() || null })) {
    createOpen.value = false
    if (cases.detail) await router.push(`/hr/personnel-cases/${cases.detail.id}`)
  }
}

onMounted(async () => {
  const userId = typeof route.query.userId === 'string' ? route.query.userId : ''
  if (userId) cases.filters.userId = userId
  await Promise.all([organization.loadOrganization(), cases.loadCases()])
})
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">EMPLOYEE OPERATIONS</p><h1>员工办理清单</h1><p>统一追踪入职、转正、调动和离职任务，明确责任人、截止时间与办理证据。</p></div><button v-if="canManage" class="primary-action" @click="openCreate">＋ 新建办理单</button></div>
  <p v-if="cases.message" class="notice success">{{ cases.message }}</p><p v-if="cases.error" class="notice error">{{ cases.error }}</p>
  <form class="filter-bar" @submit.prevent="cases.search"><input v-model="cases.filters.keyword" placeholder="单号、员工、部门或标题"><select v-model="cases.filters.type"><option value="">全部类型</option><option value="ONBOARDING">入职</option><option value="REGULARIZATION">转正</option><option value="TRANSFER">调动</option><option value="OFFBOARDING">离职</option></select><select v-model="cases.filters.status"><option value="">全部状态</option><option value="OPEN">办理中</option><option value="COMPLETED">已办结</option><option value="CANCELLED">已取消</option></select><label class="inline-check"><input v-model="cases.filters.assignedToMe" type="checkbox">只看我的任务</label><button type="submit">查询</button><button class="secondary" type="button" @click="cases.resetFilters">重置</button></form>
  <section class="panel"><div class="section-title"><div><p class="eyebrow">CASE REGISTER</p><h2>办理单台账</h2></div></div><p v-if="cases.loading" class="empty">正在加载员工办理清单…</p><template v-else-if="cases.cases.length"><div class="table-wrap"><table class="data-table personnel-case-table"><thead><tr><th>单号 / 员工</th><th>类型</th><th>生效日期</th><th>负责人</th><th>任务进度</th><th>逾期</th><th>状态</th><th>更新时间</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="item in cases.cases" :key="item.id"><td><strong>{{ item.employeeName }}</strong><small>{{ item.number }} · {{ item.departmentName }}</small></td><td>{{ typeLabel(item.type) }}</td><td>{{ item.effectiveDate }}</td><td>{{ item.ownerName }}</td><td><strong>{{ item.resolvedTaskCount }} / {{ item.taskCount }}</strong><progress :max="item.taskCount" :value="item.resolvedTaskCount"></progress></td><td><span :class="{ 'risk-text': item.overdueTaskCount > 0 }">{{ item.overdueTaskCount ? `${item.overdueTaskCount} 项` : '无' }}</span></td><td><em :class="{ archived: item.status !== 'OPEN', warning: item.overdueTaskCount > 0 }">{{ statusLabel(item.status) }}</em></td><td>{{ new Date(item.updatedAt).toLocaleString('zh-CN') }}</td><td><button class="secondary" @click="router.push(`/hr/personnel-cases/${item.id}`)">查看详情</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ cases.total }} 条</span><div><button class="secondary" :disabled="cases.page === 1" @click="cases.changePage(-1)">上一页</button><b>{{ cases.page }} / {{ cases.totalPages }}</b><button class="secondary" :disabled="cases.page === cases.totalPages" @click="cases.changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前权限或筛选条件下暂无员工办理单。</p></section>

  <OaDialog :open="createOpen" title="新建员工办理单" description="系统会按员工本人、直属上级及 HR、财务、IT、行政职责自动分派标准任务；创建后仍可逐项改派和调整截止日期。" submit-label="创建并分派" :busy="cases.saving" @close="createOpen = false" @submit="save"><div class="dialog-grid"><label class="dialog-field">员工<select v-model="form.userId"><option v-for="employee in organization.directoryEmployees" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.departmentName }}</option></select></label><label class="dialog-field">办理类型<select v-model="form.type"><option value="ONBOARDING">入职</option><option value="REGULARIZATION">转正</option><option value="TRANSFER">调动</option><option value="OFFBOARDING">离职</option></select></label><label class="dialog-field">生效日期<input v-model="form.effectiveDate" type="date"></label><label class="dialog-field">总负责人<select v-model="form.ownerId"><option v-for="employee in organization.directoryEmployees" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.departmentName }}</option></select></label></div><label class="dialog-field">办理说明<textarea v-model="form.notes" maxlength="1000" placeholder="说明办理背景、依据或特殊要求"></textarea></label></OaDialog>
</template>
