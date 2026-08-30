<script setup lang="ts">
import { onMounted } from 'vue'
import { useAuditStore } from '../stores/audit'
import { useWorkspaceStore } from '../stores/workspace'

const audit = useAuditStore()
const workspace = useWorkspaceStore()

onMounted(audit.load)
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">AUDIT LOG</p><h1>审计日志</h1><p>追溯系统内关键业务和文件操作。</p></div></div>
  <section class="panel"><form class="filter-bar" @submit.prevent="audit.search"><input v-model="audit.filters.keyword" placeholder="动作、摘要或资源 ID"><select v-model="audit.filters.actorId"><option value="">全部操作人</option><option v-for="employee in workspace.employees" :key="employee.id" :value="employee.id">{{ employee.name }}</option></select><select v-model="audit.filters.resourceType"><option value="">全部资源</option><option>LeaveRequest</option><option>ExpenseClaim</option><option>TravelRequest</option><option>PersonnelProfile</option><option>AttendanceShift</option><option>AttendanceRecord</option><option>AttendanceAppeal</option><option>EmploymentContract</option><option>File</option><option>WorkCalendar</option></select><label>开始<input v-model="audit.filters.startDate" type="date"></label><label>结束<input v-model="audit.filters.endDate" type="date"></label><button type="submit">查询</button><button class="secondary" type="button" @click="audit.resetFilters">重置</button></form>
    <p v-if="audit.error" class="notice error">{{ audit.error }}</p><p v-if="audit.loading" class="empty">正在加载审计日志…</p>
    <template v-else-if="audit.items.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>时间</th><th>操作人</th><th>动作</th><th>资源</th><th>摘要</th></tr></thead><tbody><tr v-for="item in audit.items" :key="item.id"><td>{{ new Date(item.occurredAt).toLocaleString('zh-CN') }}</td><td>{{ item.actorName }}</td><td><code>{{ item.action }}</code></td><td>{{ item.resourceType }}<small class="resource-id">{{ item.resourceId }}</small></td><td>{{ item.summary }}</td></tr></tbody></table></div><div class="pagination"><span>共 {{ audit.total }} 条</span><div><button class="secondary" :disabled="audit.page === 1" @click="audit.changePage(-1)">上一页</button><b>{{ audit.page }} / {{ audit.totalPages }}</b><button class="secondary" :disabled="audit.page === audit.totalPages" @click="audit.changePage(1)">下一页</button></div></div></template><p v-else-if="!audit.error" class="empty">当前条件下暂无审计记录。</p>
  </section>
</template>
