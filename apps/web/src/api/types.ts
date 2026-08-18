export interface AuthorizedStore { id: string; code: string; name: string; isDefault: boolean }
export interface CurrentUser { id: string; tenantId: string; displayName: string; account: string; mustChangePassword: boolean; roles: string[]; stores: AuthorizedStore[] }
export interface EmployeeStore { id: string; code: string; name: string; isPrimary: boolean }
export interface Employee { id: string; employeeNo: string; displayName: string; positionCode: string; status: string; userId?: string; account?: string; accountEnabled?: boolean; mustChangePassword?: boolean; roles: string[]; stores: EmployeeStore[]; createdAtUtc: string }
export interface EmployeeRole { id: string; code: string; name: string }
export interface ServiceItem { id: string; code: string; name: string; standardDurationMinutes: number; status: string; version: number }
export interface ProductItem { id: string; code: string; name: string; unitName: string; trackInventory: boolean; status: string; version: number }
export interface PriceBookLine { serviceItemId: string; serviceItemName: string; unitPriceMinor: number }
export interface ProductPriceBookLine { productItemId: string; productItemName: string; unitName: string; unitPriceMinor: number }
export interface PriceBook { id: string; name: string; status: string; effectiveFrom: string; publishedAtUtc?: string; lines: PriceBookLine[]; productLines: ProductPriceBookLine[] }

export interface FacilityGroup { id: string; displayName: string; sortOrder: number }
export interface FacilityType { id: string; displayName: string }
export interface FacilityBoardItem {
  id: string; code: string; displayName: string; typeName: string; status: string; version: number
  sessionId?: string; visitId?: string; visitNo?: string; sessionStatus?: string; startedAtUtc?: string
  activeSeconds: number; pausedSeconds: number; expectedDurationMinutes?: number; note?: string
}
export interface FacilityBoardGroup { id: string; displayName: string; facilities: FacilityBoardItem[] }
export interface FacilityBoard { serverNowUtc: string; groups: FacilityBoardGroup[] }

