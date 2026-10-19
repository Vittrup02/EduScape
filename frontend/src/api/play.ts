import { apiBaseUrl, toApiError } from './client'
import type { SessionMode, SessionStatus, TeamColor } from './sessions'

/**
 * Calls for students, who never log in. They join with the PIN and get a device token, which the browser keeps
 * (see deviceStorage) to get back to the same team after a reload.
 */

export interface PlayTeam {
  id: string
  color: TeamColor
}

/** What a PIN leads to. `teams` is empty in individual mode, where the server creates a team per student. */
export interface LookupResult {
  mode: SessionMode
  teams: PlayTeam[]
}

export interface TeamState {
  id: string
  color: TeamColor
  solvedLockIds: string[]
  openedDoorIds: string[]
  escapedAt: string | null
}

/** Everything the student's device needs, shared by the whole team. */
export interface PlayState {
  sessionId: string
  mode: SessionMode
  status: SessionStatus
  participantId: string
  nickname: string | null
  team: TeamState
}

export interface JoinResult {
  deviceToken: string
  state: PlayState
}

/** PINs and tokens go in the body, never in the URL, so they stay out of logs. */
async function post<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) {
    throw await toApiError(response)
  }
  return (await response.json()) as T
}

/** Throws an ApiError, e.g. PIN_NOT_FOUND, PIN_EXPIRED or SESSION_LOCKED. */
export function lookupPin(pin: string): Promise<LookupResult> {
  return post('/api/play/lookup', { pin })
}

/** `teamId` is ignored in individual mode. A blank nickname is stored as none. */
export function joinSession(pin: string, teamId: string | null, nickname: string): Promise<JoinResult> {
  return post('/api/play/join', { pin, teamId, nickname })
}

/** The team's whole state. Throws an ApiError: SESSION_CLOSED or DEVICE_NOT_RECOGNIZED mean the token is useless. */
export function reconnect(deviceToken: string): Promise<PlayState> {
  return post('/api/play/reconnect', { deviceToken })
}
