<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useApiClient } from '../api/client'
import type { Budget, BudgetStatus, BudgetTransaction } from '../api/types'
import { useAuthStore } from '../stores/auth'
import OaDialog from '../components/OaDialog.vue'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const api = useApiClient()

const budgetId = computed(() => String(route.params.id || ''))

const loading = ref(false)
const budget = ref<Budget | null>(null)
const error = ref('')

const transactions = ref<BudgetTransaction[]>([])
const txLoading = ref(false)
const txPage = ref(1)
const txPageSize = ref(10)
const txTotal = ref(0)
const txTotalPages = ref(1)

const canManage = computed(() => {
  const u = auth.currentUser
  if (!u) return false
  return u.permissions?.includes('EXPENSE_ALL_VIEW') ||
    u.role === '总经理' ||
    u.role === '财务经理' ||
    u.roles?.includes('财务经理') ||
    u.roles?.includes('总经理')
})

async function loadBudget() {
  if (!budgetId.value) return
  loading.value = true
  error.value = ''
  try {
    budget.value = await api.budgets.getBudget(budgetId.value)
  } catch (err: any) {
    error.value = err.message || '加载预算池详情失败。'
  } finally {
    loading.value = false
  }
}

async function loadTransactions() {
  if (!budgetId.value) return
  txLoading.value = true
  try {
    const res = await api.budgets.getBudgetTransactions(budgetId.value, txPage.value, txPageSize.value)
    transactions.value = res.items ?? []
    txTotal.value = res.total ?? 0
    txPage.value = res.page ?? 1
    txPageSize.value = res.pageSize ?? 10
    txTotalPages.value = res.totalPages ?? 1
  } catch (err) {
    console.error('Failed to load budget transactions', err)
  } finally {
    txLoading.value = false
  }
}

function changeTxPage(delta: number) {
  const next = txPage.value + delta
  if (next >= 1 && next <= txTotalPages.value) {
    txPage.value = next
    void loadTransactions()
  }
}

// -------------------------------------------------------------
// 额度调整
// -------------------------------------------------------------
const adjustDialogOpen = ref(false)
const adjustBusy = ref(false)
const adjustError = ref('')
const adjustAmount = ref(0)
const adjustReason = ref('')

function openAdjustDialog() {
  if (!budget.value) return
  adjustAmount.value = budget.value.allocatedAmount
  adjustReason.value = ''
  adjustError.value = ''
  adjustDialogOpen.value = true
}

async function handleAdjustBudget() {
  if (!budget.value) return
  adjustError.value = ''
  if (!adjustReason.value.trim()) {
    adjustError.value = '请填写预算调整原因。'
    return
  }
  adjustBusy.value = true
  try {
    await api.budgets.adjustBudget(budget.value.id, {
      amount: Number(adjustAmount.value),
      reason: adjustReason.value.trim(),
      expectedVersion: budget.value.concurrencyVersion
    })
    adjustDialogOpen.value = false
    await loadBudget()
    await loadTransactions()
  } catch (err: any) {
    adjustError.value = err.message || '调整预算额度失败。'
  } finally {
    adjustBusy.value = false
  }
}

// -------------------------------------------------------------
// 状态变更 (发布 / 冻结 / 解冻 / 封账)
// -------------------------------------------------------------
const statusDialogOpen = ref(false)
const statusBusy = ref(false)
const statusError = ref('')
const statusAction = ref<'publish' | 'freeze' | 'unfreeze' | 'close'>('publish')
const statusReason = ref('')

function openStatusDialog(action: 'publish' | 'freeze' | 'unfreeze' | 'close') {
  statusAction.value = action
  statusReason.value = ''
  statusError.value = ''
  statusDialogOpen.value = true
}

const statusActionTitle = computed(() => {
  switch (statusAction.value) {
    case 'publish': return '发布预算池'
    case 'freeze': return '冻结预算池'
    case 'unfreeze': return '解冻预算池'
    case 'close': return '封账关闭预算池'
  }
})

async function handleStatusChange() {
  if (!budget.value) return
  statusError.value = ''
  statusBusy.value = true
  try {
    const payload = {
      reason: statusReason.value.trim() || undefined,
      expectedVersion: budget.value.concurrencyVersion
    }
    if (statusAction.value === 'publish') {
      await api.budgets.publishBudget(budget.value.id, payload)
    } else if (statusAction.value === 'freeze') {
      await api.budgets.freezeBudget(budget.value.id, payload)
    } else if (statusAction.value === 'unfreeze') {
      await api.budgets.unfreezeBudget(budget.value.id, payload)
    } else if (statusAction.value === 'close') {
      await api.budgets.closeBudget(budget.value.id, payload)
    }
    statusDialogOpen.value = false
    await loadBudget()
    await loadTransactions()
  } catch (err: any) {
    statusError.value = err.message || '操作失败。'
  } finally {
    statusBusy.value = false
  }
}

