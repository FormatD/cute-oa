import { ref } from 'vue'
import { defineStore } from 'pinia'

export const useUiStore = defineStore('ui', () => {
  const message = ref('')
  const error = ref('')
  const submitting = ref(false)
  const sidebarOpen = ref(false)

  function resetFeedback() {
    message.value = ''
    error.value = ''
  }

  function showToast(msg: string, type: 'success' | 'error' | 'info' = 'info') {
    if (type === 'error') {
      error.value = msg
      message.value = ''
    } else {
      message.value = msg
      error.value = ''
    }
  }

  return { message, error, submitting, sidebarOpen, resetFeedback, showToast }
})
