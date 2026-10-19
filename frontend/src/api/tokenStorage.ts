/** The signed-in teacher's tokens. Kept in localStorage so a reload keeps the teacher signed in. */
export interface StoredAuth {
  email: string
  accessToken: string
  refreshToken: string
}

export const authStorageKey = 'eduscape.auth'

// Used when storage is blocked (e.g. private browsing): the teacher then stays signed in until the page is closed.
let memoryCopy: string | null = null

function isStoredAuth(value: unknown): value is StoredAuth {
  return (
    typeof value === 'object' &&
    value !== null &&
    'email' in value &&
    typeof value.email === 'string' &&
    'accessToken' in value &&
    typeof value.accessToken === 'string' &&
    'refreshToken' in value &&
    typeof value.refreshToken === 'string'
  )
}

/** The raw stored value, so callers can cheaply tell whether anything changed. */
export function readRawAuth(): string | null {
  try {
    return localStorage.getItem(authStorageKey)
  } catch {
    return memoryCopy
  }
}

export function parseAuth(raw: string | null): StoredAuth | null {
  if (raw === null) {
    return null
  }
  try {
    const value: unknown = JSON.parse(raw)
    return isStoredAuth(value) ? value : null
  } catch {
    return null
  }
}

export function loadAuth(): StoredAuth | null {
  return parseAuth(readRawAuth())
}

export function saveAuth(auth: StoredAuth | null): void {
  memoryCopy = auth ? JSON.stringify(auth) : null
  try {
    if (memoryCopy === null) {
      localStorage.removeItem(authStorageKey)
    } else {
      localStorage.setItem(authStorageKey, memoryCopy)
    }
  } catch {
    // Falls back to memoryCopy.
  }
}
