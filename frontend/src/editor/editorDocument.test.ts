import { describe, expect, it } from 'vitest'
import type { EditorDoor, EditorLock, EditorObject } from '../api/editor'
import { findAsset, type ObjectAsset } from '../assets/registry'
import { editorDocument } from '../test/editorRoom'
import {
  addObject,
  addScene,
  clampPosition,
  createObject,
  createScene,
  createTextBox,
  findObject,
  lockChoices,
  missingRequiredText,
  moveObject,
  moveScene,
  removeObject,
  removeScene,
  unlockedByChoices,
  updateObject,
  updateScene,
} from './editorDocument'
import { newId, randomId } from './ids'

function asset(key: string): ObjectAsset {
  const found = findAsset(key)
  if (found === undefined || found.type === 'Background') {
    throw new Error(key)
  }
  return found
}

describe('editor document', () => {
  it('keeps positions inside the picture and rounds them', () => {
    expect(clampPosition(-3)).toBe(0)
    expect(clampPosition(104)).toBe(100)
    expect(clampPosition(33.333333)).toBe(33.33)

    const moved = moveObject(editorDocument(), 'lock', 120, 12.3456)
    expect(findObject(moved, 'lock')?.object).toMatchObject({ x: 100, y: 12.35 })
  })

  it('returns the same document when nothing changes, so no empty undo step is made', () => {
    const document = editorDocument()

    expect(moveObject(document, 'lock', 30, 40)).toBe(document)
    expect(updateObject(document, 'unknown', (o) => ({ ...o, x: 1 }))).toBe(document)
  })

  it('creates objects from the asset library with sensible defaults', () => {
    const document = editorDocument()

    // A lock is its graphic; the teacher picks the kind of puzzle next.
    const chest = createObject(asset('builtin:chest'), 10, 20, document, 'hall') as EditorLock
    expect(chest).toMatchObject({ type: 'Lock', steps: [], graphicKey: 'builtin:chest' })

    // A new door leads to another scene straight away, and opens once the scene's puzzles are solved.
    const door = createObject(asset('builtin:door'), 10, 20, document, 'hall') as EditorDoor
    expect(door).toMatchObject({ targetSceneId: 'library', openCondition: 'AllInScene', requiredLockIds: [] })
    expect(createObject(asset('builtin:exit-door'), 10, 20, document, 'hall')).toMatchObject({
      type: 'ExitDoor',
      openCondition: 'AllInScene',
    })
    expect(createObject(asset('builtin:letter'), 10, 20, document, 'hall')).toMatchObject({ type: 'Letter', text: '' })
  })

  it('removing an object makes whatever waited for it visible from start, and doors stop waiting for it', () => {
    const letter: EditorObject = { ...createObject(asset('builtin:letter'), 50, 50, editorDocument(), 'hall'), unlockedById: 'lock' }
    let document = addObject(editorDocument(), 'hall', letter)
    document = updateObject<EditorDoor>(document, 'door', (d) => ({
      ...d,
      openCondition: 'SelectedLocks',
      requiredLockIds: ['lock', 'other'],
    }))

    const without = removeObject(document, 'lock')

    expect(findObject(without, 'lock')).toBeUndefined()
    expect(findObject(without, letter.id)?.object.unlockedById).toBeNull()
    expect((findObject(without, 'door')?.object as EditorDoor).requiredLockIds).toEqual(['other'])
  })

  it('names new scenes "Scene 3", never with a name the room already has', () => {
    const name = (n: number) => `Scene ${String(n)}`
    const document = editorDocument()

    expect(createScene(document, name).name).toBe('Scene 3')
    const withSceneThree = addScene(document, { ...createScene(document, name), name: 'Scene 3' })
    expect(createScene(withSceneThree, name).name).toBe('Scene 4')
    const renamed = updateScene(withSceneThree, 'hall', { name: 'Scene 4' })
    expect(createScene(renamed, name).name).toBe('Scene 5')
  })

  it('moves scenes in the room order', () => {
    const document = addScene(editorDocument(), createScene(editorDocument(), () => 'Kælderen'))

    expect(moveScene(document, 2, 0).scenes.map((s) => s.name)).toEqual(['Kælderen', 'Gangen', 'Biblioteket'])
    expect(moveScene(document, 0, 0)).toBe(document)
    expect(moveScene(document, 0, 5)).toBe(document)
  })

  it('lists the locks a door can open with, those in its own scene first', () => {
    const libraryLock = { ...createObject(asset('builtin:padlock'), 50, 50, editorDocument(), 'library'), id: 'library-lock' }
    const document = addObject(editorDocument(), 'library', libraryLock)

    expect(lockChoices(document, 'library').map((l) => l.id)).toEqual(['library-lock', 'lock'])
    expect(lockChoices(document, 'hall').map((l) => l.id)).toEqual(['lock', 'library-lock'])
  })

  it('removing a scene clears doors into it and moves the start scene, but never removes the last scene', () => {
    const library = createScene(editorDocument(), () => 'Kælderen')
    let document = addScene(editorDocument(), library)
    const waiting: EditorObject = { ...createObject(asset('builtin:letter'), 50, 50, document, 'library'), unlockedById: 'lock' }
    document = addObject(document, 'library', waiting)

    const withoutLibrary = removeScene(document, 'library')
    expect(withoutLibrary.scenes.map((s) => s.name)).toEqual(['Gangen', 'Kælderen'])
    expect((findObject(withoutLibrary, 'door')?.object as EditorDoor).targetSceneId).toBeNull()

    const withoutHall = removeScene(document, 'hall')
    expect(withoutHall.startSceneId).toBe('library')
    expect(findObject(withoutHall, waiting.id)?.object.unlockedById).toBeNull()

    const single = removeScene(removeScene(withoutLibrary, library.id), 'unknown')
    expect(removeScene(single, 'hall')).toBe(single)
  })

  it('a new scene gets a background the room does not use yet', () => {
    const scene = createScene(editorDocument(), () => 'Ny')

    expect(['builtin:classroom', 'builtin:library']).not.toContain(scene.backgroundImageKey)
  })

  it('offers only locks and doors to wait for, never the object itself or something that waits for it', () => {
    const document = editorDocument()
    const letter = createObject(asset('builtin:letter'), 50, 50, document, 'hall')
    const second = { ...createObject(asset('builtin:padlock'), 50, 50, document, 'hall'), id: 'second', unlockedById: 'lock' }
    const withObjects = addObject(addObject(document, 'hall', letter), 'hall', second)

    // The second lock waits for the first, and the door opens with the scene's locks: the first lock waits for neither.
    expect(unlockedByChoices(withObjects, 'lock').map((o) => o.id)).toEqual([])
    expect(unlockedByChoices(withObjects, letter.id).map((o) => o.id)).toEqual(['lock', 'door', 'second'])
    // Doors open by their own condition and never wait.
    expect(unlockedByChoices(withObjects, 'door')).toEqual([])

    // A door that opens with another lock can be waited for.
    const opensWithSecond = updateObject<EditorDoor>(withObjects, 'door', (d) => ({
      ...d,
      openCondition: 'SelectedLocks',
      requiredLockIds: ['second'],
    }))
    expect(unlockedByChoices(opensWithSecond, 'lock').map((o) => o.id)).toEqual([])
    expect(unlockedByChoices(removeObject(opensWithSecond, 'second'), 'lock').map((o) => o.id)).toEqual(['door'])
  })

  it('creates empty text boxes that can wait for a lock, but that nothing can wait for', () => {
    const textBox = createTextBox(120, 30)
    expect(textBox).toMatchObject({ type: 'TextBox', text: '', x: 100, y: 30, graphicKey: 'builtin:text-box' })

    const document = addObject(editorDocument(), 'hall', textBox)
    expect(unlockedByChoices(document, textBox.id).map((o) => o.id)).toEqual(['lock', 'door'])
    expect(unlockedByChoices(document, 'lock').map((o) => o.id)).not.toContain(textBox.id)
  })

  it('waits with saving while the title or a scene name is empty', () => {
    expect(missingRequiredText(editorDocument())).toBe(false)
    expect(missingRequiredText({ ...editorDocument(), title: '  ' })).toBe(true)
  })
})

describe('new ids', () => {
  it('are UUID version 7 and sort by the time they were made', () => {
    const first = newId(1_700_000_000_000)
    const second = newId(1_700_000_000_001)

    expect(first).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(first < second).toBe(true)
    expect(newId()).not.toBe(newId())
  })

  it('for items to put in order are random (version 4), so they never give the order away', () => {
    expect(randomId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(randomId()).not.toBe(randomId())
  })
})
