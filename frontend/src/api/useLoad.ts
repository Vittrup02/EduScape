import { useCallback, useEffect, useState } from 'react'
import { ApiError } from './client'

export type LoadState<T> =
  | { status: 'loading' }
  | { status: 'loaded'; data: T }
  | { status: 'failed'; code: string }

/**
 * Loads data when the component mounts and whenever `load` changes (wrap it in useCallback).
 * `reload` fetches again while the current data stays on screen. Errors become their backend code.
 */
export function useLoad<T>(load: (signal: AbortSignal) => Promise<T>): [LoadState<T>, () => void] {
  const [state, setState] = useState<LoadState<T>>({ status: 'loading' })
  const [version, setVersion] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    load(controller.signal).then(
      (data) => {
        if (!controller.signal.aborted) {
          setState({ status: 'loaded', data })
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setState({ status: 'failed', code: error instanceof ApiError ? error.code : 'UNKNOWN' })
        }
      },
    )
    return () => {
      controller.abort()
    }
  }, [load, version])

  const reload = useCallback(() => {
    setVersion((current) => current + 1)
  }, [])

  return [state, reload]
}
