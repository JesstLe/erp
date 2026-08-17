export interface AuthorizedStore { id: string; code: string; name: string; isDefault: boolean }
export interface CurrentUser { id: string; tenantId: string; displayName: string; account: string; roles: string[]; stores: AuthorizedStore[] }
export interface ServiceItem { id: string; code: string; name: string; standardDurationMinutes: number; status: string; version: number }
export interface PriceBookLine { serviceItemId: string; serviceItemName: string; unitPriceMinor: number }
export interface PriceBook { id: string; name: string; status: string; effectiveFrom: string; publishedAtUtc?: string; lines: PriceBookLine[] }

