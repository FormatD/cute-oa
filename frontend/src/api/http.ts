import type { FileDescriptor } from './types'

const apiHostname = typeof window !== 'undefined' && window.location.hostname ? window.location.hostname : 'localhost'
const runtimeOrigin = typeof window !== 'undefined' ? window.location.origin : 'http://localhost:5234'
const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
const baseUrl = (configuredBaseUrl || (import.meta.env.DEV ? `http://${apiHostname}:5234/api/v1` : `${runtimeOrigin}/api/v1`)).replace(/\/$/, '')

export type ApiRequest = <T>(path: string, init?: RequestInit, requiresAuth?: boolean) => Promise<T>

export type HttpClient = {
  request: ApiRequest
  uploadFile: (file: File) => Promise<FileDescriptor>
  downloadFile: (id: string, resourceType: 'leave' | 'expense' | 'travel' | 'purchase' | 'attendance' | 'contract', resourceId: string) => Promise<void>
  download: (path: string, fallbackName: string) => Promise<void>
}

export function createHttpClient(
  getAccessToken: () => string | null,
  onUnauthorized?: () => void,
  refreshAccessToken?: () => Promise<boolean>
): HttpClient {
  async function recoverAuthentication() {
    if (!refreshAccessToken || !await refreshAccessToken()) {
      onUnauthorized?.()
      return false
    }
    return true
  }

  async function request<T>(path: string, init?: RequestInit, requiresAuth = true): Promise<T> {
    const method = init?.method ?? 'GET'
    const idempotencyKey = method !== 'GET' && method !== 'HEAD' && requiresAuth ? crypto.randomUUID() : null
    const execute = () => {
      const accessToken = getAccessToken()
      return fetch(`${baseUrl}${path}`, {
        ...init,
        credentials: 'include',
        headers: {
          'Content-Type': 'application/json',
          ...(requiresAuth && accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
          ...(idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {}),
          ...init?.headers
        }
      })
    }

    let response = await execute()
    if (response.status === 401 && requiresAuth && await recoverAuthentication()) response = await execute()
    if (!response.ok) {
      const body = await response.json().catch(() => ({}))
      if (response.status === 401 && requiresAuth) onUnauthorized?.()
      throw new Error(body.message ?? '请求失败，请稍后重试。')
    }
    if (response.status === 204) return undefined as T
    return response.json()
  }

  async function uploadFile(file: File): Promise<FileDescriptor> {
    const body = new FormData()
    body.append('file', file)
    const idempotencyKey = crypto.randomUUID()
    const execute = () => {
      const accessToken = getAccessToken()
      return fetch(`${baseUrl}/files`, {
        method: 'POST',
        credentials: 'include',
        headers: {
          ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
          'Idempotency-Key': idempotencyKey
        },
        body
      })
    }
    let response = await execute()
    if (response.status === 401 && await recoverAuthentication()) response = await execute()
    if (!response.ok) {
      const error = await response.json().catch(() => ({}))
      if (response.status === 401) onUnauthorized?.()
      throw new Error(error.message ?? '附件上传失败，请稍后重试。')
    }
    return response.json()
  }

  async function downloadFile(id: string, resourceType: 'leave' | 'expense' | 'travel' | 'purchase' | 'attendance' | 'contract', resourceId: string) {
    return download(`/files/${id}?resourceType=${resourceType}&resourceId=${resourceId}`, 'attachment')
  }

  async function download(path: string, fallbackName: string) {
    const execute = () => {
      const accessToken = getAccessToken()
      return fetch(`${baseUrl}${path}`, {
        credentials: 'include',
        headers: { ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}) }
      })
    }
    let response = await execute()
    if (response.status === 401 && await recoverAuthentication()) response = await execute()
    if (!response.ok) {
      const error = await response.json().catch(() => ({}))
      if (response.status === 401) onUnauthorized?.()
      throw new Error(error.message ?? '文件下载失败。')
    }
    const name = response.headers.get('content-disposition')?.match(/filename\*?=(?:UTF-8''|\")?([^;\"]+)/i)?.[1] ?? fallbackName
    const url = URL.createObjectURL(await response.blob())
    const link = document.createElement('a')
    link.href = url
    link.download = decodeURIComponent(name)
    link.click()
    URL.revokeObjectURL(url)
  }

  return { request, uploadFile, downloadFile, download }
}

export function toQuery(filters: object, page: number, pageSize: number) {
  const query = new URLSearchParams()
  Object.entries(filters).forEach(([key, value]) => { if (value) query.set(key, String(value)) })
  query.set('page', String(page))
  query.set('pageSize', String(pageSize))
  return `?${query.toString()}`
}

export function toFilterQuery(filters: object) {
  const query = new URLSearchParams()
  Object.entries(filters).forEach(([key, value]) => { if (value) query.set(key, String(value)) })
  const value = query.toString()
  return value ? `?${value}` : ''
}
