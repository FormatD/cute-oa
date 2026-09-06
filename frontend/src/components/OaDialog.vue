<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'

const props = withDefaults(defineProps<{
  open: boolean
  title: string
  description?: string
  submitLabel: string
  busy?: boolean
  danger?: boolean
  maskClosable?: boolean
  width?: string
  submitDisabled?: boolean
}>(), {
  maskClosable: false,
  submitDisabled: false
})

const emit = defineEmits<{ close: []; submit: [] }>()
const backdrop = ref<HTMLElement | null>(null)
let previousOverflow = ''

function requestClose() {
  if (!props.busy) emit('close')
}

function handleBackdropMouseDown(e: MouseEvent) {
  // Only close if maskClosable is explicitly enabled and the target is the backdrop itself
  if (props.maskClosable && e.target === backdrop.value) {
    requestClose()
  }
}

function handleKeyDown(e: KeyboardEvent) {
  if (e.key === 'Escape' && !e.isComposing) {
    requestClose()
  }
}

watch(() => props.open, async open => {
  if (open) {
    previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    await nextTick()
    backdrop.value?.focus()
  } else {
    document.body.style.overflow = previousOverflow
  }
})

onBeforeUnmount(() => {
  document.body.style.overflow = previousOverflow
})
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      ref="backdrop"
      class="dialog-backdrop"
      tabindex="-1"
      @mousedown="handleBackdropMouseDown"
      @keydown="handleKeyDown"
    >
      <section
        class="oa-dialog"
        :style="width ? { width, maxWidth: '95vw' } : undefined"
        role="dialog"
        aria-modal="true"
        :aria-label="title"
        @mousedown.stop
      >
        <header class="dialog-header">
          <div>
            <p class="eyebrow">OPERATION</p>
            <h2>{{ title }}</h2>
            <p v-if="description">{{ description }}</p>
          </div>
          <button
            class="dialog-close"
            type="button"
            aria-label="关闭"
            :disabled="busy"
            @click="requestClose"
          >
            ×
          </button>
        </header>
        <form class="dialog-form" novalidate @submit.prevent="emit('submit')">
          <div class="dialog-body">
            <slot />
          </div>
          <footer class="dialog-footer">
            <button
              class="secondary"
              type="button"
              :disabled="busy"
              @click="requestClose"
            >
              取消
            </button>
            <button
              :class="{ danger }"
              type="submit"
              :disabled="busy || submitDisabled"
            >
              {{ busy ? '处理中…' : submitLabel }}
            </button>
          </footer>
        </form>
      </section>
    </div>
  </Teleport>
</template>

