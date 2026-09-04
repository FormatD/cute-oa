<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useDocumentStore } from '../stores/documents'
import { useOrganizationStore } from '../stores/organization'
import { useUiStore } from '../stores/ui'
import OaDialog from '../components/OaDialog.vue'
import type { DocumentCategory, KnowledgeDocument, SaveDocument, SaveDocumentCategory } from '../api/types'

const router = useRouter()
const auth = useAuthStore()
const docStore = useDocumentStore()
const organization = useOrganizationStore()
const ui = useUiStore()

const isGlobalManager = computed(() => auth.currentUser?.permissions?.includes('DOCUMENT_MANAGE') === true)
const isDeptManager = computed(() => auth.currentUser?.permissions?.includes('DOCUMENT_DEPT_MANAGE') === true)
const isManager = computed(() => isGlobalManager.value || isDeptManager.value)

function getDepartmentName(deptId?: string | null) {
  if (!deptId) return '全公司'
  const d = organization.departments.find(item => item.id === deptId)
  return d ? d.name : deptId
}

function canManageDoc(doc: KnowledgeDocument) {
  if (isGlobalManager.value) return true
  if (isDeptManager.value && doc.departmentId === auth.currentUser?.departmentId) return true
  return false
}

function canEditDoc(doc: KnowledgeDocument) {
  if (doc.status !== 'Draft') return false
  if (isGlobalManager.value) return true
  if (isDeptManager.value && doc.departmentId === auth.currentUser?.departmentId) return true
  if (doc.departmentId && doc.departmentId === auth.currentUser?.departmentId) return true
  if (doc.createdBy === auth.currentUser?.id) return true
  return false
}

function canDeleteDoc(doc: KnowledgeDocument) {
  if (isGlobalManager.value) return true
  // Department manager can delete ANY document in their department (draft, published, archived)
  if (isDeptManager.value && doc.departmentId === auth.currentUser?.departmentId) return true
  // Ordinary employee can only delete their own drafts
  if (doc.status === 'Draft' && doc.createdBy === auth.currentUser?.id) return true
  return false
}

function canManageCategory(cat: DocumentCategory) {
  if (isGlobalManager.value) return true
  if (isDeptManager.value && cat.departmentId === auth.currentUser?.departmentId) return true
  return false
}

// Dialogs
const documentEditorOpen = ref(false)
const editingDocId = ref<string | null>(null)
const categoryManagerOpen = ref(false)
const categoryEditorOpen = ref(false)
const editingCatId = ref<string | null>(null)

const docForm = reactive<{
  title: string
  categoryId: string
  departmentId: string
  summary: string
  content: string
  tagsInput: string
  isMustRead: boolean
  effectiveDate: string
  expiryDate: string
  attachments: string[]
}>({
  title: '',
  categoryId: '',
  departmentId: '',
  summary: '',
  content: '',
  tagsInput: '',
  isMustRead: false,
  effectiveDate: new Date().toISOString().slice(0, 10),
  expiryDate: '',
  attachments: []
})

const catForm = reactive<{
  code: string
  name: string
  description: string
  parentId: string
  departmentId: string
  sortOrder: number
}>({
  code: '',
  name: '',
  description: '',
  parentId: '',
  departmentId: '',
  sortOrder: 10
})

const availableParentCategories = computed(() => {
  return docStore.categories.filter(c => {
    if (editingCatId.value && c.id === editingCatId.value) return false
    if (!isGlobalManager.value && isDeptManager.value) {
      return !c.departmentId || c.departmentId === auth.currentUser?.departmentId
    }
    return true
  })
})

function getParentCategoryName(parentId?: string | null) {
  if (!parentId) return '一级分类'
  const parent = docStore.categories.find(c => c.id === parentId)
  return parent ? parent.name : '—'
}

const hierarchicalCategories = computed(() => {
  const result: DocumentCategory[] = []
  const roots = docStore.categories.filter(c => !c.parentId)
  const childrenMap = new Map<string, DocumentCategory[]>()
  for (const c of docStore.categories) {
    if (c.parentId) {
      if (!childrenMap.has(c.parentId)) childrenMap.set(c.parentId, [])
      childrenMap.get(c.parentId)!.push(c)
    }
  }
  for (const root of roots) {
    result.push(root)
    const children = childrenMap.get(root.id)
    if (children) {
      result.push(...children)
    }
  }
  for (const c of docStore.categories) {
    if (!result.some(r => r.id === c.id)) {
      result.push(c)
    }
  }
  return result
})

