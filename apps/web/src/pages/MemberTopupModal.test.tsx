// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemberTopupModal } from './MemberTopupModal'

const apiRequestMock = vi.hoisted(() => vi.fn())
vi.mock('../api/client', () => ({
  apiRequest: apiRequestMock,
  ApiError: class ApiError extends Error { code = 'REQUEST_FAILED' },
}))

beforeAll(() => {
  Object.defineProperty(window, 'matchMedia', { writable: true, value: vi.fn().mockImplementation(() => ({
    matches: false, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(),
    removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
  })) })
  Object.defineProperty(globalThis, 'ResizeObserver', { value: class ResizeObserver { observe() {} unobserve() {} disconnect() {} } })
  Object.defineProperty(globalThis.crypto, 'randomUUID', { configurable: true, value: () => '00000000-0000-4000-8000-000000000001' })
})

afterEach(cleanup)

describe('MemberTopupModal', () => {
  beforeEach(() => apiRequestMock.mockReset())

  it('submits principal, bonus and an exactly balanced payment allocation', async () => {
    apiRequestMock.mockResolvedValue({
      id: 'topup-1', principalMinor: 20_000, bonusMinor: 2_000,
    })
    const onSuccess = vi.fn()
    render(<QueryClientProvider client={new QueryClient()}><MemberTopupModal
      open storeId="store-1" customerId="customer-1" customerName="王女士"
      cards={[{ id: 'card-1', cardTypeId: 'card-type-1', cardTypeName: '金卡', maskedCardNo: 'CARD-001', status: 'Active',
        validFrom: '2026-01-01', serviceDiscountBasisPoints: 10_000, productDiscountBasisPoints: 10_000,
        accounts: [
          { id: 'principal-1', accountType: 'Principal', balanceUnits: 12_000, status: 'Active' },
          { id: 'bonus-1', accountType: 'Bonus', balanceUnits: 3_000, status: 'Active' },
        ] }]}
      methods={[{ id: 'cash-1', code: 'CASH', name: '现金', category: 'Cash', requiresOpenShift: true }]}
      shiftOpen canGrantBonus onClose={vi.fn()} onSuccess={onSuccess}
    /></QueryClientProvider>)

    expect(await screen.findByText('会员储值 · 王女士')).toBeTruthy()
    expect(screen.getByRole('combobox', { name: '存入会员卡' })).toBeTruthy()
    const principal = screen.getByRole('spinbutton', { name: '储值本金（元）' })
    const bonus = screen.getByRole('spinbutton', { name: '赠送金额（元）' })
    const paymentAmount = await screen.findByRole('spinbutton', { name: '实收金额（元）' })
    await waitFor(() => expect(Number((principal as HTMLInputElement).value)).toBe(100))
    await waitFor(() => expect(Number((paymentAmount as HTMLInputElement).value)).toBe(100))
    fireEvent.change(principal, { target: { value: '200' } })
    fireEvent.change(bonus, { target: { value: '20' } })
    fireEvent.change(paymentAmount, { target: { value: '200' } })
    await waitFor(() => expect(Number((principal as HTMLInputElement).value)).toBe(200))
    await waitFor(() => expect(Number((bonus as HTMLInputElement).value)).toBe(20))
    await waitFor(() => expect(Number((paymentAmount as HTMLInputElement).value)).toBe(200))
    fireEvent.click(screen.getByRole('button', { name: '确认收款并入账' }))

    await waitFor(() => expect(apiRequestMock).toHaveBeenCalledTimes(1))
    const [path, options] = apiRequestMock.mock.calls[0]
    expect(path).toBe('/api/v1/member-topups')
    expect(JSON.parse(options.body)).toMatchObject({
      storeId: 'store-1', customerId: 'customer-1', cardId: 'card-1',
      principalMinor: 20_000, bonusMinor: 2_000,
      allocations: [{ methodId: 'cash-1', amountMinor: 20_000 }],
    })
    await waitFor(() => expect(onSuccess).toHaveBeenCalledTimes(1))
  })
})
