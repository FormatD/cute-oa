<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useApiClient } from '../api/client'
import type { DetailView, ExpenseInvoiceListItem, PaymentTransactionListItem } from '../api/types'
import FlowInstanceTimeline from '../components/FlowInstanceTimeline.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useDetailStore } from '../stores/detail'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useExpenseStore } from '../stores/expense'
import { useFileStore } from '../stores/files'
import { useLeaveStore } from '../stores/leave'
const props = defineProps<DetailView>()
const app = useAppStore()
const auth = useAuthStore()
const detail = useDetailStore()
const employeeDirectory = useEmployeeDirectoryStore()
const expense = useExpenseStore()
const files = useFileStore()
const leave = useLeaveStore()
const router = useRouter()
const api = useApiClient()

const expensePayments = ref<PaymentTransactionListItem[]>([])
const expenseInvoices = ref<ExpenseInvoiceListItem[]>([])

async function loadClosureData() {
  if (props.module === 'expense') {
    try {
      const [pays, invs] = await Promise.all([
        api.payments.getPayments('Expense', props.id),
        api.payments.getExpenseInvoices(props.id)
      ])
      expensePayments.value = pays
      expenseInvoices.value = invs
    } catch {
      expensePayments.value = []
      expenseInvoices.value = []
    }
  }
}

