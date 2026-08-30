<script setup lang="ts">
import { onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAnnouncementStore } from '../stores/announcements'

const route = useRoute()
const router = useRouter()
const announcements = useAnnouncementStore()
function load() { void announcements.loadDetail(String(route.params.id)) }
onMounted(load)
watch(() => route.params.id, load)
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">ANNOUNCEMENT DETAIL</p><h1>公告详情</h1><p>发布内容与阅读确认记录。</p></div><button class="secondary" @click="router.push('/announcements')">返回公告列表</button></div>
  <p v-if="announcements.message" class="notice success">{{ announcements.message }}</p><p v-if="announcements.error" class="notice error">{{ announcements.error }}</p>
  <p v-if="announcements.loading" class="empty">正在加载公告…</p>
  <article v-else-if="announcements.detail" class="panel announcement-detail"><header><div><span class="status-chip">{{ announcements.detail.status === 'PUBLISHED' ? '已发布' : announcements.detail.status }}</span><h2>{{ announcements.detail.title }}</h2></div><button v-if="!announcements.detail.readAt && announcements.detail.status === 'PUBLISHED'" :disabled="announcements.saving" @click="announcements.markRead(announcements.detail.id)">{{ announcements.saving ? '提交中…' : '确认已读' }}</button><em v-else-if="announcements.detail.readAt">已于 {{ new Date(announcements.detail.readAt).toLocaleString('zh-CN') }} 确认阅读</em></header><div class="announcement-meta"><span>发布人：{{ announcements.detail.publishedByName || announcements.detail.createdByName }}</span><span>发布时间：{{ announcements.detail.publishedAt ? new Date(announcements.detail.publishedAt).toLocaleString('zh-CN') : '—' }}</span><span>有效期：{{ announcements.detail.expiresAt ? new Date(announcements.detail.expiresAt).toLocaleString('zh-CN') : '长期有效' }}</span></div><div class="announcement-content">{{ announcements.detail.content }}</div></article>
</template>
