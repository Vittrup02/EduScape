import { useCallback, useId, useState, type SubmitEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router'
import { ApiError } from '../api/client'
import { getClassNames, maxTeamCount, minTeamCount, startSession, type SessionMode } from '../api/sessions'
import { getRoom, type RoomKind } from '../api/teacher'
import { useLoad } from '../api/useLoad'
import { errorKey } from '../i18n/errors'

const defaultTeamCount = 4

/**
 * Starts a session with one of the teacher's rooms or a template: the class, the mode and (in team mode only)
 * the number of teams. Then on to the session page with the PIN.
 */
export function StartSessionPage({ kind }: { kind: RoomKind }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { roomId = '' } = useParams()
  const hintId = useId()
  const classListId = useId()
  const teamCountId = useId()
  const [className, setClassName] = useState('')
  const [mode, setMode] = useState<SessionMode>('Team')
  const [teamCount, setTeamCount] = useState(defaultTeamCount)
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const [state] = useLoad(
    useCallback(
      async (signal: AbortSignal) => {
        const [room, classNames] = await Promise.all([getRoom(kind, roomId, signal), getClassNames(signal)])
        return { room, classNames }
      },
      [kind, roomId],
    ),
  )

  async function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    setPending(true)
    setErrorCode(null)
    try {
      const session = await startSession({ roomId, className, mode, teamCount })
      await navigate(`/teacher/sessions/${session.id}`)
    } catch (error) {
      setErrorCode(error instanceof ApiError ? error.code : 'UNKNOWN')
      setPending(false)
    }
  }

  if (state.status === 'loading') {
    return (
      <main>
        <h1>{t('startSession.title')}</h1>
        <p role="status" className="status">
          {t('startSession.loading')}
        </p>
      </main>
    )
  }

  if (state.status === 'failed') {
    return (
      <main>
        <h1>{t('startSession.title')}</h1>
        <p role="alert" className="error">
          {t(errorKey(state.code))}
        </p>
      </main>
    )
  }

  const modes: { value: SessionMode; label: string; description: string }[] = [
    { value: 'Team', label: t('startSession.modeTeam'), description: t('startSession.modeTeamDescription') },
    {
      value: 'Individual',
      label: t('startSession.modeIndividual'),
      description: t('startSession.modeIndividualDescription'),
    },
  ]

  return (
    <main>
      <h1>{t('startSession.title')}</h1>
      <p className="room-title">{state.data.room.title}</p>
      {/* noValidate: the server validates, and its error codes are shown translated instead of the browser's own messages. */}
      <form className="auth-form" noValidate onSubmit={(event) => void handleSubmit(event)}>
        <label>
          {t('startSession.className')}
          <input
            name="className"
            autoComplete="off"
            required
            maxLength={100}
            list={classListId}
            aria-describedby={hintId}
            value={className}
            onChange={(event) => {
              setClassName(event.target.value)
            }}
          />
        </label>
        <datalist id={classListId}>
          {state.data.classNames.map((name) => (
            <option key={name} value={name} />
          ))}
        </datalist>
        <p id={hintId} className="hint">
          {t('startSession.classNameHint')}
        </p>

        <fieldset className="choices">
          <legend>{t('startSession.modeLegend')}</legend>
          {modes.map((option) => (
            <label key={option.value} className="choice">
              <input
                type="radio"
                name="mode"
                value={option.value}
                checked={mode === option.value}
                onChange={() => {
                  setMode(option.value)
                }}
              />
              <span className="choice-label">{option.label}</span>
              <span className="choice-description">{option.description}</span>
            </label>
          ))}
        </fieldset>

        {/* Only relevant in team mode: in individual mode every student gets a team of their own. */}
        {mode === 'Team' && (
          <div className="stepper" role="group" aria-labelledby={teamCountId}>
            <span id={teamCountId} className="stepper-label">
              {t('startSession.teamCount')}
            </span>
            <button
              type="button"
              aria-label={t('startSession.fewerTeams')}
              disabled={teamCount <= minTeamCount}
              onClick={() => {
                setTeamCount((count) => Math.max(minTeamCount, count - 1))
              }}
            >
              <span aria-hidden="true">−</span>
            </button>
            <output aria-live="polite">{teamCount}</output>
            <button
              type="button"
              aria-label={t('startSession.moreTeams')}
              disabled={teamCount >= maxTeamCount}
              onClick={() => {
                setTeamCount((count) => Math.min(maxTeamCount, count + 1))
              }}
            >
              <span aria-hidden="true">+</span>
            </button>
          </div>
        )}

        {errorCode && (
          <p role="alert" className="error">
            {t(errorKey(errorCode))}
          </p>
        )}
        <button type="submit" className="primary" disabled={pending}>
          {t(pending ? 'startSession.starting' : 'startSession.start')}
        </button>
      </form>
    </main>
  )
}