onMounted(async () => {
  await Promise.all([
    docStore.loadCategories(),
    docStore.loadDocuments(),
    organization.loadOrganization()
  ])
})

function onSearch() {
  docStore.page = 1
  void docStore.loadDocuments()
}

function selectCategory(catId: string) {
  docStore.selectedCategoryId = docStore.selectedCategoryId === catId ? '' : catId
  docStore.page = 1
  void docStore.loadDocuments()
}

function selectQuickFilter(type: 'all' | 'mustRead' | 'pendingAck' | 'draft' | 'archived') {
  docStore.selectedCategoryId = ''
  docStore.selectedTag = ''
  docStore.keyword = ''
  docStore.selectedStatus = null
  docStore.filterMustRead = undefined
  docStore.filterPendingAck = undefined

  if (type === 'mustRead') {
    docStore.filterMustRead = true
  } else if (type === 'pendingAck') {
    docStore.filterMustRead = true
    docStore.filterPendingAck = true
  } else if (type === 'draft') {
    docStore.selectedStatus = 0
  } else if (type === 'archived') {
    docStore.selectedStatus = 2
  }
  docStore.page = 1
  void docStore.loadDocuments()
}

function selectTag(tag: string) {
  docStore.selectedTag = docStore.selectedTag === tag ? '' : tag
  docStore.page = 1
  void docStore.loadDocuments()
}

function resetFilters() {
  docStore.keyword = ''
  docStore.selectedCategoryId = ''
  docStore.selectedTag = ''
  docStore.selectedStatus = null
  docStore.filterMustRead = undefined
  docStore.filterPendingAck = undefined
  docStore.page = 1
  void docStore.loadDocuments()
}

const mustReadChecked = computed({
  get: () => docStore.filterMustRead === true,
  set: (val: boolean) => {
    docStore.filterMustRead = val ? true : undefined
    if (!val) docStore.filterPendingAck = undefined
    onSearch()
  }
})

const pendingAckChecked = computed({
  get: () => docStore.filterPendingAck === true,
  set: (val: boolean) => {
    docStore.filterPendingAck = val ? true : undefined
    onSearch()
  }
})

// Document Create/Edit
function resetDocForm() {
  editingDocId.value = null
  docForm.title = ''
  docForm.categoryId = docStore.categories[0]?.id ?? ''
  docForm.departmentId = isGlobalManager.value ? '' : (auth.currentUser?.departmentId ?? '')
  docForm.summary = ''
  docForm.content = ''
  docForm.tagsInput = ''
  docForm.isMustRead = false
  docForm.effectiveDate = new Date().toISOString().slice(0, 10)
  docForm.expiryDate = ''
  docForm.attachments = []
}

function openCreateDocument() {
  resetDocForm()
  documentEditorOpen.value = true
}

function closeDocumentEditor() {
  const isDirty = Boolean(docForm.title.trim() || docForm.summary.trim() || docForm.content.trim())
  if (isDirty) {
    if (!confirm('当前制度文档内容尚未保存，确定要退出编辑吗？未保存的内容将丢失。')) {
      return
    }
  }
  documentEditorOpen.value = false
}

function openEditDocument(doc: KnowledgeDocument) {
  editingDocId.value = doc.id
  docForm.title = doc.title
  docForm.categoryId = doc.categoryId
  docForm.departmentId = doc.departmentId || ''
  docForm.summary = doc.summary
  docForm.content = doc.content
  docForm.tagsInput = doc.tags.join(', ')
  docForm.isMustRead = doc.isMustRead
  docForm.effectiveDate = doc.effectiveDate
  docForm.expiryDate = doc.expiryDate || ''
  docForm.attachments = [...doc.attachments]
  documentEditorOpen.value = true
}

