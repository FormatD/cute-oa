<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import OaDialog from '../components/OaDialog.vue'
import type { AttendanceImportItem, AttendanceShift } from '../api/types'
import { useAttendanceStore } from '../stores/attendance'
import { useAuthStore } from '../stores/auth'
import { useOrganizationStore } from '../stores/organization'

const router = useRouter()
const attendance = useAttendanceStore()
const auth = useAuthStore()
const organization = useOrganizationStore()
const importOpen = ref(false)
const shiftOpen = ref(false)
const lockOpen = ref(false)
const lockAction = ref<'lock' | 'unlock'>('lock')
const lockReason = ref('')
const importForm = reactive({ userId: '', workDate: new Date().toISOString().slice(0, 10), checkInTime: '09:00', checkOutTime: '18:00' })
const shiftForm = reactive({ id: '', code: '', name: '', workStart: '', workEnd: '', breakMinutes: 60, lateToleranceMinutes: 5, earlyLeaveToleranceMinutes: 5, isDefault: true, isEnabled: true, version: 1 })
const canManage = computed(() => auth.currentUser?.permissions?.includes('ATTENDANCE_MANAGE') === true)
const isLocked = computed(() => attendance.monthLock?.isLocked === true)
const totals = computed(() => attendance.summaries.reduce((result, item) => ({ scheduledDays: result.scheduledDays + item.scheduledDays, attendedDays: result.attendedDays + item.attendedDays, anomalyDays: result.anomalyDays + item.lateDays + item.earlyLeaveDays + item.missingPunchDays + item.absentDays, pendingAppeals: result.pendingAppeals + item.pendingAppeals }), { scheduledDays: 0, attendedDays: 0, anomalyDays: 0, pendingAppeals: 0 }))
const statusLabel = (value: string) => ({ NORMAL: '正常', LATE: '迟到', EARLY_LEAVE: '早退', LATE_AND_EARLY: '迟到且早退', MISSING_PUNCH: '缺卡', ABSENT: '旷工', LEAVE: '请假', REST_DAY: '休息日', CORRECTED: '已修正' }[value] ?? value)
const timeLabel = (value?: string | null) => value ? new Date(value).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false }) : '—'
const abnormal = (status: string) => ['LATE', 'EARLY_LEAVE', 'LATE_AND_EARLY', 'MISSING_PUNCH', 'ABSENT'].includes(status)

function openImport() {
  Object.assign(importForm, { userId: organization.directoryEmployees[0]?.id ?? auth.currentUser?.id ?? '', workDate: new Date().toISOString().slice(0, 10), checkInTime: '09:00', checkOutTime: '18:00' })
  importOpen.value = true
}
function openShift(shift: AttendanceShift) { Object.assign(shiftForm, shift); shiftOpen.value = true }
function chinaDateTime(date: string, time: string) { return time ? `${date}T${time}:00+08:00` : null }
async function saveImport() {
  const payload: AttendanceImportItem = { userId: importForm.userId, workDate: importForm.workDate, checkInAt: chinaDateTime(importForm.workDate, importForm.checkInTime), checkOutAt: chinaDateTime(importForm.workDate, importForm.checkOutTime) }
  if (await attendance.importRecord(payload)) importOpen.value = false
}
async function saveShift() {
  const { id, ...payload } = shiftForm
  if (await attendance.updateShift(id, payload)) shiftOpen.value = false
}
function openMonthLock(action: 'lock' | 'unlock') {
  lockAction.value = action
  lockReason.value = ''
  lockOpen.value = true
}
async function saveMonthLock() {
  if (await attendance.changeMonthLock(lockAction.value === 'lock', lockReason.value)) lockOpen.value = false
}

