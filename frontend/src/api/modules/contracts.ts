import type { HttpClient } from '../http'
import { toQuery } from '../http'
import type { ContractAlertAcknowledgement, ContractAlertSummary, EmploymentContract, GenerateContractDemoResult, PagedResponse, SaveEmploymentContract } from '../types'

export type ContractFilters = { keyword: string; departmentId: string; contractType: string; status: string; expiryDays: string }

export const createContractApi = ({ request }: HttpClient) => ({
  getContracts: (filters: ContractFilters, page: number, pageSize: number) => request<PagedResponse<EmploymentContract>>(`/hr/contracts${toQuery(filters, page, pageSize)}`),
  getContract: (id: string) => request<EmploymentContract>(`/hr/contracts/${encodeURIComponent(id)}`),
  getAlertSummary: () => request<ContractAlertSummary>('/hr/contracts/alerts/summary'),
  createContract: (payload: SaveEmploymentContract) => request<EmploymentContract>('/hr/contracts', { method: 'POST', body: JSON.stringify(payload) }),
  updateContract: (id: string, payload: SaveEmploymentContract) => request<EmploymentContract>(`/hr/contracts/${encodeURIComponent(id)}`, { method: 'PUT', body: JSON.stringify(payload) }),
  activateContract: (id: string, version: number, reason: string) => request<EmploymentContract>(`/hr/contracts/${encodeURIComponent(id)}/activate`, { method: 'POST', body: JSON.stringify({ version, reason }) }),
  renewContract: (id: string, payload: SaveEmploymentContract) => request<EmploymentContract>(`/hr/contracts/${encodeURIComponent(id)}/renew`, { method: 'POST', body: JSON.stringify(payload) }),
  terminateContract: (id: string, version: number, terminationDate: string, reason: string) => request<EmploymentContract>(`/hr/contracts/${encodeURIComponent(id)}/terminate`, { method: 'POST', body: JSON.stringify({ version, terminationDate, reason }) }),
  acknowledgeAlert: (id: string, thresholdDays: number) => request<ContractAlertAcknowledgement>(`/hr/contracts/${encodeURIComponent(id)}/alerts/acknowledge`, { method: 'POST', body: JSON.stringify({ thresholdDays }) }),
  generateDemoData: () => request<GenerateContractDemoResult>('/hr/contracts/demo-data', { method: 'POST' })
})
