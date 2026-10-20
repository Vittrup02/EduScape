import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, backendFetch, backendTiming, isBackendWaking, onBackendWakingChange, toApiError } from './client'

describe('toApiError', () => {
  it('reads the error code from the response body', async () => {
    const response = new Response(JSON.stringify({ code: 'SESSION_CLOSED' }), { status: 400 })

    const error = await toApiError(response)

    expect(error.code).toBe('SESSION_CLOSED')
    expect(error.status).toBe(400)
  })

  it.each([
    ['a non-JSON body', '<html>Bad gateway</html>'],
    ['JSON without a code', JSON.stringify({ title: 'Bad Request' })],
    ['an empty body', ''],
  ])('uses UNKNOWN for %s', async (_, body) => {
    const error = await toApiError(new Response(body, { status: 502 }))

    expect(error.code).toBe('UNKNOWN')
    expect(error.status).toBe(502)
  })
})

describe('backendFetch', () => {
  const unsubscribers: (() => void)[] = []

  afterEach(() => {
    vi.unstubAllGlobals()
    unsubscribers.splice(0).forEach((unsubscribe) => {
      unsubscribe()
    })
  })

  /** Records every change of isBackendWaking(), e.g. [true, false]. */
  function recordWaking(): boolean[] {
    const changes: boolean[] = []
    unsubscribers.push(onBackendWakingChange(() => changes.push(isBackendWaking())))
    return changes
  }

  it('keeps trying while the sleeping backend cannot be reached or its proxy answers 502-504', async () => {
    const answers = [
      () => Promise.reject(new TypeError('Failed to fetch')),
      () => Promise.resolve(new Response(null, { status: 503 })),
      () => Promise.resolve(new Response(null, { status: 502 })),
      () => Promise.resolve(new Response('ok', { status: 200 })),
    ]
    const fetchMock = vi.fn(() => answers[fetchMock.mock.calls.length - 1]())
    vi.stubGlobal('fetch', fetchMock)
    const waking = recordWaking()

    const response = await backendFetch('/health')

    expect(await response.text()).toBe('ok')
    expect(fetchMock).toHaveBeenCalledTimes(4)
    // "Getting ready" was shown while it woke up, and is gone now that it answers.
    expect(waking).toEqual([true, false])
    expect(isBackendWaking()).toBe(false)
  })

  it('sends the same request again, body and all', async () => {
    const bodies: unknown[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn((_: string, init?: RequestInit) => {
        bodies.push(init?.body)
        return Promise.resolve(new Response(null, { status: bodies.length === 1 ? 503 : 204 }))
      }),
    )

    await backendFetch('/api/play/join', { method: 'POST', body: '{"pin":"123456"}' })

    expect(bodies).toEqual(['{"pin":"123456"}', '{"pin":"123456"}'])
  })

  it('shows "getting ready" while a request hangs after a quiet spell', async () => {
    Object.assign(backendTiming, { slowAfterMs: 10, idleAfterMs: -1 })
    let answer: (response: Response) => void = () => undefined
    vi.stubGlobal(
      'fetch',
      vi.fn(
        () =>
          new Promise<Response>((resolve) => {
            answer = resolve
          }),
      ),
    )

    const request = backendFetch('/health')
    await vi.waitFor(() => {
      expect(isBackendWaking()).toBe(true)
    })
    answer(new Response(null, { status: 200 }))
    await request

    expect(isBackendWaking()).toBe(false)
  })

  it('does not show "getting ready" when a backend that answered recently is merely slow', async () => {
    Object.assign(backendTiming, { slowAfterMs: 1, idleAfterMs: 60_000 })
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(null, { status: 200 }))))
    await backendFetch('/health')
    const slowAnswer = () =>
      new Promise<Response>((resolve) => {
        setTimeout(() => {
          resolve(new Response(null, { status: 200 }))
        }, 20)
      })
    vi.stubGlobal('fetch', vi.fn(slowAnswer))
    const waking = recordWaking()

    await backendFetch('/api/teacher/uploads/1/complete', { method: 'POST' })

    expect(waking).toEqual([])
  })

  it("returns the app's own error responses without trying again", async () => {
    const fetchMock = vi.fn(() => Promise.resolve(new Response(JSON.stringify({ code: 'INTERNAL_ERROR' }), { status: 500 })))
    vi.stubGlobal('fetch', fetchMock)

    const response = await backendFetch('/api/play/answer', { method: 'POST' })

    expect(response.status).toBe(500)
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('gives up with SERVER_UNAVAILABLE when the backend never answers', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))

    const error = await backendFetch('/health').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).code).toBe('SERVER_UNAVAILABLE')
    expect(isBackendWaking()).toBe(false)
  })

  it('stops trying when the request is aborted', async () => {
    Object.assign(backendTiming, { retryDelayMs: 1000, giveUpAfterMs: 60_000 })
    const fetchMock = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    const request = backendFetch('/health', { signal: controller.signal }).catch((e: unknown) => e)
    await vi.waitFor(() => {
      expect(isBackendWaking()).toBe(true)
    })
    controller.abort()

    expect(await request).toHaveProperty('name', 'AbortError')
    expect(fetchMock).toHaveBeenCalledOnce()
    expect(isBackendWaking()).toBe(false)
  })
})
