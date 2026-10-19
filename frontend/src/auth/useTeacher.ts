import { useSyncExternalStore } from 'react'
import { currentTeacher, onAuthChange, type Teacher } from '../api/auth'

/** The signed-in teacher, or null. Re-renders on login and logout, also from other tabs. */
export function useTeacher(): Teacher | null {
  return useSyncExternalStore(onAuthChange, currentTeacher)
}
