export type Employee = { id: string; name: string; role: string; departmentId?: string; departmentName: string; roles?: string[]; permissions?: string[]; positionId?: string | null; positionName?: string | null; managerId?: string | null }
export type LoginSession = { accessToken: string; expiresAt: string; user: Employee }
export type LoginChallengeState = 'PASSWORD_CHANGE_REQUIRED' | 'MFA_REQUIRED' | 'MFA_SETUP_REQUIRED'
export type LoginResult = { status: 'AUTHENTICATED'; session: LoginSession } | { status: LoginChallengeState; challengeToken: string; challengeExpiresAt: string }
export type MfaSetup = { challengeToken: string; secret: string; provisioningUri: string; expiresAt: string }
export type MfaEnrollmentResult = { status: 'AUTHENTICATED'; session: LoginSession; recoveryCodes: string[] }
export type MfaStatus = { enabled: boolean; required: boolean; enabledAt?: string | null; recoveryCodesRemaining: number }
export type AuthSession = { id: string; device: string; ipAddress?: string | null; createdAt: string; lastUsedAt: string; expiresAt: string; status: 'ACTIVE' | 'EXPIRED' | 'REVOKED'; isCurrent: boolean }
export type WorkItemSummary = { pendingCount: number; pendingApprovalCount: number; pendingPersonnelCount: number; pendingAttendanceCount: number; pendingFinanceCount: number; processedCount: number; initiatedCount: number; pendingReadCount: number; riskCount: number }
export type Summary = { tenant: string; currentUser: Employee; pendingTaskCount: number; pendingReadCount: number; contractRiskCount: number; leaveBalance: LeaveBalance; workItems: WorkItemSummary }
export type WorkItemTab = 'pending' | 'processed' | 'initiated' | 'reading' | 'risk'
export type WorkItem = { id: string; tab: WorkItemTab; category: 'APPROVAL' | 'PERSONNEL' | 'ATTENDANCE' | 'FINANCE' | 'COPY' | 'ANNOUNCEMENT' | 'DOCUMENT' | 'CONTRACT'; businessType: string; resourceId: string; taskId?: string | null; number: string; title: string; applicantId?: string | null; applicantName: string; departmentName: string; status: string; currentNode?: string | null; occurredAt: string; processedAt?: string | null; dueDate?: string | null; dueAt?: string | null; urgency: 'NORMAL' | 'DUE_SOON' | 'OVERDUE'; route: string; canProcess: boolean; isRead: boolean; actionType: 'APPROVE' | 'COMPLETE' | 'REVIEW' | 'PAYMENT' | 'ACKNOWLEDGE' | 'READ' | 'VIEW' }
export type WorkItemResponse = PagedResponse<WorkItem> & { summary: WorkItemSummary }
export type WorkItemOverview = { pending: WorkItem[]; initiated: WorkItem[]; reading: WorkItem[]; summary: WorkItemSummary }
export type WorkItemFilters = { businessType: string; keyword: string; status: string; applicantId: string; departmentId: string; startDate: string; endDate: string }
export type FlowAction = { id: string; flowInstanceId: string; taskId?: string | null; sequence?: number | null; action: number; actorId: string; actorName: string; fromAssigneeId?: string | null; fromAssigneeName?: string | null; toAssigneeId?: string | null; toAssigneeName?: string | null; comment?: string | null; occurredAt: string }
export type FlowInstance = { id: string; businessType: 'Leave' | 'Expense' | 'Travel' | 'Purchase' | 'Seal'; businessId: string; businessNumber: string; applicantId: string; processDefinitionId: string; processDefinitionCode: string; processDefinitionVersion: number; attempt: number; status: number; startedAt: string; completedAt?: string | null; actions: FlowAction[] }
export type LeaveRequest = { id: string; number: string; type: string; startDate: string; endDate: string; startPeriod?: string; endPeriod?: string; days: number; reason: string; status: string; applicantName?: string; createdAt?: string; attachments?: string[]; copyRecipientIds?: string[]; tasks?: FlowTask[]; processDefinitionCode?: string | null; processDefinitionVersion?: number | null; currentFlowInstanceId?: string | null; flowInstances?: FlowInstance[] }
export type FlowTask = { id: string; leaveRequestId?: string; expenseClaimId?: string; travelRequestId?: string; purchaseRequestId?: string; sealRequestId?: string; flowInstanceId?: string | null; assigneeName: string; originalAssigneeId?: string | null; originalAssigneeName?: string | null; delegationId?: string | null; sequence: number; status: string; comment?: string; processedAt?: string | null }
export type InvoiceType = 'VatSpecial' | 'VatNormal' | 'VatElectronic' | 'TrainTicket' | 'AirItinerary' | 'QuotaInvoice' | 'OtherReceipt'
export type InvoiceStatus = 'Committed' | 'Paid' | 'Released' | 'Cancelled'
export type ExpenseInvoice = { id: string; expenseClaimId: string; invoiceType: InvoiceType; invoiceCode: string; invoiceNumber: string; invoiceFingerprint: string; billingDate: string; amountWithoutTax: number; taxRate: number; taxAmount: number; totalAmount: number; verificationCode?: string | null; attachmentId?: string | null; status: InvoiceStatus; createdAt: string }
export type ExpenseInvoiceInput = { invoiceType: InvoiceType; invoiceCode?: string | null; invoiceNumber: string; billingDate: string; amountWithoutTax: number; taxRate: number; taxAmount: number; totalAmount: number; verificationCode?: string | null; attachmentId?: string | null }
export type ValidateInvoiceRequest = { invoiceType: InvoiceType; invoiceCode?: string | null; invoiceNumber: string; billingDate: string; totalAmount: number; currentExpenseClaimId?: string | null }
export type ValidateInvoiceResult = { isValid: boolean; conflictClaimNumber?: string | null; conflictApplicantName?: string | null; conflictStatus?: string | null; errorMessage?: string | null }
export type ExpenseInvoiceListItem = { id: string; expenseClaimId: string; claimNumber: string; applicantName: string; invoiceType: InvoiceType; invoiceCode: string; invoiceNumber: string; billingDate: string; amountWithoutTax: number; taxRate: number; taxAmount: number; totalAmount: number; status: InvoiceStatus; createdAt: string }

