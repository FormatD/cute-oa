<script setup lang="ts">
import type { LeavePolicyConfig } from '../../../api/types'

const props = defineProps<{
  modelValue: LeavePolicyConfig
}>()

function addLeaveType() {
  props.modelValue.leaveTypes.push({
    type: 'NEW_TYPE',
    name: '新假期',
    isEnabled: true,
    minUnit: 0.5,
    requiresAttachment: false,
    attachmentThresholdDays: null
  })
}

function removeLeaveType(idx: number) {
  props.modelValue.leaveTypes.splice(idx, 1)
}
</script>

<template>
  <div class="card-section">
    <div class="section-subheading">通用规则与年假奖励</div>
    <div class="form-row-checks">
      <label class="checkbox-inline">
        <input v-model="modelValue.allowCrossYear" type="checkbox" />
        允许年假跨年结转
      </label>
      <label class="dialog-field compact">
        调休有效期天数
        <input v-model.number="modelValue.compTimeValidityDays" type="number" min="1" max="1000" />
      </label>
    </div>

    <div class="annual-bonus-box">
      <div class="annual-bonus-header">
        <strong>司龄年假阶梯奖励 (在法定年假基础上额外奖励)</strong>
        <label class="checkbox-inline highlight">
          <input v-model="modelValue.annualLeaveBonus.legalMinStandardProtected" type="checkbox" />
          依法保护最低带薪休假标准
        </label>
      </div>
      <div class="bonus-tiers-grid">
        <label class="dialog-field">
          1 - 5 年司龄奖励 (天)
          <input v-model.number="modelValue.annualLeaveBonus.tier1BonusDays" type="number" min="0" step="0.5" />
        </label>
        <label class="dialog-field">
          5 - 10 年司龄奖励 (天)
          <input v-model.number="modelValue.annualLeaveBonus.tier2BonusDays" type="number" min="0" step="0.5" />
        </label>
        <label class="dialog-field">
          10 年以上司龄奖励 (天)
          <input v-model.number="modelValue.annualLeaveBonus.tier3BonusDays" type="number" min="0" step="0.5" />
        </label>
      </div>
    </div>
  </div>

  <div class="card-section">
    <div class="section-subheading-flex">
      <span>假期类型列表</span>
      <button type="button" class="add-row-btn" @click="addLeaveType">
        ＋ 添加假期类型
      </button>
    </div>
    <div class="table-wrap compact">
      <table class="inner-table">
        <thead>
          <tr>
            <th>假期编码</th>
            <th>假期名称</th>
            <th>启用</th>
            <th>最小单位(天)</th>
            <th>需附证明</th>
            <th>证明起计天数</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(lt, idx) in modelValue.leaveTypes" :key="idx">
            <td><input v-model="lt.type" placeholder="如 ANNUAL" /></td>
            <td><input v-model="lt.name" placeholder="如 年假" /></td>
            <td><input v-model="lt.isEnabled" type="checkbox" /></td>
            <td>
              <select v-model.number="lt.minUnit">
                <option :value="0.5">0.5 天</option>
                <option :value="1">1.0 天</option>
              </select>
            </td>
            <td><input v-model="lt.requiresAttachment" type="checkbox" /></td>
            <td>
              <input
                v-model.number="lt.attachmentThresholdDays"
                type="number"
                placeholder="留空即必填"
                step="0.5"
                :disabled="!lt.requiresAttachment"
              />
            </td>
            <td>
              <button type="button" class="del-row-btn" @click="removeLeaveType(idx)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
