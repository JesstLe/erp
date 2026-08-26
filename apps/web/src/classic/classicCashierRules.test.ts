import { describe, expect, it } from 'vitest'
import { applyClassicOrderDiscount, classicCashierTotal, matchesClassicCatalogSearch, type ClassicCashierDraftLine } from './classicCashierRules'

const lines: ClassicCashierDraftLine[] = [
  { key: '1', lineType: 'Service', itemId: 's1', code: 'S001', name: '基础服务', quantity: 2, referencePriceMinor: 3900, enteredPriceMinor: 3900 },
  { key: '2', lineType: 'Product', itemId: 'p1', code: 'P001', name: '护理产品', quantity: 1, referencePriceMinor: 2800, enteredPriceMinor: 2800 },
]

describe('classic cashier rules', () => {
  it('filters catalog by code or Chinese name while typing', () => {
    expect(matchesClassicCatalogSearch('S001', '基础服务', 's00')).toBe(true)
    expect(matchesClassicCatalogSearch('S001', '基础服务', '基础')).toBe(true)
    expect(matchesClassicCatalogSearch('S001', '基础服务', '产品')).toBe(false)
  })

  it('applies an order discount and recalculates the total', () => {
    const discounted = applyClassicOrderDiscount(lines, 80, '整单八折')
    expect(discounted.map((line) => line.enteredPriceMinor)).toEqual([3120, 2240])
    expect(classicCashierTotal(discounted)).toBe(8480)
  })
})
