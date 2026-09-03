import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { useApiClient } from '../api/client'
import type { ListFilters, TravelForm, TravelRequest } from '../api/types'
import { PAGE_SIZE } from './pagination'
import { useUiStore } from './ui'

const blankFilters = (): ListFilters => ({ keyword: '', status: '', applicantId: '', startDate: '', endDate: '' })
export const blankTravelForm = (): TravelForm => ({ purpose: '', estimatedBudget: '', itinerary: [{ destination: '', startDate: '', endDate: '', transportation: '高铁', purpose: '' }], companionIds: [], attachments: [], copyRecipientIds: [] })

export const useTravelStore = defineStore('travel', () => {
  const ui = useUiStore()
  const api = useApiClient().travel
  const travels = ref<TravelRequest[]>([]); const initiatedTravels = ref<TravelRequest[]>([]); const travelDetail = ref<TravelRequest | null>(null)
  const approvedTravels = ref<TravelRequest[]>([])
  const travelForm = ref<TravelForm>(blankTravelForm()); const showTravelForm = ref(false); const travelFilters = ref<ListFilters>(blankFilters())
  const travelPage = ref(1); const travelTotal = ref(0); const travelTotalPages = ref(1)
  const travelSubmitting = ref(false)
  const pagedTravels = computed(() => ({ items: travels.value, currentPage: travelPage.value, totalPages: travelTotalPages.value, total: travelTotal.value }))
  async function loadTravels() { const result = await api.getTravels(travelFilters.value, travelPage.value, PAGE_SIZE); travels.value = result.items; travelPage.value = result.page; travelTotal.value = result.total; travelTotalPages.value = result.totalPages }
  async function loadInitiated(userId: string) { initiatedTravels.value = (await api.getTravels({ ...blankFilters(), applicantId: userId }, 1, 5)).items }
  async function loadApproved(userId: string) { approvedTravels.value = (await api.getTravels({ ...blankFilters(), applicantId: userId, status: '3' }, 1, 100)).items }
  async function loadTravelDetail(id: string) { travelDetail.value = await api.getTravel(id) }
  async function submitTravel() {
    ui.error = ''; travelSubmitting.value = true
    if (!travelForm.value.purpose.trim() || !travelForm.value.estimatedBudget || travelForm.value.itinerary.some(item => !item.destination.trim() || !item.startDate || !item.endDate || !item.transportation || !item.purpose.trim())) { ui.error = '请完整填写出差事由、预估预算和行程明细。'; travelSubmitting.value = false; return false }
    try { const draft = await api.createTravel(travelForm.value); await api.submitTravel(draft.id); ui.message = '出差申请已提交。'; travelForm.value = blankTravelForm(); showTravelForm.value = false; await loadTravels(); return true }
    catch (cause) { ui.error = cause instanceof Error ? cause.message : '提交出差申请失败。'; return false }
    finally { travelSubmitting.value = false }
  }
  async function withdrawTravel(item: TravelRequest) { try { await api.withdrawTravel(item.id); ui.message = '出差申请已撤回。'; await Promise.all([loadTravels(), loadTravelDetail(item.id)]); return true } catch (cause) { ui.error = cause instanceof Error ? cause.message : '撤回出差申请失败。'; return false } }
  function reset() { travels.value = []; initiatedTravels.value = []; approvedTravels.value = []; travelDetail.value = null; travelForm.value = blankTravelForm(); showTravelForm.value = false; travelFilters.value = blankFilters(); travelPage.value = 1; travelTotal.value = 0; travelTotalPages.value = 1 }
  return { travels, initiatedTravels, approvedTravels, travelDetail, travelForm, showTravelForm, travelFilters, travelPage, travelTotal, travelTotalPages, travelSubmitting, pagedTravels, loadTravels, loadInitiated, loadApproved, loadTravelDetail, submitTravel, withdrawTravel, reset }
})
