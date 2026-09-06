<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import type {
  BusinessConfigurationRecord,
  ConfigurationDomain,
  DictionaryConfig,
  ExpensePolicyConfig,
  LeavePolicyConfig,
  ProcurementPolicyConfig,
  SealPolicyConfig,
  TravelCityTierConfig,
  TravelPolicyConfig
} from '../../api/types'
import OaDialog from '../../components/OaDialog.vue'
import { useBusinessConfigurationStore } from '../../stores/business-configurations'
import DictionaryPolicyEditor from './editors/DictionaryPolicyEditor.vue'
import ExpensePolicyEditor from './editors/ExpensePolicyEditor.vue'
import LeavePolicyEditor from './editors/LeavePolicyEditor.vue'
import ProcurementPolicyEditor from './editors/ProcurementPolicyEditor.vue'
import SealPolicyEditor from './editors/SealPolicyEditor.vue'
import TravelPolicyEditor from './editors/TravelPolicyEditor.vue'
import {
  domainLabel,
  getDefaultConfigForDomain,
  normalizeDomainConfig,
  toLocalInput,
  validateConfigDraft
} from './useBusinessConfigEditor'

const props = defineProps<{
  open: boolean
  editingId: string | null
  editingVersion: number
  editingConcurrencyVersion: number
  initialRecord?: BusinessConfigurationRecord | null
  defaultDomain?: ConfigurationDomain
}>()

const emit = defineEmits<{
  close: []
  saved: []
}>()

const store = useBusinessConfigurationStore()
const editorMode = ref<'visual' | 'json'>('visual')
const validationError = ref('')

const form = reactive({
  domain: 'Leave' as ConfigurationDomain,
  code: '',
  name: '',
  description: '',
  effectiveFrom: '',
  effectiveTo: '',
  contentJson: ''
})

// Visual domain state
const leaveForm = reactive<LeavePolicyConfig>({
  leaveTypes: [],
  allowCrossYear: true,
  compTimeValidityDays: 365,
  annualLeaveBonus: {
    legalMinStandardProtected: true,
    tier1BonusDays: 1,
    tier2BonusDays: 2,
    tier3BonusDays: 3
  }
})

const expenseForm = reactive<ExpensePolicyConfig>({
  categories: []
})

const travelForm = reactive<TravelPolicyConfig>({
  cityTiers: [],
  employeeRanks: [],
  standards: []
})
const travelTierInputs = reactive<{ tierName: string; citiesStr: string }[]>([])
const travelRanksStr = ref('')

const procurementForm = reactive<ProcurementPolicyConfig>({
  categories: [],
  quoteAttachmentThreshold: 5000,
  amountTiers: [],
  defaultPurchaserUserId: '',
  requiresAcceptance: true,
  acceptanceRoleOrAssignee: ''
})

const sealForm = reactive<SealPolicyConfig>({
  seals: [],
  documentCategories: [],
  riskRules: {
    highRiskMetric: 3,
    mediumRiskMetric: 2,
    lowRiskMetric: 1
  }
})

const dictionaryForm = reactive<DictionaryConfig>({
  items: []
})

