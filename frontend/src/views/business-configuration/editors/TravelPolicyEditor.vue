<script setup lang="ts">
import type { TravelPolicyConfig } from '../../../api/types'

const props = defineProps<{
  modelValue: TravelPolicyConfig
  tierInputs: { tierName: string; citiesStr: string }[]
  ranksStr: string
}>()

const emit = defineEmits<{
  'update:ranksStr': [value: string]
}>()

function addCityTier() {
  props.tierInputs.push({ tierName: '新城市级别', citiesStr: '' })
}

function removeCityTier(idx: number) {
  props.tierInputs.splice(idx, 1)
}

function addStandard() {
  props.modelValue.standards.push({
    cityTier: props.tierInputs[0]?.tierName || '一线城市',
    rank: '基层员工',
    hotelDailyLimit: 400,
    mealDailyAllowance: 100,
    transportationStandard: '高铁二等座'
  })
}

function removeStandard(idx: number) {
  props.modelValue.standards.splice(idx, 1)
}
</script>

<template>
  <div class="card-section">
    <div class="section-subheading-flex">
      <span>城市级别与职级划分</span>
      <button type="button" class="add-row-btn" @click="addCityTier">
        ＋ 添加城市级别
      </button>
    </div>
    <div v-for="(ti, idx) in tierInputs" :key="idx" class="travel-tier-row">
      <input v-model="ti.tierName" class="tier-name-input" placeholder="级别名，如 一线城市" />
      <input v-model="ti.citiesStr" class="tier-cities-input" placeholder="包含城市（逗号分隔，如: 北京, 上海, 广州）" />
      <button type="button" class="del-row-btn" @click="removeCityTier(idx)">删除</button>
    </div>

    <label class="dialog-field" style="margin-top: 12px;">
      适用员工职级 (逗号分隔)
      <input
        :value="ranksStr"
        placeholder="例如: 基层员工, 骨干员工, 部门负责人, 公司高管"
        @input="emit('update:ranksStr', ($event.target as HTMLInputElement).value)"
      />
    </label>
  </div>

  <div class="card-section">
    <div class="section-subheading-flex">
      <span>差旅标准矩阵 (住宿、餐补、交通)</span>
      <button type="button" class="add-row-btn" @click="addStandard">
        ＋ 添加标准行
      </button>
    </div>
    <div class="table-wrap compact">
      <table class="inner-table">
        <thead>
          <tr>
            <th>城市级别</th>
            <th>职级</th>
            <th>住宿限额 (元/晚)</th>
            <th>餐补标准 (元/天)</th>
            <th>交通工具标准</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(std, idx) in modelValue.standards" :key="idx">
            <td><input v-model="std.cityTier" placeholder="城市级别" /></td>
            <td><input v-model="std.rank" placeholder="职级" /></td>
            <td><input v-model.number="std.hotelDailyLimit" type="number" /></td>
            <td><input v-model.number="std.mealDailyAllowance" type="number" /></td>
            <td><input v-model="std.transportationStandard" placeholder="如 高铁二等座/经济舱" /></td>
            <td>
              <button type="button" class="del-row-btn" @click="removeStandard(idx)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
