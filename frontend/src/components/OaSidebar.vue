<script setup lang="ts">
import type { Employee, Summary } from '../api/types'

const props = defineProps<{ open: boolean; activeNav: string; summary: Summary | null; currentUser?: Employee }>()
const emit = defineEmits<{ navigate: [route: string]; close: [] }>()

function go(route: string) { emit('navigate', route); emit('close') }
function hasPermission(permission: string) { return props.currentUser?.permissions?.includes(permission) === true }
</script>

<template>
  <aside class="sidebar" :class="{ open }">
    <div class="brand"><span class="brand-mark">OA</span><div><strong>{{ summary?.tenant ?? 'xxx公司' }}</strong><small>协同办公平台</small></div></div>
    <nav class="nav-menu" aria-label="主导航">
      <p class="nav-label">工作台</p>
      <button :class="{ active: activeNav === 'workbench' }" @click="go('workbench')"><span>⌂</span>工作台</button>
      <button :class="{ active: activeNav === 'documents' }" @click="go('documents')"><span>📖</span>知识库与制度</button>
      <button :class="{ active: activeNav === 'announcements' }" @click="go('announcements')"><span>◈</span>公司公告</button>
      <p class="nav-label">业务中心</p>
      <button :class="{ active: activeNav === 'leave' }" @click="go('leave')"><span>◫</span>请假管理</button>
      <button :class="{ active: activeNav === 'expense' }" @click="go('expense')"><span>¥</span>费用报销</button>
      <button :class="{ active: activeNav === 'travel' }" @click="go('travel')"><span>⌖</span>出差管理</button>
      <button :class="{ active: activeNav === 'purchase' }" @click="go('purchase')"><span>▦</span>采购管理</button>
      <button :class="{ active: activeNav === 'seal' }" @click="go('seal')"><span>印</span>用章管理</button>
      <button :class="{ active: activeNav === 'approval' }" @click="go('approval')"><span>✓</span>审批中心 <b v-if="summary?.pendingTaskCount">{{ summary.pendingTaskCount }}</b></button>
      <button :class="{ active: activeNav === 'copies' }" @click="go('copies')"><span>▤</span>待我阅读 <b v-if="summary?.pendingReadCount">{{ summary.pendingReadCount }}</b></button>
      <button :class="{ active: activeNav === 'delegations' }" @click="go('delegations')"><span>⇄</span>审批委托</button>
      <p class="nav-label">人力资源</p>
      <button :class="{ active: activeNav === 'hr' }" @click="go('hr/employees')"><span>♚</span>人事档案</button>
      <button :class="{ active: activeNav === 'personnel-cases' }" @click="go('hr/personnel-cases')"><span>☑</span>员工办理</button>
      <button :class="{ active: activeNav === 'contracts' }" @click="go('hr/contracts')"><span>▧</span>劳动合同</button>
      <button :class="{ active: activeNav === 'attendance' }" @click="go('attendance')"><span>◷</span>考勤管理</button>
      <p class="nav-label">组织与设置</p>
      <button :class="{ active: activeNav === 'organization' }" @click="go('organization')"><span>♙</span>组织通讯录</button>
      <button v-if="hasPermission('ORG_MANAGE')" :class="{ active: activeNav === 'departments' }" @click="go('departments')"><span>♜</span>组织架构</button>
      <button v-if="hasPermission('ORG_MANAGE')" :class="{ active: activeNav === 'positions' }" @click="go('positions')"><span>♝</span>岗位管理</button>
      <button v-if="hasPermission('ANNOUNCEMENT_MANAGE')" :class="{ active: activeNav === 'announcement-admin' }" @click="go('announcement-admin')"><span>▥</span>公告管理</button>
      <button :class="{ active: activeNav === 'calendar' }" @click="go('calendar')"><span>▣</span>工作日历</button>
      <button v-if="hasPermission('USER_MANAGE')" :class="{ active: activeNav === 'users' }" @click="go('users')"><span>♟</span>用户与权限</button>
      <button v-if="hasPermission('USER_MANAGE')" :class="{ active: activeNav === 'roles' }" @click="go('roles')"><span>◆</span>角色与权限</button>
      <button v-if="hasPermission('AUDIT_VIEW')" :class="{ active: activeNav === 'audit' }" @click="go('audit')"><span>⌕</span>审计日志</button>
      <button v-if="hasPermission('PROCESS_MANAGE')" :class="{ active: activeNav === 'processes' }" @click="go('processes')"><span>⌘</span>流程定义</button>
      <button :class="{ active: activeNav === 'security' }" @click="go('security')"><span>⚿</span>登录设备</button>
    </nav>
    <div class="sidebar-user"><span class="avatar">{{ currentUser?.name?.slice(0, 1) ?? '张' }}</span><div><strong>{{ currentUser?.name ?? '演示用户' }}</strong><small>{{ currentUser?.departmentName ?? '研发部' }}</small></div></div>
  </aside>
</template>
