import { defineStore } from 'pinia'
import { createHttpClient } from '../api/http'
import { useAuthStore } from './auth'
import { useUiStore } from './ui'

export type FileResourceType = 'leave' | 'expense' | 'travel' | 'attendance' | 'contract'

export const useFileStore = defineStore('files', () => {
  const auth = useAuthStore()
  const ui = useUiStore()
  const client = createHttpClient(() => auth.accessToken, auth.clearSession, auth.refreshAccessToken)

  async function uploadFile(file: File) {
    const uploaded = await client.uploadFile(file)
    return uploaded.id
  }

  async function downloadAttachment(id: string, resourceType: FileResourceType, resourceId: string) {
    try {
      await client.downloadFile(id, resourceType, resourceId)
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '附件下载失败。'
    }
  }

  return { uploadFile, downloadAttachment }
})
