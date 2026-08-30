import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { createHttpClient } from '../api/http'
import { createLeaveApi } from '../api/modules/leave'
import type { LeaveForm, LeaveRequest, ListFilters } from '../api/types'
import { PAGE_SIZE } from './pagination'
import { useAuthStore } from './auth'
import { useUiStore } from './ui'

export const blankLeaveForm = (): LeaveForm => ({ type: 'Annual', startDate: '', startPeriod: 'FullDay', endDate: '', endPeriod: 'FullDay', reason: '', attachments: [], copyRecipientIds: [] })
export const blankLeaveFilters = (): ListFilters => ({ keyword: '', status: '', applicantId: '', startDate: '', endDate: '' })

export const useLeaveStore = defineStore('leave', () => {
  const auth = useAuthStore()
  const ui = useUiStore()
  const api = createLeaveApi(createHttpClient(() => auth.accessToken, auth.clearSession, auth.refreshAccessToken))
  const leaves = ref<LeaveRequest[]>([])
  const initiatedLeaves = ref<LeaveRequest[]>([])
  const form = ref<LeaveForm>(blankLeaveForm())
  const showForm = ref(false)
  const leavePage = ref(1)
  const leaveTotal = ref(0)
  const leaveTotalPages = ref(1)
  const leaveFilters = ref<ListFilters>(blankLeaveFilters())
  const leaveDetail = ref<LeaveRequest | null>(null)
  const pagedLeaves = computed(() => ({ items: leaves.value, currentPage: leavePage.value, totalPages: leaveTotalPages.value, total: leaveTotal.value }))

  async function loadLeaves() {
    try {
      const result = await api.getLeaves(leaveFilters.value, leavePage.value, PAGE_SIZE)
      leaves.value = result.items
      leavePage.value = result.page
      leaveTotal.value = result.total
      leaveTotalPages.value = result.totalPages
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '请假列表加载失败。'
    }
  }

  async function loadInitiated(userId: string) {
    initiatedLeaves.value = (await api.getLeaves({ ...blankLeaveFilters(), applicantId: userId }, 1, 5)).items
  }

  async function loadLeaveDetail(id: string) {
    leaveDetail.value = await api.getLeave(id)
  }

  async function submitLeave() {
    if (!form.value.startDate || !form.value.endDate || !form.value.reason.trim()) {
      ui.error = '请填写开始日期、结束日期和请假事由。'
      return false
    }
    ui.submitting = true
    ui.error = ''
    try {
      const draft = await api.createLeave(form.value)
      await api.submitLeave(draft.id)
      ui.message = '请假申请已提交，审批人已收到待办。'
      showForm.value = false
      form.value = blankLeaveForm()
      leavePage.value = 1
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '提交失败。'
      return false
    } finally {
      ui.submitting = false
    }
  }

  async function withdrawLeave(leave: LeaveRequest) {
    if (!window.confirm(`确认撤回请假单 ${leave.number} 吗？`)) return false
    try {
      await api.withdrawLeave(leave.id)
      ui.message = '请假申请已撤回。'
      return true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '撤回请假申请失败。'
      return false
    }
  }

  function reset() {
    leaves.value = []
    initiatedLeaves.value = []
    leaveDetail.value = null
  }

  return { leaves, initiatedLeaves, form, showForm, leavePage, leaveTotal, leaveTotalPages, leaveFilters, leaveDetail, pagedLeaves, loadLeaves, loadInitiated, loadLeaveDetail, submitLeave, withdrawLeave, reset }
})
