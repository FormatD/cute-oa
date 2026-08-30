<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const router = useRouter()
const app = useAppStore()
const auth = useAuthStore()
const userId = ref('u-zhang')
const password = ref('Oa@123456')

async function submit() {
  if (!userId.value || !password.value) return
  if (await app.login(userId.value, password.value)) {
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/') ? route.query.redirect : '/workbench'
    await router.replace(redirect)
  }
}

onMounted(auth.loadDemoAccounts)
</script>

<template>
  <main class="login-page">
    <section class="login-card">
      <div class="login-brand"><span class="brand-mark">OA</span><div><strong>xxx公司</strong><small>协同办公平台</small></div></div>
      <div class="login-heading"><p class="eyebrow">WELCOME BACK</p><h1>登录办公系统</h1><p>请选择一个模拟账号进入对应角色的工作台。</p></div>
      <form class="login-form" @submit.prevent="submit">
        <label>账号<select v-model="userId" autocomplete="username"><option v-for="account in auth.demoAccounts" :key="account.id" :value="account.id">{{ account.name }} · {{ account.role }}</option></select></label>
        <label>密码<input v-model="password" type="password" autocomplete="current-password"></label>
        <p class="demo-hint">统一演示密码：<code>Oa@123456</code></p>
        <p v-if="auth.demoAccountsError" class="notice error">{{ auth.demoAccountsError }}</p>
        <p v-if="auth.authError" class="notice error">{{ auth.authError }}</p>
        <button type="submit" :disabled="auth.authLoading || !auth.demoAccounts.length">{{ auth.authLoading ? '正在登录…' : '登录' }}</button>
      </form>
      <p class="login-security">短期访问令牌仅保存在当前页面内存中；长期会话由 HttpOnly 安全 Cookie 保护，退出后服务端立即撤销。</p>
    </section>
  </main>
</template>
