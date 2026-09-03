<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { storeToRefs } from 'pinia'
import type { ProcessDefinition } from '../api/types'
import { useOrganizationStore } from '../stores/organization'
import { paginate } from '../stores/pagination'
import { useProcessDefinitionStore } from '../stores/process-definitions'

type EditableRoute = { maxValue: string; approverKeys: string[] }

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
const page = ref(1)
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
const routeLabel = (route: ProcessDefinition['routes'][number]) => `${route.maxValue === null ? '无上限' : `≤ ${route.maxValue}`}: ${route.approverKeys.map(key => approverOptions.find(option => option.key === key)?.label ?? key).join(' → ')}`
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
  editRoutes.value = definition.routes.map(route => ({ maxValue: route.maxValue === null ? '' : String(route.maxValue), approverKeys: [...route.approverKeys] }))
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
  editRoutes.value = [{ maxValue: '', approverKeys: ['DIRECT_MANAGER'] }]
  error.value = ''
  message.value = ''
}

function addRoute() { editRoutes.value.push({ maxValue: '', approverKeys: ['DIRECT_MANAGER'] }) }
function removeRoute(index: number) { if (editRoutes.value.length > 1) editRoutes.value.splice(index, 1) }

async function save() {
  if (!editingId.value) return false
  error.value = ''
  const creating = editingId.value === 'new'
  const payload = { name: editName.value, priority: editPriority.value, departmentIds: editDepartmentIds.value, leaveTypes: editBusinessType.value === 'Leave' ? editLeaveTypes.value : [], routes: editRoutes.value.map(route => ({ maxValue: route.maxValue.trim() === '' ? null : Number(route.maxValue), approverKeys: route.approverKeys })) }
  const saved = creating
    ? await processDefinitions.create({ ...payload, code: editCode.value, businessType: editBusinessType.value })
    : await processDefinitions.update(editingId.value, payload)
  if (saved) {
    editingId.value = saved.id
    message.value = creating ? '流程草稿已创建。' : '流程草稿已保存。'
    await load()
    const refreshed = definitions.value.find(item => item.id === saved.id)
    if (refreshed) edit(refreshed)
    return true
  }
  return false
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

  <section v-if="editingId" class="panel process-editor"><div class="section-title"><div><p class="eyebrow">DRAFT EDITOR</p><h2>{{ editingId === 'new' ? '新建流程草稿' : '编辑流程草稿' }}</h2></div><button class="secondary" @click="editingId = ''">关闭</button></div><div class="process-basic-grid"><label class="process-name">流程编码<input v-model="editCode" maxlength="64" :disabled="editingId !== 'new'" placeholder="例如 LEAVE_ENGINEERING"></label><label class="process-name">流程名称<input v-model="editName" maxlength="100"></label><label class="process-name">业务类型<select v-model="editBusinessType" :disabled="editingId !== 'new'"><option value="Leave">请假</option><option value="Expense">报销</option><option value="Travel">出差</option><option value="Purchase">采购</option><option value="Seal">用章</option></select></label><label class="process-name">优先级<input v-model.number="editPriority" min="0" max="1000" type="number"><small>数值越大越优先；默认流程为 0。</small></label></div><fieldset class="process-scope"><legend>适用部门（不选表示全公司，包含下级部门）</legend><label v-for="department in departments" :key="department.id"><input v-model="editDepartmentIds" type="checkbox" :value="department.id">{{ department.name }}</label></fieldset><fieldset v-if="editBusinessType === 'Leave'" class="process-scope"><legend>适用假别（不选表示全部假别）</legend><label v-for="type in leaveTypeOptions" :key="type.value"><input v-model="editLeaveTypes" type="checkbox" :value="type.value">{{ type.label }}</label></fieldset><div v-for="(route, index) in editRoutes" :key="index" class="process-route-editor"><div class="route-heading"><strong>条件规则 {{ index + 1 }}</strong><button class="secondary" type="button" @click="removeRoute(index)">删除</button></div><label>指标上限（最后一条留空表示无上限）<input v-model="route.maxValue" min="0" step="0.01" type="number" placeholder="无上限"></label><fieldset><legend>审批节点（按所选顺序显示）</legend><label v-for="option in approverOptions" :key="option.key"><input v-model="route.approverKeys" type="checkbox" :value="option.key">{{ option.label }}</label></fieldset><p class="route-order">当前顺序：{{ route.approverKeys.map(key => approverOptions.find(option => option.key === key)?.label).join(' → ') || '未选择' }}</p></div><div class="process-editor-actions"><button class="secondary" type="button" @click="addRoute">＋ 添加条件规则</button><button type="button" :disabled="saving" @click="save">{{ saving ? '保存中…' : '保存草稿' }}</button></div></section>
</template>
