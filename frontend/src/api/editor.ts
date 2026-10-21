import { assetsOfType, findAsset } from '../assets/registry'
import { objectAsset } from '../assets/sceneLayout'
import { randomId } from '../editor/ids'
import { teacherFetch } from './auth'
import type { AnswerType } from './play'
import type { GradeLevel, RoomSummary, Subject } from './teacher'

/**
 * The room editor's calls. The editor holds the whole room as one document and saves all of it at once; the server
 * keeps scenes and objects by the ids the editor gave them, so undo can bring back anything exactly as it was.
 */

interface EditorObjectBase {
  id: string
  /** Centre of the object, in percent of the scene's width and height. */
  x: number
  y: number
  /** A built-in asset key, e.g. "builtin:padlock". */
  graphicKey: string
  /** The lock or door that must be completed first; null means visible from start. Doors never wait. */
  unlockedById: string | null
}

/** A multiple-choice option. The id only exists in the editor, so options can be sorted; the server keeps the texts. */
export interface EditorOption {
  id: string
  text: string
}

/** Something students put in order. The id is random (UUID v4), so it never gives the correct order away. */
export interface EditorOrderItem {
  id: string
  text: string
  /** An uploaded image, shown with the text; null when there is none. */
  imageKey: string | null
  /** A short-lived link to the image, for the editor to show it. */
  imageUrl: string | null
}

/**
 * One step of a lock's puzzle, with the correct answer. Settings that don't apply to the answer type (options,
 * alternatives, items) are kept while the teacher switches type, so switching back restores them; only those that
 * apply are saved.
 */
export interface EditorStep {
  id: string
  question: string
  answerType: AnswerType
  correctAnswer: string
  /** Accepted deviation for numeric Text answers, e.g. 1.0. */
  tolerance: number | null
  hint: string | null
  /** The choices of a MultipleChoice step; the correct one has the text of `correctAnswer`. */
  options: EditorOption[]
  /** Other answers that are also right (synonyms), for Text steps. */
  acceptedAnswers: string[]
  /** Forgive small spelling mistakes in Text answers. */
  acceptTypos: boolean
  /** The items of an Order step, in the correct order. */
  orderItems: EditorOrderItem[]
}

/**
 * A lock: a graphic and a puzzle of one or more steps, answered in order. `steps` is empty until the teacher picks the
 * kind of puzzle. Never sent to students.
 */
export interface EditorLock extends EditorObjectBase {
  type: 'Lock'
  steps: EditorStep[]
}

export interface EditorLetter extends EditorObjectBase {
  type: 'Letter'
  text: string
}

/** When a door opens by itself: once every lock in its scene is solved, or the locks the teacher chose. */
export type DoorOpenCondition = 'AllInScene' | 'SelectedLocks'

interface EditorDoorwayBase extends EditorObjectBase {
  openCondition: DoorOpenCondition
  /** The locks that open it with SelectedLocks. */
  requiredLockIds: string[]
}

export interface EditorDoor extends EditorDoorwayBase {
  type: 'Door'
  targetSceneId: string | null
}

export interface EditorExitDoor extends EditorDoorwayBase {
  type: 'ExitDoor'
}

export type EditorDoorway = EditorDoor | EditorExitDoor

/** Text written straight onto the scene. It has no graphic to choose; its key is always `textBoxKey`. */
export interface EditorTextBox extends EditorObjectBase {
  type: 'TextBox'
  text: string
}

export type EditorObject = EditorLock | EditorLetter | EditorDoor | EditorExitDoor | EditorTextBox

export const textBoxKey = 'builtin:text-box'

export interface EditorScene {
  id: string
  name: string
  backgroundImageKey: string
  /** A short-lived link to an uploaded background; null for built-in ones. */
  backgroundImageUrl: string | null
  /** The story text students see when they enter the scene; empty when there is none. */
  narration: string
  objects: EditorObject[]
}

/** A problem that keeps the room from being played, e.g. DOOR_TARGET_MISSING for a door. */
export interface ValidationError {
  code: string
  objectId: string | null
  /** The step of a lock the problem is in; null otherwise. */
  stepIndex: number | null
}

export interface EditorRoom {
  id: string
  title: string
  subject: Subject
  gradeLevel: GradeLevel
  /** Sent back with each save; a save based on an older revision fails with ROOM_CHANGED. */
  revision: number
  startSceneId: string
  scenes: EditorScene[]
  validationErrors: ValidationError[]
  /** False when the server has no image storage; the editor then offers only built-in images. */
  uploadsEnabled: boolean
  /** How long a lock waits for the team after a wrong answer; 0 turns it off. */
  wrongAnswerCooldownSeconds: number
}

/** The part of the room the teacher edits, and that undo and redo move through. */
export type RoomDocument = Pick<
  EditorRoom,
  'title' | 'subject' | 'gradeLevel' | 'startSceneId' | 'scenes' | 'wrongAnswerCooldownSeconds'
>