export type BudgetStatus = 'Draft' | 'Active' | 'Frozen' | 'Closed'
export type Budget = { id: string; tenantId: string; departmentId: string; expenseCategory?: string | null; projectId?: string | null; year: number; month: number; allocatedAmount: number; committedAmount: number; actualAmount: number; availableAmount: number; executionRate?: number; status: BudgetStatus; concurrencyVersion: number; createdBy: string; createdAt: string; updatedAt: string }
export type CreateBudgetRequest = { departmentId: string; year: number; month: number; expenseCategory?: string | null; projectId?: string | null; allocatedAmount: number; autoPublish?: boolean }
export type AdjustBudgetRequest = { amount: number; reason: string; expectedVersion?: number | null }
export type BudgetStatusChangeRequest = { reason?: string | null; expectedVersion?: number | null }
export type BudgetCheckRequest = { departmentId: string; expenseCategory?: string | null; amount: number; year?: number | null; month?: number | null; projectId?: string | null }
export type BudgetCheckResult = { isAllowed: boolean; isExceeded: boolean; availableAmount: number; allocatedAmount: number; committedAmount: number; actualAmount: number; requestedAmount: number; warningMessage?: string | null; blockWhenExceeded: boolean; budgetId?: string | null; hasConfiguredBudget?: boolean }
export type BudgetTransaction = { id: string; tenantId: string; budgetId: string; businessType: string; businessId: string; businessNumber: string; transactionType: string; amount: number; balanceAfter: number; description?: string | null; operatorId: string; actionKey?: string | null; createdAt: string }
export type BudgetQuery = { departmentId?: string; expenseCategory?: string; projectId?: string; year?: number; month?: number; status?: string; page?: number; pageSize?: number }

export type PaymentTransactionListItem = { id: string; businessType: string; businessId: string; businessNumber: string; sequence: number; batchTitle: string; paymentDate: string; paymentMethod: string; payerAccount: string; payeeName: string; payeeAccountMasked: string; payeeBank: string; transactionNumber: string; paidAmount: number; feeAmount: number; proofAttachmentId?: string | null; remarks?: string | null; status: string; operatorId: string; operatorName: string; createdAt: string }
export type CreatePaymentTransactionRequest = { batchTitle: string; paymentDate: string; paymentMethod: string; payerAccount: string; payeeName: string; payeeAccount: string; payeeBank: string; transactionNumber: string; paidAmount: number; feeAmount: number; proofAttachmentId?: string | null; remarks?: string | null }
export type PurchaseReconciliation = { purchaseRequestId: string; purchaseRequestNumber: string; estimatedAmount: number; orderedAmount: number; acceptedAmount: number; paidAmount: number; remainingPayable: number; prepaymentLimitRate: number; maxPrepaymentAllowed: number; isAcceptancePassed: boolean; paymentStatus: string; payments: PaymentTransactionListItem[] }

