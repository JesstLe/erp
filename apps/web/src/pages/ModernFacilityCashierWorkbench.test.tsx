// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { ModernFacilityCashierWorkbench } from './ModernFacilityCashierWorkbench'
import { buildManualPaymentReference } from './modernFacilityCashierPayments'

const apiRequestMock = vi.hoisted(() => vi.fn())
vi.mock('../api/client', () => ({
  apiRequest: apiRequestMock,
  ApiError: class ApiError extends Error { code = 'REQUEST_FAILED' },
}))
vi.mock('../auth/useAuth', () => ({
  useAuth: () => ({ store: { id: 'store-1', code: 'S001', name: '测试门店' } }),
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
  if (!globalThis.crypto.randomUUID) Object.defineProperty(globalThis.crypto, 'randomUUID', { value: () => '00000000-0000-4000-8000-000000000001' })
})
afterEach(cleanup)

describe('ModernFacilityCashierWorkbench before timing starts', () => {
  beforeEach(() => {
    apiRequestMock.mockReset().mockImplementation((path: string) => {
      if (path === '/api/v1/catalog/price-books') return Promise.resolve([{ id: 'book-1', name: '当前价目', status: 'PUBLISHED', effectiveFrom: '2026-01-01', version: 1, lines: [{ serviceItemId: 'service-1', serviceItemName: '基础服务', unitPriceMinor: 10_000 }], productLines: [{ productItemId: 'product-1', productItemName: '护理用品', unitName: '件', unitPriceMinor: 5_000 }] }])
      if (path === '/api/v1/catalog/service-items') return Promise.resolve([{ id: 'service-1', code: 'S001', name: '基础服务', standardDurationMinutes: 30, status: 'ENABLED', version: 1 }])
      if (path === '/api/v1/catalog/products') return Promise.resolve([{ id: 'product-1', code: 'P001', name: '护理用品', unitName: '件', trackInventory: true, status: 'ENABLED', version: 1 }])
      if (path.startsWith('/api/v1/inventory/balances')) return Promise.resolve([{ productItemId: 'product-1', availableQuantity: 8 }])
      if (path.startsWith('/api/v1/cashier/service-employees')) return Promise.resolve([{ id: 'employee-1', employeeNo: 'E001', displayName: '李店员', positionCode: 'STAFF', positionName: '员工' }])
      if (path.startsWith('/api/v1/payments/methods')) return Promise.resolve([])
      if (path === '/api/v1/customers/search') return Promise.resolve({ items: [], total: 0, page: 1, pageSize: 30 })
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
    expect(apiRequestMock).not.toHaveBeenCalledWith('/api/v1/facilities/sessions/start', expect.anything())

    fireEvent.click(screen.getByRole('button', { name: /产品.*列表/s }))
    fireEvent.click(await screen.findByRole('button', { name: /护理用品/ }))
    expect(await screen.findByText('添加人（可选）')).toBeTruthy()
    expect(screen.getByText(/用于记录是谁将该产品加入本次消费/)).toBeTruthy()
  })

  it('keeps an auditable external reference for manual and group-buy settlement', () => {
    expect(buildManualPaymentReference('BANK_CARD_MANUAL', '  BANK-2026-0001  ')).toBe('BANK-2026-0001')
    expect(buildManualPaymentReference('GROUP_BUY_MANUAL', ' DY-889900 ', '抖音')).toBe('抖音:DY-889900')
  })
})
