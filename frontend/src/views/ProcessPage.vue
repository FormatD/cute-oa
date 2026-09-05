<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { storeToRefs } from 'pinia'
import type { ProcessDefinition, ProcessNodePolicy, ProcessSimulation } from '../api/types'
import OaDialog from '../components/OaDialog.vue'
import { useOrganizationStore } from '../stores/organization'
import { paginate } from '../stores/pagination'
import { useProcessDefinitionStore } from '../stores/process-definitions'

type EditablePolicy = Omit<ProcessNodePolicy, 'id' | 'sequence' | 'approverKey'>
type EditableRoute = { maxValue: string; approverKeys: string[]; nodePolicies: Record<string, EditablePolicy> }

const processDefinitions = useProcessDefinitionStore()
const organization = useOrganizationStore()
const { definitions, loading, saving, error, message } = storeToRefs(processDefinitions)
const editingId = ref('')
const editName = ref('')
const editCode = ref('')
const editBusinessType = ref<'Leave' | 'Expense' | 'Travel' | 'Purchase' | 'Seal'>('Leave')
const editPriority = ref(0)
const editDepartmentIds = ref<string[]>([])
const editLeaveTypes = ref<string[]>([])
const editRoutes = ref<EditableRoute[]>([])
const departments = computed(() => organization.departments)
const employees = computed(() => organization.directoryEmployees)
const page = ref(1)
const simulationApplicantId = ref('')
const simulationMetric = ref(1)
const simulation = ref<ProcessSimulation | null>(null)
const approverOptions = [
  { key: 'DIRECT_MANAGER', label: '直属上级' },
  { key: 'ROLE:财务专员', label: '财务专员' },
  { key: 'ROLE:财务经理', label: '财务经理' },
  { key: 'ROLE:HR/行政', label: 'HR/行政' },
  { key: 'ROLE:总经理', label: '总经理' }
]

const statusLabel = (status: number) => ['草稿', '已发布', '已归档'][status] ?? '未知'
const businessLabel = (type: string) => type === 'Leave' ? '请假' : type === 'Expense' ? '报销' : type === 'Travel' ? '出差' : type === 'Purchase' ? '采购' : '用章'
const leaveTypeOptions = [{ value: 'Annual', label: '年假' }, { value: 'Personal', label: '事假' }, { value: 'Sick', label: '病假' }, { value: 'CompTime', label: '调休' }]
const routeLabel = (route: ProcessDefinition['routes'][number]) => `${route.maxValue === null ? '无上限' : `≤ ${route.maxValue}`}: ${route.approverKeys.map((key, index) => `${approverOptions.find(option => option.key === key)?.label ?? key}(${route.nodes?.[index]?.handlingHours ?? 24}h)`).join(' → ')}`
const defaultPolicy = (): EditablePolicy => ({ handlingHours: 24, reminderBeforeHours: 4, escalateAfterHours: 24, escalationTarget: 'DIRECT_MANAGER', missingAssigneeAction: 'BLOCK', allowAutoSkip: false })
const policyFor = (route: EditableRoute, key: string) => route.nodePolicies[key] ??= defaultPolicy()
const scopeLabel = (definition: ProcessDefinition) => {
  const departmentText = definition.departmentIds.length ? definition.departmentIds.map(id => departments.value.find(item => item.id === id)?.name ?? id).join('、') : '全公司'
  const leaveText = definition.businessType === 'Leave' ? (definition.leaveTypes.length ? definition.leaveTypes.map(type => leaveTypeOptions.find(item => item.value === type)?.label ?? type).join('、') : '全部假别') : ''
  return `${departmentText}${leaveText ? ` · ${leaveText}` : ''}`
}
const paged = computed(() => paginate(definitions.value, page.value))

async function load() {
  await processDefinitions.load()
  if (page.value > paged.value.totalPages) page.value = paged.value.totalPages
}

function edit(definition: ProcessDefinition) {
  editingId.value = definition.id
  editCode.value = definition.code
  editName.value = definition.name
  editBusinessType.value = definition.businessType
  editPriority.value = definition.priority
  editDepartmentIds.value = [...definition.departmentIds]
  editLeaveTypes.value = [...definition.leaveTypes]
  editRoutes.value = definition.routes.map(route => ({
    maxValue: route.maxValue === null ? '' : String(route.maxValue),
    approverKeys: [...route.approverKeys],
    nodePolicies: Object.fromEntries(route.approverKeys.map((key, index) => {
      const node = route.nodes?.[index]
      return [key, node ? { handlingHours: node.handlingHours, reminderBeforeHours: node.reminderBeforeHours, escalateAfterHours: node.escalateAfterHours, escalationTarget: node.escalationTarget, missingAssigneeAction: node.missingAssigneeAction, allowAutoSkip: node.allowAutoSkip } : defaultPolicy()]
    }))
  }))
  simulation.value = null
  error.value = ''
  message.value = ''
}

