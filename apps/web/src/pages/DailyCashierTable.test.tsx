// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { DailyCashierTable } from './DailyCashierTable'
const request = vi.hoisted(() => vi.fn())
vi.mock('../api/client', () => ({ apiRequest: request }))
beforeAll(() => {
  Object.defineProperty(window, 'matchMedia', { writable: true, value: vi.fn(() => ({ matches: false,
    addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {}, dispatchEvent() {} })) })
  Object.defineProperty(globalThis, 'ResizeObserver', { value: class { observe() {} unobserve() {} disconnect() {} } })
})
afterEach(cleanup)
beforeEach(() => { request.mockReset() })
const report = { storeId: 'store-1', storeName: '测试门店', date: '2026-10-09', timeZoneId: 'Asia/Shanghai',
  generatedAtUtc: '2026-10-08T16:00:00Z',
  summary: { consumptionOrderCount: 1, consumptionMinor: 15000, consumptionRefundMinor: 1000, netRevenueMinor: 14000,
    topupCount: 1, topupMinor: 50000, topupRefundMinor: 20000, netTopupMinor: 30000,
    bonusMinor: 10000, revokedBonusMinor: 4000, pendingReconciliationMinor: 11000 },
  channels: [
    { code: 'CASH', name: '现金', consumptionMinor: 4000, consumptionRefundMinor: 1000,
      topupMinor: 50000, topupRefundMinor: 20000, pendingReconciliationMinor: 0 },
    { code: 'WECHAT', name: '微信', consumptionMinor: 11000, consumptionRefundMinor: 0,
      topupMinor: 0, topupRefundMinor: 0, pendingReconciliationMinor: 11000 },
  ],
}
function show(storeId = 'store-1') {
  return render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <DailyCashierTable storeId={storeId} />
  </QueryClientProvider>)
}
describe('daily cashier table', () => {
  it('separates consumption, topup principal, refunds and bonus, with authoritative totals', async () => {
    request.mockResolvedValue(report)
    show()
    const cash = (await screen.findByText('现金')).closest('tr')!
    expect(within(cash).getAllByRole('cell').map(cell => cell.textContent)).toEqual([
      '现金', '¥40.00', '¥10.00', '¥30.00', '¥500.00', '¥200.00', '¥300.00', '¥0.00',
    ])
    const total = screen.getByText('合计').closest('tr')!
    expect(within(total).getAllByRole('cell').map(cell => cell.textContent)).toEqual([
      '合计', '¥150.00', '¥10.00', '¥140.00', '¥500.00', '¥200.00', '¥300.00', '¥110.00',
    ])
    expect(screen.getByText('¥100.00')).toBeTruthy()
    expect(screen.getAllByText('¥40.00').length).toBe(2)
    expect(screen.getByText(/储值和赠金不计营业额/)).toBeTruthy()
    expect(request.mock.calls[0][0]).toBe('/api/v1/reports/daily-cashier?storeId=store-1')
  })
  it('requests a historical day and returns to the store-local today', async () => {
    request.mockResolvedValue(report)
    show()
    await screen.findByText('现金')
    fireEvent.change(screen.getByLabelText('营业日报日期'), { target: { value: '2026-10-07' } })
    await waitFor(() => expect(request.mock.calls.some(([path]) => path.endsWith('&date=2026-10-07'))).toBe(true))
    fireEvent.click(screen.getByRole('button', { name: /今\s*天/ }))
    await waitFor(() => expect(request.mock.calls.filter(([path]) => path === '/api/v1/reports/daily-cashier?storeId=store-1').length).toBe(2))
    expect(screen.getByLabelText('营业日报日期').getAttribute('value')).toBe(report.date)
  })
  it('shows errors instead of presenting a failed request as zero revenue', async () => {
    request.mockRejectedValue(new Error('Forbidden'))
    show()
    expect(await screen.findByText('营业日报加载失败')).toBeTruthy()
    expect(screen.queryByText('合计')).toBeNull()
    expect(screen.queryByText('¥0.00')).toBeNull()
  })
  it('preserves negative net amounts for refund-only days', async () => {
    request.mockResolvedValue({ ...report,
      summary: { ...report.summary, consumptionOrderCount: 0, consumptionMinor: 0, netRevenueMinor: -1000,
        topupCount: 0, topupMinor: 0, netTopupMinor: -20000, bonusMinor: 0, pendingReconciliationMinor: 0 },
      channels: [{ ...report.channels[0], consumptionMinor: 0, topupMinor: 0 }],
    })
    show()
    await screen.findByText('现金')
    expect(screen.getAllByText('¥-10.00')).toHaveLength(2)
    expect(screen.getAllByText('¥-200.00')).toHaveLength(2)
    expect(screen.getByText('0 单')).toBeTruthy()
  })
})