async function submitDocument() {
  if (!docForm.title.trim()) {
    ui.showToast('请输入制度文档标题', 'error')
    return
  }
  if (!docForm.categoryId) {
    ui.showToast('请选择所属目录分类', 'error')
    return
  }
  if (!docForm.summary.trim()) {
    ui.showToast('请输入文档摘要', 'error')
    return
  }
  if (!docForm.content.trim()) {
    ui.showToast('请输入文档正文', 'error')
    return
  }

  const tags = docForm.tagsInput.split(/[,，]/).map(t => t.trim()).filter(Boolean)
  const payload: SaveDocument = {
    title: docForm.title.trim(),
    categoryId: docForm.categoryId,
    departmentId: docForm.departmentId.trim() || null,
    summary: docForm.summary.trim(),
    content: docForm.content.trim(),
    tags,
    isMustRead: docForm.isMustRead,
    effectiveDate: docForm.effectiveDate,
    expiryDate: docForm.expiryDate.trim() || null,
    attachments: docForm.attachments
  }

  let res: KnowledgeDocument | null = null
  if (editingDocId.value) {
    res = await docStore.updateDraft(editingDocId.value, payload)
  } else {
    res = await docStore.createDraft(payload)
  }

  if (res) {
    documentEditorOpen.value = false
    resetDocForm()
  }
}

async function publishDraft(doc: KnowledgeDocument) {
  await docStore.publishDocument(doc.id)
}

async function handleDeleteDocument(doc: KnowledgeDocument) {
  const isDraft = doc.status === 'Draft'
  const prompt = isDraft
    ? `确认删除制度草稿《${doc.title}》吗？`
    : `确认删除制度文档《${doc.title}》（v${doc.version}.0）吗？\n\n警告：删除后该文档的所有历史版本及员工签收记录将被全部永久删除！`
  if (confirm(prompt)) {
    await docStore.deleteDocument(doc.id)
  }
}

// Category Management
function openCategoryManager() {
  categoryManagerOpen.value = true
}

function openCreateCategory() {
  editingCatId.value = null
  catForm.code = ''
  catForm.name = ''
  catForm.description = ''
  catForm.parentId = ''
  catForm.departmentId = isGlobalManager.value ? '' : (auth.currentUser?.departmentId ?? '')
  catForm.sortOrder = 10
  categoryEditorOpen.value = true
}

function openEditCategory(cat: DocumentCategory) {
  editingCatId.value = cat.id
  catForm.code = cat.code
  catForm.name = cat.name
  catForm.description = cat.description || ''
  catForm.parentId = cat.parentId || ''
  catForm.departmentId = cat.departmentId || ''
  catForm.sortOrder = cat.sortOrder
  categoryEditorOpen.value = true
}

async function submitCategory() {
  if (!catForm.code.trim()) {
    ui.showToast('请输入分类编码', 'error')
    return
  }
  if (!catForm.name.trim()) {
    ui.showToast('请输入分类名称', 'error')
    return
  }

  const payload: SaveDocumentCategory = {
    code: catForm.code.trim().toUpperCase(),
    name: catForm.name.trim(),
    description: catForm.description.trim() || null,
    parentId: catForm.parentId.trim() || null,
    departmentId: catForm.departmentId.trim() || null,
    sortOrder: Number(catForm.sortOrder) || 10
  }

  const ok = await docStore.saveCategory(payload, editingCatId.value || undefined)
  if (ok) {
    categoryEditorOpen.value = false
  }
}

async function deleteCategory(cat: DocumentCategory) {
  if (cat.documentCount > 0) {
    ui.showToast('该分类下存在有效文档，不可删除', 'error')
    return
  }
  if (confirm(`确认删除分类「${cat.name}」吗？`)) {
    await docStore.deleteCategory(cat.id)
  }
}
</script>

