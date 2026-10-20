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

/** A lock's hint, sent once the team has answered it wrong three times. */
export interface RevealedHint {
  lockId: string
  hint: string
}

export interface TeamState {
  id: string
  color: TeamColor
  solvedLockIds: string[]
  openedDoorIds: string[]
  revealedHints: RevealedHint[]
  escapedAt: string | null
}

export type AnswerType = 'Code' | 'Text' | 'MultipleChoice'

interface PlayObjectBase {
  id: string
  /** Centre of the object, in percent of the scene image's width and height. */
  x: number
  y: number
  /** A built-in asset key ("builtin:padlock") or an uploaded image's key. */
  graphicKey: string
  unlockedGraphicKey: string | null
  /** The lock or door that must be completed before this object appears; null means visible from start. */
  unlockedById: string | null
}

/** A lock as students see it: the question, never the answer. */
export interface PlayLock extends PlayObjectBase {
  type: 'Lock'
  question: string
  answerType: AnswerType
  /** The choices for MultipleChoice; null otherwise. */
  options: string[] | null
}

export interface PlayLetter extends PlayObjectBase {
  type: 'Letter'
  text: string
}

export interface PlayDoor extends PlayObjectBase {
  type: 'Door'
  targetSceneId: string | null
}

export interface PlayExitDoor extends PlayObjectBase {
  type: 'ExitDoor'
}

/** A piece of the story written straight onto the scene; it is read, not clicked. */
export interface PlayTextBox extends PlayObjectBase {
  type: 'TextBox'
  text: string
}

export type PlayObject = PlayLock | PlayLetter | PlayDoor | PlayExitDoor | PlayTextBox

export interface PlayScene {
  id: string
  /** The teacher's own name for the scene; not translated. */
  name: string
  backgroundImageKey: string
  /** A short-lived link to an uploaded background; null for built-in ones. */
  backgroundImageUrl: string | null
  /** The teacher's story text, shown when the student enters the scene; null when there is none. */
  narration: string | null
  objects: PlayObject[]
}

export interface PlayRoom {
  id: string
  title: string
  startSceneId: string | null
  scenes: PlayScene[]
}

/** Everything the student's device needs: the room, and the progress shared by the whole team. */
export interface PlayState {
  sessionId: string
  mode: SessionMode
  status: SessionStatus
  participantId: string
  nickname: string | null
  team: TeamState
  room: PlayRoom
  /** A teacher trying their own room from the editor. */
  isPreview: boolean
}

export interface JoinResult {
  deviceToken: string
  state: PlayState
}

/** `AlreadySolved`: a teammate solved the lock first, so it is open either way. */
export type AnswerOutcome = 'Correct' | 'Wrong' | 'AlreadySolved'

export interface AnswerResult {
  outcome: AnswerOutcome
  /** The lock's hint, once the team has earned it. */
  hint: string | null
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
  return (response.status === 204 ? undefined : await response.json()) as T
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

/** Only the server knows the answer. The team hears about a solved lock through the play hub. */
export function submitAnswer(deviceToken: string, lockId: string, answer: string): Promise<AnswerResult> {
  return post('/api/play/answer', { deviceToken, lockId, answer })
}

/** Opens the door for the whole team, which unlocks the scene behind it. */
export function openDoor(deviceToken: string, doorId: string): Promise<void> {
  return post('/api/play/doors/open', { deviceToken, doorId })
}

/** Throws an ApiError with EXIT_DOOR_LOCKED until every lock in the room is solved. */
export function openExitDoor(deviceToken: string, exitDoorId: string): Promise<void> {
  return post('/api/play/exit', { deviceToken, exitDoorId })
}

/**
 * Tells the teacher's dashboard which scene the student is in. Only the team's unlocked scenes can be reported
 * (SCENE_LOCKED otherwise).
 */
export function reportScene(deviceToken: string, sceneId: string): Promise<void> {
  return post('/api/play/scene', { deviceToken, sceneId })
}
