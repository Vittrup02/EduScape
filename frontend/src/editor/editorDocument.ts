import type { EditorDoor, EditorObject, EditorScene, RoomDocument } from '../api/editor'
import { assetsOfType, type ObjectAsset } from '../assets/registry'
import { newId } from './ids'

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
 * A new object from the asset library at the given position. A new door leads to another scene if there is one,
 * so that a room with two scenes needs no extra step.
 */
export function createObject(
  asset: ObjectAsset,
  x: number,
  y: number,
  document: RoomDocument,
  sceneId: string,
): EditorObject {
  const base = { id: newId(), x: clampPosition(x), y: clampPosition(y), graphicKey: asset.key, unlockedById: null }
  switch (asset.type) {
    case 'Lock':
      return {
        ...base,
        type: 'Lock',
        question: '',
        answerType: asset.key === 'builtin:code-lock' ? 'Code' : 'Text',
        correctAnswer: '',
        tolerance: null,
        hint: null,
        options: [],
      }
    case 'Letter':
      return { ...base, type: 'Letter', text: '' }
    case 'Door':
      return { ...base, type: 'Door', targetSceneId: document.scenes.find((s) => s.id !== sceneId)?.id ?? null }
    case 'ExitDoor':
      return { ...base, type: 'ExitDoor' }
  }
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

/** Removes the object; whatever waited for it becomes visible from start, as on the server. */
export function removeObject(document: RoomDocument, objectId: string): RoomDocument {
  const without = mapScenes(document, (scene) =>
    scene.objects.some((o) => o.id === objectId)
      ? { ...scene, objects: scene.objects.filter((o) => o.id !== objectId) }
      : scene,
  )
  return mapObjects(without, (object) => (object.unlockedById === objectId ? { ...object, unlockedById: null } : object))
}

const backgrounds = assetsOfType('Background')

/** A new scene with a built-in background the room doesn't use yet, so scenes look different from the start. */
export function createScene(document: RoomDocument, name: string): EditorScene {
  const used = new Set(document.scenes.map((s) => s.backgroundImageKey))
  const background = backgrounds.find((b) => !used.has(b.key)) ?? backgrounds[0]
  return { id: newId(), name, backgroundImageKey: background.key, backgroundImageUrl: null, narration: '', objects: [] }
}

export function addScene(document: RoomDocument, scene: EditorScene): RoomDocument {
  return { ...document, scenes: [...document.scenes, scene] }
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

  const removedObjects = new Set(removed.objects.map((o) => o.id))
  const scenes = document.scenes.filter((s) => s.id !== sceneId)
  const without: RoomDocument = {
    ...document,
    scenes,
    startSceneId: document.startSceneId === sceneId ? scenes[0].id : document.startSceneId,
  }
  return mapObjects(without, (object) => {
    let next = object
    if (next.unlockedById !== null && removedObjects.has(next.unlockedById)) {
      next = { ...next, unlockedById: null }
    }
    if (next.type === 'Door' && next.targetSceneId === sceneId) {
      next = { ...next, targetSceneId: null } satisfies EditorDoor
    }
    return next
  })
}

export function setStartScene(document: RoomDocument, sceneId: string): RoomDocument {
  return document.startSceneId === sceneId ? document : { ...document, startSceneId: sceneId }
}

export function setRoomDetails(
  document: RoomDocument,
  changes: Partial<Pick<RoomDocument, 'title' | 'subject' | 'gradeLevel'>>,
): RoomDocument {
  return { ...document, ...changes }
}

/**
 * The objects that `objectId` can wait for: locks and doors anywhere in the room, except itself and any object that
 * already (directly or further down its chain) waits for it, which would make the two wait for each other forever.
 */
export function unlockedByChoices(document: RoomDocument, objectId: string): EditorObject[] {
  const objects = allObjects(document)
  const byId = new Map(objects.map((o) => [o.id, o]))

  const waitsFor = (candidate: EditorObject): boolean => {
    const seen = new Set<string>()
    let current: EditorObject | undefined = candidate
    while (current?.unlockedById != null && !seen.has(current.id)) {
      if (current.unlockedById === objectId) {
        return true
      }
      seen.add(current.id)
      current = byId.get(current.unlockedById)
    }
    return false
  }

  return objects.filter((o) => o.id !== objectId && (o.type === 'Lock' || o.type === 'Door') && !waitsFor(o))
}

/** A title and scene names are required; the editor waits with saving until they are filled in. */
export function missingRequiredText(document: RoomDocument): boolean {
  return document.title.trim() === '' || document.scenes.some((s) => s.name.trim() === '')
}
