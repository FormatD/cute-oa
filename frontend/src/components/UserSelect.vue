<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useOrganizationStore } from '../stores/organization'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import type { DirectoryEmployee, Employee } from '../api/types'

interface UserOption {
  id: string
  name: string
  departmentName?: string
  positionName?: string | null
  role?: string
}

const props = withDefaults(defineProps<{
  modelValue?: string | null
  placeholder?: string
  disabled?: boolean
  clearable?: boolean
  compact?: boolean
  excludeUserIds?: string[]
  customCandidates?: UserOption[]
}>(), {
  modelValue: '',
  placeholder: '请选择员工',
  disabled: false,
  clearable: true,
  compact: false,
  excludeUserIds: () => [],
  customCandidates: undefined
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
  'change': [user: UserOption | null]
}>()

const organizationStore = useOrganizationStore()
const employeeDirectoryStore = useEmployeeDirectoryStore()

const isOpen = ref(false)
const searchQuery = ref('')
const containerRef = ref<HTMLElement | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)

// Ensure data is loaded
onMounted(async () => {
  if (!props.customCandidates) {
    if (!organizationStore.organizationLoaded) {
      void organizationStore.loadOrganization()
    }
    if (employeeDirectoryStore.employees.length === 0) {
      void employeeDirectoryStore.loadEmployees()
    }
  }
  document.addEventListener('click', handleOutsideClick)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', handleOutsideClick)
})

function handleOutsideClick(event: MouseEvent) {
  if (isOpen.value && containerRef.value && !containerRef.value.contains(event.target as Node)) {
    isOpen.value = false
  }
}

// All available candidate options merged and deduplicated by id
const allOptions = computed<UserOption[]>(() => {
  if (props.customCandidates && props.customCandidates.length > 0) {
    return props.customCandidates
  }

  const map = new Map<string, UserOption>()

  // 1. From organization directory employees
  for (const emp of organizationStore.directoryEmployees) {
    map.set(emp.id, {
      id: emp.id,
      name: emp.name,
      departmentName: emp.departmentName,
      positionName: emp.positionName || null,
      role: emp.role
    })
  }

  // 2. From demo employees (if any missing)
  for (const emp of employeeDirectoryStore.employees) {
    if (!map.has(emp.id)) {
      map.set(emp.id, {
        id: emp.id,
        name: emp.name,
        departmentName: emp.departmentName,
        positionName: emp.positionName || null,
        role: emp.role
      })
    }
  }

  return Array.from(map.values())
})

// Options excluding specified user IDs
const filteredCandidates = computed<UserOption[]>(() => {
  const excludes = new Set(props.excludeUserIds || [])
  let list = allOptions.value.filter(item => !excludes.has(item.id))

  const query = searchQuery.value.trim().toLowerCase()
  if (query) {
    list = list.filter(item => {
      const matchName = item.name.toLowerCase().includes(query)
      const matchId = item.id.toLowerCase().includes(query)
      const matchDept = (item.departmentName || '').toLowerCase().includes(query)
      const matchRole = (item.positionName || item.role || '').toLowerCase().includes(query)
      return matchName || matchId || matchDept || matchRole
    })
  }
  return list
})

// Current selected user object
const selectedUser = computed<UserOption | null>(() => {
  const currentId = props.modelValue?.trim()
  if (!currentId) return null
  const found = allOptions.value.find(u => u.id === currentId)
  if (found) return found

  // If bound ID is not in options, fallback to synthetic option
  return {
    id: currentId,
    name: currentId,
    departmentName: '',
    positionName: '已配置用户',
    role: ''
  }
})

function toggleDropdown() {
  if (props.disabled) return
  isOpen.value = !isOpen.value
  if (isOpen.value) {
    searchQuery.value = ''
    nextTick(() => {
      searchInputRef.value?.focus()
    })
  }
}

function selectUser(user: UserOption) {
  emit('update:modelValue', user.id)
  emit('change', user)
  isOpen.value = false
}

