import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useNavigate } from 'react-router'
import { ApiError } from '../api/client'
import { loadDeviceToken, saveDeviceToken } from '../api/deviceStorage'
import { reconnect, type PlayState } from '../api/play'
import { errorKey } from '../i18n/errors'
import { teamName } from '../i18n/format'

type Connection =
  | { status: 'connecting' }
  | { status: 'connected'; state: PlayState }
  | { status: 'ended'; code: string }

/** A 4xx answer means the token will never work again (session closed, device unknown); anything else is retried. */
function isFinal(error: unknown): error is ApiError {
  return error instanceof ApiError && error.status >= 400 && error.status < 500
}

/**
 * The student's game view. On every load it reconnects with the stored device token and gets the team's whole
 * state. While the backend is waking up it keeps trying and shows a friendly "connecting" message.
 */
export function PlayPage({ retryDelayMs = 3000 }: { retryDelayMs?: number }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [token] = useState(loadDeviceToken)
  const [connection, setConnection] = useState<Connection>({ status: 'connecting' })

  useEffect(() => {
    if (token === null) {
      return
    }
    let cancelled = false
    let retryTimer: ReturnType<typeof setTimeout> | undefined

    const attempt = () => {
      reconnect(token).then(
        (state) => {
          if (!cancelled) {
            setConnection({ status: 'connected', state })
          }
        },
        (error: unknown) => {
          if (cancelled) {
            return
          }
          if (isFinal(error)) {
            saveDeviceToken(null)
            setConnection({ status: 'ended', code: error.code })
          } else {
            retryTimer = setTimeout(attempt, retryDelayMs)
          }
        },
      )
    }
    attempt()

    return () => {
      cancelled = true
      clearTimeout(retryTimer)
    }
  }, [token, retryDelayMs])

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

  const { state } = connection
  return (
    <main>
      <h1 className="team-heading" data-color={state.team.color}>
        {state.mode === 'Team' ? t('play.onTeam', { team: teamName(t, state.team.color) }) : t('play.individual')}
      </h1>
      {state.nickname && <p>{t('play.nickname', { nickname: state.nickname })}</p>}
      <p>{t('play.waiting')}</p>
      <button
        type="button"
        onClick={() => {
          saveDeviceToken(null)
          void navigate('/join', { replace: true })
        }}
      >
        {t('play.leave')}
      </button>
    </main>
  )
}