<template>
  <div class="page-heading">
    <div>
      <p class="eyebrow">COMPANY KNOWLEDGE BASE</p>
      <h1>企业知识库与制度中心</h1>
      <p>集中查阅全公司规章制度、工作规范指引、合规守则及公文合同模板。</p>
    </div>
    <div class="actions">
      <button v-if="isManager" class="secondary" @click="docStore.generateDemoData">
        生成示例制度
      </button>
      <button v-if="isManager" class="secondary" @click="openCategoryManager">
        目录分类管理
      </button>
      <button @click="openCreateDocument">
        <span>＋</span> 编制制度/文档
      </button>
    </div>
  </div>

  <!-- Must-Read Policy Alert Banner -->
  <aside v-if="docStore.pendingMustReadDocuments.length" class="must-read-banner">
    <div class="banner-icon">⚠</div>
    <div class="banner-content">
      <strong>待签收合规提醒</strong>
      <p>您有 {{ docStore.pendingMustReadDocuments.length }} 份重要企业合规制度尚未完成在线签署确认，请尽快查阅并签署：</p>
      <div class="banner-links">
        <button
          v-for="doc in docStore.pendingMustReadDocuments"
          :key="doc.id"
          class="banner-link-btn"
          @click="router.push(`/documents/${doc.id}`)"
        >
          《{{ doc.title }}》v{{ doc.version }} (立即签署 →)
        </button>
      </div>
    </div>
  </aside>

  <div class="knowledge-layout">
    <!-- Left Navigation Column -->
    <aside class="kb-sidebar">
      <section class="panel nav-panel">
        <h3>快捷视图</h3>
        <ul class="filter-nav">
          <li
            :class="{ active: !docStore.selectedCategoryId && !docStore.filterMustRead && docStore.selectedStatus === null }"
            @click="selectQuickFilter('all')"
          >
            <span>全部制度与文档</span>
            <small>{{ docStore.total }}</small>
          </li>
          <li
            :class="{ active: docStore.filterMustRead && !docStore.filterPendingAck }"
            @click="selectQuickFilter('mustRead')"
          >
            <span>★ 全员/部门必读</span>
          </li>
          <li
            :class="{ active: docStore.filterPendingAck }"
            @click="selectQuickFilter('pendingAck')"
          >
            <span class="highlight-text">⚠ 待我签署确认</span>
            <b v-if="docStore.pendingMustReadDocuments.length" class="badge">
              {{ docStore.pendingMustReadDocuments.length }}
            </b>
          </li>
          <li
            v-if="isManager"
            :class="{ active: docStore.selectedStatus === 0 }"
            @click="selectQuickFilter('draft')"
          >
            <span>草稿箱</span>
          </li>
          <li
            v-if="isManager"
            :class="{ active: docStore.selectedStatus === 2 }"
            @click="selectQuickFilter('archived')"
          >
            <span>已归档文档</span>
          </li>
        </ul>

        <hr class="divider" />

        <div class="category-header">
          <h3>目录分类</h3>
          <button v-if="isManager" class="text-btn" @click="openCreateCategory">＋ 新增</button>
        </div>
        <ul class="category-tree">
          <li
            v-for="cat in hierarchicalCategories"
            :key="cat.id"
            :class="{ active: docStore.selectedCategoryId === cat.id, 'sub-category': !!cat.parentId }"
            @click="selectCategory(cat.id)"
          >
            <span class="cat-name">
              <span v-if="cat.parentId" class="tree-prefix">↳</span>
              📁 {{ cat.name }}
              <small v-if="cat.departmentId" class="dept-tag">[{{ getDepartmentName(cat.departmentId) }}]</small>
            </span>
            <span class="cat-count">{{ cat.documentCount }}</span>
          </li>
          <li v-if="!docStore.categories.length" class="empty-text">暂无分类</li>
        </ul>
      </section>
    </aside>

    <!-- Main Content Area -->
    <main class="kb-main">
      <!-- Unified Filter Bar -->
      <form class="filter-bar" @submit.prevent="onSearch">
        <input
          v-model="docStore.keyword"
          placeholder="搜索制度标题、文号、摘要或正文关键词"
        />
        <select v-model="docStore.selectedCategoryId" @change="onSearch">
          <option value="">全部目录分类</option>
          <option v-for="cat in docStore.categories" :key="cat.id" :value="cat.id">
            {{ cat.name }}
          </option>
        </select>
        <select v-if="isManager" v-model="docStore.selectedStatus" @change="onSearch">
          <option :value="null">全部状态</option>
          <option :value="1">已发布</option>
          <option :value="0">编制草稿</option>
          <option :value="2">已归档</option>
        </select>
        <label class="inline-check">
          <input v-model="mustReadChecked" type="checkbox" />
          全员/部门必读
        </label>
        <label v-if="mustReadChecked" class="inline-check">
          <input v-model="pendingAckChecked" type="checkbox" />
          待我签署
        </label>
        <button type="submit">查询</button>
        <button class="secondary" type="button" @click="resetFilters">重置</button>
        <button v-if="docStore.selectedTag" class="secondary" type="button" @click="selectTag('')">
          标签: #{{ docStore.selectedTag }} ✕
        </button>
      </form>

      <!-- Document Cards List -->
      <div v-if="docStore.loading" class="panel empty">
        正在加载企业知识库…
      </div>

      <div v-else-if="docStore.documents.length" class="doc-card-grid">
        <article
          v-for="doc in docStore.documents"
          :key="doc.id"
          class="doc-card"
          :class="{ 'must-read-card': doc.isMustRead && !doc.hasAcknowledged }"
        >
          <div class="doc-card-header">
            <span class="cat-badge">{{ doc.categoryName }}</span>
            <span class="version-chip">v{{ doc.version }}.0</span>
            <span v-if="doc.departmentId" class="scope-chip dept">
              👥 {{ getDepartmentName(doc.departmentId) }}
            </span>
            <span v-else class="scope-chip public">
              🏢 全公司
            </span>
            <span v-if="doc.status === 'Draft'" class="status-badge draft">草稿</span>
            <span v-else-if="doc.status === 'Archived'" class="status-badge archived">已归档</span>
            <span v-if="doc.isMustRead && !doc.hasAcknowledged" class="ack-badge pending">
              必读 · 待签收
            </span>
            <span v-else-if="doc.isMustRead && doc.hasAcknowledged" class="ack-badge done">
              ✓ 已签署
            </span>
          </div>

          <h2 class="doc-title" @click="router.push(`/documents/${doc.id}`)">
            {{ doc.title }}
          </h2>

          <p class="doc-summary">{{ doc.summary }}</p>

          <div v-if="doc.tags && doc.tags.length" class="doc-tags">
            <span
              v-for="tag in doc.tags"
              :key="tag"
              class="doc-tag"
              :class="{ selected: docStore.selectedTag === tag }"
              @click.stop="selectTag(tag)"
            >
              #{{ tag }}
            </span>
          </div>

          <div class="doc-card-footer">
            <div class="doc-meta">
              <span class="doc-number">{{ doc.number }}</span>
              <span>发布于 {{ (doc.publishedAt || doc.createdAt).slice(0, 10) }}</span>
              <span v-if="doc.departmentId" class="dept-scope">适用范围：{{ getDepartmentName(doc.departmentId) }}专属</span>
              <span v-else class="public-scope">适用范围：全公司通用</span>
            </div>

            <div class="doc-actions">
              <button
                v-if="doc.status === 'Draft' && canEditDoc(doc)"
                class="secondary small-btn"
                @click="openEditDocument(doc)"
              >
                编辑草稿
              </button>
              <button
                v-if="doc.status === 'Draft' && canManageDoc(doc)"
                class="small-btn"
                @click="publishDraft(doc)"
              >
                正式发布
              </button>
              <button
                v-if="canDeleteDoc(doc)"
                class="danger-outline small-btn"
                @click="handleDeleteDocument(doc)"
              >
                删除
              </button>
              <button
                v-if="doc.status === 'Published'"
                class="secondary small-btn"
                @click="router.push(`/documents/${doc.id}`)"
              >
                阅读全文 →
              </button>
            </div>
          </div>
        </article>
      </div>

      <div v-else class="panel empty">
        暂无符合条件的制度文档。
      </div>

      <!-- Pagination -->
      <div v-if="docStore.totalPages > 1" class="pagination">
        <span>共 {{ docStore.total }} 篇制度文档</span>
        <div>
          <button
            class="secondary"
            :disabled="docStore.page <= 1"
            @click="docStore.loadDocuments(docStore.page - 1)"
          >
            上一页
          </button>
          <b>{{ docStore.page }} / {{ docStore.totalPages }}</b>
          <button
            class="secondary"
            :disabled="docStore.page >= docStore.totalPages"
            @click="docStore.loadDocuments(docStore.page + 1)"
          >
            下一页
          </button>
        </div>
      </div>
    </main>
  </div>

  <!-- Document Create / Edit Dialog -->
  <OaDialog
    :open="documentEditorOpen"
    :title="editingDocId ? '编辑制度文档草稿' : '编制新制度/规范文档'"
    description="编制完成后保存在草稿箱中，确认无误后可一键正式向全员或部门发布。"
    :submit-label="editingDocId ? '更新草稿' : '保存草稿'"
    :busy="docStore.loading"
    width="min(860px, 95vw)"
    :mask-closable="false"
    @close="closeDocumentEditor"
    @submit="submitDocument"
  >
    <label class="dialog-field">
      文档标题
      <input v-model="docForm.title" maxlength="100" placeholder="例如：《差旅标准与费用报销管理实施细则》" />
    </label>

    <div class="field-row">
      <label class="dialog-field">
        所属目录分类
        <select v-model="docForm.categoryId">
          <option v-for="cat in docStore.categories" :key="cat.id" :value="cat.id">
            {{ cat.name }}
          </option>
        </select>
      </label>

      <label class="dialog-field">
        适用部门
        <select v-model="docForm.departmentId" :disabled="!isGlobalManager">
          <option value="">全公司通用（全体在职员工）</option>
          <option v-for="dept in organization.departments" :key="dept.id" :value="dept.id">
            仅限 {{ dept.name }}
          </option>
        </select>
        <small v-if="!isGlobalManager" class="hint-text">
          编制的规章制度将归属于您的所属部门「{{ getDepartmentName(auth.currentUser?.departmentId) }}」。
        </small>
      </label>
    </div>

    <div class="field-row">
      <label class="dialog-field">
        生效日期
        <input v-model="docForm.effectiveDate" type="date" />
      </label>

      <label class="dialog-field">
        失效日期（可选）
        <input v-model="docForm.expiryDate" type="date" placeholder="长期有效留空" />
      </label>
    </div>

    <div class="field-row">
      <label class="dialog-field">
        检索标签（以逗号分隔）
        <input v-model="docForm.tagsInput" placeholder="例如：财务, 报销, 差旅标准" />
      </label>

      <label class="dialog-field checkbox-field">
        <input v-model="docForm.isMustRead" type="checkbox" />
        <span>要求全员/部门员工在线签署确认（必读制度）</span>
      </label>
    </div>

    <label class="dialog-field">
      摘要简介（1–300 字）
      <textarea v-model="docForm.summary" rows="2" maxlength="300" placeholder="简要概述该制度的核心适用范围及调整要点"></textarea>
    </label>

    <label class="dialog-field">
      正文内容（支持 Markdown 语法与表格）
      <textarea v-model="docForm.content" rows="12" maxlength="20000" placeholder="支持 # 标题、* 列表、表格及引用语法…"></textarea>
    </label>
  </OaDialog>

  <!-- Category Manager Dialog -->
  <OaDialog
    :open="categoryManagerOpen"
    title="目录分类管理"
    description="管理企业知识库的组织结构层级与部门分类设置。"
    submit-label="关闭"
    width="min(820px, 95vw)"
    :mask-closable="false"
    @close="categoryManagerOpen = false"
    @submit="categoryManagerOpen = false"
  >
    <div class="cat-mgr-header">
      <button class="secondary" @click="openCreateCategory">＋ 新增分类</button>
    </div>

    <table class="data-table">
      <thead>
        <tr>
          <th>分类名称</th>
          <th>编码</th>
          <th>组织层级</th>
          <th>适用部门</th>
          <th>排序号</th>
          <th>文档数</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="cat in docStore.categories" :key="cat.id">
          <td><strong>{{ cat.name }}</strong></td>
          <td><code>{{ cat.code }}</code></td>
          <td>{{ getParentCategoryName(cat.parentId) }}</td>
          <td>{{ getDepartmentName(cat.departmentId) }}</td>
          <td>{{ cat.sortOrder }}</td>
          <td>{{ cat.documentCount }}</td>
          <td class="task-actions">
            <button v-if="canManageCategory(cat)" class="secondary small-btn" @click="openEditCategory(cat)">编辑</button>
            <button
              v-if="canManageCategory(cat) && cat.documentCount === 0"
              class="danger-outline small-btn"
              @click="deleteCategory(cat)"
            >
              删除
            </button>
          </td>
        </tr>
      </tbody>
    </table>
  </OaDialog>

  <!-- Category Edit Dialog -->
  <OaDialog
    :open="categoryEditorOpen"
    :title="editingCatId ? '编辑分类组织结构' : '新增分类'"
    description="设置分类名称、编码、上级分类及适用部门。"
    submit-label="保存分类"
    :mask-closable="false"
    @close="categoryEditorOpen = false"
    @submit="submitCategory"
  >
    <label class="dialog-field">
      分类名称
      <input v-model="catForm.name" maxlength="100" placeholder="例如：技术规范与架构指引" />
    </label>

    <label class="dialog-field">
      分类编码
      <input
        v-model="catForm.code"
        maxlength="64"
        placeholder="例如：TECH_GUIDE（唯一大写英文字符）"
        :disabled="!!editingCatId"
      />
    </label>

    <label class="dialog-field">
      上级分类（组织结构层级）
      <select v-model="catForm.parentId">
        <option value="">无（作为一级顶级分类）</option>
        <option
          v-for="parent in availableParentCategories"
          :key="parent.id"
          :value="parent.id"
        >
          📁 {{ parent.name }} ({{ parent.code }})
        </option>
      </select>
      <small class="hint-text">通过设置上级分类，可构建多层级的部门制度知识树与组织结构分类。</small>
    </label>

    <label class="dialog-field">
      适用部门
      <select v-model="catForm.departmentId" :disabled="!isGlobalManager">
        <option value="">全公司通用</option>
        <option v-for="dept in organization.departments" :key="dept.id" :value="dept.id">
          仅限 {{ dept.name }}
        </option>
      </select>
      <small v-if="!isGlobalManager" class="hint-text">
        您是部门管理员，创建的分类将归属于 {{ getDepartmentName(auth.currentUser?.departmentId) }}。
      </small>
    </label>

    <label class="dialog-field">
      排序号
      <input v-model.number="catForm.sortOrder" type="number" />
    </label>

    <label class="dialog-field">
      描述说明
      <input v-model="catForm.description" maxlength="200" placeholder="分类简述" />
    </label>
  </OaDialog>
