/**
 * The device token a student got when joining. Kept in localStorage so a reload (or a lost connection) brings the
 * student back to the same team. It is the only thing stored about the student in the browser.
 */
export const deviceStorageKey = 'eduscape.device'

// Used when storage is blocked (e.g. private browsing): the student then stays in the session until the page is closed.
let memoryCopy: string | null = null

export function loadDeviceToken(): string | null {
  try {
    return localStorage.getItem(deviceStorageKey)
  } catch {
    return memoryCopy
  }
}

export function saveDeviceToken(token: string | null): void {
  memoryCopy = token
  try {
    if (token === null) {
      localStorage.removeItem(deviceStorageKey)
    } else {
      localStorage.setItem(deviceStorageKey, token)
    }
  } catch {
    // Falls back to memoryCopy.
  }
}
