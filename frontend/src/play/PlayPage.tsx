import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useNavigate } from 'react-router'
import { loadDeviceToken, saveDeviceToken } from '../api/deviceStorage'
import { errorKey } from '../i18n/errors'
import { teamName } from '../i18n/format'
import { RoomView } from './RoomView'
import { usePlayConnection } from './usePlayConnection'

/**
 * The student's game view. It connects with the stored device token, gets the team's whole state and then follows
 * the team's progress live. While the backend wakes up or the connection is lost it keeps trying and shows a
 * friendly "connecting" message.
 */
export function PlayPage({ retryDelayMs = 3000 }: { retryDelayMs?: number }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [token] = useState(loadDeviceToken)
  const { connection, apply } = usePlayConnection(token, retryDelayMs)
  const [confirmingLeave, setConfirmingLeave] = useState(false)

  if (token === null) {
    return <Navigate to="/join" replace />
  }

  if (connection.status === 'connecting') {
    return (
      <main>
        <p role="status" className="status">
          {t('play.connecting')}
        </p>
      </main>
    )
  }

  if (connection.status === 'ended') {
    return (
      <main>
        {connection.code === 'SESSION_CLOSED' ? (
          // The normal end of a lesson, not an error.
          <section className="session-ended">
            <h1>{t('play.ended.title')}</h1>
            <p>{t('play.ended.text')}</p>
          </section>
        ) : (
          <p role="alert" className="error">
            {t(errorKey(connection.code))}
          </p>
        )}
        <p>
          <Link className="button primary" to="/join">
            {t('play.joinAgain')}
          </Link>
        </p>
      </main>
    )
  }

  const { state, online } = connection
  const individual = state.mode === 'Individual'
  return (
    <main className="play">
      <div className="play-header">
        <div>
          <h1 className="team-heading" data-color={state.team.color}>
            {individual ? t('play.individual') : t('play.onTeam', { team: teamName(t, state.team.color) })}
          </h1>
          {state.nickname && <p>{t('play.nickname', { nickname: state.nickname })}</p>}
        </div>
        {!confirmingLeave && (
          <button
            type="button"
            onClick={() => {
              setConfirmingLeave(true)
            }}
          >
            {t('play.leave')}
          </button>
        )}
      </div>
      {/* A stray tap must not throw the student out: leaving forgets the device's place on the team. */}
      {confirmingLeave && (
        <div className="confirm" role="alertdialog" aria-labelledby="leave-confirm">
          <p id="leave-confirm">{t('play.leaveConfirm')}</p>
          <div className="actions">
            <button
              type="button"
              onClick={() => {
                saveDeviceToken(null)
                void navigate('/join', { replace: true })
              }}
            >
              {t('play.leaveYes')}
            </button>
            <button
              type="button"
              className="primary"
              autoFocus
              onClick={() => {
                setConfirmingLeave(false)
              }}
            >
              {t('play.leaveNo')}
            </button>
          </div>
        </div>
      )}
      {state.isPreview && <p className="preview-banner">{t('play.preview')}</p>}
      {!online && (
        <p role="status" className="connection-banner">
          {t('play.reconnecting')}
        </p>
      )}
      {state.team.escapedAt !== null ? (
        <section className="escaped">
          <h2>{individual ? t('play.escaped.individual') : t('play.escaped.team')}</h2>
          <p>{t('play.escaped.text')}</p>
        </section>
      ) : (
        <RoomView state={state} deviceToken={token} online={online} onEvent={apply} />
      )}
    </main>
  )
}
