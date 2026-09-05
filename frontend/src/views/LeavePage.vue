<script setup lang="ts">
import { useRouter } from 'vue-router'
import OaDialog from '../components/OaDialog.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useFileStore } from '../stores/files'
import { useLeaveStore } from '../stores/leave'
import { useUiStore } from '../stores/ui'

const app = useAppStore()
const auth = useAuthStore()
const employeeDirectory = useEmployeeDirectoryStore()
const fileStore = useFileStore()
const leave = useLeaveStore()
const ui = useUiStore()
const router = useRouter()

function selectAttachments(event: Event) {
  const selectedFiles = Array.from((event.target as HTMLInputElement).files ?? [])
  void Promise.all(selectedFiles.map(file => fileStore.uploadFile(file))).then(ids => { leave.form.attachments = ids }).catch(cause => { ui.error = cause instanceof Error ? cause.message : '附件上传失败。' })
}
function search() { leave.leavePage = 1; void leave.loadLeaves() }
function changePage(offset: number) { leave.leavePage += offset; void leave.loadLeaves() }
function resetFilters() { Object.assign(leave.leaveFilters, { keyword: '', status: '', applicantId: '', startDate: '', endDate: '' }); search() }
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">LEAVE MANAGEMENT</p><h1>请假管理</h1><p>发起、查看和跟进个人请假申请。</p></div><button class="primary-action" @click="leave.showForm = true">＋ 发起请假</button></div>
  <form class="filter-bar" @submit.prevent="search"><input v-model="leave.leaveFilters.keyword" placeholder="单号、事由或申请人"><select v-model="leave.leaveFilters.status"><option value="">全部状态</option><option value="0">草稿</option><option value="1">审批中</option><option value="2">已驳回</option><option value="3">已完成</option><option value="4">已撤回</option></select><select v-model="leave.leaveFilters.applicantId"><option value="">全部申请人</option><option v-for="employee in employeeDirectory.employees" :key="employee.id" :value="employee.id">{{ employee.name }}</option></select><label>开始<input v-model="leave.leaveFilters.startDate" type="date"></label><label>结束<input v-model="leave.leaveFilters.endDate" type="date"></label><button type="submit">查询</button><button class="secondary" type="button" @click="resetFilters">重置</button></form>
  <section class="panel"><div class="section-title"><div><p class="eyebrow">LEAVE</p><h2>请假申请</h2></div><button class="secondary" @click="leave.showForm = true">新建请假</button></div>
    <template v-if="leave.leaves.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>申请单号</th><th>假别</th><th>请假时间</th><th>时长</th><th>事由</th><th>状态</th><th>操作</th></tr></thead><tbody><tr v-for="item in leave.pagedLeaves.items" :key="item.id"><td>{{ item.number }}</td><td>{{ item.type }}</td><td>{{ item.startDate }} 至 {{ item.endDate }}</td><td>{{ item.days }} 天</td><td>{{ item.reason }}</td><td><em>{{ item.status }}</em></td><td><button class="secondary" @click="router.push(`/leave/${item.id}`)">详情</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ leave.pagedLeaves.total }} 条</span><div><button class="secondary" :disabled="leave.pagedLeaves.currentPage === 1" @click="changePage(-1)">上一页</button><b>{{ leave.pagedLeaves.currentPage }} / {{ leave.pagedLeaves.totalPages }}</b><button class="secondary" :disabled="leave.pagedLeaves.currentPage === leave.pagedLeaves.totalPages" @click="changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前筛选条件下暂无请假记录。</p>
  </section>

  <OaDialog
    :open="leave.showForm"
    title="发起请假申请"
    description="填写请假类型、起止时间与请假事由，提交审批流程。"
    submit-label="保存并提交"
    :busy="ui.submitting"
    width="680px"
    @close="leave.showForm = false"
    @submit="app.submitLeave"
  >
    <div class="dialog-grid">
      <label class="dialog-field">假别
        <select v-model="leave.form.type">
          <option value="Annual">年假</option>
          <option value="Personal">事假</option>
          <option value="Sick">病假</option>
          <option value="CompTime">调休</option>
        </select>
      </label>
      <label class="dialog-field">开始日期<input v-model="leave.form.startDate" type="date"></label>
      <label class="dialog-field">开始时段
        <select v-model="leave.form.startPeriod">
          <option value="FullDay">全天</option>
          <option value="Morning">上午</option>
          <option value="Afternoon">下午</option>
        </select>
      </label>
      <label class="dialog-field">结束日期<input v-model="leave.form.endDate" type="date"></label>
      <label class="dialog-field">结束时段
        <select v-model="leave.form.endPeriod">
          <option value="FullDay">全天</option>
          <option value="Morning">上午</option>
          <option value="Afternoon">下午</option>
        </select>
      </label>
    </div>
    <label class="dialog-field">请假事由<textarea v-model="leave.form.reason" maxlength="500" placeholder="请填写请假原因"></textarea></label>
    <fieldset class="copy-selector">
      <legend>抄送人（流程完成或撤回后通知）</legend>
      <label v-for="employee in employeeDirectory.employees.filter(item => item.id !== auth.currentUserId)" :key="employee.id">
        <input v-model="leave.form.copyRecipientIds" type="checkbox" :value="employee.id">
        {{ employee.name }} · {{ employee.role }}
      </label>
    </fieldset>
    <label class="dialog-field">证明材料
      <input accept=".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.doc,.docx" multiple type="file" @change="selectAttachments">
      <small>支持 PDF、图片和 Office 文档，单个文件不超过 20MB。</small>
    </label>
    <div v-if="leave.form.attachments.length" class="attachment-list">已上传 {{ leave.form.attachments.length }} 个附件</div>
    <p v-if="ui.error" class="dialog-error">{{ ui.error }}</p>
  </OaDialog>
</template>
