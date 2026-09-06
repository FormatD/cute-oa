<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import OaDialog from '../components/OaDialog.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEffectiveConfigurationStore } from '../stores/effective-configurations'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useFileStore } from '../stores/files'
import { useTravelStore } from '../stores/travel'
import { useUiStore } from '../stores/ui'
import { businessStatusLabel } from '../utils/businessLabels'

const app = useAppStore()
const auth = useAuthStore()
const effectiveConfig = useEffectiveConfigurationStore()
const employeeDirectory = useEmployeeDirectoryStore()
const fileStore = useFileStore()
const travel = useTravelStore()
const ui = useUiStore()
const router = useRouter()
const transports = ['高铁', '飞机', '汽车', '自驾', '轮船', '其他']

onMounted(() => {
  void effectiveConfig.load()
})

const userRank = computed(() => {
  const user = auth.currentUser
  if (!user) return '员工'
  const roles = user.roles ?? (user.role ? [user.role] : [])
  if (roles.some(r => r.includes('gm') || r.includes('general_manager') || r.includes('总经理')) ||
      user.positionName?.includes('总经理') ||
      (user.departmentName?.includes('总经办') && !user.managerId)) {
    return '总经理'
  }
  if (roles.some(r => r.includes('manager') || r.includes('dept_manager') || r.includes('负责人') || r.includes('主管')) ||
      user.role.includes('经理') ||
      user.positionName?.includes('负责人') ||
      user.positionName?.includes('经理') ||
      user.positionName?.includes('主管')) {
    return '部门负责人'
  }
  return '员工'
})

const resolvedCityTier = computed(() => {
  const destinations = travel.travelForm.itinerary.map(i => i.destination?.trim() ?? '').filter(Boolean)
  for (const tier of effectiveConfig.travelCityTiers) {
    if (tier.cities.length > 0 && destinations.some(dest => tier.cities.some(c => dest.includes(c)))) {
      return tier.tierName
    }
  }
  const defaultTier = effectiveConfig.travelCityTiers.find(t => t.cities.length === 0)
  return defaultTier?.tierName ?? '其他城市'
})

const matchedStandard = computed(() => {
  return effectiveConfig.travelStandards.find(s => s.cityTier === resolvedCityTier.value && s.rank === userRank.value) ??
         effectiveConfig.travelStandards.find(s => s.rank === userRank.value) ??
         effectiveConfig.travelStandards[0]
})

const calculatedDays = computed(() => {
  const validDates = travel.travelForm.itinerary
    .filter(item => item.startDate && item.endDate)
    .map(item => ({
      start: new Date(item.startDate).getTime(),
      end: new Date(item.endDate).getTime()
    }))
  if (!validDates.length) return 0
  const minStart = Math.min(...validDates.map(d => d.start))
  const maxEnd = Math.max(...validDates.map(d => d.end))
  if (maxEnd < minStart) return 0
  return Math.round((maxEnd - minStart) / (1000 * 60 * 60 * 24)) + 1
})

const travelerCount = computed(() => 1 + travel.travelForm.companionIds.length)

const allowedBudgetLimit = computed(() => {
  if (!matchedStandard.value || calculatedDays.value <= 0) return 0
  return (matchedStandard.value.hotelDailyLimit + matchedStandard.value.mealDailyAllowance) * calculatedDays.value * travelerCount.value
})

const isOverBudget = computed(() => {
  const budget = Number(travel.travelForm.estimatedBudget) || 0
  return allowedBudgetLimit.value > 0 && budget > allowedBudgetLimit.value
})

const overBudgetAmount = computed(() => {
  if (!isOverBudget.value) return 0
  return (Number(travel.travelForm.estimatedBudget) || 0) - allowedBudgetLimit.value
})

const isOverBudgetBlocked = computed(() => {
  return isOverBudget.value && (matchedStandard.value?.blockWhenExceeded === true)
})

function addItinerary() { travel.travelForm.itinerary.push({ destination: '', startDate: '', endDate: '', transportation: '高铁', purpose: '' }) }
function removeItinerary(index: number) { if (travel.travelForm.itinerary.length > 1) travel.travelForm.itinerary.splice(index, 1) }
function selectAttachments(event: Event) { const selectedFiles = Array.from((event.target as HTMLInputElement).files ?? []); void Promise.all(selectedFiles.map(file => fileStore.uploadFile(file))).then(ids => { travel.travelForm.attachments = ids }).catch(cause => { ui.error = cause instanceof Error ? cause.message : '附件上传失败。' }) }
function search() { travel.travelPage = 1; void travel.loadTravels() }
function changePage(offset: number) { travel.travelPage += offset; void travel.loadTravels() }
function resetFilters() { Object.assign(travel.travelFilters, { keyword: '', status: '', applicantId: '', startDate: '', endDate: '' }); search() }

