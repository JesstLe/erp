export interface AuthorizedStore { id: string; code: string; name: string; isDefault: boolean }
export interface CurrentUser { id: string; tenantId: string; displayName: string; account: string; mustChangePassword: boolean; roles: string[]; stores: AuthorizedStore[] }
export interface EmployeeStore { id: string; code: string; name: string; isPrimary: boolean }
export interface Employee { id: string; employeeNo: string; displayName: string; positionCode: string; status: string; userId?: string; account?: string; accountEnabled?: boolean; mustChangePassword?: boolean; roles: string[]; stores: EmployeeStore[]; createdAtUtc: string }
export interface EmployeeRole { id: string; code: string; name: string }
export interface ServiceItem { id: string; code: string; name: string; standardDurationMinutes: number; status: string; version: number }
export interface ProductItem { id: string; code: string; name: string; unitName: string; trackInventory: boolean; imageFileId?: string; status: string; version: number }
export interface PriceBookLine { serviceItemId: string; serviceItemName: string; unitPriceMinor: number }
export interface ProductPriceBookLine { productItemId: string; productItemName: string; unitName: string; unitPriceMinor: number }
export interface PriceBook { id: string; name: string; status: string; effectiveFrom: string; publishedAtUtc?: string; lines: PriceBookLine[]; productLines: ProductPriceBookLine[] }

export interface FacilityGroup { id: string; displayName: string; sortOrder: number }
export interface FacilityType { id: string; displayName: string }
export interface FacilityConfigurationStore { id: string; code: string; name: string; status: string; managerNames: string[]; groupCount: number; facilityCount: number; enabledFacilityCount: number }
export interface FacilityConfigurationItem { id: string; groupId: string; facilityTypeId: string; typeName: string; code: string; displayName: string; serviceName?: string | null; equipmentName?: string | null; referencePriceMinor?: number | null; sortOrder: number; defaultCleaningMinutes: number; allowReservation: boolean; lifecycleStatus: string; version: number; hasOpenSession: boolean }
export interface FacilityConfigurationGroup { id: string; displayName: string; sortOrder: number; version: number; facilities: FacilityConfigurationItem[] }
export interface FacilityConfiguration { storeId: string; storeCode: string; storeName: string; managerNames: string[]; groups: FacilityConfigurationGroup[] }
export interface FacilityBoardItem {
  id: string; code: string; displayName: string; typeName: string; status: string; version: number
  sessionId?: string; visitId?: string; visitNo?: string; sessionStatus?: string; startedAtUtc?: string
  activeSeconds: number; pausedSeconds: number; expectedDurationMinutes?: number; note?: string
  serviceName?: string | null; equipmentName?: string | null; referencePriceMinor?: number | null
  customerId?: string | null; customerDisplayName?: string | null
  plannedServiceItemId?: string | null; plannedServiceItemName?: string | null
}
export interface FacilityBoardGroup { id: string; displayName: string; facilities: FacilityBoardItem[] }
export interface FacilityBoard { serverNowUtc: string; groups: FacilityBoardGroup[] }

