<script setup lang="ts">
import type { BusinessConfigurationListItem } from '../../api/types'
import {
  domainLabel,
  formatDate,
  formatDateTime,
  statusClass,
  statusLabel
} from './useBusinessConfigEditor'

defineProps<{
  items: BusinessConfigurationListItem[]
  loading: boolean
  page: number
  pageSize: number
  total: number
}>()

const emit = defineEmits<{
  'open-detail': [id: string]
  'open-edit': [item: BusinessConfigurationListItem]
  'open-publish': [item: BusinessConfigurationListItem]
  'create-new-version': [item: BusinessConfigurationListItem]
  'open-retire': [item: BusinessConfigurationListItem]
  'open-history': [item: BusinessConfigurationListItem]
  'open-delete': [item: BusinessConfigurationListItem]
  'page-change': [newPage: number]
}>()
</script>

<template>
  <p v-if="loading" class="empty">正在加载业务配置…</p>
  <template v-else-if="items.length">
    <div class="table-wrap">
      <table class="data-table config-table">
        <thead>
          <tr>
            <th>业务域</th>
            <th>配置标识与名称</th>
            <th>版本</th>
            <th>状态</th>
            <th>生效周期</th>
            <th>引用单据</th>
            <th>更新信息</th>
            <th class="action-cell">操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in items" :key="item.id">
            <td>
              <span class="domain-tag">{{ domainLabel(item.domain) }}</span>
            </td>
            <td>
              <strong>{{ item.name }}</strong>
              <small class="code-badge">{{ item.code }}</small>
              <small v-if="item.description" class="desc-text">{{ item.description }}</small>
            </td>
            <td>
              <span class="version-badge">v{{ item.version }}</span>
            </td>
            <td>
              <span class="status-badge" :class="statusClass(item.status)">
                {{ statusLabel(item.status) }}
              </span>
            </td>
            <td>
              <span>{{ formatDate(item.effectiveFrom) }}</span>
              <small class="expiry-date">至 {{ formatDate(item.effectiveTo) }}</small>
            </td>
            <td>
              <span v-if="item.referenceCount > 0" class="ref-count-tag" title="被历史业务单据引用的数量">
                {{ item.referenceCount }} 笔
              </span>
              <span v-else class="text-muted">无引用</span>
            </td>
            <td>
              <span>{{ item.updatedByName || item.createdByName }}</span>
              <small>{{ formatDateTime(item.updatedAt) }}</small>
            </td>
            <td class="task-actions config-actions">
              <button class="secondary" @click="emit('open-detail', item.id)">详情</button>
              <button v-if="item.status === 'DRAFT'" class="secondary" @click="emit('open-edit', item)">编辑</button>
              <button v-if="item.status === 'DRAFT'" class="primary-btn" @click="emit('open-publish', item)">发布</button>
              <button
                v-if="item.status === 'EFFECTIVE' || item.status === 'SCHEDULED' || item.status === 'RETIRED'"
                class="secondary"
                title="基于此版本克隆出新的草稿升版"
                @click="emit('create-new-version', item)"
              >
                新版本
              </button>
              <button
                v-if="item.status === 'EFFECTIVE' || item.status === 'SCHEDULED'"
                class="warning-outline"
                @click="emit('open-retire', item)"
              >
                下线
              </button>
              <button class="secondary" @click="emit('open-history', item)">历史</button>
              <button
                v-if="item.status === 'DRAFT'"
                class="danger-outline"
                title="删除草稿"
                @click="emit('open-delete', item)"
              >
                删除
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Pagination -->
    <div v-if="total > pageSize" class="table-footer-pagination">
      <span class="page-info">
        共 {{ total }} 条记录 · 当前第 {{ page }} / {{ Math.ceil(total / pageSize) }} 页
      </span>
      <div class="page-buttons">
        <button
          class="secondary"
          :disabled="page <= 1"
          @click="emit('page-change', page - 1)"
        >
          上一页
        </button>
        <button
          class="secondary"
          :disabled="page >= Math.ceil(total / pageSize)"
          @click="emit('page-change', page + 1)"
        >
          下一页
        </button>
      </div>
    </div>
  </template>
  <p v-else class="empty">当前筛选条件下暂无业务配置。</p>
</template>
