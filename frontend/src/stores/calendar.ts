import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { WorkCalendarEntry } from '../api/types'
import { paginate } from './pagination'

export const useCalendarStore = defineStore('calendar', () => {
  const api = useApiClient().system
  const entries = ref<WorkCalendarEntry[]>([])
  const year = new Date().getFullYear()
  const page = ref(1)
  const pagedEntries = computed(() => paginate([...entries.value].sort((a, b) => b.date.localeCompare(a.date)), page.value))

  async function loadCalendar() {
    entries.value = await api.getWorkCalendar(year)
  }

  function reset() {
    entries.value = []
    page.value = 1
  }

  return { entries, year, page, pagedEntries, loadCalendar, reset }
})
