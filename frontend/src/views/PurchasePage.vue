<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useApiClient } from '../api/client'
import type { Budget } from '../api/types'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useFileStore } from '../stores/files'
import { usePurchaseStore } from '../stores/purchase'
import { useUiStore } from '../stores/ui'
import { businessStatusLabel } from '../utils/businessLabels'

const app = useAppStore()
const auth = useAuthStore()
const employees = useEmployeeDirectoryStore()
const files = useFileStore()
const purchase = usePurchaseStore()
const ui = useUiStore()
const router = useRouter()
const api = useApiClient()

const today = new Date()
const categories = ['办公用品', 'IT设备', '软件服务', '行政物资', '市场物料', '生产物料', '专业服务', '其他']

const currentDeptBudget = ref<Budget | null>(null)
const canFinance = computed(() => auth.currentUser?.permissions?.includes('PURCHASE_MANAGE') === true || auth.currentUser?.permissions?.includes('EXPENSE_PAY') === true)

async function loadDeptBudget() {
  const dept = auth.currentUser?.departmentName
  if (!dept) return
  try {
    const list = await api.budgets.getBudgets({ departmentId: dept, year: today.getFullYear(), month: 0 })
    currentDeptBudget.value = list[0] ?? null
  } catch {
    currentDeptBudget.value = null
  }
}

onMounted(() => {
  void loadDeptBudget()
})

watch(() => purchase.showPurchaseForm, (open) => {
  if (open) void loadDeptBudget()
})

const isOverBudget = computed(() => {
  if (!currentDeptBudget.value) return false
  return purchase.estimatedTotal > currentDeptBudget.value.availableAmount
})

function addItem() {
  if (purchase.purchaseForm.items.length < 50) {
    purchase.purchaseForm.items.push({ category: '办公用品', name: '', specification: '', quantity: '1', unit: '件', estimatedUnitPrice: '', remark: '' })
  }
}

function removeItem(index: number) {
  if (purchase.purchaseForm.items.length > 1) {
    purchase.purchaseForm.items.splice(index, 1)
  }
}

async function selectAttachments(event: Event) {
  const selected = Array.from((event.target as HTMLInputElement).files ?? [])
  try {
    purchase.purchaseForm.attachments.push(...await Promise.all(selected.map(file => files.uploadFile(file))))
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '附件上传失败。'
  }
}

function search() {
  purchase.page = 1
  void purchase.loadPurchases()
}

function changePage(offset: number) {
  purchase.page += offset
  void purchase.loadPurchases()
}

function resetFilters() {
  Object.assign(purchase.filters, { keyword: '', status: '', applicantId: '', startDate: '', endDate: '' })
  search()
}

async function exportPurchaseCsv() {
  try {
    await api.payments.exportPurchasesCsv({
      applicantId: purchase.filters.applicantId || undefined,
      startDate: purchase.filters.startDate || undefined,
      endDate: purchase.filters.endDate || undefined
    })
    ui.message = '采购四单对账明细导出成功。'
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '导出失败。'
  }
}
</script>

