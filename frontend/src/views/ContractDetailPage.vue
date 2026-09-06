<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import OaDialog from '../components/OaDialog.vue'
import type { EmploymentContract, EmploymentContractType, SaveEmploymentContract } from '../api/types'
import { useAuthStore } from '../stores/auth'
import { useContractStore } from '../stores/contracts'
import { useFileStore } from '../stores/files'
import { useEffectiveConfigurationStore } from '../stores/effective-configurations'

const props = defineProps<{ id: string }>()
const router = useRouter()
const auth = useAuthStore()
const contracts = useContractStore()
const files = useFileStore()
const effectiveConfig = useEffectiveConfigurationStore()
const editorOpen = ref(false)
const editorMode = ref<'edit' | 'renew'>('edit')
const activateOpen = ref(false)
const terminateOpen = ref(false)
const uploading = ref(false)
const today = new Date().toISOString().slice(0, 10)
const action = reactive({ reason: '', terminationDate: today })
const form = reactive<SaveEmploymentContract>({ userId: '', contractType: 'FIXED_TERM', signedDate: null, startDate: today, endDate: null, probationStartDate: null, probationEndDate: null, workLocation: '', positionName: '', projectDescription: null, attachments: [], notes: null, version: 1, changeReason: '' })
const canManage = computed(() => auth.currentUser?.permissions?.includes('CONTRACT_MANAGE') === true)
const typeMap = computed(() => {
  const map = new Map<string, string>()
  for (const ct of effectiveConfig.contractTypes) {
    map.set(ct.code, ct.name)
  }
  return map
})
const typeLabel = (value: string) => typeMap.value.get(value) ?? ({ FIXED_TERM: '固定期限', OPEN_ENDED: '无固定期限', PROJECT_BASED: '以完成任务为期限', INTERNSHIP: '实习协议', LABOR_DISPATCH: '劳务派遣协议' }[value] ?? value)
const statusLabel = (value: string) => ({ DRAFT: '草稿', ACTIVE: '有效', EXPIRING: '即将到期', EXPIRED: '已到期', TERMINATED: '已终止', SUPERSEDED: '已续签替代' }[value] ?? value)
const eventLabel = (value: string) => ({ CREATED: '创建草稿', UPDATED: '更新草稿', ACTIVATED: '激活归档', SUPERSEDED: '续签替代', RENEWED_FROM: '完成续签', TERMINATED: '登记终止', ALERT_ACKNOWLEDGED: '确认预警', DEMO_CREATED: '生成演示合同' }[value] ?? value)

function addDays(date: string, days: number) { const value = new Date(`${date}T00:00:00+08:00`); value.setDate(value.getDate() + days); return value.toISOString().slice(0, 10) }
function fill(item: EmploymentContract, mode: 'edit' | 'renew') {
  const startDate = mode === 'renew' ? addDays(item.endDate ?? today, 1) : item.startDate
  const endDate = mode === 'renew' ? addDays(startDate, 365) : item.endDate ?? null
  Object.assign(form, { userId: item.userId, contractType: item.contractType, signedDate: mode === 'renew' ? today : item.signedDate ?? null, startDate, endDate, probationStartDate: mode === 'renew' ? null : item.probationStartDate ?? null, probationEndDate: mode === 'renew' ? null : item.probationEndDate ?? null, workLocation: item.workLocation, positionName: item.positionName, projectDescription: mode === 'renew' ? null : item.projectDescription ?? null, attachments: mode === 'renew' ? [] : [...item.attachments], notes: item.notes ?? null, version: item.version, changeReason: mode === 'renew' ? '劳动合同到期续签' : '更新劳动合同草稿' })
}
function openEditor(mode: 'edit' | 'renew') { if (!contracts.detail) return; editorMode.value = mode; fill(contracts.detail, mode); contracts.error = ''; contracts.message = ''; editorOpen.value = true }
function typeChanged() { if (form.contractType === 'OPEN_ENDED') form.endDate = null; else if (!form.endDate) form.endDate = addDays(form.startDate, 365); if (form.contractType !== 'PROJECT_BASED') form.projectDescription = null }
async function upload(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]; if (!file) return
  uploading.value = true
  try { form.attachments.push(await files.uploadFile(file)) } catch (cause) { contracts.error = cause instanceof Error ? cause.message : '合同附件上传失败。' }
  finally { uploading.value = false; (event.target as HTMLInputElement).value = '' }
}
async function saveEditor() {
  if (!contracts.detail) return
  const payload = { ...form, signedDate: form.signedDate || null, endDate: form.endDate || null, probationStartDate: form.probationStartDate || null, probationEndDate: form.probationEndDate || null, projectDescription: form.projectDescription || null, notes: form.notes || null }
  const success = editorMode.value === 'edit' ? await contracts.update(contracts.detail.id, payload) : await contracts.renew(contracts.detail.id, payload)
  if (success) { editorOpen.value = false; if (editorMode.value === 'renew' && contracts.detail) await router.replace(`/hr/contracts/${contracts.detail.id}`) }
}
async function activate() { if (contracts.detail && await contracts.activate(contracts.detail.id, contracts.detail.version, action.reason)) activateOpen.value = false }
async function terminate() { if (contracts.detail && await contracts.terminate(contracts.detail.id, contracts.detail.version, action.terminationDate, action.reason)) terminateOpen.value = false }

