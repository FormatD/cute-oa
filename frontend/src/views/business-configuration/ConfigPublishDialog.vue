<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import type { BusinessConfigurationListItem } from '../../api/types'
import OaDialog from '../../components/OaDialog.vue'
import { useBusinessConfigurationStore } from '../../stores/business-configurations'
import { toLocalInput, validateConfigPublish } from './useBusinessConfigEditor'

const props = defineProps<{
  open: boolean
  target: BusinessConfigurationListItem | null
}>()

const emit = defineEmits<{
  close: []
  published: [id: string]
}>()

const store = useBusinessConfigurationStore()
const validationError = ref('')

const publishForm = reactive({
  publishType: 'immediate' as 'immediate' | 'scheduled',
  effectiveFrom: '',
  effectiveTo: ''
})

watch(
  () => props.open,
  isOpen => {
    if (isOpen && props.target) {
      publishForm.publishType = 'immediate'
      publishForm.effectiveFrom = toLocalInput(props.target.effectiveFrom)
      publishForm.effectiveTo = toLocalInput(props.target.effectiveTo)
      validationError.value = ''
      store.error = ''
    }
  }
)

async function confirmPublish() {
  if (!props.target) return
  validationError.value = ''
  store.error = ''

  const error = validateConfigPublish(
    publishForm.publishType,
    publishForm.effectiveFrom,
    publishForm.effectiveTo
  )
  if (error) {
    validationError.value = error
    return
  }

  const fromIso =
    publishForm.publishType === 'immediate'
      ? new Date().toISOString()
      : new Date(publishForm.effectiveFrom).toISOString()
  const toIso = publishForm.effectiveTo ? new Date(publishForm.effectiveTo).toISOString() : null

  const result = await store.publish(props.target.id, {
    effectiveFrom: fromIso,
    effectiveTo: toIso,
    concurrencyVersion: props.target.concurrencyVersion
  })

  if (result) {
    emit('published', props.target.id)
    emit('close')
  }
}
</script>

<template>
  <OaDialog
    :open="open"
    :title="`发布业务配置 (v${target?.version})`"
    :description="`${target?.name} (${target?.domain} - ${target?.code})`"
    submit-label="确认发布"
    :busy="store.saving"
    @close="emit('close')"
    @submit="confirmPublish"
  >
    <div class="publish-mode-selector">
      <label class="radio-label">
        <input v-model="publishForm.publishType" type="radio" value="immediate" />
        <strong>立即生效</strong>
        <small>从当前时间开始生效，新提交业务单据即刻使用此版本规则。</small>
      </label>
      <label class="radio-label">
        <input v-model="publishForm.publishType" type="radio" value="scheduled" />
        <strong>指定未来时间生效 (SCHEDULED)</strong>
        <small>在指定到达生效时间点前为待生效状态，到达生效时间后自动接管。</small>
      </label>
    </div>

    <div v-if="publishForm.publishType === 'scheduled'" class="dialog-field" style="margin-top: 12px;">
      指定生效时间
      <input v-model="publishForm.effectiveFrom" type="datetime-local" />
    </div>

    <div class="dialog-field" style="margin-top: 12px;">
      指定失效时间 (可选)
      <input v-model="publishForm.effectiveTo" type="datetime-local" />
      <small>留空表示长期有效，到达失效时间后配置自动转为下线。</small>
    </div>

    <div class="operation-warning" style="margin-top: 16px;">
      发布后，该版本状态将锁定为生效/待生效，<strong>不可直接修改</strong>。如需变更规则，须基于此版本升版创建新草稿。
    </div>

    <p v-if="validationError || store.error" class="dialog-error">{{ validationError || store.error }}</p>
  </OaDialog>
</template>