<template>
  <div class="page-heading">
    <div>
      <p class="eyebrow">PURCHASE MANAGEMENT</p>
      <h1>采购管理</h1>
      <p>发起采购申请，按金额分级审批，并跟踪下单、到货验收与分期打款闭环。</p>
    </div>
    <div class="task-actions">
      <button v-if="canFinance" class="secondary" type="button" @click="exportPurchaseCsv">
        📥 导出对账 CSV
      </button>
      <button v-if="auth.currentUser?.permissions?.includes('PURCHASE_MANAGE')" class="secondary" :disabled="purchase.operationBusy" @click="purchase.generateDemoData">
        生成演示草稿
      </button>
      <button class="primary-action" @click="purchase.showPurchaseForm = true">＋ 新建采购</button>
    </div>
  </div>

  <p v-if="ui.message" class="notice success">{{ ui.message }}</p>
  <p v-if="ui.error" class="notice error">{{ ui.error }}</p>

  <form class="filter-bar" @submit.prevent="search">
    <input v-model="purchase.filters.keyword" placeholder="单号、主题、物品或申请人">
    <select v-model="purchase.filters.status">
      <option value="">全部状态</option>
      <option value="0">草稿</option>
      <option value="1">审批中</option>
      <option value="2">已驳回</option>
      <option value="3">已批准</option>
      <option value="4">已撤回</option>
      <option value="5">已下单</option>
      <option value="6">已验收</option>
    </select>
    <select v-model="purchase.filters.applicantId">
      <option value="">全部申请人</option>
      <option v-for="employee in employees.employees" :key="employee.id" :value="employee.id">{{ employee.name }}</option>
    </select>
    <label>到货日期从<input v-model="purchase.filters.startDate" type="date"></label>
    <label>至<input v-model="purchase.filters.endDate" type="date"></label>
    <button type="submit">查询</button>
    <button class="secondary" type="button" @click="resetFilters">重置</button>
  </form>

  <section class="panel">
    <div class="section-title">
      <div>
        <p class="eyebrow">PURCHASE REQUESTS</p>
        <h2>采购申请</h2>
      </div>
      <button @click="purchase.showPurchaseForm = !purchase.showPurchaseForm">
        {{ purchase.showPurchaseForm ? '收起表单' : '新建采购' }}
      </button>
    </div>

    <!-- 部门采购预算水位卡片 -->
    <div v-if="currentDeptBudget && purchase.showPurchaseForm" class="budget-summary-card">
      <h4>📊 部门可用预算池（{{ currentDeptBudget.departmentId }} · {{ currentDeptBudget.year }} 年度）</h4>
      <div class="budget-stat-grid">
        <div><span>年度编制总额</span><strong>¥{{ currentDeptBudget.allocatedAmount.toFixed(2) }}</strong></div>
        <div><span>审批预占额度</span><strong>¥{{ currentDeptBudget.committedAmount.toFixed(2) }}</strong></div>
        <div><span>累计实支结转</span><strong>¥{{ currentDeptBudget.actualAmount.toFixed(2) }}</strong></div>
        <div><span>当前可用额度</span><strong :class="{ 'warning-text': isOverBudget }">¥{{ currentDeptBudget.availableAmount.toFixed(2) }}</strong></div>
      </div>
      <p v-if="isOverBudget" class="dialog-error" style="margin-top: 10px;">
        ⚠️ 提示：预估采购总额（¥{{ purchase.estimatedTotal.toFixed(2) }}）已超出部门可用预算池剩余额度（¥{{ currentDeptBudget.availableAmount.toFixed(2) }}）。
      </p>
    </div>

    <form v-if="purchase.showPurchaseForm" class="leave-form purchase-form" @submit.prevent="app.submitPurchase">
      <label>采购主题<input v-model="purchase.purchaseForm.title" maxlength="100" placeholder="例如 研发部办公设备采购"></label>
      <label>期望到货日期<input v-model="purchase.purchaseForm.requiredDate" type="date"></label>
      <label>建议供应商（可选）<input v-model="purchase.purchaseForm.suggestedSupplier" maxlength="100" placeholder="供应商名称"></label>
      <label class="wide">采购用途<textarea v-model="purchase.purchaseForm.purpose" maxlength="500" placeholder="说明采购背景、用途和必要性"></textarea></label>

      <fieldset class="wide itinerary-editor">
        <legend>采购明细（{{ purchase.purchaseForm.items.length }}/50）</legend>
        <div v-for="(item, index) in purchase.purchaseForm.items" :key="index" class="itinerary-row purchase-item-row">
          <label>品类
            <select v-model="item.category">
              <option v-for="category in categories" :key="category">{{ category }}</option>
            </select>
          </label>
          <label>物品或服务名称<input v-model="item.name" maxlength="100" placeholder="名称"></label>
          <label>规格型号<input v-model="item.specification" maxlength="100" placeholder="可选"></label>
          <label>数量<input v-model="item.quantity" min="0.01" max="1000000" step="0.01" type="number"></label>
          <label>单位<input v-model="item.unit" maxlength="20" placeholder="件"></label>
          <label>预估单价（元）<input v-model="item.estimatedUnitPrice" min="0" max="100000000" step="0.01" type="number"></label>
          <label class="wide">备注<input v-model="item.remark" maxlength="200" placeholder="可选"></label>
          <div class="line-amount">
            小计 <strong>¥{{ (Number(item.quantity || 0) * Number(item.estimatedUnitPrice || 0)).toFixed(2) }}</strong>
          </div>
          <button class="secondary" type="button" :disabled="purchase.purchaseForm.items.length === 1" @click="removeItem(index)">删除明细</button>
        </div>
        <div class="purchase-total">
          <button class="secondary" type="button" @click="addItem">＋ 添加明细</button>
          <strong>预估合计：¥{{ purchase.estimatedTotal.toFixed(2) }}</strong>
        </div>
      </fieldset>

      <fieldset class="wide copy-selector">
        <legend>抄送人（审批完成或撤回后通知）</legend>
        <label v-for="employee in employees.employees.filter(item => item.id !== auth.currentUserId)" :key="employee.id">
          <input v-model="purchase.purchaseForm.copyRecipientIds" type="checkbox" :value="employee.id">
          {{ employee.name }} · {{ employee.role }}
        </label>
      </fieldset>

      <label class="wide">报价或采购依据附件
        <input accept=".pdf,.jpg,.jpeg,.png,.xls,.xlsx,.doc,.docx" multiple type="file" @change="selectAttachments">
        <small>5000 元及以上至少 1 份，5 万元及以上至少 2 份；单个文件不超过 20MB。</small>
      </label>
      <div v-if="purchase.purchaseForm.attachments.length" class="wide attachment-list">
        已上传 {{ purchase.purchaseForm.attachments.length }} 个附件
        <button class="secondary" type="button" @click="purchase.purchaseForm.attachments = []">清空</button>
      </div>
      <div class="wide form-actions">
        <button :disabled="purchase.submitting" type="submit">{{ purchase.submitting ? '提交中…' : '保存并提交' }}</button>
      </div>
    </form>

    <template v-if="purchase.paged.items.length">
      <div class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th>申请单号</th>
              <th>主题</th>
              <th>申请人</th>
              <th>明细</th>
              <th>预估金额</th>
              <th>期望到货</th>
              <th>状态</th>
              <th>付款状态</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in purchase.paged.items" :key="item.id">
              <td>{{ item.number }}</td>
              <td>{{ item.title }}</td>
              <td>{{ item.applicantName }}<small>{{ item.departmentName }}</small></td>
              <td>{{ item.itemCount ?? item.items?.length ?? 0 }} 项</td>
              <td>¥{{ item.estimatedTotal.toFixed(2) }}</td>
              <td>{{ item.requiredDate }}</td>
              <td><em>{{ businessStatusLabel(item.status) }}</em></td>
              <td>
                <span
                  class="invoice-badge"
                  :class="{
                    paid: item.paymentStatus === 'PAID',
                    released: !item.paymentStatus || item.paymentStatus === 'UNPAID'
                  }"
                >
                  {{ item.paymentStatus === 'PAID' ? '已结清' : item.paymentStatus === 'PARTIALLY_PAID' ? '部分付款' : '未付款' }}
                </span>
                <span v-if="item.paidTotalAmount" style="margin-left: 4px; font-size: 0.76rem; color: #64748b;">
                  (¥{{ item.paidTotalAmount.toFixed(2) }})
                </span>
              </td>
              <td>
                <button class="secondary" @click="router.push(`/purchase/${item.id}`)">详情</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="pagination">
        <span>共 {{ purchase.paged.total }} 条</span>
        <div>
          <button class="secondary" :disabled="purchase.paged.currentPage === 1" @click="changePage(-1)">上一页</button>
          <b>{{ purchase.paged.currentPage }} / {{ purchase.paged.totalPages }}</b>
          <button class="secondary" :disabled="purchase.paged.currentPage === purchase.paged.totalPages" @click="changePage(1)">下一页</button>
        </div>
      </div>
    </template>
    <p v-else class="empty">当前筛选条件下暂无采购记录。</p>
  </section>
</template>