export interface SaveResult {
  revision: number
  validationErrors: ValidationError[]
}

/** What a new room waits after a wrong answer, as on the server. */
export const defaultCooldownSeconds = 15

interface StepJson {
  id: string
  question: string
  answerType: AnswerType
  correctAnswer: string | null
  tolerance: number | null
  hint: string | null
  options: string[] | null
  acceptedAnswers: string[] | null
  acceptTypos: boolean
  orderItems: { id: string; text: string; imageKey: string | null; imageUrl?: string | null }[] | null
}

/** The object fields as the API sends them: whatever does not apply to the type is null. */
interface ObjectJson {
  id: string
  type: EditorObject['type']
  x: number
  y: number
  graphicKey: string
  unlockedById: string | null
  steps: StepJson[] | null
  text: string | null
  targetSceneId: string | null
  openCondition: DoorOpenCondition | null
  requiredLockIds: string[] | null
}

interface RoomJson extends Omit<EditorRoom, 'scenes' | 'startSceneId' | 'validationErrors'> {
  startSceneId: string | null
  scenes: (Omit<EditorScene, 'objects' | 'narration'> & { narration: string | null; objects: ObjectJson[] })[]
  validationErrors: (Omit<ValidationError, 'stepIndex'> & { stepIndex?: number | null })[]
}

const jsonHeaders = { 'Content-Type': 'application/json' }

/** Keys of images the server has processed; anything else must be a built-in asset. */
const uploadKeyPattern = /^uploads\/[0-9a-f]{32}\.webp$/

/**
 * The background the editor shows and saves. A key that is neither built in nor an upload (e.g. from an old room)
 * becomes the default background, so the room can still be saved; the play view would show that default too.
 */
function knownBackground(key: string): string {
  return findAsset(key)?.type === 'Background' || uploadKeyPattern.test(key) ? key : assetsOfType('Background')[0].key
}

function options(texts: string[]): EditorOption[] {
  return texts.map((text) => ({ id: randomId(), text }))
}

function stepFromJson(step: StepJson): EditorStep {
  return {
    id: step.id,
    question: step.question,
    answerType: step.answerType,
    correctAnswer: step.correctAnswer ?? '',
    tolerance: step.tolerance,
    hint: step.hint,
    options: options(step.options ?? []),
    acceptedAnswers: step.acceptedAnswers ?? [],
    acceptTypos: step.acceptTypos,
    orderItems: (step.orderItems ?? []).map((item) => ({
      id: item.id,
      text: item.text,
      imageKey: item.imageKey,
      imageUrl: item.imageUrl ?? null,
    })),
  }
}

function stepToJson(step: EditorStep): StepJson {
  const type = step.answerType
  return {
    id: step.id,
    question: step.question,
    answerType: type,
    correctAnswer: type === 'Order' ? null : step.correctAnswer,
    tolerance: type === 'Text' ? step.tolerance : null,
    hint: step.hint?.trim() ? step.hint : null,
    options: type === 'MultipleChoice' ? step.options.map((o) => o.text) : null,
    acceptedAnswers: type === 'Text' ? step.acceptedAnswers.filter((a) => a.trim() !== '') : null,
    acceptTypos: type === 'Text' && step.acceptTypos,
    orderItems:
      type === 'Order' ? step.orderItems.map((item) => ({ id: item.id, text: item.text, imageKey: item.imageKey })) : null,
  }
}

function fromJson(object: ObjectJson): EditorObject {
  const base = {
    id: object.id,
    x: object.x,
    y: object.y,
    // Like the play view, an unknown graphic shows (and is now saved as) the type's default.
    graphicKey: object.type === 'TextBox' ? textBoxKey : objectAsset(object.type, object.graphicKey).key,
    unlockedById: object.unlockedById,
  }
  const doorway = {
    openCondition: object.openCondition ?? 'AllInScene',
    requiredLockIds: object.requiredLockIds ?? [],
  }
  switch (object.type) {
    case 'Lock':
      return { ...base, type: 'Lock', steps: (object.steps ?? []).map(stepFromJson) }
    case 'Letter':
      return { ...base, type: 'Letter', text: object.text ?? '' }
    case 'Door':
      return { ...base, ...doorway, type: 'Door', targetSceneId: object.targetSceneId }
    case 'ExitDoor':
      return { ...base, ...doorway, type: 'ExitDoor' }
    case 'TextBox':
      return { ...base, type: 'TextBox', text: object.text ?? '' }
  }
}

function toJson(object: EditorObject): Partial<ObjectJson> {
  const base = {
    id: object.id,
    type: object.type,
    x: object.x,
    y: object.y,
    graphicKey: object.graphicKey,
    unlockedById: object.unlockedById,
  }
  switch (object.type) {
    case 'Lock':
      return { ...base, steps: object.steps.map(stepToJson) }
    case 'Letter':
      return { ...base, text: object.text }
    case 'Door':
    case 'ExitDoor':
      return {
        ...base,
        unlockedById: null,
        targetSceneId: object.type === 'Door' ? object.targetSceneId : null,
        openCondition: object.openCondition,
        requiredLockIds: object.openCondition === 'SelectedLocks' ? object.requiredLockIds : [],
      }
    case 'TextBox':
      return { ...base, text: object.text }
  }
}