function parseJsonToVisual(domain: string, jsonStr: string) {
  try {
    const data = JSON.parse(jsonStr || '{}')
    const normalized = normalizeDomainConfig(domain, data)
    if (!normalized) return

    switch (domain) {
      case 'Leave':
        leaveForm.leaveTypes = normalized.leaveTypes
        leaveForm.allowCrossYear = normalized.allowCrossYear
        leaveForm.compTimeValidityDays = normalized.compTimeValidityDays
        leaveForm.annualLeaveBonus = normalized.annualLeaveBonus
        break
      case 'Expense':
        expenseForm.categories = normalized.categories
        break
      case 'Travel':
        travelForm.cityTiers = normalized.cityTiers
        travelForm.employeeRanks = normalized.employeeRanks
        travelForm.standards = normalized.standards
        travelTierInputs.splice(
          0,
          travelTierInputs.length,
          ...travelForm.cityTiers.map((t: TravelCityTierConfig) => ({
            tierName: t.tierName,
            citiesStr: (t.cities || []).join(', ')
          }))
        )
        travelRanksStr.value = travelForm.employeeRanks.join(', ')
        break
      case 'Procurement':
        procurementForm.categories = normalized.categories
        procurementForm.quoteAttachmentThreshold = normalized.quoteAttachmentThreshold
        procurementForm.amountTiers = normalized.amountTiers
        procurementForm.defaultPurchaserUserId = normalized.defaultPurchaserUserId
        procurementForm.requiresAcceptance = normalized.requiresAcceptance
        procurementForm.acceptanceRoleOrAssignee = normalized.acceptanceRoleOrAssignee
        break
      case 'Seal':
        sealForm.seals = normalized.seals
        sealForm.documentCategories = normalized.documentCategories
        sealForm.riskRules = normalized.riskRules
        break
      case 'Dictionary':
        dictionaryForm.items = normalized.items
        break
    }
  } catch {
    editorMode.value = 'json'
  }
}

function syncVisualToJson(): string {
  let obj: unknown = {}
  switch (form.domain) {
    case 'Leave':
      obj = {
        leaveTypes: leaveForm.leaveTypes.map(t => ({
          type: (t.type || '').trim(),
          name: (t.name || '').trim(),
          isEnabled: Boolean(t.isEnabled),
          minUnit: Number(t.minUnit) || 0.5,
          requiresAttachment: Boolean(t.requiresAttachment),
          attachmentThresholdDays:
            t.attachmentThresholdDays !== null &&
            t.attachmentThresholdDays !== undefined &&
            t.attachmentThresholdDays !== ('' as unknown)
              ? Number(t.attachmentThresholdDays)
              : null
        })),
        allowCrossYear: Boolean(leaveForm.allowCrossYear),
        compTimeValidityDays: Number(leaveForm.compTimeValidityDays) || 365,
        annualLeaveBonus: {
          legalMinStandardProtected: Boolean(leaveForm.annualLeaveBonus.legalMinStandardProtected),
          tier1BonusDays: Number(leaveForm.annualLeaveBonus.tier1BonusDays) || 0,
          tier2BonusDays: Number(leaveForm.annualLeaveBonus.tier2BonusDays) || 0,
          tier3BonusDays: Number(leaveForm.annualLeaveBonus.tier3BonusDays) || 0
        }
      }
      break
    case 'Expense':
      obj = {
        categories: expenseForm.categories.map(c => ({
          name: (c.name || '').trim(),
          isEnabled: Boolean(c.isEnabled),
          singleLimit:
            c.singleLimit !== null && c.singleLimit !== undefined && c.singleLimit !== ('' as unknown)
              ? Number(c.singleLimit)
              : null,
          requiresReceipt: Boolean(c.requiresReceipt),
          requiresReasonWhenExceeded: Boolean(c.requiresReasonWhenExceeded),
          blockWhenExceeded: Boolean(c.blockWhenExceeded)
        }))
      }
      break
    case 'Travel': {
      const cityTiers = travelTierInputs.map(ti => ({
        tierName: ti.tierName.trim(),
        cities: ti.citiesStr
          .split(/[,，]/)
          .map(s => s.trim())
          .filter(Boolean)
      }))
      const employeeRanks = travelRanksStr.value
        .split(/[,，]/)
        .map(s => s.trim())
        .filter(Boolean)
      obj = {
        cityTiers,
        employeeRanks,
        standards: travelForm.standards.map(s => ({
          cityTier: (s.cityTier || '').trim(),
          rank: (s.rank || '').trim(),
          hotelDailyLimit: Number(s.hotelDailyLimit) || 0,
          mealDailyAllowance: Number(s.mealDailyAllowance) || 0,
          transportationStandard: (s.transportationStandard || '').trim()
        }))
      }
      break
    }
    case 'Procurement':
      obj = {
        categories: procurementForm.categories.map(c => ({
          name: (c.name || '').trim(),
          isEnabled: Boolean(c.isEnabled)
        })),
        quoteAttachmentThreshold: Number(procurementForm.quoteAttachmentThreshold) || 0,
        amountTiers: procurementForm.amountTiers.map(t => ({
          name: (t.name || '').trim(),
          maxAmount:
            t.maxAmount !== null && t.maxAmount !== undefined && t.maxAmount !== ('' as unknown)
              ? Number(t.maxAmount)
              : null
        })),
        defaultPurchaserUserId: (procurementForm.defaultPurchaserUserId || '').trim(),
        requiresAcceptance: Boolean(procurementForm.requiresAcceptance),
        acceptanceRoleOrAssignee: (procurementForm.acceptanceRoleOrAssignee || '').trim()
      }
      break
    case 'Seal':
      obj = {
        seals: sealForm.seals.map(s => ({
          name: (s.name || '').trim(),
          sealType: (s.sealType || '').trim(),
          custodianUserId: (s.custodianUserId || '').trim(),
          isEnabled: Boolean(s.isEnabled),
          allowOut: Boolean(s.allowOut),
          maxOutDays: Number(s.maxOutDays) || 0
        })),
        documentCategories: sealForm.documentCategories.map(dc => ({
          name: (dc.name || '').trim(),
          isEnabled: Boolean(dc.isEnabled),
          riskLevel: dc.riskLevel || 'LOW'
        })),
        riskRules: {
          highRiskMetric: Number(sealForm.riskRules.highRiskMetric) || 3,
          mediumRiskMetric: Number(sealForm.riskRules.mediumRiskMetric) || 2,
          lowRiskMetric: Number(sealForm.riskRules.lowRiskMetric) || 1
        }
      }
      break
    case 'Dictionary':
      obj = {
        items: dictionaryForm.items.map((it, idx) => ({
          code: (it.code || '').trim(),
          name: (it.name || '').trim(),
          sortOrder: typeof it.sortOrder === 'number' ? it.sortOrder : idx + 1,
          isEnabled: Boolean(it.isEnabled),
          description: (it.description || '').trim() || null
        }))
      }
      break
  }
  return JSON.stringify(obj, null, 2)
}

