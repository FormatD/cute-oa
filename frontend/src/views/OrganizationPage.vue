<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useOrganizationStore } from '../stores/organization'
import { paginate } from '../stores/pagination'

const organization = useOrganizationStore()
const keyword = ref('')
const departmentId = ref('')
const page = ref(1)
const filteredEmployees = computed(() => organization.directoryEmployees.filter(employee =>
  (!departmentId.value || employee.departmentId === departmentId.value) &&
  (!keyword.value.trim() || [employee.name, employee.positionName ?? '', employee.role, employee.departmentName, employee.managerName ?? ''].some(value => value.includes(keyword.value.trim())))
))
const pagedEmployees = computed(() => paginate(filteredEmployees.value, page.value))
const departmentCounts = computed(() => Object.fromEntries(organization.departments.map(department => [department.id, organization.directoryEmployees.filter(employee => employee.departmentId === department.id).length])))

watch([keyword, departmentId], () => { page.value = 1 })
onMounted(() => { void organization.loadOrganization() })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">ORGANIZATION</p><h1>组织通讯录</h1><p>查看公司部门、岗位和上下级关系。</p></div></div>
  <p v-if="organization.organizationLoading" class="empty">正在加载组织信息…</p>
  <template v-else>
    <section class="org-cards"><button v-for="department in organization.departments" :key="department.id" :class="{ active: departmentId === department.id }" @click="departmentId = departmentId === department.id ? '' : department.id"><strong>{{ department.name }}</strong><span>{{ departmentCounts[department.id] ?? 0 }} 人</span><small>{{ department.parentId ? '所属公司部门' : '公司直属部门' }}</small></button></section>
    <section class="panel"><div class="section-title"><div><p class="eyebrow">DIRECTORY</p><h2>员工通讯录</h2></div></div>
      <form class="filter-bar" @submit.prevent><input v-model="keyword" placeholder="姓名、岗位或上级"><select v-model="departmentId"><option value="">全部部门</option><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select><button v-if="keyword || departmentId" class="secondary" type="button" @click="keyword = ''; departmentId = ''">重置</button></form>
      <template v-if="pagedEmployees.total"><div class="table-wrap"><table class="data-table"><thead><tr><th>姓名</th><th>部门</th><th>岗位</th><th>安全角色</th><th>直属上级</th></tr></thead><tbody><tr v-for="employee in pagedEmployees.items" :key="employee.id"><td><strong>{{ employee.name }}</strong></td><td>{{ employee.departmentName }}</td><td>{{ employee.positionName || '未分配' }}</td><td>{{ employee.role }}</td><td>{{ employee.managerName || '—' }}</td></tr></tbody></table></div><div class="pagination"><span>共 {{ pagedEmployees.total }} 人</span><div><button class="secondary" :disabled="pagedEmployees.currentPage === 1" @click="page--">上一页</button><b>{{ pagedEmployees.currentPage }} / {{ pagedEmployees.totalPages }}</b><button class="secondary" :disabled="pagedEmployees.currentPage === pagedEmployees.totalPages" @click="page++">下一页</button></div></div></template><p v-else class="empty">当前条件下没有匹配的员工。</p>
    </section>
  </template>
</template>
