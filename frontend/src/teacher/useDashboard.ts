import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/client'
import { getDashboard, type SessionDashboard } from '../api/sessions'
import { createTeacherHub } from '../api/teacherHub'

export type DashboardState =
  | { status: 'loading' }
  | { status: 'failed'; code: string }
  /**
   * `live` is false until the realtime connection watches the session, and while it reconnects; the last state
   * stays on screen meanwhile. `clockOffsetMs` is the server's clock minus this device's, to count minutes.
   */
  | { status: 'loaded'; data: SessionDashboard; live: boolean; clockOffsetMs: number }

/** A 4xx answer (e.g. not the teacher's session) will not change by trying again. */
function isFinal(error: unknown): error is ApiError {
  return error instanceof ApiError && error.status >= 400 && error.status < 500
}

/**
 * Keeps the teacher's dashboard live. It fetches the session's state right away, then connects to the teacher hub,
 * watches the session and fetches again, so nothing is missed in between. Every DashboardChanged ping, and every
 * reconnect, fetches the whole state again; pings arriving during a fetch lead to one more fetch, not one each.
 * While the backend wakes up it keeps trying. `refresh` fetches at once, e.g. after the teacher's own action.
 */
export function useDashboard(sessionId: string, retryDelayMs = 2000) {
  const [state, setState] = useState<DashboardState>({ status: 'loading' })
  const refreshRef = useRef<() => Promise<void>>(() => Promise.resolve())

  useEffect(() => {
    let cancelled = false
    let retryTimer: ReturnType<typeof setTimeout> | undefined
    let fetching = false
    let fetchAgain = false
    // Functions, so TypeScript does not assume the flags are unchanged after an await.
    const isCancelled = () => cancelled
    const wantsFetchAgain = () => fetchAgain
    let live = false
    const controller = new AbortController()

    const retry = (step: () => Promise<void>) => {
      clearTimeout(retryTimer)
      retryTimer = setTimeout(() => void step(), retryDelayMs)
    }

    const setLive = (value: boolean) => {
      live = value
      setState((current) => (current.status === 'loaded' ? { ...current, live } : current))
    }

    const fail = (code: string) => {
      cancelled = true
      clearTimeout(retryTimer)
      setState({ status: 'failed', code })
      void hub.stop()
    }

    const refresh = async () => {
      if (fetching) {
        fetchAgain = true
        return
      }
      fetching = true
      try {
        do {
          fetchAgain = false
          const data = await getDashboard(sessionId, controller.signal)
          if (isCancelled()) {
            return
          }
          const clockOffsetMs = Date.parse(data.serverNow) - Date.now()
          setState({ status: 'loaded', data, live, clockOffsetMs })
        } while (wantsFetchAgain() && !isCancelled())
      } catch (error) {
        if (isCancelled()) {
          return
        }
        if (isFinal(error)) {
          fail(error.code)
        } else {
          // The backend may be waking up; the last state stays on screen.
          retry(refresh)
        }
      } finally {
        fetching = false
      }
    }
    refreshRef.current = refresh

    const hub = createTeacherHub({
      dashboardChanged: () => void refresh(),
      reconnecting: () => {
        setLive(false)
      },
      reconnected: () => void watch(),
      closed: () => {
        if (!isCancelled()) {
          setLive(false)
          retry(connect)
        }
      },
    })

    const watch = async () => {
      try {
        const code = await hub.watch(sessionId)
        if (isCancelled()) {
          return
        }
        if (code !== null) {
          fail(code)
          return
        }
        setLive(true)
        await refresh()
      } catch {
        if (!isCancelled() && hub.isConnected()) {
          retry(watch)
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
      await watch()
    }

    void refresh()
    void connect()

    return () => {
      cancelled = true
      clearTimeout(retryTimer)
      controller.abort()
      void hub.stop()
    }
  }, [sessionId, retryDelayMs])

  const refresh = useCallback(() => refreshRef.current(), [])
  return { state, refresh }
}