function handleClear(event: MouseEvent) {
  event.stopPropagation()
  emit('update:modelValue', '')
  emit('change', null)
  isOpen.value = false
}

function handleKeyDown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    isOpen.value = false
  }
}

function getAvatarText(name: string): string {
  if (!name) return '?'
  return name.slice(0, 1).toUpperCase()
}
</script>

<template>
  <div
    ref="containerRef"
    class="user-select-container"
    :class="{ 'is-open': isOpen, 'is-disabled': disabled, 'is-compact': compact }"
    @keydown="handleKeyDown"
  >
    <!-- Display Trigger -->
    <div
      class="user-select-trigger"
      :class="{ 'has-value': Boolean(selectedUser), 'is-active': isOpen }"
      tabindex="0"
      @click="toggleDropdown"
      @keydown.enter.prevent="toggleDropdown"
      @keydown.space.prevent="toggleDropdown"
    >
      <div v-if="selectedUser" class="selected-user-display">
        <span class="user-avatar-dot">{{ getAvatarText(selectedUser.name) }}</span>
        <span class="user-name-text">{{ selectedUser.name }}</span>
        <span v-if="selectedUser.departmentName && !compact" class="user-dept-tag">
          {{ selectedUser.departmentName }}
        </span>
        <span v-if="!compact" class="user-id-code"><code>{{ selectedUser.id }}</code></span>
      </div>
      <span v-else class="placeholder-text">{{ placeholder }}</span>

      <div class="trigger-icons">
        <button
          v-if="clearable && selectedUser && !disabled"
          type="button"
          class="clear-action-btn"
          title="清空选择"
          @click="handleClear"
        >
          ×
        </button>
        <span class="caret-icon" :class="{ 'is-flipped': isOpen }">▾</span>
      </div>
    </div>

    <!-- Dropdown Popover -->
    <div v-if="isOpen" class="user-select-dropdown">
      <!-- Search Input -->
      <div class="dropdown-search-wrap">
        <input
          ref="searchInputRef"
          v-model="searchQuery"
          type="text"
          class="dropdown-search-input"
          placeholder="搜索姓名、工号、部门..."
          @click.stop
        />
        <span v-if="searchQuery" class="search-clear" @click="searchQuery = ''">×</span>
      </div>

      <!-- User List -->
      <ul class="dropdown-user-list">
        <li
          v-for="user in filteredCandidates"
          :key="user.id"
          class="user-list-item"
          :class="{ 'is-selected': user.id === modelValue }"
          @click="selectUser(user)"
        >
          <div class="user-item-avatar">
            {{ getAvatarText(user.name) }}
          </div>
          <div class="user-item-info">
            <div class="user-primary-row">
              <strong class="user-item-name">{{ user.name }}</strong>
              <code class="user-item-id">{{ user.id }}</code>
            </div>
            <div class="user-secondary-row">
              <span v-if="user.departmentName" class="dept-badge">{{ user.departmentName }}</span>
              <span v-if="user.positionName || user.role" class="position-badge">
                {{ user.positionName || user.role }}
              </span>
            </div>
          </div>
          <span v-if="user.id === modelValue" class="selected-checkmark">✓</span>
        </li>

        <li v-if="filteredCandidates.length === 0" class="empty-results-tip">
          <span>未找到匹配员工</span>
        </li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
.user-select-container {
  position: relative;
  display: inline-block;
  width: 100%;
  box-sizing: border-box;
  font-family: inherit;
}

.user-select-trigger {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  min-height: 36px;
  padding: 4px 10px;
  box-sizing: border-box;
  background: #fff;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s ease;
  user-select: none;
}

.user-select-trigger:hover:not(.is-disabled) {
  border-color: #94a3b8;
}

.user-select-trigger.is-active {
  border-color: #3478e8;
  box-shadow: 0 0 0 2px rgba(52, 120, 232, 0.15);
}

.is-disabled .user-select-trigger {
  background: #f8fafc;
  border-color: #e2e8f0;
  cursor: not-allowed;
  opacity: 0.75;
}

