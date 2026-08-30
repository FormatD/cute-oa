import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router'
import { useAuthStore } from './stores/auth'
import './styles.css'

const pinia = createPinia()
const auth = useAuthStore(pinia)

router.beforeEach(async to => {
  await auth.initializeAuth()
  if (to.meta.public === true) return auth.authenticated ? '/workbench' : true
  if (!auth.authenticated) return { path: '/login', query: { redirect: to.fullPath } }
  if (typeof to.meta.permission === 'string' && !auth.currentUser?.permissions?.includes(to.meta.permission)) return '/workbench'
  return true
})

createApp(App).use(pinia).use(router).mount('#app')
