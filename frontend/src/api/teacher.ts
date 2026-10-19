import type { Translation } from '../i18n/languages'
import { teacherFetch } from './auth'

/** Fixed lists arrive as keys (e.g. "Mathematics") and are translated with t(`subjects.${subject}`). */
export type Subject = keyof Translation['subjects']
export type GradeLevel = keyof Translation['gradeLevels']

export interface RoomSummary {
  id: string
  title: string
  subject: Subject
  gradeLevel: GradeLevel
}

/** The signed-in teacher's own rooms, sorted by title. */
export async function getMyRooms(signal?: AbortSignal): Promise<RoomSummary[]> {
  const response = await teacherFetch('/api/teacher/rooms', { signal })
  return (await response.json()) as RoomSummary[]
}

/** Shared templates, which every teacher can start a session with. */
export async function getTemplates(signal?: AbortSignal): Promise<RoomSummary[]> {
  const response = await teacherFetch('/api/teacher/templates', { signal })
  return (await response.json()) as RoomSummary[]
}

/** A room a session can be started with: one of the teacher's own rooms, or a template. */
export type RoomKind = 'room' | 'template'

export async function getRoom(kind: RoomKind, id: string, signal?: AbortSignal): Promise<RoomSummary> {
  const collection = kind === 'room' ? 'rooms' : 'templates'
  const response = await teacherFetch(`/api/teacher/${collection}/${encodeURIComponent(id)}`, { signal })
  return (await response.json()) as RoomSummary
}