export interface CustomerSummary { id: string; displayName: string; maskedMobile: string; status: string; homeStoreId: string; activeCardCount: number; createdAtUtc: string }
export interface MemberCardType { id: string; code: string; name: string; validityDays?: number; status: string }
export interface MemberAccount { id: string; accountType: string; balanceUnits: number; status: string }
export interface MemberCard { id: string; cardTypeName: string; maskedCardNo: string; status: string; validFrom: string; validTo?: string; accounts: MemberAccount[] }
export interface CustomerDetail { id: string; displayName: string; maskedMobile: string; gender: string; sourceCode?: string; serviceNotificationConsent: boolean; marketingConsent: boolean; status: string; homeStoreId: string; version: number; cards: MemberCard[] }
export interface ServiceRecordAttachment { fileId: string; fileName: string; contentType: string; sizeBytes: number }
export interface ServiceRecord { id: string; storeId: string; customerId: string; serviceOrderId?: string; serviceOrderNo?: string; serviceOccurredAtUtc: string; conditionNotes?: string; serviceContent?: string; followUpNotes?: string; createdBy: string; createdByName: string; createdAtUtc: string; attachments: ServiceRecordAttachment[] }
export interface ServiceRecordOrderOption { id: string; orderNo: string; status: string; createdAtUtc: string }
export interface CashierVisit { id: string; visitNo: string; status: string; customerId?: string; customerDisplayName: string; plannedServiceItemId?: string; plannedServiceItemName?: string; facilityNames: string; arrivedAtUtc: string; serviceEndedAtUtc?: string; facilitySeconds: number; note?: string }
export interface ServiceOrderLine { id: string; lineType: 'Service' | 'Product'; serviceItemId?: string; productItemId?: string; itemCode: string; itemName: string; unitName?: string; quantity: number; returnedQuantity: number; actualSeconds?: number; referencePriceMinor: number; enteredPriceMinor: number; lineAmountMinor: number; priceOverrideReason?: string }
export interface ServiceOrder { id: string; orderNo: string; visitId: string; customerId?: string; status: string; priceBookId: string; referenceAmountMinor: number; receivableMinor: number; refundedMinor: number; note?: string; version: number; createdAtUtc: string; lines: ServiceOrderLine[] }
export interface PaymentMethod { id: string; code: string; name: string; category: string; requiresOpenShift: boolean; internalAccountType?: string; channelProvider?: string }
export interface PaymentChannelConfiguration { id: string; storeId: string; provider: string; environment: string; displayName: string; credentialProfile: string; isEnabled: boolean; credentialsPresent: boolean; missingRequirements: string[]; version: number }
export interface PaymentChannelOrder { id: string; configurationId: string; paymentId: string; paymentAllocationId: string; provider: string; outTradeNo: string; amountMinor: number; status: string; qrPayload?: string; providerTradeNo?: string; failureCode?: string; expiresAtUtc: string; paidAtUtc?: string; closedAtUtc?: string; lastQueriedAtUtc?: string; paymentStatus: string; serviceOrderStatus: string; version: number }
export interface PaymentChannelReconciliationItem { id: string; itemType: string; status: string; matchKey: string; outTradeNo?: string; outRefundNo?: string; providerTradeNo?: string; paymentAllocationId?: string; channelRefundId?: string; localAmountMinor?: number; channelAmountMinor?: number; channelFeeMinor: number; localStatus?: string; channelStatus?: string; resolvedBy?: string; resolvedAtUtc?: string; resolutionReason?: string; version: number }
export interface PaymentChannelReconciliationRun { id: string; configurationId: string; provider: string; businessDate: string; attemptNo: number; status: string; channelEntryCount: number; matchedCount: number; differenceCount: number; sourceSha256?: string; failureCode?: string; startedBy: string; startedAtUtc: string; completedAtUtc?: string; version: number; items: PaymentChannelReconciliationItem[] }
export interface PaymentAllocation { id: string; methodId: string; methodCode: string; methodName: string; category: string; amountMinor: number; externalReference?: string; confirmationStatus: string; reconciliationStatus: string; shiftId?: string; memberAccountId?: string; channelProvider?: string }
export interface Payment { id: string; paymentNo: string; orderId?: string; businessType: string; businessId: string; status: string; currency: string; receivableMinor: number; paidMinor: number; refundedMinor: number; paidAtUtc?: string; version: number; allocations: PaymentAllocation[] }
export interface RefundLine { id: string; originalAllocationId: string; amountMinor: number; category: string; memberAccountId?: string; route: string; cashShiftId?: string; completedAtUtc?: string }
export interface ChannelRefund { id: string; provider: string; outRefundNo: string; providerRefundNo?: string; amountMinor: number; status: string; failureCode?: string; lastQueriedAtUtc?: string; succeededAtUtc?: string; version: number }
export interface Refund { id: string; paymentId: string; businessType: string; businessId: string; refundNo: string; status: string; amountMinor: number; reason: string; requestedBy: string; requestedAtUtc: string; approvedBy?: string; completedAtUtc?: string; rejectionReason?: string; version: number; lines: RefundLine[]; channelRefund?: ChannelRefund }
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
export interface InventoryBalance { productItemId: string; productCode: string; productName: string; unitName: string; trackInventory: boolean; onHandQuantity: number; reservedQuantity: number; availableQuantity: number; version: number }
export interface InventoryMovement { id: string; productItemId: string; productCode: string; productName: string; unitName: string; movementType: string; direction: string; quantity: number; onHandBefore: number; onHandAfter: number; sourceType: string; sourceId: string; sourceLineId: string; commandId: string; operatorId?: string; occurredAtUtc: string }
export interface InventoryDocumentLine { id: string; productItemId: string; productCode: string; productName: string; unitName: string; quantity: number }
export interface InventoryDocument { id: string; documentNo: string; documentType: string; reason: string; postedBy: string; postedAtUtc: string; lines: InventoryDocumentLine[] }
export interface ProductReturn { id: string; orderId: string; orderLineId: string; productItemId: string; productCode: string; productName: string; unitName: string; quantity: number; reason: string; returnedBy: string; returnedAtUtc: string }
