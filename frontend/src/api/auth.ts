import { apiBaseUrl, ApiError, toApiError } from './client'
import { authStorageKey, loadAuth, parseAuth, readRawAuth, saveAuth, type StoredAuth } from './tokenStorage'

/**
 * Teacher authentication with bearer tokens. The frontend and the API are on different sites, where a cookie
 * would be a third-party cookie that Safari blocks, so the access token is sent in the Authorization header.
 * Access tokens are short-lived; on a 401 the refresh token is used once to get new ones.
 */

export interface Teacher {
  email: string
}

const jsonHeaders = { 'Content-Type': 'application/json' }
const listeners = new Set<() => void>()

let lastRaw: string | null = null
let lastTeacher: Teacher | null = null
let refreshing: Promise<RefreshResult> | null = null

/** The signed-in teacher, or null. Returns the same object until the stored tokens change (for useSyncExternalStore). */
export function currentTeacher(): Teacher | null {
  const raw = readRawAuth()
  if (raw !== lastRaw) {
    lastRaw = raw
    const auth = parseAuth(raw)
    lastTeacher = auth ? { email: auth.email } : null
  }
  return lastTeacher
}

/** Notifies when the teacher signs in or out, also in other tabs. Returns an unsubscribe function. */
export function onAuthChange(listener: () => void): () => void {
  const onStorage = (event: StorageEvent) => {
    if (event.key === authStorageKey || event.key === null) {
      listener()
    }
  }
  listeners.add(listener)
  window.addEventListener('storage', onStorage)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

function setAuth(auth: StoredAuth | null): void {
  saveAuth(auth)
  for (const listener of listeners) {
    listener()
  }
}

async function readTokens(response: Response): Promise<Pick<StoredAuth, 'accessToken' | 'refreshToken'>> {
  const body: unknown = await response.json()
  if (
    typeof body === 'object' &&
    body !== null &&
    'accessToken' in body &&
    typeof body.accessToken === 'string' &&
    'refreshToken' in body &&
    typeof body.refreshToken === 'string'
  ) {
    return { accessToken: body.accessToken, refreshToken: body.refreshToken }
  }
  throw new ApiError('UNKNOWN', response.status)
}

async function signIn(path: string, email: string, password: string): Promise<void> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ email, password }),
  })
  if (!response.ok) {
    throw await toApiError(response)
  }
  setAuth({ email: email.trim(), ...(await readTokens(response)) })
}

/** Creates a teacher account and signs in. Throws an ApiError, e.g. EMAIL_TAKEN or PASSWORD_TOO_SHORT. */
export function register(email: string, password: string): Promise<void> {
  return signIn('/api/auth/register', email, password)
}

/** Throws an ApiError, e.g. INVALID_CREDENTIALS or LOGIN_LOCKED_OUT. */
export function login(email: string, password: string): Promise<void> {
  return signIn('/api/auth/login', email, password)
}

/** Revokes the refresh tokens on the server and always signs out locally, even if the server can't be reached. */
export async function logout(): Promise<void> {
  try {
    await teacherFetch('/api/auth/logout', { method: 'POST' })
  } catch {
    // Signed out locally regardless.
  } finally {
    setAuth(null)
  }
}

/** "unavailable": the server could not answer (e.g. it is waking up), so the teacher stays signed in. */
type RefreshResult = 'refreshed' | 'rejected' | 'unavailable'

/** Gets new tokens. Concurrent callers share one request. Signs out only if the server rejects the refresh token. */
function refreshTokens(): Promise<RefreshResult> {
  refreshing ??= (async (): Promise<RefreshResult> => {
    const auth = loadAuth()
    if (!auth) {
      return 'rejected'
    }
    try {
      const response = await fetch(`${apiBaseUrl}/api/auth/refresh`, {
        method: 'POST',
        headers: jsonHeaders,
        body: JSON.stringify({ refreshToken: auth.refreshToken }),
      })
      if (response.status === 401) {
        setAuth(null)
        return 'rejected'
      }
      if (!response.ok) {
        return 'unavailable'
      }
      setAuth({ ...auth, ...(await readTokens(response)) })
      return 'refreshed'
    } catch {
      return 'unavailable'
    }
  })().finally(() => {
    refreshing = null
  })
  return refreshing
}

/**
 * Calls an endpoint that requires a signed-in teacher. Refreshes the tokens once on 401; the teacher is signed out
 * only when the server rejects them, never because it could not be reached. Throws an ApiError for error responses.
 */
export async function teacherFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const send = () => {
    const headers = new Headers(init.headers)
    const auth = loadAuth()
    if (auth) {
      headers.set('Authorization', `Bearer ${auth.accessToken}`)
    }
    return fetch(`${apiBaseUrl}${path}`, { ...init, headers })
  }

  let response = await send()
  if (response.status === 401 && (await refreshTokens()) === 'refreshed') {
    response = await send()
    if (response.status === 401) {
      setAuth(null)
    }
  }
  if (!response.ok) {
    throw await toApiError(response)
  }
  return response
}
