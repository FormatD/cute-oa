import type { HttpClient } from '../http'
import type {
  DocumentCategory,
  SaveDocumentCategory,
  KnowledgeDocument,
  SaveDocument,
  ReviseDocument,
  DocumentVersion,
  DocumentAcknowledgement,
  DocumentAcknowledgementStats,
  PagedResponse
} from '../types'

export type DocumentListParams = {
  keyword?: string
  categoryId?: string
  tag?: string
  status?: number
  mustRead?: boolean
  pendingAck?: boolean
  page?: number
  pageSize?: number
}

export const createDocumentApi = ({ request }: HttpClient) => ({
  listCategories: (): Promise<DocumentCategory[]> => request('/documents/categories'),
  createCategory: (payload: SaveDocumentCategory): Promise<DocumentCategory> =>
    request('/documents/categories', { method: 'POST', body: JSON.stringify(payload) }),
  updateCategory: (id: string, payload: SaveDocumentCategory): Promise<DocumentCategory> =>
    request(`/documents/categories/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteCategory: (id: string): Promise<boolean> =>
    request(`/documents/categories/${id}`, { method: 'DELETE' }),

  listDocuments: (params: DocumentListParams = {}): Promise<PagedResponse<KnowledgeDocument>> => {
    const query = new URLSearchParams()
    if (params.keyword) query.set('keyword', params.keyword)
    if (params.categoryId) query.set('categoryId', params.categoryId)
    if (params.tag) query.set('tag', params.tag)
    if (params.status !== undefined && params.status !== null) query.set('status', String(params.status))
    if (params.mustRead !== undefined) query.set('mustRead', String(params.mustRead))
    if (params.pendingAck !== undefined) query.set('pendingAck', String(params.pendingAck))
    if (params.page) query.set('page', String(params.page))
    if (params.pageSize) query.set('pageSize', String(params.pageSize))
    const qs = query.toString()
    return request(`/documents${qs ? `?${qs}` : ''}`)
  },

  getDocument: (id: string): Promise<KnowledgeDocument> => request(`/documents/${id}`),

  createDraft: (payload: SaveDocument): Promise<KnowledgeDocument> =>
    request('/documents', { method: 'POST', body: JSON.stringify(payload) }),

  updateDraft: (id: string, payload: SaveDocument): Promise<KnowledgeDocument> =>
    request(`/documents/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),

  deleteDraft: (id: string): Promise<boolean> =>
    request(`/documents/${id}`, { method: 'DELETE' }),

  publishDocument: (id: string): Promise<KnowledgeDocument> =>
    request(`/documents/${id}/publish`, { method: 'POST' }),

  reviseDocument: (id: string, payload: ReviseDocument): Promise<KnowledgeDocument> =>
    request(`/documents/${id}/revise`, { method: 'POST', body: JSON.stringify(payload) }),

  archiveDocument: (id: string): Promise<boolean> =>
    request(`/documents/${id}/archive`, { method: 'POST' }),

  acknowledgeDocument: (id: string): Promise<DocumentAcknowledgement> =>
    request(`/documents/${id}/acknowledge`, { method: 'POST' }),

  getAcknowledgementStats: (id: string): Promise<DocumentAcknowledgementStats> =>
    request(`/documents/${id}/acknowledgement-stats`),

  listVersions: (id: string): Promise<DocumentVersion[]> =>
    request(`/documents/${id}/versions`),

  generateDemoData: (): Promise<{ created: number; skipped: number }> =>
    request('/documents/demo-data', { method: 'POST' })
})