onMounted(async () => { await Promise.all([contracts.loadDetail(props.id), effectiveConfig.load()]) })
watch(() => props.id, id => { editorOpen.value = false; void contracts.loadDetail(id) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">CONTRACT DETAIL</p><h1>劳动合同详情</h1><p>查看签署文件、状态变化、续签链路和预警处理记录。</p></div><div class="heading-actions"><button class="secondary" @click="router.push('/hr/contracts')">← 返回合同台账</button><button v-if="canManage && contracts.detail?.status === 'DRAFT'" @click="openEditor('edit')">编辑草稿</button><button v-if="canManage && contracts.detail?.status === 'ACTIVE'" @click="openEditor('renew')">办理续签</button></div></div>
  <p v-if="contracts.message" class="notice success">{{ contracts.message }}</p><p v-if="contracts.error" class="notice error">{{ contracts.error }}</p><p v-if="contracts.loading" class="panel empty">正在加载劳动合同…</p>
  <template v-else-if="contracts.detail"><section v-if="contracts.detail.openEndedReviewRequired" class="notice warning-notice"><strong>需要人工法务评估</strong><span>{{ contracts.detail.openEndedReviewReason }}</span></section><section v-if="contracts.detail.currentAlertThreshold && !contracts.detail.currentAlertAcknowledged" class="notice warning-notice"><strong>{{ contracts.detail.currentAlertThreshold }} 天到期预警尚未确认</strong><button v-if="canManage" class="secondary" :disabled="contracts.saving" @click="contracts.acknowledge(contracts.detail.id, contracts.detail.currentAlertThreshold!)">确认已查看</button></section>
    <section class="panel"><div class="detail-status"><strong>{{ contracts.detail.employeeName }} · {{ contracts.detail.contractNumber }}</strong><em :class="{ archived: ['TERMINATED','SUPERSEDED'].includes(contracts.detail.displayStatus), warning: ['EXPIRING','EXPIRED'].includes(contracts.detail.displayStatus) }">{{ statusLabel(contracts.detail.displayStatus) }}</em></div><dl class="detail-grid"><div><dt>所属部门</dt><dd>{{ contracts.detail.departmentName }}</dd></div><div><dt>合同岗位</dt><dd>{{ contracts.detail.positionName }}</dd></div><div><dt>合同类型</dt><dd>{{ typeLabel(contracts.detail.contractType) }}</dd></div><div><dt>工作地点</dt><dd>{{ contracts.detail.workLocation }}</dd></div><div><dt>签订日期</dt><dd>{{ contracts.detail.signedDate || '未填写' }}</dd></div><div><dt>合同版本</dt><dd>v{{ contracts.detail.version }} · 第 {{ contracts.detail.renewalSequence + 1 }} 期</dd></div><div><dt>合同开始</dt><dd>{{ contracts.detail.startDate }}</dd></div><div><dt>合同结束</dt><dd>{{ contracts.detail.endDate || '无固定期限' }}</dd></div><div><dt>试用期开始</dt><dd>{{ contracts.detail.probationStartDate || '不适用' }}</dd></div><div><dt>试用期结束</dt><dd>{{ contracts.detail.probationEndDate || '不适用' }}</dd></div><div v-if="contracts.detail.renewalOfNumber"><dt>续签来源</dt><dd>{{ contracts.detail.renewalOfNumber }}</dd></div><div v-if="contracts.detail.daysUntilEnd !== null"><dt>剩余天数</dt><dd>{{ contracts.detail.daysUntilEnd }} 天</dd></div><div v-if="contracts.detail.projectDescription" class="wide"><dt>任务期限说明</dt><dd>{{ contracts.detail.projectDescription }}</dd></div><div v-if="contracts.detail.terminationReason" class="wide"><dt>终止登记</dt><dd>{{ contracts.detail.terminationDate }} · {{ contracts.detail.terminationReason }}</dd></div><div class="wide"><dt>备注</dt><dd>{{ contracts.detail.notes || '无' }}</dd></div></dl><div class="contract-files"><strong>签署附件</strong><p v-if="contracts.detail.attachments.length"><button v-for="fileId in contracts.detail.attachments" :key="fileId" class="secondary attachment-button" @click="files.downloadAttachment(fileId, 'contract', contracts.detail!.id)">下载附件 {{ fileId.slice(0, 8) }}</button></p><p v-else class="muted">尚未关联签署附件。</p></div><div v-if="canManage" class="detail-actions"><button v-if="contracts.detail.status === 'DRAFT'" :disabled="contracts.saving || !contracts.detail.signedDate || !contracts.detail.attachments.length" @click="action.reason = '签署件核验完成，激活合同'; activateOpen = true">激活并归档</button><button v-if="contracts.detail.status === 'ACTIVE'" class="danger" @click="action.reason = ''; action.terminationDate = today; terminateOpen = true">登记终止</button></div></section>
    <section class="panel"><div class="section-title"><div><p class="eyebrow">CHANGE HISTORY</p><h2>合同变更历史</h2></div></div><div v-if="contracts.detail.events.length" class="timeline"><article v-for="event in contracts.detail.events" :key="event.id"><p><strong>{{ eventLabel(event.eventType) }} · {{ event.summary }}</strong></p><p>{{ event.reason }}</p><small>{{ event.changedByName }} · {{ new Date(event.createdAt).toLocaleString('zh-CN') }}</small></article></div><p v-else class="empty">暂无合同变更历史。</p></section>
  </template>

  <OaDialog :open="editorOpen" :title="editorMode === 'edit' ? '编辑合同草稿' : '办理合同续签'" :description="editorMode === 'renew' ? '续签会创建新的有效合同，并将原合同标记为已续签替代。' : '仅草稿可编辑，保存时会校验试用期和附件归属。'" :submit-label="editorMode === 'edit' ? '保存草稿' : '确认续签'" :busy="contracts.saving || uploading" @close="editorOpen = false" @submit="saveEditor"><div class="dialog-grid"><label class="dialog-field">合同类型<select v-model="form.contractType" @change="typeChanged"><option v-for="ct in effectiveConfig.contractTypes" :key="ct.code" :value="ct.code as EmploymentContractType">{{ ct.name }}</option></select></label><label class="dialog-field">签订日期<input v-model="form.signedDate" type="date" :max="today"></label><label class="dialog-field">开始日期<input v-model="form.startDate" type="date"></label><label v-if="form.contractType !== 'OPEN_ENDED'" class="dialog-field">结束日期<input v-model="form.endDate" type="date"></label><label class="dialog-field">工作地点<input v-model="form.workLocation" maxlength="100"></label><label class="dialog-field">合同岗位<input v-model="form.positionName" maxlength="100"></label><label class="dialog-field">试用期开始<input v-model="form.probationStartDate" type="date"></label><label class="dialog-field">试用期结束<input v-model="form.probationEndDate" type="date"></label><label v-if="form.contractType === 'PROJECT_BASED'" class="dialog-field">任务说明<textarea v-model="form.projectDescription" maxlength="500"></textarea></label></div><label class="dialog-field">签署附件<input type="file" accept=".pdf,.jpg,.jpeg,.png,.doc,.docx" :disabled="uploading || form.attachments.length >= 10" @change="upload"><small>已关联 {{ form.attachments.length }} 个附件；续签必须上传新的签署件。</small></label><label class="dialog-field">备注<textarea v-model="form.notes" maxlength="1000"></textarea></label><label class="dialog-field">变更说明<textarea v-model="form.changeReason" maxlength="500"></textarea></label></OaDialog>
  <OaDialog :open="activateOpen" title="激活并归档劳动合同" description="请确认签订日期和签署附件均已核验。激活后合同正文不可直接编辑。" submit-label="确认激活" :busy="contracts.saving" @close="activateOpen = false" @submit="activate"><label class="dialog-field">激活说明<textarea v-model="action.reason" maxlength="500"></textarea></label></OaDialog>
  <OaDialog :open="terminateOpen" title="登记劳动合同终止" description="此操作仅登记已确认的终止事实，不替代线下法律程序。" submit-label="确认登记" :busy="contracts.saving" danger @close="terminateOpen = false" @submit="terminate"><div class="dialog-grid"><label class="dialog-field">终止日期<input v-model="action.terminationDate" type="date" :max="today"></label></div><label class="dialog-field">终止原因<textarea v-model="action.reason" maxlength="500" placeholder="至少 5 个字符"></textarea></label></OaDialog>
</template>