function handleSubmit() {
  if (effectiveConfig.error) {
    ui.error = `业务配置不可用，禁止提交：${effectiveConfig.error}`
    return
  }
  if (isOverBudget.value) {
    if (isOverBudgetBlocked.value) {
      ui.error = '当前出差申请预算已超出差旅标准，且策略禁止超标提交。'
      return
    }
    if (!travel.travelForm.overStandardReason?.trim()) {
      ui.error = '预估预算已超出标准上限，必须填写超标原因。'
      return
    }
  }
  void app.submitTravel()
}
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">TRAVEL MANAGEMENT</p><h1>出差管理</h1><p>发起出差申请，维护行程、同行人和预估预算。</p></div><button class="primary-action" @click="travel.showTravelForm = true">＋ 发起出差</button></div>
  <p v-if="ui.message" class="notice success">{{ ui.message }}</p><p v-if="ui.error" class="notice error">{{ ui.error }}</p>
  <form class="filter-bar" @submit.prevent="search"><input v-model="travel.travelFilters.keyword" placeholder="单号、地点、事由或申请人"><select v-model="travel.travelFilters.status"><option value="">全部状态</option><option value="0">草稿</option><option value="1">审批中</option><option value="2">已驳回</option><option value="3">已批准</option><option value="4">已撤回</option></select><select v-model="travel.travelFilters.applicantId"><option value="">全部申请人</option><option v-for="employee in employeeDirectory.employees" :key="employee.id" :value="employee.id">{{ employee.name }}</option></select><label>开始<input v-model="travel.travelFilters.startDate" type="date"></label><label>结束<input v-model="travel.travelFilters.endDate" type="date"></label><button type="submit">查询</button><button class="secondary" type="button" @click="resetFilters">重置</button></form>
  <section class="panel"><div class="section-title"><div><p class="eyebrow">TRAVEL REQUESTS</p><h2>出差申请</h2></div><button class="secondary" @click="travel.showTravelForm = true">新建出差</button></div>
    <template v-if="travel.travels.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>申请单号</th><th>申请人</th><th>日期</th><th>目的地</th><th>预算</th><th>合规状态</th><th>审批状态</th><th>操作</th></tr></thead><tbody><tr v-for="item in travel.pagedTravels.items" :key="item.id"><td>{{ item.number }}</td><td>{{ item.applicantName }}</td><td>{{ item.startDate }} 至 {{ item.endDate }}（{{ item.days }} 天）</td><td>{{ item.itinerary.map(line => line.destination).join('、') }}</td><td>¥{{ item.estimatedBudget.toFixed(2) }}</td><td><span :class="['compliance-tag', item.isOverStandard ? 'tag-warn' : 'tag-ok']">{{ item.isOverStandard ? '超标' : '合规' }}</span></td><td><em>{{ businessStatusLabel(item.status) }}</em></td><td><button class="secondary" @click="router.push(`/travel/${item.id}`)">详情</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ travel.pagedTravels.total }} 条</span><div><button class="secondary" :disabled="travel.pagedTravels.currentPage === 1" @click="changePage(-1)">上一页</button><b>{{ travel.pagedTravels.currentPage }} / {{ travel.pagedTravels.totalPages }}</b><button class="secondary" :disabled="travel.pagedTravels.currentPage === travel.pagedTravels.totalPages" @click="changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前筛选条件下暂无出差记录。</p>
  </section>

  <OaDialog
    :open="travel.showTravelForm"
    title="发起出差申请"
    description="填写出差事由、预估预算、同行人及行程明细。"
    submit-label="保存并提交"
    :busy="travel.travelSubmitting"
    :submit-disabled="!!effectiveConfig.error || isOverBudgetBlocked"
    width="850px"
    @close="travel.showTravelForm = false"
    @submit="handleSubmit"
  >
    <div v-if="effectiveConfig.error" class="dialog-error" data-testid="config-error-alert" style="margin-bottom: 12px; color: #dc2626; background: #fee2e2; padding: 8px 12px; border-radius: 6px;">
      ⚠️ 业务配置缺失或不可用，禁止提交申请：{{ effectiveConfig.error }}
    </div>
    <div v-else-if="isOverBudgetBlocked" class="dialog-error" style="margin-bottom: 12px; color: #dc2626; background: #fee2e2; padding: 8px 12px; border-radius: 6px;">
      🚫 预估预算已超出标准上限，当前差旅政策设定为【超标禁止提交】，无法发起申请。
    </div>

    <div class="standard-hint-card">
      <div class="hint-header">
        <strong>差旅标准测算参考</strong>
        <span class="policy-tag">适用职级：{{ userRank }} · 最高目的地等级：{{ resolvedCityTier }}</span>
      </div>
      <div class="hint-grid">
        <div><span>住宿限额</span><b>¥{{ matchedStandard?.hotelDailyLimit ?? 0 }} / 晚</b></div>
        <div><span>餐补标准</span><b>¥{{ matchedStandard?.mealDailyAllowance ?? 0 }} / 天</b></div>
        <div><span>交通标准</span><b>{{ matchedStandard?.transportationStandard || '高铁二等座/飞机经济舱' }}</b></div>
        <div><span>测算基数</span><b>{{ calculatedDays }} 天 · {{ travelerCount }} 人</b></div>
        <div><span>允许预算上限</span><b class="limit-val">¥{{ allowedBudgetLimit.toFixed(2) }}</b></div>
      </div>
    </div>

    <div v-if="isOverBudget && !isOverBudgetBlocked" class="over-standard-warning">
      <strong>⚠️ 预估预算超标</strong>
      <p>预估预算 ¥{{ Number(travel.travelForm.estimatedBudget).toFixed(2) }} 超出标准上限 ¥{{ allowedBudgetLimit.toFixed(2) }}（超标 ¥{{ overBudgetAmount.toFixed(2) }}）。按照公司制度，预算超标申请必须如实填报超标原因。</p>
    </div>

    <label class="dialog-field wide">出差事由<textarea v-model="travel.travelForm.purpose" maxlength="500" placeholder="说明出差目的和预期成果"></textarea></label>
    <label class="dialog-field">预估预算（元）<input v-model="travel.travelForm.estimatedBudget" min="0" max="10000000" step="0.01" type="number"></label>

    <label v-if="isOverBudget" class="dialog-field wide over-standard-field">
      超标原因 <span class="required" style="color: #e11d48;">*</span>
      <textarea
        v-model="travel.travelForm.overStandardReason"
        maxlength="500"
        placeholder="请详细说明预算超标原因（例如：会期酒店协议房满房、往返机票浮动较大等），此信息将展示给各级审批人。"
        required
      />
    </label>

    <fieldset class="copy-selector">
      <legend>同行人（可选）</legend>
      <label v-for="employee in employeeDirectory.employees.filter(item => item.id !== auth.currentUserId)" :key="employee.id">
        <input v-model="travel.travelForm.companionIds" type="checkbox" :value="employee.id">
        {{ employee.name }} · {{ employee.departmentName }}
      </label>
    </fieldset>

    <fieldset class="itinerary-editor">
      <legend>行程明细</legend>
      <div v-for="(item, index) in travel.travelForm.itinerary" :key="index" class="itinerary-row">
        <label>目的地<input v-model="item.destination" maxlength="100" placeholder="例如 上海"></label>
        <label>开始日期<input v-model="item.startDate" type="date"></label>
        <label>结束日期<input v-model="item.endDate" type="date"></label>
        <label>交通方式
          <select v-model="item.transportation">
            <option v-for="transport in transports" :key="transport">{{ transport }}</option>
          </select>
        </label>
        <label class="wide">行程任务<input v-model="item.purpose" maxlength="300" placeholder="例如 客户需求确认与方案汇报"></label>
        <button class="secondary" type="button" :disabled="travel.travelForm.itinerary.length === 1" @click="removeItinerary(index)">删除行程</button>
      </div>
      <button class="secondary" type="button" :disabled="travel.travelForm.itinerary.length >= 20" @click="addItinerary">＋ 添加行程</button>
    </fieldset>

    <fieldset class="copy-selector">
      <legend>抄送人（审批完成或撤回后通知）</legend>
      <label v-for="employee in employeeDirectory.employees.filter(item => item.id !== auth.currentUserId)" :key="employee.id">
        <input v-model="travel.travelForm.copyRecipientIds" type="checkbox" :value="employee.id">
        {{ employee.name }} · {{ employee.role }}
      </label>
    </fieldset>

    <label class="dialog-field wide">相关附件
      <input accept=".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.doc,.docx" multiple type="file" @change="selectAttachments">
      <small>可上传邀请函、会议通知或行程依据，单个文件不超过 20MB。</small>
    </label>
    <div v-if="travel.travelForm.attachments.length" class="attachment-list">已上传 {{ travel.travelForm.attachments.length }} 个附件</div>
    <p v-if="ui.error" class="dialog-error">{{ ui.error }}</p>
  </OaDialog>
