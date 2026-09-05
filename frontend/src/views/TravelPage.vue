<script setup lang="ts">
import { useRouter } from 'vue-router'
import OaDialog from '../components/OaDialog.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useFileStore } from '../stores/files'
import { useTravelStore } from '../stores/travel'
import { useUiStore } from '../stores/ui'
import { businessStatusLabel } from '../utils/businessLabels'

const app = useAppStore()
const auth = useAuthStore()
const employeeDirectory = useEmployeeDirectoryStore()
const fileStore = useFileStore()
const travel = useTravelStore()
const ui = useUiStore()
const router = useRouter()
const transports = ['高铁', '飞机', '汽车', '自驾', '轮船', '其他']
function addItinerary() { travel.travelForm.itinerary.push({ destination: '', startDate: '', endDate: '', transportation: '高铁', purpose: '' }) }
function removeItinerary(index: number) { if (travel.travelForm.itinerary.length > 1) travel.travelForm.itinerary.splice(index, 1) }
function selectAttachments(event: Event) { const selectedFiles = Array.from((event.target as HTMLInputElement).files ?? []); void Promise.all(selectedFiles.map(file => fileStore.uploadFile(file))).then(ids => { travel.travelForm.attachments = ids }).catch(cause => { ui.error = cause instanceof Error ? cause.message : '附件上传失败。' }) }
function search() { travel.travelPage = 1; void travel.loadTravels() }
function changePage(offset: number) { travel.travelPage += offset; void travel.loadTravels() }
function resetFilters() { Object.assign(travel.travelFilters, { keyword: '', status: '', applicantId: '', startDate: '', endDate: '' }); search() }
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">TRAVEL MANAGEMENT</p><h1>出差管理</h1><p>发起出差申请，维护行程、同行人和预估预算。</p></div><button class="primary-action" @click="travel.showTravelForm = true">＋ 发起出差</button></div>
  <p v-if="ui.message" class="notice success">{{ ui.message }}</p><p v-if="ui.error" class="notice error">{{ ui.error }}</p>
  <form class="filter-bar" @submit.prevent="search"><input v-model="travel.travelFilters.keyword" placeholder="单号、地点、事由或申请人"><select v-model="travel.travelFilters.status"><option value="">全部状态</option><option value="0">草稿</option><option value="1">审批中</option><option value="2">已驳回</option><option value="3">已批准</option><option value="4">已撤回</option></select><select v-model="travel.travelFilters.applicantId"><option value="">全部申请人</option><option v-for="employee in employeeDirectory.employees" :key="employee.id" :value="employee.id">{{ employee.name }}</option></select><label>开始<input v-model="travel.travelFilters.startDate" type="date"></label><label>结束<input v-model="travel.travelFilters.endDate" type="date"></label><button type="submit">查询</button><button class="secondary" type="button" @click="resetFilters">重置</button></form>
  <section class="panel"><div class="section-title"><div><p class="eyebrow">TRAVEL REQUESTS</p><h2>出差申请</h2></div><button class="secondary" @click="travel.showTravelForm = true">新建出差</button></div>
    <template v-if="travel.travels.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>申请单号</th><th>申请人</th><th>日期</th><th>目的地</th><th>预算</th><th>状态</th><th>操作</th></tr></thead><tbody><tr v-for="item in travel.pagedTravels.items" :key="item.id"><td>{{ item.number }}</td><td>{{ item.applicantName }}</td><td>{{ item.startDate }} 至 {{ item.endDate }}（{{ item.days }} 天）</td><td>{{ item.itinerary.map(line => line.destination).join('、') }}</td><td>¥{{ item.estimatedBudget.toFixed(2) }}</td><td><em>{{ businessStatusLabel(item.status) }}</em></td><td><button class="secondary" @click="router.push(`/travel/${item.id}`)">详情</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ travel.pagedTravels.total }} 条</span><div><button class="secondary" :disabled="travel.pagedTravels.currentPage === 1" @click="changePage(-1)">上一页</button><b>{{ travel.pagedTravels.currentPage }} / {{ travel.pagedTravels.totalPages }}</b><button class="secondary" :disabled="travel.pagedTravels.currentPage === travel.pagedTravels.totalPages" @click="changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前筛选条件下暂无出差记录。</p>
  </section>

  <OaDialog
    :open="travel.showTravelForm"
    title="发起出差申请"
    description="填写出差事由、预估预算、同行人及行程明细。"
    submit-label="保存并提交"
    :busy="travel.travelSubmitting"
    width="850px"
    @close="travel.showTravelForm = false"
    @submit="app.submitTravel"
  >
    <label class="dialog-field wide">出差事由<textarea v-model="travel.travelForm.purpose" maxlength="500" placeholder="说明出差目的和预期成果"></textarea></label>
    <label class="dialog-field">预估预算（元）<input v-model="travel.travelForm.estimatedBudget" min="0" max="10000000" step="0.01" type="number"></label>
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