export type ExpenseItem = { expenseDate: string; category: string; amount: number; description: string; receiptNumber?: string | null; attachments?: string[] }
export type PaymentRecord = { paymentDate: string; paymentMethod: string; transactionNumber: string; paidAmount: number; proofFile: string; operatorId: string }
export type PaymentInput = { paymentDate: string; paymentMethod: string; transactionNumber: string; proofFile: string }
export type ExpenseClaim = { id: string; number: string; totalAmount: number; description?: string; status: string; applicantName?: string; createdAt?: string; payeeAccountName?: string; bankName?: string; travelRequestId?: string | null; travelRequestNumber?: string | null; items?: ExpenseItem[]; payment?: PaymentRecord | null; copyRecipientIds?: string[]; tasks?: FlowTask[]; processDefinitionCode?: string | null; processDefinitionVersion?: number | null; currentFlowInstanceId?: string | null; flowInstances?: FlowInstance[]; budgetPoolId?: string | null; paymentStatus?: string; paidTotalAmount?: number; invoiceCount?: number; invoices?: ExpenseInvoice[]; paymentTransactions?: PaymentTransactionListItem[] }
export type TravelItineraryItem = { destination: string; startDate: string; endDate: string; transportation: string; purpose: string }
export type TravelRequest = {
  id: string;
  number: string;
  applicantId: string;
  applicantName: string;
  departmentName: string;
  purpose: string;
  startDate: string;
  endDate: string;
  days: number;
  estimatedBudget: number;
  itinerary: TravelItineraryItem[];
  companionIds: string[];
  companionNames: string[];
  attachments: string[];
  copyRecipientIds: string[];
  employeeRank?: string | null;
  primaryCityTier?: string | null;
  cityTier?: string | null;
  standardHotelDailyLimit?: number | null;
  hotelBudgetPerDay?: number | null;
  standardMealDailyAllowance?: number | null;
  mealAllowancePerDay?: number | null;
  standardTransportation?: string | null;
  transportationStandard?: string | null;
  allowedBudgetCap?: number | null;
  isOverStandard?: boolean;
  overStandardReason?: string | null;
  configVersionId?: string | null;
  configVersionNumber?: number | null;
  configurationSnapshotVersion?: number | null;
  configSnapshotJson?: string | null;
  configResolvedAt?: string | null;
  version: number;
  status: string;
  createdAt: string;
  tasks: FlowTask[];
  processDefinitionCode?: string | null;
  processDefinitionVersion?: number | null;
  currentFlowInstanceId?: string | null;
  flowInstances?: FlowInstance[];
}
export type TravelForm = { purpose: string; estimatedBudget: string; itinerary: TravelItineraryItem[]; companionIds: string[]; attachments: string[]; copyRecipientIds: string[]; overStandardReason?: string }
export type PurchaseStatus = 'Draft' | 'Approving' | 'Rejected' | 'Approved' | 'Withdrawn' | 'Ordered' | 'Received'
export type PurchaseItem = { category: string; name: string; specification?: string | null; quantity: number; unit: string; estimatedUnitPrice: number; estimatedAmount: number; remark?: string | null }
export type PurchaseFormItem = { category: string; name: string; specification: string; quantity: string; unit: string; estimatedUnitPrice: string; remark: string }
export type PurchaseOrder = { supplier: string; orderNumber: string; actualAmount: number; orderDate: string; expectedDeliveryDate: string; notes?: string | null; attachments: string[]; createdBy: string; createdByName: string; createdAt: string }
export type PurchaseReceipt = { receivedDate: string; result: string; notes: string; attachments: string[]; createdBy: string; createdByName: string; createdAt: string }
export type PurchaseRequest = { id: string; number: string; applicantId: string; applicantName: string; departmentName: string; title: string; purpose: string; requiredDate: string; suggestedSupplier?: string | null; items: PurchaseItem[]; itemCount?: number; estimatedTotal: number; attachments: string[]; copyRecipientIds: string[]; status: PurchaseStatus; version: number; isDemo: boolean; createdAt: string; updatedAt: string; tasks?: FlowTask[]; processDefinitionCode?: string | null; processDefinitionVersion?: number | null; currentFlowInstanceId?: string | null; flowInstances?: FlowInstance[]; order?: PurchaseOrder | null; receipt?: PurchaseReceipt | null; budgetPoolId?: string | null; paymentStatus?: string; paidTotalAmount?: number; prepaymentLimitRate?: number; payments?: PaymentTransactionListItem[] }
export type PurchaseForm = { title: string; purpose: string; requiredDate: string; suggestedSupplier: string; items: PurchaseFormItem[]; attachments: string[]; copyRecipientIds: string[] }
export type PurchaseOrderInput = { version: number; supplier: string; orderNumber: string; actualAmount: string; orderDate: string; expectedDeliveryDate: string; notes: string; attachments: string[] }
export type PurchaseReceiptInput = { version: number; receivedDate: string; result: 'ALL_ACCEPTED'; notes: string; attachments: string[] }
export type SealStatus = 'Draft' | 'Approving' | 'Rejected' | 'Approved' | 'Withdrawn' | 'Executed' | 'Out' | 'Returned'
export type SealExecution = { executedDate: string; operatorName: string; notes?: string | null; attachments: string[]; createdBy: string; createdByName: string; createdAt: string }
export type SealReturn = { returnDate: string; sealCondition: string; receiverName: string; notes?: string | null; attachments: string[]; createdBy: string; createdByName: string; createdAt: string }
export type SealRequest = { id: string; number: string; applicantId: string; applicantName: string; departmentName: string; title: string; documentCategory: string; documentName: string; sealType: string; copies: number; isOut: boolean; outStartDate?: string | null; outEndDate?: string | null; outCustodian?: string | null; reason: string; attachments: string[]; copyRecipientIds: string[]; status: SealStatus; version: number; isDemo: boolean; createdAt: string; updatedAt: string; tasks?: FlowTask[]; processDefinitionCode?: string | null; processDefinitionVersion?: number | null; currentFlowInstanceId?: string | null; flowInstances?: FlowInstance[]; execution?: SealExecution | null; return?: SealReturn | null }
export type SealForm = { title: string; documentCategory: string; documentName: string; sealType: string; copies: string | number; isOut: boolean; outStartDate: string; outEndDate: string; outCustodian: string; reason: string; attachments: string[]; copyRecipientIds: string[] }
export type SealExecutionInput = { version: number; executedDate: string; operatorName: string; notes: string; attachments: string[] }
export type SealReturnInput = { version: number; returnDate: string; sealCondition: 'INTACT' | 'DAMAGED' | 'LOST'; receiverName: string; notes: string; attachments: string[] }
export type Notification = { id: string; title: string; content: string; createdAt: string; readAt?: string | null }
export type WorkCalendarEntry = { date: string; isWorkingDay: boolean; note?: string; source: string }
export type DetailView = { module: 'leave' | 'expense' | 'travel' | 'purchase' | 'seal' | 'notification' | 'calendar'; id: string }
export type LeaveForm = { type: string; startDate: string; startPeriod: string; endDate: string; endPeriod: string; reason: string; attachments: string[]; copyRecipientIds: string[] }
export type ExpenseForm = { category: string; amount: string; expenseDate: string; description: string; receiptNumber: string; attachments: string[]; copyRecipientIds: string[]; travelRequestId: string; invoices?: ExpenseInvoiceInput[] }
export type FileDescriptor = { id: string; name: string; contentType: string; size: number; createdAt: string }
export type ListFilters = { keyword: string; status: string; applicantId: string; startDate: string; endDate: string; minAmount?: string; maxAmount?: string }
export type PagedResponse<T> = { items: T[]; total: number; page: number; pageSize: number; totalPages: number }
export type Department = { id: string; name: string; parentId?: string | null }
export type ManagedDepartment = { id: string; name: string; parentId?: string | null; parentName?: string | null; depth: number; sortOrder: number; isSystem: boolean; version: number; userCount: number; childCount: number; createdAt: string; updatedAt: string }
export type CreateDepartment = { id: string; name: string; parentId: string; sortOrder: number }
export type UpdateDepartment = { name: string; parentId: string | null; sortOrder: number; version: number }
export type PositionOption = { id: string; name: string; departmentId: string; departmentName: string }
export type ManagedPosition = PositionOption & { sortOrder: number; status: 'ACTIVE' | 'DISABLED'; isSystem: boolean; version: number; userCount: number; createdAt: string; updatedAt: string }
export type CreatePosition = { id: string; name: string; departmentId: string; sortOrder: number }
export type UpdatePosition = { name: string; departmentId: string; sortOrder: number; status: 'ACTIVE' | 'DISABLED'; version: number }
export type DirectoryEmployee = { id: string; name: string; role: string; departmentId: string; departmentName: string; managerName?: string | null; positionId?: string | null; positionName?: string | null }
export type AuditLog = { id: string; actorId: string; actorName: string; action: string; resourceType: string; resourceId: string; summary: string; occurredAt: string }
export type AuditFilters = { keyword: string; actorId: string; resourceType: string; startDate: string; endDate: string }
export type ProcessNodePolicy = { id?: string; sequence?: number; approverKey?: string; handlingHours: number; reminderBeforeHours: number; escalateAfterHours: number; escalationTarget: string; missingAssigneeAction: 'BLOCK' | 'SKIP' | 'PROCESS_ADMIN'; allowAutoSkip: boolean }
export type ProcessRoute = { id: string; sequence: number; maxValue: number | null; approverKeys: string[]; nodes: Array<ProcessNodePolicy & { id: string; sequence: number; approverKey: string }> }
export type ProcessDefinition = { id: string; code: string; name: string; businessType: 'Leave' | 'Expense' | 'Travel' | 'Purchase' | 'Seal'; version: number; status: number; routes: ProcessRoute[]; createdBy: string; createdAt: string; publishedBy?: string | null; publishedAt?: string | null; priority: number; departmentIds: string[]; leaveTypes: string[] }
export type UpdateProcessDefinition = { name: string; routes: Array<{ maxValue: number | null; approverKeys: string[]; nodePolicies: ProcessNodePolicy[] }>; priority: number; departmentIds: string[]; leaveTypes: string[] }
export type CreateProcessDefinition = UpdateProcessDefinition & { code: string; businessType: 'Leave' | 'Expense' | 'Travel' | 'Purchase' | 'Seal' }
export type ProcessSimulation = { definitionId: string; code: string; version: number; approvers: Array<{ assignee: DirectoryEmployee; originalApprover: DirectoryEmployee; delegationId?: string | null; policy: ProcessNodePolicy }>; routingNotes: string[] }
export type FlowDelegation = { id: string; ownerId: string; ownerName: string; delegateId: string; delegateName: string; businessType: 'All' | 'Leave' | 'Expense' | 'Travel' | 'Purchase' | 'Seal'; startAt: string; endAt: string; reason: string; status: number; isEffective: boolean; createdAt: string; cancelledAt?: string | null }
export type FlowCopy = { id: string; businessType: 'Leave' | 'Expense' | 'Travel' | 'Purchase' | 'Seal'; businessId: string; businessNumber: string; applicantName: string; title: string; status: string; availableAt: string; readAt?: string | null }
export type DataScope = 'SELF' | 'DEPARTMENT' | 'DEPARTMENT_AND_CHILDREN' | 'COMPANY'
export type SecurityRole = { code: string; name: string; isSystem: boolean; userCount: number; permissions: string[]; dataScopes: Record<'Leave' | 'Expense' | 'Travel' | 'Purchase' | 'Personnel' | 'Attendance' | 'Contract' | 'Seal', DataScope> }
export type PermissionDefinition = { code: string; name: string; description: string }
export type CreateSecurityRole = { code: string; name: string; permissions: string[]; dataScopes: Record<'Leave' | 'Expense' | 'Travel' | 'Purchase' | 'Personnel' | 'Attendance' | 'Contract' | 'Seal', DataScope> }
export type UpdateSecurityRole = { code: string; name: string; permissions: string[]; dataScopes: Record<'Leave' | 'Expense' | 'Travel' | 'Purchase' | 'Personnel' | 'Attendance' | 'Contract' | 'Seal', DataScope> }
export type ManagedUser = { id: string; name: string; departmentId: string; departmentName: string; positionId?: string | null; positionName?: string | null; managerId?: string | null; managerName?: string | null; cumulativeWorkYears: number; status: 'ACTIVE' | 'DISABLED'; version: number; roles: string[]; permissions: string[]; createdAt: string; updatedAt: string; mfaEnabled: boolean; mustChangePassword: boolean }
export type CreateManagedUser = {
  id: string
  name: string
  departmentId: string
  positionId: string | null
  managerId: string | null
  cumulativeWorkYears: number
  roles: string[]
  password: string
  employeeNumber: string
  hireDate: string
  employmentType: 'FULL_TIME' | 'PART_TIME' | 'INTERN' | 'CONTRACTOR'
  personnelStatus: 'ACTIVE' | 'PROBATION'
  probationEndDate: string | null
  cumulativeWorkStartDate: string | null
}
export type UpdateManagedUser = { name: string; departmentId: string; positionId: string | null; managerId: string | null; cumulativeWorkYears: number; status: 'ACTIVE' | 'DISABLED'; roles: string[]; version: number }
export type Announcement = { id: string; title: string; content: string; status: 'DRAFT' | 'PUBLISHED' | 'WITHDRAWN'; version: number; createdBy: string; createdByName: string; createdAt: string; updatedAt: string; publishedBy?: string | null; publishedByName?: string | null; publishedAt?: string | null; withdrawnAt?: string | null; expiresAt?: string | null; readAt?: string | null }
export type SaveAnnouncement = { title: string; content: string; expiresAt: string | null; version?: number | null }
export type EmploymentType = 'FULL_TIME' | 'PART_TIME' | 'INTERN' | 'CONTRACTOR'
export type PersonnelStatus = 'PROBATION' | 'ACTIVE' | 'OFFBOARDING' | 'TERMINATED'
export type PersonnelEvent = { id: string; eventType: string; effectiveDate: string; summary: string; changedBy: string; changedByName: string; createdAt: string }
export type PersonnelProfile = { userId: string; employeeNumber: string; name: string; departmentId: string; departmentName: string; positionId?: string | null; positionName?: string | null; managerId?: string | null; managerName?: string | null; workEmail: string; workPhone: string; workLocation: string; employmentType: EmploymentType; personnelStatus: PersonnelStatus; accountStatus: 'ACTIVE' | 'DISABLED'; hireDate: string; probationEndDate?: string | null; regularizedDate?: string | null; cumulativeWorkStartDate?: string | null; cumulativeWorkYears: number; departureDate?: string | null; departureReason?: string | null; version: number; createdAt: string; updatedAt: string; events: PersonnelEvent[] }
export type UpdatePersonnelProfile = { departmentId: string; positionId: string | null; managerId: string | null; workEmail: string; workPhone: string; workLocation: string; employmentType: EmploymentType; personnelStatus: PersonnelStatus; hireDate: string; probationEndDate: string | null; regularizedDate: string | null; cumulativeWorkStartDate: string | null; departureDate: string | null; departureReason: string | null; version: number; effectiveDate: string; changeReason: string }
export type LeaveBalanceType = 'Annual' | 'CompTime'
export type CompTimeGrantItem = {
  id: string
  grantedDate: string
  expiredAt: string
  days: number
  usedDays: number
  frozenDays: number
  availableDays: number
  reason: string
}
export type LeaveBalance = { entitled: number; frozen: number; used: number; available: number; year: number; statutoryEntitled: number; adjustment: number; version: number; grants?: CompTimeGrantItem[] }
export type AdjustLeaveBalance = { type: LeaveBalanceType; year: number; adjustment: number; reason: string; version: number }
export type PersonnelCaseType = 'ONBOARDING' | 'REGULARIZATION' | 'TRANSFER' | 'OFFBOARDING'
export type PersonnelCaseStatus = 'OPEN' | 'COMPLETED' | 'CANCELLED'
export type PersonnelTaskStatus = 'PENDING' | 'COMPLETED' | 'WAIVED'
export type PersonnelCaseTask = { id: string; code: string; title: string; category: string; required: boolean; assigneeId: string; assigneeName: string; dueDate: string; status: PersonnelTaskStatus; isOverdue: boolean; completionNote?: string | null; completedBy?: string | null; completedByName?: string | null; completedAt?: string | null; sortOrder: number; version: number }
export type PersonnelCase = { id: string; number: string; userId: string; employeeName: string; departmentName: string; type: PersonnelCaseType; title: string; effectiveDate: string; status: PersonnelCaseStatus; ownerId: string; ownerName: string; notes?: string | null; taskCount: number; resolvedTaskCount: number; overdueTaskCount: number; version: number; createdBy: string; createdByName: string; createdAt: string; updatedAt: string; completedBy?: string | null; completedByName?: string | null; completedAt?: string | null; completionComment?: string | null; cancelledBy?: string | null; cancelledByName?: string | null; cancelledAt?: string | null; cancellationReason?: string | null; tasks: PersonnelCaseTask[] }
export type CreatePersonnelCase = { userId: string; type: PersonnelCaseType; effectiveDate: string; ownerId: string; notes: string | null }
export type UpdatePersonnelCaseTask = { assigneeId: string; dueDate: string; status: PersonnelTaskStatus; completionNote: string | null; version: number }
export type AttendanceStatus = 'NORMAL' | 'LATE' | 'EARLY_LEAVE' | 'LATE_AND_EARLY' | 'MISSING_PUNCH' | 'ABSENT' | 'LEAVE' | 'REST_DAY' | 'CORRECTED'
export type AttendanceAppeal = { id: string; status: 'PENDING' | 'APPROVED' | 'REJECTED'; reason: string; attachments: string[]; submittedBy: string; submittedByName: string; submittedAt: string; reviewedBy?: string | null; reviewedByName?: string | null; reviewComment?: string | null; reviewedAt?: string | null }
export type AttendanceRecord = { id: string; userId: string; employeeName: string; departmentId: string; departmentName: string; workDate: string; shiftCode: string; shiftName: string; scheduledStart: string; scheduledEnd: string; checkInAt?: string | null; checkOutAt?: string | null; status: AttendanceStatus; originalStatus?: AttendanceStatus | null; workedMinutes: number; lateMinutes: number; earlyLeaveMinutes: number; source: 'DEMO' | 'IMPORT' | 'MANUAL'; version: number; updatedAt: string; appeals: AttendanceAppeal[] }
export type AttendanceShift = { id: string; code: string; name: string; workStart: string; workEnd: string; breakMinutes: number; lateToleranceMinutes: number; earlyLeaveToleranceMinutes: number; isDefault: boolean; isEnabled: boolean; version: number; updatedAt: string }
export type SaveAttendanceShift = Omit<AttendanceShift, 'id' | 'updatedAt'>
export type AttendanceImportItem = { userId: string; workDate: string; checkInAt: string | null; checkOutAt: string | null }
export type AttendanceImportResult = { created: number; updated: number; skipped: number }
export type AttendanceMonthlySummary = { userId: string; employeeName: string; departmentName: string; month: string; scheduledDays: number; attendedDays: number; normalDays: number; lateDays: number; earlyLeaveDays: number; missingPunchDays: number; absentDays: number; leaveDays: number; correctedDays: number; pendingAppeals: number; workedMinutes: number }
export type AttendanceMonthLock = { month: string; isLocked: boolean; version: number; lockReason?: string | null; lockedBy?: string | null; lockedByName?: string | null; lockedAt?: string | null; unlockedBy?: string | null; unlockedByName?: string | null; unlockedAt?: string | null; unlockReason?: string | null; snapshotSequence?: number | null; snapshotHash?: string | null; snapshotRowCount: number; snapshotCreatedAt?: string | null }
export type EmploymentContractType = 'FIXED_TERM' | 'OPEN_ENDED' | 'PROJECT_BASED' | 'INTERNSHIP' | 'LABOR_DISPATCH'
export type EmploymentContractStatus = 'DRAFT' | 'ACTIVE' | 'EXPIRING' | 'EXPIRED' | 'TERMINATED' | 'SUPERSEDED'
export type EmploymentContractEvent = { id: string; eventType: string; summary: string; reason: string; changedBy: string; changedByName: string; createdAt: string }
export type ContractAlertAcknowledgement = { id: string; thresholdDays: number; acknowledgedBy: string; acknowledgedByName: string; acknowledgedAt: string }
export type EmploymentContract = { id: string; contractNumber: string; userId: string; employeeName: string; departmentId: string; departmentName: string; positionName: string; workLocation: string; contractType: EmploymentContractType; status: EmploymentContractStatus; displayStatus: EmploymentContractStatus; signedDate?: string | null; startDate: string; endDate?: string | null; probationStartDate?: string | null; probationEndDate?: string | null; projectDescription?: string | null; attachments: string[]; notes?: string | null; renewalOfId?: string | null; renewalOfNumber?: string | null; renewalSequence: number; openEndedReviewRequired: boolean; openEndedReviewReason?: string | null; terminationDate?: string | null; terminationReason?: string | null; daysUntilEnd?: number | null; currentAlertThreshold?: number | null; currentAlertAcknowledged: boolean; isDemo: boolean; version: number; createdBy: string; createdAt: string; updatedAt: string; events: EmploymentContractEvent[]; alertAcknowledgements: ContractAlertAcknowledgement[] }
export type SaveEmploymentContract = { userId: string; contractType: EmploymentContractType; signedDate: string | null; startDate: string; endDate: string | null; probationStartDate: string | null; probationEndDate: string | null; workLocation: string; positionName: string; projectDescription: string | null; attachments: string[]; notes: string | null; version: number; changeReason: string }
export type ContractAlertSummary = { missingWrittenContract: number; expired: number; within7Days: number; within30Days: number; within60Days: number; within90Days: number; openEndedReviewRequired: number; unacknowledged: number; atRiskContracts: number }
export type GenerateContractDemoResult = { created: number; skipped: number }

