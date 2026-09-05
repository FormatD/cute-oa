<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import type { ManagedUser } from '../api/types'
import OaDialog from '../components/OaDialog.vue'
import UserSelect from '../components/UserSelect.vue'
import { useIdentityStore } from '../stores/identity'
import { useOrganizationStore } from '../stores/organization'

const identity = useIdentityStore()
const organization = useOrganizationStore()
const editingId = ref('')
const resetPassword = ref('')
const today = localDate(new Date())
const form = reactive({ id: '', name: '', departmentId: '', positionId: '', managerId: '', cumulativeWorkYears: 0, status: 'ACTIVE' as 'ACTIVE' | 'DISABLED', roles: [] as string[], password: '', version: 1, employeeNumber: '', hireDate: today, employmentType: 'FULL_TIME' as 'FULL_TIME' | 'PART_TIME' | 'INTERN' | 'CONTRACTOR', personnelStatus: 'ACTIVE' as 'ACTIVE' | 'PROBATION', probationEndDate: '', cumulativeWorkStartDate: '' })
const availablePositions = computed(() => organization.positions.filter(item => item.departmentId === form.departmentId))
const permissionLabels: Record<string, string> = { USER_MANAGE: '用户管理', PROCESS_MANAGE: '流程维护', AUDIT_VIEW: '审计查看', CALENDAR_MANAGE: '日历维护', EXPENSE_PAY: '付款登记', EXPENSE_ALL_VIEW: '全公司报销查看（兼容）', LEAVE_SCOPE_VIEW: '请假范围查看', EXPENSE_SCOPE_VIEW: '报销范围查看', TRAVEL_SCOPE_VIEW: '出差范围查看', PERSONNEL_SCOPE_VIEW: '人事档案范围查看', PERSONNEL_MANAGE: '人事档案维护', ATTENDANCE_SCOPE_VIEW: '考勤范围查看', ATTENDANCE_MANAGE: '考勤维护', CONTRACT_SCOPE_VIEW: '劳动合同范围查看', CONTRACT_MANAGE: '劳动合同维护', ORG_MANAGE: '组织架构维护', ANNOUNCEMENT_MANAGE: '公告管理' }

function createUser() {
  editingId.value = 'new'
  Object.assign(form, { id: '', name: '', departmentId: organization.departments[0]?.id ?? '', positionId: '', managerId: '', cumulativeWorkYears: 0, status: 'ACTIVE', roles: ['员工'], password: '', version: 1, employeeNumber: '', hireDate: today, employmentType: 'FULL_TIME', personnelStatus: 'ACTIVE', probationEndDate: '', cumulativeWorkStartDate: '' })
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
  if (!form.name.trim() || !form.departmentId || !form.roles.length || (editingId.value === 'new' && (!form.id.trim() || !form.password || !form.employeeNumber.trim() || !form.hireDate || (form.personnelStatus === 'PROBATION' && !form.probationEndDate)))) {
    identity.error = '请填写用户 ID、姓名、部门、角色、初始密码及完整建档信息。'
    return
  }
  const success = editingId.value === 'new'
    ? await identity.createUser({ id: form.id, name: form.name, departmentId: form.departmentId, positionId: form.positionId || null, managerId: form.managerId || null, cumulativeWorkYears: form.cumulativeWorkYears, roles: form.roles, password: form.password, employeeNumber: form.employeeNumber, hireDate: form.hireDate, employmentType: form.employmentType, personnelStatus: form.personnelStatus, probationEndDate: form.probationEndDate || null, cumulativeWorkStartDate: form.cumulativeWorkStartDate || null })
    : await identity.updateUser(editingId.value, { name: form.name, departmentId: form.departmentId, positionId: form.positionId || null, managerId: form.managerId || null, cumulativeWorkYears: form.cumulativeWorkYears, status: form.status, roles: form.roles, version: form.version })
  if (success) editingId.value = ''
}

async function submitPasswordReset() {
  if (!editingId.value || editingId.value === 'new' || !resetPassword.value) return
  if (!window.confirm(`确认重置用户 ${editingId.value} 的登录密码吗？`)) return
  if (await identity.resetPassword(editingId.value, resetPassword.value)) resetPassword.value = ''
}