</template>

<style scoped>
.standard-hint-card {
  margin-bottom: 16px;
  padding: 12px 16px;
  border-radius: 6px;
  border: 1px solid #dbeafe;
  background: #f8fafc;
}
.hint-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}
.policy-tag {
  font-size: 0.75rem;
  color: #3b82f6;
  background: #eff6ff;
  padding: 2px 8px;
  border-radius: 4px;
}
.hint-grid {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 8px;
  font-size: 0.8rem;
}
.hint-grid span {
  display: block;
  color: #64748b;
}
.hint-grid b {
  display: block;
  margin-top: 2px;
  color: #1e293b;
}
.hint-grid b.limit-val {
  color: #0284c7;
}
.over-standard-warning {
  margin-bottom: 14px;
  padding: 10px 14px;
  border-radius: 6px;
  border: 1px solid #fecdd3;
  background: #fff1f2;
  color: #9f1239;
  font-size: 0.85rem;
}
.over-standard-warning strong {
  display: block;
  margin-bottom: 4px;
}
.over-standard-field textarea {
  border-color: #fda4af;
  background: #fffbfa;
}
.compliance-tag {
  display: inline-block;
  padding: 2px 8px;
  font-size: 0.75rem;
  border-radius: 4px;
}
.tag-ok {
  background: #ecfdf5;
  color: #047857;
  border: 1px solid #a7f3d0;
}
.tag-warn {
  background: #fff1f2;
  color: #b91c1c;
  border: 1px solid #fecdd3;
}
</style>
