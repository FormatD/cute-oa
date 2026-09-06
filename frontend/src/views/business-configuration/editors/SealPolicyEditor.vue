<script setup lang="ts">
import type { SealPolicyConfig } from '../../../api/types'
import UserSelect from '../../../components/UserSelect.vue'

const props = defineProps<{
  modelValue: SealPolicyConfig
}>()

function addSeal() {
  const codeSuffix = Math.floor(1000 + Math.random() * 9000).toString()
  props.modelValue.seals.push({
    code: `Seal_${codeSuffix}`,
    name: '新印章',
    sealType: 'COMPANY_OFFICIAL',
    custodianUserId: 'admin',
    isEnabled: true,
    allowOut: true,
    maxOutDays: 3
  })
}

function removeSeal(idx: number) {
  props.modelValue.seals.splice(idx, 1)
}

function addDocCategory() {
  const codeSuffix = Math.floor(1000 + Math.random() * 9000).toString()
  props.modelValue.documentCategories.push({
    code: `Doc_${codeSuffix}`,
    name: '新文件类别',
    riskLevel: 'LOW',
    isEnabled: true
  })
}

function removeDocCategory(idx: number) {
  props.modelValue.documentCategories.splice(idx, 1)
}
</script>

<template>
  <div class="card-section">
    <div class="section-subheading-flex">
      <span>印章登记名录</span>
      <button type="button" class="add-row-btn" @click="addSeal">
        ＋ 添加印章
      </button>
    </div>
    <div class="table-wrap compact">
      <table class="inner-table">
        <thead>
          <tr>
            <th style="min-width: 110px;">编码 (Code)</th>
            <th>印章名称</th>
            <th>印章类型</th>
            <th style="min-width: 140px;">保管人</th>
            <th>启用</th>
            <th>支持外借</th>
            <th>外借最长天数</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(s, idx) in modelValue.seals" :key="idx">
            <td><input v-model="s.code" placeholder="如 OfficialSeal" style="min-width: 100px;" /></td>
            <td><input v-model="s.name" placeholder="公章名称" /></td>
            <td>
              <select v-model="s.sealType">
                <option value="COMPANY_OFFICIAL">公章 (COMPANY_OFFICIAL)</option>
                <option value="CONTRACT">合同专用章 (CONTRACT)</option>
                <option value="LEGAL_REPRESENTATIVE">法人章 (LEGAL_REPRESENTATIVE)</option>
                <option value="FINANCE">财务专用章 (FINANCE)</option>
              </select>
            </td>
            <td style="min-width: 140px;">
              <UserSelect
                v-model="s.custodianUserId"
                compact
                placeholder="选择保管人"
              />
            </td>
            <td><input v-model="s.isEnabled" type="checkbox" /></td>
            <td><input v-model="s.allowOut" type="checkbox" /></td>
            <td><input v-model.number="s.maxOutDays" type="number" min="0" max="90" /></td>
            <td>
              <button type="button" class="del-row-btn" @click="removeSeal(idx)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>

  <div class="card-section">
    <div class="section-subheading-flex">
      <span>用印文件类别与风险等级定义</span>
      <button type="button" class="add-row-btn" @click="addDocCategory">
        ＋ 添加类别
      </button>
    </div>
    <div class="table-wrap compact">
      <table class="inner-table">
        <thead>
          <tr>
            <th style="min-width: 110px;">编码 (Code)</th>
            <th>文件类别</th>
            <th>风险等级</th>
            <th>启用</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(dc, idx) in modelValue.documentCategories" :key="idx">
            <td><input v-model="dc.code" placeholder="如 Contract" style="min-width: 100px;" /></td>
            <td><input v-model="dc.name" placeholder="如 工商变更" /></td>
            <td>
              <select v-model="dc.riskLevel">
                <option value="HIGH">高风险 (HIGH)</option>
                <option value="MEDIUM">中风险 (MEDIUM)</option>
                <option value="LOW">低风险 (LOW)</option>
              </select>
            </td>
            <td><input v-model="dc.isEnabled" type="checkbox" /></td>
            <td>
              <button type="button" class="del-row-btn" @click="removeDocCategory(idx)">删除</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