export interface CustomerSummary { id: string; displayName: string; maskedMobile: string; status: string; homeStoreId: string; activeCardCount: number; createdAtUtc: string }
export interface MemberCardType { id: string; code: string; name: string; validityDays?: number; status: string }
export interface MemberAccount { id: string; accountType: string; balanceUnits: number; status: string }
export interface MemberCard { id: string; cardTypeName: string; maskedCardNo: string; status: string; validFrom: string; validTo?: string; accounts: MemberAccount[] }
export interface CustomerDetail { id: string; displayName: string; maskedMobile: string; gender: string; sourceCode?: string; serviceNotificationConsent: boolean; marketingConsent: boolean; status: string; homeStoreId: string; version: number; cards: MemberCard[] }
export interface CashierVisit { id: string; visitNo: string; status: string; customerId?: string; arrivedAtUtc: string; serviceEndedAtUtc?: string; facilitySeconds: number; note?: string }
export interface ServiceOrderLine { id: string; serviceItemId: string; itemCode: string; itemName: string; quantity: number; actualSeconds?: number; referencePriceMinor: number; enteredPriceMinor: number; lineAmountMinor: number; priceOverrideReason?: string }
export interface ServiceOrder { id: string; orderNo: string; visitId: string; customerId?: string; status: string; priceBookId: string; referenceAmountMinor: number; receivableMinor: number; refundedMinor: number; note?: string; version: number; createdAtUtc: string; lines: ServiceOrderLine[] }
export interface PaymentMethod { id: string; code: string; name: string; category: string; requiresOpenShift: boolean; internalAccountType?: string; channelProvider?: string }
export interface PaymentChannelConfiguration { id: string; storeId: string; provider: string; environment: string; displayName: string; credentialProfile: string; isEnabled: boolean; credentialsPresent: boolean; missingRequirements: string[]; version: number }
export interface PaymentChannelOrder { id: string; configurationId: string; paymentId: string; paymentAllocationId: string; provider: string; outTradeNo: string; amountMinor: number; status: string; qrPayload?: string; providerTradeNo?: string; failureCode?: string; expiresAtUtc: string; paidAtUtc?: string; closedAtUtc?: string; lastQueriedAtUtc?: string; paymentStatus: string; serviceOrderStatus: string; version: number }
export interface PaymentAllocation { id: string; methodId: string; methodCode: string; methodName: string; category: string; amountMinor: number; externalReference?: string; confirmationStatus: string; reconciliationStatus: string; shiftId?: string; memberAccountId?: string; channelProvider?: string }
export interface Payment { id: string; paymentNo: string; orderId?: string; businessType: string; businessId: string; status: string; currency: string; receivableMinor: number; paidMinor: number; refundedMinor: number; paidAtUtc?: string; version: number; allocations: PaymentAllocation[] }
export interface RefundLine { id: string; originalAllocationId: string; amountMinor: number; category: string; memberAccountId?: string; route: string; cashShiftId?: string; completedAtUtc?: string }
export interface Refund { id: string; paymentId: string; businessType: string; businessId: string; refundNo: string; status: string; amountMinor: number; reason: string; requestedBy: string; requestedAtUtc: string; approvedBy?: string; completedAtUtc?: string; rejectionReason?: string; version: number; lines: RefundLine[] }
export interface MemberVerification { id: string; orderId: string; customerId: string; authorizedAmountMinor: number; maskedMobile: string; status: string; attemptsRemaining: number; expiresAtUtc: string; developmentCode?: string }
export interface MemberTopup { id: string; topupNo: string; storeId: string; customerId: string; cardId: string; principalMinor: number; bonusMinor: number; receivableMinor: number; status: string; note?: string; paidAtUtc: string; paymentId: string; paymentNo: string; paymentStatus: string; paymentRefundedMinor: number; paymentVersion: number; allocations: PaymentAllocation[] }
export interface CashierShift { id: string; shiftNo: string; operatorId: string; status: string; openingCashMinor: number; expectedCashMinor?: number; submittedCashMinor?: number; cashDifferenceMinor?: number; pendingReconciliationMinor?: number; handoverNote?: string; openedAtUtc: string; submittedAtUtc?: string; reviewedBy?: string; reviewReason?: string; closedAtUtc?: string; version: number }
export interface CashierShiftReview { shift: CashierShift; operatorDisplayName: string }
export interface AuditEvent { id: string; action: string; entityType: string; entityId?: string; previousState?: string; currentState?: string; reason?: string; operatorId?: string; operatorDisplayName: string; requestId?: string; traceId: string; occurredAtUtc: string }
export interface AuditEventPage { items: AuditEvent[]; total: number; page: number; pageSize: number }
export interface OperationsSummary { settledRevenueMinor: number; recordedFundsMinor: number; pendingReconciliationMinor: number; refundMinor: number; netRevenueMinor: number; settledOrderCount: number; visitCount: number; averageTicketMinor: number; facilityActiveSeconds: number }
export interface DailyOperations { date: string; settledRevenueMinor: number; recordedFundsMinor: number; pendingReconciliationMinor: number; refundMinor: number; netRevenueMinor: number; orderCount: number; visitCount: number; facilityActiveSeconds: number }
export interface PaymentMix { methodCode: string; methodName: string; amountMinor: number; pendingReconciliationMinor: number; refundMinor: number; netAmountMinor: number; allocationCount: number }
export interface ServicePerformance { serviceItemId: string; itemCode: string; itemName: string; quantity: number; revenueMinor: number; orderCount: number }
export interface FacilityUsage { facilityId: string; facilityName: string; activeSeconds: number; usageShare: number }
export interface OperationsReport { fromDate: string; toDate: string; timeZoneId: string; summary: OperationsSummary; daily: DailyOperations[]; paymentMix: PaymentMix[]; services: ServicePerformance[]; facilities: FacilityUsage[] }
