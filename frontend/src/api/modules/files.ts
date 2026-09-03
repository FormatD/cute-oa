import type { HttpClient } from '../http'
import type { FileDescriptor } from '../types'

export type FileResourceType = 'leave' | 'expense' | 'travel' | 'purchase' | 'seal' | 'attendance' | 'contract'

export const createFileApi = ({ uploadFile, downloadFile }: HttpClient) => ({
  upload: (file: File): Promise<FileDescriptor> => uploadFile(file),
  download: (id: string, resourceType: FileResourceType, resourceId: string): Promise<void> =>
    downloadFile(id, resourceType, resourceId)
})
