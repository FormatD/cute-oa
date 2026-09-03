<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'

const route = useRoute()
const router = useRouter()
const app = useAppStore()
const auth = useAuthStore()
const userId = ref('')
const password = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const verificationCode = ref('')

async function enterWorkspace() {
  const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/') ? route.query.redirect : '/workbench'
  await router.replace(redirect)
}

async function submit() {
  if (!userId.value || !password.value) return
  if (await app.login(userId.value, password.value) === 'AUTHENTICATED') await enterWorkspace()
}

async function submitMfa() {
  if (!verificationCode.value.trim()) return
  if (await app.verifyMfa(verificationCode.value)) await enterWorkspace()
}

async function submitInitialPasswordChange() {
  if (!newPassword.value || newPassword.value !== confirmPassword.value) {
    auth.authError = '两次输入的新密码不一致。'
    return
  }
  if (await auth.changeInitialPassword(newPassword.value) === 'AUTHENTICATED') await enterWorkspace()
}

async function confirmMfaSetup() {
  if (!verificationCode.value.trim()) return
  await app.confirmMfaSetup(verificationCode.value)
}

async function finishRecoveryCodes() {
  auth.acknowledgeRecoveryCodes()
  await enterWorkspace()
}

function restartLogin() {
  auth.clearSession()
  newPassword.value = ''
  confirmPassword.value = ''
  verificationCode.value = ''
}

async function loadLoginOptions() {
  await auth.loadDemoAccounts()
  if (auth.demoAccounts.length) {
    userId.value = auth.demoAccounts.some(item => item.id === 'u-zhang') ? 'u-zhang' : auth.demoAccounts[0].id
    password.value = 'Oa@123456'
  }
}

onMounted(() => { void loadLoginOptions() })
</script>

<template>
  <main class="login-page">
    <section class="login-card">
      <div class="login-brand"><span class="brand-mark">OA</span><div><strong>xxx公司</strong><small>协同办公平台</small></div></div>
      <div v-if="auth.recoveryCodes.length" class="login-heading"><p class="eyebrow">RECOVERY CODES</p><h1>保存恢复码</h1><p>每个恢复码只能使用一次。请保存在密码管理器或其他离线安全位置，离开此页后不再显示。</p></div>
      <div v-else-if="auth.mfaState === 'PASSWORD_CHANGE_REQUIRED'" class="login-heading"><p class="eyebrow">PASSWORD SETUP</p><h1>设置正式密码</h1><p>当前密码是管理员分配的临时密码。完成修改前不会创建登录会话。</p></div>
      <div v-else-if="auth.mfaState === 'MFA_SETUP_REQUIRED'" class="login-heading"><p class="eyebrow">SECURITY SETUP</p><h1>启用多因素认证</h1><p>该账号具有敏感管理权限，必须先绑定支持 TOTP 的身份验证器。</p></div>
      <div v-else-if="auth.mfaState === 'MFA_REQUIRED'" class="login-heading"><p class="eyebrow">SECURITY CHECK</p><h1>多因素认证</h1><p>请输入身份验证器中的 6 位动态码，或一个尚未使用的恢复码。</p></div>
      <div v-else class="login-heading"><p class="eyebrow">WELCOME BACK</p><h1>登录办公系统</h1><p>{{ auth.demoAccounts.length ? '请选择模拟账号进入对应角色的工作台。' : '请输入公司账号和密码。' }}</p></div>

      <section v-if="auth.recoveryCodes.length" class="login-form recovery-code-panel">
        <ul class="recovery-code-list"><li v-for="code in auth.recoveryCodes" :key="code"><code>{{ code }}</code></li></ul>
        <button type="button" @click="finishRecoveryCodes">我已安全保存，进入系统</button>
      </section>
      <form v-else-if="auth.mfaState === 'PASSWORD_CHANGE_REQUIRED'" class="login-form" @submit.prevent="submitInitialPasswordChange">
        <label>新密码<input v-model="newPassword" type="password" autocomplete="new-password" minlength="12" maxlength="128" placeholder="至少 12 位"></label>
        <label>确认新密码<input v-model="confirmPassword" type="password" autocomplete="new-password" minlength="12" maxlength="128" placeholder="再次输入新密码"></label>
        <p class="mfa-instruction">须包含大写字母、小写字母、数字和特殊字符，且不能与临时密码相同。</p>
        <p v-if="auth.authError" class="notice error">{{ auth.authError }}</p>
        <button type="submit" :disabled="auth.authLoading || newPassword.length < 12 || !confirmPassword">{{ auth.authLoading ? '正在修改…' : '修改密码并继续' }}</button>
        <button type="button" class="secondary" @click="restartLogin">返回账号登录</button>
      </form>
      <form v-else-if="auth.mfaState === 'MFA_SETUP_REQUIRED' && auth.mfaSetup" class="login-form" @submit.prevent="confirmMfaSetup">
        <p class="mfa-instruction">在身份验证器中添加账户，扫描功能不可用时可手动输入以下密钥：</p>
        <code class="mfa-secret">{{ auth.mfaSetup.secret }}</code>
        <a class="secondary mfa-app-link" :href="auth.mfaSetup.provisioningUri">用身份验证器打开</a>
        <label>6 位动态码<input v-model="verificationCode" inputmode="numeric" autocomplete="one-time-code" maxlength="6" placeholder="000000"></label>
        <p v-if="auth.authError" class="notice error">{{ auth.authError }}</p>
        <button type="submit" :disabled="auth.authLoading || verificationCode.length !== 6">{{ auth.authLoading ? '正在验证…' : '验证并启用' }}</button>
        <button type="button" class="secondary" @click="restartLogin">返回账号登录</button>
      </form>
      <form v-else-if="auth.mfaState === 'MFA_REQUIRED'" class="login-form" @submit.prevent="submitMfa">
        <label>动态码或恢复码<input v-model="verificationCode" autocomplete="one-time-code" maxlength="19" placeholder="000000 或 XXXX-XXXX-XXXX-XXXX"></label>
        <p v-if="auth.authError" class="notice error">{{ auth.authError }}</p>
        <button type="submit" :disabled="auth.authLoading || !verificationCode.trim()">{{ auth.authLoading ? '正在验证…' : '完成登录' }}</button>
        <button type="button" class="secondary" @click="restartLogin">返回账号登录</button>
      </form>
      <form v-else class="login-form" @submit.prevent="submit">
        <label v-if="auth.demoAccounts.length">账号<select v-model="userId" autocomplete="username"><option v-for="account in auth.demoAccounts" :key="account.id" :value="account.id">{{ account.name }} · {{ account.role }}</option></select></label>
        <label v-else>账号<input v-model="userId" autocomplete="username" maxlength="64" placeholder="请输入公司账号"></label>
        <label>密码<input v-model="password" type="password" autocomplete="current-password"></label>
        <p v-if="auth.demoAccounts.length" class="demo-hint">统一演示密码：<code>Oa@123456</code></p>
        <p v-if="auth.demoAccountsError" class="notice error">{{ auth.demoAccountsError }}</p>
        <p v-if="auth.authError" class="notice error">{{ auth.authError }}</p>
        <button type="submit" :disabled="auth.authLoading || !userId.trim() || !password">{{ auth.authLoading ? '正在登录…' : '登录' }}</button>
      </form>
      <p v-if="!auth.recoveryCodes.length" class="login-security">短期访问令牌仅保存在当前页面内存中；长期会话由 HttpOnly 安全 Cookie 保护，敏感权限账号强制使用多因素认证。</p>
    </section>
  </main>
</template>
