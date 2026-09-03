import { createRouter, createWebHashHistory } from 'vue-router'
import ApprovalPage from '../views/ApprovalPage.vue'
import CalendarPage from '../views/CalendarPage.vue'
import DetailPage from '../views/DetailPage.vue'
import ExpensePage from '../views/ExpensePage.vue'
import LeavePage from '../views/LeavePage.vue'
import WorkbenchPage from '../views/WorkbenchPage.vue'
import OrganizationPage from '../views/OrganizationPage.vue'
import AuditPage from '../views/AuditPage.vue'
import LoginPage from '../views/LoginPage.vue'
import ProcessPage from '../views/ProcessPage.vue'
import DelegationPage from '../views/DelegationPage.vue'
import CopyPage from '../views/CopyPage.vue'
import UserManagementPage from '../views/UserManagementPage.vue'
import SecurityPage from '../views/SecurityPage.vue'
import RoleManagementPage from '../views/RoleManagementPage.vue'
import DepartmentManagementPage from '../views/DepartmentManagementPage.vue'
import PositionManagementPage from '../views/PositionManagementPage.vue'
import AnnouncementPage from '../views/AnnouncementPage.vue'
import AnnouncementDetailPage from '../views/AnnouncementDetailPage.vue'
import AnnouncementManagementPage from '../views/AnnouncementManagementPage.vue'
import TravelPage from '../views/TravelPage.vue'
import TravelDetailPage from '../views/TravelDetailPage.vue'
import PurchasePage from '../views/PurchasePage.vue'
import PurchaseDetailPage from '../views/PurchaseDetailPage.vue'
import SealPage from '../views/SealPage.vue'
import SealDetailPage from '../views/SealDetailPage.vue'
import PersonnelPage from '../views/PersonnelPage.vue'
import PersonnelDetailPage from '../views/PersonnelDetailPage.vue'
import PersonnelCasePage from '../views/PersonnelCasePage.vue'
import PersonnelCaseDetailPage from '../views/PersonnelCaseDetailPage.vue'
import AttendancePage from '../views/AttendancePage.vue'
import AttendanceDetailPage from '../views/AttendanceDetailPage.vue'
import ContractPage from '../views/ContractPage.vue'
import ContractDetailPage from '../views/ContractDetailPage.vue'
import DocumentCenterPage from '../views/DocumentCenterPage.vue'
import DocumentDetailPage from '../views/DocumentDetailPage.vue'

export const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', redirect: '/workbench' },
    { path: '/login', component: LoginPage, meta: { public: true } },
    { path: '/workbench', component: WorkbenchPage },
    { path: '/documents', component: DocumentCenterPage },
    { path: '/documents/:id', component: DocumentDetailPage, props: true },
    { path: '/announcements', component: AnnouncementPage },
    { path: '/announcements/:id', component: AnnouncementDetailPage },
    { path: '/announcement-admin', component: AnnouncementManagementPage, meta: { permission: 'ANNOUNCEMENT_MANAGE' } },
    { path: '/leave', component: LeavePage },
    { path: '/leave/:id', component: DetailPage, props: route => ({ module: 'leave', id: route.params.id }) },
    { path: '/expense', component: ExpensePage },
    { path: '/expense/:id', component: DetailPage, props: route => ({ module: 'expense', id: route.params.id }) },
    { path: '/travel', component: TravelPage },
    { path: '/travel/:id', component: TravelDetailPage, props: true },
    { path: '/purchase', component: PurchasePage },
    { path: '/purchase/:id', component: PurchaseDetailPage, props: true },
    { path: '/seal', component: SealPage },
    { path: '/seal/:id', component: SealDetailPage, props: true },
    { path: '/hr/employees', component: PersonnelPage },
    { path: '/hr/employees/:id', component: PersonnelDetailPage, props: true },
    { path: '/hr/personnel-cases', component: PersonnelCasePage },
    { path: '/hr/personnel-cases/:id', component: PersonnelCaseDetailPage, props: true },
    { path: '/hr/contracts', component: ContractPage },
    { path: '/hr/contracts/:id', component: ContractDetailPage, props: true },
    { path: '/attendance', component: AttendancePage },
    { path: '/attendance/:id', component: AttendanceDetailPage, props: true },
    { path: '/approval', component: ApprovalPage },
    { path: '/approval/:id', component: DetailPage, props: route => ({ module: 'notification', id: route.params.id }) },
    { path: '/calendar', component: CalendarPage },
    { path: '/calendar/:id', component: DetailPage, props: route => ({ module: 'calendar', id: route.params.id }) },
    { path: '/organization', component: OrganizationPage },
    { path: '/users', component: UserManagementPage, meta: { permission: 'USER_MANAGE' } },
    { path: '/security', component: SecurityPage },
    { path: '/roles', component: RoleManagementPage, meta: { permission: 'USER_MANAGE' } },
    { path: '/departments', component: DepartmentManagementPage, meta: { permission: 'ORG_MANAGE' } },
    { path: '/positions', component: PositionManagementPage, meta: { permission: 'ORG_MANAGE' } },
    { path: '/audit', component: AuditPage, meta: { permission: 'AUDIT_VIEW' } },
    { path: '/processes', component: ProcessPage, meta: { permission: 'PROCESS_MANAGE' } },
    { path: '/delegations', component: DelegationPage },
    { path: '/copies', component: CopyPage },
    { path: '/:pathMatch(.*)*', redirect: '/workbench' }
  ]
})