function statusText(status: BudgetStatus | string): string {
  switch (status) {
    case 'Draft': return '草稿'
    case 'Active': return '生效中'
    case 'Frozen': return '已冻结'
    case 'Closed': return '已封账'
    default: return String(status)
  }
}

function txTypeLabel(type: string): string {
  switch (type.toUpperCase()) {
    case 'RESERVED': return '审批预占'
    case 'RELEASED': return '释放预占'
    case 'CONSUMED': return '付款结转'
    case 'ADJUSTED': return '额度调整'
    default: return type
  }
}

onMounted(() => {
  void loadBudget()
  void loadTransactions()
})
</script>

<template>
  <div class="page-container budget-detail-page">
    <div class="detail-header">
      <button class="secondary" @click="router.push('/budgets')">← 返回预算中心</button>
      <div v-if="budget && canManage" class="header-actions">
        <button v-if="budget.status === 'Draft'" class="primary" @click="openStatusDialog('publish')">发布</button>
        <button v-if="budget.status === 'Active'" class="secondary" @click="openAdjustDialog">调整额度</button>
        <button v-if="budget.status === 'Active'" class="secondary" @click="openStatusDialog('freeze')">冻结</button>
        <button v-if="budget.status === 'Frozen'" class="secondary" @click="openStatusDialog('unfreeze')">解冻</button>
        <button v-if="budget.status === 'Active' || budget.status === 'Frozen'" class="secondary" @click="openStatusDialog('close')">封账关闭</button>
      </div>
    </div>

    <p v-if="loading" class="empty">正在加载预算池详情…</p>
    <p v-else-if="error" class="error-text">{{ error }}</p>
    <template v-else-if="budget">
      <!-- 汇总卡片 -->
      <section class="panel budget-info-panel">
        <div class="section-title">
          <div>
            <p class="eyebrow">BUDGET POOL OVERVIEW</p>
            <h2>{{ budget.departmentId }} · {{ budget.year }}年度 {{ budget.month > 0 ? `${budget.month}月` : '全年' }}预算</h2>
          </div>
          <em :class="{
            active: budget.status === 'Active',
            warning: budget.status === 'Frozen',
            archived: budget.status === 'Closed' || budget.status === 'Draft'
          }">{{ statusText(budget.status) }}</em>
        </div>

        <div class="metrics-grid">
          <div class="metric-card">
            <span class="label">编制总额度</span>
            <strong class="value">¥{{ budget.allocatedAmount.toFixed(2) }}</strong>
            <small class="hint">初始及调整后总编制</small>
          </div>
          <div class="metric-card">
            <span class="label">审批预占额</span>
            <strong class="value warning-text">¥{{ budget.committedAmount.toFixed(2) }}</strong>
            <small class="hint">审批中单据占用额度</small>
          </div>
          <div class="metric-card">
            <span class="label">累计实支结转</span>
            <strong class="value">¥{{ budget.actualAmount.toFixed(2) }}</strong>
            <small class="hint">已实际支付结转金额</small>
          </div>
          <div class="metric-card">
            <span class="label">当前可用余额</span>
            <strong class="value" :class="{ 'warning-text': budget.availableAmount < 0 }">
              ¥{{ budget.availableAmount.toFixed(2) }}
            </strong>
            <small class="hint">可用于新申请额度</small>
          </div>
        </div>

        <div class="pool-meta">
          <div><span>费用科目：</span><strong>{{ budget.expenseCategory || '通用科目' }}</strong></div>
          <div><span>归属项目：</span><strong>{{ budget.projectId || '通用项目' }}</strong></div>
          <div><span>并发版本：</span><code>v{{ budget.concurrencyVersion }}</code></div>
          <div><span>更新时间：</span><small>{{ new Date(budget.updatedAt).toLocaleString('zh-CN') }}</small></div>
        </div>
      </section>

      <!-- 流水明细 -->
      <section class="panel">
        <div class="section-title">
          <div>
            <p class="eyebrow">TRANSACTION HISTORY</p>
            <h3>预算变动流水明细</h3>
          </div>
          <span class="muted">共 {{ txTotal }} 条变动流水</span>
        </div>

        <p v-if="txLoading" class="empty">正在加载变动流水…</p>
        <template v-else-if="transactions.length">
          <div class="table-wrap">
            <table class="data-table">
              <thead>
                <tr>
                  <th>时间</th>
                  <th>变动类型</th>
                  <th>业务单号</th>
                  <th>变动金额</th>
                  <th>变动后可用余额</th>
                  <th>说明与摘要</th>
                  <th>操作人</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="tx in transactions" :key="tx.id">
                  <td><small>{{ new Date(tx.createdAt).toLocaleString('zh-CN') }}</small></td>
                  <td>
                    <em :class="{
                      active: tx.transactionType === 'RELEASED',
                      warning: tx.transactionType === 'RESERVED',
                      archived: tx.transactionType === 'ADJUSTED'
                    }">{{ txTypeLabel(tx.transactionType) }}</em>
                  </td>
                  <td><strong>{{ tx.businessNumber }}</strong></td>
                  <td>
                    <span :class="tx.transactionType === 'RELEASED' ? 'text-success' : ''">
                      {{ tx.transactionType === 'RELEASED' ? '-' : '+' }}¥{{ tx.amount.toFixed(2) }}
                    </span>
                  </td>
                  <td><strong>¥{{ tx.balanceAfter.toFixed(2) }}</strong></td>
                  <td>{{ tx.description || '—' }}</td>
                  <td><small>{{ tx.operatorId }}</small></td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="pagination">
            <span>共 {{ txTotal }} 条记录</span>
            <div>
              <button class="secondary" :disabled="txPage === 1" @click="changeTxPage(-1)">上一页</button>
              <b>{{ txPage }} / {{ txTotalPages }}</b>
              <button class="secondary" :disabled="txPage >= txTotalPages" @click="changeTxPage(1)">下一页</button>
            </div>
          </div>
        </template>
        <p v-else class="empty">该预算池暂无变动流水记录。</p>
      </section>
    </template>

    <!-- 调整预算额度弹窗 -->
    <OaDialog
      :open="adjustDialogOpen"
      title="调整预算额度"
      submit-label="确认调整"
      :busy="adjustBusy"
      @close="adjustDialogOpen = false"
      @submit="handleAdjustBudget"
    >
      <div v-if="budget" class="dialog-form">
        <div class="form-group">
          <label>当前编制总额</label>
          <input :value="`¥${budget.allocatedAmount.toFixed(2)}`" disabled />
        </div>
        <div class="form-group">
          <label>调整后新总额 (元) <span class="required">*</span></label>
          <input v-model.number="adjustAmount" type="number" min="0" step="100" />
        </div>
        <div class="form-group">
          <label>调整原因 <span class="required">*</span></label>
          <textarea v-model="adjustReason" rows="3" placeholder="请详细记录追加或调减的原因及审批依据"></textarea>
        </div>
        <p v-if="adjustError" class="error-text">{{ adjustError }}</p>
      </div>
    </OaDialog>

    <!-- 状态变更弹窗 -->
    <OaDialog
      :open="statusDialogOpen"
      :title="statusActionTitle"
      submit-label="确认提交"
      :busy="statusBusy"
      @close="statusDialogOpen = false"
      @submit="handleStatusChange"
    >
      <div v-if="budget" class="dialog-form">
        <p>确认对当前预算池执行<strong>{{ statusActionTitle }}</strong>操作？</p>
        <div class="form-group">
          <label>操作说明 / 原因 (选填)</label>
          <textarea v-model="statusReason" rows="3" placeholder="可填写变动原因或批准说明"></textarea>
        </div>
        <p v-if="statusError" class="error-text">{{ statusError }}</p>
      </div>
    </OaDialog>
  </div>
</template>

<style scoped>
.budget-detail-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}
.header-actions {
  display: flex;
  gap: 8px;
}
.metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 12px;
  margin: 16px 0;
}
.metric-card {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.metric-card .label {
  font-size: 0.8rem;
  color: #64748b;
}
.metric-card .value {
  font-size: 1.35rem;
  color: #1e293b;
}
.metric-card .hint {
  font-size: 0.72rem;
  color: #94a3b8;
}
.pool-meta {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 12px;
  padding-top: 12px;
  border-top: 1px solid #edf2f7;
  font-size: 0.85rem;
  color: #475569;
}
.dialog-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.required {
  color: #e04545;
}
.error-text {
  color: #e04545;
  font-size: 0.85rem;
}
.text-success {
  color: #10b981;
}
</style>
