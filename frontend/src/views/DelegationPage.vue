<script setup lang="ts">
import { computed, onMounted } from 'vue'
import type { FlowDelegation } from '../api/types'
import { useAuthStore } from '../stores/auth'
import { useDelegationStore } from '../stores/delegation'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'

const auth = useAuthStore()
const delegation = useDelegationStore()
const employeeDirectory = useEmployeeDirectoryStore()
const candidates = computed(() => employeeDirectory.employees.filter(employee => employee.id !== auth.currentUserId))
const businessLabel = (type: string) => ({ All: '全部业务', Leave: '请假', Expense: '报销', Travel: '出差', Purchase: '采购' }[type] ?? type)
const statusLabel = (item: FlowDelegation) => item.status === 1 ? '已取消' : item.isEffective ? '生效中' : new Date(item.endAt) <= new Date() ? '已到期' : '待生效'

onMounted(delegation.load)
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">APPROVAL DELEGATION</p><h1>审批委托</h1><p>按时间范围和业务类型，将新产生的审批待办交由同事代办。</p></div></div>
  <p v-if="delegation.message" class="notice success">{{ delegation.message }}</p><p v-if="delegation.error" class="notice error">{{ delegation.error }}</p>
  <section class="panel"><div class="section-title"><div><p class="eyebrow">NEW RULE</p><h2>新建委托</h2></div></div><form class="delegation-form" @submit.prevent="delegation.submit"><label>代办人<select v-model="delegation.form.delegateId"><option value="">请选择</option><option v-for="employee in candidates" :key="employee.id" :value="employee.id">{{ employee.name }} · {{ employee.role }}</option></select></label><label>业务范围<select v-model="delegation.form.businessType"><option value="All">全部业务</option><option value="Leave">请假</option><option value="Expense">报销</option><option value="Travel">出差</option><option value="Purchase">采购</option></select></label><label>开始时间<input v-model="delegation.form.startAt" type="datetime-local"></label><label>结束时间<input v-model="delegation.form.endAt" type="datetime-local"></label><label class="wide">委托原因<textarea v-model="delegation.form.reason" maxlength="200" placeholder="例如：出差期间由同事代办审批"></textarea></label><div class="form-actions wide"><button :disabled="delegation.saving" type="submit">{{ delegation.saving ? '保存中…' : '创建委托' }}</button></div></form></section>
  <section class="panel"><div class="section-title"><div><p class="eyebrow">MY RULES</p><h2>我的委托记录</h2></div></div><p v-if="delegation.loading" class="empty">正在加载委托记录…</p><template v-else-if="delegation.paged.items.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>代办人</th><th>业务范围</th><th>有效期</th><th>原因</th><th>状态</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="item in delegation.paged.items" :key="item.id"><td>{{ item.delegateName }}</td><td>{{ businessLabel(item.businessType) }}</td><td>{{ new Date(item.startAt).toLocaleString('zh-CN') }}<small class="delegation-end">至 {{ new Date(item.endAt).toLocaleString('zh-CN') }}</small></td><td>{{ item.reason }}</td><td><em :class="{ archived: item.status === 1 || !item.isEffective }">{{ statusLabel(item) }}</em></td><td class="task-actions"><button v-if="item.status === 0" class="secondary" :disabled="delegation.saving" @click="delegation.cancel(item)">取消委托</button><span v-else class="muted">—</span></td></tr></tbody></table></div><div class="pagination"><span>共 {{ delegation.paged.total }} 条</span><div><button :disabled="delegation.paged.currentPage === 1" @click="delegation.page--">上一页</button><b>{{ delegation.paged.currentPage }} / {{ delegation.paged.totalPages }}</b><button :disabled="delegation.paged.currentPage === delegation.paged.totalPages" @click="delegation.page++">下一页</button></div></div></template><p v-else class="empty">暂无审批委托记录。</p></section>
</template>
