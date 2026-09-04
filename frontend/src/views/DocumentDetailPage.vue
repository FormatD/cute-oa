<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useDocumentStore } from '../stores/documents'
import { useOrganizationStore } from '../stores/organization'
import { useUiStore } from '../stores/ui'
import OaDialog from '../components/OaDialog.vue'
import type { DocumentDiffView, ReviseDocument } from '../api/types'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const docStore = useDocumentStore()
const organization = useOrganizationStore()
const ui = useUiStore()

const docId = computed(() => String(route.params.id))
const doc = computed(() => docStore.currentDocument)
const versions = computed(() => docStore.currentVersions)
const stats = computed(() => docStore.currentStats)

const isGlobalManager = computed(() => auth.currentUser?.permissions?.includes('DOCUMENT_MANAGE') === true)
const isDeptManager = computed(() => auth.currentUser?.permissions?.includes('DOCUMENT_DEPT_MANAGE') === true)
const canManageThisDoc = computed(() => {
  if (!doc.value) return false
  if (isGlobalManager.value) return true
  if (isDeptManager.value && doc.value.departmentId === auth.currentUser?.departmentId) return true
  return false
})

const canEditThisDoc = computed(() => {
  if (!doc.value) return false
  if (isGlobalManager.value) return true
  // Department ordinary employees can edit/revise their department's documents
  if (doc.value.departmentId && doc.value.departmentId === auth.currentUser?.departmentId) return true
  return false
})

function getDepartmentName(deptId?: string | null) {
  if (!deptId) return '全公司'
  const d = organization.departments.find(item => item.id === deptId)
  return d ? d.name : deptId
}

// Revise Dialog
const reviseDialogOpen = ref(false)
const reviseForm = reactive<ReviseDocument>({
  version: 1,
  title: '',
  summary: '',
  content: '',
  changeNotes: '',
  attachments: []
})

// Move Category Dialog
const moveCategoryDialogOpen = ref(false)
const targetCategoryId = ref('')

// Rollback Dialog
const rollbackDialogOpen = ref(false)
const targetRollbackVersion = ref(1)
const rollbackReason = ref('')

// Version Diff Dialog
const diffDialogOpen = ref(false)
const diffLoading = ref(false)
const diffResult = ref<DocumentDiffView | null>(null)
const selectedCompareV1 = ref<number>(1)
const selectedCompareV2 = ref<number>(1)

// Stats tabs
const statsTab = ref<'acknowledged' | 'pending'>('pending')

onMounted(async () => {
  await Promise.all([
    organization.loadOrganization(),
    docStore.loadCategories()
  ])
  const loaded = await docStore.loadDocumentDetail(docId.value)
  if (loaded && loaded.isMustRead && canManageThisDoc.value) {
    void docStore.loadStats(docId.value)
  }
  if (versions.value.length > 1) {
    selectedCompareV1.value = versions.value[versions.value.length - 1]?.version || 1
    selectedCompareV2.value = loaded?.version || 1
  }
})

async function onAcknowledge() {
  const ok = await docStore.acknowledgeDocument(docId.value)
  if (ok) {
    if (doc.value?.isMustRead) {
      void docStore.loadStats(docId.value)
    }
  }
}

function openReviseDialog() {
  if (!doc.value) return
  reviseForm.version = doc.value.version
  reviseForm.title = doc.value.title
  reviseForm.summary = doc.value.summary
  reviseForm.content = doc.value.content
  reviseForm.changeNotes = `修订升级为 v${doc.value.version + 1}.0`
  reviseForm.attachments = [...doc.value.attachments]
  reviseDialogOpen.value = true
}

async function submitRevision() {
  if (!reviseForm.title.trim()) {
    ui.showToast('请输入制度标题', 'error')
    return
  }
  if (!reviseForm.content.trim()) {
    ui.showToast('请输入制度正文', 'error')
    return
  }

  const res = await docStore.reviseDocument(docId.value, {
    version: reviseForm.version,
    title: reviseForm.title.trim(),
    summary: reviseForm.summary.trim(),
    content: reviseForm.content.trim(),
    changeNotes: reviseForm.changeNotes?.trim() || `升级为 v${reviseForm.version + 1}.0`,
    attachments: reviseForm.attachments
  })

  if (res) {
    reviseDialogOpen.value = false
    void docStore.loadStats(docId.value)
  }
}

