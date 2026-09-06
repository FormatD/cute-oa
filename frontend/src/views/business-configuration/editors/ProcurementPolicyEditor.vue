<script setup lang="ts">
import type { ProcurementPolicyConfig } from '../../../api/types'
import UserSelect from '../../../components/UserSelect.vue'

const props = defineProps<{
  modelValue: ProcurementPolicyConfig
}>()

function addCategory() {
  props.modelValue.categories.push({ name: '新类别', isEnabled: true })
}

function removeCategory(idx: number) {
  props.modelValue.categories.splice(idx, 1)
}

function addTier() {
  props.modelValue.amountTiers.push({ name: '新金额层级', maxAmount: 10000 })
}

function removeTier(idx: number) {
  props.modelValue.amountTiers.splice(idx, 1)
}
</script>

<template>
  <div class="card-section">
    <div class="section-subheading">采购风控与流程参数</div>
    <div class="form-grid-2">
      <label class="dialog-field">
        报价比价附件起计金额阈值 (元)
        <input v-model.number="modelValue.quoteAttachmentThreshold" type="number" />
        <small>超过此金额必须上传比价报价单附件</small>
      </label>

      <label class="dialog-field">
        默认采购专员
        <UserSelect
          v-model="modelValue.defaultPurchaserUserId"
          placeholder="请选择默认承办采购专员"
        />
        <small>发起采购申请时自动指派的采购承办人员</small>
      </label>
    </div>

    <div class="form-grid-2" style="margin-top: 10px;">
      <label class="checkbox-inline">
        <input v-model="modelValue.requiresAcceptance" type="checkbox" />
        采购到货强制需要验收流程
      </label>

      <label class="dialog-field">
        验收角色或指定人
        <input v-model="modelValue.acceptanceRoleOrAssignee" placeholder="例如: 部门负责人" />
      </label>
    </div>
  </div>

  <div class="card-section">
    <div class="section-subheading-flex">
      <span>采购类别</span>
      <button type="button" class="add-row-btn" @click="addCategory">
        ＋ 添加类别
      </button>
    </div>
    <div class="table-wrap compact">
      <table class="inner-table">
        <thead>
          <tr>
            <th>类别名称</th>
            <th>启用</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(cat, idx) in modelValue.categories" :key="idx">
            <td><input v-model="cat.name" placeholder="类别名称" /></td>
            <td><input v-model="cat.isEnabled" type="checkbox" /></td>
            <td>
              <button type="button" class="del-row-btn" @click="removeCategory(idx)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>

  <div class="card-section">
    <div class="section-subheading-flex">
      <span>采购金额审批层级</span>
      <button type="button" class="add-row-btn" @click="addTier">
        ＋ 添加层级
      </button>
    </div>
    <div class="table-wrap compact">
      <table class="inner-table">
        <thead>
          <tr>
            <th>层级名称</th>
            <th>最高金额上限 (元, 空为无上限)</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(tier, idx) in modelValue.amountTiers" :key="idx">
            <td><input v-model="tier.name" placeholder="如 小额采购" /></td>
            <td><input v-model.number="tier.maxAmount" type="number" placeholder="留空无上限" /></td>
            <td>
              <button type="button" class="del-row-btn" @click="removeTier(idx)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