export type DocumentCategory = {
  id: string
  code: string
  name: string
  description: string | null
  parentId: string | null
  departmentId: string | null
  sortOrder: number
  documentCount: number
}

export type SaveDocumentCategory = {
  code: string
  name: string
  description?: string | null
  parentId?: string | null
  departmentId?: string | null
  sortOrder: number
}

export type DocumentStatus = 'Draft' | 'Published' | 'Archived'

export type KnowledgeDocument = {
  id: string
  number: string
  categoryId: string
  categoryName: string
  title: string
  summary: string
  content: string
  tags: string[]
  version: number
  status: DocumentStatus
  isMustRead: boolean
  departmentId: string | null
  effectiveDate: string
  expiryDate: string | null
  attachments: string[]
  viewCount: number
  downloadCount: number
  createdBy: string
  createdByName: string
  publishedAt: string | null
  publishedByName: string | null
  archivedAt: string | null
  createdAt: string
  updatedAt: string
  hasAcknowledged: boolean
  acknowledgedAt: string | null
}

export type SaveDocument = {
  title: string
  categoryId: string
  summary: string
  content: string
  tags?: string[]
  isMustRead: boolean
  departmentId?: string | null
  effectiveDate: string
  expiryDate?: string | null
  attachments?: string[]
}

export type ReviseDocument = {
  version: number
  title: string
  summary: string
  content: string
  changeNotes?: string
  attachments?: string[]
}

