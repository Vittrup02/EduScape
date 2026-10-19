import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { useTeacher } from './useTeacher'

/** Location state passed to the login page, so the teacher returns to where they were going. */
export interface LoginRedirectState {
  from?: string
}

/** Shows its children only to a signed-in teacher; everyone else is sent to the login page. */
export function RequireTeacher({ children }: { children: ReactNode }) {
  const teacher = useTeacher()
  const location = useLocation()

  if (!teacher) {
    const state: LoginRedirectState = { from: location.pathname }
    return <Navigate to="/login" replace state={state} />
  }
  return children
}
