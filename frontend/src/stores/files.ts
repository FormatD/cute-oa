import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { FileResourceType } from '../api/modules/files'
import { useUiStore } from './ui'

export const useFileStore = defineStore('files', () => {
  const ui = useUiStore()
  const api = useApiClient().files

  async function uploadFile(file: File) {
    const uploaded = await api.upload(file)
    return uploaded.id
  }

  async function downloadAttachment(id: string, resourceType: FileResourceType, resourceId: string) {
    try {
      await api.download(id, resourceType, resourceId)
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '附件下载失败。'
    }
  }

  return { uploadFile, downloadAttachment }
})
