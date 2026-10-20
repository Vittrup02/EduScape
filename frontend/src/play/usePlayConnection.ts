import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../api/client'
import { saveDeviceToken } from '../api/deviceStorage'
import { reconnect, type PlayState } from '../api/play'
import { createPlayHub } from '../api/playHub'
import { applyTeamEvent, type TeamEvent } from './progress'

export type PlayConnection =
  | { status: 'connecting' }
  /** `online` is false while a lost connection is being re-established; the room stays on screen meanwhile. */
  | { status: 'connected'; state: PlayState; online: boolean }
  | { status: 'ended'; code: string }

/** A 4xx answer means the token will never work again (session closed, device unknown); anything else is retried. */
function isFinal(error: unknown): error is ApiError {
  return error instanceof ApiError && error.status >= 400 && error.status < 500
}

/**
 * Keeps the student's device in sync with the team. It connects to the play hub, subscribes to the team's events
 * and then fetches the team's whole state, in that order, so no event is missed in between. After every reconnect
 * it does the same again, because events may have been missed while offline. While the backend wakes up it keeps
 * trying. When the teacher moves the student to another team, it starts over on a fresh connection.
 *
 * `apply` updates the state right away after the student's own action; the server's push of the same event
 * changes nothing more.
 */
export function usePlayConnection(token: string | null, retryDelayMs: number) {
  const [connection, setConnection] = useState<PlayConnection>({ status: 'connecting' })

  const apply = useCallback((event: TeamEvent) => {
    setConnection((current) =>
      current.status === 'connected'
        ? { ...current, state: { ...current.state, team: applyTeamEvent(current.state.team, event) } }
        : current,
    )
  }, [])

  useEffect(() => {
    if (token === null) {
      return
    }
    let cancelled = false
    // A function, so TypeScript does not assume the flag is unchanged after an await.
    const isCancelled = () => cancelled
    let retryTimer: ReturnType<typeof setTimeout> | undefined
    // Set while the hub is stopped on purpose to start a fresh connection, which leaves the old team's group.
    let switchingTeam = false

    const retry = (step: () => Promise<void>) => {
      clearTimeout(retryTimer)
      retryTimer = setTimeout(() => void step(), retryDelayMs)
    }

    const setOffline = () => {
      setConnection((current) => (current.status === 'connected' ? { ...current, online: false } : current))
    }

    const hub = createPlayHub({
      lockSolved: (lockId) => {
        apply({ type: 'lockSolved', lockId })
      },
      hintRevealed: (lockId, hint) => {
        apply({ type: 'hintRevealed', lockId, hint })
      },
      doorOpened: (doorId) => {
        apply({ type: 'doorOpened', doorId })
      },
      teamEscaped: () => {
        apply({ type: 'teamEscaped' })
      },
      teamChanged: () => {
        switchingTeam = true
        setOffline()
        void hub.stop()
      },
      reconnecting: setOffline,
      reconnected: () => void sync(),
      closed: () => {
        if (isCancelled()) {
          return
        }
        setOffline()
        if (switchingTeam) {
          switchingTeam = false
          void connect()
        } else {
          retry(connect)
        }
      },
    })

    const end = (code: string) => {
      cancelled = true
      clearTimeout(retryTimer)
      saveDeviceToken(null)
      setConnection({ status: 'ended', code })
      void hub.stop()
    }

    const sync = async () => {
      try {
        const code = await hub.subscribe(token)
        if (isCancelled()) {
          return
        }
        if (code !== null) {
          end(code)
          return
        }
        const state = await reconnect(token)
        if (!isCancelled()) {
          setConnection({ status: 'connected', state, online: true })
        }
      } catch (error) {
        if (isCancelled()) {
          return
        }
        if (isFinal(error)) {
          end(error.code)
        } else if (hub.isConnected()) {
          retry(sync)
        }
        // Otherwise the hub is reconnecting, and `reconnected` or `closed` takes it from here.
      }
    }

    const connect = async () => {
      try {
        await hub.start()
      } catch {
        if (!isCancelled()) {
          retry(connect)
        }
        return
      }
      await sync()
    }

    void connect()

    return () => {
      cancelled = true
      clearTimeout(retryTimer)
      void hub.stop()
    }
  }, [token, retryDelayMs, apply])

  return { connection, apply }
}
