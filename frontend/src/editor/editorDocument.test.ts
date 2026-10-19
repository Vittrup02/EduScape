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
  missingRequiredText,
  moveObject,
  removeObject,
  removeScene,
  unlockedByChoices,
  updateObject,
} from './editorDocument'
import { newId } from './ids'

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

    const codeLock = createObject(asset('builtin:code-lock'), 10, 20, document, 'hall') as EditorLock
    expect(codeLock).toMatchObject({ type: 'Lock', answerType: 'Code', question: '', graphicKey: 'builtin:code-lock' })
    expect((createObject(asset('builtin:padlock'), 10, 20, document, 'hall') as EditorLock).answerType).toBe('Text')

    // A new door leads to another scene straight away.
    const door = createObject(asset('builtin:door'), 10, 20, document, 'hall') as EditorDoor
    expect(door.targetSceneId).toBe('library')
    expect(createObject(asset('builtin:letter'), 10, 20, document, 'hall')).toMatchObject({ type: 'Letter', text: '' })
  })

  it('removing an object makes whatever waited for it visible from start', () => {
    const document = removeObject(editorDocument(), 'lock')

    expect(findObject(document, 'lock')).toBeUndefined()
    expect(findObject(document, 'door')?.object.unlockedById).toBeNull()
  })

  it('removing a scene clears doors into it and moves the start scene, but never removes the last scene', () => {
    const library = createScene(editorDocument(), 'Kælderen')
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
    const scene = createScene(editorDocument(), 'Ny')

    expect(['builtin:classroom', 'builtin:library']).not.toContain(scene.backgroundImageKey)
  })

  it('offers only locks and doors to wait for, never the object itself or something that waits for it', () => {
    const document = editorDocument()
    const letter = createObject(asset('builtin:letter'), 50, 50, document, 'hall')
    const withLetter = addObject(document, 'hall', letter)

    // The door waits for the lock, so the lock may not wait for the door.
    expect(unlockedByChoices(withLetter, 'lock').map((o) => o.id)).toEqual([])
    expect(unlockedByChoices(withLetter, 'door').map((o) => o.id)).toEqual(['lock'])
    expect(unlockedByChoices(withLetter, letter.id).map((o) => o.id)).toEqual(['lock', 'door'])
  })

  it('creates empty text boxes that can wait for a lock, but that nothing can wait for', () => {
    const textBox = createTextBox(120, 30)
    expect(textBox).toMatchObject({ type: 'TextBox', text: '', x: 100, y: 30, graphicKey: 'builtin:text-box' })

    const document = addObject(editorDocument(), 'hall', textBox)
    expect(unlockedByChoices(document, textBox.id).map((o) => o.id)).toEqual(['lock', 'door'])
    expect(unlockedByChoices(document, 'door').map((o) => o.id)).not.toContain(textBox.id)
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
})
