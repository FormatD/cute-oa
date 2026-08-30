<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAnnouncementStore } from '../stores/announcements'

const announcements = useAnnouncementStore()
const router = useRouter()
function changePage(offset: number) { void announcements.loadPublished(announcements.publishedPage + offset) }
onMounted(() => { announcements.message = ''; void announcements.loadPublished(1) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">COMPANY NEWS</p><h1>公司公告</h1><p>查看公司最新通知，并对需要知悉的内容确认已读。</p></div></div>
  <p v-if="announcements.message" class="notice success">{{ announcements.message }}</p><p v-if="announcements.error" class="notice error">{{ announcements.error }}</p>
  <section class="panel"><p v-if="announcements.loading" class="empty">正在加载公告…</p><template v-else-if="announcements.published.length"><div class="table-wrap"><table class="data-table announcement-table"><thead><tr><th>公告标题</th><th>发布人</th><th>发布时间</th><th>有效期</th><th>阅读状态</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="item in announcements.published" :key="item.id"><td><strong>{{ item.title }}</strong><small>{{ item.content.slice(0, 60) }}{{ item.content.length > 60 ? '…' : '' }}</small></td><td>{{ item.publishedByName || item.createdByName }}</td><td>{{ item.publishedAt ? new Date(item.publishedAt).toLocaleString('zh-CN') : '—' }}</td><td>{{ item.expiresAt ? new Date(item.expiresAt).toLocaleString('zh-CN') : '长期有效' }}</td><td><em :class="{ archived: !item.readAt }">{{ item.readAt ? '已读' : '待确认' }}</em></td><td class="task-actions"><button class="secondary" @click="router.push(`/announcements/${item.id}`)">查看详情</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ announcements.publishedTotal }} 条</span><div><button class="secondary" :disabled="announcements.publishedPage === 1" @click="changePage(-1)">上一页</button><b>{{ announcements.publishedPage }} / {{ announcements.publishedTotalPages }}</b><button class="secondary" :disabled="announcements.publishedPage === announcements.publishedTotalPages" @click="changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前没有有效公告。</p></section>
</template>
