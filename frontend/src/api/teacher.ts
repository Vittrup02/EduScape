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