function openMoveCategoryDialog() {
  if (!doc.value) return
  targetCategoryId.value = doc.value.categoryId
  moveCategoryDialogOpen.value = true
}

async function submitMoveCategory() {
  if (!doc.value || !targetCategoryId.value) return
  if (targetCategoryId.value === doc.value.categoryId) {
    moveCategoryDialogOpen.value = false
    return
  }
  const updated = await docStore.moveDocumentCategory(doc.value.id, targetCategoryId.value)
  if (updated) {
    moveCategoryDialogOpen.value = false
  }
}

function openRollbackModal(v: { version: number }) {
  targetRollbackVersion.value = v.version
  rollbackReason.value = `回退至历史版本 v${v.version}.0：恢复原标准规范`
  rollbackDialogOpen.value = true
}

async function submitRollback() {
  if (!doc.value) return
  if (!rollbackReason.value.trim()) {
    ui.showToast('请输入回退原因说明', 'error')
    return
  }
  const updated = await docStore.rollbackDocument(doc.value.id, {
    targetVersion: targetRollbackVersion.value,
    currentVersion: doc.value.version,
    reason: rollbackReason.value.trim()
  })
  if (updated) {
    rollbackDialogOpen.value = false
    void docStore.loadStats(doc.value.id)
  }
}

async function openDiffModal(v1: number, v2: number) {
  if (!doc.value) return
  diffLoading.value = true
  diffDialogOpen.value = true
  diffResult.value = await docStore.compareVersions(doc.value.id, v1, v2)
  diffLoading.value = false
}

async function handleDeleteDocument() {
  if (!doc.value) return
  if (confirm(`确认彻底删除制度文档《${doc.value.title}》（v${doc.value.version}.0）吗？\n\n警告：删除后该文档的所有历史版本及员工签收记录将被全部永久删除！`)) {
    const ok = await docStore.deleteDocument(doc.value.id)
    if (ok) {
      router.push('/documents')
    }
  }
}

async function archiveDocument() {
  if (confirm(`确认归档下线制度《${doc.value?.title}》吗？归档后普通员工将无法继续查阅。`)) {
    await docStore.archiveDocument(docId.value)
  }
}
</script>

