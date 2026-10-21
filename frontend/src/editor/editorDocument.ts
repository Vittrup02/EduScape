import {
  textBoxKey,
  type EditorDoor,
  type EditorLock,
  type EditorObject,
  type EditorScene,
  type EditorTextBox,
  type RoomDocument,
} from '../api/editor'
import { assetsOfType, type ObjectAsset } from '../assets/registry'
import { newId } from './ids'
import { moveItem } from './puzzle'

/**
 * Changes to the room document. Each returns a new document (or the same one when nothing changes), so the history
 * can keep every version for undo. Removing things clears references to them the way the server does.
 */

/** Positions are kept inside the image and rounded, so a nudge or a drag never produces 33.33333333. */
export function clampPosition(value: number): number {
  return Math.round(Math.min(100, Math.max(0, value)) * 100) / 100
}

export function findScene(document: RoomDocument, sceneId: string): EditorScene | undefined {
  return document.scenes.find((s) => s.id === sceneId)
}

export function findObject(document: RoomDocument, objectId: string): { scene: EditorScene; object: EditorObject } | undefined {
  for (const scene of document.scenes) {
    const object = scene.objects.find((o) => o.id === objectId)
    if (object !== undefined) {
      return { scene, object }
    }
  }
  return undefined
}

export function allObjects(document: RoomDocument): EditorObject[] {
  return document.scenes.flatMap((s) => s.objects)
}

/** Maps the items, returning the same array when no item changed, so unchanged parts keep their identity. */
function mapSame<T>(items: T[], map: (item: T) => T): T[] {
  const mapped = items.map(map)
  return mapped.some((item, i) => item !== items[i]) ? mapped : items
}

function mapScenes(document: RoomDocument, map: (scene: EditorScene) => EditorScene): RoomDocument {
  const scenes = mapSame(document.scenes, map)
  return scenes === document.scenes ? document : { ...document, scenes }
}

function mapObjects(document: RoomDocument, map: (object: EditorObject) => EditorObject): RoomDocument {
  return mapScenes(document, (scene) => {
    const objects = mapSame(scene.objects, map)
    return objects === scene.objects ? scene : { ...scene, objects }
  })
}

/**
 * A new object from the asset library at the given position. A lock starts without a puzzle: the teacher picks the
 * kind next. A door opens once every lock in its scene is solved, and leads to another scene if there is one, so that
 * a room with two scenes needs no extra step.
 */
export function createObject(
  asset: ObjectAsset,
  x: number,
  y: number,
  document: RoomDocument,
  sceneId: string,
): EditorObject {
  const base = { id: newId(), x: clampPosition(x), y: clampPosition(y), graphicKey: asset.key, unlockedById: null }
  const doorway = { openCondition: 'AllInScene' as const, requiredLockIds: [] }
  switch (asset.type) {
    case 'Lock':
      return { ...base, type: 'Lock', steps: [] }
    case 'Letter':
      return { ...base, type: 'Letter', text: '' }
    case 'Door':
      return { ...base, ...doorway, type: 'Door', targetSceneId: document.scenes.find((s) => s.id !== sceneId)?.id ?? null }
    case 'ExitDoor':
      return { ...base, ...doorway, type: 'ExitDoor' }
  }
}

/** A new, empty text box at the given position. */
export function createTextBox(x: number, y: number): EditorTextBox {
  return { id: newId(), type: 'TextBox', x: clampPosition(x), y: clampPosition(y), graphicKey: textBoxKey, unlockedById: null, text: '' }
}

export function addObject(document: RoomDocument, sceneId: string, object: EditorObject): RoomDocument {
  return mapScenes(document, (scene) => (scene.id === sceneId ? { ...scene, objects: [...scene.objects, object] } : scene))
}

/** Applies `update` to the object with this id. The update must keep the object's type. */
export function updateObject<T extends EditorObject>(
  document: RoomDocument,
  objectId: string,
  update: (object: T) => T,
): RoomDocument {
  return mapObjects(document, (object) => (object.id === objectId ? update(object as T) : object))
}

export function moveObject(document: RoomDocument, objectId: string, x: number, y: number): RoomDocument {
  const nextX = clampPosition(x)
  const nextY = clampPosition(y)
  return updateObject(document, objectId, (object) =>
    object.x === nextX && object.y === nextY ? object : { ...object, x: nextX, y: nextY },
  )
}

/**
 * Clears references to removed objects, as the server does: whatever waited for one becomes visible from start, and a
 * door no longer waits for a removed lock.
 */
function forgetObjects(document: RoomDocument, removed: ReadonlySet<string>): RoomDocument {
  return mapObjects(document, (object) => {
    let next = object
    if (next.unlockedById !== null && removed.has(next.unlockedById)) {
      next = { ...next, unlockedById: null }
    }
    if ((next.type === 'Door' || next.type === 'ExitDoor') && next.requiredLockIds.some((id) => removed.has(id))) {
      next = { ...next, requiredLockIds: next.requiredLockIds.filter((id) => !removed.has(id)) }
    }
    return next
  })
}

/** Removes the object, and every reference to it. */
export function removeObject(document: RoomDocument, objectId: string): RoomDocument {
  const without = mapScenes(document, (scene) =>
    scene.objects.some((o) => o.id === objectId)
      ? { ...scene, objects: scene.objects.filter((o) => o.id !== objectId) }
      : scene,
  )
  return forgetObjects(without, new Set([objectId]))
}

const backgrounds = assetsOfType('Background')