async function submitMfaReset() {
  if (!editingId.value || editingId.value === 'new') return
  if (!window.confirm(`确认重置用户 ${editingId.value} 的多因素认证吗？该用户的所有登录会话将被撤销。`)) return
  await identity.resetMfa(editingId.value)
}

function changePage(offset: number) { identity.page += offset; void identity.loadUsers() }
function departmentChanged() { if (!availablePositions.value.some(item => item.id === form.positionId)) form.positionId = '' }
function localDate(value: Date) { return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}` }

onMounted(async () => { await Promise.all([organization.loadOrganization(), identity.initialize()]) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">IDENTITY & ACCESS</p><h1>用户与权限</h1><p>维护账号状态、组织关系、社会工龄和角色授权。</p></div><button class="primary-action" @click="createUser">＋ 新建用户</button></div>
  <p v-if="identity.message" class="notice success">{{ identity.message }}</p><p v-if="identity.error" class="notice error">{{ identity.error }}</p>
  <section class="panel"><form class="filter-bar" @submit.prevent="identity.search"><input v-model="identity.filters.keyword" placeholder="用户 ID 或姓名"><select v-model="identity.filters.status"><option value="">全部状态</option><option value="ACTIVE">启用</option><option value="DISABLED">停用</option></select><select v-model="identity.filters.departmentId"><option value="">全部部门</option><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select><button type="submit">查询</button><button class="secondary" type="button" @click="identity.resetFilters">重置</button></form>
    <p v-if="identity.loading" class="empty">正在加载用户…</p><template v-else-if="identity.pagedUsers.items.length"><div class="table-wrap"><table class="data-table user-table"><thead><tr><th>用户</th><th>部门 / 上级</th><th>岗位</th><th>安全角色</th><th>工龄</th><th>状态</th><th>更新时间</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="user in identity.pagedUsers.items" :key="user.id"><td><strong>{{ user.name }}</strong><small>{{ user.id }}</small></td><td>{{ user.departmentName }}<small>{{ user.managerName ? `直属上级：${user.managerName}` : '无直属上级' }}</small></td><td>{{ user.positionName || '未分配' }}</td><td><span v-for="role in user.roles" :key="role" class="role-chip">{{ role }}</span></td><td>{{ user.cumulativeWorkYears }} 年</td><td><em :class="{ archived: user.status === 'DISABLED' }">{{ user.status === 'ACTIVE' ? '启用' : '停用' }}</em><small v-if="user.mustChangePassword">待首次改密</small></td><td>{{ new Date(user.updatedAt).toLocaleString('zh-CN') }}</td><td class="task-actions"><button class="secondary" @click="editUser(user)">编辑</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ identity.pagedUsers.total }} 条</span><div><button class="secondary" :disabled="identity.pagedUsers.currentPage === 1" @click="changePage(-1)">上一页</button><b>{{ identity.pagedUsers.currentPage }} / {{ identity.pagedUsers.totalPages }}</b><button class="secondary" :disabled="identity.pagedUsers.currentPage === identity.pagedUsers.totalPages" @click="changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前条件下暂无用户。</p>
  </section>

  <OaDialog
    :open="Boolean(editingId)"
    :title="editingId === 'new' ? '新建用户并建档' : `编辑用户 · ${editingId}`"
    :description="editingId === 'new' ? '填写账号基本信息、组织关系及初始安全建档数据。' : '维护员工账号状态、组织关系、主岗位与安全角色。'"
    :submit-label="identity.saving ? '保存中…' : '保存用户'"
    :busy="identity.saving"
    width="860px"
    @close="editingId = ''"
    @submit="save"
  >
    <div class="dialog-grid">
      <label class="dialog-field">
        用户 ID
        <input v-model="form.id" maxlength="64" :disabled="editingId !== 'new'" placeholder="例如 u-zhou">
      </label>
      <label class="dialog-field">
        姓名
        <input v-model="form.name" maxlength="64">
      </label>
      <label class="dialog-field">
        所属部门
        <select v-model="form.departmentId" @change="departmentChanged">
          <option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option>
        </select>
      </label>
      <label class="dialog-field">
        主岗位
        <select v-model="form.positionId">
          <option value="">未分配</option>
          <option v-for="position in availablePositions" :key="position.id" :value="position.id">{{ position.name }}</option>
        </select>
        <small>岗位描述组织职责，不授予系统权限。</small>
      </label>
      <label class="dialog-field">
        直属上级
        <UserSelect v-model="form.managerId" :exclude-user-ids="form.id ? [form.id] : []" placeholder="选择直属上级（无则留空）" />
      </label>
      <label class="dialog-field">
        社会工龄（年）
        <input v-model.number="form.cumulativeWorkYears" type="number" min="0" max="60">
        <small>未填写累计工作起始日时作为兼容值。</small>
      </label>
      <label v-if="editingId !== 'new'" class="dialog-field">
        账号状态
        <select v-model="form.status">
          <option value="ACTIVE">启用</option>
          <option value="DISABLED">停用</option>
        </select>
      </label>
      <template v-if="editingId === 'new'">
        <label class="dialog-field">
          工号
          <input v-model.trim="form.employeeNumber" maxlength="32" placeholder="例如 EMP-2026-001">
        </label>
        <label class="dialog-field">
          入职日期
          <input v-model="form.hireDate" type="date">
        </label>
        <label class="dialog-field">
          用工类型
          <select v-model="form.employmentType">
            <option value="FULL_TIME">全职</option>
            <option value="PART_TIME">兼职</option>
            <option value="INTERN">实习</option>
            <option value="CONTRACTOR">外包</option>
          </select>
        </label>
        <label class="dialog-field">
          人事状态
          <select v-model="form.personnelStatus">
            <option value="ACTIVE">在职</option>
            <option value="PROBATION">试用</option>
          </select>
        </label>
        <label v-if="form.personnelStatus === 'PROBATION'" class="dialog-field">
          试用期结束
          <input v-model="form.probationEndDate" type="date">
        </label>
        <label class="dialog-field">
          累计工作起始日
          <input v-model="form.cumulativeWorkStartDate" type="date">
          <small>用于精确计算法定年假；不确定时可暂不填写。</small>
        </label>
        <label class="dialog-field">
          临时密码
          <input v-model="form.password" type="password" minlength="12" maxlength="128" autocomplete="new-password">
          <small>12–128 位，须包含大小写字母、数字和特殊字符；员工首次登录仍必须设置新的正式密码。</small>
        </label>
      </template>
    </div>

    <fieldset class="role-selector" style="margin-top: 14px;">
      <legend>安全角色与权限</legend>
      <label v-for="role in identity.roles" :key="role.code">
        <input v-model="form.roles" type="checkbox" :value="role.code">
        <span>
          <strong>{{ role.name }}</strong>
          <small>{{ role.permissions.map(permission => permissionLabels[permission] ?? permission).join('、') || '基础员工权限' }}</small>
        </span>
      </label>
    </fieldset>

    <template v-if="editingId !== 'new'">
      <div class="password-reset">
        <div>
          <strong>重置临时密码</strong>
          <small>须为 12 位四类字符强密码；重置后撤销全部会话并要求用户下次登录设置正式密码。</small>
        </div>
        <input v-model="resetPassword" type="password" minlength="12" maxlength="128" autocomplete="new-password" placeholder="输入临时密码">
        <button class="secondary" :disabled="identity.saving || resetPassword.length < 12" type="button" @click="submitPasswordReset">重置密码</button>
      </div>
      <div v-if="identity.users.find(user => user.id === editingId)?.mfaEnabled" class="password-reset">
        <div>
          <strong>重置多因素认证</strong>
          <small>用于用户遗失身份验证器和全部恢复码；会撤销该用户所有登录会话。</small>
        </div>
        <span class="mfa-admin-state">当前已启用</span>
        <button class="danger-outline" :disabled="identity.saving" type="button" @click="submitMfaReset">重置 MFA</button>
      </div>
    </template>

    <p v-if="identity.error" class="dialog-error">{{ identity.error }}</p>
  </OaDialog>
</template>
