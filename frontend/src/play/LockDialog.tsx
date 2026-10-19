import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { submitAnswer, type PlayLock } from '../api/play'
import { errorKey } from '../i18n/errors'
import { Modal } from './Modal'
import type { TeamEvent } from './progress'

interface LockDialogProps {
  lock: PlayLock
  solved: boolean
  hint: string | null
  deviceToken: string
  online: boolean
  onEvent: (event: TeamEvent) => void
  onClose: () => void
}

type Feedback = { kind: 'wrong'; attempt: number } | { kind: 'error'; code: string }

/**
 * A lock's question. The answer is only checked on the server; a correct answer closes the dialog so the team sees
 * the lock open. The hint appears once the team has earned it, also when a teammate made the third wrong guess.
 */
export function LockDialog({ lock, solved, hint, deviceToken, online, onEvent, onClose }: LockDialogProps) {
  const { t } = useTranslation()
  const [answer, setAnswer] = useState('')
  const [busy, setBusy] = useState(false)
  const [feedback, setFeedback] = useState<Feedback | null>(null)

  const submit = async (value: string) => {
    if (value.trim() === '') {
      return
    }
    setBusy(true)
    setFeedback(null)
    try {
      const result = await submitAnswer(deviceToken, lock.id, value)
      if (result.hint !== null) {
        onEvent({ type: 'hintRevealed', lockId: lock.id, hint: result.hint })
      }
      if (result.outcome === 'Wrong') {
        setAnswer('')
        setFeedback((previous) => ({ kind: 'wrong', attempt: previous?.kind === 'wrong' ? previous.attempt + 1 : 1 }))
      } else {
        onEvent({ type: 'lockSolved', lockId: lock.id })
        onClose()
      }
    } catch (error) {
      setFeedback({ kind: 'error', code: error instanceof ApiError ? error.code : 'UNKNOWN' })
    } finally {
      setBusy(false)
    }
  }

  const disabled = busy || !online

  return (
    <Modal title={t('play.lock.title')} onClose={onClose}>
      <p className="lock-question">{lock.question}</p>
      {solved ? (
        <p className="lock-solved">{t('play.lock.solved')}</p>
      ) : lock.answerType === 'MultipleChoice' ? (
        <fieldset className="answer-options">
          <legend>{t('play.lock.options')}</legend>
          {(lock.options ?? []).map((option) => (
            <button key={option} type="button" disabled={disabled} onClick={() => void submit(option)}>
              {option}
            </button>
          ))}
        </fieldset>
      ) : (
        <form
          className="answer-form"
          onSubmit={(event) => {
            event.preventDefault()
            void submit(answer)
          }}
        >
          <label>
            {t('play.lock.answer')}
            <input
              value={answer}
              onChange={(event) => {
                setAnswer(event.target.value)
              }}
              autoFocus
              autoComplete="off"
              autoCapitalize="off"
              spellCheck={false}
              maxLength={200}
            />
          </label>
          <button type="submit" className="primary" disabled={disabled || answer.trim() === ''}>
            {t('play.lock.submit')}
          </button>
        </form>
      )}
      {feedback?.kind === 'wrong' && !solved && (
        <p key={feedback.attempt} role="alert" className="error">
          {t('play.lock.wrong')}
        </p>
      )}
      {feedback?.kind === 'error' && (
        <p role="alert" className="error">
          {t(errorKey(feedback.code))}
        </p>
      )}
      {hint !== null && !solved && (
        <p className="lock-hint">
          <strong>{t('play.lock.hint')}</strong> {hint}
        </p>
      )}
    </Modal>
  )
}
