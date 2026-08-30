<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import OaDialog from '../components/OaDialog.vue'
import type { ManagedDepartment } from '../api/types'
import { useDepartmentAdminStore } from '../stores/department-admin'

const admin = useDepartmentAdminStore()
const editorOpen = ref(false)
const editingId = ref('')
const editingRoot = ref(false)
const deleteTarget = ref<ManagedDepartment | null>(null)
const validationError = ref('')
const form = reactive({ id: '', name: '', parentId: '', sortOrder: 0, version: 1 })

const parentOptions = computed(() => admin.departments.filter(item => !editingId.value || item.id !== editingId.value && !isDescendant(item.id, editingId.value)))

function isDescendant(candidateId: string, departmentId: string) {
  let cursor = admin.departments.find(item => item.id === candidateId)
  const visited = new Set<string>()
  while (cursor?.parentId && !visited.has(cursor.id)) {
    visited.add(cursor.id)
    if (cursor.parentId === departmentId) return true
    cursor = admin.departments.find(item => item.id === cursor?.parentId)
  }
  return false
}

function openCreate(parentId = 'general') {
  editingId.value = ''
  editingRoot.value = false
  Object.assign(form, { id: '', name: '', parentId, sortOrder: 0, version: 1 })
  validationError.value = ''
  editorOpen.value = true
}

function openEdit(department: ManagedDepartment) {
  editingId.value = department.id
  editingRoot.value = department.parentId == null
  Object.assign(form, { id: department.id, name: department.name, parentId: department.parentId ?? '', sortOrder: department.sortOrder, version: department.version })
  validationError.value = ''
  editorOpen.value = true
}

async function saveDepartment() {
  validationError.value = ''
  if (!form.id.trim() || !form.name.trim() || (!editingRoot.value && !form.parentId)) {
    validationError.value = '请填写部门编码、名称并选择上级部门。'
    return
  }
  const success = editingId.value
    ? await admin.updateDepartment(editingId.value, { name: form.name.trim(), parentId: editingRoot.value ? null : form.parentId, sortOrder: form.sortOrder, version: form.version })
    : await admin.createDepartment({ id: form.id.trim(), name: form.name.trim(), parentId: form.parentId, sortOrder: form.sortOrder })
  if (success) editorOpen.value = false
}

async function confirmDelete() {
  if (deleteTarget.value && await admin.deleteDepartment(deleteTarget.value.id, deleteTarget.value.version)) deleteTarget.value = null
}

onMounted(() => { void admin.loadDepartments() })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">ORGANIZATION ADMIN</p><h1>组织架构</h1><p>维护部门树、显示顺序和上下级归属；员工归属请前往“用户与权限”。</p></div><button class="primary-action" @click="openCreate()">＋ 新建部门</button></div>
  <p v-if="admin.message" class="notice success">{{ admin.message }}</p><p v-if="admin.error" class="notice error">{{ admin.error }}</p>
  <section class="panel">
    <p v-if="admin.loading" class="empty">正在加载组织架构…</p>
    <template v-else-if="admin.departments.length"><div class="table-wrap"><table class="data-table department-table"><thead><tr><th>部门</th><th>上级部门</th><th>用户</th><th>下级部门</th><th>排序</th><th>类型</th><th>版本</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="department in admin.departments" :key="department.id"><td><div class="department-name" :style="{ paddingLeft: `${department.depth * 20}px` }"><span v-if="department.depth" class="tree-branch">└</span><strong>{{ department.name }}</strong><small>{{ department.id }}</small></div></td><td>{{ department.parentName || '公司根节点' }}</td><td>{{ department.userCount }} 人</td><td>{{ department.childCount }} 个</td><td>{{ department.sortOrder }}</td><td><em :class="{ archived: !department.isSystem }">{{ department.isSystem ? '系统预置' : '自定义' }}</em></td><td>v{{ department.version }}</td><td class="task-actions"><button class="secondary" @click="openEdit(department)">编辑</button><button class="secondary" @click="openCreate(department.id)">新增下级</button><button v-if="!department.isSystem" class="danger-outline" :disabled="department.userCount > 0 || department.childCount > 0" :title="department.userCount || department.childCount ? '请先移除用户和下级部门' : '删除部门'" @click="deleteTarget = department">删除</button></td></tr></tbody></table></div></template>
    <p v-else class="empty">暂无部门数据。</p>
  </section>

  <OaDialog :open="editorOpen" :title="editingId ? '编辑部门' : '新建部门'" :description="editingId ? '移动部门会即时影响部门数据范围，并使现有登录会话失效。' : '新部门创建后可在用户管理中分配员工。'" submit-label="保存部门" :busy="admin.saving" @close="editorOpen = false" @submit="saveDepartment">
    <div class="dialog-grid"><label class="dialog-field">部门编码<input v-model="form.id" maxlength="64" :disabled="Boolean(editingId)" placeholder="例如 customer-success"><small>创建后不可修改，支持字母、数字、点、横线或下划线。</small></label><label class="dialog-field">部门名称<input v-model="form.name" maxlength="100" placeholder="例如 客户成功部"></label></div>
    <div class="dialog-grid"><label class="dialog-field">上级部门<select v-model="form.parentId" :disabled="editingRoot"><option value="">请选择</option><option v-for="department in parentOptions" :key="department.id" :value="department.id">{{ '　'.repeat(department.depth) }}{{ department.name }}</option></select><small>{{ editingRoot ? '公司根节点不能移动。' : '不能选择自身或下级部门。' }}</small></label><label class="dialog-field">显示排序<input v-model.number="form.sortOrder" type="number" min="0" max="9999"><small>同级部门按数值从小到大显示。</small></label></div>
    <p v-if="validationError" class="dialog-error">{{ validationError }}</p>
  </OaDialog>

  <OaDialog :open="Boolean(deleteTarget)" title="删除部门" :description="deleteTarget ? `${deleteTarget.name}（${deleteTarget.id}）` : ''" submit-label="确认删除" :busy="admin.saving" danger @close="deleteTarget = null" @submit="confirmDelete"><p class="operation-warning">删除后不能恢复；系统部门、存在用户或存在下级部门时不允许删除。</p></OaDialog>
</template>
