import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { CreateManagedUser, CreateSecurityRole, ManagedUser, PermissionDefinition, SecurityRole, UpdateManagedUser, UpdateSecurityRole } from '../api/types'
import { PAGE_SIZE } from './pagination'

export const useIdentityStore = defineStore('identity', () => {
  const api = useApiClient().identity
  const users = ref<ManagedUser[]>([])
  const roles = ref<SecurityRole[]>([])
  const permissions = ref<PermissionDefinition[]>([])
  const filters = ref({ keyword: '', status: '', departmentId: '' })
  const page = ref(1)
  const total = ref(0)
  const totalPages = ref(1)
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')
  const pagedUsers = computed(() => ({ items: users.value, currentPage: page.value, totalPages: totalPages.value, total: total.value }))

  async function loadRoles() {
    roles.value = await api.getRoles()
  }

  async function loadPermissions() {
    permissions.value = await api.getPermissions()
  }

  async function loadUsers() {
    loading.value = true
    error.value = ''
    try {
      const result = await api.getUsers(filters.value, page.value, PAGE_SIZE)
      users.value = result.items
      page.value = result.page
      total.value = result.total
      totalPages.value = result.totalPages
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '用户列表加载失败。'
    } finally {
      loading.value = false
    }
  }

  async function initialize() {
    try { await Promise.all([loadRoles(), loadPermissions(), loadUsers()]) }
    catch (cause) { error.value = cause instanceof Error ? cause.message : '身份权限数据加载失败。' }
  }

  async function createUser(payload: CreateManagedUser) {
    return save(async () => { await api.createUser(payload); message.value = '用户已创建。'; page.value = 1 })
  }

  async function updateUser(id: string, payload: UpdateManagedUser) {
    return save(async () => { await api.updateUser(id, payload); message.value = '用户资料与角色已更新。' })
  }

  async function resetPassword(id: string, password: string) {
    return save(async () => { await api.resetPassword(id, password); message.value = '密码已重置，账号锁定状态已清除。' })
  }

  async function resetMfa(id: string) {
    return save(async () => { await api.resetMfa(id); message.value = '多因素认证已重置，相关登录会话已撤销。' })
  }

  async function createRole(payload: CreateSecurityRole) {
    return saveRole(async () => { await api.createRole(payload); message.value = '角色已创建。' })
  }

  async function updateRole(payload: UpdateSecurityRole) {
    return saveRole(async () => { await api.updateRole(payload); message.value = '角色名称与权限已更新，受影响用户需要重新登录。' })
  }

  async function deleteRole(code: string) {
    return saveRole(async () => { await api.deleteRole(code); message.value = '自定义角色已删除。' })
  }

  async function saveRole(action: () => Promise<void>) {
    saving.value = true
    error.value = ''
    message.value = ''
    try {
      await action()
      await loadRoles()
      return true
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '角色保存失败。'
      return false
    } finally {
      saving.value = false
    }
  }

  async function save(action: () => Promise<void>) {
    saving.value = true
    error.value = ''
    message.value = ''
    try {
      await action()
      await loadUsers()
      return true
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '保存失败。'
      return false
    } finally {
      saving.value = false
    }
  }

  function search() { page.value = 1; void loadUsers() }
  function resetFilters() { filters.value = { keyword: '', status: '', departmentId: '' }; search() }

  return { users, roles, permissions, filters, page, total, totalPages, loading, saving, error, message, pagedUsers, initialize, loadRoles, loadPermissions, loadUsers, createUser, updateUser, resetPassword, resetMfa, createRole, updateRole, deleteRole, search, resetFilters }
})
