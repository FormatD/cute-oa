import type { HttpClient } from '../http'
import type { FlowCopy, FlowDelegation, FlowInstance, FlowTask, PagedResponse } from '../types'

export const createWorkflowApi = ({ request }: HttpClient) => ({
  getDelegations: () => request<FlowDelegation[]>('/flow/delegations/my'),
  createDelegation: (payload: { delegateId: string; businessType: string; startAt: string; endAt: string; reason: string }) => request<FlowDelegation>('/flow/delegations', { method: 'POST', body: JSON.stringify(payload) }),
  cancelDelegation: (id: string) => request<FlowDelegation>(`/flow/delegations/${id}/cancel`, { method: 'POST', body: '{}' }),
  getFlowInstance: (id: string) => request<FlowInstance>(`/flow/instances/${id}`),
  getCopies: (page: number, pageSize: number) => request<PagedResponse<FlowCopy>>(`/flow/copies/my?page=${page}&pageSize=${pageSize}`),
  markCopyRead: (id: string) => request<FlowCopy>(`/flow/copies/${id}/read`, { method: 'POST', body: '{}' }),
  getLeaveTasks: () => request<FlowTask[]>('/flow/tasks/my'),
  getProcessedLeaveTasks: () => request<FlowTask[]>('/flow/tasks/done'),
  getExpenseTasks: () => request<FlowTask[]>('/expense-tasks/my'),
  getProcessedExpenseTasks: () => request<FlowTask[]>('/expense-tasks/done'),
  getTravelTasks: () => request<FlowTask[]>('/travel-tasks/my'),
  getProcessedTravelTasks: () => request<FlowTask[]>('/travel-tasks/done'),
  getPurchaseTasks: () => request<FlowTask[]>('/purchase-tasks/my'),
  getProcessedPurchaseTasks: () => request<FlowTask[]>('/purchase-tasks/done'),
  getSealTasks: () => request<FlowTask[]>('/seal-tasks/my'),
  getProcessedSealTasks: () => request<FlowTask[]>('/seal-tasks/done'),
  processLeaveTask: (id: string, action: 'approve' | 'reject', comment: string) => request(`/flow/tasks/${id}/${action}`, { method: 'POST', body: JSON.stringify({ comment }) }),
  transferLeaveTask: (id: string, assigneeId: string, comment: string) => request(`/flow/tasks/${id}/transfer`, { method: 'POST', body: JSON.stringify({ assigneeId, comment }) }),
  processExpenseTask: (id: string, action: 'approve' | 'reject', comment: string) => request(`/expense-tasks/${id}/${action}`, { method: 'POST', body: JSON.stringify({ comment }) }),
  transferExpenseTask: (id: string, assigneeId: string, comment: string) => request(`/expense-tasks/${id}/transfer`, { method: 'POST', body: JSON.stringify({ assigneeId, comment }) }),
  processTravelTask: (id: string, action: 'approve' | 'reject', comment: string) => request(`/travel-tasks/${id}/${action}`, { method: 'POST', body: JSON.stringify({ comment }) }),
  transferTravelTask: (id: string, assigneeId: string, comment: string) => request(`/travel-tasks/${id}/transfer`, { method: 'POST', body: JSON.stringify({ assigneeId, comment }) }),
  processPurchaseTask: (id: string, action: 'approve' | 'reject', comment: string) => request(`/purchase-tasks/${id}/${action}`, { method: 'POST', body: JSON.stringify({ comment }) }),
  transferPurchaseTask: (id: string, assigneeId: string, comment: string) => request(`/purchase-tasks/${id}/transfer`, { method: 'POST', body: JSON.stringify({ assigneeId, comment }) }),
  processSealTask: (id: string, action: 'approve' | 'reject', comment: string) => request(`/seal-tasks/${id}/${action}`, { method: 'POST', body: JSON.stringify({ comment }) }),
  transferSealTask: (id: string, assigneeId: string, comment: string) => request(`/seal-tasks/${id}/transfer`, { method: 'POST', body: JSON.stringify({ assigneeId, comment }) })
})
