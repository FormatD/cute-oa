<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useWorkflowStore } from '../stores/workflow'
import type { FlowCopy } from '../api/types'

const workflow = useWorkflowStore()
const router = useRouter()
function changePage(offset: number) { workflow.copyPage += offset; void workflow.loadCopies() }
async function open(item: FlowCopy) {
  await workflow.markCopyRead(item)
  if (!item.readAt) return
  await router.push(`/${item.businessType === 'Leave' ? 'leave' : item.businessType === 'Expense' ? 'expense' : item.businessType === 'Travel' ? 'travel' : 'purchase'}/${item.businessId}`)
}
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">COPIED TO ME</p><h1>待我阅读</h1><p>查看流程完成或撤回后抄送给我的业务事项。</p></div></div>
  <section class="panel">
    <div class="section-title"><div><p class="eyebrow">COPY RECORDS</p><h2>抄送事项</h2></div></div>
    <template v-if="workflow.pagedCopies.items.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>状态</th><th>业务单号</th><th>标题</th><th>发起人</th><th>流程结果</th><th>抄送时间</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="item in workflow.pagedCopies.items" :key="item.id"><td><em :class="{ archived: item.readAt }">{{ item.readAt ? '已读' : '未读' }}</em></td><td>{{ item.businessNumber }}</td><td>{{ item.title }}</td><td>{{ item.applicantName }}</td><td>{{ item.status }}</td><td>{{ new Date(item.availableAt).toLocaleString('zh-CN') }}</td><td class="task-actions"><button @click="open(item)">{{ item.readAt ? '查看详情' : '阅读' }}</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ workflow.pagedCopies.total }} 条</span><div><button class="secondary" :disabled="workflow.pagedCopies.currentPage === 1" @click="changePage(-1)">上一页</button><b>{{ workflow.pagedCopies.currentPage }} / {{ workflow.pagedCopies.totalPages }}</b><button class="secondary" :disabled="workflow.pagedCopies.currentPage === workflow.pagedCopies.totalPages" @click="changePage(1)">下一页</button></div></div></template>
    <p v-else class="empty">暂无抄送给我的已完成或已撤回事项。</p>
  </section>
</template>
