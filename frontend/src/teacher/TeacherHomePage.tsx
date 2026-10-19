import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { getMyRooms, type RoomSummary } from '../api/teacher'
import { errorKey } from '../i18n/errors'

type RoomsState =
  | { status: 'loading' }
  | { status: 'loaded'; rooms: RoomSummary[] }
  | { status: 'failed'; code: string }

/** The teacher's start page: their own rooms. Loading it also wakes the backend. */
export function TeacherHomePage() {
  const { t } = useTranslation()
  const [state, setState] = useState<RoomsState>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    getMyRooms(controller.signal).then(
      (rooms) => {
        setState({ status: 'loaded', rooms })
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setState({ status: 'failed', code: error instanceof ApiError ? error.code : 'UNKNOWN' })
        }
      },
    )
    return () => {
      controller.abort()
    }
  }, [])

  return (
    <main>
      <h1>{t('teacher.roomsTitle')}</h1>
      {state.status === 'loading' && (
        <p role="status" className="status">
          {t('teacher.loadingRooms')}
        </p>
      )}
      {state.status === 'failed' && (
        <p role="alert" className="error">
          {t(errorKey(state.code))}
        </p>
      )}
      {state.status === 'loaded' && state.rooms.length === 0 && <p>{t('teacher.noRooms')}</p>}
      {state.status === 'loaded' && state.rooms.length > 0 && (
        <ul className="room-list">
          {state.rooms.map((room) => (
            <li key={room.id}>
              <span className="room-title">{room.title}</span>
              <span className="room-details">
                {t('teacher.roomDetails', {
                  subject: t(`subjects.${room.subject}`),
                  gradeLevel: t(`gradeLevels.${room.gradeLevel}`),
                })}
              </span>
            </li>
          ))}
        </ul>
      )}
    </main>
  )
}
