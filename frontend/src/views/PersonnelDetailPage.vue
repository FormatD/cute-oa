<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import type { EmploymentType, PersonnelProfile, PersonnelStatus, UpdatePersonnelProfile } from '../api/types'
import { useAuthStore } from '../stores/auth'
import { useOrganizationStore } from '../stores/organization'
import { usePersonnelStore } from '../stores/personnel'

const props = defineProps<{ id: string }>()
const router = useRouter()
const auth = useAuthStore()
const personnel = usePersonnelStore()
const organization = useOrganizationStore()
const editing = ref(false)
const today = new Date().toISOString().slice(0, 10)
const form = reactive<UpdatePersonnelProfile>({ departmentId: '', positionId: null, managerId: null, workEmail: '', workPhone: '', workLocation: '', employmentType: 'FULL_TIME', personnelStatus: 'ACTIVE', hireDate: today, probationEndDate: null, regularizedDate: null, cumulativeWorkStartDate: null, departureDate: null, departureReason: null, version: 1, effectiveDate: today, changeReason: '' })
const canManage = computed(() => auth.currentUser?.permissions?.includes('PERSONNEL_MANAGE') === true)
const availablePositions = computed(() => organization.positions.filter(item => item.departmentId === form.departmentId))
const managers = computed(() => organization.directoryEmployees.filter(item => item.id !== props.id))
const statusLabel = (value: string) => ({ PROBATION: '试用', ACTIVE: '在职', OFFBOARDING: '离职办理中', TERMINATED: '已离职' }[value] ?? value)
const employmentLabel = (value: string) => ({ FULL_TIME: '全职', PART_TIME: '兼职', INTERN: '实习', CONTRACTOR: '外包/顾问' }[value] ?? value)
const eventLabel = (value: string) => ({ IMPORTED: '档案初始化', PROFILE_UPDATED: '档案更新', TRANSFER: '组织调动', REGULARIZED: '转正', OFFBOARDING_STARTED: '发起离职', TERMINATED: '离职办结' }[value] ?? value)

function fill(profile: PersonnelProfile) {
  Object.assign(form, { departmentId: profile.departmentId, positionId: profile.positionId ?? null, managerId: profile.managerId ?? null, workEmail: profile.workEmail, workPhone: profile.workPhone, workLocation: profile.workLocation, employmentType: profile.employmentType as EmploymentType, personnelStatus: profile.personnelStatus as PersonnelStatus, hireDate: profile.hireDate, probationEndDate: profile.probationEndDate ?? null, regularizedDate: profile.regularizedDate ?? null, cumulativeWorkStartDate: profile.cumulativeWorkStartDate ?? null, departureDate: profile.departureDate ?? null, departureReason: profile.departureReason ?? null, version: profile.version, effectiveDate: today, changeReason: '' })
}
function startEdit() { if (personnel.detail) fill(personnel.detail); editing.value = true; personnel.error = ''; personnel.message = '' }
function departmentChanged() { if (!availablePositions.value.some(item => item.id === form.positionId)) form.positionId = null }
async function save() {
  if (!form.changeReason.trim()) { personnel.error = '请填写本次人事变更说明。'; return }
  if (await personnel.update(props.id, { ...form, positionId: form.positionId || null, managerId: form.managerId || null, probationEndDate: form.probationEndDate || null, regularizedDate: form.regularizedDate || null, cumulativeWorkStartDate: form.cumulativeWorkStartDate || null, departureDate: form.departureDate || null, departureReason: form.departureReason || null })) editing.value = false
}

