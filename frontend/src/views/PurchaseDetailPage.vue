<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import FlowInstanceTimeline from '../components/FlowInstanceTimeline.vue'
import OaDialog from '../components/OaDialog.vue'
import { useAppStore } from '../stores/app'
import { useAuthStore } from '../stores/auth'
import { useEmployeeDirectoryStore } from '../stores/employee-directory'
import { useFileStore } from '../stores/files'
import { usePurchaseStore } from '../stores/purchase'
import { useUiStore } from '../stores/ui'
import { businessStatusLabel } from '../utils/businessLabels'

const props = defineProps<{ id: string }>()
const app = useAppStore()
const auth = useAuthStore()
const employees = useEmployeeDirectoryStore()
const files = useFileStore()
const purchase = usePurchaseStore()
const ui = useUiStore()
const router = useRouter()
const loading = ref(false); const orderOpen = ref(false); const receiptOpen = ref(false)
const canManage = computed(() => auth.currentUser?.permissions?.includes('PURCHASE_MANAGE') === true)
const canReceive = computed(() => purchase.purchaseDetail?.applicantId === auth.currentUserId || canManage.value)
const copyNames = computed(() => (purchase.purchaseDetail?.copyRecipientIds ?? []).map(id => employees.employees.find(item => item.id === id)?.name ?? id).join('、') || '无')
async function load() { loading.value = true; ui.error = ''; try { await purchase.loadPurchaseDetail(props.id) } catch (cause) { ui.error = cause instanceof Error ? cause.message : '采购详情加载失败。' } finally { loading.value = false } }
async function uploadOrderFiles(event: Event) { try { purchase.orderForm.attachments.push(...await Promise.all(Array.from((event.target as HTMLInputElement).files ?? []).map(file => files.uploadFile(file)))) } catch (cause) { ui.error = cause instanceof Error ? cause.message : '附件上传失败。' } }
async function uploadReceiptFiles(event: Event) { try { purchase.receiptForm.attachments.push(...await Promise.all(Array.from((event.target as HTMLInputElement).files ?? []).map(file => files.uploadFile(file)))) } catch (cause) { ui.error = cause instanceof Error ? cause.message : '附件上传失败。' } }
async function submitOrder() { if (purchase.purchaseDetail && await app.registerPurchaseOrder(purchase.purchaseDetail)) orderOpen.value = false }
async function submitReceipt() { if (purchase.purchaseDetail && await app.receivePurchase(purchase.purchaseDetail)) receiptOpen.value = false }
onMounted(load)
</script>

