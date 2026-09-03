import { computed, reactive, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { FlowDelegation } from '../api/types'
import { paginate } from './pagination'

function localDateTime(date: Date) {
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

const blankForm = () => ({
  delegateId: '',
  businessType: 'All',
  startAt: localDateTime(new Date(Date.now() + 5 * 60_000)),
  endAt: localDateTime(new Date(Date.now() + 24 * 60 * 60_000)),
  reason: ''
})

export const useDelegationStore = defineStore('delegation', () => {
  const api = useApiClient().workflow
  const items = ref<FlowDelegation[]>([])
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')
  const page = ref(1)
  const form = reactive(blankForm())
  const paged = computed(() => paginate(items.value, page.value))

  async function load() {
    loading.value = true
    error.value = ''
    try {
      items.value = await api.getDelegations()
      if (page.value > paged.value.totalPages) page.value = paged.value.totalPages
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '审批委托加载失败。'
    } finally {
      loading.value = false
    }
  }

  async function submit() {
    if (!form.delegateId || !form.reason.trim() || !form.startAt || !form.endAt) {
      error.value = '请完整填写代办人、起止时间和委托原因。'
      return
    }
    saving.value = true
    error.value = ''
    message.value = ''
    try {
      await api.createDelegation({ delegateId: form.delegateId, businessType: form.businessType, startAt: new Date(form.startAt).toISOString(), endAt: new Date(form.endAt).toISOString(), reason: form.reason })
      message.value = '审批委托已创建，将在有效期内应用于新生成的待办。'
      form.reason = ''
      page.value = 1
      await load()
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '审批委托创建失败。'
    } finally {
      saving.value = false
    }
  }

  async function cancel(item: FlowDelegation) {
    if (!window.confirm(`确认取消委托给 ${item.delegateName} 的规则吗？已生成的待办不会回收。`)) return
    saving.value = true
    error.value = ''
    try {
      await api.cancelDelegation(item.id)
      message.value = '审批委托已取消。'
      await load()
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '取消审批委托失败。'
    } finally {
      saving.value = false
    }
  }

  return { items, loading, saving, error, message, page, form, paged, load, submit, cancel }
})
