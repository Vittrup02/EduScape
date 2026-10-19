import { afterEach, describe, expect, it, vi } from 'vitest'
import { authorizationOf, json, mockFetch, tokens, urlOf } from '../test/fetchMock'
import { currentTeacher, login, logout, register, teacherFetch } from './auth'
import { ApiError } from './client'
import { loadAuth, saveAuth } from './tokenStorage'

function signInAs(accessToken: string) {
  saveAuth({ email: 'ida@skole.dk', accessToken, refreshToken: `refresh-for-${accessToken}` })
}

describe('teacher auth', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('stores the tokens on login and exposes the teacher', async () => {
    mockFetch({ '/api/auth/login': () => tokens('access-1') })

    await login(' ida@skole.dk ', 'en lang adgangskode')

    expect(currentTeacher()).toEqual({ email: 'ida@skole.dk' })
    expect(loadAuth()?.accessToken).toBe('access-1')
  })

  it('throws the error code when login fails and stays signed out', async () => {
    mockFetch({ '/api/auth/login': () => json({ code: 'INVALID_CREDENTIALS' }, 401) })

    const error = await login('ida@skole.dk', 'forkert').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).code).toBe('INVALID_CREDENTIALS')
    expect(currentTeacher()).toBeNull()
  })

  it('signs in straight after registering', async () => {
    mockFetch({ '/api/auth/register': () => tokens('access-new') })

    await register('ny@skole.dk', 'en lang adgangskode')

    expect(currentTeacher()).toEqual({ email: 'ny@skole.dk' })
  })

  it('sends the access token to teacher endpoints', async () => {
    signInAs('access-1')
    const fetchMock = mockFetch({ '/api/teacher/rooms': () => json([]) })

    await teacherFetch('/api/teacher/rooms')

    expect(authorizationOf(fetchMock.mock.calls[0]?.[1])).toBe('Bearer access-1')
  })

  it('refreshes an expired access token once and retries', async () => {
    signInAs('expired')
    const fetchMock = mockFetch({
      '/api/teacher/rooms': (init) =>
        authorizationOf(init) === 'Bearer fresh' ? json([]) : new Response(null, { status: 401 }),
      '/api/auth/refresh': () => tokens('fresh'),
    })

    const response = await teacherFetch('/api/teacher/rooms')

    expect(response.ok).toBe(true)
    expect(loadAuth()?.accessToken).toBe('fresh')
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('shares one refresh between concurrent requests', async () => {
    signInAs('expired')
    const fetchMock = mockFetch({
      '/api/teacher/rooms': (init) =>
        authorizationOf(init) === 'Bearer fresh' ? json([]) : new Response(null, { status: 401 }),
      '/api/auth/refresh': () => tokens('fresh'),
    })

    await Promise.all([teacherFetch('/api/teacher/rooms'), teacherFetch('/api/teacher/rooms')])

    const refreshCalls = fetchMock.mock.calls.filter(([input]) => urlOf(input).endsWith('/api/auth/refresh'))
    expect(refreshCalls).toHaveLength(1)
  })

  it('signs out when the refresh token is rejected', async () => {
    signInAs('expired')
    mockFetch({
      '/api/teacher/rooms': () => new Response(null, { status: 401 }),
      '/api/auth/refresh': () => json({ code: 'UNAUTHORIZED' }, 401),
    })

    const error = await teacherFetch('/api/teacher/rooms').catch((e: unknown) => e)

    expect((error as ApiError).status).toBe(401)
    expect(currentTeacher()).toBeNull()
  })

  it('keeps the tokens when the server cannot be reached during refresh', async () => {
    signInAs('expired')
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) =>
        urlOf(input).endsWith('/api/auth/refresh')
          ? Promise.reject(new TypeError('Failed to fetch'))
          : Promise.resolve(new Response(null, { status: 401 })),
      ),
    )

    await teacherFetch('/api/teacher/rooms').catch(() => undefined)

    // A sleeping backend must not sign the teacher out; only a rejected refresh token does.
    expect(loadAuth()?.accessToken).toBe('expired')
  })

  it('signs out locally even when logout cannot reach the server', async () => {
    signInAs('access-1')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    await logout()

    expect(currentTeacher()).toBeNull()
    expect(loadAuth()).toBeNull()
  })
})
