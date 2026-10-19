import type { EditorDoor, EditorLock, EditorRoom, RoomDocument } from '../api/editor'

/** A room as the editor gets it: a hall with a lock and a door to the library (after the lock). */
export function editorRoom(overrides: Partial<EditorRoom> = {}): EditorRoom {
  const lock: EditorLock = {
    id: 'lock',
    type: 'Lock',
    x: 30,
    y: 40,
    graphicKey: 'builtin:padlock',
    unlockedById: null,
    question: 'Hvad er 3 · 4?',
    answerType: 'Code',
    correctAnswer: '12',
    tolerance: null,
    hint: null,
    options: [],
  }
  const door: EditorDoor = {
    id: 'door',
    type: 'Door',
    x: 80,
    y: 55,
    graphicKey: 'builtin:door',
    unlockedById: 'lock',
    targetSceneId: 'library',
  }
  return {
    id: 'room',
    title: 'Brøker på flugt',
    subject: 'Mathematics',
    gradeLevel: 'Grade6',
    revision: 3,
    startSceneId: 'hall',
    scenes: [
      { id: 'hall', name: 'Gangen', backgroundImageKey: 'builtin:classroom', backgroundImageUrl: null, narration: '', objects: [lock, door] },
      { id: 'library', name: 'Biblioteket', backgroundImageKey: 'builtin:library', backgroundImageUrl: null, narration: '', objects: [] },
    ],
    validationErrors: [],
    uploadsEnabled: false,
    ...overrides,
  }
}

export function editorDocument(room: EditorRoom = editorRoom()): RoomDocument {
  return {
    title: room.title,
    subject: room.subject,
    gradeLevel: room.gradeLevel,
    startSceneId: room.startSceneId,
    scenes: room.scenes,
  }
}

/** The room as the API sends it: fields that don't apply to an object's type are null. */
export function editorRoomJson(room: EditorRoom = editorRoom()): unknown {
  const nulls = {
    question: null,
    answerType: null,
    correctAnswer: null,
    tolerance: null,
    hint: null,
    options: null,
    text: null,
    targetSceneId: null,
  }
  return {
    ...room,
    scenes: room.scenes.map((scene) => ({
      ...scene,
      objects: scene.objects.map((object) => ({ ...nulls, ...object })),
    })),
  }
}
