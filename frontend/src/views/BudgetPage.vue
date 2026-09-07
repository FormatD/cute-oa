<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useApiClient } from '../api/client'
import type { Budget, BudgetStatus, CreateBudgetRequest } from '../api/types'
import { useAuthStore } from '../stores/auth'
import OaDialog from '../components/OaDialog.vue'

const router = useRouter()
const api = useApiClient()
const auth = useAuthStore()

const loading = ref(false)
const budgets = ref<Budget[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(10)
const totalPages = ref(1)

const filters = reactive({
  departmentId: '',
  year: new Date().getFullYear(),
  month: undefined as number | undefined,
  expenseCategory: '',
  projectId: '',
  status: ''
})

const canManage = computed(() => {
  const u = auth.currentUser
  if (!u) return false
  return u.permissions?.includes('EXPENSE_ALL_VIEW') ||
    u.role === '总经理' ||
    u.role === '财务经理' ||
    u.roles?.includes('财务经理') ||
    u.roles?.includes('总经理')
})

async function loadBudgets() {
  loading.value = true
  try {
    const res = await api.budgets.getBudgets({
      departmentId: filters.departmentId.trim() || undefined,
      year: filters.year || undefined,
      month: filters.month !== undefined && filters.month !== null && String(filters.month) !== '' ? Number(filters.month) : undefined,
      expenseCategory: filters.expenseCategory.trim() || undefined,
      projectId: filters.projectId.trim() || undefined,
      status: filters.status || undefined,
      page: page.value,
      pageSize: pageSize.value
    })
    if ('items' in res) {
      budgets.value = res.items
      total.value = res.total
      page.value = res.page
      pageSize.value = res.pageSize
      totalPages.value = res.totalPages
    } else {
      budgets.value = res
      total.value = res.length
      totalPages.value = 1
    }
  } catch (err) {
    console.error('Failed to load budgets', err)
  } finally {
    loading.value = false
  }
}

function handleSearch() {
  page.value = 1
  void loadBudgets()
}

function handleReset() {
  filters.departmentId = ''
  filters.year = new Date().getFullYear()
  filters.month = undefined
  filters.expenseCategory = ''
  filters.projectId = ''
  filters.status = ''
  page.value = 1
  void loadBudgets()
}

function changePage(delta: number) {
  const next = page.value + delta
  if (next >= 1 && next <= totalPages.value) {
    page.value = next
    void loadBudgets()
  }
}

// -------------------------------------------------------------
// 创建预算弹窗
// -------------------------------------------------------------
const createDialogOpen = ref(false)
const createBusy = ref(false)
const createError = ref('')
const createForm = reactive<CreateBudgetRequest>({
  departmentId: '',
  year: new Date().getFullYear(),
  month: 0,
  expenseCategory: '',
  projectId: '',
  allocatedAmount: 10000,
  autoPublish: false
})

function openCreateDialog() {
  createForm.departmentId = auth.currentUser?.departmentName || ''
  createForm.year = new Date().getFullYear()
  createForm.month = 0
  createForm.expenseCategory = ''
  createForm.projectId = ''
  createForm.allocatedAmount = 10000
  createForm.autoPublish = false
  createError.value = ''
  createDialogOpen.value = true
}

async function handleCreateBudget() {
  createError.value = ''
  if (!createForm.departmentId.trim()) {
    createError.value = '请填写归属部门。'
    return
  }
  if (createForm.allocatedAmount < 0) {
    createError.value = '编制预算额度不能小于 0。'
    return
  }
  createBusy.value = true
  try {
    await api.budgets.createBudget({
      departmentId: createForm.departmentId.trim(),
      year: Number(createForm.year),
      month: Number(createForm.month),
      expenseCategory: createForm.expenseCategory?.trim() || null,
      projectId: createForm.projectId?.trim() || null,
      allocatedAmount: Number(createForm.allocatedAmount),
      autoPublish: createForm.autoPublish
    })
    createDialogOpen.value = false
    void loadBudgets()
  } catch (err: any) {
    createError.value = err.message || '创建预算池失败。'
  } finally {
    createBusy.value = false
  }
}

// -------------------------------------------------------------
// 调整预算额度弹窗
// -------------------------------------------------------------
const adjustDialogOpen = ref(false)
const adjustBusy = ref(false)
const adjustError = ref('')
const targetBudget = ref<Budget | null>(null)
const adjustAmount = ref(0)
const adjustReason = ref('')

function openAdjustDialog(item: Budget) {
  targetBudget.value = item
  adjustAmount.value = item.allocatedAmount
  adjustReason.value = ''
  adjustError.value = ''
  adjustDialogOpen.value = true
}

async function handleAdjustBudget() {
  if (!targetBudget.value) return
  adjustError.value = ''
  if (!adjustReason.value.trim()) {
    adjustError.value = '请填写预算调整原因。'
    return
  }
  adjustBusy.value = true
  try {
    await api.budgets.adjustBudget(targetBudget.value.id, {
      amount: Number(adjustAmount.value),
      reason: adjustReason.value.trim(),
      expectedVersion: targetBudget.value.concurrencyVersion
    })
    adjustDialogOpen.value = false
    void loadBudgets()
  } catch (err: any) {
    adjustError.value = err.message || '调整预算额度失败。'
  } finally {
    adjustBusy.value = false
  }
}

// -------------------------------------------------------------
// 状态流转操作 (发布 / 冻结 / 解冻 / 封账)
// -------------------------------------------------------------
const statusDialogOpen = ref(false)
const statusBusy = ref(false)
const statusError = ref('')
const statusAction = ref<'publish' | 'freeze' | 'unfreeze' | 'close'>('publish')
const statusReason = ref('')

function openStatusDialog(item: Budget, action: 'publish' | 'freeze' | 'unfreeze' | 'close') {
  targetBudget.value = item
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
  if (!targetBudget.value) return
  statusError.value = ''
  statusBusy.value = true
  try {
    const payload = {
      reason: statusReason.value.trim() || undefined,
      expectedVersion: targetBudget.value.concurrencyVersion
    }
    if (statusAction.value === 'publish') {
      await api.budgets.publishBudget(targetBudget.value.id, payload)
    } else if (statusAction.value === 'freeze') {
      await api.budgets.freezeBudget(targetBudget.value.id, payload)
    } else if (statusAction.value === 'unfreeze') {
      await api.budgets.unfreezeBudget(targetBudget.value.id, payload)
    } else if (statusAction.value === 'close') {
      await api.budgets.closeBudget(targetBudget.value.id, payload)
    }
    statusDialogOpen.value = false
    void loadBudgets()
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

onMounted(() => {
  void loadBudgets()
})
</script>

<template>
  <div class="page-container budget-page">
    <div class="page-header">
      <div>
        <h1 class="page-title">💰 预算中心</h1>
        <p class="page-subtitle">管理部门年度与月度预算池、科目额度、执行水位与流水明细</p>
      </div>
      <div v-if="canManage" class="header-actions">
        <button class="primary" @click="openCreateDialog">＋ 编制新预算</button>
      </div>
    </div>

    <!-- 筛选栏 -->
    <section class="panel filter-panel">
      <div class="filter-grid">
        <div class="form-group">
          <label>归属部门</label>
          <input v-model="filters.departmentId" placeholder="部门名称或ID" @keyup.enter="handleSearch" />
        </div>
        <div class="form-group">
          <label>预算年度</label>
          <input v-model.number="filters.year" type="number" placeholder="2026" @keyup.enter="handleSearch" />
        </div>
        <div class="form-group">
          <label>预算月份</label>
          <select v-model="filters.month" @change="handleSearch">
            <option :value="undefined">全部周期</option>
            <option :value="0">0 - 全年</option>
            <option v-for="m in 12" :key="m" :value="m">{{ m }} 月</option>
          </select>
        </div>
        <div class="form-group">
          <label>费用科目</label>
          <input v-model="filters.expenseCategory" placeholder="如：办公、交通" @keyup.enter="handleSearch" />
        </div>
        <div class="form-group">
          <label>归属项目</label>
          <input v-model="filters.projectId" placeholder="项目编号" @keyup.enter="handleSearch" />
        </div>
        <div class="form-group">
          <label>状态</label>
          <select v-model="filters.status" @change="handleSearch">
            <option value="">全部状态</option>
            <option value="Draft">草稿 (Draft)</option>
            <option value="Active">生效中 (Active)</option>
            <option value="Frozen">已冻结 (Frozen)</option>
            <option value="Closed">已封账 (Closed)</option>
          </select>
        </div>
      </div>
      <div class="filter-actions">
        <button class="primary" :disabled="loading" @click="handleSearch">查询</button>
        <button class="secondary" :disabled="loading" @click="handleReset">重置</button>
      </div>
    </section>

    <!-- 预算池列表 -->
    <section class="panel">
      <div class="section-title">
        <div>
          <p class="eyebrow">BUDGET POOLS</p>
          <h2>预算池清单</h2>
        </div>
        <span class="muted">共 {{ total }} 个预算池</span>
      </div>

      <p v-if="loading" class="empty">正在加载预算池数据…</p>
      <template v-else-if="budgets.length">
        <div class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>部门</th>
                <th>科目</th>
                <th>项目</th>
                <th>周期</th>
                <th>编制额度</th>
                <th>审批预占</th>
                <th>实际执行</th>
                <th>可用余额</th>
                <th>执行率</th>
                <th>状态</th>
                <th>更新时间</th>
                <th class="action-cell">操作</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="item in budgets" :key="item.id">
                <td><strong>{{ item.departmentId }}</strong></td>
                <td>{{ item.expenseCategory || '通用科目' }}</td>
                <td>{{ item.projectId || '—' }}</td>
                <td>{{ item.year }}年 {{ item.month > 0 ? `${item.month}月` : '全年' }}</td>
                <td>¥{{ item.allocatedAmount.toFixed(2) }}</td>
                <td>¥{{ item.committedAmount.toFixed(2) }}</td>
                <td>¥{{ item.actualAmount.toFixed(2) }}</td>
                <td>
                  <strong :class="{ 'warning-text': item.availableAmount < 0 }">
                    ¥{{ item.availableAmount.toFixed(2) }}
                  </strong>
                </td>
                <td>
                  <span>{{ (item.executionRate ?? (item.allocatedAmount > 0 ? ((item.committedAmount + item.actualAmount) / item.allocatedAmount * 100) : 0)).toFixed(1) }}%</span>
                </td>
                <td>
                  <em :class="{
                    active: item.status === 'Active',
                    warning: item.status === 'Frozen',
                    archived: item.status === 'Closed' || item.status === 'Draft'
                  }">{{ statusText(item.status) }}</em>
                </td>
                <td><small>{{ new Date(item.updatedAt).toLocaleString('zh-CN') }}</small></td>
                <td class="action-cell">
                  <button class="secondary" @click="router.push(`/budgets/${item.id}`)">详情</button>
                  <template v-if="canManage">
                    <button v-if="item.status === 'Draft'" class="secondary" @click="openStatusDialog(item, 'publish')">发布</button>
                    <button v-if="item.status === 'Active'" class="secondary" @click="openAdjustDialog(item)">调整</button>
                    <button v-if="item.status === 'Active'" class="secondary" @click="openStatusDialog(item, 'freeze')">冻结</button>
                    <button v-if="item.status === 'Frozen'" class="secondary" @click="openStatusDialog(item, 'unfreeze')">解冻</button>
                    <button v-if="item.status === 'Active' || item.status === 'Frozen'" class="secondary" @click="openStatusDialog(item, 'close')">封账</button>
                  </template>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div class="pagination">
          <span>共 {{ total }} 条记录</span>
          <div>
            <button class="secondary" :disabled="page === 1" @click="changePage(-1)">上一页</button>
            <b>{{ page }} / {{ totalPages }}</b>
            <button class="secondary" :disabled="page >= totalPages" @click="changePage(1)">下一页</button>
          </div>
        </div>
      </template>
      <p v-else class="empty">暂无匹配的预算池记录。</p>
    </section>

    <!-- 编制预算弹窗 -->
    <OaDialog
      :open="createDialogOpen"
      title="编制新预算池"
      submit-label="确认编制"
      :busy="createBusy"
      @close="createDialogOpen = false"
      @submit="handleCreateBudget"
    >
      <div class="dialog-form">
        <div class="form-group">
          <label>归属部门 <span class="required">*</span></label>
          <input v-model="createForm.departmentId" placeholder="例如：研发部 或 engineering" />
        </div>
        <div class="form-row">
          <div class="form-group">
            <label>预算年度 <span class="required">*</span></label>
            <input v-model.number="createForm.year" type="number" min="2020" max="2100" />
          </div>
          <div class="form-group">
            <label>预算月份 (0为全年)</label>
            <select v-model.number="createForm.month">
              <option :value="0">0 - 全年预算</option>
              <option v-for="m in 12" :key="m" :value="m">{{ m }} 月</option>
            </select>
          </div>
        </div>
        <div class="form-row">
          <div class="form-group">
            <label>费用科目 (选填)</label>
            <input v-model="createForm.expenseCategory" placeholder="留空为部门通用科目" />
          </div>
          <div class="form-group">
            <label>归属项目 (选填)</label>
            <input v-model="createForm.projectId" placeholder="留空为通用项目" />
          </div>
        </div>
        <div class="form-group">
          <label>编制总额度 (元) <span class="required">*</span></label>
          <input v-model.number="createForm.allocatedAmount" type="number" min="0" step="100" />
        </div>
        <div class="form-group form-checkbox">
          <label>
            <input v-model="createForm.autoPublish" type="checkbox" />
            编制完成后直接发布生效（跳过草稿状态）
          </label>
        </div>
        <p v-if="createError" class="error-text">{{ createError }}</p>
      </div>
    </OaDialog>

    <!-- 调整预算额度弹窗 -->
    <OaDialog
      :open="adjustDialogOpen"
      title="调整预算额度"
      submit-label="确认调整"
      :busy="adjustBusy"
      @close="adjustDialogOpen = false"
      @submit="handleAdjustBudget"
    >
      <div v-if="targetBudget" class="dialog-form">
        <p class="muted">
          部门：<strong>{{ targetBudget.departmentId }}</strong> ·
          科目：<strong>{{ targetBudget.expenseCategory || '通用' }}</strong> ·
          周期：<strong>{{ targetBudget.year }}年{{ targetBudget.month > 0 ? `${targetBudget.month}月` : '全年' }}</strong>
        </p>
        <div class="form-group">
          <label>当前编制总额</label>
          <input :value="`¥${targetBudget.allocatedAmount.toFixed(2)}`" disabled />
        </div>
        <div class="form-group">
          <label>调整后新总额 (元) <span class="required">*</span></label>
          <input v-model.number="adjustAmount" type="number" min="0" step="100" />
        </div>
        <div class="form-group">
          <label>调整原因 <span class="required">*</span></label>
          <textarea v-model="adjustReason" rows="3" placeholder="请详细记录额度追加或调减的原因及审批依据"></textarea>
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
      <div v-if="targetBudget" class="dialog-form">
        <p>确认对部门【{{ targetBudget.departmentId }}】的预算池执行<strong>{{ statusActionTitle }}</strong>操作？</p>
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
.budget-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.filter-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(170px, 1fr));
  gap: 12px;
  margin-bottom: 12px;
}
.filter-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.dialog-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
.form-checkbox label {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}
.required {
  color: #e04545;
}
.error-text {
  color: #e04545;
  font-size: 0.85rem;
}
.action-cell {
  display: flex;
  gap: 4px;
  justify-content: flex-end;
}
.table-wrap {
  overflow-x: auto;
}
@media (max-width: 600px) {
  .form-row {
    grid-template-columns: 1fr;
  }
}
</style>
