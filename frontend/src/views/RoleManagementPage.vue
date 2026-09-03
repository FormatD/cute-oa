<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import OaDialog from '../components/OaDialog.vue'
import type { DataScope, SecurityRole } from '../api/types'
import { useIdentityStore } from '../stores/identity'

const identity = useIdentityStore()
const editorOpen = ref(false)
const editing = ref(false)
const deleteTarget = ref<SecurityRole | null>(null)
const validationError = ref('')
const form = reactive({ code: '', name: '', permissions: [] as string[], leaveDataScope: 'SELF' as DataScope, expenseDataScope: 'SELF' as DataScope, travelDataScope: 'SELF' as DataScope, purchaseDataScope: 'SELF' as DataScope, sealDataScope: 'SELF' as DataScope, personnelDataScope: 'SELF' as DataScope, attendanceDataScope: 'SELF' as DataScope, contractDataScope: 'SELF' as DataScope })
const dataScopeOptions: Array<{ value: DataScope; label: string }> = [{ value: 'SELF', label: '仅本人' }, { value: 'DEPARTMENT', label: '本部门' }, { value: 'DEPARTMENT_AND_CHILDREN', label: '本部门及下级部门' }, { value: 'COMPANY', label: '全公司' }]
const scopeLabel = (scope: DataScope) => dataScopeOptions.find(item => item.value === scope)?.label ?? scope

function openCreate() {
  editing.value = false
  Object.assign(form, { code: '', name: '', permissions: [], leaveDataScope: 'SELF', expenseDataScope: 'SELF', travelDataScope: 'SELF', purchaseDataScope: 'SELF', sealDataScope: 'SELF', personnelDataScope: 'SELF', attendanceDataScope: 'SELF', contractDataScope: 'SELF' })
  validationError.value = ''
  editorOpen.value = true
}

function openEdit(role: SecurityRole) {
  editing.value = true
  Object.assign(form, { code: role.code, name: role.name, permissions: [...role.permissions], leaveDataScope: role.dataScopes?.Leave ?? 'SELF', expenseDataScope: role.dataScopes?.Expense ?? 'SELF', travelDataScope: role.dataScopes?.Travel ?? 'SELF', purchaseDataScope: role.dataScopes?.Purchase ?? 'SELF', sealDataScope: role.dataScopes?.Seal ?? 'SELF', personnelDataScope: role.dataScopes?.Personnel ?? 'SELF', attendanceDataScope: role.dataScopes?.Attendance ?? 'SELF', contractDataScope: role.dataScopes?.Contract ?? 'SELF' })
  validationError.value = ''
  editorOpen.value = true
}