</template>

<style scoped>
.actions {
  display: flex;
  gap: 8px;
}

.must-read-banner {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  background: #fff8e6;
  border: 1px solid #ffd591;
  border-radius: 8px;
  padding: 16px 20px;
  margin-bottom: 20px;
}

.banner-icon {
  font-size: 24px;
  color: #fa8c16;
}

.banner-content strong {
  color: #d46b08;
  font-size: 15px;
}

.banner-content p {
  margin: 4px 0 10px;
  color: #595959;
  font-size: 13px;
}

.banner-links {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.banner-link-btn {
  background: #fa8c16;
  color: #fff;
  border: none;
  padding: 6px 14px;
  border-radius: 4px;
  font-size: 13px;
  cursor: pointer;
  font-weight: 500;
}
.banner-link-btn:hover {
  background: #d46b08;
}

.knowledge-layout {
  display: grid;
  grid-template-columns: 260px 1fr;
  gap: 20px;
}

.kb-sidebar {
  display: flex;
  flex-direction: column;
}

.nav-panel {
  padding: 16px;
}

.nav-panel h3 {
  font-size: 14px;
  color: #8c8c8c;
  text-transform: uppercase;
  margin-bottom: 12px;
}

.filter-nav {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.filter-nav li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  border-radius: 6px;
  cursor: pointer;
  font-size: 14px;
  color: #262626;
  transition: all 0.2s;
}

.filter-nav li:hover {
  background: #f5f5f5;
}

.filter-nav li.active {
  background: #e6f7ff;
  color: #1890ff;
  font-weight: 600;
}

.highlight-text {
  color: #fa8c16;
}

.badge {
  background: #ff4d4f;
  color: #fff;
  font-size: 11px;
  padding: 2px 6px;
  border-radius: 10px;
}

.divider {
  border: none;
  border-top: 1px solid #f0f0f0;
  margin: 16px 0;
}

.category-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.text-btn {
  background: transparent;
  border: none;
  color: #1890ff;
  cursor: pointer;
  padding: 0;
  font-size: 12px;
}

.category-tree {
  list-style: none;
  padding: 0;
  margin: 10px 0 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.category-tree li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  border-radius: 6px;
  cursor: pointer;
  font-size: 13px;
}

.category-tree li:hover {
  background: #f5f5f5;
}

.category-tree li.active {
  background: #e6f7ff;
  color: #1890ff;
  font-weight: 600;
}

.category-tree li.sub-category {
  padding-left: 26px;
}

.tree-prefix {
  color: #8c8c8c;
  margin-right: 4px;
  font-weight: bold;
}

.cat-count {
  background: #f0f0f0;
  color: #595959;
  font-size: 11px;
  padding: 1px 6px;
  border-radius: 8px;
}

.empty-text {
  color: #bfbfbf;
  font-size: 12px;
  padding: 8px;
}

.kb-main .filter-bar input:first-child {
  flex: 1 1 200px;
}

.doc-card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 16px;
}

