// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { ModernFacilityCashierWorkbench } from './ModernFacilityCashierWorkbench'
import { ClassicCashierFacilitiesPage } from '../classic/ClassicCashierFacilitiesPage'
import { buildManualPaymentReference, groupBuyPlatforms } from './modernFacilityCashierPayments'

const apiRequestMock = vi.hoisted(() => vi.fn())
vi.mock('../api/client', () => ({
  apiRequest: apiRequestMock,
  ApiError: class ApiError extends Error { code = 'REQUEST_FAILED' },
}))
vi.mock('../auth/useAuth', () => ({
  useAuth: () => ({ store: { id: 'store-1', code: 'S001', name: '测试门店' } }),
}))
vi.mock('../security/useAuthorization', () => ({
  useAuthorization: () => ({ can: () => true, permissions: [] }),
}))

const facility = {
  id: 'facility-1', code: 'F001', displayName: '一号服务位', typeName: '服务位', status: 'AVAILABLE',
  version: 1, activeSeconds: 0, pausedSeconds: 0,
}

beforeAll(() => {
  Object.defineProperty(window, 'matchMedia', { writable: true, value: vi.fn().mockImplementation(() => ({
    matches: false, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(),
    removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
  })) })
  Object.defineProperty(globalThis, 'ResizeObserver', { value: class ResizeObserver { observe() {} unobserve() {} disconnect() {} } })
  if (!globalThis.crypto.randomUUID) Object.defineProperty(globalThis.crypto, 'randomUUID', { value: () => '00000000-0000-4000-8000-000000000001' })
})
afterEach(cleanup)

