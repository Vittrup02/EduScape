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
        <p role="alert" className="error">
          {t(errorKey(connection.code))}
        </p>
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
        <button
          type="button"
          onClick={() => {
            saveDeviceToken(null)
            void navigate('/join', { replace: true })
          }}
        >
          {t('play.leave')}
        </button>
      </div>
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
