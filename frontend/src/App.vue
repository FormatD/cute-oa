<script setup lang="ts">
import { computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import OaHeader from './components/OaHeader.vue'
import OaSidebar from './components/OaSidebar.vue'
import { useAppStore } from './stores/app'
import { useAuthStore } from './stores/auth'
import { useUiStore } from './stores/ui'
import { useWorkflowStore } from './stores/workflow'
import { useWorkspaceStore } from './stores/workspace'

const router = useRouter()
const route = useRoute()
const app = useAppStore()
const auth = useAuthStore()
const ui = useUiStore()
const workflow = useWorkflowStore()
const workspace = useWorkspaceStore()
const activeNav = computed(() => route.path.startsWith('/hr/contracts') ? 'contracts' : route.path.split('/')[1] || 'workbench')
const publicLayout = computed(() => route.meta.public === true)

function navigate(route: string) { router.push(`/${route}`); ui.sidebarOpen = false }
async function logout() { await app.logout(); await router.replace('/login') }

onMounted(() => { void app.initialize() })
watch(() => auth.authenticated, authenticated => {
  if (auth.authReady && !authenticated && !publicLayout.value) void router.replace({ path: '/login', query: { redirect: route.fullPath } })
})
</script>

<template>
  <RouterView v-if="publicLayout" />
  <main v-else-if="auth.authenticated" class="app-shell">
    <OaSidebar :open="ui.sidebarOpen" :active-nav="activeNav" :summary="workspace.summary" :current-user="auth.currentUser" @navigate="navigate" @close="ui.sidebarOpen = false" />
    <div v-if="ui.sidebarOpen" class="sidebar-mask" @click="ui.sidebarOpen = false"></div>
    <section class="page-frame">
      <OaHeader :active-nav="activeNav" :current-user="auth.currentUser" :notifications="workflow.notifications" @menu="ui.sidebarOpen = true" @logout="logout" />
      <section class="content-area"><RouterView /></section>
    </section>
  </main>
</template>