async function loadBaseConfigForDomain(domain: ConfigurationDomain) {
  const existing =
    store.items.find(i => i.domain === domain && i.status === 'EFFECTIVE') ||
    store.items.find(i => i.domain === domain)
  if (existing) {
    const full = await store.loadDetail(existing.id)
    if (full && full.contentJson) {
      form.code = full.code
      form.name = `${full.name} (新草稿)`
      form.description = full.description || ''
      form.contentJson = full.contentJson
      parseJsonToVisual(domain, full.contentJson)
      return
    }
  }
  const def = getDefaultConfigForDomain(domain)
  form.code = def.code
  form.name = def.name
  form.description = def.description
  form.contentJson = def.json
  parseJsonToVisual(domain, def.json)
}

function resetToDefaultTemplate() {
  const def = getDefaultConfigForDomain(form.domain)
  form.code = def.code
  form.name = def.name
  form.description = def.description
  form.contentJson = def.json
  parseJsonToVisual(form.domain, def.json)
  store.message = `已载入【${domainLabel(form.domain)}】的出厂默认配置模板。`
}

async function copyFromExistingConfig() {
  const existing =
    store.items.find(i => i.domain === form.domain && i.status === 'EFFECTIVE') ||
    store.items.find(i => i.domain === form.domain)
  if (existing) {
    const full = await store.loadDetail(existing.id)
    if (full && full.contentJson) {
      form.contentJson = full.contentJson
      form.code = full.code
      parseJsonToVisual(form.domain, full.contentJson)
      store.message = `已成功复制当前【${domainLabel(form.domain)}】生效版本 (v${full.version}) 的配置内容。`
      return
    }
  }
  store.message = `当前业务域【${domainLabel(form.domain)}】暂无已有生效配置可供复制。`
}

