<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { BusinessConfigurationRecord } from '../../api/types'
import OaDialog from '../../components/OaDialog.vue'
import { useOrganizationStore } from '../../stores/organization'
import {
  domainLabel,
  formatDate,
  formatDateTime,
  getRiskBadgeClass,
  getRiskLevelLabel,
  normalizeDomainConfig,
  statusClass,
  statusLabel
} from './useBusinessConfigEditor'

const props = defineProps<{
  open: boolean
  record: BusinessConfigurationRecord | null
}>()

const emit = defineEmits<{
  close: []
}>()

const organization = useOrganizationStore()
const detailMode = ref<'visual' | 'json'>('visual')
const copySuccess = ref(false)

watch(
  () => props.open,
  isOpen => {
    if (isOpen) {
      detailMode.value = 'visual'
      copySuccess.value = false
    }
  }
)

const detailParsed = computed(() => {
  if (!props.record?.contentJson) return null
  try {
    const raw = JSON.parse(props.record.contentJson)
    return normalizeDomainConfig(props.record.domain, raw) || raw
  } catch {
    return null
  }
})

const formattedJson = computed(() => {
  if (!props.record?.contentJson) return ''
  try {
    return JSON.stringify(JSON.parse(props.record.contentJson), null, 2)
  } catch {
    return props.record.contentJson
  }
})

async function copyJson() {
  if (!formattedJson.value) return
  try {
    await navigator.clipboard.writeText(formattedJson.value)
    copySuccess.value = true
    setTimeout(() => {
      copySuccess.value = false
    }, 2000)
  } catch {
    // fallback
  }
}

function getUserDisplayName(userId?: string | null): string {
  if (!userId) return '系统自动指派'
  const emp = organization.directoryEmployees.find(e => e.id === userId)
  if (emp) {
    return `${emp.name} (${emp.departmentName || '员工'} · ${emp.id})`
  }
  return userId
}
</script>

