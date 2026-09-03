<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useCalendarStore } from '../stores/calendar'
const calendar = useCalendarStore(); const router = useRouter()
</script>
<template>
  <div class="page-heading"><div><p class="eyebrow">WORK CALENDAR</p><h1>工作日历</h1><p>查看 {{ calendar.year }} 年法定工作日与企业调休安排，为请假和考勤计算提供基础。</p></div></div>
  <section class="panel calendar-panel"><div class="section-title"><div><p class="eyebrow">{{ calendar.year }}</p><h2>企业工作日调整</h2></div></div><p class="calendar-intro">默认使用中国法定工作日历；下列为企业维护的节假日或调休覆盖项。</p>
    <template v-if="calendar.entries.length"><div class="table-wrap"><table class="data-table"><thead><tr><th>日期</th><th>类型</th><th>说明</th><th>来源</th><th>操作</th></tr></thead><tbody><tr v-for="entry in calendar.pagedEntries.items" :key="entry.date"><td>{{ entry.date }}</td><td><em :class="{ holiday: !entry.isWorkingDay }">{{ entry.isWorkingDay ? '工作日' : '休息日' }}</em></td><td>{{ entry.note || '企业工作日设置' }}</td><td>{{ entry.source }}</td><td><button class="secondary" @click="router.push(`/calendar/${entry.date}`)">详情</button></td></tr></tbody></table></div><div class="pagination"><span>共 {{ calendar.pagedEntries.total }} 条</span><div><button class="secondary" :disabled="calendar.pagedEntries.currentPage === 1" @click="calendar.page--">上一页</button><b>{{ calendar.pagedEntries.currentPage }} / {{ calendar.pagedEntries.totalPages }}</b><button class="secondary" :disabled="calendar.pagedEntries.currentPage === calendar.pagedEntries.totalPages" @click="calendar.page++">下一页</button></div></div></template><p v-else class="empty">暂无企业自定义调整，当前按中国法定工作日历执行。</p>
  </section>
</template>