async function handleDomainChange() {
  if (props.editingId) return
  await loadBaseConfigForDomain(form.domain)
}

function switchEditorMode(mode: 'visual' | 'json') {
  if (mode === 'json') {
    form.contentJson = syncVisualToJson()
    editorMode.value = 'json'
  } else {
    try {
      JSON.parse(form.contentJson)
      parseJsonToVisual(form.domain, form.contentJson)
      editorMode.value = 'visual'
      validationError.value = ''
    } catch {
      validationError.value = '当前原始 JSON 格式不合法，请先修正语法错误后再切换到可视化表单。'
    }
  }
}

watch(
  () => props.open,
  async isOpen => {
    if (!isOpen) return
    validationError.value = ''
    editorMode.value = 'visual'

    if (props.editingId && props.initialRecord) {
      const rec = props.initialRecord
      form.domain = rec.domain as ConfigurationDomain
      form.code = rec.code
      form.name = rec.name
      form.description = rec.description || ''
      form.effectiveFrom = toLocalInput(rec.effectiveFrom)
      form.effectiveTo = toLocalInput(rec.effectiveTo)
      form.contentJson = rec.contentJson
      parseJsonToVisual(form.domain, rec.contentJson)
    } else {
      form.domain = props.defaultDomain || 'Leave'
      form.effectiveFrom = toLocalInput(new Date().toISOString())
      form.effectiveTo = ''
      await loadBaseConfigForDomain(form.domain)
    }
  }
)

async function saveDraft() {
  validationError.value = ''
  store.error = ''

  let finalJson = form.contentJson
  if (editorMode.value === 'visual') {
    finalJson = syncVisualToJson()
  }

  const err = validateConfigDraft(
    {
      name: form.name,
      code: form.code,
      effectiveFrom: form.effectiveFrom,
      effectiveTo: form.effectiveTo,
      isEditing: !!props.editingId
    },
    editorMode.value,
    finalJson
  )
  if (err) {
    validationError.value = err
    return
  }

  const effectiveFromIso = new Date(form.effectiveFrom).toISOString()
  const effectiveToIso = form.effectiveTo ? new Date(form.effectiveTo).toISOString() : null

  let success = false
  if (props.editingId) {
    const result = await store.updateDraft(props.editingId, {
      name: form.name.trim(),
      description: form.description.trim() || null,
      effectiveFrom: effectiveFromIso,
      effectiveTo: effectiveToIso,
      contentJson: finalJson,
      concurrencyVersion: props.editingConcurrencyVersion
    })
    success = !!result
  } else {
    const result = await store.createDraft({
      domain: form.domain,
      code: form.code.trim(),
      name: form.name.trim(),
      description: form.description.trim() || null,
      effectiveFrom: effectiveFromIso,
      effectiveTo: effectiveToIso,
      contentJson: finalJson
    })
    success = !!result
  }

  if (success) {
    emit('saved')
    emit('close')
  }
}
</script>