export type DocumentVersion = {
  id: string
  version: number
  title: string
  summary: string
  content: string
  changeNotes: string | null
  attachments: string[]
  publishedAt: string
  publishedByName: string
}

export type DocumentAcknowledgement = {
  id: string
  documentId: string
  documentVersion: number
  userId: string
  userName: string
  departmentName: string | null
  acknowledgedAt: string
}

export type DocumentAcknowledgementStats = {
  totalRequired: number
  totalAcknowledged: number
  acknowledgedPercentage: number
  acknowledgedList: DocumentAcknowledgement[]
  pendingList: { id: string; name: string; departmentName: string; managerName: string | null }[]
}

export type RollbackDocument = {
  targetVersion: number
  currentVersion: number
  reason?: string | null
}

export type MoveDocumentCategory = {
  newCategoryId: string
}

export type DiffLine = {
  type: 'unchanged' | 'added' | 'removed'
  oldLineNumber: number | null
  newLineNumber: number | null
  text: string
}

export type DocumentDiffView = {
  documentId: string
  sourceVersion: number
  targetVersion: number
  sourceTitle: string
  targetTitle: string
  titleChanged: boolean
  sourceSummary: string
  targetSummary: string
  summaryChanged: boolean
  sourceAttachments: string[]
  targetAttachments: string[]
  attachmentsChanged: boolean
  targetChangeNotes: string | null
  sourcePublishedAt: string
  sourcePublishedByName: string
  targetPublishedAt: string
  targetPublishedByName: string
  addedLines: number
  removedLines: number
  unchangedLines: number
  contentDiff: DiffLine[]
}

