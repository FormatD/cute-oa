<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { ExpenseClaim } from '../api/types'
import OaDialog from '../components/OaDialog.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useExpenseStore } from '../stores/expense'
import { useFileStore } from '../stores/files'
import { useTravelStore } from '../stores/travel'
import { useUiStore } from '../stores/ui'
import { useWorkspaceStore } from '../stores/workspace'

const app = useAppStore()
const auth = useAuthStore()
const expenseStore = useExpenseStore()
const files = useFileStore()
const travel = useTravelStore()
const ui = useUiStore()
const workspace = useWorkspaceStore()
const router = useRouter()
const paymentTarget = ref<ExpenseClaim | null>(null)
const paymentProof = ref<File | null>(null)
const paymentSubmitting = ref(false)
const paymentError = ref('')
const today = new Date()
const localToday = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
const paymentForm = reactive({ paymentDate: localToday, paymentMethod: '银行转账', transactionNumber: '' })

function selectAttachments(event: Event) {
  const selectedFiles = Array.from((event.target as HTMLInputElement).files ?? [])
  void Promise.all(selectedFiles.map(file => files.uploadFile(file))).then(ids => { expenseStore.expenseForm.attachments = ids }).catch(cause => { ui.error = cause instanceof Error ? cause.message : '附件上传失败。' })
}
function search() { expenseStore.expensePage = 1; void expenseStore.loadExpenses() }
function changePage(offset: number) { expenseStore.expensePage += offset; void expenseStore.loadExpenses() }
function resetFilters() { Object.assign(expenseStore.expenseFilters, { keyword: '', status: '', applicantId: '', startDate: '', endDate: '', minAmount: '', maxAmount: '' }); search() }

function openPayment(expense: ExpenseClaim) {
  paymentTarget.value = expense
  Object.assign(paymentForm, { paymentDate: localToday, paymentMethod: '银行转账', transactionNumber: '' })
  paymentProof.value = null
  paymentError.value = ''
  ui.error = ''
}

function closePayment() {
  if (paymentSubmitting.value) return
  paymentTarget.value = null
  paymentProof.value = null
  paymentError.value = ''
}

function selectPaymentProof(event: Event) {
  paymentProof.value = (event.target as HTMLInputElement).files?.[0] ?? null
}

