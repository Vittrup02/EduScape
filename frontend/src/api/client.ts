const apiBaseUrl = import.meta.env.VITE_API_URL ?? 'http://localhost:5115'

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
