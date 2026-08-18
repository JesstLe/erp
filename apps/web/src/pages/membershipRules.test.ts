import { describe, expect, it } from 'vitest'
import { buildRemainingRefundLines, isServicePassDue } from './membershipRules'

describe('membership rules', () => {
  it('allocates a later partial refund after earlier allocation amounts', () => {
    expect(buildRemainingRefundLines([{ id: 'cash', amountMinor: 30000 }, { id: 'manual', amountMinor: 20000 }],
      20000, 20000)).toEqual([
      { originalAllocationId: 'cash', amountMinor: 10000 },
      { originalAllocationId: 'manual', amountMinor: 10000 },
    ])
  })

  it('rejects a refund larger than the remaining original payment', () => {
    expect(() => buildRemainingRefundLines([{ id: 'cash', amountMinor: 50000 }], 40000, 10001))
      .toThrow('可退支付分摊不足')
  })

  it('treats a pass as due only after its valid-through date', () => {
    expect(isServicePassDue('Active', '2026-08-17', '2026-08-18')).toBe(true)
    expect(isServicePassDue('Active', '2026-08-18', '2026-08-18')).toBe(false)
    expect(isServicePassDue('Expired', '2026-08-17', '2026-08-18')).toBe(false)
  })
})
