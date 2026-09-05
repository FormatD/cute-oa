<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import type { Employee, Summary } from '../api/types'

interface NavItem {
  id: string
  navKey: string
  route: string
  icon: string
  name: string
  permission?: string
  badgeKey?: 'pendingTaskCount' | 'pendingReadCount'
  keywords?: string[]
}

interface NavSection {
  id: string
  title: string
  items: NavItem[]
}

const props = defineProps<{ open: boolean; activeNav: string; summary: Summary | null; currentUser?: Employee }>()
const emit = defineEmits<{ navigate: [route: string]; close: [] }>()

const searchInputRef = ref<HTMLInputElement | null>(null)
const searchQuery = ref('')

const STORAGE_KEY = 'cute_oa_sidebar_collapsed'

function loadCollapsedSections(): Record<string, boolean> {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? JSON.parse(raw) : {}
  } catch {
    return {}
  }
}

const collapsedSections = ref<Record<string, boolean>>(loadCollapsedSections())

function toggleSection(sectionId: string) {
  collapsedSections.value[sectionId] = !collapsedSections.value[sectionId]
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(collapsedSections.value))
  } catch {
    // ignore
  }
}

function clearSearch() {
  searchQuery.value = ''
  searchInputRef.value?.focus()
}

function go(route: string) {
  emit('navigate', route)
  emit('close')
}

function hasPermission(permission?: string) {
  if (!permission) return true
  return props.currentUser?.permissions?.includes(permission) === true
}

const navSections: NavSection[] = [
  {
    id: 'workbench',
    title: '工作台',
    items: [
      { id: 'workbench', navKey: 'workbench', route: 'workbench', icon: '⌂', name: '工作台', keywords: ['gzt', 'workbench', 'home'] },
      { id: 'documents', navKey: 'documents', route: 'documents', icon: '📖', name: '知识库与制度', keywords: ['zsk', 'wendang', 'doc'] },
      { id: 'announcements', navKey: 'announcements', route: 'announcements', icon: '◈', name: '公司公告', keywords: ['gg', 'gonggao', 'notice'] }
    ]
  },
  {
    id: 'business',
    title: '业务中心',
    items: [
      { id: 'leave', navKey: 'leave', route: 'leave', icon: '◫', name: '请假管理', keywords: ['qj', 'qingjia', 'leave'] },
      { id: 'expense', navKey: 'expense', route: 'expense', icon: '¥', name: '费用报销', keywords: ['bx', 'baoxiao', 'expense'] },
      { id: 'travel', navKey: 'travel', route: 'travel', icon: '⌖', name: '出差管理', keywords: ['cc', 'chuchai', 'travel'] },
      { id: 'purchase', navKey: 'purchase', route: 'purchase', icon: '▦', name: '采购管理', keywords: ['cg', 'caigou', 'purchase'] },
      { id: 'seal', navKey: 'seal', route: 'seal', icon: '印', name: '用章管理', keywords: ['yz', 'yongzhang', 'yinzhang', 'seal'] },
      { id: 'approval', navKey: 'approval', route: 'approval', icon: '✓', name: '审批中心', badgeKey: 'pendingTaskCount', keywords: ['sp', 'shenpi', 'approval'] },
      { id: 'copies', navKey: 'copies', route: 'copies', icon: '▤', name: '待我阅读', badgeKey: 'pendingReadCount', keywords: ['cs', 'chaosong', 'yuedu'] },
      { id: 'delegations', navKey: 'delegations', route: 'delegations', icon: '⇄', name: '审批委托', keywords: ['wt', 'weituo'] }
    ]
  },
  {
    id: 'hr',
    title: '人力资源',
    items: [
      { id: 'hr-employees', navKey: 'hr', route: 'hr/employees', icon: '♚', name: '人事档案', keywords: ['rs', 'renshi', 'dangan', 'hr'] },
      { id: 'personnel-cases', navKey: 'personnel-cases', route: 'hr/personnel-cases', icon: '☑', name: '员工办理', keywords: ['bl', 'banli', 'ruzhi', 'lizhi'] },
      { id: 'contracts', navKey: 'contracts', route: 'hr/contracts', icon: '▧', name: '劳动合同', keywords: ['ht', 'hetong', 'contract'] },
      { id: 'attendance', navKey: 'attendance', route: 'attendance', icon: '◷', name: '考勤管理', keywords: ['kq', 'kaoqin', 'attendance'] }
    ]
  },
  {
    id: 'organization',
    title: '组织与设置',
    items: [
      { id: 'organization', navKey: 'organization', route: 'organization', icon: '♙', name: '组织通讯录', keywords: ['txl', 'tongxunlu'] },
      { id: 'departments', navKey: 'departments', route: 'departments', icon: '♜', name: '组织架构', permission: 'ORG_MANAGE', keywords: ['bm', 'bumen', 'jiagou'] },
      { id: 'positions', navKey: 'positions', route: 'positions', icon: '♝', name: '岗位管理', permission: 'ORG_MANAGE', keywords: ['gw', 'gangwei'] },
      { id: 'business-configurations', navKey: 'business-configurations', route: 'business-configurations', icon: '⚙', name: '业务参数配置', permission: 'BUSINESS_CONFIG_MANAGE', keywords: ['pz', 'peizhi', 'canshu', 'config', 'guize'] },
      { id: 'announcement-admin', navKey: 'announcement-admin', route: 'announcement-admin', icon: '▥', name: '公告管理', permission: 'ANNOUNCEMENT_MANAGE', keywords: ['gggl', 'gonggao'] },
      { id: 'calendar', navKey: 'calendar', route: 'calendar', icon: '▣', name: '工作日历', keywords: ['rl', 'rili', 'calendar'] },
      { id: 'users', navKey: 'users', route: 'users', icon: '♟', name: '用户与权限', permission: 'USER_MANAGE', keywords: ['yh', 'yonghu', 'quanxian'] },
      { id: 'roles', navKey: 'roles', route: 'roles', icon: '◆', name: '角色与权限', permission: 'USER_MANAGE', keywords: ['js', 'juese'] },
      { id: 'audit', navKey: 'audit', route: 'audit', icon: '⌕', name: '审计日志', permission: 'AUDIT_VIEW', keywords: ['sj', 'shenji', 'rizhi', 'audit'] },
      { id: 'processes', navKey: 'processes', route: 'processes', icon: '⌘', name: '流程定义', permission: 'PROCESS_MANAGE', keywords: ['lc', 'liucheng', 'process'] },
      { id: 'security', navKey: 'security', route: 'security', icon: '⚿', name: '登录设备', keywords: ['sb', 'shebei', 'anquan'] }
    ]
  }
]