<template>
  <OaDialog
    :open="open"
    :title="`配置详情 - ${record?.name} (v${record?.version})`"
    :description="`${record?.domain} · ${record?.code}`"
    submit-label="关闭"
    width="880px"
    @close="emit('close')"
    @submit="emit('close')"
  >
    <dl v-if="record" class="detail-grid">
      <div>
        <dt>业务域</dt>
        <dd>{{ domainLabel(record.domain) }} ({{ record.domain }})</dd>
      </div>
      <div>
        <dt>配置标识</dt>
        <dd><code>{{ record.code }}</code></dd>
      </div>
      <div>
        <dt>版本状态</dt>
        <dd>
          <span class="status-badge" :class="statusClass(record.status)">
            {{ statusLabel(record.status) }} (v{{ record.version }})
          </span>
        </dd>
      </div>
      <div>
        <dt>单据引用统计</dt>
        <dd>
          <span v-if="record.referenceCount > 0" class="ref-count-tag">
            被 {{ record.referenceCount }} 笔单据冻结快照
          </span>
          <span v-else class="text-muted">暂无业务单据引用</span>
        </dd>
      </div>
      <div>
        <dt>生效区间</dt>
        <dd>{{ formatDate(record.effectiveFrom) }} 至 {{ formatDate(record.effectiveTo) }}</dd>
      </div>
      <div>
        <dt>创建信息</dt>
        <dd>{{ record.createdByName }} · {{ formatDateTime(record.createdAt) }}</dd>
      </div>
      <div>
        <dt>发布信息</dt>
        <dd>{{ record.publishedByName || '未发布' }} · {{ formatDateTime(record.publishedAt) }}</dd>
      </div>
      <div>
        <dt>更新信息</dt>
        <dd>{{ record.updatedByName }} · {{ formatDateTime(record.updatedAt) }}</dd>
      </div>
      <div class="wide">
        <dt>配置描述</dt>
        <dd>{{ record.description || '无' }}</dd>
      </div>
    </dl>

    <!-- Parameter Config Header & Switch -->
    <div class="param-section-heading">
      <div>
        <strong>业务参数生效配置</strong>
        <small>当前版本所固化的生效规则与快照回显</small>
      </div>
      <div class="detail-switch-bar">
        <div class="mode-switch-group">
          <button
            type="button"
            class="switch-btn"
            :class="{ active: detailMode === 'visual' }"
            @click="detailMode = 'visual'"
          >
            结构化 UI 回显
          </button>
          <button
            type="button"
            class="switch-btn"
            :class="{ active: detailMode === 'json' }"
            @click="detailMode = 'json'"
          >
            原始 JSON 快照
          </button>
        </div>
        <button type="button" class="copy-json-btn" @click="copyJson">
          {{ copySuccess ? '✓ 已复制！' : '复制 JSON' }}
        </button>
      </div>
    </div>

    <!-- Mode 1: Visual UI Echo -->
    <div v-if="detailMode === 'visual'" class="detail-visual-wrapper">
      <div v-if="!detailParsed" class="parse-fallback-note">
        <span>无法以结构化形式解析此配置内容，请切换至“原始 JSON 快照”查看。</span>
        <button type="button" class="secondary" @click="detailMode = 'json'">切换为 JSON 查看</button>
      </div>

      <!-- 1. LEAVE -->
      <template v-else-if="record?.domain === 'Leave'">
        <div class="card-section">
          <div class="section-subheading">通用结转与年假奖励规则</div>
          <div class="detail-kv-grid">
            <div class="detail-kv-item">
              <span class="kv-label">跨年结转</span>
              <span class="kv-value">
                <span class="status-pill" :class="detailParsed.allowCrossYear ? 'status-enabled' : 'status-disabled'">
                  {{ detailParsed.allowCrossYear ? '允许跨年结转' : '当年清零，禁止结转' }}
                </span>
              </span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">加班调休有效期</span>
              <span class="kv-value"><strong>{{ detailParsed.compTimeValidityDays ?? 365 }}</strong> 天</span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">法定最低标准保护</span>
              <span class="kv-value">
                <span class="status-pill" :class="detailParsed.annualLeaveBonus?.legalMinStandardProtected !== false ? 'status-enabled' : 'status-disabled'">
                  {{ detailParsed.annualLeaveBonus?.legalMinStandardProtected !== false ? '强制法定最低标准' : '未开启' }}
                </span>
              </span>
            </div>
          </div>

          <div class="bonus-tiers-readonly" style="margin-top: 10px;">
            <span class="bonus-title">司龄年假奖励阶梯：</span>
            <div class="bonus-tags-group">
              <span class="tier-pill">1~3年司龄: +{{ detailParsed.annualLeaveBonus?.tier1BonusDays ?? 0 }} 天</span>
              <span class="tier-pill">3~5年司龄: +{{ detailParsed.annualLeaveBonus?.tier2BonusDays ?? 0 }} 天</span>
              <span class="tier-pill">5年以上司龄: +{{ detailParsed.annualLeaveBonus?.tier3BonusDays ?? 0 }} 天</span>
            </div>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">假期类型定义名录 (共 {{ (detailParsed.leaveTypes || []).length }} 项)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>假期编码</th>
                  <th>假期名称</th>
                  <th>状态</th>
                  <th>最小单位</th>
                  <th>需附证明</th>
                  <th>证明门槛说明</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(lt, idx) in (detailParsed.leaveTypes || [])" :key="idx">
                  <td><code>{{ lt.type }}</code></td>
                  <td><strong>{{ lt.name }}</strong></td>
                  <td>
                    <span class="status-pill" :class="lt.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ lt.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                  <td>{{ lt.minUnit }} 天</td>
                  <td>{{ lt.requiresAttachment ? '是' : '否' }}</td>
                  <td>
                    <span v-if="lt.requiresAttachment">
                      {{ lt.attachmentThresholdDays != null ? `超过 ${lt.attachmentThresholdDays} 天必须上传` : '申请即必传证明' }}
                    </span>
                    <span v-else class="text-muted">无需证明</span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.leaveTypes?.length">
                  <td colspan="6" class="text-center text-muted">暂无假期类型配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 2. EXPENSE -->
      <template v-else-if="record?.domain === 'Expense'">
        <div class="card-section">
          <div class="section-subheading">费用报销类别与限额管控标准 (共 {{ (detailParsed.categories || []).length }} 项)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>类别名称</th>
                  <th>状态</th>
                  <th>单笔限额标准</th>
                  <th>发票凭证</th>
                  <th>超限填理由</th>
                  <th>超限阻断策略</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(cat, idx) in (detailParsed.categories || [])" :key="idx">
                  <td><strong>{{ cat.name }}</strong></td>
                  <td>
                    <span class="status-pill" :class="cat.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ cat.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                  <td>
                    <span v-if="cat.singleLimit != null" class="amount-text">¥{{ Number(cat.singleLimit).toLocaleString() }}</span>
                    <span v-else class="text-muted">不设上限</span>
                  </td>
                  <td>{{ cat.requiresReceipt ? '必须附发票' : '可选' }}</td>
                  <td>{{ cat.requiresReasonWhenExceeded ? '超限必填' : '不强制' }}</td>
                  <td>
                    <span class="risk-badge" :class="cat.blockWhenExceeded ? 'risk-high' : 'risk-low'">
                      {{ cat.blockWhenExceeded ? '超限直接阻断' : '超限允许提交' }}
                    </span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.categories?.length">
                  <td colspan="6" class="text-center text-muted">暂无报销类别配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 3. TRAVEL -->
      <template v-else-if="record?.domain === 'Travel'">
        <div class="card-section">
          <div class="section-subheading">城市级别划分与适用职级</div>
          <div class="travel-tiers-display">
            <div v-for="(tier, idx) in (detailParsed.cityTiers || [])" :key="idx" class="travel-tier-card">
              <div class="tier-card-header">
                <strong>{{ tier.tierName }}</strong>
                <span class="tag-count">({{ (tier.cities || []).length }} 个城市)</span>
              </div>
              <div class="tier-card-cities">
                <span v-for="c in (tier.cities || [])" :key="c" class="tag-badge">{{ c }}</span>
                <span v-if="!tier.cities?.length" class="text-muted">未配置城市</span>
              </div>
            </div>
          </div>

          <div class="ranks-display" style="margin-top: 12px;">
            <span class="kv-label">适用员工职级体系：</span>
            <div class="tag-list-inline">
              <span v-for="r in (detailParsed.employeeRanks || [])" :key="r" class="rank-pill">{{ r }}</span>
              <span v-if="!detailParsed.employeeRanks?.length" class="text-muted">全员通用</span>
            </div>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">差旅报销标准矩阵 (共 {{ (detailParsed.standards || []).length }} 条)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>城市级别</th>
                  <th>适用职级</th>
                  <th>住宿每晚限额</th>
                  <th>每日餐补标准</th>
                  <th>交通工具标准</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(std, idx) in (detailParsed.standards || [])" :key="idx">
                  <td><strong>{{ std.cityTier }}</strong></td>
                  <td><span class="rank-pill">{{ std.rank }}</span></td>
                  <td>
                    <span v-if="std.hotelDailyLimit != null" class="amount-text">¥{{ Number(std.hotelDailyLimit).toLocaleString() }} / 晚</span>
                    <span v-else class="text-muted">-</span>
                  </td>
                  <td>
                    <span v-if="std.mealDailyAllowance != null" class="amount-text">¥{{ Number(std.mealDailyAllowance).toLocaleString() }} / 天</span>
                    <span v-else class="text-muted">-</span>
                  </td>
                  <td>{{ std.transportationStandard || '-' }}</td>
                </tr>
                <tr v-if="!detailParsed.standards?.length">
                  <td colspan="5" class="text-center text-muted">暂无差旅标准配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 4. PROCUREMENT -->
      <template v-else-if="record?.domain === 'Procurement'">
        <div class="card-section">
          <div class="section-subheading">采购风控与流程参数</div>
          <div class="detail-kv-grid">
            <div class="detail-kv-item">
              <span class="kv-label">报价比价起计门槛</span>
              <span class="kv-value">
                <strong v-if="detailParsed.quoteAttachmentThreshold != null" class="amount-text">
                  ≥ ¥{{ Number(detailParsed.quoteAttachmentThreshold).toLocaleString() }}
                </strong>
                <span v-else class="text-muted">不限</span>
                <small class="hint-inline">（超额必须上传比价单）</small>
              </span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">默认采购专员</span>
              <span class="kv-value">
                <span v-if="detailParsed.defaultPurchaserUserId" class="user-chip">
                  <span class="user-avatar-mini">{{ detailParsed.defaultPurchaserUserId.slice(0, 1).toUpperCase() }}</span>
                  <strong>{{ getUserDisplayName(detailParsed.defaultPurchaserUserId) }}</strong>
                </span>
                <span v-else class="text-muted">系统自动指派</span>
              </span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">强制到货验收流程</span>
              <span class="kv-value">
                <span class="status-pill" :class="detailParsed.requiresAcceptance ? 'status-enabled' : 'status-disabled'">
                  {{ detailParsed.requiresAcceptance ? '强制要求' : '无需独立验收' }}
                </span>
              </span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">验收负责人/角色</span>
              <span class="kv-value">{{ detailParsed.acceptanceRoleOrAssignee || '部门负责人' }}</span>
            </div>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">采购品类定义 (共 {{ (detailParsed.categories || []).length }} 项)</div>
          <div class="tag-list-inline">
            <span
              v-for="(cat, idx) in (detailParsed.categories || [])"
              :key="idx"
              class="tag-badge"
              :class="{ 'tag-disabled': !cat.isEnabled }"
            >
              {{ cat.name }}
              <span v-if="!cat.isEnabled" class="text-muted"> (停用)</span>
            </span>
            <span v-if="!detailParsed.categories?.length" class="text-muted">暂无采购品类</span>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">采购金额审批层级 (共 {{ (detailParsed.amountTiers || []).length }} 级)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>层级名称</th>
                  <th>最高金额上限 (元)</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(tier, idx) in (detailParsed.amountTiers || [])" :key="idx">
                  <td><strong>{{ tier.name }}</strong></td>
                  <td>
                    <span v-if="tier.maxAmount != null" class="amount-text">≤ ¥{{ Number(tier.maxAmount).toLocaleString() }}</span>
                    <span v-else class="text-muted">无上限 (重大采购决策)</span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.amountTiers?.length">
                  <td colspan="2" class="text-center text-muted">暂无金额层级配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- 5. SEAL -->
      <template v-else-if="record?.domain === 'Seal'">
        <div class="card-section">
          <div class="section-subheading">印章名录与外带管控 (共 {{ (detailParsed.seals || []).length }} 枚)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>印章名称</th>
                  <th>印章类型</th>
                  <th>指定保管人</th>
                  <th>状态</th>
                  <th>外带借出</th>
                  <th>最长借出天数</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(s, idx) in (detailParsed.seals || [])" :key="idx">
                  <td><strong>{{ s.name }}</strong></td>
                  <td><code>{{ s.sealType }}</code></td>
                  <td>
                    <span v-if="s.custodianUserId" class="user-chip">
                      <span class="user-avatar-mini">{{ s.custodianUserId.slice(0, 1).toUpperCase() }}</span>
                      {{ getUserDisplayName(s.custodianUserId) }}
                    </span>
                    <span v-else class="text-muted">-</span>
                  </td>
                  <td>
                    <span class="status-pill" :class="s.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ s.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                  <td>
                    <span class="status-pill" :class="s.allowOut ? 'status-enabled' : 'status-disabled'">
                      {{ s.allowOut ? '允许外带' : '禁止外借' }}
                    </span>
                  </td>
                  <td>
                    <span v-if="s.allowOut"><strong>{{ s.maxOutDays ?? 0 }}</strong> 天</span>
                    <span v-else class="text-muted">-</span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.seals?.length">
                  <td colspan="6" class="text-center text-muted">暂无印章配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">用印文件类别与风险等级定义 (共 {{ (detailParsed.documentCategories || []).length }} 项)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>文件类别</th>
                  <th>风险等级</th>
                  <th>状态</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(dc, idx) in (detailParsed.documentCategories || [])" :key="idx">
                  <td><strong>{{ dc.name }}</strong></td>
                  <td>
                    <span class="risk-badge" :class="getRiskBadgeClass(dc.riskLevel)">
                      {{ getRiskLevelLabel(dc.riskLevel) }}
                    </span>
                  </td>
                  <td>
                    <span class="status-pill" :class="dc.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ dc.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                </tr>
                <tr v-if="!detailParsed.documentCategories?.length">
                  <td colspan="3" class="text-center text-muted">暂无文件类别配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div class="card-section" style="margin-top: 12px;">
          <div class="section-subheading">综合风险评估指标分值</div>
          <div class="detail-kv-grid">
            <div class="detail-kv-item">
              <span class="kv-label">高风险指标分值</span>
              <span class="kv-value"><strong class="text-danger">{{ detailParsed.riskRules?.highRiskMetric ?? 3 }}</strong></span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">中风险指标分值</span>
              <span class="kv-value"><strong class="text-warning">{{ detailParsed.riskRules?.mediumRiskMetric ?? 2 }}</strong></span>
            </div>
            <div class="detail-kv-item">
              <span class="kv-label">低风险指标分值</span>
              <span class="kv-value"><strong class="text-success">{{ detailParsed.riskRules?.lowRiskMetric ?? 1 }}</strong></span>
            </div>
          </div>
        </div>
      </template>

      <!-- 6. DICTIONARY -->
      <template v-else-if="record?.domain === 'Dictionary'">
        <div class="card-section">
          <div class="section-subheading">业务字典枚举项清单 (共 {{ (detailParsed.items || []).length }} 项)</div>
          <div class="table-wrap compact">
            <table class="inner-table readonly-table">
              <thead>
                <tr>
                  <th>排序</th>
                  <th>项编码</th>
                  <th>显示名称</th>
                  <th>状态</th>
                  <th>说明备注</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(it, idx) in (detailParsed.items || [])" :key="idx">
                  <td>{{ it.sortOrder ?? (Number(idx) + 1) }}</td>
                  <td><code>{{ it.code }}</code></td>
                  <td><strong>{{ it.name }}</strong></td>
                  <td>
                    <span class="status-pill" :class="it.isEnabled ? 'status-enabled' : 'status-disabled'">
                      {{ it.isEnabled ? '启用' : '停用' }}
                    </span>
                  </td>
                  <td>{{ it.description || '-' }}</td>
                </tr>
                <tr v-if="!detailParsed.items?.length">
                  <td colspan="5" class="text-center text-muted">暂无字典项配置</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </template>

      <!-- Other / Custom -->
      <template v-else>
        <div class="card-section">
          <div class="section-subheading">自定义配置参数快照</div>
          <pre class="json-code-block"><code>{{ formattedJson }}</code></pre>
        </div>
      </template>
    </div>

    <!-- Mode 2: Raw JSON Snapshot -->
    <div v-else class="detail-json-wrapper">
      <pre class="json-code-block"><code>{{ formattedJson }}</code></pre>
    </div>
  </OaDialog>
</template>