.doc-card {
  background: #fff;
  border: 1px solid #e8e8e8;
  border-radius: 8px;
  padding: 18px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  transition: box-shadow 0.2s, border-color 0.2s;
}

.doc-card:hover {
  box-shadow: 0 4px 12px rgba(0,0,0,0.08);
  border-color: #d9d9d9;
}

.must-read-card {
  border-left: 4px solid #fa8c16;
}

.doc-card-header {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}

.cat-badge {
  background: #f5f5f5;
  color: #595959;
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 4px;
}

.version-chip {
  background: #fafafa;
  color: #8c8c8c;
  border: 1px solid #d9d9d9;
  font-size: 11px;
  padding: 1px 6px;
  border-radius: 4px;
}

.status-badge {
  font-size: 11px;
  padding: 2px 6px;
  border-radius: 4px;
}
.status-badge.draft {
  background: #fffbe6;
  color: #d48806;
}
.status-badge.archived {
  background: #f5f5f5;
  color: #bfbfbf;
}

.ack-badge {
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 4px;
  font-weight: 500;
}
.ack-badge.pending {
  background: #fff1f0;
  color: #f5222d;
  border: 1px solid #ffa39e;
}
.ack-badge.done {
  background: #f6ffed;
  color: #52c41a;
  border: 1px solid #b7eb8f;
}