describe('ModernFacilityCashierWorkbench before timing starts', () => {
  beforeEach(() => {
    apiRequestMock.mockReset().mockImplementation((path: string) => {
      if (path === '/api/v1/catalog/price-books') return Promise.resolve([{ id: 'book-1', name: '当前价目', status: 'PUBLISHED', effectiveFrom: '2026-01-01', version: 1, lines: [{ serviceItemId: 'service-1', serviceItemName: '基础服务', unitPriceMinor: 10_000 }], productLines: [{ productItemId: 'product-1', productItemName: '护理用品', unitName: '件', unitPriceMinor: 5_000 }] }])
      if (path === '/api/v1/catalog/service-items') return Promise.resolve([{ id: 'service-1', code: 'S001', name: '基础服务', standardDurationMinutes: 30, status: 'ENABLED', version: 1 }, { id: 'service-2', code: 'LEGACY-SVC-2', name: '迁移未定价服务', standardDurationMinutes: 0, status: 'ENABLED', version: 1 }])
      if (path === '/api/v1/catalog/products') return Promise.resolve([{ id: 'product-1', code: 'P001', name: '护理用品', unitName: '件', trackInventory: true, status: 'ENABLED', version: 1 }])
      if (path.startsWith('/api/v1/inventory/balances')) return Promise.resolve([{ productItemId: 'product-1', availableQuantity: 8 }])
      if (path.startsWith('/api/v1/cashier/service-employees')) return Promise.resolve([{ id: 'employee-1', employeeNo: 'E001', displayName: '李店员', positionCode: 'STAFF', positionName: '员工' }])
      if (path.startsWith('/api/v1/payments/methods')) return Promise.resolve([])
      if (path === '/api/v1/customers/cashier-search') return Promise.resolve({ items: [], total: 0, page: 1, pageSize: 30 })
      return Promise.reject(new Error(`unexpected request ${path}`))
    })
  })

  it('keeps the facility idle, displays selected service immediately, and asks for product added-by attribution', async () => {
    render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><ModernFacilityCashierWorkbench facility={facility} availableFacilities={[]} onFacilityChanged={vi.fn()} onExit={vi.fn()} onCompleted={vi.fn()} /></QueryClientProvider></MemoryRouter>)

    expect(await screen.findByText('待开始计时')).toBeTruthy()
    expect(screen.getByRole('button', { name: /开始.*计时/s })).toBeTruthy()
    const serviceButton = await screen.findByRole('button', { name: /基础服务/ })
    expect(screen.getAllByText('基础服务')).toHaveLength(1)
    fireEvent.click(serviceButton)
    expect(await screen.findByText(/1\. 基础服务/)).toBeTruthy()
    expect(screen.getByText('目录价 ¥100.00')).toBeTruthy()
    expect(screen.getByText('本次成交价')).toBeTruthy()
    const priceInput = screen.getByRole('spinbutton', { name: /本次成交价/ })
    fireEvent.change(priceInput, { target: { value: '88' } })
    expect(await screen.findByText('人工改价')).toBeTruthy()
    expect(screen.getByDisplayValue('现场调整成交价')).toBeTruthy()
    expect(screen.getByText('合计 ¥88.00')).toBeTruthy()
    expect(apiRequestMock).not.toHaveBeenCalledWith('/api/v1/facilities/sessions/start', expect.anything())

    fireEvent.click(screen.getByRole('button', { name: /产品.*列表/s }))
    fireEvent.click(await screen.findByRole('button', { name: /护理用品/ }))
    expect(await screen.findByText('添加人（可选）')).toBeTruthy()
    expect(screen.getByText(/用于记录是谁将该产品加入本次消费/)).toBeTruthy()
  })

  it('keeps an auditable external reference for manual and group-buy settlement', () => {
    expect(groupBuyPlatforms).toEqual(['美团', '抖音'])
    expect(buildManualPaymentReference('BANK_CARD_MANUAL', '  BANK-2026-0001  ')).toBe('BANK-2026-0001')
    expect(buildManualPaymentReference('GROUP_BUY_MANUAL', ' DY-889900 ', '抖音')).toBe('抖音:DY-889900')
  })

  it('loads enabled migrated catalog items even when the active price book has no matching line', async () => {
    render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><ModernFacilityCashierWorkbench facility={facility} availableFacilities={[]} onFacilityChanged={vi.fn()} onExit={vi.fn()} onCompleted={vi.fn()} /></QueryClientProvider></MemoryRouter>)

    const unpricedService = await screen.findByRole('button', { name: /迁移未定价服务.*未设置目录价/s })
    fireEvent.click(unpricedService)
    expect(await screen.findByText(/1\. 迁移未定价服务/)).toBeTruthy()
    expect(screen.getByText('请填写成交价')).toBeTruthy()
    const priceInput = screen.getByRole('spinbutton', { name: /本次成交价/ })
    fireEvent.change(priceInput, { target: { value: '68' } })
    expect(await screen.findByText('人工改价')).toBeTruthy()
    expect(screen.getByText('合计 ¥68.00')).toBeTruthy()
    expect(screen.getByDisplayValue('现场调整成交价')).toBeTruthy()
  })

  it('previews birthday, age, residence and remaining stored value before linking a member', async () => {
    const baseImplementation = apiRequestMock.getMockImplementation()
    apiRequestMock.mockImplementation((path: string, options?: unknown) => {
      if (path === '/api/v1/catalog/price-books') return Promise.resolve([{ id: 'book-1', name: '当前价目', status: 'PUBLISHED', effectiveFrom: '2026-01-01', version: 1, lines: [{ serviceItemId: 'service-1', serviceItemName: '基础服务', unitPriceMinor: 5_900 }], productLines: [] }])
      if (path === '/api/v1/customers/cashier-search') return Promise.resolve({ items: [{ id: 'customer-1', displayName: '王女士', mobile: '13800001234', status: 'Active', homeStoreId: 'store-1', homeStoreName: '测试门店', activeCardCount: 1, birthDate: '1990-05-06', residence: '水木清华小区', principalBalanceMinor: 12_000, bonusBalanceMinor: 3_000, createdAtUtc: '2026-01-01T00:00:00Z' }], total: 1, page: 1, pageSize: 30 })
      if (path.startsWith('/api/v1/customers/customer-1?')) return Promise.resolve({ id: 'customer-1', displayName: '王女士', maskedMobile: '13800001234', gender: 'Unknown', status: 'Active', homeStoreId: 'store-1', homeStoreName: '测试门店', version: 1, cards: [{ id: 'card-1', cardTypeId: 'card-type-1', cardTypeName: '金卡', maskedCardNo: 'CARD-001', status: 'Active', validFrom: '2026-01-01', serviceDiscountBasisPoints: 9_000, productDiscountBasisPoints: 9_000, serviceItemDiscounts: [{ catalogItemId: 'service-1', discountBasisPoints: 8_305 }], productItemDiscounts: [], accounts: [{ id: 'account-1', accountType: 'Principal', balanceUnits: 12_000, status: 'Active' }, { id: 'account-2', accountType: 'Bonus', balanceUnits: 3_000, status: 'Active' }] }], mergedAliases: [] })
      return baseImplementation?.(path, options)
    })

    render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><ModernFacilityCashierWorkbench facility={facility} availableFacilities={[]} onFacilityChanged={vi.fn()} onExit={vi.fn()} onCompleted={vi.fn()} /></QueryClientProvider></MemoryRouter>)

    fireEvent.click(await screen.findByRole('button', { name: /基础服务/ }))
    fireEvent.click(screen.getByRole('button', { name: /会员.*刷卡/s }))
    expect(screen.getByText('请输入姓名、完整手机号或卡号后查询会员')).toBeTruthy()
    expect(apiRequestMock.mock.calls.some(([path]) => path === '/api/v1/customers/cashier-search')).toBe(false)
    fireEvent.change(screen.getByPlaceholderText('输入姓名、完整手机号或卡号自动查询'), { target: { value: '13800001234' } })
    await waitFor(() => expect(apiRequestMock.mock.calls.some(([path]) => path === '/api/v1/customers/cashier-search')).toBe(true))
    fireEvent.click(await screen.findByRole('button', { name: /王女士.*13800001234/s }))
    expect(await screen.findByText('1990-05-06')).toBeTruthy()
    expect(screen.getByText('水木清华小区')).toBeTruthy()
    expect(screen.getByText('储值本金')).toBeTruthy()
    expect(screen.getByText('¥120.00')).toBeTruthy()
    expect(screen.getByText('赠送金额')).toBeTruthy()
    expect(screen.getByText('¥30.00')).toBeTruthy()
    expect(await screen.findByText('CARD-001')).toBeTruthy()
    expect(await screen.findByText('金卡')).toBeTruthy()
    expect(screen.getByRole('button', { name: /储值$/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /护理记录/ })).toBeTruthy()
    expect(screen.getByText(/岁$/)).toBeTruthy()
    await waitFor(() => expect(screen.getByRole('button', { name: '确认关联本次消费' })).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: '确认关联本次消费' }))
    expect(await screen.findByText(/8\.305折会员价/)).toBeTruthy()
    expect(screen.getByText('合计 ¥49.00')).toBeTruthy()
    fireEvent.change(screen.getByRole('spinbutton', { name: /本次成交价/ }), { target: { value: '70' } })
    expect(await screen.findByText('人工改价')).toBeTruthy()
    expect(screen.getByDisplayValue('现场调整成交价')).toBeTruthy()
    expect(screen.getByText('合计 ¥70.00')).toBeTruthy()
  })

  it('shows payment splits and visibly inherits the selected member and card into settlement', async () => {
    const order = {
      id: 'order-1', orderNo: 'SO-001', visitId: 'visit-1', status: 'Draft', version: 1,
      customerId: 'customer-1', referenceTotalMinor: 5_000, receivableMinor: 5_000,
      lines: [{ id: 'line-1', lineType: 'Product', productItemId: 'product-1', itemCode: 'P001', itemName: '护理用品', unitName: '件', quantity: 1, referencePriceMinor: 5_000, enteredPriceMinor: 5_000, lineAmountMinor: 5_000 }],
    }
    apiRequestMock.mockImplementation((path: string) => {
      if (path === '/api/v1/catalog/price-books') return Promise.resolve([])
      if (path === '/api/v1/catalog/service-items') return Promise.resolve([])
      if (path === '/api/v1/catalog/products') return Promise.resolve([])
      if (path.startsWith('/api/v1/inventory/balances')) return Promise.resolve([])
      if (path.startsWith('/api/v1/cashier/service-employees')) return Promise.resolve([])
      if (path.startsWith('/api/v1/payments/methods')) return Promise.resolve([
        { id: 'cash', code: 'CASH', name: '现金', category: 'Cash', isEnabled: true },
        { id: 'principal', code: 'MEMBER_PRINCIPAL', name: '会员储值本金', category: 'InternalAccount', internalAccountType: 'Principal' },
        { id: 'group-buy', code: 'GROUP_BUY_MANUAL', name: '团购平台核销', category: 'ManualExternal', isEnabled: true },
      ])
      if (path === '/api/v1/customers/cashier-search') return Promise.resolve({ items: [{ id: 'customer-1', displayName: '王女士', mobile: '13615345138', status: 'Active', homeStoreId: 'store-1', homeStoreName: '测试门店', activeCardCount: 1, principalBalanceMinor: 20_000, bonusBalanceMinor: 0, createdAtUtc: '2026-01-01T00:00:00Z' }], total: 1, page: 1, pageSize: 30 })
      if (path.startsWith('/api/v1/customers/customer-1?')) return Promise.resolve({ id: 'customer-1', displayName: '王女士', maskedMobile: '136****5138', gender: 'Unknown', status: 'Active', homeStoreId: 'store-1', homeStoreName: '测试门店', version: 1, cards: [{ id: 'card-1', cardTypeName: '储值卡', maskedCardNo: 'CARD-001', status: 'Active', validFrom: '2026-01-01', accounts: [{ id: 'account-1', accountType: 'Principal', balanceUnits: 20_000, status: 'Active' }] }], mergedAliases: [] })
      if (path === '/api/v1/cashier/visits/visit-1/draft') return Promise.resolve(order)
      if (path === '/api/v1/cashier/orders/order-1/draft') return Promise.resolve({ ...order, version: 2 })
      if (path === '/api/v1/cashier/orders/order-1/confirm') return Promise.resolve({ ...order, status: 'PendingPayment', version: 3 })
      if (path === '/api/v1/facilities/sessions/session-1/end') return Promise.resolve({ ...facility, status: 'AWAITING_PAYMENT', sessionId: 'session-1', visitId: 'visit-1' })
      if (path === '/api/v1/payments/orders/order-1/settle') return Promise.resolve({ id: 'payment-1', paymentNo: 'PM001', status: 'Paid', receivableMinor: 5_000, paidMinor: 5_000, allocations: [] })
      return Promise.reject(new Error(`unexpected request ${path}`))
    })
    const runningFacility = { ...facility, status: 'IN_USE', sessionId: 'session-1', visitId: 'visit-1', visitNo: 'V001', startedAtUtc: '2026-01-01T00:00:00Z' }
    const facilityChanged = vi.fn()
    render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><ModernFacilityCashierWorkbench facility={runningFacility} availableFacilities={[]} onFacilityChanged={facilityChanged} onExit={vi.fn()} onCompleted={vi.fn()} /></QueryClientProvider></MemoryRouter>)

    await screen.findByText(/1\. 护理用品/)
    await screen.findByText('会员：王女士')
    expect(apiRequestMock.mock.calls.some(([path]) => path === '/api/v1/customers/cashier-search')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: '结算' }))
    expect(await screen.findByText('收银结算')).toBeTruthy()
    expect(screen.getByText('CARD-001 · 储值卡')).toBeTruthy()
    expect(screen.getByText('已沿用主单会员：王女士')).toBeTruthy()
    expect(screen.getByText('会员储值本金')).toBeTruthy()
    expect(screen.queryByPlaceholderText('扣卡时核对完整手机号')).toBeNull()
    expect(screen.getByText('使用已关联会员，无需重复输入手机号')).toBeTruthy()
    expect(screen.queryByText(/显示团购|更多支付/)).toBeNull()
    expect(screen.getByText('团购支付')).toBeTruthy()
    expect(screen.getByText('美团')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: '确认收款' }))
    await screen.findByText('结算完成')
    expect(facilityChanged).toHaveBeenCalledWith(expect.objectContaining({ status: 'AWAITING_PAYMENT', sessionId: 'session-1', visitId: 'visit-1' }))
    const paymentRequest = apiRequestMock.mock.calls.find(([path]) => path === '/api/v1/payments/orders/order-1/settle')
    const body = JSON.parse(paymentRequest?.[1].body)
    expect(body.verifiedMobile).toBeUndefined()
    expect(body.allocations).toEqual([{ methodId: 'principal', amountMinor: 5_000, externalReference: null, memberAccountId: 'account-1' }])
  })

  it('restores the classic bill, keeps its facility occupied after end, and retries payment using the same order', async () => {
    const base = apiRequestMock.getMockImplementation()
    const running = { ...facility, status: 'IN_USE', sessionId: 'session-1', visitId: 'visit-1' }
    const order = { id: 'order-1', orderNo: 'SO1', visitId: 'visit-1', customerId: 'customer-1', status: 'Draft', version: 1, receivableMinor: 6_000, lines: [{ id: 'line-1', lineType: 'Product', productItemId: 'product-1', itemCode: 'P1', itemName: '护理用品', quantity: 1, referencePriceMinor: 6_000, enteredPriceMinor: 6_000 }] }
    let paymentAttempts = 0
    apiRequestMock.mockImplementation((path: string, options?: unknown) => {
      if (path.startsWith('/api/v1/facilities/board')) return Promise.resolve({ serverNowUtc: new Date().toISOString(), groups: [{ id: 'group-1', displayName: '服务区', facilities: [running] }] })
      if (path === '/api/v1/customers/search') return Promise.resolve({ items: [], total: 0 })
      if (path.startsWith('/api/v1/payments/methods')) return Promise.resolve([
        { id: 'cash', code: 'CASH', name: '现金', category: 'Cash' },
        { id: 'principal', code: 'MEMBER_PRINCIPAL', name: '会员储值本金', category: 'InternalAccount' },
        { id: 'bonus', code: 'MEMBER_BONUS', name: '会员奖励金', category: 'InternalAccount' },
      ])
      if (path.startsWith('/api/v1/customers/customer-1?')) return Promise.resolve({ id: 'customer-1', cards: [{ id: 'card-1', cardTypeName: '储值卡', maskedCardNo: 'CARD001', status: 'Active', validFrom: '2026-01-01', accounts: [{ id: 'p', accountType: 'Principal', status: 'Active', balanceUnits: 5_000 }, { id: 'b', accountType: 'Bonus', status: 'Active', balanceUnits: 2_000 }] }] })
      if (path === '/api/v1/cashier/visits/visit-1/draft') return Promise.resolve(order)
      if (path === '/api/v1/cashier/orders/order-1/draft') return Promise.resolve({ ...order, version: 2 })
      if (path === '/api/v1/facilities/sessions/session-1/end') return Promise.resolve({ ...running, status: 'AWAITING_PAYMENT' })
      if (path === '/api/v1/cashier/orders/order-1/confirm') return Promise.resolve({ ...order, version: 3, status: 'PendingPayment' })
      if (path === '/api/v1/payments/orders/order-1/settle') {
        paymentAttempts += 1
        return paymentAttempts === 1 ? Promise.reject(new Error('Temporary payment failure')) : Promise.resolve({ id: 'payment-1', status: 'Paid' })
      }
      return base?.(path, options)
    })
    render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><ClassicCashierFacilitiesPage /></QueryClientProvider></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: /一号服务位/ }))
    await screen.findByText(/1\. 护理用品/)
    fireEvent.click(screen.getByRole('button', { name: /结束.*服务/s }))
    await waitFor(() => expect((screen.getByRole('button', { name: /结束.*服务/s }) as HTMLButtonElement).disabled).toBe(true))
    fireEvent.click(screen.getByRole('button', { name: '结算' }))
    await screen.findByText('会员储值本金')
    expect(screen.queryByLabelText('会员完整手机号')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '确认收款' }))
    await waitFor(() => expect(paymentAttempts).toBe(1))
    await waitFor(() => expect((screen.getByRole('button', { name: /确认收款/ }) as HTMLButtonElement).className.includes('loading')).toBe(false))
    fireEvent.click(screen.getByRole('button', { name: /确认收款/ }))
    await waitFor(() => expect(paymentAttempts).toBe(2))
    expect(apiRequestMock.mock.calls.filter(([path]) => path.endsWith('/session-1/end'))).toHaveLength(1)
    expect(apiRequestMock.mock.calls.filter(([path]) => path.endsWith('/order-1/confirm'))).toHaveLength(1)
    const request = apiRequestMock.mock.calls.find(([path]) => path === '/api/v1/payments/orders/order-1/settle')
    const body = JSON.parse(request?.[1].body)
    expect(body.verifiedMobile).toBeUndefined()
    expect(body.allocations.map((line: { methodId: string; amountMinor: number }) => [line.methodId, line.amountMinor])).toEqual([['principal', 5_000], ['bonus', 1_000]])
  })

})