function createDefinition() {
  editingId.value = 'new'
  editCode.value = ''
  editName.value = ''
  editBusinessType.value = 'Leave'
  editPriority.value = 10
  editDepartmentIds.value = []
  editLeaveTypes.value = []
  editRoutes.value = [{ maxValue: '', approverKeys: ['DIRECT_MANAGER'], nodePolicies: { DIRECT_MANAGER: defaultPolicy() } }]
  simulation.value = null
  error.value = ''
  message.value = ''
}

function addRoute() { editRoutes.value.push({ maxValue: '', approverKeys: ['DIRECT_MANAGER'], nodePolicies: { DIRECT_MANAGER: defaultPolicy() } }) }
function removeRoute(index: number) { if (editRoutes.value.length > 1) editRoutes.value.splice(index, 1) }

async function save() {
  if (!editingId.value) return false
  error.value = ''
  const creating = editingId.value === 'new'
  const payload = { name: editName.value, priority: editPriority.value, departmentIds: editDepartmentIds.value, leaveTypes: editBusinessType.value === 'Leave' ? editLeaveTypes.value : [], routes: editRoutes.value.map(route => ({ maxValue: route.maxValue.trim() === '' ? null : Number(route.maxValue), approverKeys: route.approverKeys, nodePolicies: route.approverKeys.map(key => policyFor(route, key)) })) }
  const saved = creating
    ? await processDefinitions.create({ ...payload, code: editCode.value, businessType: editBusinessType.value })
    : await processDefinitions.update(editingId.value, payload)
  if (saved) {
    editingId.value = ''
    message.value = creating ? '流程草稿已创建。' : '流程草稿已保存。'
    await load()
    return true
  }
  return false
}

async function simulate() {
  if (!editingId.value || editingId.value === 'new' || !simulationApplicantId.value) return
  simulation.value = await processDefinitions.simulate(editingId.value, { applicantId: simulationApplicantId.value, metric: simulationMetric.value, category: editBusinessType.value === 'Leave' ? editLeaveTypes.value[0] ?? null : null })
}

async function clone(definition: ProcessDefinition) {
  error.value = ''
  const draft = await processDefinitions.clone(definition.id)
  if (draft) {
    await load()
    edit(draft)
    message.value = `已复制 ${definition.code} v${definition.version}，新草稿版本为 v${draft.version}。`
  }
}

async function publish(definition: ProcessDefinition) {
  if (!window.confirm(`确认发布 ${definition.code} v${definition.version} 吗？发布后不可修改。`)) return
  error.value = ''
  if (await processDefinitions.publish(definition.id)) {
    editingId.value = ''
    message.value = `${definition.code} v${definition.version} 已发布，新提交单据将使用该版本。`
    await load()
  }
}

