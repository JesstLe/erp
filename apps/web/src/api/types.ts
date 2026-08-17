export interface AuthorizedStore { id: string; code: string; name: string; isDefault: boolean }
export interface CurrentUser { id: string; tenantId: string; displayName: string; account: string; roles: string[]; stores: AuthorizedStore[] }
export interface ServiceItem { id: string; code: string; name: string; standardDurationMinutes: number; status: string; version: number }
export interface PriceBookLine { serviceItemId: string; serviceItemName: string; unitPriceMinor: number }
export interface PriceBook { id: string; name: string; status: string; effectiveFrom: string; publishedAtUtc?: string; lines: PriceBookLine[] }

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
export interface ServiceOrder { id: string; orderNo: string; visitId: string; customerId?: string; status: string; priceBookId: string; referenceAmountMinor: number; receivableMinor: number; note?: string; version: number; createdAtUtc: string; lines: ServiceOrderLine[] }
export interface PaymentMethod { id: string; code: string; name: string; category: string; requiresOpenShift: boolean }
export interface PaymentAllocation { id: string; methodId: string; methodCode: string; methodName: string; category: string; amountMinor: number; externalReference?: string; confirmationStatus: string; reconciliationStatus: string; shiftId?: string }
export interface Payment { id: string; paymentNo: string; orderId: string; status: string; currency: string; receivableMinor: number; paidMinor: number; paidAtUtc?: string; allocations: PaymentAllocation[] }
export interface CashierShift { id: string; shiftNo: string; operatorId: string; status: string; openingCashMinor: number; expectedCashMinor?: number; submittedCashMinor?: number; cashDifferenceMinor?: number; pendingReconciliationMinor?: number; handoverNote?: string; openedAtUtc: string; submittedAtUtc?: string; reviewedBy?: string; reviewReason?: string; closedAtUtc?: string; version: number }
