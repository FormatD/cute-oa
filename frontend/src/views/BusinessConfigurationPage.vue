<script setup lang="ts">
import { onMounted, ref } from 'vue'
import type {
  BusinessConfigurationListItem,
  BusinessConfigurationRecord,
  ConfigurationDomain,
  ConfigurationVersionSummary
} from '../api/types'
import { useBusinessConfigurationStore } from '../stores/business-configurations'
import { useOrganizationStore } from '../stores/organization'
import ConfigDeleteDialog from './business-configuration/ConfigDeleteDialog.vue'
import ConfigDetailDialog from './business-configuration/ConfigDetailDialog.vue'
import ConfigEditorDialog from './business-configuration/ConfigEditorDialog.vue'
import ConfigHistoryDialog from './business-configuration/ConfigHistoryDialog.vue'
import ConfigListTable from './business-configuration/ConfigListTable.vue'
import ConfigPublishDialog from './business-configuration/ConfigPublishDialog.vue'
import ConfigRetireDialog from './business-configuration/ConfigRetireDialog.vue'
import './business-configuration/business-configuration.css'

const store = useBusinessConfigurationStore()
const organization = useOrganizationStore()

// Domain tabs
const domainTabs: { key: string; label: string }[] = [
  { key: '', label: '全部域' },
  { key: 'Leave', label: '休假规则' },
  { key: 'Expense', label: '费用报销' },
  { key: 'Travel', label: '差旅标准' },
  { key: 'Procurement', label: '采购限额' },
  { key: 'Seal', label: '用印规则' },
  { key: 'Dictionary', label: '业务字典' }
]

function selectDomainTab(domain: string) {
  store.domainFilter = domain
  void store.search()
}

// Editor dialog state
const editorOpen = ref(false)
const editingId = ref<string | null>(null)
const editingVersion = ref(1)
const editingConcurrencyVersion = ref(0)
const editingRecord = ref<BusinessConfigurationRecord | null>(null)

function openCreate() {
  editingId.value = null
  editingVersion.value = 1
  editingConcurrencyVersion.value = 0
  editingRecord.value = null
  store.error = ''
  store.message = ''
  editorOpen.value = true
}

async function openEdit(item: BusinessConfigurationListItem | BusinessConfigurationRecord) {
  store.error = ''
  store.message = ''
  const fullRecord = await store.loadDetail(item.id)
  if (!fullRecord) return

  editingId.value = fullRecord.id
  editingVersion.value = fullRecord.version
  editingConcurrencyVersion.value = fullRecord.concurrencyVersion
  editingRecord.value = fullRecord
  editorOpen.value = true
}

// Detail dialog state
const detailOpen = ref(false)
const detailRecord = ref<BusinessConfigurationRecord | null>(null)

async function openDetail(id: string) {
  const item = await store.loadDetail(id)
  if (item) {
    detailRecord.value = item
    detailOpen.value = true
  }
}

// Publish dialog state
const publishOpen = ref(false)
const publishTarget = ref<BusinessConfigurationListItem | null>(null)

function openPublish(item: BusinessConfigurationListItem) {
  publishTarget.value = item
  publishOpen.value = true
}

// Retire dialog state
const retireOpen = ref(false)
const retireTarget = ref<BusinessConfigurationListItem | null>(null)

function openRetire(item: BusinessConfigurationListItem) {
  retireTarget.value = item
  retireOpen.value = true
}

// Delete dialog state
const deleteOpen = ref(false)
const deleteTarget = ref<BusinessConfigurationListItem | null>(null)

function openDelete(item: BusinessConfigurationListItem) {
  deleteTarget.value = item
  deleteOpen.value = true
}

// History dialog state
const historyOpen = ref(false)
const historyTarget = ref<BusinessConfigurationListItem | null>(null)

function openHistory(item: BusinessConfigurationListItem) {
  historyTarget.value = item
  historyOpen.value = true
}

