<script setup lang="ts">
import { onMounted, ref } from 'vue'
import OaDialog from '../components/OaDialog.vue'
import type { AuthSession } from '../api/types'
import { useSecurityStore } from '../stores/security'

const security = useSecurityStore()
const target = ref<AuthSession | null>(null)
const revokeOthersOpen = ref(false)
const statusLabels = { ACTIVE: '有效', EXPIRED: '已过期', REVOKED: '已退出' }

function changePage(offset: number) { security.page += offset; void security.loadSessions() }
async function confirmSingle() { if (target.value && await security.revokeSession(target.value.id)) target.value = null }
async function confirmOthers() { if (await security.revokeOthers()) revokeOthersOpen.value = false }

onMounted(() => { void Promise.all([security.loadSessions(), security.loadMfaStatus()]) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">ACCOUNT SECURITY</p><h1>登录设备</h1><p>查看当前账号的登录会话，并让不再使用的设备退出登录。</p></div><button class="primary-action" :disabled="security.saving" @click="revokeOthersOpen = true">退出其他设备</button></div>
  <p v-if="security.message" class="notice success">{{ security.message }}</p><p v-if="security.error" class="notice error">{{ security.error }}</p>
  <section v-if="security.mfaStatus" class="panel mfa-status-panel">
    <div><p class="eyebrow">MULTI-FACTOR AUTHENTICATION</p><h2>多因素认证</h2><p>{{ security.mfaStatus.enabled ? '身份验证器已绑定。登录时需要动态码或一次性恢复码。' : security.mfaStatus.required ? '该账号具有敏感权限，下次登录必须完成身份验证器绑定。' : '当前账号暂不强制多因素认证。' }}</p></div>
    <div class="mfa-status-values"><em :class="{ archived: !security.mfaStatus.enabled }">{{ security.mfaStatus.enabled ? '已启用' : '未启用' }}</em><span v-if="security.mfaStatus.enabled">剩余恢复码 {{ security.mfaStatus.recoveryCodesRemaining }} 个</span><small v-if="security.mfaStatus.enabledAt">启用时间：{{ new Date(security.mfaStatus.enabledAt).toLocaleString('zh-CN') }}</small></div>
  </section>
  <section class="panel">
    <p v-if="security.loading" class="empty">正在加载登录设备…</p>
    <template v-else-if="security.sessions.length"><div class="table-wrap"><table class="data-table session-table"><thead><tr><th>设备</th><th>网络地址</th><th>最近使用</th><th>登录时间</th><th>有效期</th><th>状态</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="session in security.sessions" :key="session.id"><td><strong>{{ session.device }}</strong><small v-if="session.isCurrent">当前设备</small></td><td>{{ session.ipAddress || '未记录' }}</td><td>{{ new Date(session.lastUsedAt).toLocaleString('zh-CN') }}</td><td>{{ new Date(session.createdAt).toLocaleString('zh-CN') }}</td><td>{{ new Date(session.expiresAt).toLocaleDateString('zh-CN') }}</td><td><em :class="{ archived: session.status !== 'ACTIVE' }">{{ statusLabels[session.status] }}</em></td><td class="task-actions"><button v-if="session.status === 'ACTIVE' && !session.isCurrent" class="secondary" @click="target = session">退出设备</button><span v-else class="muted">—</span></td></tr></tbody></table></div><div class="pagination"><span>共 {{ security.total }} 条</span><div><button class="secondary" :disabled="security.page === 1" @click="changePage(-1)">上一页</button><b>{{ security.page }} / {{ security.totalPages }}</b><button class="secondary" :disabled="security.page === security.totalPages" @click="changePage(1)">下一页</button></div></div></template>
    <p v-else class="empty">暂无登录设备记录。</p>
  </section>

  <OaDialog :open="Boolean(target)" title="退出指定设备" :description="target?.device" submit-label="确认退出" :busy="security.saving" danger @close="target = null" @submit="confirmSingle"><p class="operation-warning">该设备上的访问令牌和刷新会话将立即失效。</p></OaDialog>
  <OaDialog :open="revokeOthersOpen" title="退出其他设备" description="保留当前设备，撤销此账号的其他有效登录会话。" submit-label="全部退出" :busy="security.saving" danger @close="revokeOthersOpen = false" @submit="confirmOthers"><p class="operation-warning">其他设备需要重新输入账号和密码才能登录。</p></OaDialog>
</template>
