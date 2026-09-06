<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import FlowInstanceTimeline from '../components/FlowInstanceTimeline.vue'
import OaDialog from '../components/OaDialog.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useFileStore } from '../stores/files'
import { useSealStore } from '../stores/seal'
import { useUiStore } from '../stores/ui'
import { useEffectiveConfigurationStore } from '../stores/effective-configurations'
import { businessStatusLabel } from '../utils/businessLabels'

const props = defineProps<{ id: string }>()
const app = useAppStore()
const auth = useAuthStore()
const employees = useEmployeeDirectoryStore()
const files = useFileStore()
const seal = useSealStore()
const ui = useUiStore()
const effectiveConfig = useEffectiveConfigurationStore()
const router = useRouter()

const loading = ref(false)
const executionOpen = ref(false)
const returnOpen = ref(false)

const canManage = computed(() => auth.currentUser?.permissions?.includes('SEAL_MANAGE') === true)
const copyNames = computed(() =>
  (seal.sealDetail?.copyRecipientIds ?? [])
    .map(id => employees.employees.find(item => item.id === id)?.name ?? id)
    .join('、') || '无'
)

function getSealDisplayName(codeOrName?: string) {
  if (!codeOrName) return ''
  const item = effectiveConfig.seals.find(s => s.code === codeOrName || s.name === codeOrName || s.aliases?.includes(codeOrName))
  return item?.name || codeOrName
}

function getDocumentCategoryDisplayName(codeOrName?: string) {
  if (!codeOrName) return ''
  const item = effectiveConfig.sealDocumentCategories.find(c => c.code === codeOrName || c.name === codeOrName || c.aliases?.includes(codeOrName))
  return item?.name || codeOrName
}

async function load() {
  loading.value = true
  ui.error = ''
  try {
    await seal.loadSealDetail(props.id)
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '用章详情加载失败。'
  } finally {
    loading.value = false
  }
}

async function uploadExecutionFiles(event: Event) {
  try {
    seal.executionForm.attachments.push(
      ...await Promise.all(Array.from((event.target as HTMLInputElement).files ?? []).map(file => files.uploadFile(file)))
    )
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '附件上传失败。'
  }
}

async function uploadReturnFiles(event: Event) {
  try {
    seal.returnForm.attachments.push(
      ...await Promise.all(Array.from((event.target as HTMLInputElement).files ?? []).map(file => files.uploadFile(file)))
    )
  } catch (cause) {
    ui.error = cause instanceof Error ? cause.message : '附件上传失败。'
  }
}

async function submitExecution() {
  if (seal.sealDetail && await app.registerSealExecution(seal.sealDetail)) {
    executionOpen.value = false
  }
}

async function submitReturn() {
  if (seal.sealDetail && await app.registerSealReturn(seal.sealDetail)) {
    returnOpen.value = false
  }
}

onMounted(() => {
  void effectiveConfig.load()
  void load()
})
</script>

