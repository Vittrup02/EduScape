import type { PlayExitDoor, PlayObject, PlayRoom, PlayScene, TeamState } from '../api/play'

/**
 * The unlockedBy rules for showing the room, the same as RoomProgress in the backend. The server enforces them
 * for every action; the client uses them only to decide what to show.
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

/** A lock is completed when solved, a door when opened. Letters and exit doors never complete anything. */
export function isCompleted(object: PlayObject, team: TeamState): boolean {
  switch (object.type) {
    case 'Lock':
      return team.solvedLockIds.includes(object.id)
    case 'Door':
      return team.openedDoorIds.includes(object.id)
    default:
      return false
  }
}

/** The start scene, and every scene behind a door the team has opened. */
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

/** Visible and usable: its scene is unlocked and the object it waits for (if any) is completed. */
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

export function canOpenExitDoor(room: PlayRoom, team: TeamState, exitDoor: PlayExitDoor): boolean {
  return (
    isAvailable(room, team, exitDoor) &&
    room.scenes.every((scene) => scene.objects.every((o) => o.type !== 'Lock' || team.solvedLockIds.includes(o.id)))
  )
}

/** The scenes the team can move between, in the room's order. */
export function unlockedScenes(room: PlayRoom, team: TeamState): PlayScene[] {
  return room.scenes.filter((scene) => isSceneUnlocked(room, team, scene.id))
}

export function revealedHint(team: TeamState, lockId: string): string | null {
  return team.revealedHints.find((h) => h.lockId === lockId)?.hint ?? null
}

/** Something that happened to the team, pushed by the server or answered to the student's own action. */
export type TeamEvent =
  | { type: 'lockSolved'; lockId: string }
  | { type: 'hintRevealed'; lockId: string; hint: string }
  | { type: 'doorOpened'; doorId: string }
  | { type: 'teamEscaped' }

/**
 * Applies an event to the team's state. Idempotent: the student's own answer and the server's push both report
 * the same solved lock, and that must not change anything twice.
 */
export function applyTeamEvent(team: TeamState, event: TeamEvent): TeamState {
  switch (event.type) {
    case 'lockSolved':
      return team.solvedLockIds.includes(event.lockId)
        ? team
        : { ...team, solvedLockIds: [...team.solvedLockIds, event.lockId] }
    case 'hintRevealed':
      return team.revealedHints.some((h) => h.lockId === event.lockId)
        ? team
        : { ...team, revealedHints: [...team.revealedHints, { lockId: event.lockId, hint: event.hint }] }
    case 'doorOpened':
      return team.openedDoorIds.includes(event.doorId)
        ? team
        : { ...team, openedDoorIds: [...team.openedDoorIds, event.doorId] }
    case 'teamEscaped':
      return team.escapedAt !== null ? team : { ...team, escapedAt: new Date().toISOString() }
  }
}
