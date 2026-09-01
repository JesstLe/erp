import type { MemberCard, PaymentMethod } from '../api/types'
import type { ClassicCashierDraftLine } from '../classic/classicCashierRules'
import { buildManualPaymentReference } from './modernFacilityCashierPayments'

export interface SettlementValues {
  methodId?: string
  customerId?: string
  memberCardId?: string
  discountYuan?: number
  cashYuan?: number
  cashTenderedYuan?: number
  wechatYuan?: number
  wechatReference?: string
  alipayYuan?: number
  alipayReference?: string
  unionPayYuan?: number
  unionPayReference?: string
  groupBuyYuan?: number
  groupBuyPlatform?: string
  groupBuyReference?: string
  memberYuan?: number
  verifiedMobile?: string
  settlementNote?: string
  autoPrint?: boolean
}

export interface SettlementAllocation {
  methodId: string
  amountMinor: number
  externalReference: string | null
  memberAccountId: string | null
}

function toMinor(value?: number) {
  if (value === undefined || value === null || !Number.isFinite(value)) return 0
  return Math.round(value * 100)
}

function activeAccounts(card: MemberCard | undefined) {
  return card?.accounts.filter((account) => account.status.toUpperCase() === 'ACTIVE') ?? []
}

function referenceFor(method: PaymentMethod, explicitReference: string | undefined,
  groupBuyPlatform: string | undefined, fallbackReference: string) {
  if (method.category !== 'ManualExternal') return null
  const reference = explicitReference?.trim() || fallbackReference
  return buildManualPaymentReference(method.code, reference,
    method.code === 'GROUP_BUY_MANUAL' ? groupBuyPlatform || '美团' : undefined)
}

export function applySettlementDiscount(lines: ClassicCashierDraftLine[], discountMinor: number) {
  const gross = lines.reduce((sum, line) => sum + line.referencePriceMinor * line.quantity, 0)
  if (!Number.isInteger(discountMinor) || discountMinor < 0 || discountMinor > gross)
    throw new Error('优惠金额必须在0与消费原价之间')

  let remaining = discountMinor
  const result: ClassicCashierDraftLine[] = lines.map((line) => ({
    ...line,
    enteredPriceMinor: line.referencePriceMinor,
    priceOverrideReason: undefined as string | undefined,
    pricingSource: 'ListPrice' as const,
    memberDiscountBasisPoints: undefined,
    memberCardTypeId: undefined,
    memberCardTypeName: undefined,
  }))
  const order = result.map((line, index) => ({ line, index }))
    .sort((left, right) => (left.line.quantity === 1 ? 1 : 0) - (right.line.quantity === 1 ? 1 : 0))

  for (const { line } of order) {
    if (remaining === 0) break
    const maximum = line.referencePriceMinor * line.quantity
    const applicable = line.quantity === 1
      ? Math.min(remaining, maximum)
      : Math.min(Math.floor(remaining / line.quantity) * line.quantity, maximum)
    line.enteredPriceMinor -= applicable / line.quantity
    remaining -= applicable
  }
  if (remaining !== 0)
    throw new Error('当前明细数量无法精确分摊该优惠金额，请先调整某一行成交价或数量')
  for (const line of result) {
    if (line.enteredPriceMinor !== line.referencePriceMinor)
    {
      line.priceOverrideReason = '结算窗口覆盖优惠金额'
      line.pricingSource = 'ManualOverride'
    }
  }
  return result
}

