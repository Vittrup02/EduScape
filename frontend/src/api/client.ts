import { defaultApiUrl } from './defaultApiUrl'

export const apiBaseUrl = import.meta.env.VITE_API_URL ?? defaultApiUrl

/**
 * An error response from the backend. The backend only sends a code (e.g. SESSION_CLOSED);
 * show it to the user with t(errorKey(error.code)).
 */
export class ApiError extends Error {
  readonly code: string
  readonly status: number

  constructor(code: string, status: number) {
    super(code)
    this.name = 'ApiError'
    this.code = code
    this.status = status
  }
}

/** Reads the `{ "code": "..." }` body of a failed response; anything else becomes UNKNOWN. */
export async function toApiError(response: Response): Promise<ApiError> {
  try {
    const body: unknown = await response.json()
    if (typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string') {
      return new ApiError(body.code, response.status)
    }
  } catch {
    // Not JSON, e.g. a proxy error page while the backend is waking up.
  }
  return new ApiError('UNKNOWN', response.status)
}

/**
 * How long to wait for a backend that is waking up. Render's free tier puts it to sleep after about 15 minutes
 * without traffic, and starting it again takes up to a minute. Tests shorten these.
 */
export const backendTiming = {
  /** A request still unanswered after this long, after a quiet spell, means the backend is waking up. */
  slowAfterMs: 2500,
  /** No answer for this long counts as a quiet spell; until then the backend is awake and merely busy. */
  idleAfterMs: 10 * 60_000,
  /** Pause before trying again when the backend could not be reached. */
  retryDelayMs: 2000,
  /** Then the request fails with SERVER_UNAVAILABLE. */
  giveUpAfterMs: 120_000,
}

/** Answers Render's proxy gives while the instance starts or restarts; the app itself never sends them. */
function isStarting(status: number): boolean {
  return status === 502 || status === 503 || status === 504
}

let lastAnswerAt = Number.NEGATIVE_INFINITY
let wakingRequests = 0
const wakingListeners = new Set<() => void>()

/** True while a request waits for the backend to wake up, so the UI can show "Getting ready" instead of an error. */
export function isBackendWaking(): boolean {
  return wakingRequests > 0
}

/** Notifies when isBackendWaking() changes. Returns an unsubscribe function. */
export function onBackendWakingChange(listener: () => void): () => void {
  wakingListeners.add(listener)
  return () => {
    wakingListeners.delete(listener)
  }
}

function changeWakingRequests(delta: number) {
  const before = isBackendWaking()
  wakingRequests += delta
  if (isBackendWaking() !== before) {
    for (const listener of wakingListeners) {
      listener()
    }
  }
}

function wait(ms: number, signal: AbortSignal | null | undefined): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(signal.reason as Error)
      return
    }
    const onAbort = () => {
      clearTimeout(timer)
      reject(signal?.reason as Error)
    }
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

/**
 * Every call to the backend goes through here. While the backend is waking up, the request either hangs (Render
 * holds it until the app has started) or fails to connect; it is retried until the backend answers, and meanwhile
 * isBackendWaking() is true. Error responses from the app itself are returned as they are. Throws an ApiError with
 * SERVER_UNAVAILABLE if the backend has not answered after backendTiming.giveUpAfterMs.
 */
export async function backendFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const giveUpAt = Date.now() + backendTiming.giveUpAfterMs
  let waking = false
  // A function, so TypeScript does not assume the flag is unchanged after the timer may have set it.
  const isWaking = () => waking
  const startWaking = () => {
    if (!waking) {
      waking = true
      changeWakingRequests(1)
    }
  }

  try {
    for (;;) {
      const quiet = Date.now() - lastAnswerAt > backendTiming.idleAfterMs
      const slowTimer = quiet ? setTimeout(startWaking, backendTiming.slowAfterMs) : undefined
      let response: Response | null = null
      try {
        response = await fetch(`${apiBaseUrl}${path}`, init)
      } catch (error) {
        if (init.signal?.aborted) {
          throw error
        }
        // The backend could not be reached at all, e.g. while it starts. Tried again below.
      } finally {
        clearTimeout(slowTimer)
      }

      if (response !== null && !isStarting(response.status)) {
        lastAnswerAt = Date.now()
        return response
      }
      if (Date.now() >= giveUpAt) {
        throw new ApiError('SERVER_UNAVAILABLE', response?.status ?? 0)
      }
      startWaking()
      await wait(backendTiming.retryDelayMs, init.signal)
    }
  } finally {
    if (isWaking()) {
      changeWakingRequests(-1)
    }
  }
}

/**
 * Pings the backend. Also serves as the wake-up call for the Render free tier: it keeps trying until the backend
 * is awake, which shows the "Getting ready" message meanwhile.
 */
export async function checkHealth(signal?: AbortSignal): Promise<boolean> {
  try {
    const response = await backendFetch('/health', { signal })
    return response.ok
  } catch {
    return false
  }
}
