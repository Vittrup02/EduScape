import { useCallback, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { ApiError } from '../api/client'
import { changeSession, getSession, type SessionAction, type SessionDetails } from '../api/sessions'
import { useLoad } from '../api/useLoad'
import { errorKey } from '../i18n/errors'
import { formatDateTime, teamName } from '../i18n/format'

const statusKeys = {
  Open: 'session.statusOpen',
  Locked: 'session.statusLocked',
  Closed: 'session.statusClosed',
} as const

/**
 * A running session for the teacher: the PIN to show the class, who has joined, and locking and closing.
 * The list is refreshed by hand for now; the live dashboard comes with SignalR.
 */
export function SessionPage() {
  const { t, i18n } = useTranslation()
  const { sessionId = '' } = useParams()
  const [state, reload] = useLoad(useCallback((signal: AbortSignal) => getSession(sessionId, signal), [sessionId]))
  const [confirmingClose, setConfirmingClose] = useState(false)
  const [pending, setPending] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  async function change(action: SessionAction) {
    setPending(true)
    setErrorCode(null)
    try {
      await changeSession(sessionId, action)
      setConfirmingClose(false)
      reload()
    } catch (error) {
      setErrorCode(error instanceof ApiError ? error.code : 'UNKNOWN')
    } finally {
      setPending(false)
    }
  }

  if (state.status !== 'loaded') {
    return (
      <main>
        {state.status === 'loading' ? (
          <p role="status" className="status">
            {t('session.loading')}
          </p>
        ) : (
          <p role="alert" className="error">
            {t(errorKey(state.code))}
          </p>
        )}
        <p>
          <Link to="/teacher">{t('session.backToRooms')}</Link>
        </p>
      </main>
    )
  }

  const session = state.data
  const isClosed = session.status === 'Closed'

  return (
    <main>
      <h1>{session.roomTitle}</h1>
      <p className="room-details">{session.className}</p>

      {!isClosed && (
        <div className="pin-card">
          <p className="pin-label" id="session-pin-label">
            {t('session.pinLabel')}
          </p>
          <p className="pin" aria-labelledby="session-pin-label">
            {session.pin}
          </p>
          <p>{t('session.joinHint', { url: `${window.location.origin}/join` })}</p>
          <p className="hint">{t('session.expiresAt', { time: formatDateTime(session.expiresAt, i18n.language) })}</p>
        </div>
      )}

      <p role="status" className="status" data-state={session.status === 'Open' ? 'online' : undefined}>
        {t(statusKeys[session.status])}
      </p>

      {!isClosed && (
        <div className="actions">
          <button
            type="button"
            disabled={pending}
            onClick={() => void change(session.status === 'Locked' ? 'unlock' : 'lock')}
          >
            {t(session.status === 'Locked' ? 'session.unlock' : 'session.lock')}
          </button>
          {!confirmingClose && (
            <button
              type="button"
              disabled={pending}
              onClick={() => {
                setConfirmingClose(true)
              }}
            >
              {t('session.close')}
            </button>
          )}
        </div>
      )}

      {!isClosed && confirmingClose && (
        <div className="confirm" role="alertdialog" aria-labelledby="close-confirm">
          <p id="close-confirm">{t('session.closeConfirm')}</p>
          <div className="actions">
            <button type="button" className="primary" disabled={pending} onClick={() => void change('close')}>
              {t('session.closeConfirmYes')}
            </button>
            <button
              type="button"
              disabled={pending}
              onClick={() => {
                setConfirmingClose(false)
              }}
            >
              {t('session.closeConfirmNo')}
            </button>
          </div>
        </div>
      )}

      {errorCode && (
        <p role="alert" className="error">
          {t(errorKey(errorCode))}
        </p>
      )}

      <Participants session={session} onRefresh={reload} />

      <p>
        <Link to="/teacher">{t('session.backToRooms')}</Link>
      </p>
    </main>
  )
}

/** Teams with their students in team mode; just the students in individual mode (one team each). */
function Participants({ session, onRefresh }: { session: SessionDetails; onRefresh: () => void }) {
  const { t } = useTranslation()
  const nickname = (name: string | null) => name ?? t('session.noNickname')
  const individual = session.mode === 'Individual'
  const students = session.teams.flatMap((team) => team.participants)

  return (
    <section aria-labelledby="participants-title">
      <div className="section-header">
        <h2 id="participants-title">{t(individual ? 'session.studentsTitle' : 'session.teamsTitle')}</h2>
        <button type="button" onClick={onRefresh}>
          {t('session.refresh')}
        </button>
      </div>
      {individual ? (
        students.length === 0 ? (
          <p>{t('session.noStudents')}</p>
        ) : (
          <ul className="room-list">
            {students.map((student) => (
              <li key={student.id}>{nickname(student.nickname)}</li>
            ))}
          </ul>
        )
      ) : (
        <ul className="room-list">
          {session.teams.map((team) => (
            <li key={team.id} className="team" data-color={team.color}>
              <span className="room-title">{teamName(t, team.color)}</span>
              {team.participants.length === 0 ? (
                <span className="room-details">{t('session.noStudents')}</span>
              ) : (
                <ul className="nicknames">
                  {team.participants.map((participant) => (
                    <li key={participant.id}>{nickname(participant.nickname)}</li>
                  ))}
                </ul>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