export function buildSettlementAllocations(input: {
  values: SettlementValues
  methods: PaymentMethod[]
  cards: MemberCard[]
  receivableMinor: number
  orderNo: string
  inheritedReference?: string
}) {
  const { values, methods, cards, receivableMinor, orderNo } = input
  const allocations = new Map<string, SettlementAllocation>()
  const fallbackReference = input.inheritedReference?.trim() || `人工登记-${orderNo}`
  const methodByCode = new Map(methods.map((method) => [method.code, method]))
  const methodById = new Map(methods.map((method) => [method.id, method]))

  const append = (method: PaymentMethod, amountMinor: number, reference?: string | null,
    memberAccountId?: string | null) => {
    if (amountMinor <= 0) return
    const previous = allocations.get(method.id)
    if (previous && previous.memberAccountId !== (memberAccountId ?? null))
      throw new Error('同一支付方式不能同时使用不同会员账户')
    allocations.set(method.id, {
      methodId: method.id,
      amountMinor: (previous?.amountMinor ?? 0) + amountMinor,
      externalReference: reference ?? previous?.externalReference ?? null,
      memberAccountId: memberAccountId ?? previous?.memberAccountId ?? null,
    })
  }

  const manualOverrides = [
    ['CASH', values.cashYuan, undefined, undefined],
    ['WECHAT_MANUAL', values.wechatYuan, values.wechatReference, undefined],
    ['ALIPAY_MANUAL', values.alipayYuan, values.alipayReference, undefined],
    ['BANK_CARD_MANUAL', values.unionPayYuan, values.unionPayReference, undefined],
    ['GROUP_BUY_MANUAL', values.groupBuyYuan, values.groupBuyReference, values.groupBuyPlatform],
  ] as const
  for (const [code, amountYuan, explicitReference, platform] of manualOverrides) {
    const amountMinor = toMinor(amountYuan)
    if (amountMinor === 0) continue
    const method = methodByCode.get(code)
    if (!method) throw new Error(`${code}支付方式未启用`)
    append(method, amountMinor, referenceFor(method, explicitReference, platform, fallbackReference))
  }

  const appendMember = (amountMinor: number) => {
    if (amountMinor <= 0) return
    const selectedCard = cards.find((card) => card.id === values.memberCardId) ??
      (cards.filter((card) => card.status.toUpperCase() === 'ACTIVE').length === 1
        ? cards.find((card) => card.status.toUpperCase() === 'ACTIVE') : undefined)
    if (!selectedCard) throw new Error('请选择本次扣款使用的会员卡')
    if (!values.verifiedMobile?.trim()) throw new Error('使用会员卡扣款时必须核对完整手机号')
    const accounts = activeAccounts(selectedCard)
    const principal = accounts.find((account) => account.accountType === 'Principal')
    const bonus = accounts.find((account) => account.accountType === 'Bonus')
    const principalAmount = Math.min(principal?.balanceUnits ?? 0, amountMinor)
    const bonusAmount = amountMinor - principalAmount
    if (bonusAmount > (bonus?.balanceUnits ?? 0)) throw new Error('所选会员卡余额不足')
    if (principalAmount > 0) {
      const method = methodByCode.get('MEMBER_PRINCIPAL')
      if (!method || !principal) throw new Error('会员储值本金支付方式不可用')
      append(method, principalAmount, null, principal.id)
    }
    if (bonusAmount > 0) {
      const method = methodByCode.get('MEMBER_BONUS')
      if (!method || !bonus) throw new Error('会员奖励金支付方式不可用')
      append(method, bonusAmount, null, bonus.id)
    }
  }

  appendMember(toMinor(values.memberYuan))
  const explicitlyAllocated = [...allocations.values()].reduce((sum, line) => sum + line.amountMinor, 0)
  if (explicitlyAllocated > receivableMinor) throw new Error('各支付金额合计不能超过本次应收金额')
  const remaining = receivableMinor - explicitlyAllocated
  let channelMethod: PaymentMethod | undefined
  if (remaining > 0) {
    const method = values.methodId ? methodById.get(values.methodId) : undefined
    if (!method) throw new Error('请选择未填写金额时沿用的默认支付方式')
    if (method.channelProvider) {
      if (explicitlyAllocated > 0) throw new Error('官方支付渠道暂不能与人工登记方式混合结算')
      channelMethod = method
    } else if (method.category === 'InternalAccount') {
      appendMember(remaining)
    } else {
      const explicitReference = method.code === 'WECHAT_MANUAL' ? values.wechatReference
        : method.code === 'ALIPAY_MANUAL' ? values.alipayReference
          : method.code === 'BANK_CARD_MANUAL' ? values.unionPayReference
            : method.code === 'GROUP_BUY_MANUAL' ? values.groupBuyReference : undefined
      append(method, remaining, referenceFor(method, explicitReference, values.groupBuyPlatform,
        fallbackReference))
    }
  }

  const cashMethod = methodByCode.get('CASH')
  const cashAmountMinor = cashMethod ? allocations.get(cashMethod.id)?.amountMinor ?? 0 : 0
  const cashTenderedMinor = cashAmountMinor > 0
    ? Math.max(cashAmountMinor, toMinor(values.cashTenderedYuan) || cashAmountMinor) : null
  return { allocations: [...allocations.values()], cashTenderedMinor, channelMethod }
}
