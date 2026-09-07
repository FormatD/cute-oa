<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useApiClient } from '../api/client'
import type {
  ExpenseInvoiceListItem,
  PaymentTransactionListItem,
  PurchaseReconciliationItem
} from '../api/types'
import { useAuthStore } from '../stores/auth'

type LedgerTab = 'invoices' | 'payments' | 'reconciliations'

const router = useRouter()
const api = useApiClient()
const auth = useAuthStore()

const currentTab = ref<LedgerTab>('invoices')
const loading = ref(false)
const exportLoading = ref(false)

// Pagination states
const page = ref(1)
const pageSize = ref(10)
const total = ref(0)
const totalPages = ref(1)

// Filters
const filters = reactive({
  keyword: '',
  startDate: '',
  endDate: '',
  departmentId: '',
  // Specific
  invoiceType: '',
  businessType: '',
  purchaseStatus: ''
})

// Data lists
const invoiceList = ref<ExpenseInvoiceListItem[]>([])
const paymentList = ref<PaymentTransactionListItem[]>([])
const reconciliationList = ref<PurchaseReconciliationItem[]>([])

const canAccess = computed(() => {
  const u = auth.currentUser
  if (!u) return false
  return (
    u.permissions?.includes('EXPENSE_PAY') ||
    u.permissions?.includes('EXPENSE_ALL_VIEW') ||
    u.permissions?.includes('PURCHASE_MANAGE') ||
    u.role === '财务专员' ||
    u.role === '财务经理' ||
    u.role === '采购经理' ||
    u.role === '总经理'
  )
})

async function loadData() {
  if (!canAccess.value) return
  loading.value = true
  try {
    if (currentTab.value === 'invoices') {
      const res = await api.payments.getFinanceInvoices({
        keyword: filters.keyword.trim() || undefined,
        type: filters.invoiceType || undefined,
        startDate: filters.startDate || undefined,
        endDate: filters.endDate || undefined,
        departmentId: filters.departmentId.trim() || undefined,
        page: page.value,
        pageSize: pageSize.value
      })
      invoiceList.value = res.items
      total.value = res.total
      page.value = res.page
      pageSize.value = res.pageSize
      totalPages.value = res.totalPages
    } else if (currentTab.value === 'payments') {
      const res = await api.payments.getFinancePayments({
        keyword: filters.keyword.trim() || undefined,
        businessType: filters.businessType || undefined,
        startDate: filters.startDate || undefined,
        endDate: filters.endDate || undefined,
        departmentId: filters.departmentId.trim() || undefined,
        page: page.value,
        pageSize: pageSize.value
      })
      paymentList.value = res.items
      total.value = res.total
      page.value = res.page
      pageSize.value = res.pageSize
      totalPages.value = res.totalPages
    } else if (currentTab.value === 'reconciliations') {
      const res = await api.payments.getFinanceReconciliations({
        keyword: filters.keyword.trim() || undefined,
        status: filters.purchaseStatus || undefined,
        startDate: filters.startDate ? `${filters.startDate}T00:00:00Z` : undefined,
        endDate: filters.endDate ? `${filters.endDate}T23:59:59Z` : undefined,
        departmentId: filters.departmentId.trim() || undefined,
        page: page.value,
        pageSize: pageSize.value
      })
      reconciliationList.value = res.items
      total.value = res.total
      page.value = res.page
      pageSize.value = res.pageSize
      totalPages.value = res.totalPages
    }
  } catch (err) {
    console.error('Failed to load finance ledger data', err)
  } finally {
    loading.value = false
  }
}

function switchTab(tab: LedgerTab) {
  currentTab.value = tab
  page.value = 1
  handleReset(false)
  void loadData()
}

function handleSearch() {
  page.value = 1
  void loadData()
}

function handleReset(reload = true) {
  filters.keyword = ''
  filters.startDate = ''
  filters.endDate = ''
  filters.departmentId = ''
  filters.invoiceType = ''
  filters.businessType = ''
  filters.purchaseStatus = ''
  page.value = 1
  if (reload) {
    void loadData()
  }
}

function changePage(delta: number) {
  const next = page.value + delta
  if (next >= 1 && next <= totalPages.value) {
    page.value = next
    void loadData()
  }
}

async function handleExport() {
  exportLoading.value = true
  try {
    if (currentTab.value === 'invoices' || currentTab.value === 'payments') {
      await api.payments.exportExpensesCsv({
        departmentId: filters.departmentId.trim() || undefined,
        startDate: filters.startDate || undefined,
        endDate: filters.endDate || undefined
      })
    } else {
      await api.payments.exportPurchasesCsv({
        departmentId: filters.departmentId.trim() || undefined,
        startDate: filters.startDate || undefined,
        endDate: filters.endDate || undefined
      })
    }
  } catch (err) {
    console.error('Export failed', err)
  } finally {
    exportLoading.value = false
  }
}

