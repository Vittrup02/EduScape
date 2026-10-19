import type { Translation } from '../i18n/languages'
import { teacherFetch } from './auth'

/** Fixed lists arrive as keys (e.g. "Red") and are translated, e.g. with teamName(). */
export type TeamColor = keyof Translation['teamColors']
export type SessionMode = 'Team' | 'Individual'
export type SessionStatus = 'Open' | 'Locked' | 'Closed'

/** Team mode: one team per colour at most. */
export const minTeamCount = 2
export const maxTeamCount = 8

export interface SessionSummary {
  id: string
  pin: string
  mode: SessionMode
  status: SessionStatus
  roomTitle: string
  className: string
  startedAt: string
  expiresAt: string
}

export interface SessionParticipant {
  id: string
  nickname: string | null
}

export interface SessionTeam {
  id: string
  color: TeamColor
  participants: SessionParticipant[]
}

export interface SessionDetails extends SessionSummary {
  teams: SessionTeam[]
}

export interface StartSessionRequest {
  roomId: string
  className: string
  mode: SessionMode
  teamCount: number
}

const jsonHeaders = { 'Content-Type': 'application/json' }

/** Names of the teacher's classes, suggested when starting a session. */
export async function getClassNames(signal?: AbortSignal): Promise<string[]> {
  const response = await teacherFetch('/api/teacher/classes', { signal })
  return (await response.json()) as string[]
}

/** Starts a session and returns it with its PIN. Throws an ApiError, e.g. VALUE_REQUIRED or TEAM_COUNT_INVALID. */
export async function startSession(request: StartSessionRequest): Promise<SessionDetails> {
  const response = await teacherFetch('/api/teacher/sessions', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify(request),
  })
  return (await response.json()) as SessionDetails
}

/** The teacher's sessions that can still be played, newest first. */
export async function getActiveSessions(signal?: AbortSignal): Promise<SessionSummary[]> {
  const response = await teacherFetch('/api/teacher/sessions', { signal })
  return (await response.json()) as SessionSummary[]
}

export async function getSession(id: string, signal?: AbortSignal): Promise<SessionDetails> {
  const response = await teacherFetch(`/api/teacher/sessions/${encodeURIComponent(id)}`, { signal })
  return (await response.json()) as SessionDetails
}

export type SessionAction = 'lock' | 'unlock' | 'close'

/** Lock stops new students from joining; close ends the session and the PIN stops working. */
export async function changeSession(id: string, action: SessionAction): Promise<void> {
  await teacherFetch(`/api/teacher/sessions/${encodeURIComponent(id)}/${action}`, { method: 'POST' })
}