// --- Business Configuration Center Types ---

export type ConfigurationDomain = 'Leave' | 'Expense' | 'Travel' | 'Procurement' | 'Seal' | 'Dictionary'
export type ConfigurationStatus = 'DRAFT' | 'SCHEDULED' | 'EFFECTIVE' | 'RETIRED'

export type BusinessConfigurationRecord = {
  id: string
  tenantId: string
  domain: ConfigurationDomain | string
  code: string
  name: string
  description: string | null
  version: number
  status: ConfigurationStatus
  effectiveFrom: string
  effectiveTo: string | null
  contentJson: string
  createdBy: string
  createdByName: string
  createdAt: string
  updatedBy: string
  updatedByName: string
  updatedAt: string
  publishedBy?: string | null
  publishedByName?: string | null
  publishedAt?: string | null
  concurrencyVersion: number
  referenceCount: number
}

export type BusinessConfigurationListItem = {
  id: string
  tenantId: string
  domain: ConfigurationDomain | string
  code: string
  name: string
  description: string | null
  version: number
  status: ConfigurationStatus
  effectiveFrom: string
  effectiveTo: string | null
  createdByName: string
  createdAt: string
  updatedByName: string
  updatedAt: string
  publishedByName?: string | null
  publishedAt?: string | null
  concurrencyVersion: number
  referenceCount: number
}