const visibleSections = computed(() => {
  const query = searchQuery.value.trim().toLowerCase()
  return navSections.map(section => {
    const authorizedItems = section.items.filter(item => hasPermission(item.permission))
    if (!query) {
      return { ...section, items: authorizedItems }
    }
    const matchingItems = authorizedItems.filter(item => {
      const matchName = item.name.toLowerCase().includes(query)
      const matchKeywords = item.keywords?.some(k => k.toLowerCase().includes(query))
      const matchSection = section.title.toLowerCase().includes(query)
      return matchName || matchKeywords || matchSection
    })
    return { ...section, items: matchingItems }
  }).filter(section => section.items.length > 0)
})

function isSectionCollapsed(sectionId: string) {
  if (searchQuery.value.trim()) return false
  return collapsedSections.value[sectionId] === true
}

function getBadge(badgeKey?: 'pendingTaskCount' | 'pendingReadCount'): number | undefined {
  if (!badgeKey || !props.summary) return undefined
  return props.summary[badgeKey] || undefined
}

watch(() => props.activeNav, newNav => {
  for (const section of navSections) {
    if (section.items.some(item => item.navKey === newNav)) {
      if (collapsedSections.value[section.id]) {
        collapsedSections.value[section.id] = false
        try {
          localStorage.setItem(STORAGE_KEY, JSON.stringify(collapsedSections.value))
        } catch {
          // ignore
        }
      }
      break
    }
  }
  nextTick(() => {
    const activeBtn = document.querySelector('.nav-menu button.active')
    if (activeBtn) {
      activeBtn.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
    }
  })
}, { immediate: true })

function handleKeydown(e: KeyboardEvent) {
  if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    searchInputRef.value?.focus()
  } else if (e.key === 'Escape' && searchQuery.value) {
    searchQuery.value = ''
  }
}

onMounted(() => {
  window.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown)
})
</script>

<template>
  <aside class="sidebar" :class="{ open }">
    <div class="brand">
      <span class="brand-mark">OA</span>
      <div>
        <strong>{{ summary?.tenant ?? 'xxx公司' }}</strong>
        <small>协同办公平台</small>
      </div>
    </div>

    <div class="sidebar-search">
      <span class="search-icon">🔍</span>
      <input
        ref="searchInputRef"
        v-model="searchQuery"
        type="text"
        placeholder="搜索菜单 (⌘K)"
        aria-label="搜索菜单"
      />
      <button
        v-if="searchQuery"
        type="button"
        class="clear-btn"
        title="清空搜索"
        @click="clearSearch"
      >
        ✕
      </button>
    </div>

    <nav class="nav-menu" aria-label="主导航">
      <div v-for="section in visibleSections" :key="section.id" class="nav-section">
        <button
          type="button"
          class="nav-section-header"
          :aria-expanded="!isSectionCollapsed(section.id)"
          @click="toggleSection(section.id)"
        >
          <span>{{ section.title }}</span>
          <span class="nav-arrow" :class="{ collapsed: isSectionCollapsed(section.id) }">▾</span>
        </button>
        <div v-show="!isSectionCollapsed(section.id)" class="nav-section-body">
          <button
            v-for="item in section.items"
            :key="item.id"
            class="nav-item"
            :class="{ active: activeNav === item.navKey }"
            @click="go(item.route)"
          >
            <span>{{ item.icon }}</span>
            {{ item.name }}
            <b v-if="getBadge(item.badgeKey)">{{ getBadge(item.badgeKey) }}</b>
          </button>
        </div>
      </div>

      <div v-if="visibleSections.length === 0" class="nav-empty">
        未找到与"{{ searchQuery }}"相关的菜单
      </div>
    </nav>

    <div class="sidebar-user">
      <span class="avatar">{{ currentUser?.name?.slice(0, 1) ?? '张' }}</span>
      <div>
        <strong>{{ currentUser?.name ?? '演示用户' }}</strong>
        <small>{{ currentUser?.departmentName ?? '研发部' }}</small>
      </div>
    </div>
  </aside>
</template>
