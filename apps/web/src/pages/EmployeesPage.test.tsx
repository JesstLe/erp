// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { EmployeesPage } from './EmployeesPage'

const apiRequestMock = vi.hoisted(() => vi.fn())
vi.mock('../api/client', () => ({
  apiRequest: apiRequestMock,
  ApiError: class ApiError extends Error {},
}))
vi.mock('../auth/useAuth', () => ({
  useAuth: () => ({
    user: { id: 'owner-user', roles: ['OWNER'], stores: [{ id: 'store-1', code: 'S001', name: '测试门店' }] },
    store: { id: 'store-1', code: 'S001', name: '测试门店' },
  }),
}))

const employee = {
  id: 'employee-1', employeeNo: 'E001', displayName: '王技师', positionCode: 'TECHNICIAN',
  status: 'Active', userId: 'employee-user', account: 'wang01', accountEnabled: true,
  mustChangePassword: false, roles: ['TECHNICIAN'],
  stores: [{ id: 'store-1', code: 'S001', name: '测试门店', isPrimary: true }],
  createdAtUtc: '2026-08-18T08:00:00Z', version: 1,
}

beforeAll(() => {
  Object.defineProperty(window, 'matchMedia', { writable: true, value: vi.fn().mockImplementation(() => ({
    matches: false, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(),
    removeEventListener: vi.fn(), dispatchEvent: vi.fn(),
  })) })
})
afterEach(cleanup)

describe('EmployeesPage regression', () => {
  beforeEach(() => {
    apiRequestMock.mockReset().mockImplementation((path: string) => {
      if (path === '/api/v1/employees/roles') return Promise.resolve([{ id: 'role-1', code: 'TECHNICIAN', name: '服务员工' }])
      if (path === '/api/v1/employees/positions') return Promise.resolve([{ id: 'position-1', code: 'TECHNICIAN', name: '顾问', sortOrder: 10, status: 'ENABLED', version: 1 }])
      if (path.startsWith('/api/v1/employees?')) return Promise.resolve({ items: [employee], total: 1, page: 1, pageSize: 20 })
      return Promise.reject(new Error(`unexpected request ${path}`))
    })
  })

  it('offers a keyboard reachable view action and opens employee details', async () => {
    render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><EmployeesPage /></QueryClientProvider>)
    expect(await screen.findByText('王技师')).toBeTruthy()
    const view = screen.getByRole('button', { name: /查\s*看/ })
    view.focus()
    fireEvent.keyDown(view, { key: 'Enter' })
    fireEvent.click(view)
    expect(await screen.findByText('员工与账号详情')).toBeTruthy()
    expect(screen.getAllByText('服务员工').length).toBeGreaterThan(0)
  })

  it('automatically requests the typed keyword without a search submit', async () => {
    render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}><EmployeesPage /></QueryClientProvider>)
    const search = await screen.findByLabelText('实时查询员工')
    fireEvent.change(search, { target: { value: '王技师' } })
    await waitFor(() => expect(apiRequestMock).toHaveBeenCalledWith(
      expect.stringContaining('query=%E7%8E%8B%E6%8A%80%E5%B8%88'), expect.anything(),
    ), { timeout: 1_500 })
  })
})