<template>
  <div class="page-heading">
    <div>
      <p class="eyebrow">DOCUMENT DETAIL</p>
      <h1>{{ doc?.title || '制度文档详情' }}</h1>
      <p>
        文号：<code>{{ doc?.number }}</code> · 版本：v{{ doc?.version }}.0 · 分类：{{ doc?.categoryName }} ·
        <span v-if="doc?.departmentId" class="scope-chip dept">适用范围：{{ getDepartmentName(doc.departmentId) }}专属</span>
        <span v-else class="scope-chip public">适用范围：全公司通用</span>
      </p>
    </div>
    <div class="actions">
      <button class="secondary" @click="router.push('/documents')">
        ← 返回知识库
      </button>
      <button
        v-if="canManageThisDoc"
        class="secondary"
        @click="openMoveCategoryDialog"
      >
        调整分类
      </button>
      <button
        v-if="canEditThisDoc && doc?.status === 'Published'"
        class="secondary"
        @click="openReviseDialog"
      >
        修订新版本
      </button>
      <button
        v-if="canManageThisDoc && doc?.status === 'Published'"
        class="secondary"
        @click="archiveDocument"
      >
        归档下线
      </button>
      <button
        v-if="canManageThisDoc"
        class="danger-outline"
        @click="handleDeleteDocument"
      >
        删除制度
      </button>
    </div>
  </div>

  <div v-if="docStore.loading && !doc" class="panel empty">
    正在加载制度文档详情…
  </div>

  <template v-else-if="doc">
    <!-- Must-Read Sign-off Box -->
    <section v-if="doc.isMustRead" class="ack-section">
      <div v-if="!doc.hasAcknowledged" class="ack-card pending">
        <div class="ack-header">
          <span class="ack-icon">✍</span>
          <div>
            <h3>合规阅读与承诺签署</h3>
            <p>本文件为公司重要规章制度，根据合规管理与劳动纪律要求，请认真通读并确认遵守：</p>
          </div>
        </div>

        <blockquote class="ack-promise">
          “本人已认真通读并充分理解《{{ doc.title }}》（版本：v{{ doc.version }}.0）之全部条款内容，认可本制度的合法有效性，并自愿承诺在日常履职过程中严格遵守上述各项规章制度与行为守则。”
        </blockquote>

        <div class="ack-action-bar">
          <button class="primary-btn" @click="onAcknowledge">
            ✓ 我已认真阅读并承诺严格遵守本制度
          </button>
        </div>
      </div>

      <div v-else class="ack-card done">
        <div class="ack-done-info">
          <span class="done-icon">✓</span>
          <div>
            <strong>已完成在线签署确认</strong>
            <p>您已于 {{ doc.acknowledgedAt ? new Date(doc.acknowledgedAt).toLocaleString('zh-CN') : '近期' }} 签署确认遵守《{{ doc.title }}》（生效版本：v{{ doc.version }}.0）。</p>
          </div>
        </div>
      </div>
    </section>

    <!-- Admin Acknowledgement Stats Panel -->
    <section v-if="canManageThisDoc && doc.isMustRead && stats" class="panel stats-panel">
      <div class="stats-header">
        <div>
          <h3>{{ doc.departmentId ? `${getDepartmentName(doc.departmentId)}签署进度看板` : '全员签署进度看板' }}</h3>
          <p>应签人员范围：{{ doc.departmentId ? `${getDepartmentName(doc.departmentId)}全体在职人员` : '全公司全体在职员工' }}</p>
        </div>
        <div class="stats-counter">
          <div class="stat-box">
            <span>应签人数</span>
            <strong>{{ stats.totalRequired }}</strong>
          </div>
          <div class="stat-box">
            <span>已签人数</span>
            <strong class="green-text">{{ stats.totalAcknowledged }}</strong>
          </div>
          <div class="stat-box">
            <span>签收率</span>
            <strong class="blue-text">{{ stats.acknowledgedPercentage }}%</strong>
          </div>
        </div>
      </div>

      <!-- Progress Bar -->
      <div class="progress-bar-wrap">
        <div class="progress-bar" :style="{ width: `${stats.acknowledgedPercentage}%` }"></div>
      </div>

      <!-- Tabs -->
      <div class="stats-tabs">
        <button
          :class="{ active: statsTab === 'pending' }"
          @click="statsTab = 'pending'"
        >
          待签署人员 ({{ stats.pendingList.length }})
        </button>
        <button
          :class="{ active: statsTab === 'acknowledged' }"
          @click="statsTab = 'acknowledged'"
        >
          已签署记录 ({{ stats.acknowledgedList.length }})
        </button>
      </div>

      <div v-if="statsTab === 'pending'" class="stats-table-wrap">
        <table v-if="stats.pendingList.length" class="data-table">
          <thead>
            <tr>
              <th>员工姓名</th>
              <th>所属部门</th>
              <th>直属上级</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="emp in stats.pendingList" :key="emp.id">
              <td><strong>{{ emp.name }}</strong></td>
              <td>{{ emp.departmentName }}</td>
              <td>{{ emp.managerName || '—' }}</td>
            </tr>
          </tbody>
        </table>
        <p v-else class="empty-text">全员已 100% 完成签署！</p>
      </div>

      <div v-else class="stats-table-wrap">
        <table v-if="stats.acknowledgedList.length" class="data-table">
          <thead>
            <tr>
              <th>签署人</th>
              <th>所属部门</th>
              <th>签署版本</th>
              <th>签署时间</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in stats.acknowledgedList" :key="item.id">
              <td><strong>{{ item.userName }}</strong></td>
              <td>{{ item.departmentName || '—' }}</td>
              <td>v{{ item.documentVersion }}.0</td>
              <td>{{ new Date(item.acknowledgedAt).toLocaleString('zh-CN') }}</td>
            </tr>
          </tbody>
        </table>
        <p v-else class="empty-text">暂无签署记录</p>
      </div>
    </section>

    <!-- Meta Details & Content -->
    <article class="panel doc-content-panel">
      <div class="doc-meta-bar">
        <span>发布人：{{ doc.publishedByName || doc.createdByName }}</span>
        <span>发布时间：{{ (doc.publishedAt || doc.createdAt).slice(0, 10) }}</span>
        <span>适用范围：{{ doc.departmentId ? `${getDepartmentName(doc.departmentId)}专属` : '全公司公开' }}</span>
        <span>生效日期：{{ doc.effectiveDate }}</span>
        <span>失效日期：{{ doc.expiryDate || '长期有效' }}</span>
        <span>阅读次数：{{ doc.viewCount }} 次</span>
        <span>下载次数：{{ doc.downloadCount }} 次</span>
      </div>

      <div v-if="doc.tags && doc.tags.length" class="doc-tags">
        <span v-for="tag in doc.tags" :key="tag" class="doc-tag">#{{ tag }}</span>
      </div>

      <div class="doc-summary-box">
        <strong>制度摘要：</strong>
        <p>{{ doc.summary }}</p>
      </div>

      <!-- Main Document Body -->
      <div class="markdown-body">
        <pre class="content-pre">{{ doc.content }}</pre>
      </div>

      <!-- Attachments -->
      <div v-if="doc.attachments && doc.attachments.length" class="attachments-box">
        <h4>关联附件与文件（{{ doc.attachments.length }}）</h4>
        <div class="attach-list">
          <div v-for="(fileId, idx) in doc.attachments" :key="fileId" class="attach-item">
            <span>📎 附件原件 {{ idx + 1 }} (ID: {{ fileId.slice(0, 8) }}…)</span>
            <button class="secondary small-btn" @click="docStore.downloadAttachment(doc.id, fileId)">
              下载附件
            </button>
          </div>
        </div>
      </div>
    </article>

    <!-- Version History -->
    <section v-if="versions.length" class="panel version-history-panel">
      <div class="version-header-row">
        <div>
          <h3>版本沿革与修订历史</h3>
          <p class="section-subtitle">文档全生命周期修订快照自动保留，支持任意历史版本逐行差异对比与安全回退。</p>
        </div>
        <div v-if="versions.length > 1" class="compare-quick-bar">
          <label class="compare-label">快速对比：</label>
          <select v-model.number="selectedCompareV1">
            <option v-for="v in versions" :key="'v1-'+v.version" :value="v.version">
              v{{ v.version }}.0
            </option>
          </select>
          <span class="compare-vs">对比</span>
          <select v-model.number="selectedCompareV2">
            <option v-for="v in versions" :key="'v2-'+v.version" :value="v.version">
              v{{ v.version }}.0
            </option>
          </select>
          <button
            class="secondary small-btn"
            :disabled="selectedCompareV1 === selectedCompareV2"
            @click="openDiffModal(selectedCompareV1, selectedCompareV2)"
          >
            对比差异
          </button>
        </div>
      </div>

      <div class="timeline">
        <div v-for="(v, index) in versions" :key="v.id" class="timeline-item">
          <div class="timeline-dot" :class="{ current: v.version === doc.version }"></div>
          <div class="timeline-content">
            <div class="timeline-title">
              <strong>v{{ v.version }}.0</strong>
              <span v-if="v.version === doc.version" class="current-tag">当前生效版本</span>
              <span>{{ new Date(v.publishedAt).toLocaleString('zh-CN') }}</span>
              <small>修订发布人：{{ v.publishedByName }}</small>
            </div>
            <p class="timeline-notes">{{ v.changeNotes || '日常版本维护' }}</p>

            <div class="timeline-actions">
              <button
                v-if="index < versions.length - 1"
                class="secondary small-btn"
                @click="openDiffModal(versions[index + 1].version, v.version)"
              >
                与上一版本 (v{{ versions[index + 1].version }}.0) 对比
              </button>
              <button
                v-if="v.version !== doc.version"
                class="secondary small-btn"
                @click="openDiffModal(v.version, doc.version)"
              >
                与当前版本 (v{{ doc.version }}.0) 对比
              </button>
              <button
                v-if="v.version < doc.version && canEditThisDoc"
                class="warning-btn small-btn"
                @click="openRollbackModal(v)"
              >
                ↺ 回退至此版本
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>
  </template>

  <!-- Revise Dialog -->
  <OaDialog
    :open="reviseDialogOpen"
    :title="`修订制度文档：《${doc?.title}》`"
    description="发布新版本后，制度版本号将自动递增。如果为必读制度，将向员工发送新版本知晓提醒。"
    submit-label="正式发布新版本"
    :busy="docStore.loading"
    @close="reviseDialogOpen = false"
    @submit="submitRevision"
  >
    <label class="dialog-field">
      新版本标题
      <input v-model="reviseForm.title" maxlength="100" />
    </label>

    <label class="dialog-field">
      修订变更说明（必填）
      <input v-model="reviseForm.changeNotes" maxlength="300" placeholder="简述本次版本修订的核心内容及背景" />
    </label>

    <label class="dialog-field">
      新版本摘要
      <textarea v-model="reviseForm.summary" rows="2" maxlength="300"></textarea>
    </label>

    <label class="dialog-field">
      新版本正文内容（Markdown）
      <textarea v-model="reviseForm.content" rows="12" maxlength="20000"></textarea>
    </label>
  </OaDialog>

  <!-- Move Category Dialog -->
  <OaDialog
    :open="moveCategoryDialogOpen"
    title="调整文档所属分类"
    description="将当前制度文档组织分类调整到指定分类目录中。"
    submit-label="确认调整分类"
    :busy="docStore.loading"
    @close="moveCategoryDialogOpen = false"
    @submit="submitMoveCategory"
  >
    <label class="dialog-field">
      选择目标分类目录
      <select v-model="targetCategoryId">
        <option v-for="cat in docStore.categories" :key="cat.id" :value="cat.id">
          📁 {{ cat.name }} ({{ cat.code }})
        </option>
      </select>
    </label>
  </OaDialog>

  <!-- Rollback Dialog -->
  <OaDialog
    :open="rollbackDialogOpen"
    :title="`回退制度至历史版本 v${targetRollbackVersion}.0`"
    description="系统将基于目标历史版本的快照内容递增发布新版本，原有的全部修订历史将安全保留，并完整记录版本回退审计链条。"
    submit-label="确认回退并递增发布"
    :busy="docStore.loading"
    @close="rollbackDialogOpen = false"
    @submit="submitRollback"
  >
    <div class="rollback-info-alert">
      <div class="rollback-step">
        <span class="step-label">目标内容基线：</span>
        <strong>历史版本 v{{ targetRollbackVersion }}.0</strong>
      </div>
      <div class="rollback-step">
        <span class="step-label">当前生效版本：</span>
        <strong>v{{ doc?.version }}.0</strong>
      </div>
      <div class="rollback-step">
        <span class="step-label">回退后新版本：</span>
        <strong class="green-text">v{{ (doc?.version || 1) + 1 }}.0</strong>
      </div>
    </div>

    <label class="dialog-field">
      回退原因与修订说明（必填）
      <input
        v-model="rollbackReason"
        maxlength="300"
        placeholder="例如：紧急回退至 v1.0 恢复原审批标准"
      />
    </label>
  </OaDialog>

  <!-- Version Diff Dialog -->
  <OaDialog
    :open="diffDialogOpen"
    :title="diffResult ? `版本差异对比：v${diffResult.sourceVersion}.0 → v${diffResult.targetVersion}.0` : '版本差异对比'"
    description="对比两版本之间的元数据与正文逐行变动。"
    submit-label="关闭"
    @close="diffDialogOpen = false"
    @submit="diffDialogOpen = false"
  >
    <div v-if="diffLoading" class="panel empty">
      正在分析版本差异逐行对比…
    </div>

    <div v-else-if="diffResult" class="diff-container">
      <!-- Diff Summary Badges -->
      <div class="diff-summary-row">
        <span class="diff-pill added">新增 +{{ diffResult.addedLines }} 行</span>
        <span class="diff-pill removed">删除 -{{ diffResult.removedLines }} 行</span>
        <span class="diff-pill unchanged">未变动 {{ diffResult.unchangedLines }} 行</span>
      </div>

      <!-- Metadata Changes -->
      <div v-if="diffResult.titleChanged || diffResult.summaryChanged || diffResult.attachmentsChanged" class="diff-meta-box">
        <div v-if="diffResult.titleChanged" class="meta-diff-item">
          <strong>标题变更：</strong>
          <span class="line-del">{{ diffResult.sourceTitle }}</span>
          <span class="diff-arrow">→</span>
          <span class="line-ins">{{ diffResult.targetTitle }}</span>
        </div>
        <div v-if="diffResult.summaryChanged" class="meta-diff-item">
          <strong>摘要变更：</strong>
          <p class="line-del">{{ diffResult.sourceSummary }}</p>
          <p class="line-ins">{{ diffResult.targetSummary }}</p>
        </div>
        <div v-if="diffResult.attachmentsChanged" class="meta-diff-item">
          <strong>附件变动：</strong>
          <span class="line-del">原附件 {{ diffResult.sourceAttachments.length }} 个</span>
          <span class="diff-arrow">→</span>
          <span class="line-ins">现附件 {{ diffResult.targetAttachments.length }} 个</span>
        </div>
      </div>

      <!-- Content Diff Lines -->
      <div class="diff-lines-wrapper">
        <div class="diff-lines-header">正文逐行差异（LCS 分析）</div>
        <div class="diff-lines-body">
          <div
            v-for="(line, idx) in diffResult.contentDiff"
            :key="idx"
            class="diff-line"
            :class="line.type"
          >
            <span class="line-no old">{{ line.oldLineNumber || '' }}</span>
            <span class="line-no new">{{ line.newLineNumber || '' }}</span>
            <span class="line-marker">
              {{ line.type === 'added' ? '+' : line.type === 'removed' ? '-' : ' ' }}
            </span>
            <span class="line-text">{{ line.text || ' ' }}</span>
          </div>
          <div v-if="!diffResult.contentDiff.length" class="empty-text">
            正文内容完全一致，未检测到行级差异。
          </div>
        </div>
      </div>
    </div>
  </OaDialog>
