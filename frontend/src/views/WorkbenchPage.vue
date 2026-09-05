<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import type { WorkItem, WorkItemTab } from '../api/types'
import { useAnnouncementStore } from '../stores/announcements'
import { useAuthStore } from '../stores/auth'
import { useDashboardStore } from '../stores/dashboard'
import { useExpenseStore } from '../stores/expense'
import { useLeaveStore } from '../stores/leave'
import { usePurchaseStore } from '../stores/purchase'
import { useSealStore } from '../stores/seal'
import { useTravelStore } from '../stores/travel'
import { useWorkItemStore } from '../stores/work-items'

const router = useRouter()
const announcements = useAnnouncementStore()
const auth = useAuthStore()
const dashboard = useDashboardStore()
const expense = useExpenseStore()
const leave = useLeaveStore()
const purchase = usePurchaseStore()
const seal = useSealStore()
const travel = useTravelStore()
const workItems = useWorkItemStore()

const roleHeadline = computed(() => {
  if (workItems.summary.pendingFinanceCount) return '费用审批与待付款事项已按您的财务权限汇总。'
  if (workItems.summary.pendingAttendanceCount || workItems.summary.pendingPersonnelCount) return '人员与运营事项已按您的管理范围汇总。'
  if (workItems.summary.pendingApprovalCount) return '团队审批与个人事项已集中汇总。'
  return '以下是您的个人事项、待阅信息和常用入口。'
})
const pendingDetail = computed(() => [
  workItems.summary.pendingApprovalCount ? `${workItems.summary.pendingApprovalCount} 审批` : '',
  workItems.summary.pendingPersonnelCount ? `${workItems.summary.pendingPersonnelCount} 人事` : '',
  workItems.summary.pendingAttendanceCount ? `${workItems.summary.pendingAttendanceCount} 考勤` : '',
  workItems.summary.pendingFinanceCount ? `${workItems.summary.pendingFinanceCount} 付款` : ''
].filter(Boolean).join(' · ') || '暂无待处理')

function createLeave() { leave.showForm = true; void router.push('/leave') }
function createExpense() { expense.showExpenseForm = true; void router.push('/expense') }
function createTravel() { travel.showTravelForm = true; void router.push('/travel') }
function createPurchase() { purchase.showPurchaseForm = true; void router.push('/purchase') }
function createSeal() { seal.showSealForm = true; void router.push('/seal') }
async function openItem(item: WorkItem) {
  if (item.category === 'COPY' && !await workItems.markCopyRead(item)) return
  await router.push(item.route)
}
async function openCenter(tab: WorkItemTab) { workItems.activeTab = tab; await router.push('/approval') }
function businessLabel(value: string) { return ({ leave: '请假', expense: '报销', travel: '出差', purchase: '采购', seal: '用章', personnel: '人事办理', attendance: '考勤申诉', announcement: '公告', document: '制度', contract: '合同' } as Record<string, string>)[value] ?? value }
function actionLabel(item: WorkItem) { return item.actionType === 'COMPLETE' ? '去办理' : item.actionType === 'REVIEW' ? '去审核' : item.actionType === 'PAYMENT' ? '去付款' : item.actionType === 'ACKNOWLEDGE' ? '去签收' : item.actionType === 'READ' ? '阅读' : '查看' }
onMounted(() => { void announcements.loadPublished(1, 3); if (!workItems.workbenchLoaded) void workItems.loadWorkbench() })
</script>

