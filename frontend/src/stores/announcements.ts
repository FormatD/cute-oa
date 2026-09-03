import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { Announcement, SaveAnnouncement } from '../api/types'
import { PAGE_SIZE } from './pagination'

export const useAnnouncementStore = defineStore('announcements', () => {
  const api = useApiClient().announcements
  const published = ref<Announcement[]>([])
  const publishedPage = ref(1)
  const publishedTotal = ref(0)
  const publishedTotalPages = ref(1)
  const adminItems = ref<Announcement[]>([])
  const adminPage = ref(1)
  const adminTotal = ref(0)
  const adminTotalPages = ref(1)
  const adminStatus = ref('')
  const detail = ref<Announcement | null>(null)
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const message = ref('')

  async function loadPublished(page = publishedPage.value, pageSize = PAGE_SIZE) {
    loading.value = true
    error.value = ''
    try {
      const result = await api.getPublished(page, pageSize)
      published.value = result.items
      publishedPage.value = result.page
      publishedTotal.value = result.total
      publishedTotalPages.value = result.totalPages
    } catch (cause) { error.value = cause instanceof Error ? cause.message : '公告列表加载失败。' }
    finally { loading.value = false }
  }

  async function loadDetail(id: string) {
    loading.value = true
    error.value = ''
    message.value = ''
    try { detail.value = await api.getOne(id) }
    catch (cause) { detail.value = null; error.value = cause instanceof Error ? cause.message : '公告详情加载失败。' }
    finally { loading.value = false }
  }

  async function markRead(id: string) {
    return execute(async () => { detail.value = await api.markRead(id); message.value = '已确认阅读该公告。'; await loadPublished() }, false)
  }

  async function loadAdmin(page = adminPage.value) {
    loading.value = true
    error.value = ''
    try {
      const result = await api.getAdmin(adminStatus.value, page, PAGE_SIZE)
      adminItems.value = result.items
      adminPage.value = result.page
      adminTotal.value = result.total
      adminTotalPages.value = result.totalPages
    } catch (cause) { error.value = cause instanceof Error ? cause.message : '公告管理列表加载失败。' }
    finally { loading.value = false }
  }

  function createAnnouncement(payload: SaveAnnouncement) { return execute(async () => { await api.create(payload); message.value = '公告草稿已创建。'; adminPage.value = 1 }, true) }
  function updateAnnouncement(id: string, payload: SaveAnnouncement) { return execute(async () => { await api.update(id, payload); message.value = '公告草稿已更新。' }, true) }
  function publishAnnouncement(item: Announcement) { return execute(async () => { await api.publish(item.id, item.version); message.value = '公告已发布。' }, true) }
  function withdrawAnnouncement(item: Announcement) { return execute(async () => { await api.withdraw(item.id, item.version); message.value = '公告已撤回。' }, true) }

  async function execute(action: () => Promise<void>, refreshAdmin: boolean) {
    saving.value = true
    error.value = ''
    message.value = ''
    try {
      await action()
      if (refreshAdmin) await Promise.all([loadAdmin(), loadPublished()])
      return true
    } catch (cause) { error.value = cause instanceof Error ? cause.message : '公告操作失败。'; return false }
    finally { saving.value = false }
  }

  function searchAdmin() { adminPage.value = 1; void loadAdmin(1) }
  function reset() { published.value = []; adminItems.value = []; detail.value = null; publishedPage.value = 1; adminPage.value = 1 }

  return { published, publishedPage, publishedTotal, publishedTotalPages, adminItems, adminPage, adminTotal, adminTotalPages, adminStatus, detail, loading, saving, error, message, loadPublished, loadDetail, markRead, loadAdmin, createAnnouncement, updateAnnouncement, publishAnnouncement, withdrawAnnouncement, searchAdmin, reset }
})