</template>

<style scoped>
.actions {
  display: flex;
  gap: 8px;
}

.ack-section {
  margin-bottom: 20px;
}

.ack-card {
  border-radius: 8px;
  padding: 20px;
}

.ack-card.pending {
  background: #fff8e6;
  border: 1px solid #ffd591;
}

.ack-card.done {
  background: #f6ffed;
  border: 1px solid #b7eb8f;
}

.ack-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}

.ack-icon {
  font-size: 24px;
  color: #fa8c16;
}

.ack-header h3 {
  margin: 0;
  color: #d46b08;
  font-size: 16px;
}

.ack-header p {
  margin: 4px 0 0;
  color: #595959;
  font-size: 13px;
}

.ack-promise {
  background: #fff;
  border-left: 4px solid #fa8c16;
  padding: 12px 16px;
  margin: 12px 0 16px;
  color: #262626;
  font-size: 14px;
  line-height: 1.6;
  border-radius: 0 6px 6px 0;
}

.ack-action-bar {
  display: flex;
  justify-content: flex-end;
}

.primary-btn {
  background: #fa8c16;
  color: #fff;
  border: none;
  padding: 10px 24px;
  border-radius: 6px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  box-shadow: 0 2px 8px rgba(250, 140, 22, 0.3);
}
.primary-btn:hover {
  background: #d46b08;
}

