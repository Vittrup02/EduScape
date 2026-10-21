import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { ApiError, checkHealth } from '../api/client'
import {
  changeSession,
  moveParticipant,
  type DashboardTeam,
  type SessionAction,
  type SessionDashboard,
} from '../api/sessions'
import { errorKey } from '../i18n/errors'
import { formatDateTime, formatMinutes, teamName } from '../i18n/format'
import { useDashboard } from './useDashboard'

const statusKeys = {
  Open: 'session.statusOpen',
  Locked: 'session.statusLocked',
  Closed: 'session.statusClosed',
} as const

/** A team that has made no progress on its current lock for this long is shown as stuck. */
export const stuckAfterMinutes = 5

/** How often the minutes on the dashboard are counted again. */
const clockTickMs = 15_000

function minutesBetween(fromIso: string, toMs: number): number {
  return Math.max(0, Math.floor((toMs - Date.parse(fromIso)) / 60_000))
}

/** The current time, updated every `intervalMs`. */
function useNow(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => {
      setNow(Date.now())
    }, intervalMs)
    return () => {
      clearInterval(timer)
    }
  }, [intervalMs])
  return now
}

/**
 * The teacher's live dashboard for a running session: the PIN to show the class, every team's progress and where
 * its students are, who is stuck, moving students between teams, and locking and closing the session. Progress
 * arrives through the teacher hub; times and progress are shown only here, never to students.
 */
export function SessionPage({ retryDelayMs }: { retryDelayMs?: number }) {
  const { t, i18n } = useTranslation()
  const { sessionId = '' } = useParams()
  const { state, refresh } = useDashboard(sessionId, retryDelayMs)
  const now = useNow(clockTickMs)
  const [confirmingClose, setConfirmingClose] = useState(false)
  const [showingPin, setShowingPin] = useState(false)
  const [pending, setPending] = useState(false)
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const hidePin = useCallback(() => {
    setShowingPin(false)
  }, [])

  // Wakes the backend (Render free tier) as soon as the dashboard opens.
  useEffect(() => {
    const controller = new AbortController()
    void checkHealth(controller.signal)
    return () => {
      controller.abort()
    }
  }, [])

  async function run(action: () => Promise<void>) {
    setPending(true)
    setErrorCode(null)
    try {
      await action()
      await refresh()
    } catch (error) {
      setErrorCode(error instanceof ApiError ? error.code : 'UNKNOWN')
    } finally {
      setPending(false)
    }
  }

  const change = (action: SessionAction) =>
    run(async () => {
      await changeSession(sessionId, action)
      setConfirmingClose(false)
    })

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
  const joinHint = t('session.joinHint', { url: `${window.location.origin}/join` })

  return (
    <main className="dashboard">
      <h1>{session.roomTitle}</h1>
      <p className="room-details">{session.className}</p>

      {!state.live && !isClosed && (
        <p role="status" className="status connecting">
          {t('session.connecting')}
        </p>
      )}

      {!isClosed && (
        <div className="pin-card">
          <p className="pin-label" id="session-pin-label">
            {t('session.pinLabel')}
          </p>
          <p className="pin" aria-labelledby="session-pin-label">
            {session.pin}
          </p>
          <p>{joinHint}</p>
          <p className="hint">{t('session.expiresAt', { time: formatDateTime(session.expiresAt, i18n.language) })}</p>
          <button
            type="button"
            onClick={() => {
              setShowingPin(true)
            }}
          >
            {t('session.showPin')}
          </button>
        </div>
      )}

      {showingPin && !isClosed && <PinBoard pin={session.pin} joinHint={joinHint} onClose={hidePin} />}

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

      <Teams
        session={session}
        serverNowMs={Math.max(now + state.clockOffsetMs, Date.parse(session.serverNow))}
        pending={pending}
        onMove={(participantId, teamId) =>
          void run(() => moveParticipant(sessionId, participantId, teamId))
        }
      />

      <p>
        <Link to="/teacher">{t('session.backToRooms')}</Link>
      </p>
    </main>
  )
}

/**
 * The PIN filling the screen, for the projector. Opens in full screen where the browser allows it; Escape or the
 * close button (or leaving full screen) closes it.
 */
function PinBoard({ pin, joinHint, onClose }: { pin: string; joinHint: string; onClose: () => void }) {
  const { t } = useTranslation()
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let wasFullscreen = false
    const element = ref.current
    if (element !== null && document.fullscreenEnabled) {
      element.requestFullscreen().then(
        () => {
          wasFullscreen = true
        },
        () => undefined,
      )
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    // In full screen the browser handles Escape itself and only reports leaving full screen.
    const onFullscreenChange = () => {
      if (wasFullscreen && document.fullscreenElement === null) {
        onClose()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    document.addEventListener('fullscreenchange', onFullscreenChange)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      document.removeEventListener('fullscreenchange', onFullscreenChange)
      if (document.fullscreenElement) {
        void document.exitFullscreen()
      }
    }
  }, [onClose])

  return (
    <div ref={ref} className="pin-board" role="dialog" aria-modal="true" aria-labelledby="pin-board-label">
      <p className="pin-label" id="pin-board-label">
        {t('session.pinLabel')}
      </p>
      <p className="pin">{pin}</p>
      <p className="pin-board-hint">{joinHint}</p>
      <button type="button" autoFocus onClick={onClose}>
        {t('session.hidePin')}
      </button>
    </div>
  )
}

