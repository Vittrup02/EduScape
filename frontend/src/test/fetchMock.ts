import { vi } from 'vitest'

type Handler = (init: RequestInit | undefined) => Response | Promise<Response>

export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

export function tokens(accessToken: string, refreshToken = `refresh-for-${accessToken}`): Response {
  return json({ tokenType: 'Bearer', accessToken, expiresIn: 1800, refreshToken })
}

export function urlOf(input: RequestInfo | URL): string {
  if (input instanceof Request) {
    return input.url
  }
  return input instanceof URL ? input.href : input
}

/** Stubs fetch with one handler per API path (e.g. "/api/auth/login"); unknown paths answer 404. */
export function mockFetch(handlers: Record<string, Handler>) {
  const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const path = new URL(urlOf(input)).pathname
    return Promise.resolve(Object.hasOwn(handlers, path) ? handlers[path](init) : json({ code: 'NOT_FOUND' }, 404))
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** The Authorization header a mocked fetch call was made with. */
export function authorizationOf(init: RequestInit | undefined): string | null {
  return new Headers(init?.headers).get('Authorization')
}