function load() {
  detail.loadDetail(props)
  void loadClosureData()
}
onMounted(load); watch(() => [props.module, props.id], load)
function back() { router.push(`/${props.module === 'notification' ? 'approval' : props.module}`) }
function copyNames(ids?: string[]) { return ids?.map(id => employeeDirectory.employees.find(item => item.id === id)?.name ?? id).join('、') || '无' }
function isFileId(value?: string) { return Boolean(value && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value)) }
</script>
<template>
  <section class="panel detail-page"><div class="section-title"><div><p class="eyebrow">DETAIL</p><h2>{{ module === 'leave' ? '请假申请详情' : module === 'expense' ? '报销申请详情' : module === 'notification' ? '通知详情' : '工作日历详情' }}</h2></div><button class="secondary" @click="back">← 返回列表</button></div>
    <p v-if="detail.detailLoading" class="empty">正在加载详情…</p>
    <template v-else-if="leave.leaveDetail"><div class="detail-status"><strong>{{ leave.leaveDetail.number }}</strong><em>{{ leave.leaveDetail.status }}</em></div><dl class="detail-grid"><div><dt>申请人</dt><dd>{{ leave.leaveDetail.applicantName || auth.currentUser?.name }}</dd></div><div><dt>假别</dt><dd>{{ leave.leaveDetail.type }}</dd></div><div><dt>开始时间</dt><dd>{{ leave.leaveDetail.startDate }} · {{ leave.leaveDetail.startPeriod }}</dd></div><div><dt>结束时间</dt><dd>{{ leave.leaveDetail.endDate }} · {{ leave.leaveDetail.endPeriod }}</dd></div><div><dt>请假时长</dt><dd>{{ leave.leaveDetail.days }} 天</dd></div><div><dt>当前状态</dt><dd>{{ leave.leaveDetail.status }}</dd></div><div><dt>流程版本</dt><dd>{{ leave.leaveDetail.processDefinitionCode ? `${leave.leaveDetail.processDefinitionCode} v${leave.leaveDetail.processDefinitionVersion}` : '历史单据未记录' }}</dd></div><div class="wide"><dt>请假事由</dt><dd>{{ leave.leaveDetail.reason }}</dd></div><div class="wide"><dt>抄送人</dt><dd>{{ copyNames(leave.leaveDetail.copyRecipientIds) }}</dd></div><div class="wide"><dt>证明材料</dt><dd v-if="leave.leaveDetail.attachments?.length"><button v-for="id in leave.leaveDetail.attachments" :key="id" class="secondary attachment-button" @click="files.downloadAttachment(id, 'leave', leave.leaveDetail!.id)">下载附件 {{ id.slice(0, 8) }}</button></dd><dd v-else>无</dd></div></dl><FlowInstanceTimeline :instances="leave.leaveDetail.flowInstances" :current-id="leave.leaveDetail.currentFlowInstanceId" /><div v-if="leave.leaveDetail.tasks?.length" class="timeline"><h3>当前实例任务</h3><p v-for="task in leave.leaveDetail.tasks" :key="task.id">第 {{ task.sequence }} 节点 · {{ task.assigneeName }}<span v-if="task.originalAssigneeName">（代 {{ task.originalAssigneeName }} 审批）</span> · {{ task.status }}<span v-if="task.comment">：{{ task.comment }}</span></p></div><div v-if="leave.leaveDetail.status === 'Approving' && leave.leaveDetail.applicantName === auth.currentUser?.name" class="detail-actions"><button @click="app.withdrawLeave(leave.leaveDetail)">撤回申请</button></div></template>
    <template v-else-if="expense.expenseDetail"><div class="detail-status"><strong>{{ expense.expenseDetail.number }}</strong><span v-if="expense.expenseDetail.isOverBudget" class="invoice-badge released" style="background:#fee2e2;color:#991b1b;margin-right:8px;">超预算</span><em>{{ expense.expenseDetail.status }}</em></div><section v-if="expense.expenseDetail.isOverBudget" class="embedded-card" style="border-left: 4px solid #ef4444; background: #fef2f2; margin-bottom: 1rem;"><h3 style="color: #991b1b; margin-top: 0;">⚠️ 超预算特批记录</h3><p style="color: #7f1d1d; margin: 4px 0 0;">本报销单申请金额超出可用预算额度（超额 ¥{{ (expense.expenseDetail.overBudgetAmount ?? 0).toFixed(2) }}）。系统已自动加签财务经理特批节点。</p></section><dl class="detail-grid"><div><dt>申请人</dt><dd>{{ expense.expenseDetail.applicantName || auth.currentUser?.name }}</dd></div><div><dt>报销金额</dt><dd>¥{{ expense.expenseDetail.totalAmount.toFixed(2) }}</dd></div><div><dt>已付金额 / 付款状态</dt><dd>¥{{ (expense.expenseDetail.paidTotalAmount ?? 0).toFixed(2) }} · <span class="invoice-badge" :class="{ paid: expense.expenseDetail.paymentStatus === 'PAID', released: !expense.expenseDetail.paymentStatus || expense.expenseDetail.paymentStatus === 'UNPAID' }">{{ expense.expenseDetail.paymentStatus === 'PAID' ? '已结清' : expense.expenseDetail.paymentStatus === 'PARTIALLY_PAID' ? '部分付款' : '未付款' }}</span></dd></div><div v-if="expense.expenseDetail.travelRequestId"><dt>关联出差</dt><dd><button class="secondary attachment-button" @click="router.push(`/travel/${expense.expenseDetail!.travelRequestId}`)">{{ expense.expenseDetail.travelRequestNumber || '查看出差申请' }}</button></dd></div><div><dt>收款账户名</dt><dd>{{ expense.expenseDetail.payeeAccountName || '—' }}</dd></div><div><dt>开户行</dt><dd>{{ expense.expenseDetail.bankName || '—' }}</dd></div><div><dt>流程版本</dt><dd>{{ expense.expenseDetail.processDefinitionCode ? `${expense.expenseDetail.processDefinitionCode} v${expense.expenseDetail.processDefinitionVersion}` : '历史单据未记录' }}</dd></div><div class="wide"><dt>费用说明</dt><dd>{{ expense.expenseDetail.description || '—' }}</dd></div><div class="wide"><dt>抄送人</dt><dd>{{ copyNames(expense.expenseDetail.copyRecipientIds) }}</dd></div><div class="wide"><dt>费用明细与附件</dt><dd><template v-for="item in expense.expenseDetail.items" :key="item.expenseDate + item.description"><span>{{ item.category }} ¥{{ item.amount.toFixed(2) }}</span><button v-for="id in item.attachments" :key="id" class="secondary attachment-button" @click="files.downloadAttachment(id, 'expense', expense.expenseDetail!.id)">下载附件 {{ id.slice(0, 8) }}</button><br></template><span v-if="!expense.expenseDetail.items?.length">无</span></dd></div><template v-if="expense.expenseDetail.payment"><div><dt>付款日期</dt><dd>{{ expense.expenseDetail.payment.paymentDate }}</dd></div><div><dt>付款方式</dt><dd>{{ expense.expenseDetail.payment.paymentMethod }}</dd></div><div><dt>实付金额</dt><dd>¥{{ expense.expenseDetail.payment.paidAmount.toFixed(2) }}</dd></div><div><dt>付款流水号</dt><dd>{{ expense.expenseDetail.payment.transactionNumber }}</dd></div><div class="wide"><dt>付款凭证</dt><dd><button v-if="isFileId(expense.expenseDetail.payment.proofFile)" class="secondary attachment-button" @click="files.downloadAttachment(expense.expenseDetail.payment!.proofFile, 'expense', expense.expenseDetail!.id)">下载付款凭证</button><span v-else>{{ expense.expenseDetail.payment.proofFile }}</span></dd></div></template></dl><section v-if="expenseInvoices.length" class="embedded-card"><h3>结构化发票清单（共 {{ expenseInvoices.length }} 张）</h3><div class="table-wrap"><table class="data-table"><thead><tr><th>发票类型</th><th>发票代码</th><th>发票号码</th><th>开票日期</th><th>不含税金额</th><th>税额</th><th>价税合计</th><th>状态</th></tr></thead><tbody><tr v-for="inv in expenseInvoices" :key="inv.id"><td>{{ inv.invoiceType }}</td><td>{{ inv.invoiceCode || '—' }}</td><td>{{ inv.invoiceNumber }}</td><td>{{ inv.billingDate }}</td><td>¥{{ inv.amountWithoutTax.toFixed(2) }}</td><td>¥{{ inv.taxAmount.toFixed(2) }}</td><td><strong>¥{{ inv.totalAmount.toFixed(2) }}</strong></td><td><span class="invoice-badge" :class="{ paid: inv.status === 'Paid', released: inv.status === 'Released' }">{{ inv.status === 'Paid' ? '已付款归档' : inv.status === 'Released' ? '已退还' : '占用中' }}</span></td></tr></tbody></table></div></section><section v-if="expensePayments.length" class="embedded-card"><h3>分期打款流水记录（共 {{ expensePayments.length }} 笔）</h3><div class="table-wrap"><table class="data-table"><thead><tr><th>批次</th><th>打款日期</th><th>支付方式</th><th>实付金额</th><th>银行流水号</th><th>经办财务</th><th>收款账号</th></tr></thead><tbody><tr v-for="pay in expensePayments" :key="pay.id"><td>{{ pay.batchTitle || `第 ${pay.sequence} 笔` }}</td><td>{{ pay.paymentDate }}</td><td>{{ pay.paymentMethod }}</td><td><strong>¥{{ pay.paidAmount.toFixed(2) }}</strong></td><td><code>{{ pay.transactionNumber }}</code></td><td>{{ pay.operatorName }}</td><td>{{ pay.payeeAccountMasked }}</td></tr></tbody></table></div></section><FlowInstanceTimeline :instances="expense.expenseDetail.flowInstances" :current-id="expense.expenseDetail.currentFlowInstanceId" /><div v-if="expense.expenseDetail.tasks?.length" class="timeline"><h3>当前实例任务</h3><p v-for="task in expense.expenseDetail.tasks" :key="task.id">第 {{ task.sequence }} 节点 · {{ task.assigneeName }}<span v-if="task.originalAssigneeName">（代 {{ task.originalAssigneeName }} 审批）</span> · {{ task.status }}<span v-if="task.comment">：{{ task.comment }}</span></p></div><div v-if="expense.expenseDetail.status === 'Approving' && expense.expenseDetail.applicantName === auth.currentUser?.name" class="detail-actions"><button @click="app.withdrawExpense(expense.expenseDetail)">撤回申请</button></div></template>
    <template v-else-if="detail.notificationDetail"><div class="detail-status"><strong>{{ detail.notificationDetail.title }}</strong><em>{{ detail.notificationDetail.readAt ? '已读' : '未读' }}</em></div><dl class="detail-grid"><div><dt>通知时间</dt><dd>{{ detail.notificationDetail.createdAt }}</dd></div><div><dt>状态</dt><dd>{{ detail.notificationDetail.readAt ? '已读' : '未读' }}</dd></div><div class="wide"><dt>通知内容</dt><dd>{{ detail.notificationDetail.content }}</dd></div></dl></template>
    <template v-else-if="detail.calendarDetail"><div class="detail-status"><strong>{{ detail.calendarDetail.date }}</strong><em :class="{ holiday: !detail.calendarDetail.isWorkingDay }">{{ detail.calendarDetail.isWorkingDay ? '工作日' : '休息日' }}</em></div><dl class="detail-grid"><div><dt>日期</dt><dd>{{ detail.calendarDetail.date }}</dd></div><div><dt>类型</dt><dd>{{ detail.calendarDetail.isWorkingDay ? '工作日' : '休息日' }}</dd></div><div><dt>来源</dt><dd>{{ detail.calendarDetail.source }}</dd></div><div class="wide"><dt>说明</dt><dd>{{ detail.calendarDetail.note || '企业工作日设置' }}</dd></div></dl></template>
    <p v-else class="empty">未找到该记录，可能已被删除或当前无查看权限。</p>
  </section>
</template>
