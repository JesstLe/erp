// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { ModernFacilityCashierWorkbench } from './ModernFacilityCashierWorkbench'
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
      if (path === '/api/v1/customers/cashier-search') return Promise.resolve({ items: [{ id: 'customer-1', displayName: '王女士', mobile: '13615345138', status: 'Active', homeStoreId: 'store-1', homeStoreName: '测试门店', activeCardCount: 1, birthDate: '1990-05-06', residence: '水木清华小区', principalBalanceMinor: 12_000, bonusBalanceMinor: 3_000, createdAtUtc: '2026-01-01T00:00:00Z' }], total: 1, page: 1, pageSize: 30 })
      if (path.startsWith('/api/v1/customers/customer-1?')) return Promise.resolve({ id: 'customer-1', displayName: '王女士', maskedMobile: '13615345138', gender: 'Unknown', status: 'Active', homeStoreId: 'store-1', homeStoreName: '测试门店', version: 1, cards: [{ id: 'card-1', cardTypeId: 'card-type-1', cardTypeName: '金卡', maskedCardNo: 'CARD-001', status: 'Active', validFrom: '2026-01-01', serviceDiscountBasisPoints: 8_305, productDiscountBasisPoints: 9_000, accounts: [{ id: 'account-1', accountType: 'Principal', balanceUnits: 12_000, status: 'Active' }, { id: 'account-2', accountType: 'Bonus', balanceUnits: 3_000, status: 'Active' }] }], mergedAliases: [] })
      return baseImplementation?.(path, options)
    })

    render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><ModernFacilityCashierWorkbench facility={facility} availableFacilities={[]} onFacilityChanged={vi.fn()} onExit={vi.fn()} onCompleted={vi.fn()} /></QueryClientProvider></MemoryRouter>)

    fireEvent.click(await screen.findByRole('button', { name: /基础服务/ }))
    fireEvent.click(screen.getByRole('button', { name: /会员.*刷卡/s }))
    expect(screen.getByText('请输入姓名、完整手机号或卡号后查询会员')).toBeTruthy()
    expect(apiRequestMock.mock.calls.some(([path]) => path === '/api/v1/customers/cashier-search')).toBe(false)
    fireEvent.change(screen.getByPlaceholderText('输入姓名、完整手机号或卡号自动查询'), { target: { value: '13615345138' } })
    fireEvent.click(await screen.findByRole('button', { name: /王女士.*13615345138/s }))
    expect(await screen.findByText('1990-05-06')).toBeTruthy()
    expect(screen.getByText('水木清华小区')).toBeTruthy()
    expect(screen.getByText('储值本金')).toBeTruthy()
    expect(screen.getByText('¥120.00')).toBeTruthy()
    expect(screen.getByText('赠送金额')).toBeTruthy()
    expect(screen.getByText('¥30.00')).toBeTruthy()
    expect(await screen.findByText('CARD-001')).toBeTruthy()
    expect(await screen.findByText('金卡')).toBeTruthy()
    expect(screen.getByRole('button', { name: /储值/ })).toBeTruthy()
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
    apiRequestMock.mockImplementation((path: string) => {
      if (path === '/api/v1/catalog/price-books') return Promise.resolve([])
      if (path === '/api/v1/catalog/service-items') return Promise.resolve([])
      if (path === '/api/v1/catalog/products') return Promise.resolve([])
      if (path.startsWith('/api/v1/inventory/balances')) return Promise.resolve([])
      if (path.startsWith('/api/v1/cashier/service-employees')) return Promise.resolve([])
      if (path.startsWith('/api/v1/payments/methods')) return Promise.resolve([
        { id: 'cash', code: 'CASH', name: '现金', category: 'Cash', isEnabled: true },
        { id: 'group-buy', code: 'GROUP_BUY_MANUAL', name: '团购平台核销', category: 'ManualExternal', isEnabled: true },
      ])
      if (path === '/api/v1/customers/cashier-search') return Promise.resolve({ items: [{ id: 'customer-1', displayName: '王女士', mobile: '13615345138', status: 'Active', homeStoreId: 'store-1', homeStoreName: '测试门店', activeCardCount: 1, principalBalanceMinor: 20_000, bonusBalanceMinor: 0, createdAtUtc: '2026-01-01T00:00:00Z' }], total: 1, page: 1, pageSize: 30 })
      if (path.startsWith('/api/v1/customers/customer-1?')) return Promise.resolve({ id: 'customer-1', displayName: '王女士', maskedMobile: '136****5138', gender: 'Unknown', status: 'Active', homeStoreId: 'store-1', homeStoreName: '测试门店', version: 1, cards: [{ id: 'card-1', cardTypeName: '储值卡', maskedCardNo: 'CARD-001', status: 'Active', validFrom: '2026-01-01', accounts: [{ id: 'account-1', accountType: 'Principal', balanceUnits: 20_000, status: 'Active' }] }], mergedAliases: [] })
      if (path === '/api/v1/cashier/visits/visit-1/draft') return Promise.resolve({
        id: 'order-1', orderNo: 'SO-001', visitId: 'visit-1', status: 'Draft', version: 1,
        customerId: 'customer-1',
        referenceTotalMinor: 5_000, receivableMinor: 5_000,
        lines: [{ id: 'line-1', lineType: 'Product', productItemId: 'product-1', itemCode: 'P001', itemName: '护理用品', unitName: '件', quantity: 1, referencePriceMinor: 5_000, enteredPriceMinor: 5_000, lineAmountMinor: 5_000 }],
      })
      return Promise.reject(new Error(`unexpected request ${path}`))
    })
    const runningFacility = { ...facility, status: 'IN_USE', sessionId: 'session-1', visitId: 'visit-1', visitNo: 'V001', startedAtUtc: '2026-01-01T00:00:00Z' }
    render(<MemoryRouter><QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><ModernFacilityCashierWorkbench facility={runningFacility} availableFacilities={[]} onFacilityChanged={vi.fn()} onExit={vi.fn()} onCompleted={vi.fn()} /></QueryClientProvider></MemoryRouter>)

    await screen.findByText(/1\. 护理用品/)
    await screen.findByText('会员：王女士')
    expect(apiRequestMock.mock.calls.some(([path]) => path === '/api/v1/customers/cashier-search')).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: '结算' }))
    expect(await screen.findByText('收银结算')).toBeTruthy()
    expect(screen.getByText('CARD-001 · 储值卡')).toBeTruthy()
    expect(screen.getByText('已沿用主单会员：王女士')).toBeTruthy()
    expect(screen.queryByText(/显示团购|更多支付/)).toBeNull()
    expect(screen.getByText('团购支付')).toBeTruthy()
    expect(screen.getByText('美团')).toBeTruthy()
  })

})