.doc-title {
  font-size: 16px;
  color: #1f1f1f;
  margin: 0 0 8px;
  cursor: pointer;
  line-height: 1.4;
}

.doc-title:hover {
  color: #1890ff;
}

.doc-summary {
  font-size: 13px;
  color: #595959;
  line-height: 1.5;
  margin: 0 0 12px;
  flex: 1;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.doc-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-bottom: 12px;
}

.doc-tag {
  font-size: 11px;
  color: #8c8c8c;
  background: #fafafa;
  padding: 2px 6px;
  border-radius: 4px;
  cursor: pointer;
}

.doc-tag:hover,
.doc-tag.selected {
  background: #e6f7ff;
  color: #1890ff;
}

.doc-card-footer {
  border-top: 1px solid #f0f0f0;
  padding-top: 12px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.doc-meta {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: 11px;
  color: #8c8c8c;
}

.doc-number {
  font-family: monospace;
}

.dept-scope {
  color: #fa8c16;
}

.doc-actions {
  display: flex;
  gap: 6px;
}

.small-btn {
  padding: 4px 10px;
  font-size: 12px;
}

.field-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.checkbox-field {
  display: flex;
  flex-direction: row !important;
  align-items: center;
  gap: 8px;
  margin-top: 24px;
}

.checkbox-field input {
  width: auto;
}

.cat-mgr-header {
  margin-bottom: 12px;
  display: flex;
  justify-content: flex-end;
}

.dept-tag {
  color: #fa8c16;
  font-size: 11px;
  margin-left: 4px;
}

.scope-chip {
  font-size: 11px;
  padding: 1px 6px;
  border-radius: 4px;
}

.scope-chip.dept {
  background: #fff7e6;
  color: #d46b08;
  border: 1px solid #ffd591;
}

.scope-chip.public {
  background: #e6f7ff;
  color: #096dd9;
  border: 1px solid #91d5ff;
}

.public-scope {
  color: #1890ff;
}

.hint-text {
  color: #8c8c8c;
  font-size: 12px;
  margin-top: 4px;
}
</style>
