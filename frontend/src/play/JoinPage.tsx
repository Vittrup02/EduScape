import { useId, useState, type SubmitEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, useNavigate } from 'react-router'
import { ApiError } from '../api/client'
import { loadDeviceToken, saveDeviceToken } from '../api/deviceStorage'
import { joinSession, lookupPin, type LookupResult } from '../api/play'
import { errorKey } from '../i18n/errors'
import { teamName } from '../i18n/format'

type Step =
  | { name: 'pin' }
  | { name: 'team'; pin: string; lookup: LookupResult }
  | { name: 'nickname'; pin: string; lookup: LookupResult; teamId: string | null }

function errorCodeOf(error: unknown): string {
  return error instanceof ApiError ? error.code : 'UNKNOWN'
}

/**
 * How students join: PIN → team (skipped in individual mode) → optional nickname. The device token the server
 * returns is kept in the browser, and a device that already has one goes straight back to its team.
 */
export function JoinPage() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const pinHintId = useId()
  const nicknameHintId = useId()
  const [hasToken] = useState(() => loadDeviceToken() !== null)
  const [step, setStep] = useState<Step>({ name: 'pin' })
  const [pin, setPin] = useState('')
  const [nickname, setNickname] = useState('')
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  if (hasToken) {
    return <Navigate to="/play" replace />
  }

  async function submitPin(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    // "123 456" is accepted too.
    const digits = pin.replace(/\s/g, '')
    setPending(true)
    setErrorCode(null)
    try {
      const lookup = await lookupPin(digits)
      setStep(
        lookup.mode === 'Individual'
          ? { name: 'nickname', pin: digits, lookup, teamId: null }
          : { name: 'team', pin: digits, lookup },
      )
    } catch (error) {
      setErrorCode(errorCodeOf(error))
    } finally {
      setPending(false)
    }
  }

  async function submitNickname(event: SubmitEvent<HTMLFormElement>, current: Step & { name: 'nickname' }) {
    event.preventDefault()
    setPending(true)
    setErrorCode(null)
    try {
      const joined = await joinSession(current.pin, current.teamId, nickname)
      saveDeviceToken(joined.deviceToken)
      await navigate('/play', { replace: true })
    } catch (error) {
      setErrorCode(errorCodeOf(error))
      setPending(false)
    }
  }

  function goBack() {
    setErrorCode(null)
    setStep(
      step.name === 'nickname' && step.lookup.mode === 'Team'
        ? { name: 'team', pin: step.pin, lookup: step.lookup }
        : { name: 'pin' },
    )
  }

  const error = errorCode && (
    <p role="alert" className="error">
      {t(errorKey(errorCode))}
    </p>
  )

  if (step.name === 'pin') {
    return (
      <main>
        <h1>{t('join.title')}</h1>
        <form className="auth-form" noValidate onSubmit={(event) => void submitPin(event)}>
          <label>
            {t('join.pin')}
            <input
              className="pin-input"
              name="pin"
              inputMode="numeric"
              autoComplete="off"
              maxLength={7}
              aria-describedby={pinHintId}
              value={pin}
              onChange={(event) => {
                setPin(event.target.value)
              }}
            />
          </label>
          <p id={pinHintId} className="hint">
            {t('join.pinHint')}
          </p>
          {error}
          <button type="submit" className="primary" disabled={pending}>
            {t(pending ? 'join.checking' : 'join.next')}
          </button>
        </form>
      </main>
    )
  }

  if (step.name === 'team') {
    return (
      <main>
        <h1>{t('join.chooseTeam')}</h1>
        <ul className="team-choices">
          {step.lookup.teams.map((team) => (
            <li key={team.id}>
              <button
                type="button"
                className="team-choice"
                data-color={team.color}
                onClick={() => {
                  setErrorCode(null)
                  setStep({ name: 'nickname', pin: step.pin, lookup: step.lookup, teamId: team.id })
                }}
              >
                {teamName(t, team.color)}
              </button>
            </li>
          ))}
        </ul>
        {error}
        <button type="button" onClick={goBack}>
          {t('join.back')}
        </button>
      </main>
    )
  }

  return (
    <main>
      <h1>{t('join.nicknameTitle')}</h1>
      <form className="auth-form" noValidate onSubmit={(event) => void submitNickname(event, step)}>
        <label>
          {t('join.nickname')}
          <input
            name="nickname"
            autoComplete="off"
            maxLength={20}
            aria-describedby={nicknameHintId}
            value={nickname}
            onChange={(event) => {
              setNickname(event.target.value)
            }}
          />
        </label>
        <p id={nicknameHintId} className="hint">
          {t('join.nicknameHint')}
        </p>
        {error}
        <button type="submit" className="primary" disabled={pending}>
          {t(pending ? 'join.joining' : 'join.join')}
        </button>
        <button type="button" onClick={goBack} disabled={pending}>
          {t('join.back')}
        </button>
      </form>
    </main>
  )
}
