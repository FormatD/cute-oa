<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import type { FlowCopy } from '../api/types'
import { useAnnouncementStore } from '../stores/announcements'
import { useAuthStore } from '../stores/auth'
import { useDashboardStore } from '../stores/dashboard'
import { useExpenseStore } from '../stores/expense'
import { useLeaveStore } from '../stores/leave'
import { usePurchaseStore } from '../stores/purchase'
import { useTravelStore } from '../stores/travel'
import { useWorkflowStore } from '../stores/workflow'

const router = useRouter()
const announcements = useAnnouncementStore()
const auth = useAuthStore()
const dashboard = useDashboardStore()
const expense = useExpenseStore()
const leave = useLeaveStore()
const purchase = usePurchaseStore()
const travel = useTravelStore()
const workflow = useWorkflowStore()
const activeStatuses = new Set(['Draft', 'Approving', 'Rejected', 'Approved'])
const initiatedItems = computed(() => [
  ...leave.initiatedLeaves.filter(item => activeStatuses.has(item.status)).map(item => ({ ...item, module: 'leave' as const, title: `${item.type}请假`, amount: `${item.days} 天` })),
  ...expense.initiatedExpenses.filter(item => activeStatuses.has(item.status)).map(item => ({ ...item, module: 'expense' as const, title: item.description || '费用报销', amount: `¥${item.totalAmount.toFixed(2)}` })),
  ...travel.initiatedTravels.filter(item => activeStatuses.has(item.status)).map(item => ({ ...item, module: 'travel' as const, title: item.purpose, amount: `${item.days} 天 · ¥${item.estimatedBudget.toFixed(2)}` })),
  ...purchase.initiatedPurchases.filter(item => activeStatuses.has(item.status)).map(item => ({ ...item, module: 'purchase' as const, title: item.title, amount: `¥${item.estimatedTotal.toFixed(2)}` }))
].sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? '')).slice(0, 5))
const unreadCopies = computed(() => workflow.unreadCopies.slice(0, 5))
async function openCopy(item: FlowCopy) { await workflow.markCopyRead(item); if (item.readAt) await router.push(`/${item.businessType === 'Leave' ? 'leave' : item.businessType === 'Expense' ? 'expense' : item.businessType === 'Travel' ? 'travel' : 'purchase'}/${item.businessId}`) }

function createLeave() { leave.showForm = true; router.push('/leave') }
function createExpense() { expense.showExpenseForm = true; router.push('/expense') }
function createTravel() { travel.showTravelForm = true; router.push('/travel') }
function createPurchase() { purchase.showPurchaseForm = true; router.push('/purchase') }
onMounted(() => { void announcements.loadPublished(1, 3) })
</script>

<template>
  <template v-if="dashboard.summary">
    <div class="page-heading"><div><p class="eyebrow">DASHBOARD</p><h1>你好，{{ auth.currentUser?.name ?? '同事' }}</h1><p>欢迎回来，以下是今天的工作概览。</p></div><button class="primary-action" @click="createLeave">＋ 发起请假</button></div>
    <section class="cards"><article><span class="card-icon blue">✓</span><div><small>待我审批</small><strong>{{ dashboard.summary.pendingTaskCount }}</strong><em>待处理事项</em></div></article><article><span class="card-icon green">◷</span><div><small>我的年假</small><strong>{{ dashboard.summary.leaveBalance.available }}<sup>天</sup></strong><em>已冻结 {{ dashboard.summary.leaveBalance.frozen }} 天</em></div></article><article><span class="card-icon orange">▤</span><div><small>待我阅读</small><strong>{{ dashboard.summary.pendingReadCount }}</strong><em>未读抄送事项</em></div></article><article class="record-link" @click="router.push('/hr/contracts')"><span class="card-icon orange">▧</span><div><small>合同风险</small><strong>{{ dashboard.summary.contractRiskCount }}</strong><em>待处理或待确认</em></div></article></section>

    <section class="workbench-grid">
      <article class="panel"><div class="section-title"><div><p class="eyebrow">IN PROGRESS</p><h2>我发起的进行中事项</h2></div><button class="secondary" @click="router.push('/leave')">查看请假</button></div><div v-if="initiatedItems.length" class="records"><button v-for="item in initiatedItems" :key="`${item.module}-${item.id}`" class="record record-link" @click="router.push(`/${item.module}/${item.id}`)"><span><strong>{{ item.title }}</strong><span>{{ item.number }} · {{ item.amount }}</span></span><em>{{ item.status }}</em></button></div><p v-else class="empty">暂无进行中的个人事项。</p></article>
      <article class="panel"><div class="section-title"><div><p class="eyebrow">TO READ</p><h2>待我阅读</h2></div><button class="secondary" @click="router.push('/copies')">全部抄送</button></div><div v-if="unreadCopies.length" class="records"><button v-for="item in unreadCopies" :key="item.id" class="record record-link" @click="openCopy(item)"><span><strong>{{ item.title }}</strong><span>{{ item.businessNumber }} · {{ item.applicantName }}</span></span><em>{{ item.availableAt.slice(0, 10) }}</em></button></div><p v-else class="empty">暂无未读抄送事项。</p></article>
    </section>

    <section class="panel workbench-announcements"><div class="section-title"><div><p class="eyebrow">COMPANY NEWS</p><h2>最新公告</h2></div><button class="secondary" @click="router.push('/announcements')">全部公告</button></div><div v-if="announcements.published.length" class="records"><button v-for="item in announcements.published" :key="item.id" class="record record-link" @click="router.push(`/announcements/${item.id}`)"><span><strong>{{ item.title }}</strong><span>{{ item.publishedByName || item.createdByName }} · {{ item.publishedAt?.slice(0, 10) }}</span></span><em :class="{ archived: !item.readAt }">{{ item.readAt ? '已读' : '待确认' }}</em></button></div><p v-else-if="announcements.loading" class="empty">正在加载公告…</p><p v-else class="empty">当前没有有效公告。</p></section>

    <section class="panel"><div class="section-title"><div><p class="eyebrow">QUICK ACTIONS</p><h2>常用入口</h2></div></div><div class="quick-actions"><button @click="createLeave"><span>◫</span>发起请假</button><button @click="createExpense"><span>¥</span>发起报销</button><button @click="createTravel"><span>⌖</span>发起出差</button><button @click="createPurchase"><span>▦</span>发起采购</button><button @click="router.push('/approval')"><span>✓</span>处理审批</button><button @click="router.push('/hr/contracts')"><span>▧</span>劳动合同</button><button @click="router.push('/announcements')"><span>◈</span>查看公告</button><button @click="router.push('/calendar')"><span>▣</span>查看日历</button></div></section>
  </template>
</template>