onMounted(() => {
  void loadData()
})

watch(() => currentTab.value, () => {
  page.value = 1
})
</script>

<template>
  <div class="finance-ledger-page">
    <div class="page-header">
      <div>
        <h1 class="page-title">财务台账与对账中心</h1>
        <p class="page-desc">发票合规查验、打款流水追踪与采购对账闭环</p>
      </div>
      <div class="header-actions">
        <button class="export-btn" :disabled="exportLoading" @click="handleExport">
          {{ exportLoading ? '正在导出...' : '📥 导出 CSV' }}
        </button>
      </div>
    </div>

    <div v-if="!canAccess" class="empty-state">
      <p>当前账号无权访问财务台账。仅财务专员、财务经理或采购管理人员可查看。</p>
    </div>

    <template v-else>
      <!-- Tabs -->
      <div class="tabs-nav">
        <button
          class="tab-item"
          :class="{ active: currentTab === 'invoices' }"
          @click="switchTab('invoices')"
        >
          🧾 发票台账
        </button>
        <button
          class="tab-item"
          :class="{ active: currentTab === 'payments' }"
          @click="switchTab('payments')"
        >
          💳 付款台账
        </button>
        <button
          class="tab-item"
          :class="{ active: currentTab === 'reconciliations' }"
          @click="switchTab('reconciliations')"
        >
          📦 采购对账
        </button>
      </div>

      <!-- Filters -->
      <div class="filter-card">
        <div class="filter-row">
          <div class="filter-item">
            <label>关键字</label>
            <input
              v-model="filters.keyword"
              type="text"
              placeholder="单号 / 票号 / 姓名 / 账号"
              @keyup.enter="handleSearch"
            />
          </div>

          <div class="filter-item">
            <label>部门</label>
            <input
              v-model="filters.departmentId"
              type="text"
              placeholder="所属部门"
              @keyup.enter="handleSearch"
            />
          </div>

          <div class="filter-item">
            <label>开始日期</label>
            <input v-model="filters.startDate" type="date" />
          </div>

          <div class="filter-item">
            <label>结束日期</label>
            <input v-model="filters.endDate" type="date" />
          </div>

          <!-- Invoice Type Filter -->
          <div v-if="currentTab === 'invoices'" class="filter-item">
            <label>发票类型</label>
            <select v-model="filters.invoiceType">
              <option value="">全部类型</option>
              <option value="VatSpecial">增值税专用发票</option>
              <option value="VatNormal">增值税普通发票</option>
              <option value="VatElectronic">增值税电子普通发票</option>
              <option value="TrainTicket">火车票</option>
              <option value="AirItinerary">航空运输电子客票行程单</option>
              <option value="QuotaInvoice">定额发票</option>
              <option value="OtherReceipt">通用机打发票/其他</option>
            </select>
          </div>

          <!-- Payment Business Type Filter -->
          <div v-if="currentTab === 'payments'" class="filter-item">
            <label>业务类型</label>
            <select v-model="filters.businessType">
              <option value="">全部业务</option>
              <option value="Expense">费用报销</option>
              <option value="Purchase">采购申请</option>
            </select>
          </div>

          <!-- Purchase Status Filter -->
          <div v-if="currentTab === 'reconciliations'" class="filter-item">
            <label>单据状态</label>
            <select v-model="filters.purchaseStatus">
              <option value="">全部状态</option>
              <option value="Approved">已批准待下单</option>
              <option value="Ordered">已下单待到货</option>
              <option value="Received">已验收完成</option>
            </select>
          </div>

          <div class="filter-actions">
            <button class="primary-btn" @click="handleSearch">查询</button>
            <button class="secondary-btn" @click="() => handleReset()">重置</button>
          </div>
        </div>
      </div>

      <!-- Loading Indicator -->
      <div v-if="loading" class="loading-state">
        <p>数据加载中...</p>
      </div>

      <!-- Tab 1: Invoices Table -->
      <div v-else-if="currentTab === 'invoices'" class="table-card">
        <div class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>发票类型</th>
                <th>发票号码</th>
                <th>发票代码</th>
                <th>开票日期</th>
                <th>关联单号</th>
                <th>报销人</th>
                <th>不含税金额</th>
                <th>税额</th>
                <th>价税合计</th>
                <th>状态</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="inv in invoiceList" :key="inv.id">
                <td>{{ inv.invoiceType }}</td>
                <td><code>{{ inv.invoiceNumber }}</code></td>
                <td>{{ inv.invoiceCode || '—' }}</td>
                <td>{{ inv.billingDate }}</td>
                <td>
                  <a href="javascript:void(0)" @click="router.push(`/expense/${inv.expenseClaimId}`)">
                    {{ inv.claimNumber }}
                  </a>
                </td>
                <td>{{ inv.applicantName }}</td>
                <td>¥{{ inv.amountWithoutTax.toFixed(2) }}</td>
                <td>¥{{ inv.taxAmount.toFixed(2) }}</td>
                <td><strong>¥{{ inv.totalAmount.toFixed(2) }}</strong></td>
                <td>
                  <span
                    class="badge"
                    :class="{
                      success: inv.status === 'Paid',
                      info: inv.status === 'Committed',
                      warning: inv.status === 'Released'
                    }"
                  >
                    {{ inv.status === 'Paid' ? '已付款' : inv.status === 'Committed' ? '占用中' : '已退还' }}
                  </span>
                </td>
              </tr>
              <tr v-if="invoiceList.length === 0">
                <td colspan="10" class="no-data">暂无发票数据</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Tab 2: Payments Table -->
      <div v-else-if="currentTab === 'payments'" class="table-card">
        <div class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>业务类型</th>
                <th>单据编号</th>
                <th>款项批次</th>
                <th>付款日期</th>
                <th>付款方式</th>
                <th>出资账号</th>
                <th>收款人</th>
                <th>收款账号</th>
                <th>银行流水号</th>
                <th>付款金额</th>
                <th>经办人</th>
                <th>状态</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="pay in paymentList" :key="pay.id">
                <td>{{ pay.businessType === 'Expense' ? '费用报销' : '采购申请' }}</td>
                <td>
                  <a
                    href="javascript:void(0)"
                    @click="
                      pay.businessType === 'Expense'
                        ? router.push(`/expense/${pay.businessId}`)
                        : router.push(`/purchase/${pay.businessId}`)
                    "
                  >
                    {{ pay.businessNumber }}
                  </a>
                </td>
                <td>{{ pay.batchTitle || `第 ${pay.sequence} 笔` }}</td>
                <td>{{ pay.paymentDate }}</td>
                <td>{{ pay.paymentMethod }}</td>
                <td><code>{{ pay.payerAccount }}</code></td>
                <td>{{ pay.payeeName }}</td>
                <td><code>{{ pay.payeeAccountMasked }}</code></td>
                <td><code>{{ pay.transactionNumber }}</code></td>
                <td><strong>¥{{ pay.paidAmount.toFixed(2) }}</strong></td>
                <td>{{ pay.operatorName }}</td>
                <td>
                  <span class="badge success">{{ pay.status }}</span>
                </td>
              </tr>
              <tr v-if="paymentList.length === 0">
                <td colspan="12" class="no-data">暂无打款记录</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Tab 3: Reconciliations Table -->
      <div v-else-if="currentTab === 'reconciliations'" class="table-card">
        <div class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>采购单号</th>
                <th>申请人</th>
                <th>所属部门</th>
                <th>预估总额</th>
                <th>订单金额</th>
                <th>采购订单号</th>
                <th>供应商</th>
                <th>验收合格额</th>
                <th>已付金额</th>
                <th>待付尾款</th>
                <th>单据状态</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="item in reconciliationList" :key="item.id">
                <td>{{ item.number }}</td>
                <td>{{ item.applicantName }}</td>
                <td>{{ item.departmentName }}</td>
                <td>¥{{ item.estimatedTotal.toFixed(2) }}</td>
                <td>¥{{ item.orderAmount.toFixed(2) }}</td>
                <td>{{ item.orderNumber || '—' }}</td>
                <td>{{ item.supplier || '—' }}</td>
                <td>¥{{ item.acceptedAmount.toFixed(2) }}</td>
                <td>¥{{ item.paidAmount.toFixed(2) }}</td>
                <td :style="{ color: item.remainingAmount > 0 ? '#b91c1c' : '#15803d', fontWeight: 600 }">
                  ¥{{ item.remainingAmount.toFixed(2) }}
                </td>
                <td>
                  <span
                    class="badge"
                    :class="{
                      success: item.status === 'Received',
                      info: item.status === 'Ordered',
                      warning: item.status === 'Approved'
                    }"
                  >
                    {{ item.status === 'Received' ? '已验收' : item.status === 'Ordered' ? '已下单' : '已批准' }}
                  </span>
                </td>
                <td>
                  <button class="link-btn" @click="router.push(`/purchase/${item.id}`)">
                    查看详情
                  </button>
                </td>
              </tr>
              <tr v-if="reconciliationList.length === 0">
                <td colspan="12" class="no-data">暂无对账记录</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Pagination -->
      <div class="pagination-bar">
        <div class="pagination-info">
          共 {{ total }} 条记录 · 当前第 {{ page }} / {{ totalPages }} 页
        </div>
        <div class="pagination-actions">
          <button class="page-btn" :disabled="page <= 1" @click="changePage(-1)">上一页</button>
          <button class="page-btn" :disabled="page >= totalPages" @click="changePage(1)">下一页</button>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.finance-ledger-page {
  padding: 1.5rem;
  max-width: 1300px;
  margin: 0 auto;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.5rem;
  flex-wrap: wrap;
  gap: 1rem;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #1e293b;
  margin: 0;
}

