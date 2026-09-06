<script setup lang="ts">
import { reactive, watch } from 'vue'
import type { BusinessConfigurationListItem } from '../../api/types'
import OaDialog from '../../components/OaDialog.vue'
import { useBusinessConfigurationStore } from '../../stores/business-configurations'
import { toLocalInput } from './useBusinessConfigEditor'

const props = defineProps<{
  open: boolean
  target: BusinessConfigurationListItem | null
}>()

const emit = defineEmits<{
  close: []
  retired: [id: string]
}>()

const store = useBusinessConfigurationStore()
const retireForm = reactive({
  effectiveTo: ''
})

watch(
  () => props.open,
  isOpen => {
    if (isOpen) {
      retireForm.effectiveTo = toLocalInput(new Date().toISOString())
      store.error = ''
    }
  }
)

async function confirmRetire() {
  if (!props.target) return
  const toIso = retireForm.effectiveTo
    ? new Date(retireForm.effectiveTo).toISOString()
    : new Date().toISOString()
  const result = await store.retire(props.target.id, {
    effectiveTo: toIso,
    concurrencyVersion: props.target.concurrencyVersion
  })
  if (result) {
    emit('retired', props.target.id)
    emit('close')
  }
}
</script>

<template>
  <OaDialog
    :open="open"
    :title="`下线业务配置 (v${target?.version})`"
    :description="target?.name"
    submit-label="确认下线"
    :danger="true"
    :busy="store.saving"
    @close="emit('close')"
    @submit="confirmRetire"
  >
    <p class="operation-warning">
      下线后，新提交的业务单据将不再匹配此版本配置；<strong>已引用此版本的历史业务单据仍保留原规则快照，不受影响</strong>。
    </p>

    <div class="dialog-field" style="margin-top: 12px;">
      下线生效时间
      <input v-model="retireForm.effectiveTo" type="datetime-local" />
      <small>默认为当前时间</small>
    </div>

    <p v-if="store.error" class="dialog-error">{{ store.error }}</p>
  </OaDialog>
</template>
