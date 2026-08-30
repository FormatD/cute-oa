import { ref } from 'vue'
import { defineStore } from 'pinia'
import { createHttpClient } from '../api/http'
import { createSystemApi } from '../api/modules/system'
import type { CreateProcessDefinition, ProcessDefinition, UpdateProcessDefinition } from '../api/types'
import { useAuthStore } from './auth'

export const useProcessDefinitionStore = defineStore('process-definitions', () => {
  const auth = useAuthStore()
  const api = createSystemApi(createHttpClient(() => auth.accessToken, auth.clearSession, auth.refreshAccessToken))
  const definitions = ref<ProcessDefinition[]>([])
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')

  async function load() {
    loading.value = true
    error.value = ''
    try {
      definitions.value = (await api.getProcessDefinitions()).sort((left, right) => right.createdAt.localeCompare(left.createdAt))
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '流程定义加载失败。'
    } finally {
      loading.value = false
    }
  }

  async function create(payload: CreateProcessDefinition) {
    return runSave(() => api.createProcessDefinition(payload), '流程草稿创建失败。')
  }

  async function update(id: string, payload: UpdateProcessDefinition) {
    return runSave(() => api.updateProcessDefinition(id, payload), '流程草稿保存失败。')
  }

  async function clone(id: string) {
    return runSave(() => api.cloneProcessDefinition(id), '复制流程版本失败。')
  }

  async function publish(id: string) {
    return runSave(() => api.publishProcessDefinition(id), '发布流程失败。')
  }

  async function runSave(operation: () => Promise<ProcessDefinition>, fallback: string) {
    saving.value = true
    error.value = ''
    try {
      return await operation()
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : fallback
      return null
    } finally {
      saving.value = false
    }
  }

  return { definitions, loading, saving, error, message, load, create, update, clone, publish }
})
