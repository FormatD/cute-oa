<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useOrganizationStore } from '../stores/organization'
import { usePersonnelStore } from '../stores/personnel'

const router = useRouter()
const personnel = usePersonnelStore()
const organization = useOrganizationStore()
const statusLabel = (value: string) => ({ PROBATION: '试用', ACTIVE: '在职', OFFBOARDING: '离职办理中', TERMINATED: '已离职' }[value] ?? value)
const employmentLabel = (value: string) => ({ FULL_TIME: '全职', PART_TIME: '兼职', INTERN: '实习', CONTRACTOR: '外包/顾问' }[value] ?? value)

onMounted(async () => { await Promise.all([organization.loadOrganization(), personnel.loadEmployees()]) })
</script>

<template>
  <div class="page-heading"><div><p class="eyebrow">EMPLOYEE DIRECTORY</p><h1>人事档案</h1><p>查看花名册、任职信息和员工生命周期记录。</p></div></div>
  <p v-if="personnel.message" class="notice success">{{ personnel.message }}</p><p v-if="personnel.error" class="notice error">{{ personnel.error }}</p>
  <form class="filter-bar" @submit.prevent="personnel.search"><input v-model="personnel.filters.keyword" placeholder="工号、姓名、用户 ID、部门或岗位"><select v-model="personnel.filters.departmentId"><option value="">全部部门</option><option v-for="department in organization.departments" :key="department.id" :value="department.id">{{ department.name }}</option></select><select v-model="personnel.filters.personnelStatus"><option value="">全部状态</option><option value="PROBATION">试用</option><option value="ACTIVE">在职</option><option value="OFFBOARDING">离职办理中</option><option value="TERMINATED">已离职</option></select><select v-model="personnel.filters.employmentType"><option value="">全部用工类型</option><option value="FULL_TIME">全职</option><option value="PART_TIME">兼职</option><option value="INTERN">实习</option><option value="CONTRACTOR">外包/顾问</option></select><button type="submit">查询</button><button class="secondary" type="button" @click="personnel.resetFilters">重置</button></form>
  <section class="panel"><div class="section-title"><div><p class="eyebrow">EMPLOYEE ROSTER</p><h2>员工花名册</h2></div></div><p v-if="personnel.loading" class="empty">正在加载花名册…</p><template v-else-if="personnel.employees.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>工号 / 姓名</th><th>部门</th><th>岗位</th><th>直属上级</th><th>入职日期</th><th>用工类型</th><th>人事状态</th><th class="action-cell">操作</th></tr></thead><tbody><tr v-for="employee in personnel.employees" :key="employee.userId"><td><strong>{{ employee.name }}</strong><small>{{ employee.employeeNumber }}</small></td><td>{{ employee.departmentName }}</td><td>{{ employee.positionName || '未分配' }}</td><td>{{ employee.managerName || '无' }}</td><td>{{ employee.hireDate }}</td><td>{{ employmentLabel(employee.employmentType) }}</td><td><em :class="{ archived: employee.personnelStatus === 'TERMINATED' }">{{ statusLabel(employee.personnelStatus) }}</em></td><td><button class="secondary" @click="router.push(`/hr/employees/${employee.userId}`)">查看档案</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ personnel.total }} 条</span><div><button class="secondary" :disabled="personnel.page === 1" @click="personnel.changePage(-1)">上一页</button><b>{{ personnel.page }} / {{ personnel.totalPages }}</b><button class="secondary" :disabled="personnel.page === personnel.totalPages" @click="personnel.changePage(1)">下一页</button></div></div></template><p v-else class="empty">当前权限或筛选条件下暂无员工档案。</p></section>
</template>