export type ConfigurationVersionSummary = {
  id: string
  version: number
  status: ConfigurationStatus
  effectiveFrom: string
  effectiveTo: string | null
  publishedByName?: string | null
  publishedAt?: string | null
  createdAt: string
  referenceCount: number
}

export type CreateBusinessConfigurationRequest = {
  domain: string
  code: string
  name: string
  description?: string | null
  effectiveFrom: string
  effectiveTo?: string | null
  contentJson: string
}

export type UpdateBusinessConfigurationRequest = {
  name: string
  description?: string | null
  effectiveFrom: string
  effectiveTo?: string | null
  contentJson: string
  concurrencyVersion: number
}

export type PublishBusinessConfigurationRequest = {
  effectiveFrom?: string | null
  effectiveTo?: string | null
  concurrencyVersion?: number | null
}

export type RetireBusinessConfigurationRequest = {
  effectiveTo?: string | null
  concurrencyVersion?: number | null
}

export type BusinessConfigurationFilterQuery = {
  domain?: string
  code?: string
  keyword?: string
  status?: string
  effectiveAsOf?: string
  page?: number
  pageSize?: number
}

// Strongly-typed domain configurations
export type LeaveTypePolicyConfig = {
  type: string
  name: string
  isEnabled: boolean
  minUnit: number
  requiresAttachment: boolean
  attachmentThresholdDays?: number | null
}