<template>
  <p v-if="ui.message" class="notice success">{{ ui.message }}</p>
  <p v-if="ui.error" class="notice error">{{ ui.error }}</p>

  <section class="panel detail-page">
    <div class="section-title">
      <div>
        <p class="eyebrow">SEAL DETAIL</p>
        <h2>用章申请详情</h2>
      </div>
      <button class="secondary" @click="router.push('/seal')">← 返回列表</button>
    </div>

    <p v-if="loading" class="empty">正在加载详情…</p>
    <template v-else-if="seal.sealDetail">
      <div class="detail-status">
        <strong>{{ seal.sealDetail.number }} · {{ seal.sealDetail.title }}</strong>
        <em>{{ businessStatusLabel(seal.sealDetail.status) }}</em>
      </div>

      <dl class="detail-grid">
        <div><dt>申请人</dt><dd>{{ seal.sealDetail.applicantName }}</dd></div>
        <div><dt>所属部门</dt><dd>{{ seal.sealDetail.departmentName }}</dd></div>
        <div><dt>印章类型</dt><dd>{{ getSealDisplayName(seal.sealDetail.sealType) }}</dd></div>
        <div><dt>文件类别</dt><dd>{{ getDocumentCategoryDisplayName(seal.sealDetail.documentCategory) }}</dd></div>
        <div><dt>文件名称</dt><dd>{{ seal.sealDetail.documentName }}</dd></div>
        <div><dt>用印份数</dt><dd>{{ seal.sealDetail.copies }} 份</dd></div>
        <div><dt>使用方式</dt><dd>{{ seal.sealDetail.isOut ? '外带借出' : '在司用印' }}</dd></div>
        <div v-if="seal.sealDetail.isOut"><dt>外带保管人</dt><dd>{{ seal.sealDetail.outCustodian || '未指定' }}</dd></div>
        <div v-if="seal.sealDetail.isOut"><dt>预计借还</dt><dd>{{ seal.sealDetail.outStartDate }} 至 {{ seal.sealDetail.outEndDate }}</dd></div>
        <div><dt>流程定义</dt><dd>{{ seal.sealDetail.processDefinitionCode ? `${seal.sealDetail.processDefinitionCode} v${seal.sealDetail.processDefinitionVersion}` : '尚未提交' }}</dd></div>
        <div class="wide"><dt>用印事由</dt><dd>{{ seal.sealDetail.reason }}</dd></div>
        <div class="wide"><dt>抄送人</dt><dd>{{ copyNames }}</dd></div>
        <div class="wide"><dt>申请附件</dt>
          <dd v-if="seal.sealDetail.attachments.length">
            <button
              v-for="fileId in seal.sealDetail.attachments"
              :key="fileId"
              class="secondary attachment-button"
              @click="files.downloadAttachment(fileId, 'seal', seal.sealDetail!.id)"
            >
              下载附件 {{ fileId.slice(0, 8) }}
            </button>
          </dd>
          <dd v-else>无</dd>
        </div>
      </dl>

      <!-- 印章执行记录 -->
      <section v-if="seal.sealDetail.execution" class="embedded-card">
        <h3>{{ seal.sealDetail.isOut ? '外带借出记录' : '用印执行记录' }}</h3>
        <dl class="detail-grid">
          <div><dt>执行日期</dt><dd>{{ seal.sealDetail.execution.executedDate }}</dd></div>
          <div><dt>经办人</dt><dd>{{ seal.sealDetail.execution.operatorName }}</dd></div>
          <div><dt>登记人</dt><dd>{{ seal.sealDetail.execution.createdByName }}</dd></div>
          <div><dt>登记时间</dt><dd>{{ seal.sealDetail.execution.createdAt }}</dd></div>
          <div class="wide"><dt>执行备注</dt><dd>{{ seal.sealDetail.execution.notes || '无' }}</dd></div>
          <div class="wide"><dt>盖章/借出证明附件</dt>
            <dd v-if="seal.sealDetail.execution.attachments.length">
              <button
                v-for="fileId in seal.sealDetail.execution.attachments"
                :key="fileId"
                class="secondary attachment-button"
                @click="files.downloadAttachment(fileId, 'seal', seal.sealDetail!.id)"
              >
                下载附件 {{ fileId.slice(0, 8) }}
              </button>
            </dd>
            <dd v-else>无</dd>
          </div>
        </dl>
      </section>

      <!-- 归还记录 -->
      <section v-if="seal.sealDetail.return" class="embedded-card">
        <h3>印章归还核验记录</h3>
        <dl class="detail-grid">
          <div><dt>归还日期</dt><dd>{{ seal.sealDetail.return.returnDate }}</dd></div>
          <div><dt>印章状态</dt><dd>{{ seal.sealDetail.return.sealCondition === 'INTACT' ? '完好无损' : (seal.sealDetail.return.sealCondition === 'DAMAGED' ? '破损异常' : '印章丢失') }}</dd></div>
          <div><dt>核验接收人</dt><dd>{{ seal.sealDetail.return.receiverName }}</dd></div>
          <div><dt>登记人</dt><dd>{{ seal.sealDetail.return.createdByName }}</dd></div>
          <div class="wide"><dt>核验说明</dt><dd>{{ seal.sealDetail.return.notes || '无' }}</dd></div>
          <div class="wide"><dt>归还核验附件</dt>
            <dd v-if="seal.sealDetail.return.attachments.length">
              <button
                v-for="fileId in seal.sealDetail.return.attachments"
                :key="fileId"
                class="secondary attachment-button"
                @click="files.downloadAttachment(fileId, 'seal', seal.sealDetail!.id)"
              >
                下载附件 {{ fileId.slice(0, 8) }}
              </button>
            </dd>
            <dd v-else>无</dd>
          </div>
        </dl>
      </section>

      <!-- 流程时间线 -->
      <FlowInstanceTimeline
        :instances="seal.sealDetail.flowInstances ?? []"
        :current-id="seal.sealDetail.currentFlowInstanceId"
      />

      <!-- 审批任务列表 -->
      <div v-if="seal.sealDetail.tasks?.length" class="timeline">
        <h3>当前实例任务</h3>
        <p v-for="task in seal.sealDetail.tasks" :key="task.id">
          第 {{ task.sequence }} 节点 · {{ task.assigneeName }}
          <span v-if="task.originalAssigneeName">（代 {{ task.originalAssigneeName }} 审批）</span>
          · {{ businessStatusLabel(task.status) }}
          <span v-if="task.comment">：{{ task.comment }}</span>
        </p>
      </div>

      <!-- 详情页操作按钮 -->
      <div class="detail-actions">
        <button
          v-if="seal.sealDetail.status === 'Approving' && seal.sealDetail.applicantId === auth.currentUserId"
          :disabled="seal.operationBusy"
          @click="app.withdrawSeal(seal.sealDetail)"
        >
          撤回申请
        </button>
        <button
          v-if="seal.sealDetail.status === 'Approved' && canManage"
          :disabled="seal.operationBusy"
          @click="executionOpen = true"
        >
          {{ seal.sealDetail.isOut ? '登记借出出库' : '登记用印盖章' }}
        </button>
        <button
          v-if="seal.sealDetail.status === 'Out' && canManage"
          :disabled="seal.operationBusy"
          @click="returnOpen = true"
        >
          登记外带归还
        </button>
      </div>
    </template>
    <p v-else class="empty">未找到该记录，可能已被删除或当前无查看权限。</p>
  </section>

  <!-- 登记执行弹窗 -->
  <OaDialog
    :open="executionOpen"
    :title="seal.sealDetail?.isOut ? '登记印章外带借出' : '登记在司用印盖章'"
    :description="seal.sealDetail?.isOut ? '确认借出日期与领用保管人。' : '确认用印已由管理员盖章完毕。'"
    submit-label="确认登记"
    :busy="seal.operationBusy"
    @close="executionOpen = false"
    @submit="submitExecution"
  >
    <div class="dialog-grid">
      <label class="dialog-field">
        {{ seal.sealDetail?.isOut ? '借出日期' : '用印日期' }}
        <input v-model="seal.executionForm.executedDate" type="date">
      </label>
      <label class="dialog-field">
        经办/领用人姓名
        <input v-model="seal.executionForm.operatorName" maxlength="64" placeholder="经办人或领用人">
      </label>
    </div>
    <label class="dialog-field">
      备注说明
      <textarea v-model="seal.executionForm.notes" maxlength="500" placeholder="说明印章交接或用印情况"></textarea>
    </label>
    <label class="dialog-field">
      现场盖章照或交接凭证
      <input type="file" multiple @change="uploadExecutionFiles">
      <small>已上传 {{ seal.executionForm.attachments.length }} 个附件</small>
    </label>
  </OaDialog>

  <!-- 登记归还弹窗 -->
  <OaDialog
    :open="returnOpen"
    title="登记印章外带归还"
    description="检查印章实体印面、字迹是否完好，确认验收入库。"
    submit-label="确认归还"
    :busy="seal.operationBusy"
    @close="returnOpen = false"
    @submit="submitReturn"
  >
    <div class="dialog-grid">
      <label class="dialog-field">
        归还日期
        <input v-model="seal.returnForm.returnDate" type="date">
      </label>
      <label class="dialog-field">
        核验结果
        <select v-model="seal.returnForm.sealCondition">
          <option value="INTACT">完好无损</option>
          <option value="DAMAGED">破损异常</option>
          <option value="LOST">印章丢失</option>
        </select>
      </label>
      <label class="dialog-field">
        核验接收人
        <input v-model="seal.returnForm.receiverName" maxlength="64" placeholder="印章管理员姓名">
      </label>
    </div>
    <label class="dialog-field">
      核验说明
      <textarea v-model="seal.returnForm.notes" maxlength="500" placeholder="说明印章实体及印油盒核验情况"></textarea>
    </label>
    <label class="dialog-field">
      归还核验证明附件
      <input type="file" multiple @change="uploadReturnFiles">
      <small>已上传 {{ seal.returnForm.attachments.length }} 个附件</small>
    </label>
  </OaDialog>
</template>
