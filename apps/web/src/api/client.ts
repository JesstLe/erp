export interface ApiErrorPayload {
  error?: { code?: string; message?: string }
  title?: string
  detail?: string
  traceId?: string
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId?: string

  constructor(status: number, payload: ApiErrorPayload) {
    super(payload.error?.message ?? payload.detail ?? payload.title ?? '请求失败')
    this.name = 'ApiError'
    this.status = status
    this.code = payload.error?.code ?? 'REQUEST_FAILED'
    this.traceId = payload.traceId
  }
}

let csrfToken: string | undefined

export function resetCsrfToken(): void { csrfToken = undefined }

async function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken
  const response = await fetch('/api/v1/security/csrf', { credentials: 'include' })
  if (!response.ok) throw new ApiError(response.status, await safeJson(response))
  const body = (await response.json()) as { token: string }
  csrfToken = body.token
  return body.token
}

async function safeJson(response: Response): Promise<ApiErrorPayload> {
  try { return (await response.json()) as ApiErrorPayload }
  catch { return { title: '请求失败' } }
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const method = (init.method ?? 'GET').toUpperCase()
  const unsafe = ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  if (unsafe) headers.set('X-CSRF-TOKEN', await getCsrfToken())
  const response = await fetch(path, { ...init, headers, credentials: 'include' })
  if (!response.ok) {
    const payload = await safeJson(response)
    if (unsafe && payload.error?.code === 'INVALID_ANTIFORGERY_TOKEN') {
      csrfToken = undefined
      headers.set('X-CSRF-TOKEN', await getCsrfToken())
      const retried = await fetch(path, { ...init, headers, credentials: 'include' })
      if (retried.ok) return retried.status === 204 ? undefined as T : await retried.json() as T
      throw new ApiError(retried.status, await safeJson(retried))
    }
    throw new ApiError(response.status, payload)
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export async function apiDownload(path: string, init: RequestInit = {}): Promise<{ blob: Blob; filename: string }> {
  const method = (init.method ?? 'GET').toUpperCase()
  const unsafe = ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method)
  const headers = new Headers(init.headers)
  headers.set('Accept', 'text/csv, application/octet-stream')
  if (init.body && !(init.body instanceof FormData)) headers.set('Content-Type', 'application/json')
  if (unsafe) headers.set('X-CSRF-TOKEN', await getCsrfToken())
  const response = await fetch(path, { ...init, headers, credentials: 'include' })
  if (!response.ok) {
    const payload = await safeJson(response)
    if (unsafe && payload.error?.code === 'INVALID_ANTIFORGERY_TOKEN') {
      csrfToken = undefined
      headers.set('X-CSRF-TOKEN', await getCsrfToken())
      const retried = await fetch(path, { ...init, headers, credentials: 'include' })
      if (!retried.ok) throw new ApiError(retried.status, await safeJson(retried))
      const disposition = retried.headers.get('Content-Disposition') ?? ''
      const encoded = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
      const quoted = disposition.match(/filename="([^"]+)"/i)?.[1]
      const raw = encoded ? decodeURIComponent(encoded) : quoted ?? 'download.csv'
      return { blob: await retried.blob(), filename: raw.split(/[\\/]/).pop() || 'download.csv' }
    }
    throw new ApiError(response.status, payload)
  }
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const encoded = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
  const quoted = disposition.match(/filename="([^"]+)"/i)?.[1]
  const raw = encoded ? decodeURIComponent(encoded) : quoted ?? 'download.csv'
  const filename = raw.split(/[\\/]/).pop() || 'download.csv'
  return { blob: await response.blob(), filename }
}
