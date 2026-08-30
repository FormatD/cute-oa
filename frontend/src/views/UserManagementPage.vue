<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import type { ManagedUser } from '../api/types'
import { useIdentityStore } from '../stores/identity'
import { useOrganizationStore } from '../stores/organization'

const identity = useIdentityStore()
const organization = useOrganizationStore()
const editingId = ref('')
const resetPassword = ref('')
const form = reactive({ id: '', name: '', departmentId: '', positionId: '', managerId: '', cumulativeWorkYears: 0, status: 'ACTIVE' as 'ACTIVE' | 'DISABLED', roles: [] as string[], password: '', version: 1 })
const availablePositions = computed(() => organization.positions.filter(item => item.departmentId === form.departmentId))
const permissionLabels: Record<string, string> = { USER_MANAGE: '用户管理', PROCESS_MANAGE: '流程维护', AUDIT_VIEW: '审计查看', CALENDAR_MANAGE: '日历维护', EXPENSE_PAY: '付款登记', EXPENSE_ALL_VIEW: '全公司报销查看（兼容）', LEAVE_SCOPE_VIEW: '请假范围查看', EXPENSE_SCOPE_VIEW: '报销范围查看', TRAVEL_SCOPE_VIEW: '出差范围查看', PERSONNEL_SCOPE_VIEW: '人事档案范围查看', PERSONNEL_MANAGE: '人事档案维护', ATTENDANCE_SCOPE_VIEW: '考勤范围查看', ATTENDANCE_MANAGE: '考勤维护', CONTRACT_SCOPE_VIEW: '劳动合同范围查看', CONTRACT_MANAGE: '劳动合同维护', ORG_MANAGE: '组织架构维护', ANNOUNCEMENT_MANAGE: '公告管理' }

function createUser() {
  editingId.value = 'new'
  Object.assign(form, { id: '', name: '', departmentId: organization.departments[0]?.id ?? '', positionId: '', managerId: '', cumulativeWorkYears: 0, status: 'ACTIVE', roles: ['员工'], password: 'Oa@123456', version: 1 })
  resetPassword.value = ''
  identity.error = ''
  identity.message = ''
}

function editUser(user: ManagedUser) {
  editingId.value = user.id
  Object.assign(form, { id: user.id, name: user.name, departmentId: user.departmentId, positionId: user.positionId ?? '', managerId: user.managerId ?? '', cumulativeWorkYears: user.cumulativeWorkYears, status: user.status, roles: [...user.roles], password: '', version: user.version })
  resetPassword.value = ''
  identity.error = ''
  identity.message = ''
}

async function save() {
  if (!form.name.trim() || !form.departmentId || !form.roles.length || (editingId.value === 'new' && (!form.id.trim() || !form.password))) {
    identity.error = '请填写用户 ID、姓名、部门、角色和初始密码。'
    return
  }
  const success = editingId.value === 'new'
    ? await identity.createUser({ id: form.id, name: form.name, departmentId: form.departmentId, positionId: form.positionId || null, managerId: form.managerId || null, cumulativeWorkYears: form.cumulativeWorkYears, roles: form.roles, password: form.password })
    : await identity.updateUser(editingId.value, { name: form.name, departmentId: form.departmentId, positionId: form.positionId || null, managerId: form.managerId || null, cumulativeWorkYears: form.cumulativeWorkYears, status: form.status, roles: form.roles, version: form.version })
  if (success) editingId.value = ''
}

async function submitPasswordReset() {
  if (!editingId.value || editingId.value === 'new' || !resetPassword.value) return
  if (!window.confirm(`确认重置用户 ${editingId.value} 的登录密码吗？`)) return
  if (await identity.resetPassword(editingId.value, resetPassword.value)) resetPassword.value = ''
}

function changePage(offset: number) { identity.page += offset; void identity.loadUsers() }
function departmentChanged() { if (!availablePositions.value.some(item => item.id === form.positionId)) form.positionId = '' }

