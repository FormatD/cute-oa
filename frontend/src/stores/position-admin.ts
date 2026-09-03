import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { CreatePosition, ManagedPosition, UpdatePosition } from '../api/types'
import { useOrganizationStore } from './organization'

export const usePositionAdminStore = defineStore('position-admin', () => {
  const organization = useOrganizationStore()
  const api = useApiClient().organization
  const positions = ref<ManagedPosition[]>([])
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')

  async function loadPositions() {
    loading.value = true
    error.value = ''
    try { positions.value = await api.getManagedPositions() }
    catch (cause) { error.value = cause instanceof Error ? cause.message : '岗位列表加载失败。' }
    finally { loading.value = false }
  }

  function createPosition(payload: CreatePosition) { return execute(async () => { await api.createPosition(payload); message.value = '岗位已创建。' }) }
  function updatePosition(id: string, payload: UpdatePosition) { return execute(async () => { await api.updatePosition(id, payload); message.value = '岗位信息已更新。' }) }
  function deletePosition(id: string, version: number) { return execute(async () => { await api.deletePosition(id, version); message.value = '岗位已删除。' }) }

  async function execute(action: () => Promise<void>) {
    saving.value = true
    error.value = ''
    message.value = ''
    try {
      await action()
      organization.reset()
      await Promise.all([loadPositions(), organization.loadOrganization()])
      return true
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '岗位保存失败。'
      return false
    } finally { saving.value = false }
  }

  return { positions, loading, saving, error, message, loadPositions, createPosition, updatePosition, deletePosition }
})
