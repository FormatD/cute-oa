import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { ListFilters, PurchaseForm, PurchaseOrderInput, PurchaseReceiptInput, PurchaseRequest } from '../api/types'
import { PAGE_SIZE } from './pagination'
import { useUiStore } from './ui'

const blankFilters = (): ListFilters => ({ keyword: '', status: '', applicantId: '', startDate: '', endDate: '' })
const today = () => new Date().toISOString().slice(0, 10)
const futureDate = (days: number) => { const date = new Date(); date.setDate(date.getDate() + days); return date.toISOString().slice(0, 10) }
export const blankPurchaseForm = (): PurchaseForm => ({
  title: '', purpose: '', requiredDate: futureDate(14), suggestedSupplier: '', attachments: [], copyRecipientIds: [],
  items: [{ category: '办公用品', name: '', specification: '', quantity: '1', unit: '件', estimatedUnitPrice: '', remark: '' }]
})
export const blankPurchaseOrder = (version = 1): PurchaseOrderInput => ({ version, supplier: '', orderNumber: '', actualAmount: '', orderDate: today(), expectedDeliveryDate: futureDate(7), notes: '', attachments: [] })
export const blankPurchaseReceipt = (version = 1): PurchaseReceiptInput => ({ version, receivedDate: today(), result: 'ALL_ACCEPTED', notes: '', attachments: [] })

export const usePurchaseStore = defineStore('purchase', () => {
  const ui = useUiStore()
  const api = useApiClient().purchase
  const purchases = ref<PurchaseRequest[]>([])
  const initiatedPurchases = ref<PurchaseRequest[]>([])
  const purchaseDetail = ref<PurchaseRequest | null>(null)
  const purchaseForm = ref<PurchaseForm>(blankPurchaseForm())
  const orderForm = ref<PurchaseOrderInput>(blankPurchaseOrder())
  const receiptForm = ref<PurchaseReceiptInput>(blankPurchaseReceipt())
  const showPurchaseForm = ref(false)
  const filters = ref<ListFilters>(blankFilters())
  const page = ref(1); const total = ref(0); const totalPages = ref(1)
  const submitting = ref(false); const operationBusy = ref(false)
  const estimatedTotal = computed(() => purchaseForm.value.items.reduce((sum, item) => sum + Number(item.quantity || 0) * Number(item.estimatedUnitPrice || 0), 0))
  const paged = computed(() => ({ items: purchases.value, currentPage: page.value, totalPages: totalPages.value, total: total.value }))

  async function loadPurchases() { const result = await api.getPurchases(filters.value, page.value, PAGE_SIZE); purchases.value = result.items; page.value = result.page; total.value = result.total; totalPages.value = result.totalPages }
  async function loadInitiated(userId: string) { initiatedPurchases.value = (await api.getPurchases({ ...blankFilters(), applicantId: userId }, 1, 5)).items }
  async function loadPurchaseDetail(id: string) { purchaseDetail.value = await api.getPurchase(id); orderForm.value = blankPurchaseOrder(purchaseDetail.value.version); receiptForm.value = blankPurchaseReceipt(purchaseDetail.value.version) }

  function validateForm() {
    const form = purchaseForm.value
    if (!form.title.trim() || !form.purpose.trim() || !form.requiredDate) return '请填写采购主题、用途和期望到货日期。'
    if (form.items.length < 1 || form.items.some(item => !item.category.trim() || !item.name.trim() || !item.unit.trim() || Number(item.quantity) <= 0 || Number(item.estimatedUnitPrice) < 0)) return '请完整填写采购明细，数量须大于 0，单价不能为负数。'
    if (estimatedTotal.value <= 0) return '采购预估总额必须大于 0。'
    if (estimatedTotal.value >= 50_000 && form.attachments.length < 2) return '5 万元及以上采购至少需要上传两份报价或依据附件。'
    if (estimatedTotal.value >= 5_000 && form.attachments.length < 1) return '5000 元及以上采购至少需要上传一份报价或依据附件。'
    return ''
  }

  async function submitPurchase() {
    ui.error = ''
    const validation = validateForm()
    if (validation) { ui.error = validation; return false }
    submitting.value = true
    try {
      const draft = await api.createPurchase(purchaseForm.value)
      await api.submitPurchase(draft.id)
      ui.message = '采购申请已提交。'
      purchaseForm.value = blankPurchaseForm(); showPurchaseForm.value = false
      await loadPurchases()
      return true
    } catch (cause) { ui.error = cause instanceof Error ? cause.message : '提交采购申请失败。'; return false }
    finally { submitting.value = false }
  }

  async function withdrawPurchase(item: PurchaseRequest) {
    operationBusy.value = true; ui.error = ''
    try { await api.withdrawPurchase(item.id); ui.message = '采购申请已撤回。'; await Promise.all([loadPurchases(), loadPurchaseDetail(item.id)]); return true }
    catch (cause) { ui.error = cause instanceof Error ? cause.message : '撤回采购申请失败。'; return false }
    finally { operationBusy.value = false }
  }

  async function registerOrder(item: PurchaseRequest) {
    if (!orderForm.value.supplier.trim() || !orderForm.value.orderNumber.trim() || Number(orderForm.value.actualAmount) <= 0 || !orderForm.value.orderDate || !orderForm.value.expectedDeliveryDate) { ui.error = '请完整填写供应商、订单号、金额和日期。'; return false }
    operationBusy.value = true; ui.error = ''
    try { purchaseDetail.value = await api.registerOrder(item.id, { ...orderForm.value, version: item.version }); ui.message = '采购下单信息已登记。'; orderForm.value = blankPurchaseOrder(purchaseDetail.value.version); await loadPurchases(); return true }
    catch (cause) { ui.error = cause instanceof Error ? cause.message : '登记采购订单失败。'; return false }
    finally { operationBusy.value = false }
  }

  async function receive(item: PurchaseRequest) {
    if (!receiptForm.value.receivedDate || !receiptForm.value.notes.trim()) { ui.error = '请填写验收日期和验收说明。'; return false }
    operationBusy.value = true; ui.error = ''
    try { purchaseDetail.value = await api.receivePurchase(item.id, { ...receiptForm.value, version: item.version }); ui.message = '采购已验收完成。'; receiptForm.value = blankPurchaseReceipt(purchaseDetail.value.version); await loadPurchases(); return true }
    catch (cause) { ui.error = cause instanceof Error ? cause.message : '登记采购验收失败。'; return false }
    finally { operationBusy.value = false }
  }

  async function generateDemoData() {
    operationBusy.value = true; ui.error = ''
    try { const result = await api.generateDemoData(); ui.message = result.created ? '已生成采购演示草稿。' : '采购演示数据已存在，未重复生成。'; await loadPurchases(); return true }
    catch (cause) { ui.error = cause instanceof Error ? cause.message : '生成采购演示数据失败。'; return false }
    finally { operationBusy.value = false }
  }

  function reset() { purchases.value = []; initiatedPurchases.value = []; purchaseDetail.value = null; purchaseForm.value = blankPurchaseForm(); orderForm.value = blankPurchaseOrder(); receiptForm.value = blankPurchaseReceipt(); showPurchaseForm.value = false; filters.value = blankFilters(); page.value = 1; total.value = 0; totalPages.value = 1 }
  return { purchases, initiatedPurchases, purchaseDetail, purchaseForm, orderForm, receiptForm, showPurchaseForm, filters, page, total, totalPages, submitting, operationBusy, estimatedTotal, paged, loadPurchases, loadInitiated, loadPurchaseDetail, submitPurchase, withdrawPurchase, registerOrder, receive, generateDemoData, reset }
})