export type AnnualLeaveBonusConfig = {
  legalMinStandardProtected: boolean
  tier1BonusDays: number
  tier2BonusDays: number
  tier3BonusDays: number
}

export type LeavePolicyConfig = {
  leaveTypes: LeaveTypePolicyConfig[]
  allowCrossYear: boolean
  compTimeValidityDays: number
  annualLeaveBonus: AnnualLeaveBonusConfig
}

export type ExpenseCategoryPolicyConfig = {
  code?: string
  name: string
  aliases?: string[]
  isEnabled: boolean
  singleLimit?: number | null
  requiresReceipt: boolean
  requiresReasonWhenExceeded: boolean
  blockWhenExceeded: boolean
}

export type ExpensePolicyConfig = {
  categories: ExpenseCategoryPolicyConfig[]
}

export type TravelCityTierConfig = {
  tierName: string
  cities: string[]
}

export type TravelStandardItemConfig = {
  cityTier: string
  rank: string
  hotelDailyLimit: number
  mealDailyAllowance: number
  transportationStandard: string
  blockWhenExceeded?: boolean
}

export type TravelPolicyConfig = {
  cityTiers: TravelCityTierConfig[]
  employeeRanks: string[]
  standards: TravelStandardItemConfig[]
  blockWhenExceeded?: boolean
}

export type ProcurementCategoryPolicyConfig = {
  code?: string
  name: string
  aliases?: string[]
  isEnabled: boolean
}

export type ProcurementAmountTierConfig = {
  name: string
  maxAmount?: number | null
}

export type ProcurementPolicyConfig = {
  categories: ProcurementCategoryPolicyConfig[]
  quoteAttachmentThreshold: number
  amountTiers: ProcurementAmountTierConfig[]
  defaultPurchaserUserId: string
  requiresAcceptance: boolean
  acceptanceRoleOrAssignee: string
}

export type SealRegistryItemConfig = {
  code?: string
  name: string
  aliases?: string[]
  sealType: string
  custodianUserId: string
  isEnabled: boolean
  allowOut: boolean
  maxOutDays: number
}

export type SealDocumentCategoryConfig = {
  code?: string
  name: string
  aliases?: string[]
  isEnabled: boolean
  riskLevel: 'LOW' | 'MEDIUM' | 'HIGH' | string
}

export type SealRiskRulesConfig = {
  highRiskMetric: number
  mediumRiskMetric: number
  lowRiskMetric: number
}

export type SealPolicyConfig = {
  seals: SealRegistryItemConfig[]
  documentCategories: SealDocumentCategoryConfig[]
  riskRules: SealRiskRulesConfig
}

export type DictionaryItemConfig = {
  code: string
  name: string
  sortOrder: number
  isEnabled: boolean
  description?: string | null
  effectiveDate?: string | null
  version?: number | null
}

export type DictionaryConfig = {
  items: DictionaryItemConfig[]
}

export type BusinessRuleSnapshot = {
  configVersionId: string
  configVersionNumber: number
  domain: string
  code: string
  resolvedAt: string
  parameters: Record<string, unknown>
}

export type EffectiveLeaveTypeOption = {
  code: string
  name: string
  minUnit: number
  requiresAttachment: boolean
  attachmentThresholdDays?: number | null
}

export type EffectiveExpenseCategoryOption = {
  code: string
  name: string
  singleLimit?: number | null
  requiresReceipt: boolean
  requiresReasonWhenExceeded: boolean
  blockWhenExceeded: boolean
}

export type EffectiveProcurementCategoryOption = {
  code: string
  name: string
}

export type EffectiveProcurementAmountTierOption = {
  name: string
  maxAmount?: number | null
}

export type EffectiveSealOption = {
  code: string
  name: string
  aliases?: string[]
  sealType: string
  allowOut: boolean
  maxOutDays: number
}

export type EffectiveSealDocCategoryOption = {
  code: string
  name: string
  aliases?: string[]
  riskLevel: string
}

export type EffectiveDictionaryOption = {
  code: string
  name: string
  sortOrder: number
}

export type EffectiveBusinessConfigurationBundle = {
  leaveTypes: EffectiveLeaveTypeOption[]
  expenseCategories: EffectiveExpenseCategoryOption[]
  travelCityTiers: TravelCityTierConfig[]
  travelEmployeeRanks: string[]
  travelStandards: TravelStandardItemConfig[]
  procurementCategories: EffectiveProcurementCategoryOption[]
  procurementAmountTiers: EffectiveProcurementAmountTierOption[]
  procurementQuoteThreshold: number
  seals: EffectiveSealOption[]
  sealDocumentCategories: EffectiveSealDocCategoryOption[]
  contractTypes: EffectiveDictionaryOption[]
  attachmentTypes: EffectiveDictionaryOption[]
  approvalCommentPresets: EffectiveDictionaryOption[]
  announcementTypes: EffectiveDictionaryOption[]
}