.ack-done-info {
  display: flex;
  align-items: center;
  gap: 12px;
}

.done-icon {
  font-size: 24px;
  color: #52c41a;
  background: #fff;
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  border: 2px solid #52c41a;
}

.ack-done-info strong {
  color: #274916;
  font-size: 15px;
}

.ack-done-info p {
  margin: 4px 0 0;
  color: #595959;
  font-size: 13px;
}

.stats-panel {
  padding: 20px;
  margin-bottom: 20px;
}

.stats-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.stats-header h3 {
  margin: 0;
  font-size: 16px;
}

.stats-header p {
  margin: 4px 0 0;
  color: #8c8c8c;
  font-size: 12px;
}

.stats-counter {
  display: flex;
  gap: 24px;
}

.stat-box {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
}

.stat-box span {
  font-size: 12px;
  color: #8c8c8c;
}

.stat-box strong {
  font-size: 20px;
}

.green-text { color: #52c41a; }
.blue-text { color: #1890ff; }

.progress-bar-wrap {
  height: 8px;
  background: #f0f0f0;
  border-radius: 4px;
  overflow: hidden;
  margin-bottom: 16px;
}

.progress-bar {
  height: 100%;
  background: linear-gradient(90deg, #1890ff, #52c41a);
  transition: width 0.3s ease;
}

.stats-tabs {
  display: flex;
  gap: 12px;
  border-bottom: 1px solid #f0f0f0;
  margin-bottom: 12px;
}

.stats-tabs button {
  background: none;
  border: none;
  padding: 8px 16px;
  cursor: pointer;
  color: #595959;
  font-size: 13px;
  border-bottom: 2px solid transparent;
}

.stats-tabs button.active {
  color: #1890ff;
  border-bottom-color: #1890ff;
  font-weight: 600;
}

.empty-text {
  color: #8c8c8c;
  padding: 16px;
  text-align: center;
  font-size: 13px;
}

.doc-content-panel {
  padding: 24px 30px;
  margin-bottom: 20px;
}

.doc-meta-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  font-size: 12px;
  color: #8c8c8c;
  border-bottom: 1px solid #f0f0f0;
  padding-bottom: 12px;
  margin-bottom: 16px;
}

.doc-tags {
  display: flex;
  gap: 8px;
  margin-bottom: 16px;
}

.doc-tag {
  font-size: 12px;
  color: #1890ff;
  background: #e6f7ff;
  padding: 2px 8px;
  border-radius: 4px;
}

.doc-summary-box {
  background: #fafafa;
  border-left: 4px solid #1890ff;
  padding: 12px 16px;
  border-radius: 0 6px 6px 0;
  margin-bottom: 24px;
  font-size: 13px;
  line-height: 1.6;
}

.doc-summary-box p {
  margin: 4px 0 0;
  color: #595959;
}

.markdown-body {
  font-size: 14px;
  line-height: 1.8;
  color: #262626;
}

.content-pre {
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
  margin: 0;
  line-height: 1.8;
}

.attachments-box {
  margin-top: 30px;
  border-top: 1px solid #f0f0f0;
  padding-top: 20px;
}

.attachments-box h4 {
  margin: 0 0 12px;
  font-size: 14px;
}

.attach-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.attach-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: #f9f9f9;
  border: 1px solid #f0f0f0;
  padding: 8px 14px;
  border-radius: 6px;
  font-size: 13px;
}

.version-history-panel {
  padding: 20px;
}

.version-history-panel h3 {
  margin: 0 0 16px;
  font-size: 15px;
}

.timeline {
  display: flex;
  flex-direction: column;
  gap: 16px;
  position: relative;
  padding-left: 20px;
}

.timeline::before {
  content: '';
  position: absolute;
  left: 6px;
  top: 4px;
  bottom: 4px;
  width: 2px;
  background: #e8e8e8;
}

.timeline-item {
  position: relative;
}

.timeline-dot {
  position: absolute;
  left: -20px;
  top: 4px;
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: #1890ff;
}

.timeline-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.timeline-title {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 13px;
}

.timeline-title span {
  color: #8c8c8c;
  font-size: 12px;
}

.timeline-title small {
  color: #595959;
}

.timeline-notes {
  margin: 0;
  font-size: 13px;
  color: #595959;
}

.small-btn {
  padding: 4px 12px;
  font-size: 12px;
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

.version-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
  flex-wrap: wrap;
  gap: 12px;
}

.section-subtitle {
  margin: 4px 0 0;
  color: #8c8c8c;
  font-size: 13px;
}

.compare-quick-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  background: #f5f5f5;
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 13px;
}

