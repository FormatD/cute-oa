import { ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { Department, DirectoryEmployee, PositionOption } from '../api/types'
import { useUiStore } from './ui'

export const useOrganizationStore = defineStore('organization', () => {
  const ui = useUiStore()
  const api = useApiClient().organization
  const departments = ref<Department[]>([])
  const directoryEmployees = ref<DirectoryEmployee[]>([])
  const positions = ref<PositionOption[]>([])
  const organizationLoading = ref(false)
  const organizationLoaded = ref(false)

  async function loadOrganization() {
    if (organizationLoaded.value) return
    organizationLoading.value = true
    try {
      const [departmentRecords, employeeRecords, positionRecords] = await Promise.all([api.getDepartments(), api.getDirectoryEmployees(), api.getPositions()])
      departments.value = departmentRecords
      directoryEmployees.value = employeeRecords
      positions.value = positionRecords
      organizationLoaded.value = true
    } catch (cause) {
      ui.error = cause instanceof Error ? cause.message : '组织通讯录加载失败。'
    } finally {
      organizationLoading.value = false
    }
  }

  function reset() {
    departments.value = []
    directoryEmployees.value = []
    positions.value = []
    organizationLoaded.value = false
  }

  return { departments, directoryEmployees, positions, organizationLoading, organizationLoaded, loadOrganization, reset }
})