/**
 * A new scene with a readable name, "Scene 3" in the teacher's language (`nameFor` gives the name for a number), and a
 * built-in background the room doesn't use yet, so scenes look different from the start. The number counts the scenes,
 * skipping names the room already has.
 */
export function createScene(document: RoomDocument, nameFor: (n: number) => string): EditorScene {
  const usedNames = new Set(document.scenes.map((s) => s.name.trim()))
  let n = document.scenes.length + 1
  while (usedNames.has(nameFor(n))) {
    n++
  }
  const used = new Set(document.scenes.map((s) => s.backgroundImageKey))
  const background = backgrounds.find((b) => !used.has(b.key)) ?? backgrounds[0]
  return { id: newId(), name: nameFor(n), backgroundImageKey: background.key, backgroundImageUrl: null, narration: '', objects: [] }
}

export function addScene(document: RoomDocument, scene: EditorScene): RoomDocument {
  return { ...document, scenes: [...document.scenes, scene] }
}

/** Moves a scene in the room's order, which is the order students see the scenes in. */
export function moveScene(document: RoomDocument, from: number, to: number): RoomDocument {
  if (from === to || to < 0 || to >= document.scenes.length) {
    return document
  }
  return { ...document, scenes: moveItem(document.scenes, from, to) }
}

export function updateScene(
  document: RoomDocument,
  sceneId: string,
  changes: Partial<Pick<EditorScene, 'name' | 'backgroundImageKey' | 'backgroundImageUrl' | 'narration'>>,
): RoomDocument {
  return mapScenes(document, (scene) => (scene.id === sceneId ? { ...scene, ...changes } : scene))
}

/**
 * Removes a scene with its objects. Doors that led there lose their target, and objects that waited for something
 * in it become visible from start, as on the server. The last scene cannot be removed.
 */
export function removeScene(document: RoomDocument, sceneId: string): RoomDocument {
  const removed = findScene(document, sceneId)
  if (removed === undefined || document.scenes.length === 1) {
    return document
  }

  const scenes = document.scenes.filter((s) => s.id !== sceneId)
  const without: RoomDocument = {
    ...document,
    scenes,
    startSceneId: document.startSceneId === sceneId ? scenes[0].id : document.startSceneId,
  }
  const forgotten = forgetObjects(without, new Set(removed.objects.map((o) => o.id)))
  return mapObjects(forgotten, (object) =>
    object.type === 'Door' && object.targetSceneId === sceneId
      ? ({ ...object, targetSceneId: null } satisfies EditorDoor)
      : object,
  )
}

export function setStartScene(document: RoomDocument, sceneId: string): RoomDocument {
  return document.startSceneId === sceneId ? document : { ...document, startSceneId: sceneId }
}

export function setRoomDetails(
  document: RoomDocument,
  changes: Partial<Pick<RoomDocument, 'title' | 'subject' | 'gradeLevel' | 'wrongAnswerCooldownSeconds'>>,
): RoomDocument {
  return { ...document, ...changes }
}

/** The room's locks, for a door that opens when chosen locks are solved: those in the door's scene first. */
export function lockChoices(document: RoomDocument, sceneId: string): EditorLock[] {
  const locks = (scene: EditorScene) => scene.objects.filter((o): o is EditorLock => o.type === 'Lock')
  const own = findScene(document, sceneId)
  return [...(own === undefined ? [] : locks(own)), ...document.scenes.filter((s) => s.id !== sceneId).flatMap(locks)]
}

/**
 * What an object needs before students can complete it: what it waits for with unlockedBy, and for a door the locks
 * that open it (every lock in its scene, or the chosen ones).
 */
function dependencies(document: RoomDocument, sceneId: string, object: EditorObject): string[] {
  const needs = object.unlockedById === null ? [] : [object.unlockedById]
  if (object.type !== 'Door' && object.type !== 'ExitDoor') {
    return needs
  }
  const opensWith =
    object.openCondition === 'AllInScene'
      ? (findScene(document, sceneId)?.objects.filter((o) => o.type === 'Lock').map((o) => o.id) ?? [])
      : object.requiredLockIds
  return [...needs, ...opensWith]
}

/**
 * The objects that `objectId` can wait for: locks and doors anywhere in the room, except itself and any object that
 * already needs it, directly or further down the line (e.g. a door that opens with a lock that waits for it), which
 * would make them wait for each other forever. Doors never wait for anything: they open by their own condition.
 */
export function unlockedByChoices(document: RoomDocument, objectId: string): EditorObject[] {
  const self = findObject(document, objectId)?.object
  if (self?.type === 'Door' || self?.type === 'ExitDoor') {
    return []
  }

  const needsSelf = (candidateId: string): boolean => {
    const seen = new Set<string>()
    const pending = [candidateId]
    for (let id = pending.pop(); id !== undefined; id = pending.pop()) {
      if (id === objectId) {
        return true
      }
      const found = seen.has(id) ? undefined : findObject(document, id)
      if (found !== undefined) {
        seen.add(id)
        pending.push(...dependencies(document, found.scene.id, found.object))
      }
    }
    return false
  }

  return allObjects(document).filter(
    (o) => o.id !== objectId && (o.type === 'Lock' || o.type === 'Door') && !needsSelf(o.id),
  )
}

/** A title and scene names are required; the editor waits with saving until they are filled in. */
export function missingRequiredText(document: RoomDocument): boolean {
  return document.title.trim() === '' || document.scenes.some((s) => s.name.trim() === '')
}