<template>
  <OaDialog
    :open="open"
    :title="editingId ? `编辑配置草稿 (v${editingVersion})` : '新建业务配置草稿'"
    :description="editingId ? `${form.domain} - ${form.code}` : '配置先保存为草稿，发布后正式生效并锁定历史版本不可直接修改。'"
    submit-label="保存草稿"
    :busy="store.saving"
    width="920px"
    @close="emit('close')"
    @submit="saveDraft"
  >
    <div class="form-grid-2">
      <label class="dialog-field">
        业务域
        <select v-model="form.domain" :disabled="!!editingId" @change="handleDomainChange">
          <option value="Leave">休假规则 (Leave)</option>
          <option value="Expense">费用报销 (Expense)</option>
          <option value="Travel">差旅标准 (Travel)</option>
          <option value="Procurement">采购限额 (Procurement)</option>
          <option value="Seal">用印规则 (Seal)</option>
          <option value="Dictionary">业务字典 (Dictionary)</option>
        </select>
      </label>

      <label class="dialog-field">
        配置唯一标识 (Code)
        <input
          v-model="form.code"
          :disabled="!!editingId"
          maxlength="64"
          placeholder="例如: LeavePolicy, ExpensePolicy"
        />
      </label>
    </div>

    <div class="form-grid-2">
      <label class="dialog-field">
        配置名称
        <input v-model="form.name" maxlength="128" placeholder="请输入直观名称，如：全员休假规则" />
      </label>

      <label class="dialog-field">
        生效起始时间
        <input v-model="form.effectiveFrom" type="datetime-local" />
      </label>
    </div>

    <div class="form-grid-2">
      <label class="dialog-field">
        生效失效时间 (可选)
        <input v-model="form.effectiveTo" type="datetime-local" />
        <small>留空表示长期有效</small>
      </label>

      <label class="dialog-field">
        配置描述说明
        <input v-model="form.description" maxlength="500" placeholder="简述该配置适用范围或变更背景" />
      </label>
    </div>

    <!-- Parameter Config Header & Switch -->
    <div class="param-section-heading">
      <div>
        <strong>业务参数规则配置</strong>
        <small>支持通过结构化可视化表单配置，或直接编辑原始 JSON</small>
      </div>
      <div class="mode-switch-group">
        <button
          v-if="!editingId"
          type="button"
          class="switch-btn"
          title="从系统当前已有的生效配置复制所有参数内容"
          @click="copyFromExistingConfig"
        >
          📋 复制已有配置
        </button>
        <button
          v-if="!editingId"
          type="button"
          class="switch-btn"
          title="重置为系统出厂预设模板"
          @click="resetToDefaultTemplate"
        >
          🔄 默认模板
        </button>
        <button
          type="button"
          class="switch-btn"
          :class="{ active: editorMode === 'visual' }"
          @click="switchEditorMode('visual')"
        >
          可视化表单
        </button>
        <button
          type="button"
          class="switch-btn"
          :class="{ active: editorMode === 'json' }"
          @click="switchEditorMode('json')"
        >
          原始 JSON
        </button>
      </div>
    </div>

    <!-- Visual Domain Editors -->
    <div v-if="editorMode === 'visual'" class="visual-editor-container">
      <LeavePolicyEditor v-if="form.domain === 'Leave'" :model-value="leaveForm" />
      <ExpensePolicyEditor v-else-if="form.domain === 'Expense'" :model-value="expenseForm" />
      <TravelPolicyEditor
        v-else-if="form.domain === 'Travel'"
        :model-value="travelForm"
        :tier-inputs="travelTierInputs"
        v-model:ranks-str="travelRanksStr"
      />
      <ProcurementPolicyEditor v-else-if="form.domain === 'Procurement'" :model-value="procurementForm" />
      <SealPolicyEditor v-else-if="form.domain === 'Seal'" :model-value="sealForm" />
      <DictionaryPolicyEditor v-else-if="form.domain === 'Dictionary'" :model-value="dictionaryForm" />
    </div>

    <!-- Mode 2: Raw JSON Editor -->
    <div v-else class="raw-json-editor-container">
      <textarea
        v-model="form.contentJson"
        class="raw-json-textarea"
        rows="18"
        spellcheck="false"
        placeholder="在此直接编辑原始配置 JSON 规则快照…"
      />
    </div>

    <p v-if="validationError || store.error" class="dialog-error">{{ validationError || store.error }}</p>
  </OaDialog>
</template>
