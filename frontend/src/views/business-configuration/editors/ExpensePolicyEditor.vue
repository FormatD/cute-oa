<script setup lang="ts">
import type { ExpensePolicyConfig } from '../../../api/types'

const props = defineProps<{
  modelValue: ExpensePolicyConfig
}>()

function addCategory() {
  const codeSuffix = Math.floor(1000 + Math.random() * 9000).toString()
  props.modelValue.categories.push({
    code: `Expense_${codeSuffix}`,
    name: '新费用类别',
    isEnabled: true,
    singleLimit: 1000,
    requiresReceipt: true,
    requiresReasonWhenExceeded: true,
    blockWhenExceeded: false
  })
}

function removeCategory(idx: number) {
  props.modelValue.categories.splice(idx, 1)
}
</script>

<template>
  <div class="card-section">
    <div class="section-subheading-flex">
      <span>费用报销类别与限额规则</span>
      <button type="button" class="add-row-btn" @click="addCategory">
        ＋ 添加报销类别
      </button>
    </div>
    <div class="table-wrap compact">
      <table class="inner-table">
        <thead>
          <tr>
            <th style="min-width: 120px;">编码 (Code)</th>
            <th>类别名称</th>
            <th>启用</th>
            <th>单笔限额 (元, 空为无限制)</th>
            <th>发票凭证</th>
            <th>超限填理由</th>
            <th>超限阻断提交</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(cat, idx) in modelValue.categories" :key="idx">
            <td><input v-model="cat.code" placeholder="如 Traffic" style="min-width: 110px;" /></td>
            <td><input v-model="cat.name" placeholder="如 办公用品" /></td>
            <td><input v-model="cat.isEnabled" type="checkbox" /></td>
            <td><input v-model.number="cat.singleLimit" type="number" placeholder="留空无上限" /></td>
            <td><input v-model="cat.requiresReceipt" type="checkbox" /></td>
            <td><input v-model="cat.requiresReasonWhenExceeded" type="checkbox" /></td>
            <td><input v-model="cat.blockWhenExceeded" type="checkbox" /></td>
            <td>
              <button type="button" class="del-row-btn" @click="removeCategory(idx)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