onMounted(async () => { await Promise.all([organization.loadOrganization(), attendance.loadRecords()]) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">ATTENDANCE MANAGEMENT</p><h1>考勤管理</h1><p>查看班次、每日打卡、异常状态与月度汇总。</p></div><div v-if="canManage" class="heading-actions"><button v-if="isLocked" class="secondary" @click="openMonthLock('unlock')">解封本月</button><button v-else class="secondary" :disabled="!attendance.records.length" @click="openMonthLock('lock')">封账本月</button><button class="secondary" :disabled="isLocked" @click="attendance.generateDemoData">生成本月模拟数据</button><button class="primary-action" :disabled="isLocked" @click="openImport">＋ 录入考勤</button></div></div>
  <p v-if="attendance.message" class="notice success">{{ attendance.message }}</p><p v-if="attendance.error" class="notice error">{{ attendance.error }}</p>
  <section v-if="attendance.monthLock" class="attendance-lock" :class="{ locked: isLocked }"><strong>{{ attendance.filters.month.slice(0, 7) }} {{ isLocked ? '已封账' : '未封账' }}</strong><span v-if="isLocked">{{ attendance.monthLock.lockedByName }} 于 {{ new Date(attendance.monthLock.lockedAt!).toLocaleString('zh-CN') }} 完成封账 · {{ attendance.monthLock.lockReason }}</span><span v-else-if="attendance.monthLock.unlockedAt">最近由 {{ attendance.monthLock.unlockedByName }} 于 {{ new Date(attendance.monthLock.unlockedAt).toLocaleString('zh-CN') }} 解封 · {{ attendance.monthLock.unlockReason }}</span><span v-else>HR 完成异常申诉后可封账，封账后禁止导入、修改和申诉。</span></section>
  <section class="attendance-summary"><article class="panel"><small>应出勤人次</small><strong>{{ totals.scheduledDays }}</strong></article><article class="panel"><small>实际打卡人次</small><strong>{{ totals.attendedDays }}</strong></article><article class="panel"><small>异常项</small><strong>{{ totals.anomalyDays }}</strong></article><article class="panel"><small>待审申诉</small><strong>{{ totals.pendingAppeals }}</strong></article></section>
  <form class="filter-bar" @submit.prevent="attendance.search"><label>月份<input :value="attendance.filters.month.slice(0, 7)" type="month" @change="attendance.setMonth(($event.target as HTMLInputElement).value)"></label><input v-model="attendance.filters.keyword" placeholder="姓名、用户 ID 或部门"><select v-model="attendance.filters.userId"><option value="">全部员工</option><option v-for="employee in organization.directoryEmployees" :key="employee.id" :value="employee.id">{{ employee.name }}</option></select><select v-model="attendance.filters.departmentId"><option value="">全部部门</option><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select><select v-model="attendance.filters.status"><option value="">全部状态</option><option v-for="status in ['NORMAL','LATE','EARLY_LEAVE','LATE_AND_EARLY','MISSING_PUNCH','ABSENT','LEAVE','REST_DAY','CORRECTED']" :key="status" :value="status">{{ statusLabel(status) }}</option></select><button type="submit">查询</button><button class="secondary" type="button" @click="attendance.resetFilters">重置</button></form>
  <section v-if="attendance.shifts.length" class="panel"><div class="section-title"><div><p class="eyebrow">SHIFT RULE</p><h2>当前默认班次</h2></div><button v-if="canManage" class="secondary" @click="openShift(attendance.shifts[0])">编辑班次</button></div><div class="shift-overview"><strong>{{ attendance.shifts[0].name }}</strong><span>{{ attendance.shifts[0].workStart.slice(0, 5) }}–{{ attendance.shifts[0].workEnd.slice(0, 5) }}</span><small>午休 {{ attendance.shifts[0].breakMinutes }} 分钟 · 迟到宽限 {{ attendance.shifts[0].lateToleranceMinutes }} 分钟 · 早退宽限 {{ attendance.shifts[0].earlyLeaveToleranceMinutes }} 分钟</small></div></section>
  <section class="panel"><div class="section-title"><div><p class="eyebrow">DAILY RECORDS</p><h2>每日考勤记录</h2></div></div><p v-if="attendance.loading" class="empty">正在加载考勤记录…</p><template v-else-if="attendance.records.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>日期</th><th>员工</th><th>部门</th><th>班次</th><th>上班打卡</th><th>下班打卡</th><th>工作时长</th><th>状态</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="record in attendance.records" :key="record.id"><td>{{ record.workDate }}</td><td><strong>{{ record.employeeName }}</strong><small>{{ record.userId }}</small></td><td>{{ record.departmentName }}</td><td>{{ record.shiftName }}</td><td>{{ timeLabel(record.checkInAt) }}</td><td>{{ timeLabel(record.checkOutAt) }}</td><td>{{ Math.floor(record.workedMinutes / 60) }}时{{ record.workedMinutes % 60 }}分</td><td><em :class="{ archived: record.status === 'REST_DAY', warning: abnormal(record.status) }">{{ statusLabel(record.status) }}</em></td><td><button class="secondary" @click="router.push(`/attendance/${record.id}`)">查看详情</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ attendance.total }} 条</span><div><button class="secondary" :disabled="attendance.page === 1" @click="attendance.changePage(-1)">上一页</button><b>{{ attendance.page }} / {{ attendance.totalPages }}</b><button class="secondary" :disabled="attendance.page === attendance.totalPages" @click="attendance.changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前月份暂无考勤记录；HR 可生成不覆盖已有数据的模拟考勤。</p></section>

  <OaDialog :open="importOpen" title="录入考勤" description="同一员工同一日期再次录入将更新原记录。留空某个时间可模拟缺卡，全部留空可记录旷工。" submit-label="保存记录" :busy="attendance.saving" @close="importOpen = false" @submit="saveImport"><div class="dialog-grid"><label class="dialog-field">员工<select v-model="importForm.userId"><option v-for="employee in organization.directoryEmployees" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.departmentName }}</option></select></label><label class="dialog-field">考勤日期<input v-model="importForm.workDate" type="date"></label><label class="dialog-field">上班打卡<input v-model="importForm.checkInTime" type="time"></label><label class="dialog-field">下班打卡<input v-model="importForm.checkOutTime" type="time"></label></div></OaDialog>
  <OaDialog :open="shiftOpen" title="编辑默认班次" description="修改只影响后续导入或生成的记录，历史记录保留原班次快照。" submit-label="保存班次" :busy="attendance.saving" @close="shiftOpen = false" @submit="saveShift"><div class="dialog-grid"><label class="dialog-field">班次编码<input v-model="shiftForm.code" maxlength="32"></label><label class="dialog-field">班次名称<input v-model="shiftForm.name" maxlength="100"></label><label class="dialog-field">上班时间<input v-model="shiftForm.workStart" type="time"></label><label class="dialog-field">下班时间<input v-model="shiftForm.workEnd" type="time"></label><label class="dialog-field">休息分钟<input v-model.number="shiftForm.breakMinutes" type="number" min="0" max="240"></label><label class="dialog-field">迟到宽限<input v-model.number="shiftForm.lateToleranceMinutes" type="number" min="0" max="120"></label><label class="dialog-field">早退宽限<input v-model.number="shiftForm.earlyLeaveToleranceMinutes" type="number" min="0" max="120"></label></div></OaDialog>
  <OaDialog :open="lockOpen" :title="lockAction === 'lock' ? '封账本月考勤' : '解封本月考勤'" :description="lockAction === 'lock' ? '封账前请确认本月异常和申诉已全部处理；封账后所有考勤变更都会被服务端拒绝。' : '解封属于敏感操作，系统将记录操作者、时间和原因。'" :submit-label="lockAction === 'lock' ? '确认封账' : '确认解封'" :busy="attendance.saving" :danger="lockAction === 'unlock'" @close="lockOpen = false" @submit="saveMonthLock"><label class="dialog-field">{{ lockAction === 'lock' ? '封账说明' : '解封原因' }}<textarea v-model="lockReason" maxlength="500" minlength="5" :placeholder="lockAction === 'lock' ? '例如：本月申诉已处理完毕，月报核对完成' : '说明为什么需要重新开放历史考勤'" /></label></OaDialog>
</template>
