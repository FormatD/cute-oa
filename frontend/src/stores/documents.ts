import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type {
  DocumentCategory,
  SaveDocumentCategory,
  KnowledgeDocument,
  SaveDocument,
  ReviseDocument,
  DocumentVersion,
  DocumentAcknowledgementStats,
  RollbackDocument,
  DocumentDiffView,
  MoveDocumentCategory
} from '../api/types'
import { useUiStore } from './ui'

export const useDocumentStore = defineStore('documents', () => {
  const api = useApiClient()
  const ui = useUiStore()

  const categories = ref<DocumentCategory[]>([])
  const documents = ref<KnowledgeDocument[]>([])
  const total = ref(0)
  const page = ref(1)
  const pageSize = ref(12)
  const loading = ref(false)

  const currentDocument = ref<KnowledgeDocument | null>(null)
  const currentVersions = ref<DocumentVersion[]>([])
  const currentStats = ref<DocumentAcknowledgementStats | null>(null)

  // Filters
  const keyword = ref('')
  const selectedCategoryId = ref('')
  const selectedTag = ref('')
  const selectedStatus = ref<number | null>(null)
  const filterMustRead = ref<boolean | undefined>(undefined)
  const filterPendingAck = ref<boolean | undefined>(undefined)

  const pendingMustReadDocuments = computed(() =>
    documents.value.filter(doc => doc.status === 'Published' && doc.isMustRead && !doc.hasAcknowledged)
  )

  const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

  async function loadCategories() {
    try {
      categories.value = await api.documents.listCategories()
    } catch (err: any) {
      ui.showToast(err.message || '加载文档分类失败', 'error')
    }
  }

  async function loadDocuments(targetPage?: number) {
    if (targetPage) page.value = targetPage
    loading.value = true
    try {
      const res = await api.documents.listDocuments({
        keyword: keyword.value.trim() || undefined,
        categoryId: selectedCategoryId.value || undefined,
        tag: selectedTag.value || undefined,
        status: selectedStatus.value !== null ? selectedStatus.value : undefined,
        mustRead: filterMustRead.value,
        pendingAck: filterPendingAck.value,
        page: page.value,
        pageSize: pageSize.value
      })
      documents.value = res.items
      total.value = res.total
    } catch (err: any) {
      ui.showToast(err.message || '加载文档列表失败', 'error')
    } finally {
      loading.value = false
    }
  }

  async function loadDocumentDetail(id: string) {
    loading.value = true
    try {
      const [doc, versions] = await Promise.all([
        api.documents.getDocument(id),
        api.documents.listVersions(id).catch(() => [])
      ])
      currentDocument.value = doc
      currentVersions.value = versions

      // Update in list if present
      const idx = documents.value.findIndex(d => d.id === id)
      if (idx >= 0) documents.value[idx] = doc

      return doc
    } catch (err: any) {
      ui.showToast(err.message || '加载文档详情失败', 'error')
      return null
    } finally {
      loading.value = false
    }
  }

  async function loadStats(id: string) {
    try {
      currentStats.value = await api.documents.getAcknowledgementStats(id)
    } catch {
      currentStats.value = null
    }
  }

  async function saveCategory(payload: SaveDocumentCategory, id?: string) {
    try {
      if (id) {
        await api.documents.updateCategory(id, payload)
        ui.showToast('分类修改成功', 'success')
      } else {
        await api.documents.createCategory(payload)
        ui.showToast('分类创建成功', 'success')
      }
      await loadCategories()
      return true
    } catch (err: any) {
      ui.showToast(err.message || '保存分类失败', 'error')
      return false
    }
  }

  async function deleteCategory(id: string) {
    try {
      await api.documents.deleteCategory(id)
      ui.showToast('分类删除成功', 'success')
      await loadCategories()
      return true
    } catch (err: any) {
      ui.showToast(err.message || '删除分类失败', 'error')
      return false
    }
  }

  async function createDraft(payload: SaveDocument) {
    try {
      const doc = await api.documents.createDraft(payload)
      ui.showToast('草稿保存成功', 'success')
      await loadDocuments()
      return doc
    } catch (err: any) {
      ui.showToast(err.message || '保存草稿失败', 'error')
      return null
    }
  }

  async function updateDraft(id: string, payload: SaveDocument) {
    try {
      const doc = await api.documents.updateDraft(id, payload)
      ui.showToast('草稿更新成功', 'success')
      await loadDocumentDetail(id)
      return doc
    } catch (err: any) {
      ui.showToast(err.message || '更新草稿失败', 'error')
      return null
    }
  }

  async function deleteDocument(id: string) {
    try {
      await api.documents.deleteDocument(id)
      ui.showToast('文档删除成功', 'success')
      await loadDocuments()
      return true
    } catch (err: any) {
      ui.showToast(err.message || '删除文档失败', 'error')
      return false
    }
  }

  async function deleteDraft(id: string) {
    return deleteDocument(id)
  }

  async function publishDocument(id: string) {
    try {
      const doc = await api.documents.publishDocument(id)
      ui.showToast('制度文档正式发布成功', 'success')
      await loadDocumentDetail(id)
      await loadDocuments()
      return doc
    } catch (err: any) {
      ui.showToast(err.message || '发布失败', 'error')
      return null
    }
  }

  async function reviseDocument(id: string, payload: ReviseDocument) {
    try {
      const doc = await api.documents.reviseDocument(id, payload)
      ui.showToast(`新版本 v${doc.version}.0 修订发布成功`, 'success')
      await loadDocumentDetail(id)
      await loadDocuments()
      return doc
    } catch (err: any) {
      ui.showToast(err.message || '修订发布失败', 'error')
      return null
    }
  }

  async function rollbackDocument(id: string, payload: RollbackDocument) {
    try {
      const doc = await api.documents.rollbackDocument(id, payload)
      ui.showToast(`已成功回退至版本 v${payload.targetVersion}.0（新生成版本为 v${doc.version}.0）`, 'success')
      await loadDocumentDetail(id)
      await loadDocuments()
      return doc
    } catch (err: any) {
      ui.showToast(err.message || '回退文档版本失败', 'error')
      return null
    }
  }

  async function compareVersions(id: string, v1: number, v2: number): Promise<DocumentDiffView | null> {
    try {
      return await api.documents.compareVersions(id, v1, v2)
    } catch (err: any) {
      ui.showToast(err.message || '获取版本差异对比失败', 'error')
      return null
    }
  }

  async function moveDocumentCategory(id: string, newCategoryId: string) {
    try {
      const doc = await api.documents.moveDocumentCategory(id, { newCategoryId })
      ui.showToast('文档分类调整成功', 'success')
      await loadDocumentDetail(id)
      await loadDocuments()
      return doc
    } catch (err: any) {
      ui.showToast(err.message || '调整分类失败', 'error')
      return null
    }
  }

  async function archiveDocument(id: string) {
    try {
      await api.documents.archiveDocument(id)
      ui.showToast('文档已归档下线', 'success')
      await loadDocumentDetail(id)
      await loadDocuments()
      return true
    } catch (err: any) {
      ui.showToast(err.message || '归档失败', 'error')
      return false
    }
  }

  async function acknowledgeDocument(id: string) {
    try {
      await api.documents.acknowledgeDocument(id)
      ui.showToast('已确认签署本规章制度', 'success')
      await loadDocumentDetail(id)
      await loadDocuments()
      return true
    } catch (err: any) {
      ui.showToast(err.message || '确认签署失败', 'error')
      return false
    }
  }

  async function downloadAttachment(documentId: string, fileId: string) {
    try {
      await api.files.download(fileId, 'document', documentId)
    } catch (err: any) {
      ui.showToast(err.message || '下载附件失败', 'error')
    }
  }

  async function generateDemoData() {
    try {
      const res = await api.documents.generateDemoData()
      ui.showToast(`示例数据生成完毕：新增 ${res.created} 篇，跳过 ${res.skipped} 篇`, 'success')
      await loadCategories()
      await loadDocuments()
      return res
    } catch (err: any) {
      ui.showToast(err.message || '生成示例数据失败', 'error')
      return null
    }
  }

  function reset() {
    categories.value = []
    documents.value = []
    total.value = 0
    page.value = 1
    currentDocument.value = null
    currentVersions.value = []
    currentStats.value = null
    keyword.value = ''
    selectedCategoryId.value = ''
    selectedTag.value = ''
    selectedStatus.value = null
    filterMustRead.value = undefined
    filterPendingAck.value = undefined
  }

  return {
    categories,
    documents,
    total,
    page,
    pageSize,
    totalPages,
    loading,
    currentDocument,
    currentVersions,
    currentStats,
    keyword,
    selectedCategoryId,
    selectedTag,
    selectedStatus,
    filterMustRead,
    filterPendingAck,
    pendingMustReadDocuments,

    loadCategories,
    loadDocuments,
    loadDocumentDetail,
    loadStats,
    saveCategory,
    deleteCategory,
    createDraft,
    updateDraft,
    deleteDraft,
    deleteDocument,
    publishDocument,
    reviseDocument,
    rollbackDocument,
    compareVersions,
    moveDocumentCategory,
    archiveDocument,
    acknowledgeDocument,
    downloadAttachment,
    generateDemoData,
    reset
  }
})
