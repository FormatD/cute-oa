import type { HttpClient } from '../http'
import { toQuery } from '../http'
import type { WorkItemFilters, WorkItemOverview, WorkItemResponse, WorkItemTab } from '../types'

export const createWorkItemApi = ({ request }: HttpClient) => ({
  overview: (take = 5) => request<WorkItemOverview>(`/work-items/overview?take=${take}`),
  list: (tab: WorkItemTab, filters: WorkItemFilters, page: number, pageSize: number) =>
    request<WorkItemResponse>(`/work-items${toQuery({ tab, ...filters }, page, pageSize)}`)
})