interface TeamsProps {
  session: SessionDashboard
  serverNowMs: number
  pending: boolean
  onMove: (participantId: string, teamId: string) => void
}

/** One card per team in team mode; one per student in individual mode, where every team is one student. */
function Teams({ session, serverNowMs, pending, onMove }: TeamsProps) {
  const { t } = useTranslation()
  const individual = session.mode === 'Individual'

  return (
    <section aria-labelledby="participants-title">
      <h2 id="participants-title">{t(individual ? 'session.studentsTitle' : 'session.teamsTitle')}</h2>
      {session.teams.length === 0 ? (
        <p>{t('session.noStudents')}</p>
      ) : (
        <ul className="team-cards">
          {session.teams.map((team) => (
            <TeamCard
              key={team.id}
              team={team}
              session={session}
              serverNowMs={serverNowMs}
              canMove={!individual && session.status !== 'Closed' && !pending}
              onMove={onMove}
            />
          ))}
        </ul>
      )}
    </section>
  )
}

interface TeamCardProps {
  team: DashboardTeam
  session: SessionDashboard
  serverNowMs: number
  canMove: boolean
  onMove: (participantId: string, teamId: string) => void
}

function TeamCard({ team, session, serverNowMs, canMove, onMove }: TeamCardProps) {
  const { t, i18n } = useTranslation()
  const individual = session.mode === 'Individual'
  const nickname = (name: string | null) => name ?? t('session.noNickname')
  const sceneName = (sceneId: string | null) => session.scenes.find((s) => s.id === sceneId)?.name
  const minutes = (count: number) => formatMinutes(count, i18n.language)
  const title = individual ? nickname(team.participants[0]?.nickname ?? null) : teamName(t, team.color)
  const headingId = `team-${team.id}`

  const solved = team.solvedLockIds.length
  const total = session.locks.length
  const current = team.currentLock
  // The step the team is on, and "step 2 of 3" when the lock has more than one.
  const currentLock = session.locks.find((l) => l.id === current?.lockId)
  const stepCount = currentLock?.questions.length ?? 0
  const stepIndex = Math.min(current?.solvedSteps ?? 0, Math.max(stepCount - 1, 0))
  const currentQuestion = currentLock?.questions[stepIndex] ?? ''
  const stepNote = stepCount > 1 ? ` ${t('session.step', { n: stepIndex + 1, count: stepCount })}` : ''
  const stuckMinutes = current === null ? 0 : minutesBetween(current.stuckSince, serverNowMs)
  const isStuck = current !== null && stuckMinutes >= stuckAfterMinutes
  const otherTeams = session.teams.filter((other) => other.id !== team.id)

  return (
    <li className="team-card" data-color={team.color} aria-labelledby={headingId}>
      <h3 id={headingId}>{title}</h3>
      <p className="team-progress">
        {t('session.locksSolved', { solved, total })}
        <progress max={Math.max(total, 1)} value={solved} aria-hidden="true" />
      </p>
      {team.escapedAt !== null && (
        <p className="team-escaped">
          {t('session.escaped', { duration: minutes(minutesBetween(session.startedAt, Date.parse(team.escapedAt))) })}
        </p>
      )}
      {current !== null && (
        <p className={isStuck ? 'team-stuck' : 'team-working'} data-stuck={isStuck || undefined}>
          {isStuck
            ? t('session.stuck', { question: currentQuestion, duration: minutes(stuckMinutes) })
            : t('session.workingOn', { question: currentQuestion })}
          {stepNote}{' '}
          · {t('session.wrongAttempts', { count: current.wrongAttempts })}
        </p>
      )}
      {team.participants.length === 0 ? (
        <p className="room-details">
          {team.participantCount > 0
            ? t('session.participantCount', { count: team.participantCount })
            : t('session.noStudents')}
        </p>
      ) : (
        <ul className="team-students">
          {team.participants.map((participant) => {
            const scene = sceneName(participant.sceneId)
            const name = nickname(participant.nickname)
            return (
              <li key={participant.id}>
                {!individual && <span className="student-name">{name}</span>}
                {scene !== undefined && <span className="room-details">{t('session.inScene', { scene })}</span>}
                {canMove && otherTeams.length > 0 && (
                  <select
                    aria-label={t('session.moveTo', { name })}
                    value=""
                    onChange={(event) => {
                      if (event.target.value !== '') {
                        onMove(participant.id, event.target.value)
                      }
                    }}
                  >
                    <option value="">{t('session.movePlaceholder')}</option>
                    {otherTeams.map((other) => (
                      <option key={other.id} value={other.id}>
                        {teamName(t, other.color)}
                      </option>
                    ))}
                  </select>
                )}
              </li>
            )
          })}
        </ul>
      )}
    </li>
  )
}
