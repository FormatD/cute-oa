<script setup lang="ts">
import type { DictionaryConfig } from '../../../api/types'

const props = defineProps<{
  modelValue: DictionaryConfig
}>()

function addItem() {
  props.modelValue.items.push({
    code: 'ITEM_CODE',
    name: '字典项名称',
    sortOrder: props.modelValue.items.length + 1,
    isEnabled: true,
    description: ''
  })
}

function removeItem(idx: number) {
  props.modelValue.items.splice(idx, 1)
}
</script>

<template>
  <div class="card-section">
    <div class="section-subheading-flex">
      <span>字典项配置列表</span>
      <button type="button" class="add-row-btn" @click="addItem">
        ＋ 添加字典项
      </button>
    </div>
    <div class="table-wrap compact">
      <table class="inner-table">
        <thead>
          <tr>
            <th>字典编码</th>
            <th>字典名称</th>
            <th>排序</th>
            <th>启用</th>
            <th>描述说明</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(it, idx) in modelValue.items" :key="idx">
            <td><input v-model="it.code" placeholder="编码" /></td>
            <td><input v-model="it.name" placeholder="名称" /></td>
            <td><input v-model.number="it.sortOrder" type="number" style="width: 60px;" /></td>
            <td><input v-model="it.isEnabled" type="checkbox" /></td>
            <td><input v-model="it.description" placeholder="描述" /></td>
            <td>
              <button type="button" class="del-row-btn" @click="removeItem(idx)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
