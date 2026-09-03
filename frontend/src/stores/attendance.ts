import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { AttendanceFilters } from '../api/modules/attendance'
import type { AttendanceImportItem, AttendanceMonthLock, AttendanceMonthlySummary, AttendanceRecord, AttendanceShift, SaveAttendanceShift } from '../api/types'
import { PAGE_SIZE } from './pagination'

const currentMonth = () => `${new Date().toISOString().slice(0, 7)}-01`
const blankFilters = (): AttendanceFilters => ({ keyword: '', userId: '', departmentId: '', status: '', month: currentMonth() })

export const useAttendanceStore = defineStore('attendance', () => {
  const api = useApiClient().attendance
  const records = ref<AttendanceRecord[]>([])
  const detail = ref<AttendanceRecord | null>(null)
  const shifts = ref<AttendanceShift[]>([])
  const summaries = ref<AttendanceMonthlySummary[]>([])
  const monthLock = ref<AttendanceMonthLock | null>(null)
  const filters = ref<AttendanceFilters>(blankFilters())
  const page = ref(1)
  const total = ref(0)
  const totalPages = ref(1)
  const loading = ref(false)
  const saving = ref(false)
  const downloading = ref(false)
  const error = ref('')
  const message = ref('')

  async function loadRecords() {
    loading.value = true; error.value = ''
    try {
      const [result, monthly, shiftList, lock] = await Promise.all([api.getRecords(filters.value, page.value, PAGE_SIZE), api.getMonthlySummary(filters.value.month, filters.value.userId), api.getShifts(), api.getMonthLock(filters.value.month)])
      records.value = result.items; page.value = result.page; total.value = result.total; totalPages.value = result.totalPages; summaries.value = monthly; shifts.value = shiftList; monthLock.value = lock
    } catch (cause) { records.value = []; summaries.value = []; monthLock.value = null; error.value = cause instanceof Error ? cause.message : '考勤数据加载失败。' }
    finally { loading.value = false }
  }

  async function loadDetail(id: string) {
    loading.value = true; error.value = ''; detail.value = null
    try { detail.value = await api.getRecord(id); monthLock.value = await api.getMonthLock(`${detail.value.workDate.slice(0, 7)}-01`) }
    catch (cause) { error.value = cause instanceof Error ? cause.message : '考勤详情加载失败。' }
    finally { loading.value = false }
  }

  async function generateDemoData() {
    return mutate(async () => {
      const result = await api.generateDemoData(filters.value.month)
      message.value = `模拟考勤已补齐：新增 ${result.created} 条，保留已有 ${result.skipped} 条。`
      await loadRecords()
    }, '模拟考勤生成失败。')
  }

  async function importRecord(item: AttendanceImportItem) {
    return mutate(async () => {
      const result = await api.importRecords([item])
      message.value = `考勤已保存：新增 ${result.created} 条，更新 ${result.updated} 条。`
      filters.value.month = `${item.workDate.slice(0, 7)}-01`; page.value = 1
      await loadRecords()
    }, '考勤记录保存失败。')
  }

  async function updateShift(id: string, payload: SaveAttendanceShift) {
    return mutate(async () => { await api.updateShift(id, payload); message.value = '班次规则已更新。'; await loadRecords() }, '班次保存失败。')
  }

  async function changeMonthLock(locked: boolean, reason: string) {
    return mutate(async () => {
      const version = monthLock.value?.version ?? 0
      monthLock.value = locked
        ? await api.lockMonth(filters.value.month, reason, version)
        : await api.unlockMonth(filters.value.month, reason, version)
      message.value = locked ? `${filters.value.month.slice(0, 7)} 已封账，历史考勤已锁定。` : `${filters.value.month.slice(0, 7)} 已解封，可以继续处理考勤。`
      await loadRecords()
    }, locked ? '考勤封账失败。' : '考勤解封失败。')
  }

  async function downloadMonthSnapshot() {
    downloading.value = true; error.value = ''; message.value = ''
    try {
      await api.downloadMonthSnapshot(filters.value.month)
      message.value = `${filters.value.month.slice(0, 7)} 封账月报已下载，操作已记录审计。`
      return true
    } catch (cause) {
      error.value = cause instanceof Error ? cause.message : '封账月报下载失败。'
      return false
    } finally { downloading.value = false }
  }

  async function submitAppeal(recordId: string, reason: string, attachments: string[]) {
    return mutate(async () => { await api.submitAppeal(recordId, reason, attachments); message.value = '考勤申诉已提交。'; await loadDetail(recordId) }, '考勤申诉提交失败。')
  }

  async function reviewAppeal(recordId: string, appealId: string, approved: boolean, comment: string) {
    return mutate(async () => { await api.reviewAppeal(appealId, approved, comment); message.value = approved ? '申诉已通过，考勤记录已修正。' : '申诉已驳回。'; await loadDetail(recordId) }, '考勤申诉审核失败。')
  }

  async function mutate(action: () => Promise<void>, fallback: string) {
    saving.value = true; error.value = ''; message.value = ''
    try { await action(); return true }
    catch (cause) { error.value = cause instanceof Error ? cause.message : fallback; return false }
    finally { saving.value = false }
  }

  function setMonth(value: string) { filters.value.month = `${value}-01`; page.value = 1; void loadRecords() }
  function search() { page.value = 1; void loadRecords() }
  function resetFilters() { filters.value = blankFilters(); search() }
  function changePage(offset: number) { page.value += offset; void loadRecords() }
  function reset() { records.value = []; detail.value = null; shifts.value = []; summaries.value = []; monthLock.value = null; filters.value = blankFilters(); page.value = 1; total.value = 0; totalPages.value = 1; error.value = ''; message.value = '' }

  return { records, detail, shifts, summaries, monthLock, filters, page, total, totalPages, loading, saving, downloading, error, message, loadRecords, loadDetail, generateDemoData, importRecord, updateShift, changeMonthLock, downloadMonthSnapshot, submitAppeal, reviewAppeal, setMonth, search, resetFilters, changePage, reset }
})
