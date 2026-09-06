<script setup lang="ts">
import { watch } from 'vue'
import type { BusinessConfigurationListItem, ConfigurationVersionSummary } from '../../api/types'
import OaDialog from '../../components/OaDialog.vue'
import { useBusinessConfigurationStore } from '../../stores/business-configurations'
import { formatDate, formatDateTime, statusClass, statusLabel } from './useBusinessConfigEditor'

const props = defineProps<{
  open: boolean
  target: BusinessConfigurationListItem | null
}>()

const emit = defineEmits<{
  close: []
  'open-detail': [id: string]
  branch: [ver: ConfigurationVersionSummary]
}>()

const store = useBusinessConfigurationStore()

watch(
  () => props.open,
  isOpen => {
    if (isOpen && props.target) {
      void store.loadVersions(props.target.id)
    }
  }
)
</script>

<template>
  <OaDialog
    :open="open"
    :title="`版本历史 - ${target?.name}`"
    :description="`${target?.domain} · ${target?.code}`"
    submit-label="关闭"
    width="840px"
    @close="emit('close')"
    @submit="emit('close')"
  >
    <div v-if="store.versions.length" class="table-wrap compact">
      <table class="data-table">
        <thead>
          <tr>
            <th>版本号</th>
            <th>状态</th>
            <th>生效周期</th>
            <th>发布人 / 发布时间</th>
            <th>单据引用</th>
            <th class="action-cell">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="ver in store.versions" :key="ver.id">
            <td><strong>v{{ ver.version }}</strong></td>
            <td>
              <span class="status-badge" :class="statusClass(ver.status)">
                {{ statusLabel(ver.status) }}
              </span>
            </td>
            <td>
              <span>{{ formatDate(ver.effectiveFrom) }}</span>
              <small class="expiry-date">至 {{ formatDate(ver.effectiveTo) }}</small>
            </td>
            <td>
              <span>{{ ver.publishedByName || '—' }}</span>
              <small>{{ formatDateTime(ver.publishedAt) }}</small>
            </td>
            <td>
              <span v-if="ver.referenceCount > 0" class="ref-count-tag">{{ ver.referenceCount }} 笔</span>
              <span v-else class="text-muted">无引用</span>
            </td>
            <td class="task-actions">
              <button class="secondary" @click="emit('open-detail', ver.id)">查看参数</button>
              <button class="primary-btn" @click="emit('branch', ver)">基于此版本升版</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <p v-else class="empty">暂无历史版本记录。</p>
  </OaDialog>
</template>
