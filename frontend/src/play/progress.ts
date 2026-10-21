import type { LockProgress, PlayExitDoor, PlayLock, PlayObject, PlayRoom, PlayScene, TeamState } from '../api/play'

/**
 * The rules for showing the room, the same as RoomProgress in the backend. The server enforces them for every action
 * and tells the client which doors are open; the client uses them only to decide what to show.
 */

export function findScene(room: PlayRoom, sceneId: string | null): PlayScene | undefined {
  return room.scenes.find((scene) => scene.id === sceneId)
}

function sceneOf(room: PlayRoom, objectId: string): PlayScene | undefined {
  return room.scenes.find((scene) => scene.objects.some((o) => o.id === objectId))
}

function findObject(room: PlayRoom, objectId: string): PlayObject | undefined {
  return sceneOf(room, objectId)?.objects.find((o) => o.id === objectId)
}

/** A lock is completed when solved, a door when open. Letters and text boxes never complete anything. */
export function isCompleted(object: PlayObject, team: TeamState): boolean {
  switch (object.type) {
    case 'Lock':
      return team.solvedLockIds.includes(object.id)
    case 'Door':
    case 'ExitDoor':
      return team.openedDoorIds.includes(object.id)
    default:
      return false
  }
}

/** The start scene, and every scene behind an open door. */
export function isSceneUnlocked(room: PlayRoom, team: TeamState, sceneId: string): boolean {
  return (
    room.startSceneId === sceneId ||
    room.scenes.some((scene) =>
      scene.objects.some(
        (o) => o.type === 'Door' && o.targetSceneId === sceneId && team.openedDoorIds.includes(o.id),
      ),
    )
  )
}

/**
 * Visible and usable: its scene is unlocked and the object it waits for (if any) is completed. Doors never wait,
 * so they are always there, open or shut.
 */
export function isAvailable(room: PlayRoom, team: TeamState, object: PlayObject): boolean {
  const scene = sceneOf(room, object.id)
  if (scene === undefined || !isSceneUnlocked(room, team, scene.id)) {
    return false
  }
  if (object.unlockedById === null) {
    return true
  }
  const source = findObject(room, object.unlockedById)
  return source !== undefined && isCompleted(source, team)
}

/** The team can walk out: the exit door is there and open. */
export function canOpenExitDoor(room: PlayRoom, team: TeamState, exitDoor: PlayExitDoor): boolean {
  return isAvailable(room, team, exitDoor) && team.openedDoorIds.includes(exitDoor.id)
}

/** The scenes the team can move between, in the room's order. */
export function unlockedScenes(room: PlayRoom, team: TeamState): PlayScene[] {
  return room.scenes.filter((scene) => isSceneUnlocked(room, team, scene.id))
}

export function lockProgress(team: TeamState, lockId: string): LockProgress | undefined {
  return team.locks.find((l) => l.lockId === lockId)
}

/** The index of the step the team is on; for a solved lock, the number of steps. */
export function currentStep(team: TeamState, lock: PlayLock): number {
  return team.solvedLockIds.includes(lock.id) ? lock.steps.length : (lockProgress(team, lock.id)?.solvedSteps ?? 0)
}

/** The hint of the step the team is on, once earned. */
export function revealedHint(team: TeamState, lockId: string): string | null {
  return lockProgress(team, lockId)?.hint ?? null
}

/** Whole seconds the lock still waits after a wrong answer; 0 when the team may answer. */
export function cooldownRemaining(team: TeamState, lockId: string, now: number): number {
  const until = lockProgress(team, lockId)?.cooldownUntil ?? null
  return until === null ? 0 : Math.max(0, Math.ceil((until - now) / 1000))
}

/** Something that happened to the team, pushed by the server or answered to the student's own action. */
export type TeamEvent =
  | { type: 'lockSolved'; lockId: string }
  | { type: 'stepSolved'; lockId: string; solvedSteps: number }
  | { type: 'hintRevealed'; lockId: string; hint: string }
  /** `until` is on this device's clock (see cooldownUntil in api/play). */
  | { type: 'lockCooldown'; lockId: string; until: number | null }
  | { type: 'doorOpened'; doorId: string }
  | { type: 'teamEscaped' }

function updateLock(team: TeamState, lockId: string, update: (lock: LockProgress) => LockProgress): TeamState {
  const existing = lockProgress(team, lockId) ?? { lockId, solvedSteps: 0, hint: null, cooldownUntil: null }
  const updated = update(existing)
  if (updated === existing && team.locks.includes(existing)) {
    return team
  }
  return { ...team, locks: [...team.locks.filter((l) => l.lockId !== lockId), updated] }
}

/**
 * Applies an event to the team's state. Idempotent: the student's own answer and the server's push both report
 * the same progress, and that must not change anything twice.
 */
export function applyTeamEvent(team: TeamState, event: TeamEvent): TeamState {
  switch (event.type) {
    case 'lockSolved':
      return team.solvedLockIds.includes(event.lockId)
        ? team
        : {
            ...team,
            solvedLockIds: [...team.solvedLockIds, event.lockId],
            locks: team.locks.filter((l) => l.lockId !== event.lockId),
          }
    case 'stepSolved':
      if (team.solvedLockIds.includes(event.lockId)) {
        return team
      }
      // A new step starts afresh: its own hint, and no wait.
      return updateLock(team, event.lockId, (lock) =>
        lock.solvedSteps >= event.solvedSteps ? lock : { ...lock, solvedSteps: event.solvedSteps, hint: null, cooldownUntil: null },
      )
    case 'hintRevealed':
      return updateLock(team, event.lockId, (lock) => (lock.hint === event.hint ? lock : { ...lock, hint: event.hint }))
    case 'lockCooldown':
      return updateLock(team, event.lockId, (lock) =>
        lock.cooldownUntil === event.until ? lock : { ...lock, cooldownUntil: event.until },
      )
    case 'doorOpened':
      return team.openedDoorIds.includes(event.doorId)
        ? team
        : { ...team, openedDoorIds: [...team.openedDoorIds, event.doorId] }
    case 'teamEscaped':
      return team.escapedAt !== null ? team : { ...team, escapedAt: new Date().toISOString() }
  }
}
