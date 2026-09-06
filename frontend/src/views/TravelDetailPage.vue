<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import FlowInstanceTimeline from '../components/FlowInstanceTimeline.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useDetailStore } from '../stores/detail'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useFileStore } from '../stores/files'
import { useTravelStore } from '../stores/travel'
import { businessStatusLabel } from '../utils/businessLabels'

const props = defineProps<{ id: string }>()
const app = useAppStore()
const auth = useAuthStore()
const detail = useDetailStore()
const employeeDirectory = useEmployeeDirectoryStore()
const files = useFileStore()
const travel = useTravelStore()
const router = useRouter()
const copyNames = (ids: string[]) => ids.map(id => employeeDirectory.employees.find(item => item.id === id)?.name ?? id).join('、') || '无'
onMounted(() => { void detail.loadDetail({ module: 'travel', id: props.id }) })
</script>

<template>
  <section class="panel detail-page">
    <div class="section-title">
      <div>
        <p class="eyebrow">TRAVEL DETAIL</p>
        <h2>出差申请详情</h2>
      </div>
      <button class="secondary" @click="router.push('/travel')">← 返回列表</button>
    </div>
    <p v-if="detail.detailLoading" class="empty">正在加载详情…</p>
    <template v-else-if="travel.travelDetail">
      <div class="detail-status">
        <strong>{{ travel.travelDetail.number }}</strong>
        <em>{{ businessStatusLabel(travel.travelDetail.status) }}</em>
      </div>
      <dl class="detail-grid">
        <div><dt>申请人</dt><dd>{{ travel.travelDetail.applicantName }}</dd></div>
        <div><dt>所属部门</dt><dd>{{ travel.travelDetail.departmentName }}</dd></div>
        <div><dt>出差日期</dt><dd>{{ travel.travelDetail.startDate }} 至 {{ travel.travelDetail.endDate }}</dd></div>
        <div><dt>出差时长</dt><dd>{{ travel.travelDetail.days }} 天</dd></div>
        <div><dt>预估预算</dt><dd>¥{{ travel.travelDetail.estimatedBudget.toFixed(2) }}</dd></div>
        <div><dt>同行人</dt><dd>{{ travel.travelDetail.companionNames.join('、') || '无' }}</dd></div>
        <div><dt>流程版本</dt><dd>{{ travel.travelDetail.processDefinitionCode ? `${travel.travelDetail.processDefinitionCode} v${travel.travelDetail.processDefinitionVersion}` : '尚未提交' }}</dd></div>
        <div><dt>标准合规状态</dt><dd><span :class="['compliance-tag', travel.travelDetail.isOverStandard ? 'tag-warn' : 'tag-ok']">{{ travel.travelDetail.isOverStandard ? '超标出差' : '合规出差' }}</span></dd></div>
        <div><dt>申请人职级</dt><dd>{{ travel.travelDetail.employeeRank || '未指定' }}</dd></div>
        <div><dt>目的地城市等级</dt><dd>{{ travel.travelDetail.primaryCityTier || travel.travelDetail.cityTier || '未指定' }}</dd></div>
        <div><dt>住宿标准上限</dt><dd>¥{{ (travel.travelDetail.standardHotelDailyLimit ?? travel.travelDetail.hotelBudgetPerDay ?? 0).toFixed(2) }} /天</dd></div>
        <div><dt>餐补标准</dt><dd>¥{{ (travel.travelDetail.standardMealDailyAllowance ?? travel.travelDetail.mealAllowancePerDay ?? 0).toFixed(2) }} /天</dd></div>
        <div><dt>交通工具标准</dt><dd>{{ travel.travelDetail.standardTransportation || travel.travelDetail.transportationStandard || '未指定' }}</dd></div>
        <div><dt>允许预算上限</dt><dd>¥{{ ((travel.travelDetail.allowedBudgetCap ?? (((travel.travelDetail.standardHotelDailyLimit ?? 0) + (travel.travelDetail.standardMealDailyAllowance ?? 0)) * travel.travelDetail.days * (1 + travel.travelDetail.companionNames.length)))).toFixed(2) }}</dd></div>
        <div><dt>策略版本快照</dt><dd>{{ (travel.travelDetail.configVersionNumber ?? travel.travelDetail.configurationSnapshotVersion) ? `v${travel.travelDetail.configVersionNumber ?? travel.travelDetail.configurationSnapshotVersion}` : '无快照' }}</dd></div>
        <div v-if="travel.travelDetail.isOverStandard"><dt>超标差额</dt><dd style="color: #b91c1c; font-weight: 600;">+¥{{ Math.max(0, travel.travelDetail.estimatedBudget - (travel.travelDetail.allowedBudgetCap ?? (((travel.travelDetail.standardHotelDailyLimit ?? 0) + (travel.travelDetail.standardMealDailyAllowance ?? 0)) * travel.travelDetail.days * (1 + travel.travelDetail.companionNames.length)))).toFixed(2) }}</dd></div>
        <div v-if="travel.travelDetail.isOverStandard" class="wide"><dt>超标事由说明</dt><dd style="color: #9f1239;">{{ travel.travelDetail.overStandardReason || '未填写' }}</dd></div>
        <div class="wide"><dt>出差事由</dt><dd>{{ travel.travelDetail.purpose }}</dd></div>
        <div class="wide"><dt>行程明细</dt><dd><span v-for="(item, index) in travel.travelDetail.itinerary" :key="index" class="detail-line">{{ item.startDate }} 至 {{ item.endDate }} · {{ item.destination }} · {{ item.transportation }} · {{ item.purpose }}</span></dd></div>
        <div class="wide"><dt>抄送人</dt><dd>{{ copyNames(travel.travelDetail.copyRecipientIds) }}</dd></div>
        <div class="wide"><dt>相关附件</dt><dd v-if="travel.travelDetail.attachments.length"><button v-for="fileId in travel.travelDetail.attachments" :key="fileId" class="secondary attachment-button" @click="files.downloadAttachment(fileId, 'travel', travel.travelDetail!.id)">下载附件 {{ fileId.slice(0, 8) }}</button></dd><dd v-else>无</dd></div>
      </dl>
      <FlowInstanceTimeline :instances="travel.travelDetail.flowInstances" :current-id="travel.travelDetail.currentFlowInstanceId" />
      <div v-if="travel.travelDetail.tasks.length" class="timeline">
        <h3>当前实例任务</h3>
        <p v-for="task in travel.travelDetail.tasks" :key="task.id">
          第 {{ task.sequence }} 节点 · {{ task.assigneeName }}
          <span v-if="task.originalAssigneeName">（代 {{ task.originalAssigneeName }} 审批）</span> · {{ businessStatusLabel(task.status) }}
          <span v-if="task.comment">：{{ task.comment }}</span>
        </p>
      </div>
      <div v-if="travel.travelDetail.status === 'Approving' && travel.travelDetail.applicantId === auth.currentUserId" class="detail-actions">
        <button @click="app.withdrawTravel(travel.travelDetail)">撤回申请</button>
      </div>
    </template>
    <p v-else class="empty">未找到该记录，可能已被删除或当前无查看权限。</p>
  </section>
</template>

<style scoped>
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