onMounted(async () => {
  await Promise.all([load(), organization.loadOrganization()])
})
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">PROCESS CENTER</p><h1>流程定义</h1><p>按优先级、组织和假别配置请假与报销流程；已发布版本不可修改。</p></div><button class="primary-action" @click="createDefinition">＋ 新建流程</button></div>
  <p v-if="message" class="notice success">{{ message }}</p><p v-if="error" class="notice error">{{ error }}</p>
  <section class="panel"><p v-if="loading" class="empty">正在加载流程定义…</p><template v-else-if="definitions.length"><div class="table-wrap"><table class="data-table process-table"><thead><tr><th>流程</th><th>业务</th><th>优先级</th><th>适用范围</th><th>版本</th><th>状态</th><th>路由规则</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="definition in paged.items" :key="definition.id"><td><strong>{{ definition.name }}</strong><small>{{ definition.code }}</small></td><td>{{ businessLabel(definition.businessType) }}</td><td>{{ definition.priority }}</td><td><small>{{ scopeLabel(definition) }}</small></td><td>v{{ definition.version }}</td><td><em :class="{ archived: definition.status === 2 }">{{ statusLabel(definition.status) }}</em></td><td><small v-for="route in definition.routes" :key="route.id" class="process-route-summary">{{ routeLabel(route) }}</small></td><td class="task-actions"><button v-if="definition.status === 0" class="secondary" @click="edit(definition)">编辑</button><button v-if="definition.status === 0" @click="publish(definition)">发布</button><button v-else class="secondary" :disabled="saving" @click="clone(definition)">复制新版本</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ paged.total }} 条</span><div><button class="secondary" :disabled="paged.currentPage === 1" @click="page--">上一页</button><b>{{ paged.currentPage }} / {{ paged.totalPages }}</b><button class="secondary" :disabled="paged.currentPage === paged.totalPages" @click="page++">下一页</button></div></div></template><p v-else class="empty">暂无流程定义。</p></section>

  <OaDialog
    :open="Boolean(editingId)"
    :title="editingId === 'new' ? '新建流程草稿' : '编辑流程草稿'"
    description="配置流程基本信息、适用范围、条件规则与审批节点超时策略。"
    submit-label="保存草稿"
    :busy="saving"
    width="960px"
    @close="editingId = ''"
    @submit="save"
  >
    <div class="process-basic-grid">
      <label class="process-name">
        流程编码
        <input v-model="editCode" maxlength="64" :disabled="editingId !== 'new'" placeholder="例如 LEAVE_ENGINEERING">
      </label>
      <label class="process-name">
        流程名称
        <input v-model="editName" maxlength="100">
      </label>
      <label class="process-name">
        业务类型
        <select v-model="editBusinessType" :disabled="editingId !== 'new'">
          <option value="Leave">请假</option>
          <option value="Expense">报销</option>
          <option value="Travel">出差</option>
          <option value="Purchase">采购</option>
          <option value="Seal">用章</option>
        </select>
      </label>
      <label class="process-name">
        优先级
        <input v-model.number="editPriority" min="0" max="1000" type="number">
        <small>数值越大越优先；默认流程为 0。</small>
      </label>
    </div>
    <fieldset class="process-scope" style="margin-top: 12px;">
      <legend>适用部门（不选表示全公司，包含下级部门）</legend>
      <label v-for="department in departments" :key="department.id">
        <input v-model="editDepartmentIds" type="checkbox" :value="department.id">
        {{ department.name }}
      </label>
    </fieldset>
    <fieldset v-if="editBusinessType === 'Leave'" class="process-scope" style="margin-top: 12px;">
      <legend>适用假别（不选表示全部假别）</legend>
      <label v-for="type in leaveTypeOptions" :key="type.value">
        <input v-model="editLeaveTypes" type="checkbox" :value="type.value">
        {{ type.label }}
      </label>
    </fieldset>
    <div v-for="(route, index) in editRoutes" :key="index" class="process-route-editor" style="margin-top: 14px;">
      <div class="route-heading">
        <strong>条件规则 {{ index + 1 }}</strong>
        <button class="secondary" type="button" @click="removeRoute(index)">删除</button>
      </div>
      <label>
        指标上限（最后一条留空表示无上限）
        <input v-model="route.maxValue" min="0" step="0.01" type="number" placeholder="无上限">
      </label>
      <fieldset>
        <legend>审批节点（按所选顺序显示）</legend>
        <label v-for="option in approverOptions" :key="option.key">
          <input v-model="route.approverKeys" type="checkbox" :value="option.key">
          {{ option.label }}
        </label>
      </fieldset>
      <p class="route-order">当前顺序：{{ route.approverKeys.map(key => approverOptions.find(option => option.key === key)?.label).join(' → ') || '未选择' }}</p>
      <div v-for="key in route.approverKeys" :key="key" class="process-node-policy">
        <strong>{{ approverOptions.find(option => option.key === key)?.label ?? key }}</strong>
        <label>
          办理时限（小时）
          <input v-model.number="policyFor(route, key).handlingHours" min="1" max="2160" type="number">
        </label>
        <label>
          提前提醒（小时）
          <input v-model.number="policyFor(route, key).reminderBeforeHours" min="0" max="720" type="number">
        </label>
        <label>
          逾期后升级（小时）
          <input v-model.number="policyFor(route, key).escalateAfterHours" min="0" max="2160" type="number">
        </label>
        <label>
          升级对象
          <select v-model="policyFor(route, key).escalationTarget">
            <option value="DIRECT_MANAGER">办理人直属上级</option>
            <option value="PROCESS_ADMIN">流程管理员</option>
            <option value="ROLE:总经理">总经理</option>
          </select>
        </label>
        <label>
          审批人缺失
          <select v-model="policyFor(route, key).missingAssigneeAction">
            <option value="BLOCK">阻断提交</option>
            <option value="SKIP">跳过节点</option>
            <option value="PROCESS_ADMIN">转流程管理员</option>
          </select>
        </label>
        <label class="checkbox-label">
          <input v-model="policyFor(route, key).allowAutoSkip" type="checkbox">
          连续节点同人时允许自动跳过
        </label>
      </div>
    </div>
    <div style="margin: 14px 0;">
      <button class="secondary" type="button" @click="addRoute">＋ 添加条件规则</button>
    </div>
    <div v-if="editingId !== 'new'" class="process-simulation">
      <h3>发布前流程试算</h3>
      <div class="filter-bar">
        <label>
          申请人
          <select v-model="simulationApplicantId">
            <option value="">请选择</option>
            <option v-for="employee in employees" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.departmentName }}</option>
          </select>
        </label>
        <label>
          业务指标
          <input v-model.number="simulationMetric" min="0" step="0.5" type="number">
        </label>
        <button type="button" :disabled="saving || !simulationApplicantId" @click="simulate">试算路由</button>
      </div>
      <div v-if="simulation" class="notice success">
        <strong>命中 {{ simulation.code }} v{{ simulation.version }}</strong>
        <p>{{ simulation.approvers.map(item => `${item.assignee.name} (${item.policy.handlingHours}h)`).join(' → ') }}</p>
        <small v-for="note in simulation.routingNotes" :key="note">{{ note }}</small>
      </div>
    </div>
    <p v-if="error" class="dialog-error">{{ error }}</p>
  </OaDialog>
</template>