/* Compact mode for table cells */
.is-compact .user-select-trigger {
  min-height: 28px;
  padding: 2px 6px;
  font-size: 0.78rem;
  border-radius: 4px;
}

.selected-user-display {
  display: flex;
  align-items: center;
  gap: 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.user-avatar-dot {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: #3478e8;
  color: #fff;
  font-size: 0.72rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.is-compact .user-avatar-dot {
  width: 18px;
  height: 18px;
  font-size: 0.65rem;
}

.user-name-text {
  font-size: 0.84rem;
  font-weight: 600;
  color: #1e293b;
}

.is-compact .user-name-text {
  font-size: 0.76rem;
}

.user-dept-tag {
  display: inline-block;
  padding: 1px 6px;
  border-radius: 4px;
  background: #f1f5f9;
  color: #475569;
  font-size: 0.72rem;
}

.user-id-code {
  font-size: 0.72rem;
  color: #94a3b8;
}

.placeholder-text {
  color: #94a3b8;
  font-size: 0.82rem;
}

.is-compact .placeholder-text {
  font-size: 0.76rem;
}

.trigger-icons {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-left: 6px;
  flex-shrink: 0;
}

.clear-action-btn {
  border: 0;
  background: transparent;
  color: #94a3b8;
  font-size: 1rem;
  line-height: 1;
  padding: 0 2px;
  cursor: pointer;
  border-radius: 50%;
}

.clear-action-btn:hover {
  color: #ef4444;
  background: #fee2e2;
}

.caret-icon {
  color: #64748b;
  font-size: 0.7rem;
  transition: transform 0.15s ease;
}

.caret-icon.is-flipped {
  transform: rotate(180deg);
}

/* Dropdown */
.user-select-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  width: 100%;
  min-width: 260px;
  max-width: 380px;
  max-height: 280px;
  background: #fff;
  border: 1px solid #cbd5e1;
  border-radius: 8px;
  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.1), 0 8px 10px -6px rgba(0, 0, 0, 0.1);
  z-index: 1000;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.dropdown-search-wrap {
  position: relative;
  padding: 8px;
  border-bottom: 1px solid #f1f5f9;
  background: #fafafa;
}

.dropdown-search-input {
  width: 100%;
  box-sizing: border-box;
  padding: 6px 24px 6px 8px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 0.8rem;
  outline: none;
}

.dropdown-search-input:focus {
  border-color: #3478e8;
}

.search-clear {
  position: absolute;
  right: 14px;
  top: 50%;
  transform: translateY(-50%);
  color: #94a3b8;
  cursor: pointer;
  font-size: 0.9rem;
}

.dropdown-user-list {
  list-style: none;
  margin: 0;
  padding: 4px 0;
  overflow-y: auto;
  max-height: 220px;
}

.user-list-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  cursor: pointer;
  transition: background 0.12s ease;
}

.user-list-item:hover {
  background: #f8fafc;
}

.user-list-item.is-selected {
  background: #eff6ff;
}

.user-item-avatar {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: #e0e7ff;
  color: #3730a3;
  font-size: 0.78rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.user-item-info {
  flex: 1;
  min-width: 0;
}

.user-primary-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.user-item-name {
  font-size: 0.84rem;
  color: #1e293b;
}

.user-item-id {
  font-size: 0.7rem;
  color: #94a3b8;
}

.user-secondary-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 2px;
}

.dept-badge {
  display: inline-block;
  padding: 1px 5px;
  border-radius: 3px;
  background: #f1f5f9;
  color: #475569;
  font-size: 0.68rem;
}

.position-badge {
  display: inline-block;
  padding: 1px 5px;
  border-radius: 3px;
  background: #eff6ff;
  color: #1d4ed8;
  font-size: 0.68rem;
}

.selected-checkmark {
  color: #2563eb;
  font-weight: 700;
  font-size: 0.9rem;
}

.empty-results-tip {
  padding: 16px;
  text-align: center;
  color: #94a3b8;
  font-size: 0.78rem;
}
</style>
