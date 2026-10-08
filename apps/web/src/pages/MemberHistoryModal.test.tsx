// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemberHistoryModal } from './MemberHistoryModal'
const request = vi.hoisted(() => vi.fn())
vi.mock('../api/client', () => ({ apiRequest: request }))
beforeAll(() => {
  Object.defineProperty(window, 'matchMedia', { writable: true, value: vi.fn(() => ({ matches: false,
    addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {}, dispatchEvent() {} })) })
  Object.defineProperty(globalThis, 'ResizeObserver', { value: class { observe() {} unobserve() {} disconnect() {} } })
})
afterEach(cleanup)
beforeEach(() => { request.mockReset() })
function show(initialTab: 'topups' | 'orders', canReadTopups = true) {
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <MemberHistoryModal storeId="store-1" customerId="customer-1" customerName="王女士" initialTab={initialTab}
      canReadTopups={canReadTopups} canReadOrders onClose={vi.fn()} />
  </QueryClientProvider>)
}
const order = { id: 'order-1', orderNo: 'SO-001', createdAtUtc: '2026-10-08T00:00:00Z',
  status: 'Settled', receivableMinor: 8800, refundedMinor: 0,
  lines: [{ id: 'line-1', itemName: '护理服务', quantity: 1, enteredPriceMinor: 8800,
    lineAmountMinor: 8800, employeeName: '李店员', returnedQuantity: 0 }] }
describe('member history', () => {
  it('loads the selected history only and expands consumption details', async () => {
    request.mockImplementation((path: string) => Promise.resolve(path.startsWith('/api/v1/member-topups') ? {
      items: [{ id: 'topup-1', topupNo: 'TU-001', status: 'PartiallyRefunded', paidAtUtc: '2026-10-08T00:00:00Z',
        principalMinor: 10000, bonusMinor: 2000, refundedPrincipalMinor: 4000, revokedBonusMinor: 800,
        remainingPrincipalMinor: 6000, paymentNo: 'PAY-001', allocations: [] }], total: 1,
    } : { items: [order], total: 1 }))
    show('topups')
    expect(await screen.findByText('TU-001')).toBeTruthy()
    expect(request).toHaveBeenCalledTimes(1)
    expect(request.mock.calls[0][0]).toBe('/api/v1/member-topups?storeId=store-1&customerId=customer-1&page=1&pageSize=10')
    fireEvent.click(screen.getByRole('tab', { name: '消费记录' }))
    expect(await screen.findByText('SO-001')).toBeTruthy()
    expect(request.mock.calls[1][0]).toBe('/api/v1/cashier/orders?storeId=store-1&customerId=customer-1&page=1&pageSize=10')
    fireEvent.click(screen.getByText('SO-001').closest('tr')!.querySelector('button')!)
    expect(await screen.findByText('护理服务')).toBeTruthy()
    expect(screen.getByText('李店员')).toBeTruthy()
  })
  it('keeps customer filters when requesting the next page', async () => {
    request.mockResolvedValue({ items: [order], total: 11 })
    show('orders')
    expect(await screen.findByText('SO-001')).toBeTruthy()
    fireEvent.click(document.querySelector('.ant-pagination-next button')!)
    await waitFor(() => expect(request.mock.calls.some(([path]) => path === '/api/v1/cashier/orders?storeId=store-1&customerId=customer-1&page=2&pageSize=10')).toBe(true))
  })
  it('does not request a history without permission', async () => {
    request.mockResolvedValue({ items: [], total: 0 })
    show('topups', false)
    expect(request).not.toHaveBeenCalled()
    expect(screen.getByRole('tab', { name: '储值记录' }).getAttribute('aria-disabled')).toBe('true')
    fireEvent.click(screen.getByRole('tab', { name: '消费记录' }))
    await waitFor(() => expect(request).toHaveBeenCalledTimes(1))
    expect(request.mock.calls[0][0]).toContain('/api/v1/cashier/orders?storeId=store-1&customerId=customer-1')
  })
})
