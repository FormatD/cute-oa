<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import type { ManagedPosition } from '../api/types'
import OaDialog from '../components/OaDialog.vue'
import { useOrganizationStore } from '../stores/organization'
import { usePositionAdminStore } from '../stores/position-admin'

const admin = usePositionAdminStore()
const organization = useOrganizationStore()
const editorOpen = ref(false)
const editingId = ref('')
const deleteTarget = ref<ManagedPosition | null>(null)
const validationError = ref('')
const departmentFilter = ref('')
const form = reactive({ id: '', name: '', departmentId: '', sortOrder: 0, status: 'ACTIVE' as 'ACTIVE' | 'DISABLED', version: 1 })
const filteredPositions = computed(() => departmentFilter.value ? admin.positions.filter(item => item.departmentId === departmentFilter.value) : admin.positions)

function openCreate() {
  editingId.value = ''
  Object.assign(form, { id: '', name: '', departmentId: departmentFilter.value || organization.departments.find(item => item.parentId)?.id || organization.departments[0]?.id || '', sortOrder: 0, status: 'ACTIVE', version: 1 })
  admin.error = ''
  validationError.value = ''
  editorOpen.value = true
}

function openEdit(position: ManagedPosition) {
  editingId.value = position.id
  Object.assign(form, { id: position.id, name: position.name, departmentId: position.departmentId, sortOrder: position.sortOrder, status: position.status, version: position.version })
  admin.error = ''
  validationError.value = ''
  editorOpen.value = true
}

async function savePosition() {
  validationError.value = ''
  if (!form.id.trim() || !form.name.trim() || !form.departmentId) { validationError.value = '请填写岗位编码、名称并选择所属部门。'; return }
  const success = editingId.value
    ? await admin.updatePosition(editingId.value, { name: form.name.trim(), departmentId: form.departmentId, sortOrder: form.sortOrder, status: form.status, version: form.version })
    : await admin.createPosition({ id: form.id.trim(), name: form.name.trim(), departmentId: form.departmentId, sortOrder: form.sortOrder })
  if (success) editorOpen.value = false
}

async function confirmDelete() {
  if (deleteTarget.value && await admin.deletePosition(deleteTarget.value.id, deleteTarget.value.version)) deleteTarget.value = null
}

onMounted(async () => { await Promise.all([organization.loadOrganization(), admin.loadPositions()]) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">POSITION ADMIN</p><h1>岗位管理</h1><p>岗位描述组织职责，角色只用于安全授权，两者互不替代。</p></div><button class="primary-action" @click="openCreate">＋ 新建岗位</button></div>
  <p v-if="admin.message" class="notice success">{{ admin.message }}</p><p v-if="admin.error" class="notice error">{{ admin.error }}</p>
  <section class="panel"><form class="filter-bar" @submit.prevent><select v-model="departmentFilter"><option value="">全部部门</option><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select><button v-if="departmentFilter" class="secondary" type="button" @click="departmentFilter = ''">重置</button></form>
    <p v-if="admin.loading" class="empty">正在加载岗位…</p>
    <template v-else-if="filteredPositions.length"><div class="table-wrap"><table class="data-table position-table"><thead><tr><th>岗位</th><th>所属部门</th><th>用户</th><th>排序</th><th>状态</th><th>类型</th><th>版本</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="position in filteredPositions" :key="position.id"><td><strong>{{ position.name }}</strong><small>{{ position.id }}</small></td><td>{{ position.departmentName }}</td><td>{{ position.userCount }} 人</td><td>{{ position.sortOrder }}</td><td><em :class="{ archived: position.status === 'DISABLED' }">{{ position.status === 'ACTIVE' ? '启用' : '停用' }}</em></td><td><em :class="{ archived: !position.isSystem }">{{ position.isSystem ? '系统预置' : '自定义' }}</em></td><td>v{{ position.version }}</td><td class="task-actions"><button class="secondary" @click="openEdit(position)">编辑</button><button v-if="!position.isSystem" class="danger-outline" :disabled="position.userCount > 0" :title="position.userCount ? '请先移除岗位用户' : '删除岗位'" @click="deleteTarget = position">删除</button></td></tr></tbody></table></div></template><p v-else class="empty">当前部门暂无岗位。</p>
  </section>

  <OaDialog :open="editorOpen" :title="editingId ? '编辑岗位' : '新建岗位'" description="岗位属于一个部门，只有启用岗位可分配给用户。" submit-label="保存岗位" :busy="admin.saving" @close="editorOpen = false" @submit="savePosition">
    <label class="dialog-field">岗位编码<input v-model="form.id" :disabled="!!editingId" maxlength="64" placeholder="例如 customer-success-manager"><small>创建后不可修改。</small></label>
    <label class="dialog-field">岗位名称<input v-model="form.name" maxlength="100" placeholder="例如 客户成功经理"></label>
    <label class="dialog-field">所属部门<select v-model="form.departmentId"><option value="">请选择</option><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select></label>
    <div class="dialog-grid"><label class="dialog-field">显示排序<input v-model.number="form.sortOrder" type="number" min="0" max="9999"></label><label v-if="editingId" class="dialog-field">岗位状态<select v-model="form.status"><option value="ACTIVE">启用</option><option value="DISABLED">停用</option></select></label></div>
    <p v-if="validationError || admin.error" class="dialog-error">{{ validationError || admin.error }}</p>
  </OaDialog>
  <OaDialog :open="!!deleteTarget" title="删除岗位" :description="deleteTarget ? `岗位 ${deleteTarget.name} 删除后无法恢复。` : ''" submit-label="确认删除" danger :busy="admin.saving" @close="deleteTarget = null" @submit="confirmDelete"><div v-if="deleteTarget" class="operation-summary"><strong>{{ deleteTarget.name }}</strong><span>{{ deleteTarget.departmentName }} · {{ deleteTarget.id }}</span></div></OaDialog>
</template>