async function handleBranchFromHistory(ver: ConfigurationVersionSummary) {
  historyOpen.value = false
  store.error = ''
  store.message = ''
  const newRecord = await store.createNewVersion(ver.id)
  if (newRecord) {
    await openEdit(newRecord)
  }
}

async function handleCreateNewVersion(item: BusinessConfigurationListItem) {
  store.error = ''
  store.message = ''
  const newRecord = await store.createNewVersion(item.id)
  if (newRecord) {
    await openEdit(newRecord)
  }
}

// Lifecycle
onMounted(() => {
  store.message = ''
  store.error = ''
  void store.loadList(1)
  if (!organization.organizationLoaded) {
    void organization.loadOrganization()
  }
})
</script>

<template>
  <div class="page-heading">
    <div>
      <p class="eyebrow">BUSINESS CONFIGURATION</p>
      <h1>业务参数配置中心</h1>
      <p>集中管理各业务域（休假、报销、差旅、采购、印章、字典）生效规则与版本历史。已生效配置不可直接篡改，历史单据保留快照审计追溯。</p>
    </div>
    <button class="primary-action" @click="openCreate">＋ 新建配置草稿</button>
  </div>

  <p v-if="store.message" class="notice success">{{ store.message }}</p>
  <p v-if="store.error" class="notice error">{{ store.error }}</p>

  <!-- Domain Navigation Tabs -->
  <div class="domain-tabs-nav">
    <button
      v-for="tab in domainTabs"
      :key="tab.key"
      class="domain-tab-btn"
      :class="{ active: store.domainFilter === tab.key }"
      @click="selectDomainTab(tab.key)"
    >
      {{ tab.label }}
    </button>
  </div>

  <section class="panel">
    <!-- Filter Bar -->
    <form class="filter-bar" @submit.prevent="store.search">
      <label>
        状态
        <select v-model="store.statusFilter">
          <option value="">全部状态</option>
          <option value="EFFECTIVE">生效中</option>
          <option value="SCHEDULED">待生效</option>
          <option value="DRAFT">草稿</option>
          <option value="RETIRED">已下线</option>
        </select>
      </label>

      <label>
        关键字搜索
        <input v-model="store.keyword" placeholder="搜索配置标识 / 名称 / 描述" />
      </label>

      <button type="submit">查询</button>
      <button
        v-if="store.domainFilter || store.statusFilter || store.keyword"
        class="secondary"
        type="button"
        @click="store.resetFilters"
      >
        重置
      </button>
    </form>

    <!-- Table Component -->
    <ConfigListTable
      :items="store.items"
      :loading="store.loading"
      :page="store.page"
      :page-size="store.pageSize"
      :total="store.total"
      @open-detail="openDetail"
      @open-edit="openEdit"
      @open-publish="openPublish"
      @create-new-version="handleCreateNewVersion"
      @open-retire="openRetire"
      @open-history="openHistory"
      @open-delete="openDelete"
      @page-change="store.loadList"
    />
  </section>

  <!-- Modals & Drawers -->
  <ConfigEditorDialog
    :open="editorOpen"
    :editing-id="editingId"
    :editing-version="editingVersion"
    :editing-concurrency-version="editingConcurrencyVersion"
    :initial-record="editingRecord"
    :default-domain="(store.domainFilter as ConfigurationDomain) || 'Leave'"
    @close="editorOpen = false"
  />

  <ConfigDetailDialog
    :open="detailOpen"
    :record="detailRecord"
    @close="detailOpen = false"
  />

  <ConfigPublishDialog
    :open="publishOpen"
    :target="publishTarget"
    @close="publishOpen = false"
  />

  <ConfigRetireDialog
    :open="retireOpen"
    :target="retireTarget"
    @close="retireOpen = false"
  />

  <ConfigDeleteDialog
    :open="deleteOpen"
    :target="deleteTarget"
    @close="deleteOpen = false"
  />

  <ConfigHistoryDialog
    :open="historyOpen"
    :target="historyTarget"
    @close="historyOpen = false"
    @open-detail="openDetail"
    @branch="handleBranchFromHistory"
  />
</template>
