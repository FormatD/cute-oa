<script setup lang="ts">
import { computed } from 'vue'
import type { Employee, Notification } from '../api/types'

const props = defineProps<{ activeNav: string; currentUser?: Employee; notifications: Notification[] }>()
const emit = defineEmits<{ menu: []; logout: [] }>()
const pageName = computed(() => ({ workbench: '工作台', announcements: '公司公告', 'announcement-admin': '公告管理', leave: '请假管理', expense: '费用报销', travel: '出差管理', approval: '事项中心', copies: '审批抄送', delegations: '审批委托', hr: '人事档案', 'personnel-cases': '员工办理', contracts: '劳动合同', attendance: '考勤管理', organization: '组织通讯录', departments: '组织架构', positions: '岗位管理', 'business-configurations': '业务参数配置', calendar: '工作日历', users: '用户与权限', roles: '角色与权限', audit: '审计日志', processes: '流程定义', security: '登录设备' }[props.activeNav] ?? '工作台'))
const unreadCount = computed(() => props.notifications.filter(item => !item.readAt).length)
</script>

<template>
  <header class="headerbar">
    <button class="menu-toggle" aria-label="打开导航" @click="emit('menu')">☰</button>
    <div class="breadcrumb"><span>OA 协同办公</span><i>/</i><strong>{{ pageName }}</strong></div>
    <div class="header-actions"><button class="icon-button" title="通知">🔔<b v-if="unreadCount">{{ unreadCount }}</b></button><span class="signed-user"><strong>{{ currentUser?.name }}</strong><small>{{ currentUser?.role }}</small></span><button class="logout-button" @click="emit('logout')">退出</button></div>
  </header>
</template>