.page-desc {
  font-size: 0.875rem;
  color: #64748b;
  margin: 0.25rem 0 0;
}

.export-btn {
  background-color: #0f766e;
  color: #fff;
  border: none;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.875rem;
  font-weight: 500;
}

.export-btn:hover {
  background-color: #115e59;
}

.tabs-nav {
  display: flex;
  gap: 0.5rem;
  border-bottom: 2px solid #e2e8f0;
  margin-bottom: 1.25rem;
}

.tab-item {
  background: none;
  border: none;
  padding: 0.625rem 1.25rem;
  font-size: 0.95rem;
  font-weight: 600;
  color: #64748b;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  margin-bottom: -2px;
  transition: all 0.2s;
}

.tab-item:hover {
  color: #0f172a;
}

.tab-item.active {
  color: #2563eb;
  border-bottom-color: #2563eb;
}

.filter-card {
  background: #fff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 1rem 1.25rem;
  margin-bottom: 1.25rem;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
}

.filter-row {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  align-items: flex-end;
}

.filter-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.filter-item label {
  font-size: 0.75rem;
  font-weight: 600;
  color: #475569;
}

.filter-item input,
.filter-item select {
  height: 36px;
  padding: 0 0.625rem;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.875rem;
  color: #1e293b;
  background-color: #f8fafc;
}

