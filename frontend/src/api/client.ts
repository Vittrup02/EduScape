const apiBaseUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5115'

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
 * Pings the backend. Also serves as the wake-up call for the Render free tier,
 * so a slow response is expected after the backend has been idle.
 */
export async function checkHealth(signal?: AbortSignal): Promise<boolean> {
  try {
    const response = await fetch(`${apiBaseUrl}/health`, { signal })
    return response.ok
  } catch {
    return false
  }
}
