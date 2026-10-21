import { backendFetch, toApiError } from './client'
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

/**
 * The team's progress on an unsolved lock: the step it is on (the number of steps answered), the hint of that step
 * once the team has earned it, and until when the lock waits after a wrong answer (this device's clock, in ms).
 */
export interface LockProgress {
  lockId: string
  solvedSteps: number
  hint: string | null
  cooldownUntil: number | null
}

export interface TeamState {
  id: string
  color: TeamColor
  solvedLockIds: string[]
  /** The open doors and exit doors; they open by themselves when the team meets their condition. */
  openedDoorIds: string[]
  locks: LockProgress[]
  escapedAt: string | null
}

export type AnswerType = 'Code' | 'Text' | 'MultipleChoice' | 'Order'

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

/** Something to put in order, with a short-lived link to its image. */
export interface PlayOrderItem {
  id: string
  text: string
  imageUrl: string | null
}

/** One step of a lock: the question, never the answer. */
export interface PlayStep {
  question: string
  answerType: AnswerType
  /** The choices for MultipleChoice; null otherwise. */
  options: string[] | null
  /** The items of an Order step, shuffled by the server; null otherwise. */
  orderItems: PlayOrderItem[] | null
}

/** A lock as students see it: its steps, answered in order. */
export interface PlayLock extends PlayObjectBase {
  type: 'Lock'
  steps: PlayStep[]
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
  /** How long a lock waits after a wrong answer; 0 when it doesn't. */
  wrongAnswerCooldownSeconds: number
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
  /** A teacher trying their own room. */
  isPreview: boolean
  /** The scene the student was last in, e.g. before a reload; null at the start. */
  currentSceneId: string | null
}

export interface JoinResult {
  deviceToken: string
  state: PlayState
}

/**
 * `AlreadySolved`: a teammate answered the step first, so it is done either way. `CoolingDown`: the team answered
 * wrong a moment ago and has to wait.
 */
export type AnswerOutcome = 'Correct' | 'Wrong' | 'AlreadySolved' | 'CoolingDown'

export interface AnswerResult {
  outcome: AnswerOutcome
  /** The team's progress on the lock afterwards. */
  solvedSteps: number
  /** This answer solved the lock's last step. */
  lockSolved: boolean
  /** The current step's hint, once the team has earned it. */
  hint: string | null
  /** How long the team must wait before answering the lock again. */
  cooldownSeconds: number
  /** Doors that opened because of this answer. */
  openedDoorIds: string[]
}

/** What the student answers: a typed or chosen answer, or the items' ids in the student's order. */
export type Answer = { text: string } | { order: string[] }

/** The team's state as the server sends it: waits in seconds from now. */
interface TeamStateJson extends Omit<TeamState, 'locks'> {
  locks: { lockId: string; solvedSteps: number; hint: string | null; cooldownSeconds: number }[]
}

interface PlayStateJson extends Omit<PlayState, 'team'> {
  team: TeamStateJson
}

/** Turns the server's "seconds from now" into a time on this device's clock, which the countdown runs on. */
export function cooldownUntil(seconds: number, now: number = Date.now()): number | null {
  return seconds > 0 ? now + seconds * 1000 : null
}

function fromJson(state: PlayStateJson): PlayState {
  return {
    ...state,
    team: {
      ...state.team,
      locks: state.team.locks.map(({ cooldownSeconds, ...lock }) => ({
        ...lock,
        cooldownUntil: cooldownUntil(cooldownSeconds),
      })),
    },
  }
}

/** PINs and tokens go in the body, never in the URL, so they stay out of logs. */
async function post<T>(path: string, body: unknown): Promise<T> {
  const response = await backendFetch(path, {
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
export async function joinSession(pin: string, teamId: string | null, nickname: string): Promise<JoinResult> {
  const joined = await post<{ deviceToken: string; state: PlayStateJson }>('/api/play/join', { pin, teamId, nickname })
  return { deviceToken: joined.deviceToken, state: fromJson(joined.state) }
}

/** The team's whole state. Throws an ApiError: SESSION_CLOSED or DEVICE_NOT_RECOGNIZED mean the token is useless. */
export async function reconnect(deviceToken: string): Promise<PlayState> {
  return fromJson(await post<PlayStateJson>('/api/play/reconnect', { deviceToken }))
}

/**
 * Only the server knows the answer. `stepIndex` is the step the student answered, so an answer to a step a teammate
 * has just solved changes nothing. The team hears about progress through the play hub.
 */
export function submitAnswer(deviceToken: string, lockId: string, stepIndex: number, answer: Answer): Promise<AnswerResult> {
  return post('/api/play/answer', {
    deviceToken,
    lockId,
    stepIndex,
    answer: 'text' in answer ? answer.text : null,
    order: 'order' in answer ? answer.order : null,
  })
}

/** Throws an ApiError with EXIT_DOOR_LOCKED until the exit door is open. */
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