.filter-item input:focus,
.filter-item select:focus {
  outline: none;
  border-color: #2563eb;
  background-color: #fff;
}

.filter-actions {
  display: flex;
  gap: 0.5rem;
  margin-left: auto;
}

.primary-btn {
  background-color: #2563eb;
  color: #fff;
  border: none;
  padding: 0 1rem;
  height: 36px;
  border-radius: 6px;
  font-size: 0.875rem;
  font-weight: 500;
  cursor: pointer;
}

.primary-btn:hover {
  background-color: #1d4ed8;
}

.secondary-btn {
  background-color: #f1f5f9;
  color: #475569;
  border: 1px solid #cbd5e1;
  padding: 0 1rem;
  height: 36px;
  border-radius: 6px;
  font-size: 0.875rem;
  cursor: pointer;
}

.secondary-btn:hover {
  background-color: #e2e8f0;
}

.table-card {
  background: #fff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.table-wrap {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
  text-align: left;
}

.data-table th {
  background-color: #f8fafc;
  color: #475569;
  font-weight: 600;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #e2e8f0;
  white-space: nowrap;
}

.data-table td {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #f1f5f9;
  color: #334155;
  white-space: nowrap;
}

.data-table tbody tr:hover {
  background-color: #f8fafc;
}

.data-table code {
  font-family: ui-monospace, monospace;
  background-color: #f1f5f9;
  padding: 0.15rem 0.35rem;
  border-radius: 4px;
  font-size: 0.8rem;
}

.badge {
  display: inline-block;
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.badge.success {
  background-color: #dcfce7;
  color: #15803d;
}

.badge.info {
  background-color: #e0f2fe;
  color: #0369a1;
}

.badge.warning {
  background-color: #fef3c7;
  color: #b45309;
}

.link-btn {
  background: none;
  border: none;
  color: #2563eb;
  cursor: pointer;
  font-size: 0.875rem;
  padding: 0;
  text-decoration: underline;
}

.no-data {
  text-align: center;
  color: #94a3b8;
  padding: 2.5rem !important;
}

.loading-state {
  text-align: center;
  padding: 3rem;
  color: #64748b;
}

.empty-state {
  background: #f8fafc;
  border: 1px dashed #cbd5e1;
  border-radius: 8px;
  padding: 3rem;
  text-align: center;
  color: #64748b;
}

.pagination-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 1rem;
  font-size: 0.875rem;
  color: #64748b;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.pagination-actions {
  display: flex;
  gap: 0.5rem;
}

.page-btn {
  background-color: #fff;
  border: 1px solid #cbd5e1;
  padding: 0.375rem 0.75rem;
  border-radius: 6px;
  font-size: 0.875rem;
  cursor: pointer;
}

.page-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.page-btn:not(:disabled):hover {
  background-color: #f1f5f9;
}

@media (max-width: 768px) {
  .finance-ledger-page {
    padding: 0.75rem;
  }
  .filter-actions {
    margin-left: 0;
    width: 100%;
  }
  .primary-btn, .secondary-btn {
    flex: 1;
  }
}
</style>
