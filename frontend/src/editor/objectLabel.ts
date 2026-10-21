import type { TFunction } from 'i18next'
import type { EditorObject, RoomDocument } from '../api/editor'
import { allObjects, findScene } from './editorDocument'

const maxQuestionLength = 40

function shorten(text: string): string {
  const trimmed = text.trim()
  return trimmed.length > maxQuestionLength ? `${trimmed.slice(0, maxQuestionLength - 1)}…` : trimmed
}

/** The number of the object among the room's objects of its type, counting from 1, e.g. "Lås 2". */
function numberOf(document: RoomDocument, object: EditorObject): number {
  return allObjects(document).filter((o) => o.type === object.type).findIndex((o) => o.id === object.id) + 1
}

/**
 * How the editor names an object to the teacher: a lock by its (first) question, a door by where it leads. Unfinished
 * objects get a number instead, so two of them can still be told apart.
 */
export function objectLabel(t: TFunction, document: RoomDocument, object: EditorObject): string {
  switch (object.type) {
    case 'Lock': {
      const question = object.steps[0]?.question.trim() ?? ''
      return question === ''
        ? t('editor.object.lockNumbered', { n: numberOf(document, object) })
        : t('editor.object.lock', { question: shorten(question) })
    }
    case 'Letter':
      return object.text.trim() === ''
        ? t('editor.object.letterNumbered', { n: numberOf(document, object) })
        : t('editor.object.letter', { text: shorten(object.text) })
    case 'Door': {
      const target = object.targetSceneId === null ? undefined : findScene(document, object.targetSceneId)
      return target === undefined
        ? t('editor.object.doorNumbered', { n: numberOf(document, object) })
        : t('editor.object.door', { scene: target.name })
    }
    case 'ExitDoor':
      return t('editor.object.exitDoor')
    case 'TextBox':
      return object.text.trim() === ''
        ? t('editor.object.textBoxNumbered', { n: numberOf(document, object) })
        : t('editor.object.textBox', { text: shorten(object.text) })
  }
}
