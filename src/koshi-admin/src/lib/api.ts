/** RFC 7807 error returned by the KOSHI API. */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly errors: Record<string, string[]>

  constructor(status: number, code: string, message: string, errors: Record<string, string[]> = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.errors = errors
  }
}

type TokenListener = (token: string | null) => void

// The access token lives only in memory (not localStorage) to limit XSS exposure;
// the refresh token is an httpOnly SameSite=Strict cookie scoped to /api/auth.
let accessToken: string | null = null
let refreshing: Promise<boolean> | null = null
const listeners = new Set<TokenListener>()

export function setAccessToken(token: string | null): void {
  accessToken = token
  listeners.forEach((l) => l(token))
}

export function getAccessToken(): string | null {
  return accessToken
}

/** Downloads an authenticated file (e.g. CSV export) without exposing the token in a URL. */
export async function downloadFile(path: string, query: RequestOptions['query'], fallbackName: string): Promise<void> {
  const res = await apiRaw(path, { query })
  const disposition = res.headers.get('Content-Disposition') ?? ''
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? fallbackName
  const url = URL.createObjectURL(await res.blob())
  const a = document.createElement('a')
  a.href = url
  a.download = decodeURIComponent(name)
  a.click()
  URL.revokeObjectURL(url)
}

export function onAccessTokenChange(listener: TokenListener): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

const CLIENT_HEADER = { 'X-KOSHI-Client': 'admin' }

async function toError(res: Response): Promise<ApiError> {
  let body: { title?: string; code?: string; errors?: Record<string, string[]> } = {}
  try {
    body = await res.json()
  } catch {
    /* non-JSON error body */
  }
  return new ApiError(res.status, body.code ?? `http_${res.status}`, body.title ?? res.statusText ?? 'Request failed', body.errors ?? {})
}

export interface LoginResponse {
  accessToken: string
  accessTokenExpiresAt: string
  user: CurrentUser
  mfaRequired: boolean
}

export interface CurrentUser {
  id: string
  email: string
  displayName: string
  roles: string[]
  permissions: string[]
  departmentScopeId: string | null
}

export async function refreshSession(): Promise<LoginResponse | null> {
  const res = await fetch('/api/auth/refresh', { method: 'POST', credentials: 'same-origin', headers: CLIENT_HEADER })
  if (!res.ok) {
    setAccessToken(null)
    return null
  }
  const data = (await res.json()) as LoginResponse
  setAccessToken(data.accessToken)
  return data
}

async function tryRefresh(): Promise<boolean> {
  refreshing ??= refreshSession()
    .then((r) => r !== null)
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  query?: Record<string, string | number | boolean | null | undefined>
  signal?: AbortSignal
}

export function buildUrl(path: string, query?: RequestOptions['query']): string {
  const params = new URLSearchParams()
  Object.entries(query ?? {}).forEach(([k, v]) => {
    if (v !== undefined && v !== null && v !== '') params.set(k, String(v))
  })
  const qs = params.toString()
  return qs ? `${path}?${qs}` : path
}

export async function apiRaw(path: string, opts: RequestOptions = {}, retry = true): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`
  if (opts.body !== undefined) headers['Content-Type'] = 'application/json'

  const res = await fetch(buildUrl(path, opts.query), {
    method: opts.method ?? 'GET',
    headers,
    body: opts.body !== undefined ? JSON.stringify(opts.body) : undefined,
    credentials: 'same-origin',
    signal: opts.signal,
  })

  if (res.status === 401 && retry && !path.startsWith('/api/auth/')) {
    if (await tryRefresh()) return apiRaw(path, opts, false)
  }
  if (!res.ok) throw await toError(res)
  return res
}

export async function api<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const res = await apiRaw(path, opts)
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

export async function login(email: string, password: string): Promise<LoginResponse> {
  const res = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify({ email, password }),
    credentials: 'same-origin',
  })
  if (!res.ok) throw await toError(res)
  const data = (await res.json()) as LoginResponse
  setAccessToken(data.accessToken)
  return data
}

export async function logout(): Promise<void> {
  try {
    await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin', headers: CLIENT_HEADER })
  } finally {
    setAccessToken(null)
  }
}
