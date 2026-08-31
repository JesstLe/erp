export interface ClassicCashierDraftLine {
  key: string
  lineType: 'Service' | 'Product'
  itemId: string
  code: string
  name: string
  unitName?: string
  quantity: number
  actualMinutes?: number
  referencePriceMinor: number
  referencePriceDefined?: boolean
  enteredPriceMinor: number
  employeeId?: string
  employeeName?: string
  priceOverrideReason?: string
}

export function classicCashierLineAmount(line: ClassicCashierDraftLine) {
  return line.enteredPriceMinor * line.quantity
}

export function classicCashierTotal(lines: ClassicCashierDraftLine[]) {
  return lines.reduce((total, line) => total + classicCashierLineAmount(line), 0)
}

export function matchesClassicCatalogSearch(code: string, name: string, keyword: string) {
  const normalized = keyword.trim().toLocaleLowerCase('zh-CN')
  if (!normalized) return true
  return code.toLocaleLowerCase('zh-CN').includes(normalized) || name.toLocaleLowerCase('zh-CN').includes(normalized)
}

export function applyClassicOrderDiscount(lines: ClassicCashierDraftLine[], percent: number, reason: string) {
  const factor = Math.max(0, Math.min(100, percent)) / 100
  return lines.map((line) => ({
    ...line,
    enteredPriceMinor: Math.round(line.referencePriceMinor * factor),
    priceOverrideReason: factor === 1 ? undefined : reason,
  }))
}