onMounted(async () => { await Promise.all([organization.loadOrganization(), identity.initialize()]) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">IDENTITY & ACCESS</p><h1>用户与权限</h1><p>维护账号状态、组织关系、社会工龄和角色授权。</p></div><button class="primary-action" @click="createUser">＋ 新建用户</button></div>
  <p v-if="identity.message" class="notice success">{{ identity.message }}</p><p v-if="identity.error" class="notice error">{{ identity.error }}</p>
  <section class="panel"><form class="filter-bar" @submit.prevent="identity.search"><input v-model="identity.filters.keyword" placeholder="用户 ID 或姓名"><select v-model="identity.filters.status"><option value="">全部状态</option><option value="ACTIVE">启用</option><option value="DISABLED">停用</option></select><select v-model="identity.filters.departmentId"><option value="">全部部门</option><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select><button type="submit">查询</button><button class="secondary" type="button" @click="identity.resetFilters">重置</button></form>
    <p v-if="identity.loading" class="empty">正在加载用户…</p><template v-else-if="identity.pagedUsers.items.length"><div class="table-wrap"><table class="data-table user-table"><thead><tr><th>用户</th><th>部门 / 上级</th><th>岗位</th><th>安全角色</th><th>工龄</th><th>状态</th><th>更新时间</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="user in identity.pagedUsers.items" :key="user.id"><td><strong>{{ user.name }}</strong><small>{{ user.id }}</small></td><td>{{ user.departmentName }}<small>{{ user.managerName ? `直属上级：${user.managerName}` : '无直属上级' }}</small></td><td>{{ user.positionName || '未分配' }}</td><td><span v-for="role in user.roles" :key="role" class="role-chip">{{ role }}</span></td><td>{{ user.cumulativeWorkYears }} 年</td><td><em :class="{ archived: user.status === 'DISABLED' }">{{ user.status === 'ACTIVE' ? '启用' : '停用' }}</em></td><td>{{ new Date(user.updatedAt).toLocaleString('zh-CN') }}</td><td class="task-actions"><button class="secondary" @click="editUser(user)">编辑</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ identity.pagedUsers.total }} 条</span><div><button class="secondary" :disabled="identity.pagedUsers.currentPage === 1" @click="changePage(-1)">上一页</button><b>{{ identity.pagedUsers.currentPage }} / {{ identity.pagedUsers.totalPages }}</b><button class="secondary" :disabled="identity.pagedUsers.currentPage === identity.pagedUsers.totalPages" @click="changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前条件下暂无用户。</p>
  </section>

  <section v-if="editingId" class="panel user-editor"><div class="section-title"><div><p class="eyebrow">USER EDITOR</p><h2>{{ editingId === 'new' ? '新建用户' : `编辑用户 · ${editingId}` }}</h2></div><button class="secondary" @click="editingId = ''">关闭</button></div><form class="user-form" @submit.prevent="save"><label>用户 ID<input v-model="form.id" maxlength="64" :disabled="editingId !== 'new'" placeholder="例如 u-zhou"></label><label>姓名<input v-model="form.name" maxlength="64"></label><label>所属部门<select v-model="form.departmentId" @change="departmentChanged"><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select></label><label>主岗位<select v-model="form.positionId"><option value="">未分配</option><option v-for="position in availablePositions" :key="position.id" :value="position.id">{{ position.name }}</option></select><small>岗位描述组织职责，不授予系统权限。</small></label><label>直属上级<select v-model="form.managerId"><option value="">无</option><option v-for="employee in organization.directoryEmployees.filter(item => item.id !== form.id)" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.positionName || employee.role }}</option></select></label><label>社会工龄（年）<input v-model.number="form.cumulativeWorkYears" type="number" min="0" max="60"></label><label v-if="editingId !== 'new'">账号状态<select v-model="form.status"><option value="ACTIVE">启用</option><option value="DISABLED">停用</option></select></label><label v-if="editingId === 'new'">初始密码<input v-model="form.password" type="password" maxlength="128"><small>8–128 位，必须同时包含字母和数字。</small></label><fieldset class="wide role-selector"><legend>安全角色与权限</legend><label v-for="role in identity.roles" :key="role.code"><input v-model="form.roles" type="checkbox" :value="role.code"><span><strong>{{ role.name }}</strong><small>{{ role.permissions.map(permission => permissionLabels[permission] ?? permission).join('、') || '基础员工权限' }}</small></span></label></fieldset><div class="form-actions wide"><button :disabled="identity.saving" type="submit">{{ identity.saving ? '保存中…' : '保存用户' }}</button></div></form>
    <form v-if="editingId !== 'new'" class="password-reset" @submit.prevent="submitPasswordReset"><div><strong>重置登录密码</strong><small>重置后同时清除登录失败锁定状态。</small></div><input v-model="resetPassword" type="password" maxlength="128" placeholder="输入新密码"><button class="secondary" :disabled="identity.saving || !resetPassword" type="submit">重置密码</button></form>
  </section>
</template>
