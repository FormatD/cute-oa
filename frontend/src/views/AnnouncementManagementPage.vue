<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import type { Announcement } from '../api/types'
import OaDialog from '../components/OaDialog.vue'
import { useAnnouncementStore } from '../stores/announcements'

const announcements = useAnnouncementStore()
const editorOpen = ref(false)
const editingId = ref('')
const validationError = ref('')
const actionTarget = ref<{ item: Announcement; action: 'publish' | 'withdraw' } | null>(null)
const form = reactive({ title: '', content: '', expiresAt: '', version: 1 })

function toLocalInput(value?: string | null) {
  if (!value) return ''
  const date = new Date(value)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}

function openCreate() {
  editingId.value = ''
  Object.assign(form, { title: '', content: '', expiresAt: '', version: 1 })
  validationError.value = ''
  announcements.error = ''
  editorOpen.value = true
}

function openEdit(item: Announcement) {
  editingId.value = item.id
  Object.assign(form, { title: item.title, content: item.content, expiresAt: toLocalInput(item.expiresAt), version: item.version })
  validationError.value = ''
  announcements.error = ''
  editorOpen.value = true
}

async function save() {
  validationError.value = ''
  if (!form.title.trim() || !form.content.trim()) { validationError.value = '请填写公告标题和正文。'; return }
  const payload = { title: form.title.trim(), content: form.content.trim(), expiresAt: form.expiresAt ? new Date(form.expiresAt).toISOString() : null, version: editingId.value ? form.version : null }
  const success = editingId.value ? await announcements.updateAnnouncement(editingId.value, payload) : await announcements.createAnnouncement(payload)
  if (success) editorOpen.value = false
}

async function confirmAction() {
  if (!actionTarget.value) return
  const success = actionTarget.value.action === 'publish' ? await announcements.publishAnnouncement(actionTarget.value.item) : await announcements.withdrawAnnouncement(actionTarget.value.item)
  if (success) actionTarget.value = null
}

function changePage(offset: number) { void announcements.loadAdmin(announcements.adminPage + offset) }
onMounted(() => { announcements.message = ''; void announcements.loadAdmin(1) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">ANNOUNCEMENT ADMIN</p><h1>公告管理</h1><p>公告先保存为草稿，发布后员工可见；已发布内容不可直接覆盖。</p></div><button class="primary-action" @click="openCreate">＋ 新建公告</button></div>
  <p v-if="announcements.message" class="notice success">{{ announcements.message }}</p><p v-if="announcements.error" class="notice error">{{ announcements.error }}</p>
  <section class="panel"><form class="filter-bar" @submit.prevent="announcements.searchAdmin"><select v-model="announcements.adminStatus"><option value="">全部状态</option><option value="DRAFT">草稿</option><option value="PUBLISHED">已发布</option><option value="WITHDRAWN">已撤回</option></select><button type="submit">查询</button><button v-if="announcements.adminStatus" class="secondary" type="button" @click="announcements.adminStatus = ''; announcements.searchAdmin()">重置</button></form><p v-if="announcements.loading" class="empty">正在加载公告…</p><template v-else-if="announcements.adminItems.length"><div class="table-wrap"><table class="data-table announcement-admin-table"><thead><tr><th>公告</th><th>状态</th><th>创建人</th><th>发布时间</th><th>有效期</th><th>版本</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="item in announcements.adminItems" :key="item.id"><td><strong>{{ item.title }}</strong><small>{{ item.content.slice(0, 48) }}{{ item.content.length > 48 ? '…' : '' }}</small></td><td><em :class="{ archived: item.status !== 'PUBLISHED' }">{{ item.status === 'DRAFT' ? '草稿' : item.status === 'PUBLISHED' ? '已发布' : '已撤回' }}</em></td><td>{{ item.createdByName }}</td><td>{{ item.publishedAt ? new Date(item.publishedAt).toLocaleString('zh-CN') : '—' }}</td><td>{{ item.expiresAt ? new Date(item.expiresAt).toLocaleString('zh-CN') : '长期有效' }}</td><td>v{{ item.version }}</td><td class="task-actions"><button v-if="item.status === 'DRAFT'" class="secondary" @click="openEdit(item)">编辑</button><button v-if="item.status === 'DRAFT'" @click="actionTarget = { item, action: 'publish' }">发布</button><button v-if="item.status === 'PUBLISHED'" class="danger-outline" @click="actionTarget = { item, action: 'withdraw' }">撤回</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ announcements.adminTotal }} 条</span><div><button class="secondary" :disabled="announcements.adminPage === 1" @click="changePage(-1)">上一页</button><b>{{ announcements.adminPage }} / {{ announcements.adminTotalPages }}</b><button class="secondary" :disabled="announcements.adminPage === announcements.adminTotalPages" @click="changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前条件下暂无公告。</p></section>

  <OaDialog :open="editorOpen" :title="editingId ? '编辑公告草稿' : '新建公告'" description="发布后正文不可直接修改，如有错误请撤回并新建公告。" submit-label="保存草稿" :busy="announcements.saving" @close="editorOpen = false" @submit="save"><label class="dialog-field">公告标题<input v-model="form.title" maxlength="200" placeholder="请输入简明标题"></label><label class="dialog-field">公告正文<textarea v-model="form.content" maxlength="10000" rows="10" placeholder="请输入公告正文"></textarea><small>{{ form.content.length }} / 10000</small></label><label class="dialog-field">有效期<input v-model="form.expiresAt" type="datetime-local"><small>留空表示长期有效，必须晚于当前时间。</small></label><p v-if="validationError || announcements.error" class="dialog-error">{{ validationError || announcements.error }}</p></OaDialog>
  <OaDialog :open="!!actionTarget" :title="actionTarget?.action === 'publish' ? '发布公告' : '撤回公告'" :description="actionTarget ? actionTarget.item.title : ''" :submit-label="actionTarget?.action === 'publish' ? '确认发布' : '确认撤回'" :danger="actionTarget?.action === 'withdraw'" :busy="announcements.saving" @close="actionTarget = null" @submit="confirmAction"><p class="operation-warning">{{ actionTarget?.action === 'publish' ? '发布后全公司员工可见，正文不能直接修改。' : '撤回后普通员工将无法继续查看该公告。' }}</p></OaDialog>
</template>
