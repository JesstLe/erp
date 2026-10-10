import { describe, expect, it } from 'vitest'
import type { MemberCard, PaymentMethod } from '../api/types'
import { applySettlementDiscount, buildSettlementAllocations, defaultSettlementMethod, validSettlementCards } from './modernFacilitySettlement'

const methods: PaymentMethod[] = [
  { id: 'cash', code: 'CASH', name: '现金', category: 'Cash', requiresOpenShift: true },
  { id: 'wechat', code: 'WECHAT_MANUAL', name: '微信', category: 'ManualExternal', requiresOpenShift: true },
  { id: 'group', code: 'GROUP_BUY_MANUAL', name: '团购', category: 'ManualExternal', requiresOpenShift: true },
  { id: 'principal', code: 'MEMBER_PRINCIPAL', name: '本金', category: 'InternalAccount', requiresOpenShift: false, internalAccountType: 'Principal' },
  { id: 'bonus', code: 'MEMBER_BONUS', name: '赠送金', category: 'InternalAccount', requiresOpenShift: false, internalAccountType: 'Bonus' },
]
const cards: MemberCard[] = [{ id: 'card-1', cardTypeId: 'card-type-1', cardTypeName: '储值卡', maskedCardNo: '****1234', status: 'Active', validFrom: '2026-01-01', serviceDiscountBasisPoints: 10_000, productDiscountBasisPoints: 10_000, accounts: [
  { id: 'principal-account', accountType: 'Principal', balanceUnits: 5_000, status: 'Active' },
  { id: 'bonus-account', accountType: 'Bonus', balanceUnits: 2_000, status: 'Active' },
] }]

describe('modern facility settlement', () => {
  it('defaults linked stored-value members to principal, and other customers to cash', () => {
    expect(defaultSettlementMethod(methods, cards)?.code).toBe('MEMBER_PRINCIPAL')
    expect(defaultSettlementMethod(methods, [])?.code).toBe('CASH')
    expect(defaultSettlementMethod(methods, [{ ...cards[0], status: 'Frozen' }])?.code).toBe('CASH')
    expect(validSettlementCards([{ ...cards[0], validTo: '2026-01-02' }], '2026-10-10')).toEqual([])
  })

  it('uses principal then bonus for the default method without re-entering the linked phone', () => {
    const result = buildSettlementAllocations({ values: { methodId: 'principal', memberCardId: 'card-1' }, methods, cards, receivableMinor: 6_000, orderNo: 'SO001' })
    expect(result.allocations.map((line) => [line.methodId, line.amountMinor])).toEqual([['principal', 5_000], ['bonus', 1_000]])
  })

  it('does not reuse principal balance when explicit member payment and the default both use the card', () => {
    const result = buildSettlementAllocations({ values: { methodId: 'principal', memberYuan: 40 }, methods, cards, receivableMinor: 6_000, orderNo: 'SO001' })
    expect(result.allocations.map((line) => [line.methodId, line.amountMinor])).toEqual([['principal', 5_000], ['bonus', 1_000]])
    expect(() => buildSettlementAllocations({ values: { methodId: 'principal', memberYuan: 40 }, methods, cards, receivableMinor: 8_000, orderNo: 'SO001' })).toThrow('余额不足')
  })

  it('requires an explicit card choice for multiple valid cards and rejects an expired selected card', () => {
    expect(() => buildSettlementAllocations({ values: { methodId: 'principal' }, methods, cards: [...cards, { ...cards[0], id: 'other' }], receivableMinor: 1_000, orderNo: 'SO001' })).toThrow('请选择')
    expect(() => buildSettlementAllocations({ values: { methodId: 'principal', memberCardId: 'expired' }, methods, cards: [...cards, { ...cards[0], id: 'expired', validTo: '2026-01-02' }], receivableMinor: 1_000, orderNo: 'SO001' })).toThrow('请选择')
  })
  it('uses explicit split values first and leaves the remainder to the inherited method', () => {
    const result = buildSettlementAllocations({ values: { methodId: 'cash', wechatYuan: 30, wechatReference: 'WX-001' }, methods, cards: [], receivableMinor: 10_000, orderNo: 'SO001' })
    expect(result.allocations).toEqual([
      { methodId: 'wechat', amountMinor: 3_000, externalReference: 'WX-001', memberAccountId: null },
      { methodId: 'cash', amountMinor: 7_000, externalReference: null, memberAccountId: null },
    ])
  })

  it('deducts member principal before bonus and keeps group-buy platform', () => {
    const result = buildSettlementAllocations({ values: { methodId: 'cash', memberCardId: 'card-1', memberYuan: 60, verifiedMobile: '13800000000', groupBuyYuan: 10, groupBuyPlatform: '抖音', groupBuyReference: 'DY-1' }, methods, cards, receivableMinor: 8_000, orderNo: 'SO001' })
    expect(result.allocations).toEqual([
      { methodId: 'group', amountMinor: 1_000, externalReference: '抖音:DY-1', memberAccountId: null },
      { methodId: 'principal', amountMinor: 5_000, externalReference: null, memberAccountId: 'principal-account' },
      { methodId: 'bonus', amountMinor: 1_000, externalReference: null, memberAccountId: 'bonus-account' },
      { methodId: 'cash', amountMinor: 1_000, externalReference: null, memberAccountId: null },
    ])
  })

  it('overrides previous line prices with an exact settlement discount', () => {
    const result = applySettlementDiscount([{ key: '1', lineType: 'Service', itemId: 'service', code: 'SV1', name: '服务', quantity: 1, referencePriceMinor: 10_000, enteredPriceMinor: 8_000 }], 1_500)
    expect(result[0].enteredPriceMinor).toBe(8_500)
    expect(result[0].priceOverrideReason).toBe('结算窗口覆盖优惠金额')
  })
})