async function submitPayment() {
  if (!paymentTarget.value || !paymentForm.paymentDate || !paymentForm.paymentMethod || !paymentForm.transactionNumber.trim() || !paymentProof.value) {
    paymentError.value = '请填写付款日期、方式和流水号，并上传付款凭证。'
    return
  }
  if (paymentForm.paymentDate > localToday) {
    paymentError.value = '付款日期不能晚于今天。'
    return
  }
  paymentSubmitting.value = true
  paymentError.value = ''
  try {
    const proofFile = await files.uploadFile(paymentProof.value)
    const success = await app.registerPayment(paymentTarget.value, { paymentDate: paymentForm.paymentDate, paymentMethod: paymentForm.paymentMethod, transactionNumber: paymentForm.transactionNumber.trim(), proofFile })
    if (success) {
      paymentSubmitting.value = false
      closePayment()
    }
    else paymentError.value = ui.error || '付款登记失败，请刷新后重试。'
  } catch (cause) {
    paymentError.value = cause instanceof Error ? cause.message : '付款凭证上传失败。'
  } finally {
    paymentSubmitting.value = false
  }
}
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">EXPENSE MANAGEMENT</p><h1>费用报销</h1><p>管理费用申请、付款和报销进度。</p></div><button class="primary-action" @click="expenseStore.showExpenseForm = true">＋ 发起报销</button></div>
  <form class="filter-bar" @submit.prevent="search"><input v-model="expenseStore.expenseFilters.keyword" placeholder="单号、说明或申请人"><select v-model="expenseStore.expenseFilters.status"><option value="">全部状态</option><option value="0">草稿</option><option value="1">审批中</option><option value="2">已驳回</option><option value="3">已审批</option><option value="4">已完成</option><option value="5">已撤回</option></select><select v-model="expenseStore.expenseFilters.applicantId"><option value="">全部申请人</option><option v-for="employee in workspace.employees" :key="employee.id" :value="employee.id">{{ employee.name }}</option></select><label>开始<input v-model="expenseStore.expenseFilters.startDate" type="date"></label><label>结束<input v-model="expenseStore.expenseFilters.endDate" type="date"></label><input v-model="expenseStore.expenseFilters.minAmount" min="0" placeholder="最低金额" type="number"><input v-model="expenseStore.expenseFilters.maxAmount" min="0" placeholder="最高金额" type="number"><button type="submit">查询</button><button class="secondary" type="button" @click="resetFilters">重置</button></form>
  <section class="panel expense-panel"><div class="section-title"><div><p class="eyebrow">EXPENSE</p><h2>报销申请</h2></div><button @click="expenseStore.showExpenseForm = !expenseStore.showExpenseForm">{{ expenseStore.showExpenseForm ? '收起表单' : '新建报销' }}</button></div>
    <form v-if="expenseStore.showExpenseForm" class="leave-form" @submit.prevent="app.submitExpense"><label class="wide">关联出差申请（可选）<select v-model="expenseStore.expenseForm.travelRequestId"><option value="">不关联出差</option><option v-for="item in travel.approvedTravels" :key="item.id" :value="item.id">{{ item.number }} · {{ item.startDate }} 至 {{ item.endDate }} · {{ item.itinerary.map(line => line.destination).join('、') }}</option></select><small>仅显示本人已批准的出差申请，关联后可从报销详情追溯原申请。</small></label><label>费用类别<select v-model="expenseStore.expenseForm.category"><option>交通</option><option>住宿</option><option>餐饮招待</option><option>办公</option><option>通讯</option><option>培训</option><option>其他</option></select></label><label>费用日期<input v-model="expenseStore.expenseForm.expenseDate" type="date"></label><label>金额（元）<input v-model="expenseStore.expenseForm.amount" min="0.01" step="0.01" type="number"></label><label>票据号（可选）<input v-model="expenseStore.expenseForm.receiptNumber" placeholder="发票或凭证编号"></label><label class="wide">费用说明<textarea v-model="expenseStore.expenseForm.description" maxlength="500" placeholder="请填写费用用途"></textarea></label><fieldset class="wide copy-selector"><legend>抄送人（流程审批完成或撤回后通知）</legend><label v-for="employee in workspace.employees.filter(item => item.id !== auth.currentUserId)" :key="employee.id"><input v-model="expenseStore.expenseForm.copyRecipientIds" type="checkbox" :value="employee.id">{{ employee.name }} · {{ employee.role }}</label></fieldset><label class="wide">票据附件<input accept=".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.doc,.docx" multiple type="file" @change="selectAttachments"><small>支持 PDF、图片和 Office 文档，单个文件不超过 20MB。</small></label><div v-if="expenseStore.expenseForm.attachments.length" class="wide attachment-list">已上传 {{ expenseStore.expenseForm.attachments.length }} 个附件</div><div class="wide form-actions"><button :disabled="ui.submitting" type="submit">{{ ui.submitting ? '提交中…' : '保存并提交' }}</button></div></form>
    <template v-if="expenseStore.expenses.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>报销单号</th><th>费用说明</th><th>报销金额</th><th>状态</th><th>操作</th></tr></thead><tbody><tr v-for="expense in expenseStore.pagedExpenses.items" :key="expense.id"><td>{{ expense.number }}</td><td>{{ expense.description || '费用报销' }}</td><td>¥{{ expense.totalAmount.toFixed(2) }}</td><td><em>{{ expense.status }}</em></td><td class="task-actions"><button class="secondary" @click="router.push(`/expense/${expense.id}`)">详情</button><button v-if="expense.status === 'Approved' && auth.currentUser?.permissions?.includes('EXPENSE_PAY')" @click="openPayment(expense)">登记付款</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ expenseStore.pagedExpenses.total }} 条</span><div><button class="secondary" :disabled="expenseStore.pagedExpenses.currentPage === 1" @click="changePage(-1)">上一页</button><b>{{ expenseStore.pagedExpenses.currentPage }} / {{ expenseStore.pagedExpenses.totalPages }}</b><button class="secondary" :disabled="expenseStore.pagedExpenses.currentPage === expenseStore.pagedExpenses.totalPages" @click="changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前筛选条件下暂无报销记录。</p>
  </section>

  <OaDialog :open="Boolean(paymentTarget)" title="登记付款" :description="paymentTarget ? `${paymentTarget.number} · ¥${paymentTarget.totalAmount.toFixed(2)}` : ''" submit-label="确认付款" :busy="paymentSubmitting" @close="closePayment" @submit="submitPayment">
    <div v-if="paymentTarget" class="payment-summary"><span>本次实付金额</span><strong>¥{{ paymentTarget.totalAmount.toFixed(2) }}</strong><small>当前版本仅允许一次性全额付款。</small></div>
    <div class="dialog-grid"><label class="dialog-field">付款日期<input v-model="paymentForm.paymentDate" type="date" :max="localToday"></label><label class="dialog-field">付款方式<select v-model="paymentForm.paymentMethod"><option>银行转账</option><option>企业网银</option><option>现金</option><option>其他</option></select></label></div>
    <label class="dialog-field">付款流水号<input v-model="paymentForm.transactionNumber" maxlength="128" placeholder="银行流水号或付款凭证编号"></label>
    <label class="dialog-field">付款凭证<input accept=".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.doc,.docx" type="file" @change="selectPaymentProof"><small>支持 PDF、图片和 Office 文档，单个文件不超过 20MB。</small></label>
    <p v-if="paymentError" class="dialog-error">{{ paymentError }}</p>
  </OaDialog>
</template>
