import { useAuthStore } from '../stores/auth'
import { createHttpClient, type HttpClient } from './http'
import { createAnnouncementApi } from './modules/announcements'
import { createAttendanceApi } from './modules/attendance'
import { createAuthApi } from './modules/auth'
import { createBusinessConfigurationApi } from './modules/business-configurations'
import { createContractApi } from './modules/contracts'
import { createDocumentApi } from './modules/documents'
import { createExpenseApi } from './modules/expense'
import { createFileApi } from './modules/files'
import { createIdentityApi } from './modules/identity'
import { createLeaveApi } from './modules/leave'
import { createOrganizationApi } from './modules/organization'
import { createPersonnelApi } from './modules/personnel'
import { createPurchaseApi } from './modules/purchase'
import { createSealApi } from './modules/seal'
import { createSystemApi } from './modules/system'
import { createTravelApi } from './modules/travel'
import { createWorkflowApi } from './modules/workflow'

function createApiClient(http: HttpClient) {
  return {
    announcements: createAnnouncementApi(http),
    attendance: createAttendanceApi(http),
    auth: createAuthApi(http),
    businessConfigurations: createBusinessConfigurationApi(http),
    contracts: createContractApi(http),
    documents: createDocumentApi(http),
    expense: createExpenseApi(http),
    files: createFileApi(http),
    identity: createIdentityApi(http),
    leave: createLeaveApi(http),
    organization: createOrganizationApi(http),
    personnel: createPersonnelApi(http),
    purchase: createPurchaseApi(http),
    seal: createSealApi(http),
    system: createSystemApi(http),
    travel: createTravelApi(http),
    workflow: createWorkflowApi(http)
  }
}

export type ApiClient = ReturnType<typeof createApiClient>

const clients = new WeakMap<object, ApiClient>()

/**
 * Returns the API client bound to the active Pinia auth store.
 * One client is shared by all business stores, so token refresh and
 * unauthorized handling have a single, consistent implementation.
 */
export function useApiClient(): ApiClient {
  const auth = useAuthStore()
  const existing = clients.get(auth)
  if (existing) return existing

  const http = createHttpClient(
    () => auth.accessToken,
    auth.clearSession,
    auth.refreshAccessToken
  )
  const client = createApiClient(http)
  clients.set(auth, client)
  return client
}
