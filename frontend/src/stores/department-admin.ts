import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { CreateDepartment, ManagedDepartment, UpdateDepartment } from '../api/types'
import { useOrganizationStore } from './organization'

export const useDepartmentAdminStore = defineStore('department-admin', () => {
  const organization = useOrganizationStore()
  const api = useApiClient().organization
  const departments = ref<ManagedDepartment[]>([])
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')

  async function loadDepartments() {
    loading.value = true
    error.value = ''
    try { departments.value = await api.getManagedDepartments() }
    catch (cause) { error.value = cause instanceof Error ? cause.message : '组织架构加载失败。' }
    finally { loading.value = false }
  }

  async function createDepartment(payload: CreateDepartment) {
    return execute(async () => { await api.createDepartment(payload); message.value = '部门已创建。' })
  }

  async function updateDepartment(id: string, payload: UpdateDepartment) {
    return execute(async () => { await api.updateDepartment(id, payload); message.value = '部门信息已更新。' })
  }

  async function deleteDepartment(id: string, version: number) {
    return execute(async () => { await api.deleteDepartment(id, version); message.value = '部门已删除。' })
  }

  async function execute(action: () => Promise<void>) {
    saving.value = true
    error.value = ''
    message.value = ''
    try {
      await action()
      organization.reset()
      await Promise.all([loadDepartments(), organization.loadOrganization()])
      return true
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '组织架构保存失败。'
      return false
    } finally { saving.value = false }
  }

  return { departments, loading, saving, error, message, loadDepartments, createDepartment, updateDepartment, deleteDepartment }
})
