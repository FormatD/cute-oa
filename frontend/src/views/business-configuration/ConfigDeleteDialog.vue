<script setup lang="ts">
import type { BusinessConfigurationListItem } from '../../api/types'
import OaDialog from '../../components/OaDialog.vue'
import { useBusinessConfigurationStore } from '../../stores/business-configurations'
import { statusLabel } from './useBusinessConfigEditor'

const props = defineProps<{
  open: boolean
  target: BusinessConfigurationListItem | null
}>()

const emit = defineEmits<{
  close: []
  deleted: [id: string]
}>()

const store = useBusinessConfigurationStore()

async function confirmDelete() {
  if (!props.target) return
  if (props.target.status !== 'DRAFT') {
    store.error = '仅草稿状态的配置允许删除。'
    emit('close')
    return
  }
  if (props.target.referenceCount > 0) {
    store.error = `此配置已被历史单据引用（引用数：${props.target.referenceCount}），禁止删除。`
    emit('close')
    return
  }

  const success = await store.deleteConfiguration(props.target.id)
  if (success) {
    emit('deleted', props.target.id)
    emit('close')
  }
}
</script>

<template>
  <OaDialog
    :open="open"
    title="删除配置草稿"
    :description="target?.name"
    :submit-label="target?.referenceCount ? '不可删除' : '确认删除'"
    :danger="true"
    :busy="store.saving"
    @close="emit('close')"
    @submit="confirmDelete"
  >
    <template v-if="target?.referenceCount && target.referenceCount > 0">
      <div class="operation-warning danger">
        ⚠️ <strong>禁止删除：</strong>该配置草稿已被历史单据引用（引用记录数：{{ target.referenceCount }}）。
        为保证业务数据合规性与不可篡改审计追踪，系统已锁定此配置禁止删除。
      </div>
    </template>
    <template v-else-if="target?.status !== 'DRAFT'">
      <div class="operation-warning danger">
        ⚠️ <strong>禁止删除：</strong>当前状态为 {{ statusLabel(target?.status || '') }}，系统仅允许删除未生效的 DRAFT 草稿配置。已生效或已下线配置需保留版本记录。
      </div>
    </template>
    <template v-else>
      <p class="operation-warning">
        确定要彻底删除该配置草稿（v{{ target?.version }}）吗？删除后不可恢复。
      </p>
    </template>
    <p v-if="store.error" class="dialog-error">{{ store.error }}</p>
  </OaDialog>
</template>
