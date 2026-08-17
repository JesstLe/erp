import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiRequest, resetCsrfToken } from './client'

describe('apiRequest', () => {
  beforeEach(() => resetCsrfToken())
  afterEach(() => vi.unstubAllGlobals())

  it('obtains a CSRF token and sends it with unsafe requests', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ token: 'csrf-token' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await apiRequest<void>('/api/v1/auth/logout', { method: 'POST' })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/security/csrf')
    const request = fetchMock.mock.calls[1]
    const requestInit = request[1] as RequestInit
    expect(request[0]).toBe('/api/v1/auth/logout')
    expect((requestInit.headers as Headers).get('X-CSRF-TOKEN')).toBe('csrf-token')
    expect(requestInit.credentials).toBe('include')
  })

  it('surfaces structured API errors without exposing response internals', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      error: { code: 'AUTHENTICATION_FAILED', message: '账号或密码错误' },
      traceId: 'trace-1',
    }), { status: 401, headers: { 'Content-Type': 'application/json' } })))

    const error = await apiRequest('/api/v1/auth/me').catch((reason: unknown) => reason)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 401, code: 'AUTHENTICATION_FAILED', traceId: 'trace-1' })
    expect((error as Error).message).toBe('账号或密码错误')
  })
})
