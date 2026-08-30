<script setup lang="ts">
import { computed } from 'vue'
import type { FlowInstance } from '../api/types'

const props = defineProps<{ instances?: FlowInstance[]; currentId?: string | null }>()
const ordered = computed(() => [...(props.instances ?? [])].sort((a, b) => b.attempt - a.attempt))
const statusLabel = (status: number) => ['审批中', '已完成', '已驳回', '已撤回'][status] ?? '未知'
const actionLabel = (action: number) => ['提交审批', '审批通过', '审批驳回', '审批转办', '申请撤回'][action] ?? '流程动作'
const actionDetail = (action: FlowInstance['actions'][number]) => action.action === 3
  ? `${action.fromAssigneeName ?? '原审批人'} → ${action.toAssigneeName ?? '新审批人'}`
  : action.sequence ? `第 ${action.sequence} 节点` : ''
</script>

<template>
  <section v-if="ordered.length" class="flow-history">
    <h3>流程实例与永久轨迹</h3>
    <article v-for="instance in ordered" :key="instance.id" class="flow-instance">
      <header>
        <div><strong>第 {{ instance.attempt }} 次提交</strong><small>{{ instance.processDefinitionCode }} v{{ instance.processDefinitionVersion }}</small></div>
        <div><span v-if="instance.id === currentId" class="current-instance">当前</span><em :class="`flow-status status-${instance.status}`">{{ statusLabel(instance.status) }}</em></div>
      </header>
      <ol>
        <li v-for="action in instance.actions" :key="action.id">
          <span class="action-dot"></span>
          <div><strong>{{ actionLabel(action.action) }}</strong><span>{{ action.actorName }}<template v-if="actionDetail(action)"> · {{ actionDetail(action) }}</template></span><small>{{ new Date(action.occurredAt).toLocaleString('zh-CN') }}<template v-if="action.comment"> · {{ action.comment }}</template></small></div>
        </li>
      </ol>
    </article>
  </section>
</template>
