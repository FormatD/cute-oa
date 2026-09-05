<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useApiClient } from '../api/client'
import type { Budget, ExpenseClaim, InvoiceType } from '../api/types'
import OaDialog from '../components/OaDialog.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useExpenseStore } from '../stores/expense'
import { useFileStore } from '../stores/files'
import { useTravelStore } from '../stores/travel'
import { useUiStore } from '../stores/ui'

const app = useAppStore()
const auth = useAuthStore()
const employeeDirectory = useEmployeeDirectoryStore()
const expenseStore = useExpenseStore()
const files = useFileStore()
const travel = useTravelStore()
const ui = useUiStore()
const router = useRouter()
const api = useApiClient()

const paymentTarget = ref<ExpenseClaim | null>(null)
const paymentProof = ref<File | null>(null)
const paymentSubmitting = ref(false)
const paymentError = ref('')
const today = new Date()
const localToday = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`

const paymentForm = reactive({
  batchTitle: '第 1 批付款',
  paidAmount: 0,
  paymentDate: localToday,
  paymentMethod: '银行转账',
  payerAccount: '955880001',
  payeeName: '',
  payeeAccount: '',
  payeeBank: '',
  transactionNumber: '',
  remarks: ''
})

const currentDeptBudget = ref<Budget | null>(null)
const budgetLoading = ref(false)

interface InvoiceItemState {
  invoiceType: InvoiceType
  invoiceCode: string
  invoiceNumber: string
  billingDate: string
  amountWithoutTax: number
  taxRate: number
  taxAmount: number
  totalAmount: number
  verificationCode: string
  duplicateError?: string
}

const invoiceList = ref<InvoiceItemState[]>([])

async function loadDeptBudget() {
  const dept = auth.currentUser?.departmentName
  if (!dept) return
  budgetLoading.value = true
  try {
    const list = await api.budgets.getBudgets({ departmentId: dept, year: today.getFullYear(), month: 0 })
    currentDeptBudget.value = list[0] ?? null
  } catch {
    currentDeptBudget.value = null
  } finally {
    budgetLoading.value = false
  }
}

onMounted(() => {
  void loadDeptBudget()
})

watch(() => expenseStore.showExpenseForm, (open) => {
  if (open) void loadDeptBudget()
})

function addInvoiceRow() {
  invoiceList.value.push({
    invoiceType: 'VatElectronic',
    invoiceCode: '',
    invoiceNumber: '',
    billingDate: localToday,
    amountWithoutTax: 0,
    taxRate: 0.06,
    taxAmount: 0,
    totalAmount: 0,
    verificationCode: ''
  })
}

function removeInvoiceRow(index: number) {
  invoiceList.value.splice(index, 1)
}

function updateInvoiceAmounts(row: InvoiceItemState) {
  row.taxAmount = Number((Number(row.amountWithoutTax || 0) * Number(row.taxRate || 0)).toFixed(2))
  row.totalAmount = Number((Number(row.amountWithoutTax || 0) + row.taxAmount).toFixed(2))
}

async function validateInvoiceFingerprint(row: InvoiceItemState) {
  if (!row.invoiceNumber.trim()) {
    row.duplicateError = ''
    return
  }
  try {
    const result = await api.payments.validateInvoice({
      invoiceType: row.invoiceType,
      invoiceCode: row.invoiceCode.trim() || null,
      invoiceNumber: row.invoiceNumber.trim()
    })
    if (!result.isValid) {
      row.duplicateError = result.errorMessage || '发票号码已存在冲突，禁止重复报销'
    } else {
      row.duplicateError = ''
    }
  } catch (cause) {
    row.duplicateError = cause instanceof Error ? cause.message : '发票防重查验失败'
  }
}

const isOverBudget = computed(() => {
  if (!currentDeptBudget.value) return false
  const claimAmt = Number(expenseStore.expenseForm.amount || 0)
  return claimAmt > currentDeptBudget.value.availableAmount
})

function selectAttachments(event: Event) {
  const selectedFiles = Array.from((event.target as HTMLInputElement).files ?? [])
  void Promise.all(selectedFiles.map(file => files.uploadFile(file))).then(ids => {
    expenseStore.expenseForm.attachments = ids
  }).catch(cause => {
    ui.error = cause instanceof Error ? cause.message : '附件上传失败。'
  })
}

function search() {
  expenseStore.expensePage = 1
  void expenseStore.loadExpenses()
}

function changePage(offset: number) {
  expenseStore.expensePage += offset
  void expenseStore.loadExpenses()
}

function resetFilters() {
  Object.assign(expenseStore.expenseFilters, { keyword: '', status: '', applicantId: '', startDate: '', endDate: '', minAmount: '', maxAmount: '' })
  search()
}

function openPayment(expense: ExpenseClaim) {
  paymentTarget.value = expense
  const paid = expense.paidTotalAmount ?? 0
  const remaining = Math.max(0, expense.totalAmount - paid)
  Object.assign(paymentForm, {
    batchTitle: paid > 0 ? '尾款支付' : '第 1 批付款',
    paidAmount: Number(remaining.toFixed(2)),
    paymentDate: localToday,
    paymentMethod: '银行转账',
    payerAccount: '955880001',
    payeeName: expense.payeeAccountName || expense.applicantName || '',
    payeeAccount: '6222026000001234',
    payeeBank: expense.bankName || '招商银行',
    transactionNumber: '',
    remarks: ''
  })
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
  if (!paymentTarget.value || !paymentForm.paymentDate || !paymentForm.paymentMethod || !paymentForm.transactionNumber.trim()) {
    paymentError.value = '请填写付款日期、方式和银行流水号。'
    return
  }
  if (paymentForm.paidAmount <= 0) {
    paymentError.value = '付款金额必须大于 0。'
    return
  }
  const remaining = paymentTarget.value.totalAmount - (paymentTarget.value.paidTotalAmount ?? 0)
  if (paymentForm.paidAmount > remaining) {
    paymentError.value = `本次付款金额不能超过待付余额 (¥${remaining.toFixed(2)})。`
    return
  }
  if (paymentForm.paymentDate > localToday) {
    paymentError.value = '付款日期不能晚于今天。'
    return
  }
  paymentSubmitting.value = true
  paymentError.value = ''
  try {
    let proofId: string | null = null
    if (paymentProof.value) {
      proofId = await files.uploadFile(paymentProof.value)
    }
    await api.payments.registerExpensePayment(paymentTarget.value.id, {
      batchTitle: paymentForm.batchTitle.trim(),
      paymentDate: paymentForm.paymentDate,
      paymentMethod: paymentForm.paymentMethod,
      payerAccount: paymentForm.payerAccount.trim(),
      payeeName: paymentForm.payeeName.trim(),
      payeeAccount: paymentForm.payeeAccount.trim(),
      payeeBank: paymentForm.payeeBank.trim(),
      transactionNumber: paymentForm.transactionNumber.trim(),
      paidAmount: paymentForm.paidAmount,
      feeAmount: 0,
      proofAttachmentId: proofId,
      remarks: paymentForm.remarks.trim() || null
    })
    ui.message = '付款登记成功！'
    closePayment()
    await expenseStore.loadExpenses()
  } catch (cause) {
    paymentError.value = cause instanceof Error ? cause.message : '付款登记失败，请刷新后重试。'
  } finally {
    paymentSubmitting.value = false
  }
}

async function handleExpenseSubmit() {
  if (invoiceList.value.some(inv => Boolean(inv.duplicateError))) {
    ui.error = '存在冲突或无效的发票号码，请修正后再提交。'
    return
  }
  expenseStore.expenseForm.invoices = invoiceList.value.map(inv => ({
    invoiceType: inv.invoiceType,
    invoiceCode: inv.invoiceCode.trim() || null,
    invoiceNumber: inv.invoiceNumber.trim(),
    billingDate: inv.billingDate,
    amountWithoutTax: Number(inv.amountWithoutTax),
    taxRate: Number(inv.taxRate),
    taxAmount: Number(inv.taxAmount),
    totalAmount: Number(inv.totalAmount),
    verificationCode: inv.verificationCode.trim() || null,
    attachmentId: null
  }))
  const success = await app.submitExpense()
  if (success) {
    invoiceList.value = []
    void loadDeptBudget()
  }
}

async function exportExpenseCsv() {
  try {
    await api.payments.exportExpensesCsv({
      applicantId: expenseStore.expenseFilters.applicantId || undefined,
      startDate: expenseStore.expenseFilters.startDate || undefined,
      endDate: expenseStore.expenseFilters.endDate || undefined
    })
    ui.message = '报销对账明细导出成功。'
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '导出失败。'
  }
}
</script>

<template>
  <div class="page-heading">
    <div>
      <p class="eyebrow">EXPENSE MANAGEMENT</p>
      <h1>费用报销</h1>
      <p>管理费用申请、预算水位、发票查验与分期打款闭环。</p>
    </div>
    <div class="task-actions">
      <button
        v-if="auth.currentUser?.permissions?.includes('EXPENSE_PAY') || auth.currentUser?.permissions?.includes('EXPENSE_ALL_VIEW')"
        class="secondary"
        type="button"
        @click="exportExpenseCsv"
      >
        📥 导出财务对账 CSV
      </button>
      <button class="primary-action" @click="expenseStore.showExpenseForm = true">＋ 发起报销</button>
    </div>
  </div>

  <form class="filter-bar" @submit.prevent="search">
    <input v-model="expenseStore.expenseFilters.keyword" placeholder="单号、说明或申请人">
    <select v-model="expenseStore.expenseFilters.status">
      <option value="">全部状态</option>
      <option value="0">草稿</option>
      <option value="1">审批中</option>
      <option value="2">已驳回</option>
      <option value="3">已审批</option>
      <option value="4">已完成</option>
      <option value="5">已撤回</option>
    </select>
    <select v-model="expenseStore.expenseFilters.applicantId">
      <option value="">全部申请人</option>
      <option v-for="employee in employeeDirectory.employees" :key="employee.id" :value="employee.id">{{ employee.name }}</option>
    </select>
    <label>开始<input v-model="expenseStore.expenseFilters.startDate" type="date"></label>
    <label>结束<input v-model="expenseStore.expenseFilters.endDate" type="date"></label>
    <input v-model="expenseStore.expenseFilters.minAmount" min="0" placeholder="最低金额" type="number">
    <input v-model="expenseStore.expenseFilters.maxAmount" min="0" placeholder="最高金额" type="number">
    <button type="submit">查询</button>
    <button class="secondary" type="button" @click="resetFilters">重置</button>
  </form>

  <section class="panel expense-panel">
    <div class="section-title">
      <div>
        <p class="eyebrow">EXPENSE</p>
        <h2>报销申请</h2>
      </div>
      <button class="secondary" @click="expenseStore.showExpenseForm = true">
        新建报销
      </button>
    </div>

    <template v-if="expenseStore.expenses.length">
      <div class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>报销单号</th>
              <th>费用说明</th>
              <th>报销金额</th>
              <th>已付 / 付款状态</th>
              <th>发票</th>
              <th>流程状态</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="expense in expenseStore.pagedExpenses.items" :key="expense.id">
              <td>{{ expense.number }}</td>
              <td>{{ expense.description || '费用报销' }}</td>
              <td>¥{{ expense.totalAmount.toFixed(2) }}</td>
              <td>
                ¥{{ (expense.paidTotalAmount ?? 0).toFixed(2) }}
                <span
                  class="invoice-badge"
                  :class="{
                    paid: expense.paymentStatus === 'PAID',
                    released: !expense.paymentStatus || expense.paymentStatus === 'UNPAID'
                  }"
                  style="margin-left: 6px;"
                >
                  {{ expense.paymentStatus === 'PAID' ? '已结清' : expense.paymentStatus === 'PARTIALLY_PAID' ? '部分打款' : '未付款' }}
                </span>
              </td>
              <td>{{ expense.invoiceCount ?? expense.invoices?.length ?? 0 }} 张</td>
              <td><em>{{ expense.status }}</em></td>
              <td class="task-actions">
                <button class="secondary" @click="router.push(`/expense/${expense.id}`)">详情</button>
                <button
                  v-if="(expense.status === 'Approved' || expense.paymentStatus === 'PARTIALLY_PAID') && expense.paymentStatus !== 'PAID' && auth.currentUser?.permissions?.includes('EXPENSE_PAY')"
                  @click="openPayment(expense)"
                >
                  分期打款
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="pagination">
        <span>共 {{ expenseStore.pagedExpenses.total }} 条</span>
        <div>
          <button class="secondary" :disabled="expenseStore.pagedExpenses.currentPage === 1" @click="changePage(-1)">上一页</button>
          <b>{{ expenseStore.pagedExpenses.currentPage }} / {{ expenseStore.pagedExpenses.totalPages }}</b>
          <button class="secondary" :disabled="expenseStore.pagedExpenses.currentPage === expenseStore.pagedExpenses.totalPages" @click="changePage(1)">下一页</button>
        </div>
      </div>
    </template>
    <p v-else class="empty">当前筛选条件下暂无报销记录。</p>
  </section>

  <!-- 分期打款登记弹窗 -->
  <OaDialog
    :open="Boolean(paymentTarget)"
    title="财务分期打款登记"
    :description="paymentTarget ? `${paymentTarget.number} · 报销总额 ¥${paymentTarget.totalAmount.toFixed(2)}` : ''"
    submit-label="确认打款"
    :busy="paymentSubmitting"
    @close="closePayment"
    @submit="submitPayment"
  >
    <div v-if="paymentTarget" class="payment-summary">
      <div>
        <span>已付总额：¥{{ (paymentTarget.paidTotalAmount ?? 0).toFixed(2) }}</span>
        <small>本次剩余待付额度：¥{{ Math.max(0, paymentTarget.totalAmount - (paymentTarget.paidTotalAmount ?? 0)).toFixed(2) }}</small>
      </div>
      <strong>本次支付：¥{{ paymentForm.paidAmount.toFixed(2) }}</strong>
    </div>
    <div class="dialog-grid">
      <label class="dialog-field">批次标题
        <input v-model="paymentForm.batchTitle" maxlength="64" placeholder="例如 第 1 批首期款">
      </label>
      <label class="dialog-field">本次实付金额（元）
        <input v-model.number="paymentForm.paidAmount" type="number" min="0.01" step="0.01">
      </label>
      <label class="dialog-field">打款日期
        <input v-model="paymentForm.paymentDate" type="date" :max="localToday">
      </label>
      <label class="dialog-field">支付方式
        <select v-model="paymentForm.paymentMethod">
          <option value="BANK_TRANSFER">银行转账</option>
          <option value="CORPORATE_ALIPAY">企业支付宝</option>
          <option value="CORPORATE_WECHAT">企业微信</option>
          <option value="CHEQUE">支票</option>
          <option value="CASH">现金</option>
        </select>
      </label>
      <label class="dialog-field">付款企业账户
        <input v-model="paymentForm.payerAccount" maxlength="64">
      </label>
      <label class="dialog-field">收款人账户
        <input v-model="paymentForm.payeeAccount" maxlength="64">
      </label>
    </div>
    <label class="dialog-field">银行转账流水号 *
      <input v-model="paymentForm.transactionNumber" maxlength="128" placeholder="银行电子回单流水号（全局唯一防重）" required>
    </label>
    <label class="dialog-field">付款凭证 / 回单
      <input accept=".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.doc,.docx" type="file" @change="selectPaymentProof">
      <small>支持 PDF、图片和 Office 文档，单个文件不超过 20MB。</small>
    </label>
    <label class="dialog-field">打款备注
      <input v-model="paymentForm.remarks" maxlength="200" placeholder="选填">
    </label>
    <p v-if="paymentError" class="dialog-error">{{ paymentError }}</p>
  </OaDialog>

  <!-- 新建报销弹窗 -->
  <OaDialog
    :open="expenseStore.showExpenseForm"
    title="新建报销申请"
    description="录入报销项目、结构化发票与关联出差单，支持防重查验与预算预警。"
    submit-label="保存并提交"
    :busy="ui.submitting"
    width="920px"
    @close="expenseStore.showExpenseForm = false"
    @submit="handleExpenseSubmit"
  >
    <!-- 部门预算卡片 -->
    <div v-if="currentDeptBudget" class="budget-summary-card" style="margin-bottom: 8px;">
      <h4>📊 部门预算水位池（{{ currentDeptBudget.departmentId }} · {{ currentDeptBudget.year }} 年度）</h4>
      <div class="budget-stat-grid">
        <div><span>年度编制总额</span><strong>¥{{ currentDeptBudget.allocatedAmount.toFixed(2) }}</strong></div>
        <div><span>审批预占额度</span><strong>¥{{ currentDeptBudget.committedAmount.toFixed(2) }}</strong></div>
        <div><span>累计实支结转</span><strong>¥{{ currentDeptBudget.actualAmount.toFixed(2) }}</strong></div>
        <div><span>当前可用额度</span><strong :class="{ 'warning-text': isOverBudget }">¥{{ currentDeptBudget.availableAmount.toFixed(2) }}</strong></div>
      </div>
      <p v-if="isOverBudget" class="dialog-error" style="margin-top: 10px;">
        ⚠️ 提示：当前填写的报销金额（¥{{ Number(expenseStore.expenseForm.amount || 0).toFixed(2) }}）已超出部门可用预算池剩余额度（¥{{ currentDeptBudget.availableAmount.toFixed(2) }}）。
      </p>
    </div>

    <label class="dialog-field">关联出差申请（可选）
      <select v-model="expenseStore.expenseForm.travelRequestId">
        <option value="">不关联出差</option>
        <option v-for="item in travel.approvedTravels" :key="item.id" :value="item.id">
          {{ item.number }} · {{ item.startDate }} 至 {{ item.endDate }} · {{ item.itinerary.map(line => line.destination).join('、') }}
        </option>
      </select>
      <small>仅显示本人已批准的出差申请，关联后可从报销详情追溯原申请。</small>
    </label>

    <div class="dialog-grid">
      <label class="dialog-field">费用类别
        <select v-model="expenseStore.expenseForm.category">
          <option>交通</option>
          <option>住宿</option>
          <option>餐饮招待</option>
          <option>办公</option>
          <option>通讯</option>
          <option>培训</option>
          <option>其他</option>
        </select>
      </label>
      <label class="dialog-field">费用日期<input v-model="expenseStore.expenseForm.expenseDate" type="date"></label>
      <label class="dialog-field">金额（元）<input v-model="expenseStore.expenseForm.amount" min="0.01" step="0.01" type="number"></label>
      <label class="dialog-field">手工票据号（可选）<input v-model="expenseStore.expenseForm.receiptNumber" placeholder="纸质票据编号"></label>
    </div>

    <label class="dialog-field">费用说明<textarea v-model="expenseStore.expenseForm.description" maxlength="500" placeholder="请填写费用用途"></textarea></label>

    <!-- 结构化发票录入 -->
    <fieldset class="itinerary-editor">
      <legend>结构化发票清单（支持即时防重查验）</legend>
      <div v-for="(inv, idx) in invoiceList" :key="idx" class="invoice-row">
        <select v-model="inv.invoiceType" title="发票类型">
          <option value="VatElectronic">数电/电子普票</option>
          <option value="VatSpecial">增值税专用发票</option>
          <option value="VatNormal">增值税普通发票</option>
          <option value="TrainTicket">铁路车票</option>
          <option value="AirItinerary">机票行程单</option>
          <option value="QuotaInvoice">定额发票</option>
          <option value="OtherReceipt">其他合规凭证</option>
        </select>
        <input v-model="inv.invoiceCode" placeholder="发票代码" maxlength="32">
        <input v-model="inv.invoiceNumber" placeholder="发票号码 *" maxlength="64" required @blur="validateInvoiceFingerprint(inv)">
        <input v-model="inv.billingDate" type="date" title="开票日期 *" required>
        <input v-model.number="inv.amountWithoutTax" type="number" step="0.01" min="0" placeholder="不含税金额" @input="updateInvoiceAmounts(inv)">
        <input v-model.number="inv.taxRate" type="number" step="0.01" min="0" max="1" placeholder="税率(如0.06)" @input="updateInvoiceAmounts(inv)">
        <input v-model.number="inv.totalAmount" type="number" step="0.01" min="0" placeholder="价税合计 *" required>
        <button class="secondary" type="button" @click="removeInvoiceRow(idx)">删除</button>
        <div v-if="inv.duplicateError" class="invoice-error">❌ {{ inv.duplicateError }}</div>
      </div>
      <div style="margin-top: 8px;">
        <button class="secondary" type="button" @click="addInvoiceRow">＋ 添加发票明细</button>
        <small style="margin-left: 12px; color: #64748b;">支持发票号码失焦自动查重；开票日期不得超过 180 天。</small>
      </div>
    </fieldset>

    <fieldset class="copy-selector">
      <legend>抄送人（流程审批完成或撤回后通知）</legend>
      <label v-for="employee in employeeDirectory.employees.filter(item => item.id !== auth.currentUserId)" :key="employee.id">
        <input v-model="expenseStore.expenseForm.copyRecipientIds" type="checkbox" :value="employee.id">
        {{ employee.name }} · {{ employee.role }}
      </label>
    </fieldset>

    <label class="dialog-field">票据附件
      <input accept=".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.doc,.docx" multiple type="file" @change="selectAttachments">
      <small>支持 PDF、图片和 Office 文档，单个文件不超过 20MB。</small>
    </label>
    <div v-if="expenseStore.expenseForm.attachments.length" class="attachment-list">
      已上传 {{ expenseStore.expenseForm.attachments.length }} 个附件
    </div>
    <p v-if="ui.error" class="dialog-error">{{ ui.error }}</p>
  </OaDialog>
</template>