.compare-label {
  color: #595959;
  font-weight: 500;
}

.compare-vs {
  color: #8c8c8c;
  font-weight: 500;
}

.current-tag {
  background: #e6f7ff;
  color: #1890ff;
  border: 1px solid #91d5ff;
  font-size: 11px;
  padding: 1px 6px;
  border-radius: 4px;
  margin-left: 8px;
  font-weight: normal;
}

.timeline-actions {
  display: flex;
  gap: 8px;
  margin-top: 10px;
  flex-wrap: wrap;
}

.warning-btn {
  background: #fa8c16;
  color: #fff;
  border: none;
  cursor: pointer;
  border-radius: 4px;
}

.warning-btn:hover {
  background: #d46b08;
}

.rollback-info-alert {
  background: #f0f5ff;
  border: 1px solid #adc6ff;
  border-radius: 6px;
  padding: 12px 16px;
  margin-bottom: 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 14px;
}

.rollback-step {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.rollback-step .step-label {
  color: #595959;
}

.rollback-step .green-text {
  color: #52c41a;
  font-size: 16px;
}

.diff-container {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.diff-summary-row {
  display: flex;
  gap: 10px;
}

.diff-pill {
  padding: 4px 12px;
  border-radius: 12px;
  font-size: 12px;
  font-weight: 600;
}

.diff-pill.added {
  background: #e6ffed;
  color: #2da44e;
  border: 1px solid #acf2bd;
}

.diff-pill.removed {
  background: #ffeef0;
  color: #cf222e;
  border: 1px solid #ffccd3;
}

.diff-pill.unchanged {
  background: #f6f8fa;
  color: #57606a;
  border: 1px solid #d0d7de;
}

.diff-meta-box {
  background: #fafafa;
  border: 1px solid #eaecef;
  border-radius: 6px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 13px;
}

.meta-diff-item strong {
  color: #262626;
  margin-right: 6px;
}

.line-del {
  background: #ffeef0;
  color: #b31d28;
  text-decoration: line-through;
  padding: 2px 6px;
  border-radius: 3px;
}

.line-ins {
  background: #e6ffed;
  color: #22863a;
  padding: 2px 6px;
  border-radius: 3px;
}

.diff-arrow {
  margin: 0 6px;
  color: #8c8c8c;
}

.diff-lines-wrapper {
  border: 1px solid #d0d7de;
  border-radius: 6px;
  overflow: hidden;
  max-height: 480px;
  display: flex;
  flex-direction: column;
}

.diff-lines-header {
  background: #f6f8fa;
  padding: 8px 12px;
  font-size: 12px;
  font-weight: 600;
  color: #57606a;
  border-bottom: 1px solid #d0d7de;
}

.diff-lines-body {
  overflow-y: auto;
  font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, Courier, monospace;
  font-size: 12px;
  line-height: 20px;
}

.diff-line {
  display: flex;
  align-items: stretch;
}

.diff-line.added {
  background: #e6ffed;
  color: #1a7f37;
}

.diff-line.removed {
  background: #ffeef0;
  color: #cf222e;
}

.diff-line.unchanged {
  background: #ffffff;
  color: #24292f;
}

.diff-line:hover {
  filter: brightness(0.97);
}

.line-no {
  width: 40px;
  padding: 0 6px;
  text-align: right;
  color: #8c8c8c;
  user-select: none;
  background: rgba(0, 0, 0, 0.02);
  border-right: 1px solid #f0f0f0;
  flex-shrink: 0;
}

.line-marker {
  width: 22px;
  text-align: center;
  font-weight: bold;
  user-select: none;
  flex-shrink: 0;
}

.line-text {
  padding: 0 8px;
  white-space: pre-wrap;
  word-break: break-all;
  flex: 1;
}
</style>
