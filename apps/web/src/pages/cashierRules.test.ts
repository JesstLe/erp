import { describe, expect, it } from 'vitest'
import { cashAmountMinor, cashTenderedMinorForSubmission, hasAllocationCategory } from './cashierRules'

const methods = [
  { id: 'cash', category: 'Cash' },
  { id: 'member', category: 'InternalAccount' },
]

describe('cashier cash presentation rules', () => {
  it('calculates cash only from cash allocations in a mixed settlement', () => {
    const allocations = [
      { methodId: 'cash', amountYuan: 40 },
      { methodId: 'member', amountYuan: 60 },
    ]
    expect(cashAmountMinor(allocations, methods)).toBe(4_000)
    expect(cashTenderedMinorForSubmission(allocations, methods, 50)).toBe(5_000)
  })

  it('does not submit a stale tender when cash is removed', () => {
    const allocations = [{ methodId: 'member', amountYuan: 100 }]
    expect(cashAmountMinor(allocations, methods)).toBe(0)
    expect(cashTenderedMinorForSubmission(allocations, methods, 200)).toBeNull()
  })

  it('recognizes a manual external receipt independently from real channel payments', () => {
    const paymentMethods = [
      ...methods,
      { id: 'wechat-manual', category: 'ManualExternal' },
      { id: 'wechat-channel', category: 'ChannelExternal' },
    ]
    const allocations = [{ methodId: 'wechat-manual', amountYuan: 100 }]

    expect(hasAllocationCategory(allocations, paymentMethods, 'ManualExternal')).toBe(true)
    expect(hasAllocationCategory(allocations, paymentMethods, 'ChannelExternal')).toBe(false)
  })
})