onMounted(async () => { await Promise.all([organization.loadOrganization(), personnel.loadDetail(props.id)]) })
watch(() => props.id, id => { editing.value = false; void personnel.loadDetail(id) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">PERSONNEL PROFILE</p><h1>员工档案详情</h1><p>任职主数据和入转调离记录永久留痕。</p></div><div class="heading-actions"><button class="secondary" @click="router.push('/hr/employees')">← 返回花名册</button><button v-if="canManage && personnel.detail" @click="startEdit">编辑档案</button></div></div>
  <p v-if="personnel.message" class="notice success">{{ personnel.message }}</p><p v-if="personnel.error" class="notice error">{{ personnel.error }}</p><p v-if="personnel.loading" class="empty">正在加载人事档案…</p>
  <template v-else-if="personnel.detail"><section class="panel"><div class="detail-status"><strong>{{ personnel.detail.name }} · {{ personnel.detail.employeeNumber }}</strong><em :class="{ archived: personnel.detail.personnelStatus === 'TERMINATED' }">{{ statusLabel(personnel.detail.personnelStatus) }}</em></div><dl class="detail-grid"><div><dt>用户 ID</dt><dd>{{ personnel.detail.userId }}</dd></div><div><dt>账号状态</dt><dd>{{ personnel.detail.accountStatus === 'ACTIVE' ? '启用' : '停用' }}</dd></div><div><dt>所属部门</dt><dd>{{ personnel.detail.departmentName }}</dd></div><div><dt>主岗位</dt><dd>{{ personnel.detail.positionName || '未分配' }}</dd></div><div><dt>直属上级</dt><dd>{{ personnel.detail.managerName || '无' }}</dd></div><div><dt>办公地点</dt><dd>{{ personnel.detail.workLocation || '未填写' }}</dd></div><div><dt>工作邮箱</dt><dd>{{ personnel.detail.workEmail || '未填写' }}</dd></div><div><dt>工作电话</dt><dd>{{ personnel.detail.workPhone || '未填写' }}</dd></div><div><dt>用工类型</dt><dd>{{ employmentLabel(personnel.detail.employmentType) }}</dd></div><div><dt>入职日期</dt><dd>{{ personnel.detail.hireDate }}</dd></div><div><dt>试用期结束</dt><dd>{{ personnel.detail.probationEndDate || '不适用' }}</dd></div><div><dt>转正日期</dt><dd>{{ personnel.detail.regularizedDate || '未记录' }}</dd></div><div><dt>累计工作起始日</dt><dd>{{ personnel.detail.cumulativeWorkStartDate || '未记录' }}</dd></div><div><dt>社会工龄</dt><dd>{{ personnel.detail.cumulativeWorkYears }} 年</dd></div><div><dt>离职日期</dt><dd>{{ personnel.detail.departureDate || '不适用' }}</dd></div><div class="wide"><dt>离职原因</dt><dd>{{ personnel.detail.departureReason || '不适用' }}</dd></div></dl></section>
    <section class="panel"><div class="section-title"><div><p class="eyebrow">LIFECYCLE HISTORY</p><h2>员工生命周期记录</h2></div></div><div v-if="personnel.detail.events.length" class="timeline"><p v-for="event in personnel.detail.events" :key="event.id"><strong>{{ event.effectiveDate }} · {{ eventLabel(event.eventType) }}</strong><span>{{ event.summary }}</span><small>{{ event.changedByName }} · {{ new Date(event.createdAt).toLocaleString('zh-CN') }}</small></p></div><p v-else class="empty">暂无变更历史。</p></section>
  </template>
  <section v-if="editing && personnel.detail" class="panel user-editor"><div class="section-title"><div><p class="eyebrow">HR EDITOR</p><h2>编辑档案 · {{ personnel.detail.name }}</h2></div><button class="secondary" @click="editing = false">关闭</button></div><form class="user-form" @submit.prevent="save"><label>工号<input :value="personnel.detail.employeeNumber" disabled><small>工号创建后不可修改。</small></label><label>用工类型<select v-model="form.employmentType"><option value="FULL_TIME">全职</option><option value="PART_TIME">兼职</option><option value="INTERN">实习</option><option value="CONTRACTOR">外包/顾问</option></select></label><label>人事状态<select v-model="form.personnelStatus"><option value="PROBATION">试用</option><option value="ACTIVE">在职</option><option value="OFFBOARDING">离职办理中</option><option value="TERMINATED">已离职</option></select></label><label>入职日期<input v-model="form.hireDate" type="date"></label><label>所属部门<select v-model="form.departmentId" @change="departmentChanged"><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select></label><label>主岗位<select v-model="form.positionId"><option :value="null">未分配</option><option v-for="position in availablePositions" :key="position.id" :value="position.id">{{ position.name }}</option></select></label><label>直属上级<select v-model="form.managerId"><option :value="null">无</option><option v-for="manager in managers" :key="manager.id" :value="manager.id">{{ manager.name }} · {{ manager.positionName || manager.role }}</option></select></label><label>办公地点<input v-model="form.workLocation" maxlength="100"></label><label>工作邮箱<input v-model="form.workEmail" maxlength="128" type="email"></label><label>工作电话<input v-model="form.workPhone" maxlength="32"></label><label>试用期结束日期<input v-model="form.probationEndDate" type="date"></label><label>转正日期<input v-model="form.regularizedDate" type="date"></label><label>累计工作起始日期<input v-model="form.cumulativeWorkStartDate" type="date"><small>用于计算社会工龄和后续年假配额。</small></label><label>离职日期<input v-model="form.departureDate" type="date"></label><label class="wide">离职原因<textarea v-model="form.departureReason" maxlength="500"></textarea></label><label>变更生效日期<input v-model="form.effectiveDate" type="date"></label><label class="wide">本次变更说明<textarea v-model="form.changeReason" maxlength="500" placeholder="例如：试用期考核通过，2026-09-01 起转正"></textarea></label><div class="form-actions wide"><button :disabled="personnel.saving" type="submit">{{ personnel.saving ? '保存中…' : '保存并记录历史' }}</button></div></form></section>
</template>
