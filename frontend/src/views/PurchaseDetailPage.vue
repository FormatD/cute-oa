<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useApiClient } from '../api/client'
import type { PurchaseReconciliation } from '../api/types'
import FlowInstanceTimeline from '../components/FlowInstanceTimeline.vue'
import OaDialog from '../components/OaDialog.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useFileStore } from '../stores/files'
import { usePurchaseStore } from '../stores/purchase'
import { useUiStore } from '../stores/ui'
import { businessStatusLabel } from '../utils/businessLabels'

const props = defineProps<{ id: string }>()
const app = useAppStore()
const auth = useAuthStore()
const employees = useEmployeeDirectoryStore()
const files = useFileStore()
const purchase = usePurchaseStore()
const ui = useUiStore()
const router = useRouter()
const api = useApiClient()

const loading = ref(false)
const orderOpen = ref(false)
const receiptOpen = ref(false)
const paymentOpen = ref(false)
const paymentSubmitting = ref(false)
const paymentError = ref('')
const recon = ref<PurchaseReconciliation | null>(null)

const today = new Date()
const localToday = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`

const canManage = computed(() => auth.currentUser?.permissions?.includes('PURCHASE_MANAGE') === true)
const canFinance = computed(() => auth.currentUser?.permissions?.includes('EXPENSE_PAY') === true || canManage.value)
const canReceive = computed(() => purchase.purchaseDetail?.applicantId === auth.currentUserId || canManage.value)
const copyNames = computed(() => (purchase.purchaseDetail?.copyRecipientIds ?? []).map(id => employees.employees.find(item => item.id === id)?.name ?? id).join('、') || '无')

const paymentForm = reactive({
  batchTitle: '首期预付款',
  paidAmount: 0,
  paymentDate: localToday,
  paymentMethod: 'BANK_TRANSFER',
  payerAccount: '955880001',
  payeeName: '',
  payeeAccount: '6222026000008888',
  payeeBank: '招商银行',
  transactionNumber: '',
  remarks: ''
})
const paymentProof = ref<File | null>(null)

async function loadReconciliation() {
  try {
    recon.value = await api.payments.getPurchaseReconciliation(props.id)
  } catch {
    recon.value = null
  }
}

async function load() {
  loading.value = true
  ui.error = ''
  try {
    await Promise.all([
      purchase.loadPurchaseDetail(props.id),
      loadReconciliation()
    ])
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '采购详情加载失败。'
  } finally {
    loading.value = false
  }
}

async function uploadOrderFiles(event: Event) {
  try {
    purchase.orderForm.attachments.push(...await Promise.all(Array.from((event.target as HTMLInputElement).files ?? []).map(file => files.uploadFile(file))))
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '附件上传失败。'
  }
}

async function uploadReceiptFiles(event: Event) {
  try {
    purchase.receiptForm.attachments.push(...await Promise.all(Array.from((event.target as HTMLInputElement).files ?? []).map(file => files.uploadFile(file))))
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '附件上传失败。'
  }
}

async function submitOrder() {
  if (purchase.purchaseDetail && await app.registerPurchaseOrder(purchase.purchaseDetail)) {
    orderOpen.value = false
    await load()
  }
}

async function submitReceipt() {
  if (purchase.purchaseDetail && await app.receivePurchase(purchase.purchaseDetail)) {
    receiptOpen.value = false
    await load()
  }
}

function openPaymentDialog() {
  paymentError.value = ''
  if (!recon.value) return
  const isAccepted = recon.value.isAcceptancePassed
  const maxAllowed = isAccepted ? recon.value.orderedAmount : recon.value.maxPrepaymentAllowed
  const remaining = Math.max(0, maxAllowed - recon.value.paidAmount)
  paymentForm.batchTitle = recon.value.paidAmount > 0 ? (isAccepted ? '验收付清尾款' : '第二期款') : '首期预付款 (40%)'
  paymentForm.paidAmount = Number(remaining.toFixed(2))
  paymentForm.paymentDate = localToday
  paymentForm.paymentMethod = 'BANK_TRANSFER'
  paymentForm.payerAccount = '955880001'
  paymentForm.payeeName = purchase.purchaseDetail?.order?.supplier || '戴尔(中国)有限公司'
  paymentForm.payeeAccount = '6222026000008888'
  paymentForm.payeeBank = '招商银行'
  paymentForm.transactionNumber = ''
  paymentForm.remarks = ''
  paymentProof.value = null
  paymentOpen.value = true
}

function selectPaymentProof(event: Event) {
  paymentProof.value = (event.target as HTMLInputElement).files?.[0] ?? null
}

async function submitPayment() {
  if (!paymentForm.transactionNumber.trim()) {
    paymentError.value = '请填写银行转账流水号。'
    return
  }
  if (paymentForm.paidAmount <= 0) {
    paymentError.value = '付款金额必须大于 0。'
    return
  }
  if (!recon.value) return
  if (!recon.value.isAcceptancePassed) {
    if (recon.value.paidAmount + paymentForm.paidAmount > recon.value.maxPrepaymentAllowed) {
      paymentError.value = `未完成验收前，累计预付款不能超过限额 ¥${recon.value.maxPrepaymentAllowed.toFixed(2)}。`
      return
    }
  } else {
    if (recon.value.paidAmount + paymentForm.paidAmount > recon.value.orderedAmount) {
      paymentError.value = `累计付款不能超过合同总额 ¥${recon.value.orderedAmount.toFixed(2)}。`
      return
    }
  }

  paymentSubmitting.value = true
  paymentError.value = ''
  try {
    let proofId: string | null = null
    if (paymentProof.value) {
      proofId = await files.uploadFile(paymentProof.value)
    }
    await api.payments.registerPurchasePayment(props.id, {
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
    ui.message = '采购付款登记成功！'
    paymentOpen.value = false
    await load()
  } catch (cause) {
    paymentError.value = cause instanceof Error ? cause.message : '付款登记失败。'
  } finally {
    paymentSubmitting.value = false
  }
}

async function exportPurchaseCsv() {
  try {
    await api.payments.exportPurchasesCsv({
      applicantId: purchase.purchaseDetail?.applicantId
    })
    ui.message = '采购四单对账明细导出成功。'
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '导出失败。'
  }
}

onMounted(load)
</script>

<template>
  <p v-if="ui.message" class="notice success">{{ ui.message }}</p>
  <p v-if="ui.error" class="notice error">{{ ui.error }}</p>
  <section class="panel detail-page">
    <div class="section-title">
      <div>
        <p class="eyebrow">PURCHASE DETAIL</p>
        <h2>采购申请详情</h2>
      </div>
      <div class="task-actions">
        <button v-if="canFinance" class="secondary" type="button" @click="exportPurchaseCsv">
          📥 导出对账 CSV
        </button>
        <button class="secondary" @click="router.push('/purchase')">← 返回列表</button>
      </div>
    </div>
    <p v-if="loading" class="empty">正在加载详情…</p>
    <template v-else-if="purchase.purchaseDetail">
      <div class="detail-status">
        <strong>{{ purchase.purchaseDetail.number }} · {{ purchase.purchaseDetail.title }}</strong>
        <em>{{ businessStatusLabel(purchase.purchaseDetail.status) }}</em>
      </div>
      <dl class="detail-grid">
        <div><dt>申请人</dt><dd>{{ purchase.purchaseDetail.applicantName }}</dd></div>
        <div><dt>所属部门</dt><dd>{{ purchase.purchaseDetail.departmentName }}</dd></div>
        <div><dt>期望到货</dt><dd>{{ purchase.purchaseDetail.requiredDate }}</dd></div>
        <div><dt>建议供应商</dt><dd>{{ purchase.purchaseDetail.suggestedSupplier || '未指定' }}</dd></div>
        <div><dt>预估总额</dt><dd>¥{{ purchase.purchaseDetail.estimatedTotal.toFixed(2) }}</dd></div>
        <div>
          <dt>付款状态</dt>
          <dd>
            <span
              class="invoice-badge"
              :class="{
                paid: purchase.purchaseDetail.paymentStatus === 'PAID',
                released: !purchase.purchaseDetail.paymentStatus || purchase.purchaseDetail.paymentStatus === 'UNPAID'
              }"
            >
              {{ purchase.purchaseDetail.paymentStatus === 'PAID' ? '已结清' : purchase.purchaseDetail.paymentStatus === 'PARTIALLY_PAID' ? '部分打款' : '未付款' }}
            </span>
            <span v-if="purchase.purchaseDetail.paidTotalAmount" style="margin-left: 6px; font-size: 0.8rem; color: #64748b;">
              (已付 ¥{{ purchase.purchaseDetail.paidTotalAmount.toFixed(2) }})
            </span>
          </dd>
        </div>
        <div class="wide"><dt>采购用途</dt><dd>{{ purchase.purchaseDetail.purpose }}</dd></div>
        <div class="wide"><dt>抄送人</dt><dd>{{ copyNames }}</dd></div>
        <div class="wide"><dt>报价或依据附件</dt>
          <dd v-if="purchase.purchaseDetail.attachments.length">
            <button v-for="fileId in purchase.purchaseDetail.attachments" :key="fileId" class="secondary attachment-button" @click="files.downloadAttachment(fileId, 'purchase', purchase.purchaseDetail!.id)">
              下载附件 {{ fileId.slice(0, 8) }}
            </button>
          </dd>
          <dd v-else>无</dd>
        </div>
      </dl>

      <div class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>品类</th>
              <th>名称</th>
              <th>规格</th>
              <th>数量</th>
              <th>预估单价</th>
              <th>小计</th>
              <th>备注</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(item, index) in purchase.purchaseDetail.items" :key="index">
              <td>{{ item.category }}</td>
              <td>{{ item.name }}</td>
              <td>{{ item.specification || '—' }}</td>
              <td>{{ item.quantity }} {{ item.unit }}</td>
              <td>¥{{ item.estimatedUnitPrice.toFixed(2) }}</td>
              <td>¥{{ item.estimatedAmount.toFixed(2) }}</td>
              <td>{{ item.remark || '—' }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <section v-if="purchase.purchaseDetail.order" class="embedded-card">
        <h3>采购订单</h3>
        <dl class="detail-grid">
          <div><dt>供应商</dt><dd>{{ purchase.purchaseDetail.order.supplier }}</dd></div>
          <div><dt>订单号</dt><dd>{{ purchase.purchaseDetail.order.orderNumber }}</dd></div>
          <div><dt>实际金额</dt><dd>¥{{ purchase.purchaseDetail.order.actualAmount.toFixed(2) }}</dd></div>
          <div><dt>下单日期</dt><dd>{{ purchase.purchaseDetail.order.orderDate }}</dd></div>
          <div><dt>预计交付</dt><dd>{{ purchase.purchaseDetail.order.expectedDeliveryDate }}</dd></div>
          <div><dt>登记人</dt><dd>{{ purchase.purchaseDetail.order.createdByName }}</dd></div>
          <div class="wide"><dt>备注</dt><dd>{{ purchase.purchaseDetail.order.notes || '无' }}</dd></div>
          <div class="wide"><dt>订单附件</dt>
            <dd v-if="purchase.purchaseDetail.order.attachments.length">
              <button v-for="fileId in purchase.purchaseDetail.order.attachments" :key="fileId" class="secondary attachment-button" @click="files.downloadAttachment(fileId, 'purchase', purchase.purchaseDetail!.id)">
                下载附件 {{ fileId.slice(0, 8) }}
              </button>
            </dd>
            <dd v-else>无</dd>
          </div>
        </dl>
      </section>

      <section v-if="purchase.purchaseDetail.receipt" class="embedded-card">
        <h3>验收记录</h3>
        <dl class="detail-grid">
          <div><dt>验收日期</dt><dd>{{ purchase.purchaseDetail.receipt.receivedDate }}</dd></div>
          <div><dt>验收结果</dt><dd>全部验收通过</dd></div>
          <div><dt>验收人</dt><dd>{{ purchase.purchaseDetail.receipt.createdByName }}</dd></div>
          <div class="wide"><dt>验收说明</dt><dd>{{ purchase.purchaseDetail.receipt.notes }}</dd></div>
          <div class="wide"><dt>验收附件</dt>
            <dd v-if="purchase.purchaseDetail.receipt.attachments.length">
              <button v-for="fileId in purchase.purchaseDetail.receipt.attachments" :key="fileId" class="secondary attachment-button" @click="files.downloadAttachment(fileId, 'purchase', purchase.purchaseDetail!.id)">
                下载附件 {{ fileId.slice(0, 8) }}
              </button>
            </dd>
            <dd v-else>无</dd>
          </div>
        </dl>
      </section>

      <!-- 采购四单对账看板 -->
      <section v-if="recon" class="embedded-card">
        <h3>📊 采购四单对账看板（申请 vs 订单 vs 验收 vs 实付）</h3>
        <div class="reconciliation-board">
          <div class="recon-card">
            <span>1. 申请预估金额</span>
            <strong>¥{{ recon.estimatedAmount.toFixed(2) }}</strong>
            <small>单号 {{ recon.purchaseRequestNumber }}</small>
          </div>
          <div class="recon-card">
            <span>2. 合同下单金额</span>
            <strong>¥{{ recon.orderedAmount.toFixed(2) }}</strong>
            <small>{{ purchase.purchaseDetail.order ? purchase.purchaseDetail.order.supplier : '尚未下单' }}</small>
          </div>
          <div class="recon-card">
            <span>3. 验收入库状态</span>
            <strong>¥{{ recon.acceptedAmount.toFixed(2) }}</strong>
            <small :class="{ unaccepted: !recon.isAcceptancePassed }">
              {{ recon.isAcceptancePassed ? '✅ 全部验收通过' : '⏳ 尚未到货验收' }}
            </small>
          </div>
          <div class="recon-card">
            <span>4. 累计打款实付</span>
            <strong>¥{{ recon.paidAmount.toFixed(2) }}</strong>
            <small :class="{ unaccepted: recon.remainingPayable > 0 }">
              {{ recon.paymentStatus === 'PAID' ? '✅ 已全部结清' : `待付敞口 ¥${recon.remainingPayable.toFixed(2)}` }}
            </small>
          </div>
        </div>
        <p v-if="!recon.isAcceptancePassed" style="margin-top: 10px; color: #b45309; font-size: 0.78rem;">
          🛡️ 预付款风控规则：货物到货验收前，首付款累计比例不得超过合同总额的 {{ (recon.prepaymentLimitRate * 100).toFixed(0) }}%（最高允许付款 ¥{{ recon.maxPrepaymentAllowed.toFixed(2) }}）。
        </p>
      </section>

      <!-- 分期打款流水记录 -->
      <section v-if="recon?.payments?.length" class="embedded-card">
        <h3>分期打款流水记录（共 {{ recon.payments.length }} 笔）</h3>
        <div class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>批次</th>
                <th>打款日期</th>
                <th>支付方式</th>
                <th>实付金额</th>
                <th>银行流水号</th>
                <th>经办财务</th>
                <th>收款账号</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="pay in recon.payments" :key="pay.id">
                <td>{{ pay.batchTitle || `第 ${pay.sequence} 笔` }}</td>
                <td>{{ pay.paymentDate }}</td>
                <td>{{ pay.paymentMethod }}</td>
                <td><strong>¥{{ pay.paidAmount.toFixed(2) }}</strong></td>
                <td><code>{{ pay.transactionNumber }}</code></td>
                <td>{{ pay.operatorName }}</td>
                <td>{{ pay.payeeAccountMasked }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <FlowInstanceTimeline :instances="purchase.purchaseDetail.flowInstances ?? []" :current-id="purchase.purchaseDetail.currentFlowInstanceId" />
      <div v-if="purchase.purchaseDetail.tasks?.length" class="timeline">
        <h3>当前实例任务</h3>
        <p v-for="task in purchase.purchaseDetail.tasks" :key="task.id">
          第 {{ task.sequence }} 节点 · {{ task.assigneeName }}
          <span v-if="task.originalAssigneeName">（代 {{ task.originalAssigneeName }} 审批）</span>
          · {{ businessStatusLabel(task.status) }}
          <span v-if="task.comment">：{{ task.comment }}</span>
        </p>
      </div>

      <div class="detail-actions">
        <button v-if="purchase.purchaseDetail.status === 'Approving' && purchase.purchaseDetail.applicantId === auth.currentUserId" :disabled="purchase.operationBusy" @click="app.withdrawPurchase(purchase.purchaseDetail)">
          撤回申请
        </button>
        <button v-if="purchase.purchaseDetail.status === 'Approved' && canManage" :disabled="purchase.operationBusy" @click="orderOpen = true">
          登记下单
        </button>
        <button v-if="purchase.purchaseDetail.status === 'Ordered' && canReceive" :disabled="purchase.operationBusy" @click="receiptOpen = true">
          登记验收
        </button>
        <button
          v-if="(purchase.purchaseDetail.status === 'Ordered' || purchase.purchaseDetail.status === 'Received') && canFinance && recon?.paymentStatus !== 'PAID'"
          :disabled="purchase.operationBusy"
          @click="openPaymentDialog"
        >
          分期登记付款
        </button>
      </div>
    </template>
    <p v-else class="empty">未找到该记录，可能已被删除或当前无查看权限。</p>
  </section>

  <!-- 登记下单弹窗 -->
  <OaDialog :open="orderOpen" title="登记采购订单" description="实际金额不能超过审批预估金额的 110%。" submit-label="确认下单" :busy="purchase.operationBusy" @close="orderOpen = false" @submit="submitOrder">
    <div class="dialog-grid">
      <label class="dialog-field">供应商<input v-model="purchase.orderForm.supplier" maxlength="100"></label>
      <label class="dialog-field">采购订单号<input v-model="purchase.orderForm.orderNumber" maxlength="64"></label>
      <label class="dialog-field">实际金额（元）<input v-model="purchase.orderForm.actualAmount" type="number" min="0.01" step="0.01"></label>
      <label class="dialog-field">下单日期<input v-model="purchase.orderForm.orderDate" type="date"></label>
      <label class="dialog-field">预计交付日期<input v-model="purchase.orderForm.expectedDeliveryDate" type="date"></label>
    </div>
    <label class="dialog-field">备注<textarea v-model="purchase.orderForm.notes" maxlength="500"></textarea></label>
    <label class="dialog-field">订单附件<input type="file" multiple @change="uploadOrderFiles"><small>已上传 {{ purchase.orderForm.attachments.length }} 个附件</small></label>
  </OaDialog>

  <!-- 登记验收弹窗 -->
  <OaDialog :open="receiptOpen" title="登记采购验收" description="当前版本仅支持全部验收通过；部分到货请暂不提交。" submit-label="确认验收" :busy="purchase.operationBusy" @close="receiptOpen = false" @submit="submitReceipt">
    <label class="dialog-field">验收日期<input v-model="purchase.receiptForm.receivedDate" type="date"></label>
    <label class="dialog-field">验收结果<select v-model="purchase.receiptForm.result"><option value="ALL_ACCEPTED">全部验收通过</option></select></label>
    <label class="dialog-field">验收说明<textarea v-model="purchase.receiptForm.notes" maxlength="500" placeholder="说明数量、规格和质量核验结果"></textarea></label>
    <label class="dialog-field">验收附件<input type="file" multiple @change="uploadReceiptFiles"><small>已上传 {{ purchase.receiptForm.attachments.length }} 个附件</small></label>
  </OaDialog>

  <!-- 分期打款登记弹窗 -->
  <OaDialog
    :open="paymentOpen"
    title="采购分期付款登记"
    :description="recon ? `合同下单金额 ¥${recon.orderedAmount.toFixed(2)} · 待付敞口 ¥${recon.remainingPayable.toFixed(2)}` : ''"
    submit-label="确认打款"
    :busy="paymentSubmitting"
    @close="paymentOpen = false"
    @submit="submitPayment"
  >
    <div v-if="recon" class="payment-summary">
      <div>
        <span>已付总额：¥{{ recon.paidAmount.toFixed(2) }}</span>
        <small v-if="!recon.isAcceptancePassed">
          未验收首付限额：¥{{ recon.maxPrepaymentAllowed.toFixed(2) }} (当前可付上限 ¥{{ Math.max(0, recon.maxPrepaymentAllowed - recon.paidAmount).toFixed(2) }})
        </small>
        <small v-else>
          已通过验收，可支付全额尾款
        </small>
      </div>
      <strong>本次打款：¥{{ paymentForm.paidAmount.toFixed(2) }}</strong>
    </div>
    <div class="dialog-grid">
      <label class="dialog-field">批次说明<input v-model="paymentForm.batchTitle" maxlength="64" placeholder="例如 首期预付款 (40%)"></label>
      <label class="dialog-field">本次支付金额（元）<input v-model.number="paymentForm.paidAmount" type="number" min="0.01" step="0.01"></label>
      <label class="dialog-field">打款日期<input v-model="paymentForm.paymentDate" type="date" :max="localToday"></label>
      <label class="dialog-field">付款方式
        <select v-model="paymentForm.paymentMethod">
          <option value="BANK_TRANSFER">银行转账</option>
          <option value="CORPORATE_ALIPAY">企业支付宝</option>
          <option value="CORPORATE_WECHAT">企业微信</option>
          <option value="CHEQUE">支票</option>
          <option value="CASH">现金</option>
        </select>
      </label>
      <label class="dialog-field">付款企业账户<input v-model="paymentForm.payerAccount" maxlength="64"></label>
      <label class="dialog-field">收款方名称<input v-model="paymentForm.payeeName" maxlength="100"></label>
      <label class="dialog-field">收款银行账号<input v-model="paymentForm.payeeAccount" maxlength="64"></label>
      <label class="dialog-field">收款开户行<input v-model="paymentForm.payeeBank" maxlength="64"></label>
    </div>
    <label class="dialog-field">银行转账流水号 *
      <input v-model="paymentForm.transactionNumber" maxlength="128" placeholder="银行电子回单流水号（全局唯一防重）" required>
    </label>
    <label class="dialog-field">付款回单凭证
      <input accept=".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.doc,.docx" type="file" @change="selectPaymentProof">
      <small>支持 PDF、图片和 Office 文档，单个文件不超过 20MB。</small>
    </label>
    <label class="dialog-field">备注说明<input v-model="paymentForm.remarks" maxlength="200" placeholder="选填"></label>
    <p v-if="paymentError" class="dialog-error">{{ paymentError }}</p>
  </OaDialog>
</template>
