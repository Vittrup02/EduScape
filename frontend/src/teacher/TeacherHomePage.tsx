import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { ApiError } from '../api/client'
import { copyTemplate } from '../api/editor'
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

/** One room with its actions; `startPath` is the start page for this room. */
function RoomItem({ room, startPath, children }: { room: RoomSummary; startPath: string; children?: ReactNode }) {
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
      <div className="room-actions">
        {children}
        <Link className="button room-action" to={startPath} aria-label={`${t('teacher.startSession')}: ${room.title}`}>
          {t('teacher.startSession')}
        </Link>
      </div>
    </li>
  )
}

/** Copies the template and opens the copy in the editor; the template itself stays as it is. */
function UseTemplateButton({ template }: { template: RoomSummary }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  return (
    <>
      <button
        type="button"
        className="primary room-action"
        disabled={busy}
        aria-label={`${t('teacher.useTemplate')}: ${template.title}`}
        onClick={() => {
          setBusy(true)
          setErrorCode(null)
          copyTemplate(template.id)
            .then((copy) => navigate(`/teacher/rooms/${copy.id}/edit`))
            .catch((error: unknown) => {
              setErrorCode(error instanceof ApiError ? error.code : 'UNKNOWN')
              setBusy(false)
            })
        }}
      >
        {busy ? t('teacher.copyingTemplate') : t('teacher.useTemplate')}
      </button>
      {errorCode !== null && (
        <p role="alert" className="error">
          {t(errorKey(errorCode))}
        </p>
      )}
    </>
  )
}

/** The teacher's start page: running sessions, their own rooms and the templates. Loading it also wakes the backend. */
export function TeacherHomePage() {
  const { t } = useTranslation()
  const [state] = useLoad(loadHome)

  return (
    <main>
      <div className="section-header">
        <h1>{t('teacher.roomsTitle')}</h1>
        <Link className="button primary" to="/teacher/rooms/new">
          {t('teacher.newRoom')}
        </Link>
      </div>
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
                <RoomItem key={room.id} room={room} startPath={`/teacher/rooms/${room.id}/start`}>
                  <Link
                    className="button room-action"
                    to={`/teacher/rooms/${room.id}/edit`}
                    aria-label={`${t('teacher.editRoom')}: ${room.title}`}
                  >
                    {t('teacher.editRoom')}
                  </Link>
                </RoomItem>
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
            <>
              <p className="room-details">{t('teacher.templatesIntro')}</p>
              <ul className="room-list" aria-label={t('teacher.templatesTitle')}>
                {state.data.templates.map((room) => (
                  <RoomItem key={room.id} room={room} startPath={`/teacher/templates/${room.id}/start`}>
                    <UseTemplateButton template={room} />
                  </RoomItem>
                ))}
              </ul>
            </>
          )}
        </>
      )}
    </main>
  )
}
