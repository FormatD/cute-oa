import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { Employee } from '../api/types'

export const useEmployeeDirectoryStore = defineStore('employee-directory', () => {
  const api = useApiClient().auth
  const employees = ref<Employee[]>([])

  async function loadEmployees() {
    employees.value = await api.getDemoUsers()
  }

  function reset() {
    employees.value = []
  }

  return { employees, loadEmployees, reset }
})