/** Creates a room with one scene, named in the teacher's language. Returns it for the editor to open. */
export async function createRoom(
  title: string,
  subject: Subject,
  gradeLevel: GradeLevel,
  sceneName: string,
): Promise<RoomSummary> {
  const response = await teacherFetch('/api/teacher/rooms', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ title, subject, gradeLevel, sceneName }),
  })
  return (await response.json()) as RoomSummary
}

/** Copies a template into a room of the teacher's own, which the editor then opens. */
export async function copyTemplate(templateId: string): Promise<RoomSummary> {
  const response = await teacherFetch(`/api/teacher/templates/${encodeURIComponent(templateId)}/copy`, {
    method: 'POST',
  })
  return (await response.json()) as RoomSummary
}

function validationErrors(errors: RoomJson['validationErrors']): ValidationError[] {
  return errors.map((error) => ({ ...error, stepIndex: error.stepIndex ?? null }))
}

export async function getEditorRoom(roomId: string, signal?: AbortSignal): Promise<EditorRoom> {
  const response = await teacherFetch(`/api/teacher/rooms/${encodeURIComponent(roomId)}/editor`, { signal })
  const room = (await response.json()) as RoomJson
  return {
    ...room,
    startSceneId: room.startSceneId ?? room.scenes[0].id,
    validationErrors: validationErrors(room.validationErrors),
    scenes: room.scenes.map((scene) => ({
      ...scene,
      backgroundImageKey: knownBackground(scene.backgroundImageKey),
      narration: scene.narration ?? '',
      objects: scene.objects.map(fromJson),
    })),
  }
}

/** Throws an ApiError, e.g. ROOM_CHANGED when the room was saved elsewhere in the meantime. */
export async function saveEditorRoom(roomId: string, revision: number, document: RoomDocument): Promise<SaveResult> {
  const response = await teacherFetch(`/api/teacher/rooms/${encodeURIComponent(roomId)}/editor`, {
    method: 'PUT',
    headers: jsonHeaders,
    body: JSON.stringify({
      revision,
      title: document.title,
      subject: document.subject,
      gradeLevel: document.gradeLevel,
      startSceneId: document.startSceneId,
      wrongAnswerCooldownSeconds: document.wrongAnswerCooldownSeconds,
      scenes: document.scenes.map((scene) => ({
        id: scene.id,
        name: scene.name,
        backgroundImageKey: scene.backgroundImageKey,
        narration: scene.narration.trim() === '' ? null : scene.narration,
        objects: scene.objects.map(toJson),
      })),
    }),
  })
  const saved = (await response.json()) as Omit<SaveResult, 'validationErrors'> & Pick<RoomJson, 'validationErrors'>
  return { revision: saved.revision, validationErrors: validationErrors(saved.validationErrors) }
}

/**
 * Starts a preview of the saved room and returns the device token to play it with. With `startSceneId` it starts in
 * that scene, with the locks before it solved; without, from the beginning. Throws ROOM_INVALID if unfinished.
 */
export async function startPreview(roomId: string, startSceneId: string | null = null): Promise<string> {
  const response = await teacherFetch(`/api/teacher/rooms/${encodeURIComponent(roomId)}/preview`, {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ startSceneId }),
  })
  return ((await response.json()) as { deviceToken: string }).deviceToken
}

/** The types and size the server accepts; it checks again, these only give quicker feedback. */
export const uploadContentTypes = ['image/jpeg', 'image/png', 'image/webp'] as const
export const maxUploadBytes = 10 * 1024 * 1024

export interface UploadedImage {
  imageKey: string
  imageUrl: string
}

/**
 * Uploads an image (a background, or the picture of an item to put in order): the file goes straight to storage
 * through a presigned URL, then the server checks it, removes its metadata and stores a copy under a generated name.
 * Throws an ApiError, e.g. IMAGE_TYPE_NOT_ALLOWED.
 */
export async function uploadImage(file: File): Promise<UploadedImage> {
  const started = await teacherFetch('/api/teacher/uploads', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ contentType: file.type, size: file.size }),
  })
  const { uploadId, url } = (await started.json()) as { uploadId: string; url: string }

  // No Authorization header: the signature in the URL is the permission.
  const put = await fetch(url, { method: 'PUT', headers: { 'Content-Type': file.type }, body: file })
  if (!put.ok) {
    throw new Error(`Upload failed with ${String(put.status)}`)
  }

  const completed = await teacherFetch(`/api/teacher/uploads/${encodeURIComponent(uploadId)}/complete`, {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ contentType: file.type }),
  })
  return (await completed.json()) as UploadedImage
}
