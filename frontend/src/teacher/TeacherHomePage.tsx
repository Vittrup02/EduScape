import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { getActiveSessions, type SessionSummary } from '../api/sessions'
import { getMyRooms, getTemplates, type RoomSummary } from '../api/teacher'
import { useLoad } from '../api/useLoad'
import { errorKey } from '../i18n/errors'

interface HomeData {
  rooms: RoomSummary[]
  templates: RoomSummary[]
  sessions: SessionSummary[]
}

async function loadHome(signal: AbortSignal): Promise<HomeData> {
  const [rooms, templates, sessions] = await Promise.all([
    getMyRooms(signal),
    getTemplates(signal),
    getActiveSessions(signal),
  ])
  return { rooms, templates, sessions }
}

/** One room with a button to start a session; `startPath` is the start page for this room. */
function RoomItem({ room, startPath }: { room: RoomSummary; startPath: string }) {
  const { t } = useTranslation()
  return (
    <li>
      <span className="room-title">{room.title}</span>
      <span className="room-details">
        {t('teacher.roomDetails', {
          subject: t(`subjects.${room.subject}`),
          gradeLevel: t(`gradeLevels.${room.gradeLevel}`),
        })}
      </span>
      <Link className="button room-action" to={startPath} aria-label={`${t('teacher.startSession')}: ${room.title}`}>
        {t('teacher.startSession')}
      </Link>
    </li>
  )
}

/** The teacher's start page: running sessions, their own rooms and the templates. Loading it also wakes the backend. */
export function TeacherHomePage() {
  const { t } = useTranslation()
  const [state] = useLoad(loadHome)

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
      {state.status === 'loaded' && (
        <>
          {state.data.rooms.length === 0 ? (
            <p>{t('teacher.noRooms')}</p>
          ) : (
            <ul className="room-list" aria-label={t('teacher.roomsTitle')}>
              {state.data.rooms.map((room) => (
                <RoomItem key={room.id} room={room} startPath={`/teacher/rooms/${room.id}/start`} />
              ))}
            </ul>
          )}

          {state.data.sessions.length > 0 && (
            <section aria-labelledby="active-sessions">
              <h2 id="active-sessions">{t('teacher.activeSessionsTitle')}</h2>
              <ul className="room-list">
                {state.data.sessions.map((session) => (
                  <li key={session.id}>
                    <Link className="room-title" to={`/teacher/sessions/${session.id}`}>
                      {session.roomTitle}
                    </Link>
                    <span className="room-details">
                      {t('teacher.activeSessionDetails', { className: session.className, pin: session.pin })}
                    </span>
                  </li>
                ))}
              </ul>
            </section>
          )}

          <h2>{t('teacher.templatesTitle')}</h2>
          {state.data.templates.length === 0 ? (
            <p>{t('teacher.noTemplates')}</p>
          ) : (
            <ul className="room-list" aria-label={t('teacher.templatesTitle')}>
              {state.data.templates.map((room) => (
                <RoomItem key={room.id} room={room} startPath={`/teacher/templates/${room.id}/start`} />
              ))}
            </ul>
          )}
        </>
      )}
    </main>
  )
}