<template>
  <p v-if="ui.message" class="notice success">{{ ui.message }}</p><p v-if="ui.error" class="notice error">{{ ui.error }}</p>
  <section class="panel detail-page"><div class="section-title"><div><p class="eyebrow">PURCHASE DETAIL</p><h2>采购申请详情</h2></div><button class="secondary" @click="router.push('/purchase')">← 返回列表</button></div>
    <p v-if="loading" class="empty">正在加载详情…</p>
    <template v-else-if="purchase.purchaseDetail"><div class="detail-status"><strong>{{ purchase.purchaseDetail.number }} · {{ purchase.purchaseDetail.title }}</strong><em>{{ businessStatusLabel(purchase.purchaseDetail.status) }}</em></div><dl class="detail-grid"><div><dt>申请人</dt><dd>{{ purchase.purchaseDetail.applicantName }}</dd></div><div><dt>所属部门</dt><dd>{{ purchase.purchaseDetail.departmentName }}</dd></div><div><dt>期望到货</dt><dd>{{ purchase.purchaseDetail.requiredDate }}</dd></div><div><dt>建议供应商</dt><dd>{{ purchase.purchaseDetail.suggestedSupplier || '未指定' }}</dd></div><div><dt>预估总额</dt><dd>¥{{ purchase.purchaseDetail.estimatedTotal.toFixed(2) }}</dd></div><div><dt>流程版本</dt><dd>{{ purchase.purchaseDetail.processDefinitionCode ? `${purchase.purchaseDetail.processDefinitionCode} v${purchase.purchaseDetail.processDefinitionVersion}` : '尚未提交' }}</dd></div><div class="wide"><dt>采购用途</dt><dd>{{ purchase.purchaseDetail.purpose }}</dd></div><div class="wide"><dt>抄送人</dt><dd>{{ copyNames }}</dd></div><div class="wide"><dt>报价或依据附件</dt><dd v-if="purchase.purchaseDetail.attachments.length"><button v-for="fileId in purchase.purchaseDetail.attachments" :key="fileId" class="secondary attachment-button" @click="files.downloadAttachment(fileId, 'purchase', purchase.purchaseDetail!.id)">下载附件 {{ fileId.slice(0, 8) }}</button></dd><dd v-else>无</dd></div></dl>
      <div class="table-wrap"><table class="data-table"><thead><tr><th>品类</th><th>名称</th><th>规格</th><th>数量</th><th>预估单价</th><th>小计</th><th>备注</th></tr></thead><tbody><tr v-for="(item, index) in purchase.purchaseDetail.items" :key="index"><td>{{ item.category }}</td><td>{{ item.name }}</td><td>{{ item.specification || '—' }}</td><td>{{ item.quantity }} {{ item.unit }}</td><td>¥{{ item.estimatedUnitPrice.toFixed(2) }}</td><td>¥{{ item.estimatedAmount.toFixed(2) }}</td><td>{{ item.remark || '—' }}</td></tr></tbody></table></div>
      <section v-if="purchase.purchaseDetail.order" class="embedded-card"><h3>采购订单</h3><dl class="detail-grid"><div><dt>供应商</dt><dd>{{ purchase.purchaseDetail.order.supplier }}</dd></div><div><dt>订单号</dt><dd>{{ purchase.purchaseDetail.order.orderNumber }}</dd></div><div><dt>实际金额</dt><dd>¥{{ purchase.purchaseDetail.order.actualAmount.toFixed(2) }}</dd></div><div><dt>下单日期</dt><dd>{{ purchase.purchaseDetail.order.orderDate }}</dd></div><div><dt>预计交付</dt><dd>{{ purchase.purchaseDetail.order.expectedDeliveryDate }}</dd></div><div><dt>登记人</dt><dd>{{ purchase.purchaseDetail.order.createdByName }}</dd></div><div class="wide"><dt>备注</dt><dd>{{ purchase.purchaseDetail.order.notes || '无' }}</dd></div><div class="wide"><dt>订单附件</dt><dd v-if="purchase.purchaseDetail.order.attachments.length"><button v-for="fileId in purchase.purchaseDetail.order.attachments" :key="fileId" class="secondary attachment-button" @click="files.downloadAttachment(fileId, 'purchase', purchase.purchaseDetail!.id)">下载附件 {{ fileId.slice(0, 8) }}</button></dd><dd v-else>无</dd></div></dl></section>
      <section v-if="purchase.purchaseDetail.receipt" class="embedded-card"><h3>验收记录</h3><dl class="detail-grid"><div><dt>验收日期</dt><dd>{{ purchase.purchaseDetail.receipt.receivedDate }}</dd></div><div><dt>验收结果</dt><dd>全部验收通过</dd></div><div><dt>验收人</dt><dd>{{ purchase.purchaseDetail.receipt.createdByName }}</dd></div><div class="wide"><dt>验收说明</dt><dd>{{ purchase.purchaseDetail.receipt.notes }}</dd></div><div class="wide"><dt>验收附件</dt><dd v-if="purchase.purchaseDetail.receipt.attachments.length"><button v-for="fileId in purchase.purchaseDetail.receipt.attachments" :key="fileId" class="secondary attachment-button" @click="files.downloadAttachment(fileId, 'purchase', purchase.purchaseDetail!.id)">下载附件 {{ fileId.slice(0, 8) }}</button></dd><dd v-else>无</dd></div></dl></section>
      <FlowInstanceTimeline :instances="purchase.purchaseDetail.flowInstances ?? []" :current-id="purchase.purchaseDetail.currentFlowInstanceId" /><div v-if="purchase.purchaseDetail.tasks?.length" class="timeline"><h3>当前实例任务</h3><p v-for="task in purchase.purchaseDetail.tasks" :key="task.id">第 {{ task.sequence }} 节点 · {{ task.assigneeName }}<span v-if="task.originalAssigneeName">（代 {{ task.originalAssigneeName }} 审批）</span> · {{ businessStatusLabel(task.status) }}<span v-if="task.comment">：{{ task.comment }}</span></p></div>
      <div class="detail-actions"><button v-if="purchase.purchaseDetail.status === 'Approving' && purchase.purchaseDetail.applicantId === auth.currentUserId" :disabled="purchase.operationBusy" @click="app.withdrawPurchase(purchase.purchaseDetail)">撤回申请</button><button v-if="purchase.purchaseDetail.status === 'Approved' && canManage" :disabled="purchase.operationBusy" @click="orderOpen = true">登记下单</button><button v-if="purchase.purchaseDetail.status === 'Ordered' && canReceive" :disabled="purchase.operationBusy" @click="receiptOpen = true">登记验收</button></div>
    </template><p v-else class="empty">未找到该记录，可能已被删除或当前无查看权限。</p>
  </section>

  <OaDialog :open="orderOpen" title="登记采购订单" description="实际金额不能超过审批预估金额的 110%。" submit-label="确认下单" :busy="purchase.operationBusy" @close="orderOpen = false" @submit="submitOrder"><div class="dialog-grid"><label class="dialog-field">供应商<input v-model="purchase.orderForm.supplier" maxlength="100"></label><label class="dialog-field">采购订单号<input v-model="purchase.orderForm.orderNumber" maxlength="64"></label><label class="dialog-field">实际金额（元）<input v-model="purchase.orderForm.actualAmount" type="number" min="0.01" step="0.01"></label><label class="dialog-field">下单日期<input v-model="purchase.orderForm.orderDate" type="date"></label><label class="dialog-field">预计交付日期<input v-model="purchase.orderForm.expectedDeliveryDate" type="date"></label></div><label class="dialog-field">备注<textarea v-model="purchase.orderForm.notes" maxlength="500"></textarea></label><label class="dialog-field">订单附件<input type="file" multiple @change="uploadOrderFiles"><small>已上传 {{ purchase.orderForm.attachments.length }} 个附件</small></label></OaDialog>
  <OaDialog :open="receiptOpen" title="登记采购验收" description="当前版本仅支持全部验收通过；部分到货请暂不提交。" submit-label="确认验收" :busy="purchase.operationBusy" @close="receiptOpen = false" @submit="submitReceipt"><label class="dialog-field">验收日期<input v-model="purchase.receiptForm.receivedDate" type="date"></label><label class="dialog-field">验收结果<select v-model="purchase.receiptForm.result"><option value="ALL_ACCEPTED">全部验收通过</option></select></label><label class="dialog-field">验收说明<textarea v-model="purchase.receiptForm.notes" maxlength="500" placeholder="说明数量、规格和质量核验结果"></textarea></label><label class="dialog-field">验收附件<input type="file" multiple @change="uploadReceiptFiles"><small>已上传 {{ purchase.receiptForm.attachments.length }} 个附件</small></label></OaDialog>
</template>