async function saveRole() {
  validationError.value = ''
  if (!form.code.trim() || !form.name.trim()) {
    validationError.value = '请填写角色编码和名称。'
    return
  }
  if (form.leaveDataScope !== 'SELF' && !form.permissions.includes('LEAVE_SCOPE_VIEW')) {
    validationError.value = '请假范围超过本人时，需要同时选择“请假范围查看”。'
    return
  }
  if (form.expenseDataScope !== 'SELF' && !form.permissions.some(item => item === 'EXPENSE_SCOPE_VIEW' || item === 'EXPENSE_ALL_VIEW')) {
    validationError.value = '报销范围超过本人时，需要同时选择“报销范围查看”。'
    return
  }
  if (form.travelDataScope !== 'SELF' && !form.permissions.includes('TRAVEL_SCOPE_VIEW')) {
    validationError.value = '出差范围超过本人时，需要同时选择“出差范围查看”。'
    return
  }
  if (form.purchaseDataScope !== 'SELF' && !form.permissions.includes('PURCHASE_SCOPE_VIEW')) {
    validationError.value = '采购范围超过本人时，需要同时选择“采购范围查看”。'
    return
  }
  if (form.permissions.includes('PURCHASE_MANAGE') && !form.permissions.includes('PURCHASE_SCOPE_VIEW')) {
    validationError.value = '采购执行维护必须同时选择“采购范围查看”。'
    return
  }
  if (form.sealDataScope !== 'SELF' && !form.permissions.includes('SEAL_SCOPE_VIEW')) {
    validationError.value = '用章范围超过本人时，需要同时选择“用章范围查看”。'
    return
  }
  if (form.permissions.includes('SEAL_MANAGE') && !form.permissions.includes('SEAL_SCOPE_VIEW')) {
    validationError.value = '用章维护必须同时选择“用章范围查看”。'
    return
  }
  if (form.personnelDataScope !== 'SELF' && !form.permissions.includes('PERSONNEL_SCOPE_VIEW')) {
    validationError.value = '人事档案范围超过本人时，需要同时选择“人事档案范围查看”。'
    return
  }
  if (form.permissions.includes('PERSONNEL_EXPORT') && !form.permissions.includes('PERSONNEL_SCOPE_VIEW')) {
    identity.error = '花名册导出权限必须同时授予人事档案范围查看权限。'
    return
  }
  if (form.permissions.includes('PERSONNEL_MANAGE') && !form.permissions.includes('PERSONNEL_SCOPE_VIEW')) {
    validationError.value = '人事档案维护必须同时选择“人事档案范围查看”。'
    return
  }
  if (form.attendanceDataScope !== 'SELF' && !form.permissions.includes('ATTENDANCE_SCOPE_VIEW')) {
    validationError.value = '考勤范围超过本人时，需要同时选择“考勤范围查看”。'
    return
  }
  if (form.permissions.includes('ATTENDANCE_MANAGE') && !form.permissions.includes('ATTENDANCE_SCOPE_VIEW')) {
    validationError.value = '考勤维护必须同时选择“考勤范围查看”。'
    return
  }
  if (form.contractDataScope !== 'SELF' && !form.permissions.includes('CONTRACT_SCOPE_VIEW')) {
    validationError.value = '劳动合同范围超过本人时，需要同时选择“劳动合同范围查看”。'
    return
  }
  if (form.permissions.includes('CONTRACT_MANAGE') && !form.permissions.includes('CONTRACT_SCOPE_VIEW')) {
    validationError.value = '劳动合同维护必须同时选择“劳动合同范围查看”。'
    return
  }
  const payload = { code: form.code.trim(), name: form.name.trim(), permissions: [...form.permissions], dataScopes: { Leave: form.leaveDataScope, Expense: form.expenseDataScope, Travel: form.travelDataScope, Purchase: form.purchaseDataScope, Seal: form.sealDataScope, Personnel: form.personnelDataScope, Attendance: form.attendanceDataScope, Contract: form.contractDataScope } }
  const success = editing.value ? await identity.updateRole(payload) : await identity.createRole(payload)
  if (success) editorOpen.value = false
}

async function confirmDelete() {
  if (deleteTarget.value && await identity.deleteRole(deleteTarget.value.code)) deleteTarget.value = null
}

