import type { EditorDoor, EditorLock, EditorRoom, EditorStep, RoomDocument } from '../api/editor'

/** A step with the editor's defaults; `overrides` fills in what the test is about. */
export function editorStep(overrides: Partial<EditorStep> = {}): EditorStep {
  return {
    id: 'step',
    question: 'Hvad er 3 · 4?',
    answerType: 'Code',
    correctAnswer: '12',
    tolerance: null,
    hint: null,
    options: [],
    acceptedAnswers: [],
    acceptTypos: false,
    orderItems: [],
    ...overrides,
  }
}

/** A room as the editor gets it: a hall with a lock and a door to the library that opens with the lock. */
export function editorRoom(overrides: Partial<EditorRoom> = {}): EditorRoom {
  const lock: EditorLock = {
    id: 'lock',
    type: 'Lock',
    x: 30,
    y: 40,
    graphicKey: 'builtin:padlock',
    unlockedById: null,
    steps: [editorStep()],
  }
  const door: EditorDoor = {
    id: 'door',
    type: 'Door',
    x: 80,
    y: 55,
    graphicKey: 'builtin:door',
    unlockedById: null,
    targetSceneId: 'library',
    openCondition: 'AllInScene',
    requiredLockIds: [],
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
    wrongAnswerCooldownSeconds: 15,
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
    wrongAnswerCooldownSeconds: room.wrongAnswerCooldownSeconds,
  }
}

/**
 * The room as the API sends it: fields that don't apply to an object's type are null, and a step's options are only
 * their texts.
 */
export function editorRoomJson(room: EditorRoom = editorRoom()): unknown {
  const nulls = { steps: null, text: null, targetSceneId: null, openCondition: null, requiredLockIds: null }
  return {
    ...room,
    scenes: room.scenes.map((scene) => ({
      ...scene,
      objects: scene.objects.map((object) =>
        object.type === 'Lock'
          ? {
              ...nulls,
              ...object,
              steps: object.steps.map((step) => ({ ...step, options: step.options.map((o) => o.text) })),
            }
          : { ...nulls, ...object },
      ),
    })),
  }
}
