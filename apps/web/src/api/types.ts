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