onMounted(() => { void Promise.all([identity.loadRoles(), identity.loadPermissions()]) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">ROLE BASED ACCESS</p><h1>角色与权限</h1><p>维护角色定义和操作权限；用户分配请前往“用户与权限”。</p></div><button class="primary-action" @click="openCreate">＋ 新建角色</button></div>
  <p v-if="identity.message" class="notice success">{{ identity.message }}</p><p v-if="identity.error" class="notice error">{{ identity.error }}</p>
  <section class="panel">
    <p v-if="!identity.roles.length" class="empty">正在加载角色…</p>
    <template v-else><div class="table-wrap"><table class="data-table role-table"><thead><tr><th>角色</th><th>类型</th><th>已分配用户</th><th>数据范围</th><th>操作权限</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="role in identity.roles" :key="role.code"><td><strong>{{ role.name }}</strong><small>{{ role.code }}</small></td><td><em :class="{ archived: !role.isSystem }">{{ role.isSystem ? '系统预置' : '自定义' }}</em></td>        <td>{{ role.userCount }} 人</td>
        <td>
          <small>请假：{{ scopeLabel(role.dataScopes?.Leave ?? 'SELF') }}</small>
          <small>报销：{{ scopeLabel(role.dataScopes?.Expense ?? 'SELF') }}</small>
          <small>出差：{{ scopeLabel(role.dataScopes?.Travel ?? 'SELF') }}</small>
          <small>采购：{{ scopeLabel(role.dataScopes?.Purchase ?? 'SELF') }}</small>
          <small>用章：{{ scopeLabel(role.dataScopes?.Seal ?? 'SELF') }}</small>
          <small>人事：{{ scopeLabel(role.dataScopes?.Personnel ?? 'SELF') }}</small>
          <small>考勤：{{ scopeLabel(role.dataScopes?.Attendance ?? 'SELF') }}</small>
          <small>合同：{{ scopeLabel(role.dataScopes?.Contract ?? 'SELF') }}</small>
        </td>
        <td>
          <span v-if="!role.permissions.length" class="muted">基础业务权限</span>
          <span v-for="permission in role.permissions" :key="permission" class="role-chip">{{ identity.permissions.find(item => item.code === permission)?.name ?? permission }}</span>
        </td>
        <td class="task-actions">
          <button class="secondary" @click="openEdit(role)">编辑</button>
          <button v-if="!role.isSystem" class="danger-outline" :disabled="role.userCount > 0" :title="role.userCount ? '请先从用户移除此角色' : '删除角色'" @click="deleteTarget = role">删除</button>
        </td>
      </tr>
    </tbody>
  </table>
</div>
</template>
  </section>

  <OaDialog :open="editorOpen" :title="editing ? '编辑角色' : '新建角色'" :description="editing ? '权限变化后，使用该角色的有效会话将立即失效。' : '创建后可在用户管理中分配给员工。'" submit-label="保存角色" :busy="identity.saving" @close="editorOpen = false" @submit="saveRole">
    <div class="dialog-grid"><label class="dialog-field">角色编码<input v-model="form.code" maxlength="64" :disabled="editing" placeholder="例如 PROJECT_MANAGER"><small>创建后不可修改，可使用中文、字母、数字、点、横线、斜线或下划线。</small></label><label class="dialog-field">角色名称<input v-model="form.name" maxlength="64" placeholder="例如 项目经理"></label></div>
    <div class="dialog-grid">
      <label class="dialog-field">请假数据范围<select v-model="form.leaveDataScope"><option v-for="option in dataScopeOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select><small>直属上下级关系不受此范围限制。</small></label>
      <label class="dialog-field">报销数据范围<select v-model="form.expenseDataScope"><option v-for="option in dataScopeOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select><small>跨用户范围需要对应的范围查看权限。</small></label>
      <label class="dialog-field">出差数据范围<select v-model="form.travelDataScope"><option v-for="option in dataScopeOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select><small>跨用户范围需要对应的范围查看权限。</small></label>
      <label class="dialog-field">采购数据范围<select v-model="form.purchaseDataScope"><option v-for="option in dataScopeOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select><small>采购执行权限仍受所选数据范围约束。</small></label>
      <label class="dialog-field">用章数据范围<select v-model="form.sealDataScope"><option v-for="option in dataScopeOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select><small>用章执行与归还权限仍受所选数据范围约束。</small></label>
      <label class="dialog-field">人事档案数据范围<select v-model="form.personnelDataScope"><option v-for="option in dataScopeOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select><small>上级仍可查看管理链下属。</small></label>
      <label class="dialog-field">考勤数据范围<select v-model="form.attendanceDataScope"><option v-for="option in dataScopeOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select><small>员工本人和管理链上级始终可查看。</small></label>
      <label class="dialog-field">劳动合同数据范围<select v-model="form.contractDataScope"><option v-for="option in dataScopeOptions" :key="option.value" :value="option.value">{{ option.label }}</option></select><small>合同属于敏感数据，上级不会自动获得下属查看权。</small></label>
    </div>
    <fieldset class="permission-selector"><legend>操作权限</legend><label v-for="permission in identity.permissions" :key="permission.code"><input v-model="form.permissions" type="checkbox" :value="permission.code"><span><strong>{{ permission.name }}</strong><small>{{ permission.description }}</small></span></label></fieldset>
    <p v-if="validationError" class="dialog-error">{{ validationError }}</p>
  </OaDialog>

  <OaDialog :open="Boolean(deleteTarget)" title="删除自定义角色" :description="deleteTarget ? `${deleteTarget.name}（${deleteTarget.code}）` : ''" submit-label="确认删除" :busy="identity.saving" danger @close="deleteTarget = null" @submit="confirmDelete"><p class="operation-warning">删除后不能恢复；只有未分配给任何用户的自定义角色可以删除。</p></OaDialog>
</template>
