import { describe, expect, it } from 'vitest'
import { applyMemberDiscountAndRoundToWholeYuan, buildRemainingRefundLines, isServicePassDue } from './membershipRules'

it('rounds discounted member prices to the nearest whole yuan', () => {
  expect(applyMemberDiscountAndRoundToWholeYuan(5_900, 8_305)).toBe(4_900)
  expect(applyMemberDiscountAndRoundToWholeYuan(9_900, 9_500)).toBe(9_400)
  expect(applyMemberDiscountAndRoundToWholeYuan(10_100, 5_000)).toBe(5_100)
})

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
