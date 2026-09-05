import type { HttpClient } from '../http'
import type {
  BusinessConfigurationFilterQuery,
  BusinessConfigurationListItem,
  BusinessConfigurationRecord,
  ConfigurationVersionSummary,
  CreateBusinessConfigurationRequest,
  PagedResponse,
  PublishBusinessConfigurationRequest,
  RetireBusinessConfigurationRequest,
  UpdateBusinessConfigurationRequest
} from '../types'

export const createBusinessConfigurationApi = ({ request }: HttpClient) => ({
  list: (params: BusinessConfigurationFilterQuery = {}): Promise<PagedResponse<BusinessConfigurationListItem>> => {
    const query = new URLSearchParams()
    if (params.domain) query.set('domain', params.domain)
    if (params.code) query.set('code', params.code)
    if (params.keyword) query.set('keyword', params.keyword)
    if (params.status) query.set('status', params.status)
    if (params.effectiveAsOf) query.set('effectiveAsOf', params.effectiveAsOf)
    if (params.page) query.set('page', String(params.page))
    if (params.pageSize) query.set('pageSize', String(params.pageSize))
    const qs = query.toString()
    return request<PagedResponse<BusinessConfigurationListItem>>(`/business-configurations${qs ? `?${qs}` : ''}`)
  },

  getEffective: (domain: string, code: string, asOf?: string): Promise<BusinessConfigurationRecord> => {
    const query = new URLSearchParams({ domain, code })
    if (asOf) query.set('asOf', asOf)
    return request<BusinessConfigurationRecord>(`/business-configurations/effective?${query.toString()}`)
  },

  get: (id: string): Promise<BusinessConfigurationRecord> =>
    request<BusinessConfigurationRecord>(`/business-configurations/${id}`),

  listVersions: (id: string): Promise<ConfigurationVersionSummary[]> =>
    request<ConfigurationVersionSummary[]>(`/business-configurations/${id}/versions`),

  createDraft: (payload: CreateBusinessConfigurationRequest): Promise<BusinessConfigurationRecord> =>
    request<BusinessConfigurationRecord>('/business-configurations', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),

  updateDraft: (id: string, payload: UpdateBusinessConfigurationRequest): Promise<BusinessConfigurationRecord> =>
    request<BusinessConfigurationRecord>(`/business-configurations/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload)
    }),

  createNewVersion: (id: string): Promise<BusinessConfigurationRecord> =>
    request<BusinessConfigurationRecord>(`/business-configurations/${id}/versions`, {
      method: 'POST',
      body: '{}'
    }),

  publish: (id: string, payload?: PublishBusinessConfigurationRequest): Promise<BusinessConfigurationRecord> =>
    request<BusinessConfigurationRecord>(`/business-configurations/${id}/publish`, {
      method: 'POST',
      body: JSON.stringify(payload ?? {})
    }),

  retire: (id: string, payload?: RetireBusinessConfigurationRequest): Promise<BusinessConfigurationRecord> =>
    request<BusinessConfigurationRecord>(`/business-configurations/${id}/retire`, {
      method: 'POST',
      body: JSON.stringify(payload ?? {})
    }),

  delete: (id: string): Promise<boolean> =>
    request<boolean>(`/business-configurations/${id}`, {
      method: 'DELETE'
    })
})
