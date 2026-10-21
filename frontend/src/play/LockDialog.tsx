import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../api/client'
import { cooldownUntil, submitAnswer, type Answer, type PlayLock, type PlayStep, type TeamState } from '../api/play'
import { errorKey } from '../i18n/errors'
import { Modal } from './Modal'
import { OrderPuzzle } from './OrderPuzzle'
import { cooldownRemaining, currentStep, lockProgress, revealedHint, type TeamEvent } from './progress'

interface LockDialogProps {
  lock: PlayLock
  team: TeamState
  deviceToken: string
  online: boolean
  onEvent: (event: TeamEvent) => void
  onClose: () => void
}

type Feedback = { kind: 'wrong'; attempt: number } | { kind: 'error'; code: string } | { kind: 'stepSolved'; step: number }

/** The current time for a countdown to `until`: it ticks every `intervalMs` until then, and stands still otherwise. */
function useNow(until: number | null, intervalMs = 250): number {
  const [now, setNow] = useState(() => Date.now())
  const active = until !== null && now < until
  useEffect(() => {
    if (!active) {
      return
    }
    const tick = () => {
      setNow(Date.now())
    }
    // Catch up at once: the clock stood still while nothing counted down.
    const first = setTimeout(tick, 0)
    const timer = setInterval(tick, intervalMs)
    return () => {
      clearTimeout(first)
      clearInterval(timer)
    }
  }, [active, intervalMs])
  return now
}

/**
 * A lock's puzzle, one step at a time in the same pop-up: the student sees only the step the team is on, and the next
 * one appears when it is answered, by them or a teammate. The answer is only checked on the server; the lock opens
 * after the last step, and the dialog closes so the team sees it open. After a wrong answer the lock waits for the
 * whole team, with a countdown. A step's hint appears once the team has earned it.
 */
export function LockDialog({ lock, team, deviceToken, online, onEvent, onClose }: LockDialogProps) {
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [feedback, setFeedback] = useState<Feedback | null>(null)
  const stepIndex = currentStep(team, lock)
  const [shownStep, setShownStep] = useState(stepIndex)
  const solved = team.solvedLockIds.includes(lock.id)
  const step: PlayStep | undefined = lock.steps.at(Math.min(stepIndex, lock.steps.length - 1))
  const hint = revealedHint(team, lock.id)
  const now = useNow(lockProgress(team, lock.id)?.cooldownUntil ?? null)
  const secondsLeft = cooldownRemaining(team, lock.id, now)

  // The team moved on to the next step (a teammate, or this student): say so, and start the new step afresh.
  if (stepIndex !== shownStep) {
    setShownStep(stepIndex)
    setFeedback(stepIndex > shownStep && !solved ? { kind: 'stepSolved', step: stepIndex } : null)
  }

  const submit = async (answer: Answer) => {
    setBusy(true)
    setFeedback(null)
    try {
      const result = await submitAnswer(deviceToken, lock.id, stepIndex, answer)
      if (result.hint !== null) {
        onEvent({ type: 'hintRevealed', lockId: lock.id, hint: result.hint })
      }
      if (result.cooldownSeconds > 0) {
        onEvent({ type: 'lockCooldown', lockId: lock.id, until: cooldownUntil(result.cooldownSeconds) })
      }
      switch (result.outcome) {
        case 'Wrong':
          setFeedback((previous) => ({ kind: 'wrong', attempt: previous?.kind === 'wrong' ? previous.attempt + 1 : 1 }))
          break
        case 'CoolingDown':
          break
        default:
          if (result.lockSolved || result.solvedSteps >= lock.steps.length) {
            onEvent({ type: 'lockSolved', lockId: lock.id })
            for (const doorId of result.openedDoorIds) {
              onEvent({ type: 'doorOpened', doorId })
            }
            onClose()
          } else {
            onEvent({ type: 'stepSolved', lockId: lock.id, solvedSteps: result.solvedSteps })
          }
      }
    } catch (error) {
      setFeedback({ kind: 'error', code: error instanceof ApiError ? error.code : 'UNKNOWN' })
    } finally {
      setBusy(false)
    }
  }

  const disabled = busy || !online || secondsLeft > 0
  const multiStep = lock.steps.length > 1

  return (
    <Modal title={t('play.lock.title')} onClose={onClose}>
      {multiStep && !solved && (
        <p className="lock-step">{t('play.lock.step', { n: stepIndex + 1, count: lock.steps.length })}</p>
      )}
      {feedback?.kind === 'stepSolved' && (
        <p role="status" className="lock-step-solved">
          {t('play.lock.stepSolved')}
        </p>
      )}
      {step !== undefined && <p className="lock-question">{step.question}</p>}
      {solved || step === undefined ? (
        <p className="lock-solved">{t('play.lock.solved')}</p>
      ) : (
        // Keyed by the step, so a new step starts with an empty answer. The keys differ from the wrong-answer
        // message's, its sibling: a shared key would let React mix the two up and lose the student's order.
        <StepAnswer
          key={`step-${String(stepIndex)}`}
          step={step}
          disabled={disabled}
          onSubmit={(answer) => void submit(answer)}
        />
      )}
      {secondsLeft > 0 && !solved && (
        <p role="timer" className="lock-cooldown">
          {t('play.lock.cooldown', { count: secondsLeft })}
        </p>
      )}
      {feedback?.kind === 'wrong' && !solved && (
        <p key={`wrong-${String(feedback.attempt)}`} role="alert" className="error">
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

/** How the student answers the step: typing, picking an option or putting the items in order. */
function StepAnswer({ step, disabled, onSubmit }: { step: PlayStep; disabled: boolean; onSubmit: (answer: Answer) => void }) {
  const { t } = useTranslation()
  const [text, setText] = useState('')

  if (step.answerType === 'Order') {
    return (
      <OrderPuzzle
        items={step.orderItems ?? []}
        disabled={disabled}
        onSubmit={(order) => {
          onSubmit({ order })
        }}
      />
    )
  }

  if (step.answerType === 'MultipleChoice') {
    return (
      <fieldset className="answer-options">
        <legend>{t('play.lock.options')}</legend>
        {(step.options ?? []).map((option) => (
          <button
            key={option}
            type="button"
            disabled={disabled}
            onClick={() => {
              onSubmit({ text: option })
            }}
          >
            {option}
          </button>
        ))}
      </fieldset>
    )
  }

  return (
    <form
      className="answer-form"
      onSubmit={(event) => {
        event.preventDefault()
        if (text.trim() !== '') {
          onSubmit({ text })
          setText('')
        }
      }}
    >
      <label>
        {t('play.lock.answer')}
        <input
          value={text}
          onChange={(event) => {
            setText(event.target.value)
          }}
          autoFocus
          autoComplete="off"
          autoCapitalize="off"
          spellCheck={false}
          maxLength={200}
        />
      </label>
      <button type="submit" className="primary" disabled={disabled || text.trim() === ''}>
        {t('play.lock.submit')}
      </button>
    </form>
  )
}
