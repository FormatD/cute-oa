<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAttendanceStore } from '../stores/attendance'
import { useAuthStore } from '../stores/auth'
import { useFileStore } from '../stores/files'

const props = defineProps<{ id: string }>()
const router = useRouter()
const attendance = useAttendanceStore()
const auth = useAuthStore()
const files = useFileStore()
const reason = ref('')
const attachments = ref<string[]>([])
const reviewComments = ref<Record<string, string>>({})
const uploading = ref(false)
const canManage = computed(() => auth.currentUser?.permissions?.includes('ATTENDANCE_MANAGE') === true)
const isOwner = computed(() => attendance.detail?.userId === auth.currentUser?.id)
const isLocked = computed(() => attendance.monthLock?.isLocked === true)
const canAppeal = computed(() => !isLocked.value && isOwner.value && attendance.detail && ['LATE', 'EARLY_LEAVE', 'LATE_AND_EARLY', 'MISSING_PUNCH', 'ABSENT'].includes(attendance.detail.status) && !attendance.detail.appeals.some(item => item.status === 'PENDING'))
const statusLabel = (value: string) => ({ NORMAL: '正常', LATE: '迟到', EARLY_LEAVE: '早退', LATE_AND_EARLY: '迟到且早退', MISSING_PUNCH: '缺卡', ABSENT: '旷工', LEAVE: '请假', REST_DAY: '休息日', CORRECTED: '已修正', PENDING: '待审核', APPROVED: '已通过', REJECTED: '已驳回' }[value] ?? value)
const dateTime = (value?: string | null) => value ? new Date(value).toLocaleString('zh-CN') : '—'

async function upload(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return
  uploading.value = true
  try { attachments.value.push(await files.uploadFile(file)) } finally { uploading.value = false; (event.target as HTMLInputElement).value = '' }
}
async function submitAppeal() { if (await attendance.submitAppeal(props.id, reason.value, attachments.value)) { reason.value = ''; attachments.value = [] } }
async function review(id: string, approved: boolean) { await attendance.reviewAppeal(props.id, id, approved, reviewComments.value[id] ?? '') }

onMounted(() => { void attendance.loadDetail(props.id) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">ATTENDANCE DETAIL</p><h1>考勤详情</h1><p>查看原始打卡、异常计算和申诉处理记录。</p></div><button class="secondary" @click="router.push('/attendance')">← 返回考勤列表</button></div>
  <p v-if="attendance.message" class="notice success">{{ attendance.message }}</p><p v-if="attendance.error" class="notice error">{{ attendance.error }}</p><p v-if="attendance.loading" class="panel empty">正在加载考勤详情…</p>
  <template v-else-if="attendance.detail"><section v-if="isLocked" class="attendance-lock locked"><strong>{{ attendance.detail.workDate.slice(0, 7) }} 已封账</strong><span>该记录已进入锁定月报，不能再提交或审核申诉。</span></section><section class="panel"><div class="detail-status"><strong>{{ attendance.detail.employeeName }} · {{ attendance.detail.workDate }}</strong><em :class="{ holiday: attendance.detail.status === 'ABSENT' }">{{ statusLabel(attendance.detail.status) }}</em></div><dl class="detail-grid"><div><dt>所属部门</dt><dd>{{ attendance.detail.departmentName }}</dd></div><div><dt>数据来源</dt><dd>{{ attendance.detail.source }}</dd></div><div><dt>班次</dt><dd>{{ attendance.detail.shiftName }}（{{ attendance.detail.scheduledStart.slice(0,5) }}–{{ attendance.detail.scheduledEnd.slice(0,5) }}）</dd></div><div><dt>记录版本</dt><dd>v{{ attendance.detail.version }}</dd></div><div><dt>上班打卡</dt><dd>{{ dateTime(attendance.detail.checkInAt) }}</dd></div><div><dt>下班打卡</dt><dd>{{ dateTime(attendance.detail.checkOutAt) }}</dd></div><div><dt>工作时长</dt><dd>{{ Math.floor(attendance.detail.workedMinutes / 60) }} 小时 {{ attendance.detail.workedMinutes % 60 }} 分</dd></div><div><dt>异常分钟</dt><dd>迟到 {{ attendance.detail.lateMinutes }} 分 · 早退 {{ attendance.detail.earlyLeaveMinutes }} 分</dd></div><div v-if="attendance.detail.originalStatus" class="wide"><dt>原始异常</dt><dd>{{ statusLabel(attendance.detail.originalStatus) }}（申诉通过后仍保留）</dd></div></dl></section>
    <section class="panel"><div class="section-title"><div><p class="eyebrow">APPEAL HISTORY</p><h2>异常申诉</h2></div></div><div v-if="attendance.detail.appeals.length" class="timeline"><article v-for="appeal in attendance.detail.appeals" :key="appeal.id"><p><strong>{{ appeal.submittedByName }}</strong> · {{ dateTime(appeal.submittedAt) }} <em :class="{ archived: appeal.status === 'REJECTED' }">{{ statusLabel(appeal.status) }}</em></p><p>{{ appeal.reason }}</p><p v-if="appeal.attachments.length"><button v-for="fileId in appeal.attachments" :key="fileId" class="secondary attachment-button" @click="files.downloadAttachment(fileId, 'attendance', attendance.detail!.id)">下载附件 {{ fileId.slice(0, 8) }}</button></p><p v-if="appeal.reviewComment" class="muted">审核人：{{ appeal.reviewedByName }} · {{ appeal.reviewComment }} · {{ dateTime(appeal.reviewedAt) }}</p><div v-if="canManage && !isLocked && appeal.status === 'PENDING'" class="appeal-review"><textarea v-model="reviewComments[appeal.id]" maxlength="500" placeholder="填写审核意见"></textarea><button :disabled="attendance.saving" @click="review(appeal.id, true)">通过并修正</button><button class="danger-outline" :disabled="attendance.saving" @click="review(appeal.id, false)">驳回</button></div></article></div><p v-else class="empty">暂无申诉记录。</p></section>
    <section v-if="canAppeal" class="panel"><div class="section-title"><div><p class="eyebrow">NEW APPEAL</p><h2>提交异常申诉</h2></div></div><label class="dialog-field">申诉原因<textarea v-model="reason" maxlength="500" placeholder="请说明实际出勤情况和需要修正的原因（至少 5 个字符）"></textarea></label><label class="dialog-field">证明附件（最多 5 个）<input type="file" accept=".pdf,.jpg,.jpeg,.png,.doc,.docx,.xls,.xlsx" :disabled="uploading || attachments.length >= 5" @change="upload"><small>{{ uploading ? '正在上传…' : attachments.length ? `已上传 ${attachments.length} 个附件` : '可选，支持 PDF、图片和 Office 文档' }}</small></label><div class="detail-actions"><button :disabled="attendance.saving || uploading" @click="submitAppeal">提交申诉</button></div></section>
  </template>
</template>