<template>
  <template v-if="dashboard.summary">
    <div class="page-heading"><div><p class="eyebrow">ROLE WORKBENCH</p><h1>你好，{{ auth.currentUser?.name ?? '同事' }}</h1><p>{{ roleHeadline }}</p></div><button class="primary-action" @click="createLeave">＋ 发起请假</button></div>

    <aside v-if="workItems.workbenchReading.some(item => item.category === 'DOCUMENT')" class="notice warning record-link" style="cursor: pointer; margin-bottom: 20px;" @click="openCenter('reading')">⚠ 您有必须阅读或签收的制度文件，点击进入事项中心处理 →</aside>

    <section class="cards">
      <article class="record-link" @click="openCenter('pending')"><span class="card-icon blue">✓</span><div><small>待我处理</small><strong>{{ workItems.summary.pendingCount }}</strong><em>{{ pendingDetail }}</em></div></article>
      <article><span class="card-icon green">◷</span><div><small>我的年假</small><strong>{{ dashboard.summary.leaveBalance.available }}<sup>天</sup></strong><em>已冻结 {{ dashboard.summary.leaveBalance.frozen }} 天</em></div></article>
      <article class="record-link" @click="openCenter('reading')"><span class="card-icon orange">▤</span><div><small>待我阅读</small><strong>{{ workItems.summary.pendingReadCount }}</strong><em>公告、制度与审批抄送</em></div></article>
      <article class="record-link" @click="openCenter('risk')"><span class="card-icon orange">▧</span><div><small>风险提醒</small><strong>{{ workItems.summary.riskCount }}</strong><em>按权限范围展示</em></div></article>
    </section>

    <section class="workbench-grid">
      <article class="panel"><div class="section-title"><div><p class="eyebrow">TO DO</p><h2>待我处理</h2></div><button class="secondary" @click="openCenter('pending')">全部待办</button></div><div v-if="workItems.workbenchPending.length" class="records"><button v-for="item in workItems.workbenchPending" :key="item.id" class="record record-link" @click="openItem(item)"><span><strong>{{ item.title }}</strong><span>{{ businessLabel(item.businessType) }} · {{ item.applicantName }} · {{ item.currentNode }}</span></span><em :class="{ archived: item.urgency === 'OVERDUE' }">{{ actionLabel(item) }}</em></button></div><p v-else class="empty">当前没有待处理事项。</p></article>
      <article class="panel"><div class="section-title"><div><p class="eyebrow">INITIATED</p><h2>我发起的</h2></div><button class="secondary" @click="openCenter('initiated')">全部发起</button></div><div v-if="workItems.workbenchInitiated.length" class="records"><button v-for="item in workItems.workbenchInitiated" :key="item.id" class="record record-link" @click="openItem(item)"><span><strong>{{ item.title }}</strong><span>{{ item.number }} · {{ businessLabel(item.businessType) }}</span></span><em>{{ item.status }}</em></button></div><p v-else class="empty">暂无个人发起事项。</p></article>
    </section>

    <section class="panel"><div class="section-title"><div><p class="eyebrow">TO READ</p><h2>待我阅读</h2></div><button class="secondary" @click="openCenter('reading')">全部待阅</button></div><div v-if="workItems.workbenchReading.length" class="records"><button v-for="item in workItems.workbenchReading" :key="item.id" class="record record-link" @click="openItem(item)"><span><strong>{{ item.title }}</strong><span>{{ businessLabel(item.businessType) }} · {{ item.applicantName || '公司发布' }}</span></span><em>{{ actionLabel(item) }}</em></button></div><p v-else class="empty">暂无未读事项。</p></section>

    <section class="panel workbench-announcements"><div class="section-title"><div><p class="eyebrow">COMPANY NEWS</p><h2>最新公告</h2></div><button class="secondary" @click="router.push('/announcements')">全部公告</button></div><div v-if="announcements.published.length" class="records"><button v-for="item in announcements.published" :key="item.id" class="record record-link" @click="router.push(`/announcements/${item.id}`)"><span><strong>{{ item.title }}</strong><span>{{ item.publishedByName || item.createdByName }} · {{ item.publishedAt?.slice(0, 10) }}</span></span><em :class="{ archived: !item.readAt }">{{ item.readAt ? '已读' : '待确认' }}</em></button></div><p v-else-if="announcements.loading" class="empty">正在加载公告…</p><p v-else class="empty">当前没有有效公告。</p></section>

    <section class="panel"><div class="section-title"><div><p class="eyebrow">QUICK ACTIONS</p><h2>常用入口</h2></div></div><div class="quick-actions"><button @click="createLeave"><span>◫</span>发起请假</button><button @click="createExpense"><span>¥</span>发起报销</button><button @click="createTravel"><span>⌖</span>发起出差</button><button @click="createPurchase"><span>▦</span>发起采购</button><button @click="createSeal"><span>印</span>发起用章</button><button @click="openCenter('pending')"><span>✓</span>处理事项</button><button @click="router.push('/hr/contracts')"><span>▧</span>劳动合同</button><button @click="router.push('/documents')"><span>📖</span>制度知识库</button><button @click="router.push('/announcements')"><span>◈</span>查看公告</button><button @click="router.push('/calendar')"><span>▣</span>查看日历</button></div></section>
  </template>
</template>
